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

namespace VPB
{
    public class SuperControllerHook
    {
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

        static int _forcedLoadingUiFrame = -1;

        static void TryForceLoadingUiEarly(SuperController sc, string saveName)
        {
            try
            {
                if (sc == null) return;
                if (string.IsNullOrEmpty(saveName)) return;
                if (_forcedLoadingUiFrame == Time.frameCount) return;
                if (LogUtil.IsSceneLoading()) return;

                _forcedLoadingUiFrame = Time.frameCount;

                // Best-effort: enable VaM's loading UI *only* if a well-known flag exists.
                // Do NOT invoke unknown methods or force 'isLoading' flags, as that can block scene loads.
                try
                {
                    var t = sc.GetType();
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                    try
                    {
                        var p = t.GetProperty("loadingUIActive", flags);
                        if (p != null && p.PropertyType == typeof(bool) && p.CanWrite)
                        {
                            p.SetValue(sc, true, null);
                            return;
                        }
                    }
                    catch { }

                    try
                    {
                        var f = t.GetField("loadingUIActive", flags);
                        if (f != null && f.FieldType == typeof(bool))
                        {
                            f.SetValue(sc, true);
                            return;
                        }
                    }
                    catch { }
                }
                catch { }
            }
            catch { }
        }

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

        static int CalculateImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            string p = path;

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
            LogUtil.EndSceneLoadTotal("WorldUI.Activate");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "Load", new Type[] { typeof(string) })]
        public static void PreLoad(SuperController __instance, string saveName)
        {
            TryForceLoadingUiEarly(__instance, saveName);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadMerge", new Type[] { typeof(string) })]
        public static void PreLoadMerge(SuperController __instance, string saveName)
        {
            TryForceLoadingUiEarly(__instance, saveName);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal " + saveName + " " + loadMerge + " " + editMode);
            try
            {
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
            LogUtil.BeginSceneLoad(saveName);

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
            LogUtil.EndSceneLoadInternal("LoadInternal");
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

            if (!Settings.Instance.EnableZstdCompression.Value) return;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Skip the original logic
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
    }

}
