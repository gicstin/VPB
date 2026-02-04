using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
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
            string value;
            string str;
            if (responseHeaders.TryGetValue("Content-Disposition", out value))
            {
                value = Regex.Replace(value, ";$", string.Empty);
                str = Regex.Replace(value, ".*filename=\"?([^\"]+)\"?.*", "$1");
            }
            else
            {
                str = Traverse.Create(__instance).Field("resolvedVarName").GetValue<string>();
            }
            LogUtil.Log("Hook DownloadComplete "+ str);
            // Move into the repository directory, then link it back
            VPB.FileManager.Refresh(true, false, false);
        }

    }
}
