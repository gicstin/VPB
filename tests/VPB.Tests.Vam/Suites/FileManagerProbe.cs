using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class FileManagerProbe
    {
        private readonly ITestOutputHelper _out;
        public FileManagerProbe(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private void Probe(string label, Action action)
        {
            try
            {
                action();
                _out.WriteLine("OK    " + label);
            }
            catch (Exception ex)
            {
                string detail = HeadlessVam.DescribeUnwrapped(ex);
                _out.WriteLine((HeadlessVam.IsEcall(ex) ? "ECALL " : "THROW ") + label + " :: " + detail);
            }
        }

        [Fact]
        public void SurfaceReport()
        {
            using (var install = new TempInstall("fm_probe"))
            {
                Probe("PackageIDToPackageGroupID", () => FileManager.PackageIDToPackageGroupID("Creator.Pack.3"));
                Probe("PackageIDToPackageVersion", () => FileManager.PackageIDToPackageVersion("Creator.Pack.3"));
                Probe("NormalizeID", () => FileManager.NormalizeID("Creator.Pack.3"));
                Probe("CanonicalizeUidSegments", () => FileManager.CanonicalizeUidSegments("Some Creator.Pack Name.3"));
                Probe("TryParseVarVersionSegment", () => { int v; FileManager.TryParseVarVersionSegment("3", out v); });
                Probe("TryGetCanonicalUidFromVarPath", () => { string u; FileManager.TryGetCanonicalUidFromVarPath("AddonPackages/Creator.Pack.3.var", out u); });
                Probe("IsPackagePath", () => FileManager.IsPackagePath("Creator.Pack.3:/Saves/scene/x.json"));
                Probe("IsSimulatedPackagePath", () => FileManager.IsSimulatedPackagePath("AddonPackages/Creator.Pack.3.var:/x"));
                Probe("RemovePackageFromPath", () => FileManager.RemovePackageFromPath("Creator.Pack.3:/Saves/scene/x.json"));
                Probe("ConvertSimulatedPackagePathToNormalPath", () => FileManager.ConvertSimulatedPackagePathToNormalPath("AddonPackages/Creator.Pack.3.var:/x"));
                Probe("CleanFilePath", () => FileManager.CleanFilePath("Saves\\scene\\x.json"));
                Probe("CleanDirectoryPath", () => FileManager.CleanDirectoryPath("Saves\\scene\\"));
                Probe("GetDirectoryName", () => FileManager.GetDirectoryName("Saves/scene/x.json"));
                Probe("NormalizePath(relative)", () => FileManager.NormalizePath("Saves/scene/x.json"));
                Probe("NormalizePath(package)", () => FileManager.NormalizePath("Creator.Pack.3:/Saves/scene/x.json"));
                Probe("NormalizeLoadPath", () => FileManager.NormalizeLoadPath("Saves/scene/x.json"));
                Probe("NormalizeSavePath", () => FileManager.NormalizeSavePath("Saves/scene/x.json"));
                Probe("GetFullPath", () => FileManager.GetFullPath("Saves/scene/x.json"));

                Probe("GetPackageCount", () => FileManager.GetPackageCount());
                Probe("IsPackage", () => FileManager.IsPackage("Creator.Pack.3"));
                Probe("GetExactRegisteredPackage", () => FileManager.GetExactRegisteredPackage("Creator.Pack.3"));
                Probe("GetPackageGroup", () => FileManager.GetPackageGroup("Creator.Pack"));
                Probe("GetPackage", () => FileManager.GetPackage("Creator.Pack.3", false));
                Probe("ResolveDependency", () => FileManager.ResolveDependency("Creator.Pack.3"));
                Probe("GetPackageForDependency", () => FileManager.GetPackageForDependency("Creator.Pack.3", false));
                Probe("IsDependencySatisfiedByInstalled", () => FileManager.IsDependencySatisfiedByInstalled("Creator.Pack.3"));
                Probe("ShouldForceLatestForPackageGroup", () => FileManager.ShouldForceLatestForPackageGroup("Creator.Pack"));
                Probe("TryResolveWhitespaceAliasUid", () => { string a; FileManager.TryResolveWhitespaceAliasUid("SomeCreator.PackName.3", out a); });
                Probe("TryMapLookupUidToRegisteredUid", () => { string a; FileManager.TryMapLookupUidToRegisteredUid("SomeCreator.PackName.3", out a); });
                Probe("TryMapLookupGroupIdToRegisteredGroupId", () => { string a; FileManager.TryMapLookupGroupIdToRegisteredGroupId("SomeCreator.PackName", out a); });

                Probe("SafeGetFiles", () => { var r = new List<string>(); FileManager.SafeGetFiles(install.Root, "*.*", r); });
                Probe("SafeGetDirectories", () => { var r = new List<string>(); FileManager.SafeGetDirectories(install.Root, "*", r); });
                Probe("SafeGetImmediateFilesInDirectory", () => { var r = new List<string>(); FileManager.SafeGetImmediateFilesInDirectory(install.Root, "*.*", r); });
                Probe("SafeGetImmediateSubdirectories", () => { var r = new List<string>(); FileManager.SafeGetImmediateSubdirectories(install.Root, r); });
                Probe("TryGetDirectoryLastWriteBinaryFollowingLinks", () => { long b; bool rp; FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(install.Root, out b, out rp); });
                Probe("CheckIfDirectoryChanged", () => FileManager.CheckIfDirectoryChanged(install.Root, DateTime.MinValue, false));
                Probe("FolderContentsCount", () => FileManager.FolderContentsCount(install.Root));
                Probe("DirectoryExists", () => FileManager.DirectoryExists("Saves"));
                Probe("FileExists", () => FileManager.FileExists("Saves/scene/nope.json"));
                Probe("IsFileInPackage", () => FileManager.IsFileInPackage("Creator.Pack.3:/x.json"));
                Probe("IsSecureReadPath", () => FileManager.IsSecureReadPath("Saves/scene/x.json"));
                Probe("IsSecureWritePath", () => FileManager.IsSecureWritePath("Saves/scene/x.json"));
                Probe("GetFileEntry", () => FileManager.GetFileEntry("Saves/scene/nope.json"));
                Probe("GetSystemFileEntry", () => FileManager.GetSystemFileEntry("Saves/scene/nope.json"));

                Probe("SetLoadDir", () => FileManager.SetLoadDir("Saves/scene"));
                Probe("CurrentLoadDir", () => { string s = FileManager.CurrentLoadDir; });
                Probe("PushLoadDir/PopLoadDir", () => { FileManager.PushLoadDir("Saves"); FileManager.PopLoadDir(); });
                Probe("CurrentPackageUid", () => { string s = FileManager.CurrentPackageUid; });
                Probe("lastPackageRefreshTime", () => { DateTime d = FileManager.lastPackageRefreshTime; });
                Probe("IsScanning", () => { bool b = FileManager.IsScanning; });
                Probe("IsBulkDeepScanActive", () => { bool b = FileManager.IsBulkDeepScanActive; });
                string cfg = System.IO.Path.Combine(install.Root, "probe.cfg");
                var file = new BepInEx.Configuration.ConfigFile(cfg, true);
                Probe("Bind<string>", () => file.Bind<string>("S", "str", "x", "d"));
                Probe("Bind<bool>", () => file.Bind<bool>("S", "b", true, "d"));
                Probe("Bind<int>", () => file.Bind<int>("S", "i", 1, "d"));
                Probe("Bind<float>", () => file.Bind<float>("S", "f", 1f, "d"));
                Probe("Bind<Vector2>", () => file.Bind<UnityEngine.Vector2>("S", "v", UnityEngine.Vector2.zero, "d"));
                Probe("Settings.Init", () => Settings.Init(new BepInEx.Configuration.ConfigFile(cfg + "2", true)));

                Probe("TryGetMorphOwner", () => { string o; FileManager.TryGetMorphOwner("Custom/Atom/Person/Morphs/female/x.vmi", out o); });
            }
        }
    }
}
