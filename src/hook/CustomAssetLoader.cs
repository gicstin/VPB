using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MeshVR;

namespace VPB
{
	public class CustomAssetLoader : MonoBehaviour
	{
		protected static CustomAssetLoader singleton;

		protected Dictionary<string, int> assetBundleReferenceCounts;

		protected Dictionary<string, AssetBundle> pathToAssetBundle;

		protected List<MeshVR.AssetLoader.AssetBundleFromFileRequest> assetBundleFromFileQueue;

		protected List<MeshVR.AssetLoader.SceneLoadIntoTransformRequest> sceneLoadIntoTransformQueue;

		protected IEnumerator LoadBundleFileAsync(MeshVR.AssetLoader.AssetBundleFromFileRequest abffr)
		{
			if (assetBundleReferenceCounts == null)
			{
				assetBundleReferenceCounts = new Dictionary<string, int>();
			}
			if (pathToAssetBundle == null)
			{
				pathToAssetBundle = new Dictionary<string, AssetBundle>();
			}
			string path = abffr.path;
			int cnt2;
			if (assetBundleReferenceCounts.TryGetValue(path, out cnt2))
			{
				cnt2++;
				assetBundleReferenceCounts.Remove(path);
				assetBundleReferenceCounts.Add(path, cnt2);
			}
			else
			{
				assetBundleReferenceCounts.Add(path, 1);
			}
			AssetBundle ab = null;
			if (!pathToAssetBundle.TryGetValue(path, out ab))
			{
				AssetBundleCreateRequest abcr2 = null;
				if (MVR.FileManagement.FileManager.IsFileInPackage(path))
				{
					var vfe = MVR.FileManagement.FileManager.GetVarFileEntry(path);
					if (vfe.Simulated)
					{
						string path2 = vfe.Package.Path + "\\" + vfe.InternalPath;
						abcr2 = AssetBundle.LoadFromFileAsync(path2);
					}
					else
					{
						byte[] assetbundleBytes = new byte[vfe.Size];
						yield return MVR.FileManagement.FileManager.ReadAllBytesCoroutine(vfe, assetbundleBytes);
						abcr2 = AssetBundle.LoadFromMemoryAsync(assetbundleBytes);
					}
				}
				else
				{
					abcr2 = AssetBundle.LoadFromFileAsync(path);
				}
				if (abcr2 != null)
				{
					yield return abcr2;
					if (!abcr2.assetBundle)
					{
						SuperController.LogError("Error during attempt to load assetbundle " + path + ". Not valid");
					}
					else
					{
						ab = abcr2.assetBundle;
						pathToAssetBundle.Add(path, abcr2.assetBundle);
					}
				}
			}
			abffr.assetBundle = ab;
			if (abffr.callback != null)
			{
				abffr.callback(abffr);
			}
		}

		public static void QueueLoadAssetBundleFromFile(MeshVR.AssetLoader.AssetBundleFromFileRequest abffr)
		{
			if (singleton != null)
			{
				//LogUtil.Log("CustomAssetLoader QueueLoadAssetBundleFromFile " + abffr.path);
				if (singleton.assetBundleFromFileQueue == null)
				{
					singleton.assetBundleFromFileQueue = new List<MeshVR.AssetLoader.AssetBundleFromFileRequest>();
				}
				singleton.assetBundleFromFileQueue.Add(abffr);
			}
		}

		protected IEnumerator AssetBundleFromFileQueueProcessor()
		{
			if (assetBundleFromFileQueue == null)
			{
				assetBundleFromFileQueue = new List<MeshVR.AssetLoader.AssetBundleFromFileRequest>();
			}
			while (true)
			{
				yield return null;
				if (assetBundleFromFileQueue.Count > 0)
				{
					MeshVR.AssetLoader.AssetBundleFromFileRequest abffr = assetBundleFromFileQueue[0];
					assetBundleFromFileQueue.RemoveAt(0);
					yield return LoadBundleFileAsync(abffr);
				}
			}
		}

		public static void DoneWithAssetBundleFromFile(string path)
		{
			//LogUtil.Log("CustomAssetLoader DoneWithAssetBundleFromFile " + path);
			int value;
			if (!(singleton != null) || singleton.assetBundleReferenceCounts == null || !singleton.assetBundleReferenceCounts.TryGetValue(path, out value))
			{
				return;
			}
			value--;
			if (value <= 0)
			{
				AssetBundle value2;
				if (singleton.pathToAssetBundle.TryGetValue(path, out value2))
				{
					Debug.Log("Unloading unused asset bundle " + path);
					value2.Unload(true);
					singleton.pathToAssetBundle.Remove(path);
				}
				singleton.assetBundleReferenceCounts.Remove(path);
			}
			else
			{
				singleton.assetBundleReferenceCounts.Remove(path);
				singleton.assetBundleReferenceCounts.Add(path, value);
			}
		}

		protected IEnumerator LoadSceneIntoTransformAsync(MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr)
		{
			AsyncOperation async = null;
			try
			{
				async = SceneManager.LoadSceneAsync(slr.scenePath, LoadSceneMode.Additive);
			}
			catch (Exception ex)
			{
				SuperController.LogError("Error during attempt to load scene: " + ex);
			}
			if (async == null)
			{
				yield break;
			}
			yield return async;
			Scene sc = SceneManager.GetSceneByPath(slr.scenePath);
			if (!sc.IsValid())
			{
				yield break;
			}
			if (slr.requestCancelled)
			{
				yield return SceneManager.UnloadSceneAsync(sc);
				yield break;
			}
			LightmapData[] newLightmapData = LightmapSettings.lightmaps;
			LightProbes lightProbes = LightmapSettings.lightProbes;
			if (GlobalLightingManager.singleton != null)
			{
				if (GlobalLightingManager.singleton.PushLightmapData(newLightmapData))
				{
					slr.lightmapData = newLightmapData;
					if (!slr.importLightmaps)
					{
						GlobalLightingManager.singleton.RemoveLightmapData(slr.lightmapData);
					}
				}
				else
				{
					slr.lightmapData = null;
				}
				slr.lightProbesHolder = GlobalLightingManager.singleton.PushLightProbes(lightProbes);
				if (slr.lightProbesHolder != null && !slr.importLightProbes)
				{
					GlobalLightingManager.singleton.RemoveLightProbesHolder(slr.lightProbesHolder);
				}
			}
			if (slr.transform != null)
			{
				GameObject[] rootGameObjects = sc.GetRootGameObjects();
				GameObject[] array = rootGameObjects;
				foreach (GameObject gameObject in array)
				{
					Vector3 localPosition = gameObject.transform.localPosition;
					Quaternion localRotation = gameObject.transform.localRotation;
					Vector3 localScale = gameObject.transform.localScale;
					SceneManager.MoveGameObjectToScene(gameObject, SceneManager.GetActiveScene());
					gameObject.transform.SetParent(slr.transform);
					gameObject.transform.localPosition = localPosition;
					gameObject.transform.localRotation = localRotation;
					gameObject.transform.localScale = localScale;
				}
			}
			yield return SceneManager.UnloadSceneAsync(sc);
			if (slr.requestCancelled)
			{
				if (slr.lightmapData != null && slr.importLightmaps)
				{
					GlobalLightingManager.singleton.RemoveLightmapData(slr.lightmapData);
					slr.lightmapData = null;
				}
				if (slr.lightProbesHolder != null && slr.importLightProbes)
				{
					GlobalLightingManager.singleton.RemoveLightProbesHolder(slr.lightProbesHolder);
					slr.lightProbesHolder = null;
				}
			}
			else if (slr.callback != null)
			{
				slr.callback(slr);
			}
		}

		public static void QueueLoadSceneIntoTransform(MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr)
		{

			if (singleton != null)
			{
				//LogUtil.Log("CustomAssetLoader QueueLoadSceneIntoTransform " + slr.scenePath);
				if (singleton.sceneLoadIntoTransformQueue == null)
				{
					singleton.sceneLoadIntoTransformQueue = new List<MeshVR.AssetLoader.SceneLoadIntoTransformRequest>();
				}
				singleton.sceneLoadIntoTransformQueue.Add(slr);
			}
		}

		protected IEnumerator SceneLoadIntoTransfromQueueProcessor()
		{
			if (sceneLoadIntoTransformQueue == null)
			{
				sceneLoadIntoTransformQueue = new List<MeshVR.AssetLoader.SceneLoadIntoTransformRequest>();
			}
			while (true)
			{
				yield return null;
				if (sceneLoadIntoTransformQueue.Count > 0)
				{
					MeshVR.AssetLoader.SceneLoadIntoTransformRequest slr = sceneLoadIntoTransformQueue[0];
					sceneLoadIntoTransformQueue.RemoveAt(0);
					yield return LoadSceneIntoTransformAsync(slr);
				}
			}
		}

		private void Awake()
		{
			singleton = this;
		}

		private void Start()
		{
			StartCoroutine(AssetBundleFromFileQueueProcessor());
			StartCoroutine(SceneLoadIntoTransfromQueueProcessor());
		}

        public static IEnumerator GetAssetBundleContent(string path, Action<List<string>> callback)
        {
            AssetBundle ab = null;
            bool shouldUnload = false;

            // 1. Check if already tracked in our cache
            if (singleton != null && singleton.pathToAssetBundle != null && singleton.pathToAssetBundle.TryGetValue(path, out ab))
            {
                shouldUnload = false;
            }

            // 2. If not in cache, try loading
            if (ab == null)
            {
                if (MVR.FileManagement.FileManager.IsFileInPackage(path))
                {
                    var vfe = MVR.FileManagement.FileManager.GetVarFileEntry(path);
                    if (vfe.Simulated)
                    {
                        string path2 = vfe.Package.Path + "\\" + vfe.InternalPath;
                        AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(path2);
                        yield return req;
                        ab = req.assetBundle;
                    }
                    else
                    {
                        byte[] assetbundleBytes = new byte[vfe.Size];
                        yield return MVR.FileManagement.FileManager.ReadAllBytesCoroutine(vfe, assetbundleBytes);
                        AssetBundleCreateRequest req = AssetBundle.LoadFromMemoryAsync(assetbundleBytes);
                        yield return req;
                        ab = req.assetBundle;
                    }
                }
                else
                {
                    AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(path);
                    yield return req;
                    ab = req.assetBundle;
                }
                
                if (ab != null) shouldUnload = true;
            }

            // 3. If still null, it might be loaded by VaM but not in our cache. 
            // AssetBundle.LoadFromFile returns null if already loaded.
            // Try to find it in all loaded bundles.
            if (ab == null)
            {
                // Heuristic: Check by file name
                string fileName = Path.GetFileNameWithoutExtension(path);
                var allBundles = AssetBundle.GetAllLoadedAssetBundles();
                foreach (var b in allBundles)
                {
                    if (b.name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        ab = b;
                        shouldUnload = false;
                        break;
                    }
                }
            }

            if (ab != null)
            {
                string[] names = ab.GetAllAssetNames();
                List<string> result = new List<string>(names);
                if (shouldUnload) ab.Unload(true);
                callback?.Invoke(result);
            }
            else
            {
                callback?.Invoke(null);
            }
        }

		private void OnDestroy()
		{
			if (pathToAssetBundle == null)
			{
				return;
			}
			foreach (AssetBundle value in pathToAssetBundle.Values)
			{
				value.Unload(true);
			}
		}
	}
}
