using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Prime31.MessageKit;
using ICSharpCode.SharpZipLib.Zip;
using GPUTools.Hair.Scripts.Settings;
namespace var_browser
{
    class SuperControllerHook
    {

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefresh()
        {
            LogUtil.Log("FileManager PreRefresh");
            ZipConstants.DefaultCodePage = Settings.Instance.CodePage.Value;
        }

        //点击“Return To Scene View"
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
        /// 始终设置Allow Always
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
            if (!Settings.Instance.ReduceTextureSize.Value) return;
            if (string.IsNullOrEmpty(qi.imgPath)) return;
            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                //跳过原有的逻辑
                qi.skipCache = true;
                qi.processed = true;
                qi.finished = true;
            }
        }



        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnail", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueThumbnail(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (!Settings.Instance.ReduceTextureSize.Value) return;
            if (string.IsNullOrEmpty(qi.imgPath)) return;

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;
            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;
            //LogUtil.Log("PostQueueThumbnail:" + qi.imgPath + " " + qi.textureFormat);

            qi.isThumbnail = true;
            if (ImageLoadingMgr.singleton.Request(qi))
            {
                //跳过
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;

                queuedImagesListCache.Clear();
                foreach (var item in queuedImages)
                {
                    if (item.imgPath == qi.imgPath)
                    {
                        queuedImagesListCache.Add(item);
                    }
                }
                //移除最后一个
                int cnt = queuedImagesListCache.Count;
                if (cnt > 0)
                {
                    queuedImages.Remove(queuedImagesListCache[cnt-1]);
                }
                return;
            }
        }
        public static List<ImageLoaderThreaded.QueuedImage> queuedImagesListCache = new List<ImageLoaderThreaded.QueuedImage>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnailImmediate", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueThumbnailImmediate(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (!Settings.Instance.ReduceTextureSize.Value) return;
            if (string.IsNullOrEmpty(qi.imgPath)) return;
            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;

            //LogUtil.Log("PostQueueThumbnailImmediate:" + qi.imgPath + " " + qi.textureFormat);

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                //跳过
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;

                queuedImagesListCache.Clear();
                foreach (var item in queuedImages)
                {
                    if (item.imgPath == qi.imgPath)
                    {
                        queuedImagesListCache.Add(item);
                    }
                }
                //移除第一个
                int cnt = queuedImagesListCache.Count;
                if (cnt > 0)
                {
                    queuedImages.Remove(queuedImagesListCache[0]);
                }
                return;
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            if (!Settings.Instance.ReduceTextureSize.Value) return;
            if (string.IsNullOrEmpty(qi.imgPath)) return;


            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;
            //LogUtil.Log("PostQueueImage:" + qi.imgPath + " " + qi.textureFormat);

            if (ImageLoadingMgr.singleton.Request(qi))
            {
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                queuedImagesListCache.Clear();
                foreach (var item in queuedImages)
                {
                    if (item.imgPath == qi.imgPath)
                    {
                        queuedImagesListCache.Add(item);
                        break;
                    }
                }
                int cnt = queuedImagesListCache.Count;
                if (cnt > 0)
                {
                    queuedImages.Remove(queuedImagesListCache[cnt-1]);

                    var field2 = Traverse.Create(__instance).Field("numRealQueuedImages");
                    var numRealQueuedImages = (int)field2.GetValue();
                    field2.SetValue(numRealQueuedImages - 1);
                    var field3 = Traverse.Create(__instance).Field("progressMax");
                    var progressMax = (int)field3.GetValue();
                    field3.SetValue(progressMax - 1);
                }
            }
        }
        //在callback之前就会放入cache，所以需要提前一步设置skipCache
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded.QueuedImage), "Finish")]
        public static void PostFinish_QueuedImage(ImageLoaderThreaded.QueuedImage __instance)
        {
            if (!Settings.Instance.ReduceTextureSize.Value) return;

            if (string.IsNullOrEmpty(__instance.imgPath)) return;

            //忽略hub browse
            if (__instance.webRequest != null || __instance.useWebCache) return;
            if (Regex.IsMatch(__instance.imgPath, "^http")) return;

            if (__instance.tex != null)
            {
                if (__instance.tex.format == TextureFormat.DXT1
                    || __instance.tex.format == TextureFormat.DXT5
                    || __instance.tex.format == TextureFormat.RGB24
                    || __instance.tex.format == TextureFormat.RGBA32)
                {
                    //LogUtil.Log("PostFinish_QueuedImage:" + __instance.imgPath + " " + __instance.textureFormat + " " + __instance.tex.format);

                    var tex = ImageLoadingMgr.singleton.GetResizedTextureFromBytes(__instance);
                    if (tex != null)
                    {
                        __instance.skipCache = true;
                    }
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
                  var  numDeltas = binaryReader.ReadInt32();
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
        //    return false; // 阻止原始方法执行
        //}
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MeshVR.AssetLoader), "QueueLoadAssetBundleFromFile")]
        static bool QueueLoadAssetBundleFromFile(MeshVR.AssetLoader.AssetBundleFromFileRequest abffr)
        {
            if (Settings.Instance.CacheAssetBundle.Value)
            {
                var_browser.CustomAssetLoader.QueueLoadAssetBundleFromFile(abffr);
                return false; // 阻止原始方法执行
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
                return false; // 阻止原始方法执行
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
                return false; // 阻止原始方法执行
            }
            return false;
        }
    }

}
