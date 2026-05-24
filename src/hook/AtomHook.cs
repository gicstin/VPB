using System.Runtime.InteropServices;
using System.Collections;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using SimpleJSON;
using System.Reflection;

namespace VPB
{
    class AtomHook
    {
        private static readonly HashSet<string> s_PresetCatalogRefreshedUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Person-level preset storables need a synchronous FileManager.Refresh before VaM binds
        // morph/clothing/hair from on-demand packages. Per-item clothing/hair preset storables
        // (e.g. Creator:ItemNamePreset) must not trigger sync refresh mid-apply — it runs
        // "Person refresh clothing and hair" and reverts active items to defaults.
        private static readonly HashSet<string> s_SyncRefreshPresetStorables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ClothingPresets",
                "HairPresets",
                "PosePresets",
                "AppearancePresets",
                "Skin",
                "PluginsPresets",
                "AnimationPresets",
                "BreastPhysicsPresets",
            };

        static bool ShouldSyncRefreshForPresetStorable(string storableId)
        {
            // "unknown" must not trigger person-level ClothingPresets refresh — that resets
            // per-item color presets and can clear hair during interactive apply.
            if (string.IsNullOrEmpty(storableId)) return false;
            if (string.Equals(storableId, "unknown", StringComparison.OrdinalIgnoreCase)) return false;
            return s_SyncRefreshPresetStorables.Contains(storableId);
        }

        // Load-look feature
        //prefab:TabControlAtom
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Atom), "LoadAppearancePreset", new Type[] { typeof(string) })]
        public static void PreLoadAppearancePreset(Atom __instance, string saveName = "savefile")
        {
            LogUtil.Log("[VPB hook]PreLoadAppearancePreset " + saveName);
            SuperControllerHook.ParsePresetForSimTextures(saveName);
            if (MVR.FileManagement.FileManager.FileExists(saveName))
            {
                using (MVR.FileManagement.FileEntryStreamReader fileEntryStreamReader = MVR.FileManagement.FileManager.OpenStreamReader(saveName, true))
                {
                    string aJSON = fileEntryStreamReader.ReadToEnd();
                    FileButton.EnsureInstalledInternal(aJSON);
                }
            }
        }

        // Clothing-related hooks were moved to DAZClothingHook.cs.

        // ky1001.PresetLoader loads using this method
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Atom), "LoadPreset", new Type[] { typeof(string) })]
        public static void PreLoadPreset(Atom __instance, string saveName = "savefile")
        {
            LogUtil.Log("[VPB hook]PreLoadPreset " + saveName);
            SuperControllerHook.ParsePresetForSimTextures(saveName);
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
            LogUtil.Log("[VPB hook]PreSyncPresetBrowsePath " + url);
            VarFileEntry varFileEntry = FileManager.GetVarFileEntry(url);
            if (varFileEntry != null)
            {
                var movedUids = new List<string>();
                bool dirty = varFileEntry.Package.InstallRecursive(movedUids);
                if (dirty)
                {
                    if (movedUids.Count == 0 && !string.IsNullOrEmpty(varFileEntry.Package.Uid))
                        movedUids.Add(varFileEntry.Package.Uid);
                    FileManagerBridge.Refresh("preset_sync_browse", RefreshScope.InstallOnly, movedUids, flushNativeImmediately: true);
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
            LogUtil.Log("[VPB hook]PreLoadSubSceneWithPath " + p);
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SubScene), "LoadSubScene")]
        public static void PreLoadSubScene(SubScene __instance)
        {
            MethodInfo getStorePathMethod = typeof(SubScene).GetMethod("GetStorePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object ret= getStorePathMethod.Invoke(__instance, new object[1] {true });
            string path = (string)ret + ".json";
            LogUtil.Log("[VPB hook]PreLoadSubScene " + path);
            if (path.Contains(":"))
            {
                string packagename = path.Substring(0,path.IndexOf(":"));
                var package = FileManager.GetPackage(packagename);
                if (package != null)
                {
                    var movedUids = new List<string>();
                    bool dirty = package.InstallRecursive(movedUids);
                    if (dirty)
                    {
                        if (movedUids.Count == 0 && !string.IsNullOrEmpty(package.Uid))
                            movedUids.Add(package.Uid);
                        FileManagerBridge.Refresh("subscene_install", RefreshScope.InstallOnly, movedUids);
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
            JSONClass processJSON = inputJSON;
            
            if (inputJSON != null && JSONOptimization.HasTimelinePlugin(inputJSON))
            {
                LogUtil.Log("[VPB hook]Filtering timeline plugin from preset");
                processJSON = JSONOptimization.FilterTimelinePlugins(inputJSON);
            }
            
            string storableId = "unknown";
            string atomName = "unknown";
            try
            {
                var storable = __instance.GetComponentInParent<JSONStorable>();
                if (storable != null)
                {
                    storableId = storable.storeId;
                    var atom = storable.GetComponentInParent<Atom>();
                    if (atom != null)
                    {
                        atomName = atom.name;
                    }
                }
            }
            catch { }

            try
            {
                var asm = typeof(AtomHook).Assembly;
                string asmPath = null;
                try { asmPath = asm != null ? asm.Location : null; } catch { }
                if (string.IsNullOrEmpty(asmPath))
                {
                    try
                    {
                        string codeBase = asm != null ? asm.CodeBase : null;
                        if (!string.IsNullOrEmpty(codeBase))
                        {
                            asmPath = new Uri(codeBase).LocalPath;
                        }
                    }
                    catch { }
                }
                string asmVer = asm != null ? asm.GetName().Version.ToString() : "null";
                string asmTime = "null";
                try { if (!string.IsNullOrEmpty(asmPath)) asmTime = System.IO.File.GetLastWriteTime(asmPath).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                LogUtil.Log("[VPB] DLL marker (preset hook) | ver=" + asmVer + " | ts=" + asmTime + " | path=" + (string.IsNullOrEmpty(asmPath) ? "null" : asmPath));
            }
            catch { }

            LogUtil.Log($"[VPB hook]PresetManager PreLoadPresetPreFromJSON {atomName} {storableId} {__instance.presetName}");
            if (processJSON != null)
            {
                SuperControllerHook.ParsePresetForSimTextures(processJSON, __instance.presetName);
                bool shouldRefreshCatalog = EnsureInstalledFromJSON(processJSON);
                if (shouldRefreshCatalog)
                {
                    try { VamOnDemandLoader.RequestCoalescedVamRefresh("preset_json_catalog"); } catch { }

                    // VaM's per-type catalogs (DAZ morph/clothing/hair) only repopulate during MVR FileManager.Refresh.
                    // Coalesced refresh fires 250ms+ later on a subsequent Update frame, after VaM has already
                    // applied this preset against stale catalogs. For interactive preset clicks, flush now so
                    // morphs/clothing/hair bind on first apply. Scene-load cascades stay coalesced to avoid N refreshes.
                    bool sceneLoad = false;
                    try { sceneLoad = VPBConfig.Instance != null && VPBConfig.Instance.IsLoadingScene; } catch { }
                    bool syncRefreshEnabled = true;
                    try { syncRefreshEnabled = Settings.Instance != null && Settings.Instance.SyncRefreshOnPresetLoad != null ? Settings.Instance.SyncRefreshOnPresetLoad.Value : true; } catch { }
                    if (!sceneLoad && syncRefreshEnabled && ShouldSyncRefreshForPresetStorable(storableId))
                    {
                        try { VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("preset_json_catalog_interactive"); } catch { }
                    }
                }
            }
        }

        static bool EnsureInstalledFromJSON(JSONNode node)
        {
            bool shouldRefreshCatalog = false;
            var results = new HashSet<string>();
            JSONOptimization.ExtractAllVariableReferences(node, results);
            if (results.Count > 0)
            {
                // Resolve + install (if needed) via FileManager.GetPackage, which also normalizes whitespace.
                // Keep logs high-signal: only summarize missing packages.
                var missing = new List<string>();
                foreach (var key in results)
                {
                    if (string.IsNullOrEmpty(key)) continue;

                    // EnsureInstalled defaults to true; will install recursively if the package exists but is not installed.
                    var pkg = FileManager.GetPackage(key);
                    if (pkg == null && !key.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                    {
                        pkg = FileManager.GetPackage(key + ".latest");
                    }

                    string touchedUid = null;
                    if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
                        touchedUid = pkg.Uid;
                    else
                        touchedUid = key;

                    // In scan-whitelist mode, installing via VPB's package registry alone is not enough.
                    // Pre-register the resolved UID in VaM FileManager before preset item lookups (hair/clothing).
                    if (ScanWhitelistManager.Instance.IsEnabled)
                    {
                        try
                        {
                            string od = null;
                            if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
                                od = VamOnDemandLoader.TryRegisterPackageOnDemand(pkg.Uid);
                            else
                                od = VamOnDemandLoader.TryRegisterPackageOnDemand(key);
                            if (!string.IsNullOrEmpty(od)) shouldRefreshCatalog = true;
                        }
                        catch { }

                        // Edge case: first UIA preset load can still miss catalog population until a later browse/refresh.
                        // Force a one-time refresh per dependency UID encountered through preset JSON.
                        if (!string.IsNullOrEmpty(touchedUid))
                        {
                            if (!s_PresetCatalogRefreshedUids.Contains(touchedUid))
                            {
                                s_PresetCatalogRefreshedUids.Add(touchedUid);
                                shouldRefreshCatalog = true;
                            }
                        }
                    }

                    if (pkg == null)
                    {
                        missing.Add(key);
                    }
                }

                if (missing.Count > 0)
                {
                    // Dedup + keep output short by default.
                    var unique = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
                    int shown = 0;
                    var sb = new StringBuilder();
                    sb.Append("Missing dependency packages (").Append(unique.Count).Append("):");
                    foreach (var k in unique)
                    {
                        if (shown >= 8) break;
                        sb.Append(" ").Append(k).Append(";");
                        shown++;
                    }
                    if (unique.Count > shown) sb.Append(" ...");
                    LogUtil.LogWarning(sb.ToString());
                }
            }
            return shouldRefreshCatalog;
        }
    }
}
