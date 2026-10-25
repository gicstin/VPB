using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SimpleJSON;
using VPB.src.util;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class AppearanceImportTests
    {
        public AppearanceImportTests(VamFixture vam) { }

        private static JSONClass Appearance()
        {
            return JSON.Parse(@"{""storables"":[
                {""id"":""geometry"",""character"":""Female 1"",
                 ""morphs"":[{""uid"":""Author.Body.1:/Custom/Atom/Person/Morphs/female/body.vmi"",""value"":0.7}],
                 ""clothing"":[{""id"":""Author.Dress.1:/Custom/Clothing/Female/Dress/dress.vam"",""internalId"":""dress"",""enabled"":true}],
                 ""hair"":[{""id"":""Author.Hair.1:/Custom/Hair/Female/Hair/hair.vam"",""enabled"":true}]},
                {""id"":""Skin"",""customTexture_MainTex"":""SELF:/Custom/Textures/face.png"",""gloss"":0.3},
                {""id"":""hairTool:hair"",""curl"":0.4},
                {""id"":""dress:sim"",""simEnabled"":true},
                {""id"":""dress:skin"",""customTexture_MainTex"":""SELF:/Custom/Textures/dress.png""}
            ]}").AsObject;
        }


        [Theory]
        [InlineData("{\"storables\":[]}", (int)VpbImportSourceKind.Appearance)]
        [InlineData("{\"atoms\":[]}", (int)VpbImportSourceKind.Scene)]
        public void ImportKindComesFromContentRatherThanTheFileLocation(string json, int expected)
        {
            Assert.Equal((VpbImportSourceKind)expected, VpbImportSource.Kind(JSON.Parse(json).AsObject));
            Assert.Throws<InvalidDataException>(() => VpbImportSource.Kind(JSON.Parse("{}").AsObject));
        }

        [Fact]
        public void LocalVapOutsideAppearanceFoldersKeepsEverySavedComponent()
        {
            using (var install = new TempInstall("local-vap-import"))
            {
                string path = Path.GetFullPath("outside.vap");
                JSONClass expected = Appearance();
                JSONArray storables = expected["storables"].AsArray;
                storables.Add(JSON.Parse("{\"id\":\"FemaleBreastPhysics\",\"spring\":1}"));
                storables.Add(JSON.Parse("{\"id\":\"FemaleGlutePhysics\",\"spring\":1}"));
                storables.Add(JSON.Parse("{\"id\":\"PluginManager\",\"plugins\":{\"plugin#0\":\"./plugin.cs\"}}"));
                storables.Add(JSON.Parse("{\"id\":\"hipControl\",\"position\":{\"x\":1}}"));
                storables.Add(JSON.Parse("{\"id\":\"Preset\",\"includeAppearance\":true}"));
                File.WriteAllText(path, JsonSerializationUtil.Serialize(expected, 8192));
                VpbImportReadRequest request = LocalRequest(path);
                JSONClass root = request.Read();
                Assert.Equal(VpbImportSourceKind.Appearance, VpbImportSource.Kind(root));
                Assert.Equal(JsonSerializationUtil.Serialize(expected, 8192), JsonSerializationUtil.Serialize(root, 8192));
                foreach (VpbResourceType type in new[] { VpbResourceType.Appearance, VpbResourceType.Morphs,
                    VpbResourceType.Skin, VpbResourceType.Clothing, VpbResourceType.Hair, VpbResourceType.BreastPhysics,
                    VpbResourceType.Glute, VpbResourceType.Plugins, VpbResourceType.Pose, VpbResourceType.General })
                {
                    Assert.True(VpbImportSource.Count(root, type) > 0);
                    Assert.NotNull(VpbImportSource.Slice(root, type));
                }
            }
        }

        [Fact]
        public void VacKeepsBundledFilesTogetherAndResolvesPathsForEveryAtom()
        {
            using (var install = new TempInstall("vac-import"))
            {
                string source = WriteVac(new VarFixture("Test", "Vac", 1)
                    .WithText("different-name.json", "{\"atoms\":[{\"id\":\"Person\",\"type\":\"Person\",\"storables\":[{\"id\":\"Skin\",\"texture\":\"./textures/face.png\"}]},{\"id\":\"Prop\",\"storables\":[{\"id\":\"asset\",\"assetUrl\":\"prop.assetbundle\",\"displayName\":\"prop.assetbundle\"}]}]}")
                    .WithText("textures/face.png", "texture")
                    .WithText("prop.assetbundle", "asset")
                    .WithText("Custom/Atom/Person/Morphs/female/body.vmi", "{}")
                    .WithText("Embedded.Other.1.var", "package")
                    .WithMeta());
                byte[] before = File.ReadAllBytes(source);
                VpbImportReadRequest request = LocalRequest(source);
                JSONClass root = request.Read();
                Assert.Equal(VpbImportSourceKind.Scene, VpbImportSource.Kind(root));
                string folder = Path.GetDirectoryName(request.ResolvedPath);
                Assert.Equal("texture", File.ReadAllText(Path.Combine(folder, "textures/face.png")));
                Assert.True(File.Exists(Path.Combine(folder, "Custom/Atom/Person/Morphs/female/body.vmi")));
                Assert.Equal(Path.Combine(folder, "textures/face.png").Replace('\\', '/'), root["atoms"][0]["storables"][0]["texture"].Value);
                Assert.Equal(Path.Combine(folder, "prop.assetbundle").Replace('\\', '/'), root["atoms"][1]["storables"][0]["assetUrl"].Value);
                Assert.Equal("prop.assetbundle", root["atoms"][1]["storables"][0]["displayName"].Value);
                Assert.Single(request.EmbeddedPackages);
                Assert.False(File.Exists("AddonPackages/Embedded.Other.1.var"));
                Assert.Equal(before, File.ReadAllBytes(source));
                Assert.Equal(request.ResolvedPath, ReadAgain(source).ResolvedPath);
            }
        }

        [Fact]
        public void VacCanContainAPresetAndDoesNotSelectMetadata()
        {
            using (var install = new TempInstall("vac-preset"))
            {
                string source = WriteVac(new VarFixture("Test", "Vac", 1)
                    .WithText("look.vap", JsonSerializationUtil.Serialize(Appearance(), 8192)).WithMeta());
                JSONClass root = LocalRequest(source).Read();
                Assert.Equal(VpbImportSourceKind.Appearance, VpbImportSource.Kind(root));
                Assert.NotNull(VpbImportSource.Slice(root, VpbResourceType.Clothing));
                Assert.NotNull(VpbImportSource.Slice(root, VpbResourceType.Hair));
            }
        }

        [Theory]
        [InlineData("../escape.txt")]
        [InlineData("a/../../escape.txt")]
        [InlineData("C:/escape.txt")]
        [InlineData("/escape.txt")]
        [InlineData("a/file:stream")]
        [InlineData("a/NUL.txt")]
        [InlineData("a./file")]
        public void VacRejectsUnsafePathsBeforeExtractingAnything(string name)
        {
            using (var install = new TempInstall("vac-unsafe"))
            {
                string source = WriteVac(new VarFixture("Test", "Vac", 1)
                    .WithText("scene.json", "{\"atoms\":[]}")
                    .WithText(name, "unsafe"));
                Assert.Throws<InvalidDataException>(() => LocalRequest(source).Read());
                Assert.False(File.Exists("escape.txt"));
                Assert.Empty(Directory.GetFiles("Cache", "*", SearchOption.AllDirectories));
            }
        }

        [Theory]
        [InlineData("second.json")]
        [InlineData("SCENE.JSON")]
        public void VacRejectsAmbiguousDocumentsAndDuplicatePaths(string other)
        {
            using (var install = new TempInstall("vac-ambiguous"))
            {
                string source = WriteVac(new VarFixture("Test", "Vac", 1)
                    .WithText("scene.json", "{\"atoms\":[]}")
                    .WithText(other, "{\"atoms\":[]}"));
                Assert.Throws<InvalidDataException>(() => LocalRequest(source).Read());
            }
        }

        [Fact]
        public void VacCacheDamageCannotBeAppliedSilently()
        {
            using (var install = new TempInstall("vac-cache"))
            {
                string source = WriteVac(new VarFixture("Test", "Vac", 1).WithText("scene.json", "{\"atoms\":[]}"));
                VpbImportReadRequest request = ReadAgain(source);
                File.Delete(request.ResolvedPath);
                Assert.False(request.IsCurrent());
                Assert.Throws<InvalidDataException>(() => LocalRequest(source).Read());
            }
        }

        private static VpbImportReadRequest ReadAgain(string path)
        {
            VpbImportReadRequest request = LocalRequest(path);
            request.Read();
            return request;
        }

        private static VpbImportReadRequest LocalRequest(string path)
        {
            var file = new FileInfo(path);
            return new VpbImportReadRequest { Path = file.FullName, Size = file.Length,
                WriteTicks = file.LastWriteTimeUtc.Ticks, CacheRoot = Path.GetFullPath("Cache") };
        }

        private static string WriteVac(VarFixture fixture)
        {
            string original = fixture.WriteTo(Path.GetFullPath("Sources"));
            string path = Path.ChangeExtension(original, ".vac");
            File.Move(original, path);
            return path;
        }

        private static JSONClass Storable(JSONClass preset, string id)
        {
            foreach (JSONNode node in preset["storables"].AsArray)
                if (node["id"].Value == id) return node.AsObject;
            return null;
        }

        [Fact]
        public void AnAppearanceIsUsableWithoutASceneOrPersonId()
        {
            JSONClass source = Appearance();
            Assert.Same(source, VpbImportSource.Preset(source, VpbImportSourceKind.Appearance, null));
            Assert.Null(VpbImportSource.Preset(source, VpbImportSourceKind.Scene, "Person"));
            Assert.Null(VpbImportSource.Preset(JSON.Parse("{\"storables\":{}}").AsObject, VpbImportSourceKind.Appearance, null));
        }

        [Fact]
        public void ASceneSelectionCannotFallBackToAnotherPerson()
        {
            JSONClass scene = JSON.Parse("{\"atoms\":[{\"id\":\"A\",\"type\":\"Person\",\"storables\":[]},{\"id\":\"B\",\"type\":\"Person\",\"storables\":[]}]}").AsObject;
            Assert.NotNull(VpbImportSource.Preset(scene, VpbImportSourceKind.Scene, "B"));
            Assert.Null(VpbImportSource.Preset(scene, VpbImportSourceKind.Scene, "Missing"));
            Assert.Null(VpbImportSource.Preset(scene, VpbImportSourceKind.Scene, null));
        }

        [Theory]
        [InlineData((int)VpbResourceType.Morphs)]
        [InlineData((int)VpbResourceType.Skin)]
        [InlineData((int)VpbResourceType.Clothing)]
        [InlineData((int)VpbResourceType.Hair)]
        public void ComponentPreparationDoesNotMutateTheCachedAppearance(int typeValue)
        {
            VpbResourceType type = (VpbResourceType)typeValue;
            JSONClass source = Appearance();
            string before = JsonSerializationUtil.Serialize(source, 8192);
            Assert.True(VpbImportSource.Count(source, type) > 0, "Saved components must be available without separate preset files.");
            JSONClass slice = VpbImportSource.Slice(source, type);
            Assert.NotNull(slice);
            JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(slice, "Author.Look.1");
            slice["storables"].AsArray[0]["changed"] = "test";
            Assert.Equal(before, JsonSerializationUtil.Serialize(source, 8192));
        }

        [Fact]
        public void SkinExcludesClothingTexturesMorphsAndHair()
        {
            JSONClass slice = VpbImportSource.Slice(Appearance(), VpbResourceType.Skin);
            Assert.Equal(2, slice["storables"].AsArray.Count);
            JSONClass geometry = Storable(slice, "geometry");
            Assert.NotNull(geometry);
            Assert.Equal("Female 1", geometry["character"].Value);
            Assert.Equal(2, geometry.Count);
            JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(slice, "Author.Look.1");
            Assert.Equal("Author.Look.1:/Custom/Textures/face.png", Storable(slice, "Skin")["customTexture_MainTex"].Value);
        }

        [Theory]
        [InlineData("Female 1")]
        [InlineData("")]
        public void BuiltInSkinSelectionIsAvailableWithoutCustomTextures(string character)
        {
            JSONClass source = JSON.Parse("{\"storables\":[{\"id\":\"geometry\"}]}").AsObject;
            source["storables"][0]["character"] = character;
            Assert.Equal(1, VpbImportSource.Count(source, VpbResourceType.Skin));
            JSONClass slice = VpbImportSource.Slice(source, VpbResourceType.Skin);
            Assert.NotNull(slice);
            Assert.Equal(character, Storable(slice, "geometry")["character"].Value);
        }

        [Fact]
        public void SkinWithoutCharacterDoesNotInventAGeometrySelection()
        {
            JSONClass source = Appearance();
            Storable(source, "geometry").Remove("character");
            JSONClass slice = VpbImportSource.Slice(source, VpbResourceType.Skin);
            Assert.NotNull(Storable(slice, "Skin"));
            Assert.Null(Storable(slice, "geometry"));
        }

        [Fact]
        public void OutfitSkinSelectionRetainsItsCharacterWithoutMutatingTheSource()
        {
            JSONClass source = Appearance();
            var selected = new HashSet<string> { VpbImport.MergeOutfitSkinUid };
            JSONClass slice = VpbImport.BuildOutfitComponentSlice(source, VpbResourceType.Skin, selected);
            JSONClass geometry = Storable(slice, "geometry");
            Assert.NotNull(geometry);
            Assert.Equal("Female 1", geometry["character"].Value);
            geometry["character"] = "Female 2";
            Assert.Equal("Female 1", Storable(source, "geometry")["character"].Value);
        }

        [Fact]
        public void MorphOptionsExistEvenWhenTheAppearanceSavedNoManagerShell()
        {
            JSONClass slice = VpbImportSource.Slice(Appearance(), VpbResourceType.Morphs);
            SubToggleOptions options = SubToggleOptions.AllOn();
            options.IncludeAppearanceMorphs = false;
            VpbImportSubToggleFilter.FilterForType(slice, VpbResourceType.Morphs, options);
            Assert.False(Storable(slice, "MorphPresets")["includeAppearance"].AsBool);
            Assert.True(Storable(slice, "MorphPresets")["includePhysical"].AsBool);
            Assert.Null(Storable(slice, "geometry")["clothing"] as JSONArray);
            Assert.Null(Storable(slice, "Skin"));
        }

        [Fact]
        public void ClothingAndHairRetainTheirSavedItemSettings()
        {
            JSONClass source = Appearance();
            JSONClass clothing = VpbImportSource.Slice(source, VpbResourceType.Clothing);
            JSONClass hair = VpbImportSource.Slice(source, VpbResourceType.Hair);
            Assert.NotNull(Storable(clothing, "dress:sim"));
            Assert.NotNull(Storable(hair, "hairTool:hair"));
            Assert.Null(Storable(clothing, "Skin"));
            Assert.Null(Storable(hair, "geometry")["clothing"] as JSONArray);
        }

        [Fact]
        public void AbsentSceneDataAndDisabledItemsAreUnavailable()
        {
            JSONClass source = Appearance();
            Storable(source, "geometry")["clothing"][0]["enabled"] = "false";
            Assert.Equal(0, VpbImportSource.Count(source, VpbResourceType.Clothing));
            Assert.Equal(0, VpbImportSource.Count(source, VpbResourceType.CUA));
            Assert.Equal(0, VpbImportSource.Count(source, VpbResourceType.Atoms));
            Assert.Equal(0, VpbImportSource.Count(source, VpbResourceType.Plugins));
            Assert.Equal(0, VpbImportSource.Count(JSON.Parse("{\"storables\":[]}").AsObject, VpbResourceType.Appearance));
        }

        [Fact]
        public void AppearanceAndComponentSelectionsCannotDoubleApplyTheLook()
        {
            var selection = new HashSet<VpbResourceType> { VpbResourceType.Skin, VpbResourceType.Hair };
            VpbImportSource.Select(selection, VpbResourceType.Appearance);
            Assert.Equal(new[] { VpbResourceType.Appearance }, selection);
            VpbImportSource.Select(selection, VpbResourceType.Morphs);
            Assert.Equal(new[] { VpbResourceType.Morphs }, selection);
        }

        [Fact]
        public void ChangedOrDeletedSourcesCannotBeAppliedFromCachedBytes()
        {
            using (var install = new TempInstall("appearance-source"))
            {
                string path = Path.GetFullPath("look.vap");
                File.WriteAllText(path, "{\"storables\":[]}");
                var stat = new FileInfo(path);
                var request = new VpbImportReadRequest { Path = path, Size = stat.Length, WriteTicks = stat.LastWriteTimeUtc.Ticks };
                Assert.NotNull(request.Read());
                File.WriteAllText(path, "{\"storables\":[],\"changed\":true}");
                Assert.False(request.IsCurrent());
                Assert.Throws<IOException>(() => request.Read());
                File.Delete(path);
                Assert.False(request.IsCurrent());
            }
        }

        [Theory]
        [InlineData("{\"storables\":[]")]
        [InlineData("{\"storables\":[}")]
        [InlineData("[]")]
        [InlineData("{} {}")]
        public void MalformedSourcesCannotPublishPartialPresetData(string text)
        {
            Assert.ThrowsAny<Exception>(() => VpbImportReadRequest.ReadJson(new StringReader(text)));
        }

        [Fact]
        public void TheStreamingReaderPreservesPresetValues()
        {
            string raw = "{\"storables\":[{\"id\":\"Skin\",\"gloss\":0.375,\"enabled\":true,\"texture\":\"SELF:/Custom/Textures/face.png\"}]}";
            JSONClass root = VpbImportReadRequest.ReadJson(new StringReader(raw));
            Assert.Equal(JsonSerializationUtil.Serialize(JSON.Parse(raw), 8192), JsonSerializationUtil.Serialize(root, 8192));
        }

        [Fact]
        public void AnAppearanceOnlyPackageNeedsNoSceneEntry()
        {
            using (var install = new TempInstall("appearance-package"))
            {
                string internalPath = "Custom/Atom/Person/Appearance/Preset_Look.vap";
                string archive = new VarFixture("Test", "Look", 1)
                    .WithText(internalPath, JsonSerializationUtil.Serialize(Appearance(), 8192))
                    .WithMeta().WriteTo(Path.GetFullPath("AddonPackages"));
                FileInfo stat = new FileInfo(archive);
                var request = new VpbImportReadRequest { Path = archive, InternalPath = internalPath,
                    Size = stat.Length, WriteTicks = stat.LastWriteTimeUtc.Ticks, CodePage = int.MinValue };
                JSONClass source = VpbImportSource.Preset(request.Read(), VpbImportSourceKind.Appearance, null);
                Assert.True(VpbImportSource.Count(source, VpbResourceType.Skin) > 0);
                request.InternalPath = "missing.vap";
                Assert.Throws<IOException>(() => request.Read());
            }
        }

        [Fact]
        public void LocalTexturePathsStayRelativeToTheSourceAppearance()
        {
            JSONClass preset = JSON.Parse("{\"storables\":[{\"id\":\"Skin\",\"customTexture_MainTex\":\"./face.png\"}]}").AsObject;
            VarPresetPathFixups.Apply(preset, "Custom/Atom/Person/Appearance/Preset_Look.vap");
            Assert.EndsWith("Custom/Atom/Person/Appearance/face.png", preset["storables"][0]["customTexture_MainTex"].Value.Replace('\\', '/'));
        }

        [Fact]
        public void OutfitSelectionExcludesUnselectedDependenciesAndSourceBody()
        {
            JSONClass source = Appearance();
            var selected = new HashSet<string> { Storable(source, "geometry")["hair"][0]["id"].Value };
            Assert.Null(VpbImport.BuildOutfitComponentSlice(source, VpbResourceType.Clothing, selected));
            Assert.Null(VpbImport.BuildOutfitComponentSlice(source, VpbResourceType.Skin, selected));
            JSONClass hair = VpbImport.BuildOutfitComponentSlice(source, VpbResourceType.Hair, selected);
            Assert.NotNull(hair);
            Assert.Null(Storable(hair, "geometry")["morphs"] as JSONArray);
            Assert.False(hair["setUnlistedParamsToDefault"].AsBool);
        }

        [Fact]
        public void RapidSelectionReadsOnlyTheRunningAndLatestRequests()
        {
            using (var started = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var latestRead = new ManualResetEvent(false))
            {
                var readIds = new List<int>();
                var queue = new VpbImportReadQueue(request => {
                    lock (readIds) readIds.Add(request.Generation);
                    if (request.Generation == 1) { started.Set(); if (!release.WaitOne(5000)) throw new TimeoutException(); }
                    if (request.Generation == 3) latestRead.Set();
                    return new JSONClass();
                });
                try
                {
                    queue.Submit(new VpbImportReadRequest { Generation = 1 });
                    Assert.True(started.WaitOne(5000));
                    queue.Submit(new VpbImportReadRequest { Generation = 2 });
                    queue.Submit(new VpbImportReadRequest { Generation = 3 });
                    release.Set();
                    Assert.True(latestRead.WaitOne(5000));
                    VpbImportReadQueue.Result result = null;
                    for (int i = 0; i < 500 && result == null; i++) { result = queue.Take(); if (result == null) Thread.Sleep(10); }
                    Assert.NotNull(result);
                    Assert.Equal(3, result.Request.Generation);
                    lock (readIds) Assert.Equal(new[] { 1, 3 }, readIds);
                    queue.Cancel();
                    Assert.Null(queue.Take());
                }
                finally { release.Set(); queue.Cancel(); }
            }
        }
    }
}
