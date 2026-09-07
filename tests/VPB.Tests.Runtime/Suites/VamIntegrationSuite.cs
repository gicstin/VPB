using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "VamIntegration", Order = 20)]
    public static class VamIntegrationSuite
    {
        [VpbRuntimeTest]
        public static void SuperControllerIsAvailable()
        {
            RuntimeAssert.NotNull(SuperController.singleton,
                "SuperController.singleton is null after the settle wait. Every later test in this suite " +
                "depends on VaM being up, so raise the settle time rather than trusting the results.");
        }

        [VpbRuntimeTest]
        public static void UnprefixedCustomPathsAreQualifiedAgainstTheOwningPackage()
        {
            string ownerUid = FindAnyInstalledPackageUid();
            if (ownerUid == null)
                RuntimeAssert.Inconclusive(
                    "no VAR package is installed in this VaM, so there is no owning package to qualify " +
                    "an unprefixed Custom/ path against. Install any .var and re-arm.");

            JSONClass preset = JSON.Parse(
                "{ \"storables\" : [ { \"id\" : \"geometry\", \"clothing\" : [ " +
                "{ \"id\" : \"Custom/Clothing/Female/nothing/nothing.vam\" } ] } ] }").AsObject;

            RuntimeAssert.DoesNotThrow(
                () => VarPresetPathFixups.Apply(preset, ownerUid + ":/Custom/Atom/Person/Appearance/look.vap"),
                "VarPresetPathFixups.Apply threw on the unprefixed-Custom-path branch. This is the branch the " +
                "headless suite cannot reach, because it routes through VaM's package-aware path resolution.");

            string id = preset["storables"].AsArray[0].AsObject["clothing"].AsArray[0].AsObject["id"].Value;
            RuntimeAssert.False(string.IsNullOrEmpty(id),
                "The clothing reference was blanked out rather than left alone or qualified.");
        }

        [VpbRuntimeTest]
        public static void PackageAwarePathsResolveThroughTheFileManager()
        {
            string uid = FindAnyInstalledPackageUid();
            if (uid == null)
                RuntimeAssert.Inconclusive(
                    "no VAR package is installed in this VaM, so no package-aware path exists to resolve. " +
                    "Install any .var and re-arm.");

            string metaPath = uid + ":/meta.json";
            RuntimeAssert.True(MVR.FileManagement.FileManager.FileExists(metaPath),
                "FileManager.FileExists says " + metaPath + " is missing, but the package is registered. " +
                "VPB hooks this method; a hook that stops resolving package-internal paths makes the whole " +
                "gallery look empty.");
        }

        [VpbRuntimeTest]
        public static void ARealGameObjectAndRectTransformCanBeBuiltAndTornDown()
        {
            GameObject go = null;
            try
            {
                go = new GameObject("VpbRuntimeTestProbe", typeof(RectTransform));
                var rect = go.GetComponent<RectTransform>();

                RuntimeAssert.NotNull(rect, "RectTransform was not attached.");

                rect.sizeDelta = new Vector2(320f, 240f);
                RuntimeAssert.Approximately(320f, rect.sizeDelta.x, 0.001f, "sizeDelta.x did not stick.");
                RuntimeAssert.Approximately(240f, rect.sizeDelta.y, 0.001f, "sizeDelta.y did not stick.");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [VpbRuntimeTest]
        public static IEnumerator ADestroyedGameObjectIsGoneOnTheNextFrame()
        {
            var go = new GameObject("VpbRuntimeTestDestroyProbe");
            UnityEngine.Object.Destroy(go);

            yield return null;

            RuntimeAssert.True(go == null,
                "Unity's fake-null did not report the destroyed object as null on the next frame. VPB relies on " +
                "that check after every yield and thread hop before touching a VaM object.");
        }

        [VpbRuntimeTest]
        public static void TheEngineClockIsRunning()
        {
            RuntimeAssert.True(Time.realtimeSinceStartup > 0f,
                "Time.realtimeSinceStartup is not advancing. LogUtil.EngineRealtime falls back to 0 when the " +
                "engine is absent; seeing 0 here means the plugin is measuring nothing.");
        }

        [VpbRuntimeTest]
        public static void VpbConfigIsLoadedFromTheRealInstall()
        {
            VPBConfig cfg = VPBConfig.Instance;
            RuntimeAssert.NotNull(cfg, "VPBConfig.Instance is null in the running game.");

            string path = cfg.ConfigPathForDebug;
            RuntimeAssert.False(string.IsNullOrEmpty(path), "VPBConfig resolved an empty config path.");
            RuntimeAssert.True(path.IndexOf("PluginData", StringComparison.OrdinalIgnoreCase) >= 0,
                "VPBConfig is not resolving under Saves/PluginData - it is reading or writing somewhere else: " + path);
        }

        [VpbRuntimeTest]
        public static void TheGalleryIndexAgreesWithTheLivePackageInventory()
        {
            var groups = new List<string>();
            if (!VpbLocalDatabase.TryReadIndexedPackageGroups(groups))
                RuntimeAssert.Inconclusive(
                    "the gallery SQLite index could not be read, so there is nothing to compare the live " +
                    "package inventory against. Let the index finish building and re-arm.");
            if (groups.Count == 0)
                RuntimeAssert.Inconclusive(
                    "the gallery index holds no package groups - an empty library or an index that has not " +
                    "been built yet. Nothing to agree or disagree with.");

            int resolvable = 0;
            int checkedGroups = 0;
            for (int i = 0; i < groups.Count && checkedGroups < 50; i++)
            {
                string group = groups[i];
                if (string.IsNullOrEmpty(group)) continue;
                checkedGroups++;

                string uid;
                if (VpbLocalDatabase.TryResolveLatestUidFromIndex(group, out uid) && !string.IsNullOrEmpty(uid))
                    resolvable++;
            }

            RuntimeAssert.True(checkedGroups == 0 || resolvable > 0,
                "The index lists " + groups.Count + " package groups but none of the first " + checkedGroups +
                " resolves to a uid. The index and the live inventory have diverged, which shows up as a gallery " +
                "full of rows that do nothing when clicked.");
        }

        private static string FindAnyInstalledPackageUid()
        {
            try
            {
                Dictionary<string, VarPackage> packages = FileManager.PackagesByUid;
                if (packages == null) return null;
                foreach (KeyValuePair<string, VarPackage> kv in packages)
                {
                    if (!string.IsNullOrEmpty(kv.Key)) return kv.Key;
                }
            }
            catch { }
            return null;
        }
    }
}
