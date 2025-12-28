using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Prime31.MessageKit;
using GPUTools.Hair.Scripts.Settings;
namespace var_browser
{
    class SuperControllerHook
    {

        static int GetImagePriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 1000;
            string p;
            try { p = path.ToLowerInvariant(); } catch { p = path; }

            if (p.Contains("/hair/") || p.Contains("/hairstyles/") || p.Contains("/textures/hair") || p.Contains("hair_") || p.Contains("scalp") || p.Contains("strand") || p.Contains("hairtex")) return 0;

            if (p.Contains("/textures/makeups/") || p.Contains("/textures/makeup/") || p.Contains("/makeups/")) return 1;
            if (p.Contains("/textures/decals/") || p.Contains("/textures/decal/") || p.Contains("/decals/") || p.Contains("/decal/")) return 1;
            if (p.Contains("/textures/overlays/") || p.Contains("/textures/overlay/") || p.Contains("/overlays/") || p.Contains("/overlay/")) return 1;
            if (p.Contains("facemask") || p.Contains("face_mask") || p.Contains("mask") || p.Contains("opacity") || p.Contains("alpha"))
            {
                if (p.Contains("face") || p.Contains("makeup") || p.Contains("makeups") || p.Contains("freckle") || p.Contains("blush")) return 1;
            }
            if (p.Contains("freckle") || p.Contains("blush") || p.Contains("eyeshadow") || p.Contains("eye_shadow") || p.Contains("eyeliner") || p.Contains("eye_liner") || p.Contains("lipstick") || p.Contains("lip") || p.Contains("brow") || p.Contains("eyebrow") || p.Contains("foundation") || p.Contains("concealer") || p.Contains("highlight") || p.Contains("highlighter") || p.Contains("contour") || p.Contains("powder")) return 1;
            if (p.Contains("/textures/") && (p.Contains("/face") || p.Contains("faced") || p.Contains("face_"))) return 1;
            if (p.Contains("mouth")) return 2;
            if (p.Contains("eye") || p.Contains("iris") || p.Contains("cornea") || p.Contains("eyeball")) return 3;
            if (p.Contains("head")) return 4;
            if (p.Contains("torso") || p.Contains("body")) return 5;
            if (p.Contains("limb") || p.Contains("arms") || p.Contains("legs")) return 6;
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
                if (var_browser.FileManager.GetVarFileEntry(candidate) != null)
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

        static bool TryPromoteQueuedImage(LinkedList<ImageLoaderThreaded.QueuedImage> queuedImages, ImageLoaderThreaded.QueuedImage qi)
        {
            if (queuedImages == null || qi == null) return false;

            if (Settings.Instance == null) return false;
            bool face = (Settings.Instance.PrioritizeFaceTextures != null) && Settings.Instance.PrioritizeFaceTextures.Value;
            bool hair = (Settings.Instance.PrioritizeHairTextures != null) && Settings.Instance.PrioritizeHairTextures.Value;
            if (!face && !hair)
            {
                try
                {
                    if (Settings.Instance.LogImageQueueEvents != null && Settings.Instance.LogImageQueueEvents.Value)
                    {
                        int pri0 = GetImagePriority(qi.imgPath);
                        LogUtil.Log(string.Format("IMGQ promote.skip flags face={0} hair={1} pri={2} path={3}", face ? "1" : "0", hair ? "1" : "0", pri0, qi.imgPath));
                    }
                }
                catch { }
                return false;
            }

            if (qi.isThumbnail) return false;

            int pri = GetImagePriority(qi.imgPath);
            if (pri == 0)
            {
                if (!hair) return false;
            }
            else if (pri >= 1 && pri <= 4)
            {
                if (!face) return false;
            }
            else
            {
                return false;
            }

            LinkedListNode<ImageLoaderThreaded.QueuedImage> node = null;
            var it = queuedImages.Last;
            while (it != null)
            {
                if (object.ReferenceEquals(it.Value, qi)) { node = it; break; }
                it = it.Previous;
            }
            if (node == null) return false;

            var target = queuedImages.First;
            while (target != null)
            {
                var v = target.Value;
                if (v == null) { target = target.Next; continue; }
                if (object.ReferenceEquals(v, qi)) break;
                int p2 = GetImagePriority(v.imgPath);
                if (p2 > pri) break;
                target = target.Next;
            }
            if (target == null) return false;
            if (object.ReferenceEquals(target.Value, qi)) return false;

            try
            {
                if (Settings.Instance.LogImageQueueEvents != null && Settings.Instance.LogImageQueueEvents.Value)
                {
                    LogUtil.Log(string.Format("IMGQ promote.do flags face={0} hair={1} pri={2} path={3}", face ? "1" : "0", hair ? "1" : "0", pri, qi.imgPath));
                }
            }
            catch { }

            queuedImages.Remove(node);
            queuedImages.AddBefore(target, qi);
            return true;
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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "FileExists", new Type[] { typeof(string) })]
        public static void PreFileExists(ref string path)
        {
            string rewritten = RewriteVdsPathIfNeeded(path);
            if (!string.Equals(rewritten, path, StringComparison.Ordinal))
            {
                path = rewritten;
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
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal " + saveName + " " + loadMerge + " " + editMode);
            try
            {
                if (!LogUtil.IsSceneClickActive())
                {
                    LogUtil.BeginSceneClick(saveName);
                }
            }
            catch { }
            LogUtil.BeginSceneLoad(saveName);

            try
            {
                if (ImageLoadingMgr.singleton != null && !string.IsNullOrEmpty(saveName))
                {
                    string sceneJsonText = null;
                    if (File.Exists(saveName))
                    {
                        sceneJsonText = File.ReadAllText(saveName);
                    }
                    else if (saveName.Contains(":/"))
                    {
                        using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(saveName, true))
                        {
                            using (var sr = new StreamReader(fileEntryStream.Stream))
                            {
                                sceneJsonText = sr.ReadToEnd();
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(sceneJsonText))
                    {
                        ImageLoadingMgr.singleton.StartScenePrewarm(saveName, sceneJsonText);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM scene read failed: " + saveName + " " + ex.ToString());
            }
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
            if (Settings.Instance.PluginsAlwaysEnabled.Value)
                Traverse.Create(__instance).Field("_pluginsAlwaysEnabled").SetValue(true);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImageImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PreProcessImageImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath)) return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (!Settings.Instance.ReduceTextureSize.Value) return;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                // Skip the original logic
                qi.skipCache = true;
                qi.processed = true;
                qi.finished = true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "ProcessImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PreProcessImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath)) return;
            LogUtil.MarkImageActivity();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnail", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueThumbnail(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath)) return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (!Settings.Instance.ReduceTextureSize.Value) return;

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;
            //LogUtil.Log("PostQueueThumbnail:" + qi.imgPath + " " + qi.textureFormat);

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
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;

                if (queuedImages != null)
                {
                    var node = queuedImages.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (object.ReferenceEquals(node.Value, qi))
                        {
                            queuedImages.Remove(node);
                            break;
                        }
                        node = next;
                    }
                }
                return;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = TryPromoteQueuedImage(q, qi);
                LogImageQueueEvent("enqueue.thumb", qi, qCount, realQ, moved);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnailImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueThumbnailImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath)) return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (!Settings.Instance.ReduceTextureSize.Value) return;

            //LogUtil.Log("PostQueueThumbnailImmediate:" + qi.imgPath + " " + qi.textureFormat);

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
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;

                if (queuedImages != null)
                {
                    var node = queuedImages.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (object.ReferenceEquals(node.Value, qi))
                        {
                            queuedImages.Remove(node);
                            break;
                        }
                        node = next;
                    }
                }
                return;
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = TryPromoteQueuedImage(q, qi);
                LogImageQueueEvent("enqueue.thumb.immediate", qi, qCount, realQ, moved);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(qi.imgPath)) return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (!Settings.Instance.ReduceTextureSize.Value) return;

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;
            //LogUtil.Log("PostQueueImage:" + qi.imgPath + " " + qi.textureFormat);

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                bool removed = false;
                if (queuedImages != null)
                {
                    var node = queuedImages.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (object.ReferenceEquals(node.Value, qi))
                        {
                            queuedImages.Remove(node);
                            removed = true;
                            break;
                        }
                        node = next;
                    }
                }

                if (removed)
                {
                    var field2 = Traverse.Create(__instance).Field("numRealQueuedImages");
                    var numRealQueuedImages = (int)field2.GetValue();
                    field2.SetValue(numRealQueuedImages - 1);
                    var field3 = Traverse.Create(__instance).Field("progressMax");
                    var progressMax = (int)field3.GetValue();
                    field3.SetValue(progressMax - 1);
                }
            }

            try
            {
                var tr = Traverse.Create(__instance);
                var q = tr.Field("queuedImages").GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                int qCount = q != null ? q.Count : 0;
                int realQ = 0;
                try { realQ = (int)tr.Field("numRealQueuedImages").GetValue(); } catch { }
                bool moved = TryPromoteQueuedImage(q, qi);
                LogImageQueueEvent("enqueue.img", qi, qCount, realQ, moved);
            }
            catch { }
        }

        // It is added to cache before the callback, so we need to set skipCache one step earlier.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded.QueuedImage), "Finish")]
        public static void PostFinish_QueuedImage(ImageLoaderThreaded.QueuedImage __instance)
        {
            if (string.IsNullOrEmpty(__instance.imgPath)) return;

            // Track image activity for scene-load timing even when caching/resize is disabled.
            LogUtil.MarkImageActivity();

            if (!Settings.Instance.ReduceTextureSize.Value) return;

            if (LogUtil.IsSceneLoadActive() && __instance.isThumbnail) return;

            // Ignore hub browse
            if (__instance.tex != null)
            {
                ImageLoadingMgr.singleton.ResolveInflightForQueuedImage(__instance);
            }

            if (__instance.tex != null)
            {
                var tex = ImageLoadingMgr.singleton.GetResizedTextureFromBytes(__instance);
                if (tex != null)
                {
                    __instance.skipCache = true;
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
            if (Settings.Instance.CacheAssetBundle.Value)
            {
                var_browser.CustomAssetLoader.QueueLoadAssetBundleFromFile(abffr);
                return false; // Prevent the original method from running
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadSceneIntoTransform")]
        static bool QueueLoadSceneIntoTransform(MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr)
        {
            if (Settings.Instance.CacheAssetBundle.Value)
            {
                var_browser.CustomAssetLoader.QueueLoadSceneIntoTransform(slr);
                return false; // Prevent the original method from running
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "DoneWithAssetBundleFromFile")]
        static bool DoneWithAssetBundleFromFile(string path)
        {
            if (Settings.Instance.CacheAssetBundle.Value)
            {
                var_browser.CustomAssetLoader.DoneWithAssetBundleFromFile(path);
                return false; // Prevent the original method from running
            }
            return true;
        }
    }

}
