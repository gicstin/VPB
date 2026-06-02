using System;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    class MVRPluginManagerHook
    {
        // Runs before the plugin compiles/instantiates; mvrp.pluginURLJSON.val gives the parent
        // package, so we register declared deps and ingest morphs before the plugin resolves by name.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVRPluginManager), "SyncPluginUrlInternal")]
        public static void PreSyncPluginUrl(object mvrp)
        {
            try
            {
                MVRPlugin plugin = mvrp as MVRPlugin;
                if (plugin == null || plugin.pluginURLJSON == null) return;
                string url = plugin.pluginURLJSON.val;
                if (string.IsNullOrEmpty(url)) return;

                string parentUid = VamOnDemandLoader.UidFromEntryPath(url);
                if (string.IsNullOrEmpty(parentUid)) return;

                if (VamOnDemandLoader.EnsureDeclaredDependenciesActivatedForParent(parentUid))
                    RefreshPackageMorphsOnAllPersons();
            }
            catch (Exception e)
            {
                LogUtil.Log("[VPB PluginDep] " + e);
            }
        }

        // RefreshPackageMorphs re-ingests morphs from newly-registered packages into the morph DB
        // that GetMorphByDisplayName reads. Idempotent: a no-op when the package set is unchanged.
        static bool RefreshPackageMorphsOnAllPersons()
        {
            var sc = SuperController.singleton;
            if (sc == null) return false;
            bool changed = false;
            foreach (Atom atom in sc.GetAtoms())
            {
                if (atom == null || atom.type != "Person") continue;
                var selector = atom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (selector != null && selector.RefreshPackageMorphs()) changed = true;
            }
            return changed;
        }
    }
}
