using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class VarPackageTests
    {
        private readonly ITestOutputHelper _out;
        public VarPackageTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static VarFixture Library(string creator, string name, int version)
        {
            return new VarFixture(creator, name, version);
        }

        [Fact]
        public void MetaJsonFieldsAreReadBackFromTheArchive()
        {
            using (var install = new TempInstall("var_meta"))
            {
                VarFixture fixture = Library("Creator", "Pack", 3)
                    .WithText("Saves/scene/demo.json", VarFixture.SceneJson("Demo"))
                    .WithPlaceholderJpg("Saves/scene/demo.jpg")
                    .WithMeta(
                        licenseType: "PC EA",
                        description: "A fixture package.",
                        promotionalLink: "https://example.invalid/pack",
                        tags: new[] { "dress", "formal", "long" },
                        clothingTags: new[] { "dress", "formal" },
                        hairTags: new[] { "long" });

                fixture.WriteTo(install.AddonPackagesDir);

                VarPackage package = fixture.AsPackage();
                bool loaded = package.TryEnsureMetaJsonLiteFields();

                _out.WriteLine("loaded=" + loaded + " license=" + package.LicenseType);
                Assert.True(loaded, "TryEnsureMetaJsonLiteFields() found nothing in the fixture archive.");
                Assert.Equal("PC EA", package.LicenseType);
                Assert.Equal("A fixture package.", package.Description);
                Assert.Equal("https://example.invalid/pack", package.PromotionalLink);
                Assert.NotNull(package.PackageMetaTags);
                Assert.Contains("dress", package.PackageMetaTags);
                Assert.Contains("formal", package.PackageMetaTags);
                Assert.Contains("long", package.PackageMetaTags);
            }
        }

        [Fact]
        public void MissingMetaJsonIsHandledWithoutThrowing()
        {
            using (var install = new TempInstall("var_nometa"))
            {
                VarFixture fixture = Library("Creator", "NoMeta", 1)
                    .WithText("Saves/scene/demo.json", VarFixture.SceneJson("Demo"));
                fixture.WriteTo(install.AddonPackagesDir);

                VarPackage package = fixture.AsPackage();
                Assert.False(package.TryEnsureMetaJsonLiteFields());
                Assert.True(string.IsNullOrEmpty(package.LicenseType));
            }
        }

        [Fact]
        public void CorruptArchiveIsHandledWithoutThrowing()
        {
            using (var install = new TempInstall("var_corrupt"))
            {
                string path = Path.Combine(install.AddonPackagesDir, "Creator.Broken.1.var");
                File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 });

                VarPackage package = new VarFixture("Creator", "Broken", 1).AsPackage();
                Exception thrown = Record.Exception(() => package.TryEnsureMetaJsonLiteFields());

                Assert.True(thrown == null,
                    "A truncated .var must not throw out of the package layer - in the game one bad file in " +
                    "AddonPackages would take down the whole scan. Got: " +
                    (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
            }
        }

        [Fact]
        public void MissingFileOnDiskIsHandledWithoutThrowing()
        {
            using (var install = new TempInstall("var_missing"))
            {
                VarPackage package = new VarFixture("Creator", "Absent", 1).AsPackage();
                Assert.False(package.TryEnsureMetaJsonLiteFields());
            }
        }

        [Fact]
        public void SpacedAndUnicodePackageNamesOpenCorrectly()
        {
            using (var install = new TempInstall("var_spaced"))
            {
                VarFixture fixture = Library("Some Creator", "Pack Name é", 2)
                    .WithText("Saves/scene/a scene.json", VarFixture.SceneJson("A Scene"))
                    .WithMeta(licenseType: "CC BY-SA");

                string written = fixture.WriteTo(install.AddonPackagesDir);
                _out.WriteLine("wrote " + Path.GetFileName(written));

                VarPackage package = fixture.AsPackage();
                Assert.True(package.TryEnsureMetaJsonLiteFields(),
                    "A .var whose file name contains spaces or non-ASCII did not open.");
                Assert.Equal("CC BY-SA", package.LicenseType);
            }
        }

        [Fact]
        public void DeclaredDependenciesAreExtractedFromMetaJson()
        {
            using (var install = new TempInstall("var_deps"))
            {
                VarFixture fixture = Library("Creator", "Consumer", 1)
                    .WithText("Saves/scene/demo.json", VarFixture.SceneJson("Demo", new[] { "Other.Assets.4" }))
                    .WithMeta(dependencies: new[] { "Other.Assets.4", "Third.Party.latest" });

                fixture.WriteTo(install.AddonPackagesDir);

                string metaText = ReadEntry(Path.Combine(install.AddonPackagesDir, fixture.FileName), "meta.json");
                HashSet<string> found = DependencyExtractor.ExtractDependenciesFromJson(metaText);

                _out.WriteLine("extracted: " + string.Join(", ", found.ToArray()));
                Assert.Contains("Other.Assets.4", found);
                Assert.Contains("Third.Party.latest", found);
            }
        }

        [Fact]
        public void SceneReferencesAreExtractedFromEmbeddedJson()
        {
            using (var install = new TempInstall("var_scene_deps"))
            {
                VarFixture fixture = Library("Creator", "Scene", 1)
                    .WithText("Saves/scene/demo.json", VarFixture.SceneJson("Demo", new[] { "Other.Assets.4", "Hair.Pack.min2" }))
                    .WithMeta();

                fixture.WriteTo(install.AddonPackagesDir);

                string sceneText = ReadEntry(Path.Combine(install.AddonPackagesDir, fixture.FileName), "Saves/scene/demo.json");
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DependencyExtractor.ScanAllStringsForDependencies(JSON.Parse(sceneText), found);

                Assert.Contains("Other.Assets.4", found);
                Assert.Contains("Hair.Pack.min2", found);
            }
        }

        [Fact]
        public void FileExtensionsAreNotMistakenForDependencies()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            JSONNode node = JSON.Parse("{\"a\":\"MyPlugin.Something.cs\",\"b\":\"lib.helper.dll\",\"c\":\"Creator.Pack.3\"}");
            DependencyExtractor.ScanAllStringsForDependencies(node, found);

            Assert.Contains("Creator.Pack.3", found);
            Assert.DoesNotContain("MyPlugin.Something.cs", found);
            Assert.DoesNotContain("lib.helper.dll", found);
        }

        private static string ReadEntry(string varPath, string internalPath)
        {
            using (FileStream fs = File.OpenRead(varPath))
            using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(fs))
            {
                ICSharpCode.SharpZipLib.Zip.ZipEntry entry = zip.GetEntry(internalPath);
                Assert.True(entry != null, "fixture archive has no entry " + internalPath);
                using (Stream s = zip.GetInputStream(entry))
                using (var reader = new StreamReader(s))
                    return reader.ReadToEnd();
            }
        }
    }
}
