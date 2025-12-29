using System.Runtime.InteropServices;
using System.Collections;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using SimpleJSON;
using System.Reflection;

namespace var_browser
{
    class AtomHook
    {
        // Load-look feature
        //prefab:TabControlAtom
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Atom), "LoadAppearancePreset", new Type[] { typeof(string) })]
        public static void PreLoadAppearancePreset(Atom __instance, string saveName = "savefile")
        {
            LogUtil.Log("[var browser hook]PreLoadAppearancePreset " + saveName);
            if (MVR.FileManagement.FileManager.FileExists(saveName))
            {
                using (MVR.FileManagement.FileEntryStreamReader fileEntryStreamReader = MVR.FileManagement.FileManager.OpenStreamReader(saveName, true))
                {
                    string aJSON = fileEntryStreamReader.ReadToEnd();
                    FileButton.EnsureInstalledInternal(aJSON);
                }
            }
        }

        // ky1001.PresetLoader loads using this method
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Atom), "LoadPreset", new Type[] { typeof(string) })]
        public static void PreLoadPreset(Atom __instance, string saveName = "savefile")
        {
            LogUtil.Log("[var browser hook]PreLoadPreset " + saveName);
            if (MVR.FileManagement.FileManager.FileExists(saveName))
            {
                using (MVR.FileManagement.FileEntryStreamReader fileEntryStreamReader = MVR.FileManagement.FileManager.OpenStreamReader(saveName, true))
                {
                    string aJSON = fileEntryStreamReader.ReadToEnd();
                    FileButton.EnsureInstalledInternal(aJSON);
                }
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.PresetManagerControl), "SyncPresetBrowsePath", new Type[] { typeof(string) })]
        protected static void PreSyncPresetBrowsePath(MeshVR.PresetManagerControl __instance, string url)
        {
            LogUtil.Log("[var browser hook]PreSyncPresetBrowsePath " + url);
            VarFileEntry varFileEntry = FileManager.GetVarFileEntry(url);
            if (varFileEntry != null)
            {
                bool dirty= varFileEntry.Package.InstallRecursive();
                if (dirty)
                {
                    MVR.FileManagement.FileManager.Refresh();
                    var_browser.FileManager.Refresh();
                }
            }
            else
            {
                if (File.Exists(url))
                {
                    string text = File.ReadAllText(url);
                    FileButton.EnsureInstalledInternal(text);
                }
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SubScene), "LoadSubSceneWithPath", new Type[] { typeof(string)})]
        public static void PreLoadSubSceneWithPath(SubScene __instance,string p)
        {
            LogUtil.Log("[var browser hook]PreLoadSubSceneWithPath " + p);
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SubScene), "LoadSubScene")]
        public static void PreLoadSubScene(SubScene __instance)
        {
            MethodInfo getStorePathMethod = typeof(SubScene).GetMethod("GetStorePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object ret= getStorePathMethod.Invoke(__instance, new object[1] {true });
            string path = (string)ret + ".json";
            LogUtil.Log("[var browser hook]PreLoadSubScene " + path);
            if (path.Contains(":"))
            {
                string packagename = path.Substring(0,path.IndexOf(":"));
                var package = FileManager.GetPackage(packagename);
                if (package != null)
                {
                    bool dirty = package.InstallRecursive();
                    if (dirty)
                    {
                        MVR.FileManagement.FileManager.Refresh();
                        var_browser.FileManager.Refresh();
                    }
                }
            }
            else
            {
                if (File.Exists(path))
                {
                    //Debug.Log("Exists " + url);
                    string text = File.ReadAllText(path);
                    FileButton.EnsureInstalledInternal(text);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.PresetManager), "LoadPresetPreFromJSON",
            new Type[] { typeof(JSONClass), typeof(bool) })]
        protected static void PreLoadPresetPreFromJSON(MeshVR.PresetManager __instance,
            JSONClass inputJSON,
            bool isMerge = false)
        {
            JSONClass newInst = null;
            if (inputJSON != null && inputJSON["storables"] != null)
            {
                // First check whether this preset contains timeline
                JSONArray array = inputJSON["storables"] as JSONArray;
                for (int i = 0; i < array.Count; i++)
                {
                    var node = array[i]["id"];
                    if (node != null)
                    {
                        // If this is a timeline preset, do not process it.
                        // Not sure if this approach is ideal.
                        if (node.Value.EndsWith("_VamTimeline.AtomPlugin"))
                        {
                            newInst = new JSONClass();
                            break;
                        }
                    }
                }
                // If timeline is included, remove it and clone a new JSON
                if (newInst != null)
                {
                    foreach (var item in inputJSON.Keys)
                    {
                        var node = inputJSON[item];
                        if (item != "storables")
                        {
                            newInst.Add(item, node);
                        }
                        else
                        {
                            JSONArray newArray = new JSONArray();
                            JSONArray array2 = inputJSON["storables"] as JSONArray;
                            for (int i = 0; i < array2.Count; i++)
                            {
                                var node2 = array2[i]["id"];
                                if (node2 != null)
                                {
                                    // Remove timeline data
                                    if (!node2.Value.EndsWith("_VamTimeline.AtomPlugin"))
                                    {
                                        newArray.Add(array2[i]);
                                    }
                                }
                                else
                                {
                                    newArray.Add(array2[i]);
                                }
                            }
                            newInst.Add(item, newArray);
                        }
                    }
                }
            }
            LogUtil.Log("[var browser hook]PresetManager PreLoadPresetPreFromJSON " + __instance.presetName);
            // This step can be slow when the JSON is large
            if (newInst!=null)
            {
                EnsureInstalledFromJSON(newInst);
            }
            else
            {
                EnsureInstalledFromJSON(inputJSON);
            }
        }

        static void EnsureInstalledFromJSON(JSONNode node)
        {
            var results = new HashSet<string>();
            ScanJSON(node, results);
            if (results.Count > 0)
            {
                bool dirty = FileButton.EnsureInstalledBySet(results);
                if (dirty)
                {
                    MVR.FileManagement.FileManager.Refresh();
                    var_browser.FileManager.Refresh();
                }
            }
        }

        static void ScanJSON(JSONNode node, HashSet<string> results)
        {
            if (node == null) return;

            if (node is JSONClass)
            {
                var cls = node as JSONClass;
                foreach (string k in cls.Keys)
                {
                    ScanJSON(cls[k], results);
                }
            }
            else if (node is JSONArray)
            {
                var arr = node as JSONArray;
                for (int i = 0; i < arr.Count; i++)
                {
                    ScanJSON(arr[i], results);
                }
            }
            else
            {
                string v = node.Value;
                VarNameParser.Parse(v, results);
            }
        }
    }
}
