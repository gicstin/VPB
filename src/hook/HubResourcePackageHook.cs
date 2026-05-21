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
            // The Hub package UI updates itself on download completion. Leave package
            // indexing to explicit refreshes to avoid full scans after each download.
        }

    }
}
