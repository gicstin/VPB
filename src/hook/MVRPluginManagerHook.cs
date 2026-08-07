using System;
using HarmonyLib;

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
                    VamOnDemandLoader.EnsurePackageMorphsIngested(null, "plugin_dep:" + parentUid);
            }
            catch (Exception e)
            {
                LogUtil.Log("[VPB PluginDep] " + e);
            }
        }
    }
}
