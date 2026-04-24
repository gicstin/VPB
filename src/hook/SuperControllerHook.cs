using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Prime31.MessageKit;
using GPUTools.Hair.Scripts.Settings;
using SimpleJSON;

namespace VPB
{
    public class SuperControllerHook
    {
        // Registry of confirmed simulation texture paths extracted from preset files
        private static HashSet<string> simTextureRegistry = new HashSet<string>();
        private static HashSet<string> simTexturePatchedThisLoad = new HashSet<string>();
        private static readonly object registryLock = new object();

        /// <summary>
        /// Registers a texture path as a simulation texture based on preset parsing
        /// </summary>
        public static void RegisterSimTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string normalized = path.ToLowerInvariant();
            lock (registryLock)
            {
                simTextureRegistry.Add(normalized);
            }
        }

        /// <summary>
        /// Clears the sim texture registry when loading a new scene
        /// </summary>
        public static void ClearSimTextureRegistry()
        {
            lock (registryLock)
            {
                simTextureRegistry.Clear();
                simTexturePatchedThisLoad.Clear();
            }
        }

        /// <summary>
        /// Parses a clothing preset (.vaj/.vap) to find sim-enabled textures
        /// </summary>
        public static void ParsePresetForSimTextures(string presetPath)
        {
            if (string.IsNullOrEmpty(presetPath)) return;
            
            try
            {
                JSONNode root = UI.LoadJSONWithFallback(presetPath, null);
                if (root == null) return;

                // Recursively search for simEnabled entries with texture URLs
                ParseNodeForSimTexturesRecursive(root, presetPath);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB SIM] Failed to parse preset for sim textures: {presetPath} - {ex.Message}");
            }
        }

        private static void ParseNodeForSimTexturesRecursive(JSONNode node, string presetPath)
        {
            if (node == null) return;

            var obj = node.AsObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kv in obj)
                {
                    string key = kv.Key;
                    JSONNode value = kv.Value;

                    // Check if this entry has simEnabled="true"
                    if (key.Equals("simEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        string valStr = value.Value?.ToLowerInvariant();
                        if (valStr == "true" || valStr == "1")
                        {
                            // Look for texture URL in the parent or sibling nodes
                            // The structure is typically: { "id": "...", "simEnabled": "true", "textureUrl": "..." }
                            // Or: { "id": "...Sim", "simEnabled": "true", ... }
                            
                            // Try to find a texture URL in the same object
                            string textureUrl = FindTextureUrlInObject(obj);
                            if (!string.IsNullOrEmpty(textureUrl))
                            {
                                RegisterSimTexture(textureUrl);
                                LogUtil.Log($"[VPB SIM] Registered sim texture from preset: {textureUrl}");
                            }
                        }
                    }

                    // Recurse into child nodes
                    ParseNodeForSimTexturesRecursive(value, presetPath);
                }
            }

            var arr = node.AsArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    ParseNodeForSimTexturesRecursive(arr[i], presetPath);
                }
            }
        }

        private static string FindTextureUrlInObject(JSONClass obj)
        {
            if (obj == null) return null;

            // Common keys for texture URLs in clothing presets
            string[] urlKeys = new[] { "url", "Url", "textureUrl", "TextureUrl", 
                                       "diffuseUrl", "normalUrl", "specularUrl", "glossUrl" };

            foreach (var key in urlKeys)
            {
                if (obj[key] != null)
                {
                    string val = obj[key].Value;
                    if (!string.IsNullOrEmpty(val) && (val.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                                        val.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                    {
                        return val;
                    }
                }
            }

            // Also check "id" field which often contains the texture reference
            if (obj["id"] != null)
            {
                string id = obj["id"].Value;
                // If id ends with "Sim" and there's a nearby texture reference
                if (id.EndsWith("Sim", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find any URL field
                    foreach (KeyValuePair<string, JSONNode> kv in obj)
                    {
                        if (kv.Key.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            kv.Key.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string val = kv.Value?.Value;
                            if (!string.IsNullOrEmpty(val) && (val.EndsWith(".png") || val.EndsWith(".jpg")))
                                return val;
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsTextureReadableCompat(Texture2D tex)
        {
            if (tex == null) return false;
            try
            {
                // Unity versions used by VaM may not expose Texture2D.isReadable.
                // Probe via GetPixel, which throws when the texture is non-readable.
                tex.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPluginsAlwaysEnabledSettingOn()
        {
            try
            {
                return Settings.Instance != null
                    && Settings.Instance.PluginsAlwaysEnabled != null
                    && Settings.Instance.PluginsAlwaysEnabled.Value;
            }
            catch
            {
                return false;
            }
        }

        static Dictionary<string, int> _priorityCache = new Dictionary<string, int>(StringComparer.Ordinal);
        static object _priorityCacheLock = new object();

        static bool Has(string source, string value)
        {
            if (source == null || value == null) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            
            int priority;
            lock (_priorityCacheLock)
            {
                if (_priorityCache.TryGetValue(path, out priority))
                    return priority;
            }

            priority = CalculateImagePriority(path);

            lock (_priorityCacheLock)
            {
                if (_priorityCache.Count >= 10000) _priorityCache.Clear();
                if (!_priorityCache.ContainsKey(path))
                    _priorityCache.Add(path, priority);
            }
            return priority;
        }

        internal static bool IsSimulationTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();

            // First check the registry of confirmed sim textures from preset parsing
            lock (registryLock)
            {
                if (simTextureRegistry.Contains(lower))
                    return true;
            }

            // Fall back to heuristic detection
            if (lower.Contains("phys")) return true;
            if (lower.Contains("simulation")) return true;

            int lastSlash = lower.LastIndexOfAny(new char[] { '/', '\\' });
            string filename = lastSlash >= 0 ? lower.Substring(lastSlash + 1) : lower;
            int lastDot = filename.LastIndexOf('.');
            if (lastDot > 0) filename = filename.Substring(0, lastDot);

            // Conservative fallback only: match "sim" as a token to avoid false positives like "simone".
            // Accepted examples: sim_foo, foo_sim, foo-sim1, sim1, phys_foo, physics-2
            if (Regex.IsMatch(filename, @"(^|[_\-])sim([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(filename, @"(^|[_\-])phys(ics)?([_\-]|\d|$)", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        static int CalculateImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            string p = path;

            if (IsSimulationTexturePath(p)) return -1;

            if (Has(p, "/hair/") || Has(p, "/hairstyles/") || Has(p, "/textures/hair") || Has(p, "hair_") || Has(p, "scalp") || Has(p, "strand") || Has(p, "hairtex")) return 0;

            if (Has(p, "/textures/makeups/") || Has(p, "/textures/makeup/") || Has(p, "/makeups/")) return 1;
            if (Has(p, "/textures/decals/") || Has(p, "/textures/decal/") || Has(p, "/decals/") || Has(p, "/decal/")) return 1;
            if (Has(p, "/textures/overlays/") || Has(p, "/textures/overlay/") || Has(p, "/overlays/") || Has(p, "/overlay/")) return 1;
            if (Has(p, "facemask") || Has(p, "face_mask") || Has(p, "mask") || Has(p, "opacity") || Has(p, "alpha"))
            {
                if (Has(p, "face") || Has(p, "makeup") || Has(p, "makeups") || Has(p, "freckle") || Has(p, "blush")) return 1;
            }
            if (Has(p, "freckle") || Has(p, "blush") || Has(p, "eyeshadow") || Has(p, "eye_shadow") || Has(p, "eyeliner") || Has(p, "eye_liner") || Has(p, "lipstick") || Has(p, "lip") || Has(p, "brow") || Has(p, "eyebrow") || Has(p, "foundation") || Has(p, "concealer") || Has(p, "highlight") || Has(p, "highlighter") || Has(p, "contour") || Has(p, "powder")) return 1;
            if (Has(p, "/textures/") && (Has(p, "/face") || Has(p, "faced") || Has(p, "face_"))) return 1;
            if (Has(p, "mouth")) return 2;
            if (Has(p, "eye") || Has(p, "iris") || Has(p, "cornea") || Has(p, "eyeball")) return 3;
            if (Has(p, "head")) return 4;
            if (Has(p, "torso") || Has(p, "body")) return 5;
            if (Has(p, "limb") || Has(p, "arms") || Has(p, "legs")) return 6;
            return 100;
        }

        static string GetImageCategory(string path)
        {
            int pri = GetImagePriority(path);
            if (pri == 0) return "hair";
            if (pri == 1) return "face";
            if (pri == 2) return "mouth";
            if (pri == 3) return "eyes";
            if (pri == 4) return "head";
            if (pri == 5) return "body";
            if (pri == 6) return "limbs";
            return "other";
        }

        static string RewriteVdsPathIfNeeded(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return path;
                if (!VdsLauncher.IsVdsEnabled()) return path;
                if (!LogUtil.IsSceneLoadActive()) return path;

                string scenePkg = LogUtil.GetSceneLoadPackageUid();

                string p = path.Replace('\\', '/');
                if (p.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
                {
                    string curPkg = null;
                    try { curPkg = MVR.FileManagement.FileManager.CurrentPackageUid; } catch { }
                    string pkg = !string.IsNullOrEmpty(curPkg) ? curPkg : scenePkg;
                    if (string.IsNullOrEmpty(pkg)) return path;
                    return pkg + ":/" + p.Substring("SELF:/".Length);
                }
                if (p.Contains(":/")) return path;
                if (!p.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return path;

                if (string.IsNullOrEmpty(scenePkg)) return path;

                string candidate = scenePkg + ":/" + p;
                if (VPB.FileManager.GetVarFileEntry(candidate) != null)
                {
                    return candidate;
                }
                return path;
            }
            catch
            {
                return path;
            }
        }

        public static void PatchOptional(Harmony harmony)
        {
            PatchFileExists(harmony);
            PatchProcessImage(harmony);
        }

        static void PatchFileExists(Harmony harmony)
        {
            var fm = typeof(MVR.FileManagement.FileManager);
            var prefix = AccessTools.Method(typeof(SuperControllerHook), nameof(PreFileExists));
            if (prefix == null) return;
            var candidates = new Type[][]
            {
                new[] { typeof(string), typeof(bool), typeof(bool) },
                new[] { typeof(string), typeof(bool) },
                new[] { typeof(string) }
            };
            foreach (var sig in candidates)
            {
                var m = AccessTools.Method(fm, "FileExists", sig);
                if (m == null) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                return;
            }
        }

        static void PatchProcessImage(Harmony harmony)
        {
            var ilt = typeof(ImageLoaderThreaded);
            var prefix = AccessTools.Method(typeof(SuperControllerHook), nameof(PreProcessImage));
            var postfix = AccessTools.Method(typeof(SuperControllerHook), nameof(PostProcessImage));
            if (prefix == null) return;
            var methods = AccessTools.GetDeclaredMethods(ilt);
            if (methods == null) return;
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (!string.Equals(m.Name, "ProcessImage", StringComparison.Ordinal)) continue;
                var p = m.GetParameters();
                if (p == null || p.Length == 0) continue;
                if (p[0].ParameterType != typeof(ImageLoaderThreaded.QueuedImage)) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                return;
            }
        }

        static void LogImageQueueEvent(string evt, ImageLoaderThreaded.QueuedImage qi, int queueCount, int numRealQueuedImages, bool moved)
        {
            if (qi == null) return;
            try
            {
                if (Settings.Instance != null && Settings.Instance.LogImageQueueEvents != null && !Settings.Instance.LogImageQueueEvents.Value)
                {
                    return;
                }
            }
            catch { }
            string scene = LogUtil.GetSceneLoadName();
            int pri = GetImagePriority(qi.imgPath);
            string cat = GetImageCategory(qi.imgPath);
            string thumb = qi.isThumbnail ? "thumb" : "img";
            LogUtil.Log(string.Format("IMGQ {0} scene={1} type={2} cat={3} pri={4} moved={5} q={6} realq={7} path={8}", evt, scene, thumb, cat, pri, moved ? "1" : "0", queueCount, numRealQueuedImages, qi.imgPath));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefresh()
        {
            LogUtil.Log("FileManager PreRefresh");
            try { PackageHidePrefs.InvalidateHideMarkerCache(); } catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "NormalizeLoadPath", new Type[] { typeof(string) })]
        public static void PreNormalizeLoadPath(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
            }
        }

        public static void PreFileExists(ref string __0)
        {
            string rewritten = RewriteVdsPathIfNeeded(__0);
            if (!string.Equals(rewritten, __0, StringComparison.Ordinal))
            {
                __0 = rewritten;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "FileExists", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
        public static void PostFileExists3(ref bool __result)
        {
            LogUtil.RecordFileExistsResult(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "FileExists", new Type[] { typeof(string), typeof(bool) })]
        public static void PostFileExists2(ref bool __result)
        {
            LogUtil.RecordFileExistsResult(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "FileExists", new Type[] { typeof(string) })]
        public static void PostFileExists1(ref bool __result)
        {
            LogUtil.RecordFileExistsResult(__result);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStream", new Type[] { typeof(string), typeof(bool) })]
        public static void PreOpenStream(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStream", new Type[] { typeof(string), typeof(bool) })]
        public static void PostOpenStream(object __result)
        {
            LogUtil.RecordOpenStreamResult(__result != null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStreamReader", new Type[] { typeof(string), typeof(bool) })]
        public static void PreOpenStreamReader(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "OpenStreamReader", new Type[] { typeof(string), typeof(bool) })]
        public static void PostOpenStreamReader(object __result)
        {
            LogUtil.RecordOpenStreamResult(__result != null);
        }

        // Click "Return To Scene View"
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "DeactivateWorldUI")]
        public static void PostDeactivateWorldUI(SuperController __instance)
        {
            LogUtil.Log("PostDeactivateWorldUI");
            MessageKit.post(MessageDef.DeactivateWorldUI);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "ActivateWorldUI")]
        public static void PostActivateWorldUI(SuperController __instance)
        {
            LogUtil.LogStartupReadyOnce("World UI activated");
            LogUtil.MarkScenePhaseWorldUiActivated();
            LogUtil.EndSceneLoadTotal("WorldUI.Activate");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal " + saveName + " " + loadMerge + " " + editMode);
            LogUtil.BeginSceneLoad(saveName);
            LogUtil.MarkScenePhasePreLoadInternal();
            try
            {
                // Clear sim texture registry for new scene
                ClearSimTextureRegistry();

                try
                {
                    SceneLoadingUtils.NotifySceneLoadStarting(saveName, loadMerge);
                }
                catch { }

                if (ImageLoadingMgr.singleton != null)
                {
                    ImageLoadingMgr.singleton.ClearCandidates();
                }

                if (!string.IsNullOrEmpty(saveName))
                {
                    // Track current scene package UID for UninstallAll protection
                    int idx = saveName.IndexOf(":/");
                    if (idx >= 0)
                    {
                        VamHookPlugin.CurrentScenePackageUid = saveName.Substring(0, idx);
                    }
                    else if (!loadMerge)
                    {
                        // Only clear if not merging (merging implies we are adding to current scene)
                        VamHookPlugin.CurrentScenePackageUid = null;
                    }
                }

                if (!LogUtil.IsSceneClickActive())
                {
                    LogUtil.BeginSceneClick(saveName);
                }
            }
            catch { }

            if (saveName == "Saves\\scene\\MeshedVR\\default.json")
            {
                if (File.Exists(saveName))
                {
                    string text = File.ReadAllText(saveName);
                    FileButton.EnsureInstalledInternal(text);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PostLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.MarkScenePhasePostLoadInternal();
            LogUtil.EndSceneLoadInternal("LoadInternal");
            try { SceneLoadingUtils.ScheduleGalleryTargetListRefresh(); } catch { }
        }

        /// <summary>
        /// Keep gallery target picker in sync when an atom is removed (no polling).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "RemoveAtom", new Type[] { typeof(Atom) })]
        public static void PostRemoveAtom(SuperController __instance, Atom atom)
        {
            try { GalleryPanel.NotifyAllPanelsSceneTargetsChanged(); } catch { }
        }

        /// <summary>
        /// Always set Allow Always
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "LoadUserPrefs")]
        public static void PostLoadUserPrefs(MVR.FileManagement.VarPackage __instance)
        {
            if (__instance == null) return;
            if (!IsPluginsAlwaysEnabledSettingOn()) return;
            try
            {
                Traverse.Create(__instance).Field("_pluginsAlwaysEnabled").SetValue(true);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "get_PluginsAlwaysEnabled")]
        public static void PostGetPluginsAlwaysEnabled(ref bool __result)
        {
            if (IsPluginsAlwaysEnabledSettingOn()) __result = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "get_PluginsAlwaysDisabled")]
        public static void PostGetPluginsAlwaysDisabled(ref bool __result)
        {
            if (IsPluginsAlwaysEnabledSettingOn()) __result = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImageImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PreProcessImageImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            LogUtil.MarkImageActivity();

            ImageLoadingMgr.currentProcessingPath = qi.imgPath;
            ImageLoadingMgr.currentProcessingIsThumbnail = qi.isThumbnail;
            ImageLoadingMgr.currentProcessingQI = qi;

            if (!Settings.Instance.EnableZstdCompression.Value) return;

            bool immOk = ImageLoadingMgr.singleton.RequestImmediate(qi);
            try { int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0; if (lvl >= 1 && (qi.createAlphaFromGrayscale || (qi.imgPath != null && qi.imgPath.IndexOf("Alphamidpart", StringComparison.OrdinalIgnoreCase) >= 0))) LogUtil.Log("[VPB IMM] path=" + qi.imgPath + " A=" + qi.createAlphaFromGrayscale + " ok=" + immOk + " tex=" + (qi.tex != null ? qi.tex.format.ToString() + " " + qi.tex.width + "x" + qi.tex.height : "null")); } catch { }
            if (immOk)
            {
                qi.skipCache = true;
                qi.processed = true;
                qi.finished = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImageImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostProcessImageImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            ImageLoadingMgr.currentProcessingPath = null;
            ImageLoadingMgr.currentProcessingIsThumbnail = false;
            ImageLoadingMgr.currentProcessingQI = null;

            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            if (!Settings.Instance.EnableZstdCompression.Value) return;
        }

        public static void PreProcessImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage __0)
        {
            var qi = __0;
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            LogUtil.MarkImageActivity();

            ImageLoadingMgr.currentProcessingPath = qi.imgPath;
            ImageLoadingMgr.currentProcessingIsThumbnail = qi.isThumbnail;
        }

        public static void PostProcessImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage __0)
        {
            ImageLoadingMgr.currentProcessingPath = null;
            ImageLoadingMgr.currentProcessingIsThumbnail = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnail", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueThumbnail(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("thumb", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null)
            {
                LogUtil.LogWarning("[VPB] PreQueueThumbnail: ImageLoadingMgr.singleton is null");
                return true;
            }

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            qi.isThumbnail = true;
            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Served from VPB cache: ensure VaM's thumbnail cache is populated.
                try
                {
                    var thumbCache = Traverse.Create(__instance).Field("thumbnailCache").GetValue() as Dictionary<string, Texture2D>;
                    if (thumbCache != null && qi.tex != null && !thumbCache.ContainsKey(qi.imgPath))
                    {
                        thumbCache.Add(qi.imgPath, qi.tex);
                    }
                }
                catch { }

                // Skip VaM threaded processing for this request.
                return false;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.thumb", qi, qCount, realQ, moved);
            }
            catch { }
            return true;
        }

        private static void LogImageRequestDetails(string kind, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;

            string imgPath = qi.imgPath;
            string nativeCachePath = null;
            bool nativeExists = false;
            bool metaExists = false;
            FileEntry fe = null;

            try { fe = FileManager.GetFileEntry(imgPath); } catch { fe = null; }

            try
            {
                nativeCachePath = TextureUtil.GetNativeCachePath(imgPath);
                if (!string.IsNullOrEmpty(nativeCachePath))
                {
                    nativeExists = File.Exists(nativeCachePath);
                    metaExists = File.Exists(nativeCachePath + "meta");
                }
            }
            catch { }

            string feInfo = fe != null ? ("fe=1 size=" + fe.Size + " ts=" + fe.LastWriteTime.ToFileTime()) : "fe=0";
            string cacheInfo = !string.IsNullOrEmpty(nativeCachePath) ? ("cache=1 exists=" + (nativeExists ? "1" : "0") + " meta=" + (metaExists ? "1" : "0")) : "cache=0";
            LogUtil.Log("[VPB] [VaMLoad] " + kind + " | " + imgPath + " | " + feInfo + " | " + cacheInfo);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnailImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueThumbnailImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("thumb.immediate", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null) return true;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Served from VPB cache: ensure VaM's thumbnail cache is populated.
                try
                {
                    var thumbCache = Traverse.Create(__instance).Field("thumbnailCache").GetValue() as Dictionary<string, Texture2D>;
                    if (thumbCache != null && qi.tex != null && !thumbCache.ContainsKey(qi.imgPath))
                    {
                        thumbCache.Add(qi.imgPath, qi.tex);
                    }
                }
                catch { }

                // Skip VaM threaded processing for this request.
                return false;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.thumb.immediate", qi, qCount, realQ, moved);
            }
            catch { }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static bool PreQueueImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return true;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            try
            {
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogImageRequestDetails("img", qi);
                }
            }
            catch { }

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return true;
            if (!Settings.Instance.EnableZstdCompression.Value) return true;

            if (ImageLoadingMgr.singleton == null) return true;

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                return false;
            }

            try { int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0; if (lvl >= 1 && (qi.createAlphaFromGrayscale || (qi.imgPath != null && qi.imgPath.IndexOf("Alphamidpart", StringComparison.OrdinalIgnoreCase) >= 0))) LogUtil.Log("[VPB QUEUE] MISS path=" + qi.imgPath + " A=" + qi.createAlphaFromGrayscale); } catch { }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = false;
                LogImageQueueEvent("enqueue.img", qi, qCount, realQ, moved);
            }
            catch { }
            return true;
        }

        // It is added to cache before the callback, so we need to set skipCache one step earlier.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded.QueuedImage), "Finish")]
        public static void PostFinish_QueuedImage(ImageLoaderThreaded.QueuedImage __instance)
        {
            if (string.IsNullOrEmpty(__instance.imgPath) || __instance.imgPath == "NULL") return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null) return;
            if (!Settings.Instance.EnableZstdCompression.Value) return;



            // Ignore hub browse
            if (__instance.tex != null)
            {
                // Fix up simulation textures that were loaded as non-readable by VaM's native loader
                if (IsSimulationTexturePath(__instance.imgPath))
                {
                    try
                    {
                        bool alreadyPatched;
                        lock (registryLock)
                        {
                            alreadyPatched = simTexturePatchedThisLoad.Contains(__instance.imgPath);
                            if (!alreadyPatched) simTexturePatchedThisLoad.Add(__instance.imgPath);
                        }

                        // Avoid repeatedly running expensive conversion for the same asset path in one scene load.
                        if (!alreadyPatched)
                        {
                            var tex = __instance.tex as Texture2D;
                            if (tex != null && !IsTextureReadableCompat(tex))
                            {
                                LogUtil.Log($"[VPB SIM] PostFinish: Fixing up non-readable sim texture: {__instance.imgPath}");

                                // Recreate as readable using GPU copy -> ReadPixels.
                                RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                                Graphics.Blit(tex, rt);

                                RenderTexture prev = RenderTexture.active;
                                RenderTexture.active = rt;
                                Texture2D readableTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, __instance.linear);
                                readableTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                                readableTex.Apply(false, false); // keep readable
                                RenderTexture.active = prev;
                                RenderTexture.ReleaseTemporary(rt);

                                UnityEngine.Object.Destroy(tex);
                                __instance.tex = readableTex;

                                LogUtil.Log($"[VPB SIM] PostFinish: Fixed sim texture to be readable: {__instance.imgPath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[VPB SIM] PostFinish: Failed to fix up sim texture {__instance.imgPath}: {ex.Message}");
                    }
                }

                if (__instance.createAlphaFromGrayscale)
                {
                    ImageLoadingMgr.WriteAlphaTextureToZstdCache(__instance);
                }

                if (ImageLoadingMgr.singleton != null)
                {
                    ImageLoadingMgr.singleton.ResolveInflightForQueuedImage(__instance);
                }
            }

        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DAZMorph), "LoadDeltas")]
        public static void PostLoadDeltasFromBinaryFile(DAZMorph __instance)
        {
            var path = __instance.deltasLoadPath;
            if (string.IsNullOrEmpty(path)) return;
            if (__instance.deltasLoaded) return;
            __instance.deltasLoaded = true;

            if (DAZMorphMgr.singleton.cache.ContainsKey(path))
            {
                LogUtil.Log("LoadDeltas use cache:" + path);
                __instance.deltas = DAZMorphMgr.singleton.cache[path];
                return;
            }

            using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(path, true))
            {
                using (BinaryReader binaryReader = new BinaryReader(fileEntryStream.Stream))
                {
                    var numDeltas = binaryReader.ReadInt32();
                    var deltas = new DAZMorphVertex[numDeltas];
                    Vector3 delta = default(Vector3);
                    for (int i = 0; i < numDeltas; i++)
                    {
                        DAZMorphVertex dAZMorphVertex = new DAZMorphVertex();
                        dAZMorphVertex.vertex = binaryReader.ReadInt32();
                        delta.x = binaryReader.ReadSingle();
                        delta.y = binaryReader.ReadSingle();
                        delta.z = binaryReader.ReadSingle();
                        dAZMorphVertex.delta = delta;
                        deltas[i] = dAZMorphVertex;
                    }

                    __instance.deltas = deltas;
                    DAZMorphMgr.singleton.cache.Add(path, deltas);
                }
            }
        }

    }

    //[HarmonyPatch(typeof(HairLODSettings), nameof(HairLODSettings.GetDencity))]
    //class PatchHairLODSettings1
    //{
    //    static void Postfix(HairLODSettings __instance,ref int __result)
    //    {
    //        //if (!Settings.Instance.UseNewCahe.Value) return;
    //        //if (!__instance.UseFixedSettings)
    //            __result = 1;// (int)__instance.Density.Min;
    //    }
    //}
    //[HarmonyPatch(typeof(HairLODSettings), nameof(HairLODSettings.GetWidth))]
    //class PatchHairLODSettings2
    //{
    //    static void Postfix(HairLODSettings __instance, ref float __result)
    //    {
    //        //if (!Settings.Instance.UseNewCahe.Value) return;
    //        //if (!__instance.UseFixedSettings)
    //        __result = __result*5;
    //    }
    //}

    class PatchAssetLoader
    {
        //[HarmonyPrefix]
        //[HarmonyPatch(typeof(MeshVR.AssetLoader),"Start")]
        //static bool Start(MeshVR.AssetLoader __instance)
        //{
        //    LogUtil.Log("PatchAssetLoader Start");
        //    return false; // Prevent the original method from running
        //}
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadAssetBundleFromFile")]
        static bool QueueLoadAssetBundleFromFile(MeshVR.AssetLoader.AssetBundleFromFileRequest abffr)
        {
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadSceneIntoTransform")]
        static bool QueueLoadSceneIntoTransform(MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr)
        {
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "DoneWithAssetBundleFromFile")]
        static bool DoneWithAssetBundleFromFile(string path)
        {
            return true;
        }

        // --- Scan Whitelist Patches ---

        /// <summary>
        /// Blocks VaM from registering non-whitelisted packages during its startup scan.
        /// PREFIX patch so VaM never opens the .var zip or reads the manifest for excluded
        /// packages — the expensive I/O is skipped entirely, not just cleaned up afterward.
        /// On-demand registration (via VamOnDemandLoader) bypasses this via s_AllowRegistration.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "RegisterPackage")]
        public static bool PreRegisterPackageScanFilter(string __0)
        {
            try
            {
                if (!ScanWhitelistManager.Instance.IsEnabled) return true;
                if (VamOnDemandLoader.s_AllowRegistration) return true;
                if (string.IsNullOrEmpty(__0)) return true;

                string norm = __0.Replace('\\', '/');
                if (!norm.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) return true;

                string uid = System.IO.Path.GetFileNameWithoutExtension(norm);
                if (ScanWhitelistManager.Instance.IsUidOverrideIncluded(uid))
                {
                    VamScanFilter.RecordScanAllowed();
                    return true;
                }

                bool allowed = ScanWhitelistManager.Instance.IsPathWhitelisted(norm);
                if (allowed) VamScanFilter.RecordScanAllowed();
                else VamScanFilter.RecordScanBlocked();
                return allowed;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB ScanFilter] PreRegisterPackageScanFilter error: " + ex.Message);
                return true; // fail open
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefreshResetScanCounters()
        {
            VamScanFilter.ResetScanCounters();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PostRefreshLogScanResult()
        {
            VamScanFilter.LogScanResult();
        }

        /// <summary>
        /// After VaM's GetVarFileEntry returns null for a scan-excluded package,
        /// register the package on-demand in VaM's FileManager and retry.
        /// This ensures MVRScript plugins can still load dependencies from
        /// non-whitelisted packages without requiring a full scan.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "GetVarFileEntry", new Type[] { typeof(string) })]
        public static void PostGetVarFileEntryOnDemand(string path, ref MVR.FileManagement.VarFileEntry __result)
        {
            try
            {
                if (__result != null) return;
                if (!ScanWhitelistManager.Instance.IsEnabled) return;
                if (VamOnDemandLoader.s_InOnDemand) return;

                string uid = VamOnDemandLoader.UidFromEntryPath(path);
                if (string.IsNullOrEmpty(uid)) return;
                LogUtil.RecordVarEntryMiss();

                LogUtil.RecordOnDemandRetry();
                VamOnDemandLoader.TryRegisterPackageOnDemand(uid);
                VamOnDemandLoader.s_InOnDemand = true;
                try
                {
                    __result = MVR.FileManagement.FileManager.GetVarFileEntry(path);
                    if (__result != null) return;

                    // Some VaM call sites pass *.latest:/... and do not resolve aliases
                    // after registration. Retry with a concrete UID path when possible.
                    string rewritten = VamOnDemandLoader.TryRewriteLatestEntryPath(path, attemptRegister: true);
                    if (!string.IsNullOrEmpty(rewritten) && !string.Equals(rewritten, path, StringComparison.OrdinalIgnoreCase))
                    {
                        LogUtil.RecordOnDemandRetry();
                        __result = MVR.FileManagement.FileManager.GetVarFileEntry(rewritten);
                    }
                }
                finally
                {
                    VamOnDemandLoader.s_InOnDemand = false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] PostGetVarFileEntryOnDemand error: " + ex.Message);
            }
        }
    }

}
