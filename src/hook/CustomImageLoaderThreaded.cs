using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace var_browser
{
	public class CustomImageLoaderThreaded : MonoBehaviour
	{
		public delegate void ImageLoaderCallback(QueuedImage qi);

		public class QueuedImage
		{
            public void Reset()
            {
                isThumbnail = false;
                imgPath = null;
                skipCache = false;
                forceReload = false;
                createMipMaps = false;
                compress = true;
                linear = false;
                processed = false;
                preprocessed = false;
                cancel = false;
                finished = false;
                isNormalMap = false;
                createAlphaFromGrayscale = false;
                createNormalFromBump = false;
                bumpStrength = 1f;
                invert = false;
                setSize = false;
                fillBackground = false;
                width = 0;
                height = 0;
                if (raw != null)
                {
                    ByteArrayPool.Return(raw);
                }
                raw = null;
                hadError = false;
                errorText = null;
                textureFormat = TextureFormat.RGBA32;
                tex = null;
                rawImageToLoad = null;
                useWebCache = false;
                webRequest = null;
                webRequestDone = false;
                webRequestHadError = false;
                webRequestData = null;
                callback = null;
                priority = 1000;
                insertionIndex = 0;
            }

            public int priority;
            public long insertionIndex;

			public bool isThumbnail;

			public string imgPath;

			public bool skipCache;

			public bool forceReload;

			public bool createMipMaps;

			public bool compress = true;

			public bool linear;

			public bool processed;

			public bool preprocessed;

			public bool cancel;

			public bool finished;

			public bool isNormalMap;

			public bool createAlphaFromGrayscale;

			public bool createNormalFromBump;

			public float bumpStrength = 1f;

			public bool invert;

			public bool setSize;

			public bool fillBackground;

			public int width;

			public int height;

			public byte[] raw;

			public bool hadError;

			public string errorText;

			public TextureFormat textureFormat;

			public Texture2D tex;

			public RawImage rawImageToLoad;

			public bool useWebCache;

			public UnityWebRequest webRequest;

			public bool webRequestDone;

			public bool webRequestHadError;

			public byte[] webRequestData;

			public ImageLoaderCallback callback;

			public string cacheSignature
			{
				get
				{
					string text = imgPath;
					if (compress)
					{
						text += ":C";
					}
					if (linear)
					{
						text += ":L";
					}
					if (isNormalMap)
					{
						text += ":N";
					}
					if (createAlphaFromGrayscale)
					{
						text += ":A";
					}
					if (createNormalFromBump)
					{
						text = text + ":BN" + bumpStrength;
					}
					if (invert)
					{
						text += ":I";
					}
					return text;
				}
			}

			protected string diskCacheSignature
			{
				get
				{
					string text = ((!setSize) ? string.Empty : (width + "_" + height));
					if (compress)
					{
						text += "_C";
					}
					if (linear)
					{
						text += "_L";
					}
					if (isNormalMap)
					{
						text += "_N";
					}
					if (createAlphaFromGrayscale)
					{
						text += "_A";
					}
					if (createNormalFromBump)
					{
						text = text + "_BN" + bumpStrength;
					}
					if (invert)
					{
						text += "_I";
					}
					return text;
				}
			}

			protected string GetDiskCachePath()
			{
				string result = null;
				FileEntry fileEntry = FileManager.GetFileEntry(imgPath);
				string textureCacheDir =MVR.FileManagement.CacheManager.GetTextureCacheDir();
				if (fileEntry != null && textureCacheDir != null)
				{
					string text = fileEntry.Size.ToString();
					string text2 = fileEntry.LastWriteTime.ToFileTime().ToString();
					string text3 = textureCacheDir + "/";
					string fileName = Path.GetFileName(imgPath);
					fileName = fileName.Replace('.', '_');
					result = text3 + fileName + "_" + text + "_" + text2 + "_" + diskCacheSignature + ".vamcache";
				}
				return result;
			}

			protected string GetWebCachePath()
			{
				string result = null;
				string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
				if (textureCacheDir != null)
				{
					string text = imgPath.Replace("https://", string.Empty);
					text = text.Replace("http://", string.Empty);
					text = text.Replace("/", "__");
					text = text.Replace("?", "_");
					string text2 = textureCacheDir + "/";
					result = text2 + text + "_" + diskCacheSignature + ".vamcache";
				}
				return result;
			}

			public bool WebCachePathExists()
			{
				string webCachePath = GetWebCachePath();
				if (webCachePath != null && FileManager.FileExists(webCachePath))
				{
					return true;
				}
				return false;
			}

			public void CreateTexture()
			{
				if (tex == null)
				{
					try
					{
						tex = new Texture2D(width, height, textureFormat, createMipMaps, linear);
					}
					catch (Exception ex)
					{
						LogUtil.LogError(imgPath + " " + ex);
					}
					tex.name = cacheSignature;
				}
			}

			protected void ReadMetaJson(string jsonString)
			{
				JSONNode jSONNode = JSON.Parse(jsonString);
				JSONClass asObject = jSONNode.AsObject;
				if (asObject != null)
				{
					if (asObject["width"] != null)
					{
						width = asObject["width"].AsInt;
					}
					if (asObject["height"] != null)
					{
						height = asObject["height"].AsInt;
					}
					if (asObject["format"] != null)
					{
						textureFormat = (TextureFormat)Enum.Parse(typeof(TextureFormat), asObject["format"]);
					}
				}
			}

			protected void ProcessFromStream(Stream st)
			{
				Bitmap bitmap = new Bitmap(st);
				SolidBrush solidBrush = new SolidBrush(System.Drawing.Color.White);
				bitmap.RotateFlip(RotateFlipType.Rotate180FlipX);
				if (!setSize)
				{
					width = bitmap.Width;
					height = bitmap.Height;
					if (compress)
					{
						int num = width / 4;
						if (num == 0)
						{
							num = 1;
						}
						width = num * 4;
						int num2 = height / 4;
						if (num2 == 0)
						{
							num2 = 1;
						}
						height = num2 * 4;
					}
				}
				int num3 = 3;
				textureFormat = TextureFormat.RGB24;
				PixelFormat format = PixelFormat.Format24bppRgb;
				if (createAlphaFromGrayscale || isNormalMap || createNormalFromBump || bitmap.PixelFormat == PixelFormat.Format32bppArgb)
				{
					textureFormat = TextureFormat.RGBA32;
					format = PixelFormat.Format32bppArgb;
					num3 = 4;
				}
				Bitmap bitmap2 = new Bitmap(width, height, format);
				BitmapData srcBitmapData = null;
				BitmapData dstBitmapData = null;
			int num8 = 0;
			try
				{
					srcBitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
					dstBitmapData = bitmap2.LockBits(new Rectangle(0, 0, bitmap2.Width, bitmap2.Height), ImageLockMode.WriteOnly, bitmap2.PixelFormat);
					
					byte[] srcData = new byte[srcBitmapData.Stride * srcBitmapData.Height];
					byte[] dstData = new byte[dstBitmapData.Stride * dstBitmapData.Height];
					Marshal.Copy(srcBitmapData.Scan0, srcData, 0, srcData.Length);
					
					int srcBpp = bitmap.PixelFormat == PixelFormat.Format32bppArgb ? 4 : 3;
					ImageProcessingOptimization.FastBitmapCopy(srcData, bitmap.Width, bitmap.Height, srcBitmapData.Stride, srcBpp,
						dstData, width, height, dstBitmapData.Stride, num3, setSize, fillBackground);
					
					Marshal.Copy(dstData, 0, dstBitmapData.Scan0, dstData.Length);
					
					int num7 = width * height;
					num8 = num7 * num3;
					int num9 = Mathf.CeilToInt((float)num8 * 1.5f);
					raw = ByteArrayPool.Rent(num9);
					Marshal.Copy(dstBitmapData.Scan0, raw, 0, num8);
				}
				finally
				{
					if (srcBitmapData != null) bitmap.UnlockBits(srcBitmapData);
					if (dstBitmapData != null) bitmap2.UnlockBits(dstBitmapData);
				}
				bool flag = isNormalMap && num3 == 4;
				for (int i = 0; i < num8; i += num3)
				{
					byte b = raw[i];
					raw[i] = raw[i + 2];
					raw[i + 2] = b;
					if (flag)
					{
						raw[i + 3] = byte.MaxValue;
					}
				}
				if (invert)
				{
					for (int j = 0; j < num8; j++)
					{
						int num10 = 255 - raw[j];
						raw[j] = (byte)num10;
					}
				}
				if (createAlphaFromGrayscale)
				{
					for (int k = 0; k < num8; k += 4)
					{
						int num11 = raw[k];
						int num12 = raw[k + 1];
						int num13 = raw[k + 2];
						int num14 = (num11 + num12 + num13) / 3;
						raw[k + 3] = (byte)num14;
					}
				}
				if (createNormalFromBump)
				{
					ImageProcessingOptimization.OptimizedNormalMapGeneration(raw, width, height, bumpStrength);

				}
				solidBrush.Dispose();
				bitmap.Dispose();
				bitmap2.Dispose();
			}

			public void Process()
			{
				if (processed)
				{
					return;
				}
				if (imgPath != null && imgPath != "NULL")
				{
					if (useWebCache)
					{
						string webCachePath = GetWebCachePath();
						try
						{
							string text = webCachePath + "meta";
							if (FileManager.FileExists(text))
							{
								string jsonString = FileManager.ReadAllText(text);
								ReadMetaJson(jsonString);
								using (Stream fs = new FileStream(webCachePath, FileMode.Open, FileAccess.Read))
								{
									int len = (int)fs.Length;
									raw = ByteArrayPool.Rent(len);
									int read = 0;
									while (read < len)
									{
										int r = fs.Read(raw, read, len - read);
										if (r == 0) break;
										read += r;
									}
								}
								preprocessed = true;
							}
							else
							{
								hadError = true;
								errorText = "Missing cache meta file " + text;
							}
						}
						catch (Exception ex)
						{
							LogUtil.LogError("Exception during cache file read " + ex);
							hadError = true;
							errorText = ex.ToString();
						}
					}
					else if (webRequest != null)
					{
						if (!webRequestDone)
						{
							return;
						}
						try
						{
							if (!webRequestHadError && webRequestData != null)
							{
								using (MemoryStream st = new MemoryStream(webRequestData))
								{
									ProcessFromStream(st);
								}
							}
						}
						catch (Exception ex2)
						{
							hadError = true;
							LogUtil.LogError("Exception " + ex2);
							errorText = ex2.ToString();
						}
					}
					else if (FileManager.FileExists(imgPath))
					{
						string diskCachePath = GetDiskCachePath();
						if (MVR.FileManagement.CacheManager.CachingEnabled && diskCachePath != null && FileManager.FileExists(diskCachePath))
						{
							try
							{
								string text2 = diskCachePath + "meta";
								if (FileManager.FileExists(text2))
								{
									string jsonString2 = FileManager.ReadAllText(text2);
									ReadMetaJson(jsonString2);
									using (Stream fs = new FileStream(diskCachePath, FileMode.Open, FileAccess.Read))
									{
										int len = (int)fs.Length;
										raw = ByteArrayPool.Rent(len);
										int read = 0;
										while (read < len)
										{
											int r = fs.Read(raw, read, len - read);
											if (r == 0) break;
											read += r;
										}
									}
									preprocessed = true;
								}
								else
								{
									hadError = true;
									errorText = "Missing cache meta file " + text2;
								}
							}
							catch (Exception ex3)
							{
								LogUtil.LogError("Exception during cache file read " + ex3);
								hadError = true;
								errorText = ex3.ToString();
							}
						}
						else
						{
							try
							{
								// Load image from a var package
								using (FileEntryStream fileEntryStream = FileManager.OpenStream(imgPath))
								{
									Stream stream = fileEntryStream.Stream;
									ProcessFromStream(stream);
								}
							}
							catch (Exception ex4)
							{
								hadError = true;
								LogUtil.LogError("Exception " + ex4 + " " + imgPath);
								errorText = ex4.ToString();
							}
						}
					}
					//else
					//{
					//	hadError = true;
					//	errorText = "Path " + imgPath + " is not valid";
					//}
				}
				else
				{
					finished = true;
				}
				processed = true;
			}

			protected bool IsPowerOfTwo(uint x)
			{
				return x != 0 && (x & (x - 1)) == 0;
			}

			public void Finish()
			{
				if (webRequest != null)
				{
					webRequest.Dispose();
					webRequestData = null;
					webRequest = null;
				}
				if (hadError || finished)
				{
					return;
				}
				bool flag = (!createMipMaps || !compress || (IsPowerOfTwo((uint)width) && IsPowerOfTwo((uint)height))) && compress;
				CreateTexture();
				if (preprocessed)
				{
					try
					{
						tex.LoadRawTextureData(raw);
					}
					catch
					{
						UnityEngine.Object.Destroy(tex);
						tex = null;
						createMipMaps = false;
						CreateTexture();
						tex.LoadRawTextureData(raw);
					}
					tex.Apply(false);
					if (compress && textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5)
					{
						tex.Compress(true);
					}
				}
				else if (tex.format == TextureFormat.DXT1 || tex.format == TextureFormat.DXT5)
				{
					Texture2D texture2D = new Texture2D(width, height, textureFormat, createMipMaps, linear);
					texture2D.LoadRawTextureData(raw);
					texture2D.Apply();
					texture2D.Compress(true);
					byte[] rawTextureData = texture2D.GetRawTextureData();
					tex.LoadRawTextureData(rawTextureData);
					tex.Apply();
					UnityEngine.Object.Destroy(texture2D);
				}
				else
				{
					tex.LoadRawTextureData(raw);
					tex.Apply();
					if (flag)
					{
						tex.Compress(true);
					}
					if (MVR.FileManagement.CacheManager.CachingEnabled)
					{
						string text = ((!Regex.IsMatch(imgPath, "^http")) ? GetDiskCachePath() : GetWebCachePath());
						if (text != null && !FileManager.FileExists(text))
						{
							try
							{
								JSONClass jSONClass = new JSONClass();
								jSONClass["type"] = "image";
								jSONClass["width"].AsInt = tex.width;
								jSONClass["height"].AsInt = tex.height;
								jSONClass["format"] = tex.format.ToString();
								string contents = jSONClass.ToString(string.Empty);
								byte[] rawTextureData2 = tex.GetRawTextureData();
								File.WriteAllText(text + "meta", contents);
								File.WriteAllBytes(text, rawTextureData2);
							}
							catch (Exception ex)
							{
								LogUtil.LogError("Exception during caching " + ex);
								hadError = true;
								errorText = "Exception during caching of " + imgPath + ": " + ex;
							}
						}
					}
				}
                if (raw != null)
                {
                    ByteArrayPool.Return(raw);
                    raw = null;
                }
				finished = true;
			}

			public void DoCallback()
			{
                if (rawImageToLoad != null)
				{
					rawImageToLoad.texture = tex;
				}
				if (callback != null)
				{
					callback(this);
					callback = null;
				}
			}
        }

		public static class QIPool
        {
            static Stack<QueuedImage> stack = new Stack<QueuedImage>();
            static object lockObj = new object();

            public static QueuedImage Get()
            {
                lock(lockObj)
                {
                    if (stack.Count > 0) return stack.Pop();
                }
                return new QueuedImage();
            }

            public static void Return(QueuedImage qi)
            {
                if (qi == null) return;
                qi.Reset();
                lock(lockObj)
                {
                    stack.Push(qi);
                }
            }
        }

		protected class ImageLoaderTaskInfo
		{
			public string name;

			public AutoResetEvent resetEvent;

			public Thread thread;

			public volatile bool working;

			public volatile bool kill;
		}

		public static var_browser.CustomImageLoaderThreaded singleton;

		public GameObject progressHUD;

		public Slider progressSlider;

		public Text progressText;

		protected ImageLoaderTaskInfo imageLoaderTask;

		protected bool _threadsRunning;

		protected Dictionary<string, Texture2D> thumbnailCache;

		protected Dictionary<string, Texture2D> textureCache;

		protected Dictionary<string, Texture2D> immediateTextureCache;

		protected Dictionary<Texture2D, bool> textureTrackedCache;

		protected Dictionary<Texture2D, int> textureUseCount;

		protected PriorityQueue<QueuedImage> queuedImages;

		protected int numRealQueuedImages;

		protected int progress;

		protected int progressMax;

		protected AsyncFlag loadFlag;

		protected void MTTask(object info)
		{
			ImageLoaderTaskInfo imageLoaderTaskInfo = (ImageLoaderTaskInfo)info;
			while (_threadsRunning)
			{
				imageLoaderTaskInfo.resetEvent.WaitOne(-1, true);
				if (imageLoaderTaskInfo.kill)
				{
					break;
				}
				ProcessImageQueueThreaded();
				imageLoaderTaskInfo.working = false;
			}
		}

		protected void StopThreads()
		{
			_threadsRunning = false;
			if (imageLoaderTask != null)
			{
				imageLoaderTask.kill = true;
				imageLoaderTask.resetEvent.Set();
				while (imageLoaderTask.thread.IsAlive)
				{
				}
				imageLoaderTask = null;
			}
		}

		protected void StartThreads()
		{
			if (!_threadsRunning)
			{
				_threadsRunning = true;
				imageLoaderTask = new ImageLoaderTaskInfo();
				imageLoaderTask.name = "ImageLoaderTask";
				imageLoaderTask.resetEvent = new AutoResetEvent(false);
				imageLoaderTask.thread = new Thread(MTTask);
				imageLoaderTask.thread.Priority = System.Threading.ThreadPriority.Normal;
				imageLoaderTask.thread.Start(imageLoaderTask);
			}
		}

		public bool RegisterTextureUse(Texture2D tex)
		{
			if (textureTrackedCache.ContainsKey(tex))
			{
				int value = 0;
				if (textureUseCount.TryGetValue(tex, out value))
				{
					textureUseCount.Remove(tex);
				}
				value++;
				textureUseCount.Add(tex, value);
				return true;
			}
			return false;
		}

		public bool DeregisterTextureUse(Texture2D tex)
		{
			int value = 0;
			if (textureUseCount.TryGetValue(tex, out value))
			{
				textureUseCount.Remove(tex);
				value--;
				if (value > 0)
				{
					textureUseCount.Add(tex, value);
				}
				else
				{
					textureUseCount.Remove(tex);
					textureCache.Remove(tex.name);
					textureTrackedCache.Remove(tex);
					UnityEngine.Object.Destroy(tex);
				}
				return true;
			}
			return false;
		}

		public void ReportOnTextures()
		{
			int num = 0;
			if (textureCache != null)
			{
				foreach (Texture2D value2 in textureCache.Values)
				{
					num++;
					int value = 0;
					if (textureUseCount.TryGetValue(value2, out value))
					{
						//SuperController.LogMessage("Texture " + value2.name + " is in use " + value + " times");
					}
				}
			}
			//SuperController.LogMessage("Using " + num + " textures");
		}

		public void PurgeAllTextures()
		{
			if (textureCache == null)
			{
				return;
			}
			foreach (Texture2D value in textureCache.Values)
			{
				UnityEngine.Object.Destroy(value);
			}
			textureUseCount.Clear();
			textureCache.Clear();
			textureTrackedCache.Clear();
		}

		public void PurgeAllImmediateTextures()
		{
			if (immediateTextureCache == null)
			{
				return;
			}
			foreach (Texture2D value in immediateTextureCache.Values)
			{
				UnityEngine.Object.Destroy(value);
			}
			immediateTextureCache.Clear();
		}

		public void ClearCacheThumbnail(string imgPath)
		{
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(imgPath, out value))
			{
				thumbnailCache.Remove(imgPath);
				UnityEngine.Object.Destroy(value);
			}
		}

		protected void ProcessImageQueueThreaded()
		{
			if (queuedImages != null && queuedImages.Count > 0)
			{
				QueuedImage value = queuedImages.Peek();
				value.Process();
			}
		}

		public Texture2D GetCachedThumbnail(string path)
		{
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(path, out value))
			{
				return value;
			}
			return null;
		}

        public void QueueImage(QueuedImage qi)
        {
            //if (ImageLoadingMgr.singleton.Request(qi))
            //    return;

            if (queuedImages != null)
            {
                qi.priority = SuperControllerHook.GetImagePriority(qi.imgPath);
                qi.insertionIndex = ++_insertionOrderCounter;
                queuedImages.Enqueue(qi);
            }
            numRealQueuedImages++;
            progressMax++;
        }

        public void QueueThumbnail(QueuedImage qi)
        {
            qi.isThumbnail = true;
            //if (ImageLoadingMgr.singleton.Request(qi))
            //    return;

            if (queuedImages != null)
            {
                qi.priority = 1000;
                qi.insertionIndex = ++_insertionOrderCounter;
                queuedImages.Enqueue(qi);
            }
        }

        public void QueueThumbnailImmediate(QueuedImage qi)
		{
			qi.isThumbnail = true;
			if (queuedImages != null)
			{
                qi.priority = -1;
                qi.insertionIndex = ++_insertionOrderCounter;
                queuedImages.Enqueue(qi);
			}
		}

		public void ProcessImageImmediate(QueuedImage qi)
		{
			Texture2D value;
			if (!qi.skipCache && immediateTextureCache != null && immediateTextureCache.TryGetValue(qi.cacheSignature, out value))
			{
				UseCachedTex(qi, value);
			}
			qi.Process();
			qi.Finish();
			if (!qi.skipCache && !immediateTextureCache.ContainsKey(qi.cacheSignature) && qi.tex != null)
			{
				immediateTextureCache.Add(qi.cacheSignature, qi.tex);
			}
		}

		protected void PostProcessImageQueue()
		{
			if (queuedImages == null || queuedImages.Count <= 0)
			{
				return;
			}
			QueuedImage value = queuedImages.Peek();
			if (value.processed)
			{
				queuedImages.Dequeue();
				if (!value.isThumbnail)
				{
					progress++;
					numRealQueuedImages--;
                    
                    // Log stats every 50 images so we can see it working during heavy loads
                    if (progress % 50 == 0 && ByteArrayPool.TotalRented > 0)
                    {
                        LogUtil.Log(ByteArrayPool.GetStatus());
                    }

					if (numRealQueuedImages == 0)
					{
						progress = 0;
						progressMax = 0;
						if (progressHUD != null)
						{
							progressHUD.SetActive(false);
						}
                        if(ByteArrayPool.TotalRented > 0)
                            LogUtil.Log(ByteArrayPool.GetStatus());
					}
					else
					{
						if (progressHUD != null)
						{
							progressHUD.SetActive(true);
						}
						if (progressSlider != null)
						{
							progressSlider.maxValue = progressMax;
							progressSlider.value = progress;
						}
					}
				}
				value.Finish();
				if (!value.skipCache && value.imgPath != null && value.imgPath != "NULL")
				{
					if (value.isThumbnail)
					{
						if (!thumbnailCache.ContainsKey(value.imgPath) && value.tex != null)
						{
							thumbnailCache.Add(value.imgPath, value.tex);
						}
					}
					else if (!textureCache.ContainsKey(value.cacheSignature) && value.tex != null)
					{
						textureCache.Add(value.cacheSignature, value.tex);
						textureTrackedCache.Add(value.tex, true);
					}
				}
				value.DoCallback();
                QIPool.Return(value);
			}
			if (numRealQueuedImages != 0)
			{
				if (loadFlag == null)
				{
					loadFlag = new AsyncFlag("ImageLoader");
					//SuperController.singleton.SetLoadingIconFlag(loadFlag);
				}
			}
			else if (loadFlag != null)
			{
				loadFlag.Raise();
				loadFlag = null;
			}
		}

		protected void UseCachedTex(QueuedImage qi, Texture2D tex)
		{
			qi.tex = tex;
			if (qi.forceReload)
			{
				qi.width = tex.width;
				qi.height = tex.height;
				qi.setSize = true;
				qi.fillBackground = false;
			}
			else
			{
				qi.processed = true;
				qi.finished = true;
			}
		}

		protected void RemoveCanceledImages()
		{
			if (queuedImages != null)
			{
				while (queuedImages.Count > 0 && queuedImages.Peek().cancel)
				{
                    QueuedImage qi = queuedImages.Peek();
					queuedImages.Dequeue();
                    QIPool.Return(qi);
				}
			}
		}

		protected void PreprocessImageQueue()
		{
			RemoveCanceledImages();
			if (queuedImages == null || queuedImages.Count <= 0)
			{
				return;
			}
			QueuedImage value = queuedImages.Peek();
			if (value == null)
			{
				return;
			}
			if (!value.skipCache && value.imgPath != null && value.imgPath != "NULL")
			{
				Texture2D value2;
				if (value.isThumbnail)
				{
					if (thumbnailCache != null && thumbnailCache.TryGetValue(value.imgPath, out value2))
					{
						if (value2 == null)
						{
							LogUtil.LogError("Trying to use cached texture at " + value.imgPath + " after it has been destroyed");
							thumbnailCache.Remove(value.imgPath);
						}
						else
						{
							UseCachedTex(value, value2);
						}
					}
				}
				else if (textureCache != null && textureCache.TryGetValue(value.cacheSignature, out value2))
				{
					if (value2 == null)
					{
						LogUtil.LogError("Trying to use cached texture at " + value.imgPath + " after it has been destroyed");
						textureCache.Remove(value.cacheSignature);
						textureTrackedCache.Remove(value2);
					}
					else
					{
						UseCachedTex(value, value2);
					}
				}
			}
			if (!value.processed && value.imgPath != null && Regex.IsMatch(value.imgPath, "^http"))
			{
				if (MVR.FileManagement.CacheManager.CachingEnabled && value.WebCachePathExists())
				{
					value.useWebCache = true;
				}
				else
				{
					if (value.webRequest == null)
					{
						value.webRequest = UnityWebRequest.Get(value.imgPath);
						value.webRequest.SendWebRequest();
					}
					if (value.webRequest.isDone)
					{
						if (!value.webRequest.isNetworkError)
						{
							if (value.webRequest.responseCode == 200)
							{
								value.webRequestData = value.webRequest.downloadHandler.data;
								value.webRequestDone = true;
							}
							else
							{
								value.webRequestHadError = true;
								value.webRequestDone = true;
								value.hadError = true;
								value.errorText = "Error " + value.webRequest.responseCode;
							}
						}
						else
						{
							value.webRequestHadError = true;
							value.webRequestDone = true;
							value.hadError = true;
							value.errorText = value.webRequest.error;
						}
					}
				}
			}
			if (!value.isThumbnail && progressText != null)
			{
				progressText.text = "[" + progress + "/" + progressMax + "] " + value.imgPath;
			}
		}

		private void Update()
		{
			StartThreads();
			if (!imageLoaderTask.working)
			{
				PostProcessImageQueue();
				if (queuedImages != null && queuedImages.Count > 0)
				{
					PreprocessImageQueue();
					imageLoaderTask.working = true;
					imageLoaderTask.resetEvent.Set();
				}
			}
		}

		public void OnDestroy()
		{
			if (Application.isPlaying)
			{
				StopThreads();
			}
			if (loadFlag != null)
			{
				loadFlag.Raise();
			}
			PurgeAllTextures();
			PurgeAllImmediateTextures();
		}

		protected void OnApplicationQuit()
		{
			if (Application.isPlaying)
			{
				StopThreads();
			}
		}

		private void Awake()
		{
			singleton = this;
            LogUtil.Log("CustomImageLoaderThreaded initialized. ByteArrayPool ready.");
			immediateTextureCache = new Dictionary<string, Texture2D>();
			textureCache = new Dictionary<string, Texture2D>();
			textureTrackedCache = new Dictionary<Texture2D, bool>();
			thumbnailCache = new Dictionary<string, Texture2D>();
			textureUseCount = new Dictionary<Texture2D, int>();
			queuedImages = new PriorityQueue<QueuedImage>((a, b) => {
                int p = a.priority - b.priority;
                if (p != 0) return p;
                return a.insertionIndex.CompareTo(b.insertionIndex);
            });
		}

        private static long _insertionOrderCounter = 0;

        public class PriorityQueue<T>
        {
            private List<T> data;
            private Comparison<T> comparison;

            public PriorityQueue(Comparison<T> comparison)
            {
                this.data = new List<T>();
                this.comparison = comparison;
            }

            public void Enqueue(T item)
            {
                data.Add(item);
                int ci = data.Count - 1; 
                while (ci > 0)
                {
                    int pi = (ci - 1) / 2;
                    if (comparison(data[ci], data[pi]) >= 0) break;
                    T tmp = data[ci]; data[ci] = data[pi]; data[pi] = tmp;
                    ci = pi;
                }
            }

            public T Dequeue()
            {
                int li = data.Count - 1;
                T frontItem = data[0];
                data[0] = data[li];
                data.RemoveAt(li);

                --li;
                int pi = 0;
                while (true)
                {
                    int ci = pi * 2 + 1;
                    if (ci > li) break;
                    int rc = ci + 1;
                    if (rc <= li && comparison(data[rc], data[ci]) < 0) ci = rc;
                    if (comparison(data[pi], data[ci]) <= 0) break;
                    T tmp = data[pi]; data[pi] = data[ci]; data[ci] = tmp;
                    pi = ci;
                }
                return frontItem;
            }

            public T Peek()
            {
                if (data.Count == 0) return default(T);
                return data[0];
            }

            public int Count
            {
                get { return data.Count; }
            }

            public void Clear()
            {
                data.Clear();
            }
        }
	}

}
