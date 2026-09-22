using System.Collections.Generic;
using SimpleJSON;
using VPB.src.util;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SystemAtomProtectionTests
    {
        public SystemAtomProtectionTests(VamFixture vam) { }

        [Theory]
        [InlineData("[CameraRig]", "VRController", true)]
        [InlineData("CameraRig", "VRController", true)]
        [InlineData("WindowCamera", "WindowCamera", true)]
        [InlineData("PlayerNavigationPanel", "PlayerNavigationPanel", true)]
        [InlineData("CoreControl", "CoreControl", true)]
        [InlineData("CoreControl#2", "SessionPluginManager", true)]
        [InlineData("RenamedRig", "VRController", true)]
        [InlineData("DeskCam", "WindowCamera", true)]
        [InlineData("Nav", "PlayerNavigationPanel", true)]
        [InlineData("Cube", "Cube", false)]
        [InlineData("Light", "InvisibleLight", false)]
        [InlineData("Person", "Person", false)]
        public void SystemAtomsStayOutOfEraseAndImport(string id, string type, bool system)
        {
            Assert.Equal(system, SceneUtils.IsSystemProtectedSceneAtom(id, type));
        }

        [Fact]
        public void SceneImporterOmitsBuiltinAtomsThatAreAlwaysInTheScene()
        {
            JSONClass scene = JSON.Parse(
                "{\n" +
                "  \"atoms\" : [\n" +
                "    { \"id\" : \"[CameraRig]\", \"type\" : \"VRController\" },\n" +
                "    { \"id\" : \"WindowCamera\", \"type\" : \"WindowCamera\" },\n" +
                "    { \"id\" : \"PlayerNavigationPanel\", \"type\" : \"PlayerNavigationPanel\" },\n" +
                "    { \"id\" : \"CoreControl\", \"type\" : \"CoreControl\" },\n" +
                "    { \"id\" : \"Person\", \"type\" : \"Person\" },\n" +
                "    { \"id\" : \"Cube\", \"type\" : \"Cube\" }\n" +
                "  ]\n" +
                "}\n").AsObject;

            List<SceneAtomImporter.SceneAtomEntry> listed = SceneAtomImporter.EnumerateSceneAtoms(scene, null);

            SceneAtomImporter.SceneAtomEntry only = Assert.Single(listed);
            Assert.Equal("Cube", only.Id);
            Assert.Equal("Cube", only.Type);
        }
    }
}
