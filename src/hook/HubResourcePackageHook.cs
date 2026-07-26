using System;
using System.Collections.Generic;
using HarmonyLib;
namespace VPB
{
    class HubResourcePackageHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.Hub.HubResourcePackage), "DownloadComplete", 
            new Type[] { typeof(byte[]), typeof(Dictionary<string, string>) })]
        static void PostDownloadComplete(MVR.Hub.HubResourcePackage __instance, 
            byte[] data, Dictionary<string, string> responseHeaders)
        {
            // VPB.HubResourcePackage.DownloadComplete registers the package; avoid duplicate refresh here.
        }

    }
}
