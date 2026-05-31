using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace VPB
{
	public class CustomImageLoaderThreaded : MonoBehaviour
	{
        // VAR pkg:/ thumbnail reads: dedicated IO thread + queue (concurrent OpenStream on workers wedged).

        private static readonly object _varThumbIoQueueLock = new object();
        private static readonly Queue<QueuedImage> _varThumbIoQueue = new Queue<QueuedImage>();
        private static Thread _varThumbIoThread;
        private static volatile bool _varThumbIoStop;
		public delegate void ImageLoaderCallback(QueuedImage qi);

		public class QueuedImage
		{
            // Diagnostics only (timestamps from main thread)
            public float debugEnqueueRealtime;
            public float debugDispatchRealtime;
            public float debugWorkStartRealtime;
            public volatile bool debugAbortIssued;
            public Thread debugWorkerThread;
            public volatile int debugStage;
            public volatile int debugStageAux;
            public volatile float debugStageRealtime;
            public volatile bool thumbVarIoPending;

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
				decodedFromFastPath = false;
                turboJpegScaleDenom = 1;
                thumbnailUnityDecodeOnly = false;
                loadedFromGalleryCache = false;
                loadedFromCache = false;
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
                rawLength = 0;
                needsDecoding = false;
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
                groupId = null;
                debugEnqueueRealtime = 0f;
                debugDispatchRealtime = 0f;
                debugWorkStartRealtime = 0f;
                debugAbortIssued = false;
                debugWorkerThread = null;
                debugStage = 0;
                debugStageAux = 0;
                debugStageRealtime = 0f;
                thumbVarIoPending = false;
                onDemandCacheBuild = false;
                onDemandDecodeFromSource = false;
                suppressNativeDiskWrite = false;
            }

            /// <summary>On-demand cache prewarm: use loader Process/Finish instead of a parallel Unity build path.</summary>
            public bool onDemandCacheBuild;

            /// <summary>When true with <see cref="onDemandCacheBuild"/>, skip gallery/VPB/VaM disk cache reads and decode from source file.</summary>
            public bool onDemandDecodeFromSource;

            /// <summary>When true with <see cref="onDemandCacheBuild"/>, do not write VaM <c>.vamcache</c> in <see cref="Finish"/> (zstd-only builds).</summary>
            public bool suppressNativeDiskWrite;

            public int priority;
            public long insertionIndex;
            public string groupId;

			public bool isThumbnail;

			public string imgPath;

			public bool skipCache;

			public bool forceReload;

			public bool createMipMaps;

			public bool compress = true;

			public bool linear;

			public bool processed;

			public bool preprocessed;
            public volatile bool working;
			/// <summary>TurboJPEG decode denominator (1,2,4,8); grid-derived thumbs use up to 4; non-thumbs ignore.</summary>
			public int turboJpegScaleDenom = 1;
			/// <summary>When true (hover preview), skip TurboJPEG and gallery pixel cache; decode JPEG via Unity <see cref="Texture2D.LoadImage"/>; separate RAM cache tier.</summary>
			public bool thumbnailUnityDecodeOnly;
			public bool loadedFromGalleryCache;
			public bool loadedFromCache;
			public bool loadedFromDownscaledCache;
            public bool decodedFromFastPath;
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

			public int rawLength;

			public bool needsDecoding;

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

			protected string GetVPBCachePath()
			{
                return TextureUtil.GetZstdCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, setSize ? width : 0, setSize ? height : 0, bumpStrength, SuperControllerHook.IsSimulationTexturePath(imgPath));
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

			public string ResolveDiskCachePath()
			{
				return GetDiskCachePath();
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

			/// <summary>Decode/file paths only supply base mip in <see cref="raw"/>; mip chain is built on <see cref="Texture2D.Apply"/>.</summary>
			bool ShouldAllocateMipChainForRawLoad()
			{
				if (!createMipMaps) return false;
				if (!preprocessed) return false;
				int len = rawLength > 0 ? rawLength : (raw != null ? raw.Length : 0);
				if (len <= 0) return false;
				int baseSize = TextureUtil.GetExpectedRawDataSize(width, height, textureFormat);
				if (baseSize <= 0) return true;
				return len > baseSize;
			}

			void LogLoadRawDiagnostics(string pathLabel, string errorMsg)
			{
				int len = rawLength > 0 ? rawLength : (raw != null ? raw.Length : -1);
				int expected = TextureUtil.GetExpectedRawDataSize(width, height, textureFormat);
				LogUtil.LogError(
					"[VPB] LoadRawTextureData failed (" + pathLabel + ") for " + imgPath
					+ ": " + errorMsg
					+ " | " + width + "x" + height + " fmt=" + textureFormat
					+ " rawLen=" + len + " expectedBase=" + expected
					+ " mips=" + createMipMaps + " mipAlloc=" + ShouldAllocateMipChainForRawLoad()
					+ " pre=" + preprocessed + " fast=" + decodedFromFastPath
					+ " onDemand=" + onDemandCacheBuild
					+ " N=" + isNormalMap + " C=" + compress);
			}

			public void CreateTexture()
			{
				if (tex == null)
				{
					if (width <= 0 || height <= 0)
					{
						return;
					}
					try
					{
						tex = new Texture2D(width, height, textureFormat, ShouldAllocateMipChainForRawLoad(), linear);
					}
					catch (Exception ex)
					{
						LogUtil.LogError(imgPath + " " + ex);
					}
					if (tex != null)
					{
						tex.name = cacheSignature;
					}
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
					try { loadedFromDownscaledCache = asObject["downscaled"].AsBool; }
					catch { loadedFromDownscaledCache = false; }
					try { createMipMaps = asObject["createMipMaps"].AsBool; }
					catch { }
					try
					{
						string mipStorage = asObject["mipStorage"];
						if (!string.IsNullOrEmpty(mipStorage)
							&& string.Equals(mipStorage, TextureUtil.MipStorageFull, StringComparison.OrdinalIgnoreCase))
						{
							createMipMaps = true;
						}
					}
					catch { }
				}
			}

			internal void ProcessFromStream(Stream st)
			{
				if (VaMCompatibleImageDecode.ShouldUseVaMDecode(isThumbnail, onDemandDecodeFromSource, imgPath))
				{
					try
					{
						try { debugStage = 22; debugStageAux = 1; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
						string decodeErr;
						int w, h;
						TextureFormat fmt;
						byte[] decoded;
						int decodedLen;
						if (VaMCompatibleImageDecode.TryDecodeFromStream(
							st, setSize, width, height, fillBackground, compress,
							isNormalMap, createAlphaFromGrayscale, createNormalFromBump, bumpStrength, invert,
							out w, out h, out fmt, out decoded, out decodedLen, out decodeErr))
						{
							width = w;
							height = h;
							textureFormat = fmt;
							rawLength = decodedLen;
							raw = ByteArrayPool.Rent(rawLength);
							Buffer.BlockCopy(decoded, 0, raw, 0, rawLength);
							needsDecoding = false;
							preprocessed = false;
							try { debugStage = 24; debugStageAux = rawLength; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
							return;
						}
						hadError = true;
						errorText = decodeErr ?? "VaM GDI+ decode failed";
						return;
					}
					catch (Exception exGdi)
					{
						hadError = true;
						errorText = exGdi.Message;
						return;
					}
				}

				try
				{
                    try { debugStage = 22; debugStageAux = 0; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
					byte[] buffer = new byte[16384];
					using (MemoryStream ms = new MemoryStream())
					{
						int read;
                        int loops = 0;
						while ((read = st.Read(buffer, 0, buffer.Length)) > 0)
						{
							ms.Write(buffer, 0, read);
                            if (isThumbnail)
                            {
                                loops++;
                                if ((loops & 0x3F) == 0)
                                {
                                    try { debugStage = 23; debugStageAux = (int)Mathf.Min(ms.Length, int.MaxValue); debugStageRealtime = Time.realtimeSinceStartup; } catch { }
                                }
                            }
						}
						byte[] bytes = ms.ToArray();
						rawLength = bytes.Length;
						raw = ByteArrayPool.Rent(rawLength);
						Buffer.BlockCopy(bytes, 0, raw, 0, rawLength);
						needsDecoding = true;
					}
                    try { debugStage = 24; debugStageAux = rawLength; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
				}
				catch (Exception ex)
				{
					LogUtil.LogError("Error reading image stream: " + ex);
					hadError = true;
					errorText = ex.Message;
				}
			}

			public void Process()
			{
				if (processed || cancel)
				{
                    processed = true;
					return;
				}
                // VAR thumb: IO thread already filled raw via OpenStream. FileExists(pkg:/...) can be false for same path
                // → hadError "not found" and Finish() returns before Decode — red question mark grid.
                if (isThumbnail && needsDecoding && raw != null && rawLength > 0 && !string.IsNullOrEmpty(imgPath)
                    && IsVarPackageVfsPath(imgPath))
                {
                    processed = true;
                    return;
                }
                if (imgPath != null && imgPath.StartsWith("http"))
                {
                    LogUtil.Log("[VPB] [Loader] Thread processing: " + imgPath);
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
						// Assign the queued image's field, not a local — downstream skip logic reads it to avoid duplicate disk-cache jobs.
						loadedFromGalleryCache = false;
						if (onDemandCacheBuild && onDemandDecodeFromSource)
						{
							try
							{
								using (FileEntryStream fileEntryStream = FileManager.OpenStream(imgPath))
								{
									ProcessFromStream(fileEntryStream.Stream);
								}
							}
							catch (Exception ex4)
							{
								hadError = true;
								LogUtil.LogError("Exception " + ex4 + " " + imgPath);
								errorText = ex4.ToString();
							}
						}
						else
						{
							if (isThumbnail && !skipCache && !thumbnailUnityDecodeOnly)
							{
	                            long lastWriteTime = 0;
	                            bool foundTime = false;
	                            
	                            if (GalleryThumbnailCache.Instance.IsPackagePath(imgPath))
	                            {
	                                lastWriteTime = 0;
	                                foundTime = true;
	                            }
	                            else
	                            {
	                                lastWriteTime = GetLooseThumbnailLastWriteTime(imgPath);
	                                foundTime = lastWriteTime != 0;
	                                if (!foundTime)
	                                {
	                                    try { foundTime = FileManager.FileExists(imgPath); } catch { foundTime = false; }
	                                }
	                            }

								if (foundTime)
								{
									int w, h;
									TextureFormat fmt;
									byte[] data;
									if (GalleryThumbnailCache.Instance.TryGetThumbnail(imgPath, lastWriteTime, out data, out w, out h, out fmt, turboJpegScaleDenom))
									{
										raw = data;
										width = w;
										height = h;
										textureFormat = fmt;
										preprocessed = true;
										loadedFromGalleryCache = true;
										loadedFromCache = true;
										loadedFromDownscaledCache = false;
									}
								}
							}

							if (!loadedFromGalleryCache && !preprocessed && !onDemandCacheBuild && ImageLoadingMgr.singleton != null)
							{
								byte[] cachedRaw;
								int cachedLen;
								int cw, ch;
								TextureFormat cfmt;
								bool cmips;
								if (ImageLoadingMgr.singleton.TryFillPreprocessedRawForProcess(
									imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, bumpStrength,
									setSize, width, height, createMipMaps,
									out cachedRaw, out cachedLen, out cw, out ch, out cfmt, out cmips))
								{
									raw = cachedRaw;
									rawLength = cachedLen;
									width = cw;
									height = ch;
									textureFormat = cfmt;
									createMipMaps = cmips;
									preprocessed = true;
									loadedFromCache = true;
								}
							}

							if (!preprocessed)
							{
								try
								{
	                                try { debugStage = 20; debugStageAux = 0; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
	                                bool isVarPath = false;
	                                try { isVarPath = imgPath != null && imgPath.IndexOf(":/", StringComparison.Ordinal) > 0; } catch { isVarPath = false; }

	                                if (isThumbnail && isVarPath)
	                                {
	                                    return;
	                                }
	                                else
	                                {
	                                    using (FileEntryStream fileEntryStream = FileManager.OpenStream(imgPath))
	                                    {
	                                        try { debugStage = 21; debugStageAux = 0; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
	                                        Stream stream = fileEntryStream.Stream;
	                                        ProcessFromStream(stream);
	                                    }
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
					}
					else
					{
						hadError = true;
						errorText = "Path " + imgPath + " not found via FileManager";
						// Log only for loose files, as VAR files might legitimately have missing thumbnails
						if (!imgPath.Contains(":/")) 
						{
							LogUtil.LogWarning("[VPB] Image not found: " + imgPath);
						}
					}
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

			public void Decode()
			{
				if (!needsDecoding || raw == null || rawLength == 0) return;

				try
				{
                    try { debugStage = 30; debugStageAux = rawLength; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
					bool isJpeg = TurboJpegNative.LooksLikeJpeg(raw, rawLength);
					bool useTurboJpeg = false;
					try
					{
						useTurboJpeg = TurboJpegNative.ShouldAttemptTurboDecode() && isJpeg;
					}
					catch { useTurboJpeg = false; }
					if (isThumbnail && thumbnailUnityDecodeOnly) useTurboJpeg = false;

					Texture2D turboTex = null;
					string turboErr = null;
					if (useTurboJpeg)
					{
						int turboDenom = (!isThumbnail || turboJpegScaleDenom <= 1) ? 1 : TurboJpegNative.NormalizeScaleDenom(turboJpegScaleDenom);
                        try { debugStage = 31; debugStageAux = turboDenom; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
						bool turboOk = TurboJpegNative.TryDecodeJpegToTexture2D(raw, rawLength, turboDenom, out turboTex, out turboErr);
                        try { debugStage = turboOk ? 32 : 33; debugStageAux = turboDenom; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
						if (turboOk)
						{
							TurboJpegStats.NoteFirstTurboSuccess();
							ByteArrayPool.Return(raw);
							raw = null;
							rawLength = 0;
							needsDecoding = false;
							tex = turboTex;
							width = turboTex.width;
							height = turboTex.height;
							textureFormat = turboTex.format;
							decodedFromFastPath = true;
							preprocessed = false;
							return;
						}
					}

					Texture2D tempTex = new Texture2D(2, 2);
					byte[] dataToLoad = raw;
					if (raw.Length != rawLength)
					{
						dataToLoad = new byte[rawLength];
						Buffer.BlockCopy(raw, 0, dataToLoad, 0, rawLength);
					}

                    try { debugStage = 34; debugStageAux = rawLength; debugStageRealtime = Time.realtimeSinceStartup; } catch { }
					bool loadImageOk = tempTex.LoadImage(dataToLoad);
                    try { debugStage = loadImageOk ? 35 : 36; debugStageAux = tempTex != null ? tempTex.width : 0; debugStageRealtime = Time.realtimeSinceStartup; } catch { }

					if (loadImageOk)
					{
						int origWidth = tempTex.width;
						int origHeight = tempTex.height;
                        int targetWidth = origWidth;
                        int targetHeight = origHeight;

						if (setSize)
						{
                            targetWidth = width;
                            targetHeight = height;
						}
                        else
                        {
                            if (compress)
                            {
                                targetWidth = (origWidth / 4) * 4;
                                if (targetWidth == 0) targetWidth = 4;
                                targetHeight = (origHeight / 4) * 4;
                                if (targetHeight == 0) targetHeight = 4;
                            }
                            width = targetWidth;
                            height = targetHeight;
                        }

                        // Fast path: if no resizing and no transforms, use texture directly
                        bool sizeMatch = (targetWidth == origWidth && targetHeight == origHeight);
                        bool noTransforms = !isNormalMap && !invert && !createAlphaFromGrayscale && !createNormalFromBump;
                        
                        if (sizeMatch && noTransforms)
                        {
                            tex = tempTex;
                            textureFormat = tex.format;
                            width = tex.width;
                            height = tex.height;
                            decodedFromFastPath = true;
                            
                            ByteArrayPool.Return(raw);
                            raw = null;
                            needsDecoding = false;
                            return;
                        }

                        Texture2D outputTex = tempTex;
                        bool destroyedTemp = false;

                        if (targetWidth != origWidth || targetHeight != origHeight)
                        {
                            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                            RenderTexture previous = RenderTexture.active;
                            RenderTexture.active = rt;
                            GL.Clear(false, true, fillBackground ? UnityEngine.Color.white : UnityEngine.Color.clear);
                            Graphics.Blit(tempTex, rt);
                            outputTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
                            outputTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                            outputTex.Apply();
                            RenderTexture.active = previous;
                            RenderTexture.ReleaseTemporary(rt);
                            destroyedTemp = true;
                        }

                        Color32[] pix = outputTex.GetPixels32();
                        int num3 = 4;
                        int num8 = targetWidth * targetHeight * num3;
                        
                        ByteArrayPool.Return(raw);
                        raw = ByteArrayPool.Rent(num8);
                        textureFormat = TextureFormat.RGBA32;

                        // Color32 is RGBA; copy channels directly into the raw byte buffer.
                        for (int i = 0; i < pix.Length; i++)
                        {
                            int idx = i * 4;
                            raw[idx] = pix[i].r;
                            raw[idx + 1] = pix[i].g;
                            raw[idx + 2] = pix[i].b;
                            raw[idx + 3] = pix[i].a;
                        }

                        // Apply transformations (Invert, Alpha, Normal)
                        ApplyTransformations(num8);

                        if (destroyedTemp) UnityEngine.Object.Destroy(tempTex);
                        UnityEngine.Object.Destroy(outputTex);
					}
                    else
                    {
                        hadError = true;
                        errorText = "LoadImage failed";
                    }
				}
				catch (Exception ex)
				{
					LogUtil.LogError("Error in Decode: " + ex);
					hadError = true;
					errorText = ex.Message;
				}
				needsDecoding = false;
			}

            protected void ApplyTransformations(int num8)
            {
                if (isNormalMap)
                {
                    for (int i = 0; i < num8; i += 4)
                    {
                        raw[i + 3] = byte.MaxValue;
                    }
                }

                if (invert)
                {
                    for (int j = 0; j < num8; j++)
                    {
                        raw[j] = (byte)(255 - raw[j]);
                    }
                }

                if (createAlphaFromGrayscale)
                {
                    bool hasExistingAlpha = false;
                    for (int k = 3; k < num8; k += 4)
                    {
                        if (raw[k] != byte.MaxValue)
                        {
                            hasExistingAlpha = true;
                            break;
                        }
                    }

                    if (!hasExistingAlpha)
                    {
                        for (int k = 0; k < num8; k += 4)
                        {
                            int avg = (raw[k] + raw[k + 1] + raw[k + 2]) / 3;
                            raw[k + 3] = (byte)avg;
                        }
                    }

                    bool enforceDxt5 = compress && imgPath != null && imgPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                    if (enforceDxt5)
                    {
                        raw[3] = 128;
                    }
                }

                if (createNormalFromBump)
                {
                    // Sobel convolution on a grayscale heightmap; output is a tangent-space normal in RGBA layout.
                    byte[] array = new byte[num8]; // Not pooled because it's temporary here
                    float[][] hMap = new float[height][];
                    for (int l = 0; l < height; l++)
                    {
                        hMap[l] = new float[width];
                        for (int m = 0; m < width; m++)
                        {
                            int idx = (l * width + m) * 4;
                            hMap[l][m] = (raw[idx] + raw[idx + 1] + raw[idx + 2]) / 768f;
                        }
                    }

                    Vector3 v = default(Vector3);
                    for (int n = 0; n < height; n++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            float h21 = 0.5f, h22 = 0.5f, h23 = 0.5f, h24 = 0.5f, h25 = 0.5f, h26 = 0.5f, h27 = 0.5f, h28 = 0.5f;
                            int xm1 = x - 1, xp1 = x + 1, yp1 = n + 1, ym1 = n - 1;

                            if (yp1 < height && xm1 >= 0) h21 = hMap[yp1][xm1];
                            if (xm1 >= 0) h22 = hMap[n][xm1];
                            if (ym1 >= 0 && xm1 >= 0) h23 = hMap[ym1][xm1];
                            if (yp1 < height) h24 = hMap[yp1][x];
                            if (ym1 >= 0) h25 = hMap[ym1][x];
                            if (yp1 < height && xp1 < width) h26 = hMap[yp1][xp1];
                            if (xp1 < width) h27 = hMap[n][xp1];
                            if (ym1 >= 0 && xp1 < width) h28 = hMap[ym1][xp1];

                            float nx = h26 + 2f * h27 + h28 - h21 - 2f * h22 - h23;
                            float ny = h23 + 2f * h25 + h28 - h21 - 2f * h24 - h26;
                            v.x = nx * bumpStrength;
                            v.y = ny * bumpStrength;
                            v.z = 1f;
                            v.Normalize();
                            
                            int idx = (n * width + x) * 4;
                            raw[idx] = (byte)((v.x * 0.5f + 0.5f) * 255f);
                            raw[idx + 1] = (byte)((v.y * 0.5f + 0.5f) * 255f);
                            raw[idx + 2] = (byte)((v.z * 0.5f + 0.5f) * 255f);
                            raw[idx + 3] = byte.MaxValue;
                        }
                    }
                }
            }

			private void TrySaveGalleryThumbnail(ref bool savedToGalleryCache)
			{
				if (!isThumbnail || GalleryThumbnailCache.Instance == null || Regex.IsMatch(imgPath, "^http") || loadedFromGalleryCache || tex == null)
				{
					return;
				}

				try
				{
					if (!FileManager.FileExists(imgPath)) return;

					long lastWriteTime = 0;
					bool foundTime = false;
					if (GalleryThumbnailCache.Instance.IsPackagePath(imgPath))
					{
						lastWriteTime = 0;
						foundTime = true;
					}
					else
					{
						lastWriteTime = GetLooseThumbnailLastWriteTime(imgPath);
						foundTime = lastWriteTime != 0;
					}

					if (!foundTime || tex.width > 512 || tex.height > 512) return;

					byte[] rawTextureData2 = tex.GetRawTextureData();
					GalleryThumbnailCache.Instance.SaveThumbnail(imgPath, rawTextureData2, rawTextureData2.Length, tex.width, tex.height, tex.format, lastWriteTime, turboJpegScaleDenom);
					savedToGalleryCache = true;
					loadedFromGalleryCache = true;
				}
				catch (Exception ex)
				{
					LogUtil.LogError("Exception during gallery caching " + ex);
				}
			}

			private void TryWriteVaMDiskCache(bool savedToGalleryCache)
			{
				if (tex == null || suppressNativeDiskWrite || savedToGalleryCache || !MVR.FileManagement.CacheManager.CachingEnabled)
				{
					return;
				}

				// On-demand prewarm builds zstd directly; skip interim native while job runs.
				if (!onDemandCacheBuild)
				{
					try
					{
						if (NativeTextureOnDemandCache.IsOnDemandBusy
							&& Settings.Instance != null
							&& Settings.Instance.EnableZstdCompression != null
							&& Settings.Instance.EnableZstdCompression.Value)
						{
							return;
						}
					}
					catch { }
				}

				string text = ((!Regex.IsMatch(imgPath, "^http")) ? GetDiskCachePath() : GetWebCachePath());
				if (text == null || FileManager.FileExists(text))
				{
					return;
				}

				try
				{
					string cacheDir = null;
					try { cacheDir = Path.GetDirectoryName(text); } catch { cacheDir = null; }
					if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
					{
						Directory.CreateDirectory(cacheDir);
					}

					JSONClass jSONClass = new JSONClass();
					jSONClass["type"] = "image";
					jSONClass["width"] = tex.width.ToString();
					jSONClass["height"] = tex.height.ToString();
					jSONClass["format"] = tex.format.ToString();
					byte[] rawTextureData2 = tex.GetRawTextureData();
					TextureUtil.WriteMipFieldsToMeta(jSONClass, tex.width, tex.height, tex.format, rawTextureData2 != null ? rawTextureData2.Length : 0, createMipMaps);
					string contents = VPB.src.util.JsonSerializationUtil.Serialize(jSONClass, 1024);
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

			public void Finish()
			{
				if (needsDecoding) Decode();
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

				bool canCompress = compress && !loadedFromGalleryCache && width > 0 && height > 0 && IsPowerOfTwo((uint)width) && IsPowerOfTwo((uint)height);
				
                if (!decodedFromFastPath)
                {
    				CreateTexture();
                    if (tex == null)
                    {
                        LogUtil.LogError("Failed to create texture in Finish() for " + imgPath);
                        hadError = true;
                        return;
                    }
                }

				if (preprocessed)
				{
					try
					{
                        TextureUtil.SafeLoadRawTextureData(tex, raw, width, height, textureFormat);
					}
					catch (Exception ex)
					{
                        LogLoadRawDiagnostics("preprocessed", ex.Message);
						UnityEngine.Object.Destroy(tex);
						tex = null;
						createMipMaps = false;
						CreateTexture();
                        if (tex != null)
                        {
                            try { TextureUtil.SafeLoadRawTextureData(tex, raw, width, height, textureFormat); }
                            catch (Exception ex2) { LogUtil.LogError($"[VPB] LoadRawTextureData retry failed for {imgPath}: {ex2.Message}"); }
                        }
					}
					bool isSimTexture = SuperControllerHook.IsSimulationTexturePath(imgPath);
					bool keepReadable = onDemandCacheBuild || isThumbnail;
					bool genMipsOnApply = createMipMaps && !ShouldAllocateMipChainForRawLoad();
					tex.Apply(genMipsOnApply, !keepReadable && !isSimTexture);
					if (canCompress && textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5)
					{
						try { tex.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed " + ex + " path=" + imgPath); canCompress = false; }
					}
				}
                else if (decodedFromFastPath)
                {
					bool isSimTexture = SuperControllerHook.IsSimulationTexturePath(imgPath);
					// Thumbnails (TurboJPEG RGB24): keep CPU copy readable — non-readable breaks Blit/ReadPixels
					// in gallery disk-cache pipeline and some UI paths; full-size loads keep original policy.
					bool keepReadable = onDemandCacheBuild || isThumbnail;
					bool makeNoLongerReadable = !keepReadable && !canCompress && !isSimTexture;
                    tex.Apply(createMipMaps, makeNoLongerReadable);
                    if (canCompress && tex.format != TextureFormat.DXT1 && tex.format != TextureFormat.DXT5)
                    {
                        try { tex.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed " + ex + " path=" + imgPath); canCompress = false; }
                    }
                }
				else if (tex.format == TextureFormat.DXT1 || tex.format == TextureFormat.DXT5)
				{
                    try
                    {
					    Texture2D texture2D = new Texture2D(width, height, textureFormat, createMipMaps, linear);
					    TextureUtil.SafeLoadRawTextureData(texture2D, raw, width, height, textureFormat);
					    texture2D.Apply(false, false);
					    if (canCompress)
					    {
						    try { texture2D.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed (dxt) " + ex + " path=" + imgPath); canCompress = false; }
					    }
					    byte[] rawTextureData = texture2D.GetRawTextureData();
					    tex.LoadRawTextureData(rawTextureData);
						bool isSimTexture = SuperControllerHook.IsSimulationTexturePath(imgPath);
					    tex.Apply(false, !isSimTexture && !onDemandCacheBuild);
					    UnityEngine.Object.Destroy(texture2D);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[VPB] LoadRawTextureData failed (DXT path) for {imgPath}: {ex.Message}");
                    }
				}
				else
				{
                    try
                    {
					    TextureUtil.SafeLoadRawTextureData(tex, raw, width, height, textureFormat);
						bool isSimTexture = SuperControllerHook.IsSimulationTexturePath(imgPath);
						bool keepReadable = onDemandCacheBuild || isThumbnail;
						bool genMipsOnApply = createMipMaps && !ShouldAllocateMipChainForRawLoad();
					    tex.Apply(genMipsOnApply, !keepReadable && !isSimTexture);
					    if (canCompress)
					    {
						    try { tex.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed " + ex + " path=" + imgPath); canCompress = false; }
					    }
                    }
                    catch (Exception ex)
                    {
                        LogLoadRawDiagnostics("standard", ex.Message);
                    }
				}

				if (tex != null && !hadError)
				{
					bool savedToGalleryCache = loadedFromCache || loadedFromGalleryCache;
					TrySaveGalleryThumbnail(ref savedToGalleryCache);
					TryWriteVaMDiskCache(savedToGalleryCache);
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

		public class QueuedImagePool
        {
            Stack<QueuedImage> stack = new Stack<QueuedImage>();
            object lockObj = new object();

            public QueuedImage Get()
            {
                lock(lockObj)
                {
                    if (stack.Count > 0) return stack.Pop();
                }
                return new QueuedImage();
            }

            public void Return(QueuedImage qi)
            {
                if (qi == null) return;
                qi.Reset();
                lock(lockObj)
                {
                    stack.Push(qi);
                }
            }
        }

        protected QueuedImagePool pool = new QueuedImagePool();

        public QueuedImage GetQI()
        {
            return pool.Get();
        }

        public void ReturnQueuedImage(QueuedImage qi)
        {
            pool.Return(qi);
        }

        public struct OnDemandCacheBuildResult
        {
            public bool success;
            public string error;
            public byte[] payload;
            public int width;
            public int height;
            public TextureFormat format;
            public bool wroteNativeCache;
            public bool didDownscale;
            public int sourceWidth;
            public int sourceHeight;
        }

        /// <summary>Main-thread compress + optional native disk write from pre-decoded RGB(A) produced on a worker thread.</summary>
        public static OnDemandCacheBuildResult BuildOnDemandFinishFromDecoded(
            string imgUidPath,
            byte[] decodedRaw,
            int decodedRawLength,
            int width,
            int height,
            TextureFormat textureFormat,
            bool compress,
            bool linear,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            bool invert,
            float bumpStrength,
            bool suppressNativeDiskWrite)
        {
            var result = new OnDemandCacheBuildResult();
            CustomImageLoaderThreaded loader = singleton;
            if (loader == null)
            {
                result.error = "ImageLoader singleton missing";
                return result;
            }

            if (string.IsNullOrEmpty(imgUidPath) || decodedRaw == null || decodedRawLength <= 0 || width <= 0 || height <= 0)
            {
                result.error = "Invalid decoded payload";
                return result;
            }

            QueuedImage qi = loader.GetQI();
            try
            {
                qi.onDemandCacheBuild = true;
                qi.onDemandDecodeFromSource = true;
                qi.suppressNativeDiskWrite = suppressNativeDiskWrite;
                qi.isThumbnail = false;
                qi.skipCache = true;
                qi.imgPath = imgUidPath;
                qi.compress = compress;
                qi.linear = linear;
                qi.isNormalMap = isNormalMap;
                qi.createAlphaFromGrayscale = createAlphaFromGrayscale;
                qi.createNormalFromBump = createNormalFromBump;
                qi.invert = invert;
                qi.bumpStrength = bumpStrength;
                qi.createMipMaps = true;
                qi.setSize = false;
                qi.processed = true;
                qi.needsDecoding = false;
                qi.preprocessed = false;
                qi.width = width;
                qi.height = height;
                qi.textureFormat = textureFormat;
                qi.rawLength = decodedRawLength;
                qi.raw = ByteArrayPool.Rent(decodedRawLength);
                Buffer.BlockCopy(decodedRaw, 0, qi.raw, 0, decodedRawLength);

                qi.Finish();

                if (qi.hadError)
                {
                    result.error = qi.errorText;
                    return result;
                }

                if (qi.tex == null)
                {
                    result.error = "Texture null after Finish";
                    return result;
                }

                result.width = qi.tex.width;
                result.height = qi.tex.height;
                result.format = qi.tex.format;
                result.sourceWidth = result.width;
                result.sourceHeight = result.height;

                try
                {
                    result.payload = qi.tex.GetRawTextureData();
                }
                catch (Exception ex)
                {
                    result.error = "GetRawTextureData: " + ex.Message;
                    return result;
                }

                if (!suppressNativeDiskWrite)
                {
                    string nativePathAfter = qi.ResolveDiskCachePath();
                    if (!string.IsNullOrEmpty(nativePathAfter))
                    {
                        result.wroteNativeCache = File.Exists(nativePathAfter) && File.Exists(nativePathAfter + "meta");
                    }
                }

                result.success = result.payload != null && result.payload.Length > 0;

                if (result.success && qi.tex != null)
                {
                    try { loader.TryRegisterOnDemandBuiltTexture(qi); } catch { }
                }

                return result;
            }
            finally
            {
                if (qi != null)
                {
                    if (qi.raw != null)
                    {
                        try { ByteArrayPool.Return(qi.raw); } catch { }
                        qi.raw = null;
                    }
                    if (qi.tex != null)
                    {
                        UnityEngine.Object.Destroy(qi.tex);
                        qi.tex = null;
                    }
                    loader.ReturnQueuedImage(qi);
                }
            }
        }

        /// <summary>Build DXT payload via the same Process/Finish path used at runtime (VaM-compatible .vamcache write when enabled).</summary>
        public static OnDemandCacheBuildResult BuildOnDemandViaLoader(
            string imgUidPath,
            bool compress,
            bool linear,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            bool invert,
            float bumpStrength,
            int targetWidth,
            int targetHeight,
            bool decodeFromSourceOnly,
            bool suppressNativeDiskWrite)
        {
            var result = new OnDemandCacheBuildResult();
            CustomImageLoaderThreaded loader = singleton;
            if (loader == null)
            {
                result.error = "ImageLoader singleton missing";
                return result;
            }

            if (string.IsNullOrEmpty(imgUidPath))
            {
                result.error = "Empty image path";
                return result;
            }

            QueuedImage qi = loader.GetQI();
            string nativePathBefore = null;
            bool nativeExisted = false;
            try
            {
                qi.onDemandCacheBuild = true;
                qi.onDemandDecodeFromSource = decodeFromSourceOnly;
                qi.suppressNativeDiskWrite = suppressNativeDiskWrite;
                qi.isThumbnail = false;
                qi.skipCache = true;
                qi.imgPath = imgUidPath;
                qi.compress = compress;
                qi.linear = linear;
                qi.isNormalMap = isNormalMap;
                qi.createAlphaFromGrayscale = createAlphaFromGrayscale;
                qi.createNormalFromBump = createNormalFromBump;
                qi.invert = invert;
                qi.bumpStrength = bumpStrength;
                qi.createMipMaps = true;
                qi.setSize = targetWidth > 0 && targetHeight > 0;
                if (qi.setSize)
                {
                    qi.width = targetWidth;
                    qi.height = targetHeight;
                }

                nativePathBefore = qi.ResolveDiskCachePath();
                if (!string.IsNullOrEmpty(nativePathBefore))
                {
                    nativeExisted = File.Exists(nativePathBefore) && File.Exists(nativePathBefore + "meta");
                }

                qi.Process();
                qi.Finish();

                if (qi.hadError)
                {
                    result.error = qi.errorText;
                    try
                    {
                        int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                        if (lvl >= 1)
                        {
                            LogUtil.LogWarning(
                                "[VPB OnDemand] Loader hadError path=" + imgUidPath
                                + " err=" + (qi.errorText ?? string.Empty)
                                + " N=" + isNormalMap + " C=" + compress + " L=" + linear
                                + " decodeSrc=" + decodeFromSourceOnly + " noNativeWrite=" + suppressNativeDiskWrite);
                        }
                    }
                    catch { }
                    return result;
                }

                if (qi.tex == null)
                {
                    result.error = "Texture null after Finish";
                    return result;
                }

                result.width = qi.tex.width;
                result.height = qi.tex.height;
                result.format = qi.tex.format;
                result.sourceWidth = result.width;
                result.sourceHeight = result.height;
                if (qi.setSize)
                {
                    result.didDownscale = true;
                }

                try
                {
                    result.payload = qi.tex.GetRawTextureData();
                }
                catch (Exception ex)
                {
                    result.error = "GetRawTextureData: " + ex.Message;
                    try
                    {
                        int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                        if (lvl >= 1)
                        {
                            LogUtil.LogWarning(
                                "[VPB OnDemand] GetRawTextureData failed path=" + imgUidPath
                                + " fmt=" + qi.tex.format + " " + qi.tex.width + "x" + qi.tex.height
                                + " onDemandBuild=" + qi.onDemandCacheBuild + " err=" + ex.Message);
                        }
                    }
                    catch { }
                    return result;
                }

                if (!suppressNativeDiskWrite)
                {
                    string nativePathAfter = qi.ResolveDiskCachePath();
                    if (!string.IsNullOrEmpty(nativePathAfter))
                    {
                        result.wroteNativeCache = File.Exists(nativePathAfter) && File.Exists(nativePathAfter + "meta");
                    }
                }

                result.success = result.payload != null && result.payload.Length > 0;

                if (result.success && qi.tex != null && targetWidth == 0 && targetHeight == 0)
                {
                    try { loader.TryRegisterOnDemandBuiltTexture(qi); } catch { }
                }

                return result;
            }
            finally
            {
                if (qi != null)
                {
                    if (qi.tex != null)
                    {
                        UnityEngine.Object.Destroy(qi.tex);
                        qi.tex = null;
                    }
                    loader.ReturnQueuedImage(qi);
                }
            }
        }

        /// <summary>Mirror stock PostProcessImageQueue RAM cache after on-demand full-size build.</summary>
        public void TryRegisterOnDemandBuiltTexture(QueuedImage qi)
        {
            if (qi == null || qi.hadError || qi.tex == null || qi.isThumbnail) return;
            if (qi.setSize && (qi.width > 0 || qi.height > 0)) return;
            if (string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;

            if (textureCache == null)
            {
                textureCache = new Dictionary<string, Texture2D>();
                textureTrackedCache = new Dictionary<Texture2D, bool>();
            }

            string sig = qi.cacheSignature;
            if (textureCache.ContainsKey(sig)) return;

            Texture2D dup = null;
            try
            {
                byte[] raw = qi.tex.GetRawTextureData();
                dup = new Texture2D(qi.tex.width, qi.tex.height, qi.tex.format, qi.tex.mipmapCount > 1, qi.linear);
                dup.name = sig;
                dup.LoadRawTextureData(raw);
                dup.Apply(false, false);
            }
            catch
            {
                if (dup != null)
                {
                    UnityEngine.Object.Destroy(dup);
                    dup = null;
                }
            }

            if (dup == null) return;

            textureCache.Add(sig, dup);
            textureTrackedCache.Add(dup, true);
        }

        /// <summary>In-RAM thumbnail LRU key; matches disk <see cref="GalleryThumbnailCache.GetThumbnailCacheKey"/> tier suffix; <c>|uj</c> = Unity-only decode (hover preview).</summary>
        public static string ThumbnailMemoryCacheKey(string path, int turboJpegScaleDenom, bool unityDecodeOnly = false)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string k = turboJpegScaleDenom <= 1 ? path : path + "|tj" + turboJpegScaleDenom;
            if (unityDecodeOnly) return k + "|uj";
            return k;
        }

        /// <summary>Live disk mtime for loose thumbs; FileEntry can stay stale after overwrite save.</summary>
        private static long GetLooseThumbnailLastWriteTime(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return 0;

            try
            {
                string full = FileManager.GetFullPath(imgPath.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(full) && File.Exists(full))
                    return File.GetLastWriteTimeUtc(full).ToFileTimeUtc();
            }
            catch { }

            try
            {
                FileEntry fe = FileManager.GetFileEntry(imgPath);
                if (fe != null) return fe.LastWriteTime.ToFileTime();
            }
            catch { }

            return 0;
        }

        private static int ClampInt(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static bool TurboJpegConcurrencyReduction()
        {
            try
            {
                return TurboJpegNative.ShouldAttemptTurboDecode();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Auto caps scale with <see cref="Environment.ProcessorCount"/>; TurboJPEG-on uses lower caps. BepInEx <c>MaxLoaderThreads</c>: use 0 for auto.</summary>
        public static int GetEffectiveMaxLoaderThreads()
        {
            try
            {
                var st = Settings.Instance;
                if (st != null && st.MaxLoaderThreads != null && st.MaxLoaderThreads.Value > 0)
                    return ClampInt(st.MaxLoaderThreads.Value, 1, 64);
                int p = Environment.ProcessorCount;
                if (p < 1) p = 1;
                if (TurboJpegConcurrencyReduction())
                    return ClampInt(p / 3, 3, 8);
                return ClampInt(p / 2, 4, 12);
            }
            catch
            {
                return 12;
            }
        }

        /// <summary>BepInEx <c>MaxThumbnailThreads</c>: use 0 for auto.</summary>
        public static int GetEffectiveMaxThumbnailThreads()
        {
            try
            {
                var st = Settings.Instance;
                if (st != null && st.MaxThumbnailThreads != null && st.MaxThumbnailThreads.Value > 0)
                    return ClampInt(st.MaxThumbnailThreads.Value, 1, 64);
                int p = Environment.ProcessorCount;
                if (p < 1) p = 1;
                if (TurboJpegConcurrencyReduction())
                    return ClampInt(p / 4, 2, 6);
                return ClampInt(p / 3, 3, 8);
            }
            catch
            {
                return 8;
            }
        }

        private static bool IsVarPackageVfsPath(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return false;
            return imgPath.IndexOf(":/", StringComparison.Ordinal) > 0;
        }

        private static void EnsureVarThumbIoWorker()
        {
            if (_varThumbIoThread != null && _varThumbIoThread.IsAlive) return;
            lock (typeof(CustomImageLoaderThreaded))
            {
                if (_varThumbIoThread != null && _varThumbIoThread.IsAlive) return;
                _varThumbIoStop = false;
                _varThumbIoThread = new Thread(VarThumbIoLoop)
                {
                    IsBackground = true,
                    Name = "VPB-VarThumbIo"
                };
                _varThumbIoThread.Start();
            }
        }

        private static void VarThumbIoLoop()
        {
            while (!_varThumbIoStop)
            {
                QueuedImage qi = null;
                try
                {
                    lock (_varThumbIoQueueLock)
                    {
                        if (_varThumbIoQueue.Count > 0)
                            qi = _varThumbIoQueue.Dequeue();
                    }
                    if (qi == null)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (qi.cancel || qi.processed) continue;
                    RunVarThumbIoRead(qi);
                }
                catch (Exception ex)
                {
                    try
                    {
                        qi.hadError = true;
                        qi.errorText = ex.Message;
                    }
                    catch { }
                }
                finally
                {
                    QueuedImage q = qi;
                    try
                    {
                        if (q != null) q.thumbVarIoPending = false;
                    }
                    catch { }

                    try
                    {
                        if (singleton != null && q != null) singleton.StartThumbnailDecodeThread(q);
                    }
                    catch { }
                }
            }
        }

        private static void RunVarThumbIoRead(QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath)) return;
            if (!qi.isThumbnail) return;
            if (!IsVarPackageVfsPath(qi.imgPath)) return;
            if (qi.cancel) return;

			try { qi.debugStage = 11; qi.debugStageAux = 0; qi.debugStageRealtime = Time.realtimeSinceStartup; } catch { }

            using (FileEntryStream fileEntryStream = FileManager.OpenStream(qi.imgPath))
            {
                try { qi.debugStage = 21; qi.debugStageAux = 0; qi.debugStageRealtime = Time.realtimeSinceStartup; } catch { }
                Stream stream = fileEntryStream.Stream;
                qi.ProcessFromStream(stream);
            }
        }

        private void StartThumbnailDecodeThread(QueuedImage head)
        {
            if (head == null) return;
            try
            {
                if (head.cancel || head.processed)
                {
                    return;
                }

                head.working = true;
                try { head.debugWorkStartRealtime = Time.realtimeSinceStartup; } catch { }
                System.Threading.Interlocked.Increment(ref runningTasks);
                System.Threading.Interlocked.Increment(ref runningThumbnailTasks);

                Thread t = new Thread(() =>
                {
                    try
                    {
                        head.Process();
                    }
                    catch (Exception ex)
                    {
                        head.hadError = true;
                        head.errorText = ex.Message;
                    }
                    finally
                    {
                        if (!head.debugAbortIssued)
                        {
                            head.processed = true;
                            head.working = false;
                            System.Threading.Interlocked.Decrement(ref runningTasks);
                            System.Threading.Interlocked.Decrement(ref runningThumbnailTasks);
                        }
                        head.debugWorkerThread = null;
                    }
                });
                t.IsBackground = true;
                head.debugWorkerThread = t;
                t.Start();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] [Loader] Thumbnail decode thread start failed: " + ex.Message);
                try
                {
                    head.working = false;
                    System.Threading.Interlocked.Decrement(ref runningTasks);
                    System.Threading.Interlocked.Decrement(ref runningThumbnailTasks);
                }
                catch { }
            }
        }

        public static VPB.CustomImageLoaderThreaded singleton;

		public GameObject progressHUD;

		public Slider progressSlider;

		public Text progressText;

        protected int runningTasks;
        protected int runningThumbnailTasks;
        /// <summary>When queued image count exceeds this, drop stale thumbnail requests (see <see cref="PruneThumbnailQueueOverBudget"/>).</summary>
        /// <remarks>Large grids (many cols × buffer rows) enqueue 150+ thumbs per scroll; cap must stay below that burst or prune never runs (imgQ stalls ~500+).</remarks>
        private const int ThumbnailQueueSoftCap = 280;
        private const int ThumbnailQueuePruneTarget = 220;

		protected Dictionary<string, Texture2D> thumbnailCache;
		private const int ThumbnailCacheMaxItems = 512;
		private LinkedList<string> thumbnailCacheLru;
		private Dictionary<string, LinkedListNode<string>> thumbnailCacheLruNodes;
		private Dictionary<Texture2D, int> thumbnailUseCount;
		private HashSet<Texture2D> thumbnailEvicted;

		private readonly object pendingThumbnailLock = new object();
		private Dictionary<string, List<ImageLoaderCallback>> pendingThumbnailCallbacks;

        /// <summary>Number of thumbnails still being decoded/loaded in the background queue.</summary>
        public int PendingThumbnailCount
        {
            get
            {
                lock (pendingThumbnailLock)
                {
                    return pendingThumbnailCallbacks != null ? pendingThumbnailCallbacks.Count : 0;
                }
            }
        }

		protected Dictionary<string, Texture2D> textureCache;

		protected Dictionary<string, Texture2D> immediateTextureCache;

		protected Dictionary<Texture2D, bool> textureTrackedCache;

		protected Dictionary<Texture2D, int> textureUseCount;

		protected PriorityQueue<QueuedImage> queuedImages;

		protected int numRealQueuedImages;

		protected int progress;

		protected int progressMax;

		protected AsyncFlag loadFlag;

        // MTTask removed
        // StopThreads removed
        // StartThreads removed

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
					TextureUtil.UnmarkDownscaledActive("CIL:" + tex.name);
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
				if (value != null) TextureUtil.UnmarkDownscaledActive("CIL:" + value.name);
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
				if (value != null) TextureUtil.UnmarkDownscaledActive("CIL:" + value.name);
				UnityEngine.Object.Destroy(value);
			}
			immediateTextureCache.Clear();
		}

		public void PurgeAllThumbnails()
		{
			if (thumbnailCache != null)
			{
				foreach (Texture2D t in thumbnailCache.Values)
				{
					if (t != null) UnityEngine.Object.Destroy(t);
				}
				thumbnailCache.Clear();
			}
			if (thumbnailCacheLru != null) thumbnailCacheLru.Clear();
			if (thumbnailCacheLruNodes != null) thumbnailCacheLruNodes.Clear();
			if (thumbnailUseCount != null) thumbnailUseCount.Clear();
			if (thumbnailEvicted != null) thumbnailEvicted.Clear();
		}

		public void RegisterThumbnailUse(Texture2D tex)
		{
			if (tex == null) return;
			if (thumbnailUseCount == null) thumbnailUseCount = new Dictionary<Texture2D, int>();
			int c;
			if (thumbnailUseCount.TryGetValue(tex, out c)) thumbnailUseCount[tex] = c + 1;
			else thumbnailUseCount[tex] = 1;
		}

		public void DeregisterThumbnailUse(Texture2D tex)
		{
			if (tex == null || thumbnailUseCount == null) return;
			int c;
			if (!thumbnailUseCount.TryGetValue(tex, out c)) return;
			c--;
			if (c > 0)
			{
				thumbnailUseCount[tex] = c;
				return;
			}
			thumbnailUseCount.Remove(tex);
			if (thumbnailEvicted != null && thumbnailEvicted.Remove(tex))
			{
				try
				{
					if (tex != null) UnityEngine.Object.Destroy(tex);
				}
				catch { }
			}
		}

		private bool IsThumbnailInUse(Texture2D tex)
		{
			if (tex == null || thumbnailUseCount == null) return false;
			int c;
			return thumbnailUseCount.TryGetValue(tex, out c) && c > 0;
		}

		public void ClearCacheThumbnail(string imgPath, int turboJpegScaleDenom = 1, bool unityDecodeOnly = false)
		{
			string key = ThumbnailMemoryCacheKey(imgPath, turboJpegScaleDenom, unityDecodeOnly);
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(key, out value))
			{
				thumbnailCache.Remove(key);
				RemoveThumbnailCacheLru(key);
				if (value != null)
				{
					if (IsThumbnailInUse(value))
					{
						if (thumbnailEvicted == null) thumbnailEvicted = new HashSet<Texture2D>();
						thumbnailEvicted.Add(value);
					}
					else
					{
						UnityEngine.Object.Destroy(value);
					}
				}
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

		public Texture2D GetCachedThumbnail(string path, int turboJpegScaleDenom = 1, bool unityDecodeOnly = false)
		{
			string key = ThumbnailMemoryCacheKey(path, turboJpegScaleDenom, unityDecodeOnly);
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(key, out value))
			{
				if (value == null)
				{
					thumbnailCache.Remove(key);
					RemoveThumbnailCacheLru(key);
					return null;
				}
				TouchThumbnailCacheLru(key);
				return value;
			}
			return null;
		}

		public void AddCachedThumbnail(string path, Texture2D tex, int turboJpegScaleDenom = 1, bool unityDecodeOnly = false)
		{
			CacheThumbnail(ThumbnailMemoryCacheKey(path, turboJpegScaleDenom, unityDecodeOnly), tex);
		}

		private void CacheThumbnail(string cacheKey, Texture2D tex)
		{
			if (thumbnailCache == null || string.IsNullOrEmpty(cacheKey) || tex == null) return;
			if (!thumbnailCache.ContainsKey(cacheKey)) thumbnailCache.Add(cacheKey, tex);
			TouchThumbnailCacheLru(cacheKey);
			EnforceThumbnailCacheLimit();
		}

		private void TouchThumbnailCacheLru(string path)
		{
			if (thumbnailCacheLru == null || thumbnailCacheLruNodes == null || string.IsNullOrEmpty(path)) return;
			LinkedListNode<string> node;
			if (thumbnailCacheLruNodes.TryGetValue(path, out node) && node != null)
			{
				thumbnailCacheLru.Remove(node);
				thumbnailCacheLru.AddFirst(node);
			}
			else
			{
				node = thumbnailCacheLru.AddFirst(path);
				thumbnailCacheLruNodes[path] = node;
			}
		}

		private void RemoveThumbnailCacheLru(string path)
		{
			if (thumbnailCacheLru == null || thumbnailCacheLruNodes == null || string.IsNullOrEmpty(path)) return;
			LinkedListNode<string> node;
			if (thumbnailCacheLruNodes.TryGetValue(path, out node) && node != null)
			{
				thumbnailCacheLru.Remove(node);
			}
			thumbnailCacheLruNodes.Remove(path);
		}

		private void EnforceThumbnailCacheLimit()
		{
			if (thumbnailCache == null || thumbnailCacheLru == null || ThumbnailCacheMaxItems <= 0) return;
			while (thumbnailCache.Count > ThumbnailCacheMaxItems)
			{
				LinkedListNode<string> last = thumbnailCacheLru.Last;
				if (last == null) break;
				string key = last.Value;
				thumbnailCacheLru.RemoveLast();
				if (thumbnailCacheLruNodes != null) thumbnailCacheLruNodes.Remove(key);

				Texture2D victim;
				if (thumbnailCache.TryGetValue(key, out victim))
				{
					thumbnailCache.Remove(key);
					if (victim != null)
					{
						if (IsThumbnailInUse(victim))
						{
							if (thumbnailEvicted == null) thumbnailEvicted = new HashSet<Texture2D>();
							thumbnailEvicted.Add(victim);
						}
						else
						{
							UnityEngine.Object.Destroy(victim);
						}
					}
				}
			}
		}

		private void DispatchPendingThumbnailCallbacks(QueuedImage res)
		{
			if (res == null || string.IsNullOrEmpty(res.imgPath)) return;
			string pendingKey = ThumbnailMemoryCacheKey(res.imgPath, res.turboJpegScaleDenom, res.thumbnailUnityDecodeOnly);
			List<ImageLoaderCallback> callbacks = null;
			lock (pendingThumbnailLock)
			{
				if (pendingThumbnailCallbacks != null && pendingThumbnailCallbacks.TryGetValue(pendingKey, out callbacks))
				{
					pendingThumbnailCallbacks.Remove(pendingKey);
				}
			}
			if (callbacks == null) return;
			for (int i = 0; i < callbacks.Count; i++)
			{
				try
				{
					ImageLoaderCallback cb = callbacks[i];
					if (cb != null) cb(res);
				}
				catch (Exception ex)
				{
					LogUtil.LogError("Thumbnail callback error: " + ex);
				}
			}
		}

		/// <summary>
		/// Fast scroll can enqueue thousands of unique VAR paths; only <see cref="GetEffectiveMaxThumbnailThreads"/> decode at once.
		/// Remove lowest-urgency queued thumbnails (highest <see cref="QueuedImage.priority"/>), notify callbacks with cancel, return pooled QI.
		/// Never prunes <c>priority &lt; 0</c> (force-reload / skipCache lane).
		/// </summary>
		private void PruneThumbnailQueueOverBudget()
		{
			if (queuedImages == null || queuedImages.data == null) return;
			if (queuedImages.Count <= ThumbnailQueueSoftCap) return;
			int[] floors = new[] { 56, 48, 40, 32, 24, 16, 8, 0 };
			int guard = 0;
			foreach (int floor in floors)
			{
				while (queuedImages.Count > ThumbnailQueuePruneTarget && guard++ < 500)
				{
					QueuedImage worst = null;
					int worstP = int.MinValue;
					List<QueuedImage> data = queuedImages.data;
					int n = data.Count;
					for (int i = 0; i < n; i++)
					{
						QueuedImage q = data[i];
						if (q == null || !q.isThumbnail || q.working || q.processed || q.cancel) continue;
						if (q.priority < 0) continue;
						if (q.priority < floor) continue;
						if (q.priority > worstP)
						{
							worstP = q.priority;
							worst = q;
						}
					}
					if (worst == null) break;
					worst.cancel = true;
					queuedImages.Remove(worst);
					DispatchPendingThumbnailCallbacks(worst);
					pool.Return(worst);
				}
				if (queuedImages.Count <= ThumbnailQueuePruneTarget) return;
			}

		}

		protected List<QueuedImage> dispatchedImages = new List<QueuedImage>();
        private const float ThumbnailSoftTimeoutSec = 9.0f;
        private static readonly object _thumbHangLock = new object();
        private static HashSet<string> _thumbHangBlacklist;

		public void CancelGroup(string groupId)
		{
			if (string.IsNullOrEmpty(groupId)) return;
			if (queuedImages != null && queuedImages.data != null)
			{
				foreach (var qi in queuedImages.data)
				{
					if (qi.groupId == groupId)
					{
						qi.cancel = true;
					}
				}
			}
            // Also cancel dispatched items
            if (dispatchedImages != null)
            {
                foreach(var qi in dispatchedImages)
                {
                    if (qi.groupId == groupId) qi.cancel = true;
                }
            }
		}

		public void QueueImage(QueuedImage qi)
		{
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
			if (qi == null) return;
            // LogUtil.Log("[VPB-Debug] QueueThumbnail: " + qi.imgPath);
			qi.isThumbnail = true;
            try { qi.debugEnqueueRealtime = Time.realtimeSinceStartup; } catch { }
			PruneThumbnailQueueOverBudget();
			if (!string.IsNullOrEmpty(qi.imgPath))
			{
                // Session blacklist: if thumb path previously hung, fail fast to avoid repeated stalls.
                try
                {
                    bool black = false;
                    lock (_thumbHangLock)
                    {
                        if (_thumbHangBlacklist != null) black = _thumbHangBlacklist.Contains(qi.imgPath);
                    }
                    if (black)
                    {
                        qi.cancel = true;
                        if (qi.callback != null) qi.callback(qi);
                        pool.Return(qi);
                        return;
                    }
                }
                catch { }

				lock (pendingThumbnailLock)
				{
					if (pendingThumbnailCallbacks == null) pendingThumbnailCallbacks = new Dictionary<string, List<ImageLoaderCallback>>();
					List<ImageLoaderCallback> list;
					string pendingKey = ThumbnailMemoryCacheKey(qi.imgPath, qi.turboJpegScaleDenom, qi.thumbnailUnityDecodeOnly);
					if (pendingThumbnailCallbacks.TryGetValue(pendingKey, out list))
					{
						if (qi.callback != null) list.Add(qi.callback);
						QueuedImage existing = null;
						if (queuedImages != null && queuedImages.data != null)
						{
							for (int i = 0; i < queuedImages.data.Count; i++)
							{
								QueuedImage cand = queuedImages.data[i];
								if (cand.imgPath == qi.imgPath && cand.turboJpegScaleDenom == qi.turboJpegScaleDenom)
								{
									existing = cand;
									break;
								}
							}
						}
						if (existing != null)
						{
							queuedImages.Remove(existing);
							existing.insertionIndex = ++_insertionOrderCounter;
							queuedImages.Enqueue(existing);
							pool.Return(qi);
							return;
						}
						// Dict had pendingKey but no queue row (stale) — keep merged callbacks, own decode with this qi.
						pendingThumbnailCallbacks[pendingKey] = list;
						qi.callback = (res) => { DispatchPendingThumbnailCallbacks(res); };
					}
					else
					{
						list = new List<ImageLoaderCallback>(4);
						if (qi.callback != null) list.Add(qi.callback);
						pendingThumbnailCallbacks[pendingKey] = list;
						qi.callback = (res) => { DispatchPendingThumbnailCallbacks(res); };
					}
				}
			}
			if (queuedImages != null)
			{
				qi.insertionIndex = ++_insertionOrderCounter;
				queuedImages.Enqueue(qi);
			}
		}

		public void QueueThumbnailImmediate(QueuedImage qi)
		{
			if (qi == null) return;
			qi.isThumbnail = true;
			qi.cancel = false;
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
				if (qi.loadedFromDownscaledCache) TextureUtil.MarkDownscaledActive("CIL:" + qi.cacheSignature);
			}
		}

		protected void PostProcessImageQueue()
		{
            // Limit processing time to avoid UI freezes (target 8ms budget)
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            long maxTicks = 8 * 10000; // 8ms

            // 1. Process dispatched (in-flight) images that are done
            for (int i = dispatchedImages.Count - 1; i >= 0; i--)
            {
                if (sw.ElapsedTicks > maxTicks) break;

                QueuedImage value = dispatchedImages[i];
                if (value.processed || value.cancel)
                {
                    dispatchedImages.RemoveAt(i);
                    
                    if (value.cancel)
                    {
                        if (value.isThumbnail && !string.IsNullOrEmpty(value.imgPath))
						{
                            DispatchPendingThumbnailCallbacks(value);
						}
                        pool.Return(value);
                        continue;
                    }

                    if (!value.isThumbnail)
                    {
                        progress++;
                        numRealQueuedImages--;
                        
                        if (progress % 50 == 0 && ByteArrayPool.TotalRented > 0)
                        {
                            LogUtil.Log(ByteArrayPool.GetStatus());
                        }

                        if (numRealQueuedImages == 0)
                        {
                            progress = 0;
                            progressMax = 0;
                            if (progressHUD != null) progressHUD.SetActive(false);
                            if(ByteArrayPool.TotalRented > 0) LogUtil.Log(ByteArrayPool.GetStatus());
                        }
                        else
                        {
                            if (progressHUD != null) progressHUD.SetActive(true);
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
                            if (value.tex != null) CacheThumbnail(ThumbnailMemoryCacheKey(value.imgPath, value.turboJpegScaleDenom, value.thumbnailUnityDecodeOnly), value.tex);
                        }
                        else if (!textureCache.ContainsKey(value.cacheSignature) && value.tex != null)
                        {
                            textureCache.Add(value.cacheSignature, value.tex);
                            textureTrackedCache.Add(value.tex, true);
                            if (value.loadedFromDownscaledCache) TextureUtil.MarkDownscaledActive("CIL:" + value.cacheSignature);
                        }
                    }
                    value.DoCallback();
                    if (value.imgPath != null && value.imgPath.StartsWith("http")) LogUtil.Log("[VPB] [Loader] Finished: " + value.imgPath);
                    // else if (value.isThumbnail) LogUtil.Log("[VPB-Debug] Finished: " + value.imgPath);
                    pool.Return(value);
                }
            }

            // 2. Cleanup canceled items from the pending queue (Peek only)
            // We can't easily iterate the PriorityQueue, but we can peek the top.
            while (queuedImages != null && queuedImages.Count > 0)
            {
                if (queuedImages.Peek().cancel)
                {
                    QueuedImage qi = queuedImages.Dequeue();
                    if (qi.isThumbnail && !string.IsNullOrEmpty(qi.imgPath))
                    {
                        qi.cancel = true;
                        DispatchPendingThumbnailCallbacks(qi);
                    }
                    pool.Return(qi);
                }
                else
                {
                    break;
                }
            }

			if (numRealQueuedImages != 0)
			{
				if (loadFlag == null)
				{
					loadFlag = new AsyncFlag("ImageLoader");
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
                    if (qi.isThumbnail && !string.IsNullOrEmpty(qi.imgPath))
                    {
                        qi.cancel = true;
                        DispatchPendingThumbnailCallbacks(qi);
                    }
                    pool.Return(qi);
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
					Texture2D cached = GetCachedThumbnail(value.imgPath, value.turboJpegScaleDenom, value.thumbnailUnityDecodeOnly);
					if (cached != null) UseCachedTex(value, cached);
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
                        value.webRequest.timeout = 30;
						value.webRequest.SendWebRequest();
                        if (value.imgPath.StartsWith("http")) LogUtil.Log("[VPB] [Loader] Started WebRequest: " + value.imgPath);
					}
					if (value.webRequest.isDone)
					{
						if (!value.webRequest.isNetworkError)
						{
							if (value.webRequest.responseCode == 200)
							{
                                if (value.imgPath.StartsWith("http")) LogUtil.Log("[VPB] [Loader] WebRequest Success: " + value.imgPath);
								value.webRequestData = value.webRequest.downloadHandler.data;
								value.webRequestDone = true;
							}
							else
							{
                                LogUtil.LogWarning("[VPB] [Loader] WebRequest Status Error: " + value.webRequest.responseCode + " for " + value.imgPath);
								value.webRequestHadError = true;
								value.webRequestDone = true;
								value.hadError = true;
								value.errorText = "Error " + value.webRequest.responseCode;
							}
						}
						else
						{
                            LogUtil.LogWarning("[VPB] [Loader] WebRequest Network Error: " + value.webRequest.error + " for " + value.imgPath);
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

		protected void Update()
		{
			PruneThumbnailQueueOverBudget();
			PostProcessImageQueue();
            int maxTasks = GetEffectiveMaxLoaderThreads();
            int maxThumb = GetEffectiveMaxThumbnailThreads();

            // Dispatch pending dispatched items (web requests that just finished)
            for(int i=0; i<dispatchedImages.Count; i++)
            {
                if (runningTasks >= maxTasks) break;

                QueuedImage qi = dispatchedImages[i];
                if (!qi.working && !qi.processed && !qi.cancel && !qi.thumbVarIoPending)
                {
                    // Check conditions
                    if (qi.webRequest != null && !qi.webRequestDone) continue;
                    if (qi.isThumbnail && runningThumbnailTasks >= maxThumb) continue;

                    // Execute
                    StartWorker(qi);
                }
            }

            // Thumbnails stuck in Process(): recover slots / blacklist toxic paths.
            try
            {
                const float stuckSec = 2.0f;
                float now = Time.realtimeSinceStartup;
                if (dispatchedImages != null && dispatchedImages.Count > 0)
                {
                    for (int i = 0; i < dispatchedImages.Count; i++)
                    {
                        QueuedImage qi = dispatchedImages[i];
                        if (qi == null || !qi.isThumbnail) continue;
                        if (!qi.working || qi.processed || qi.cancel) continue;
                        float ws = qi.debugWorkStartRealtime;
                        if (ws <= 0f) continue;
                        float age = now - ws;
                        if (age < stuckSec) continue;

                        // If worker never reaches Process() (stg<20) for a long time, treat as wedged start and reset slot.
                        if (!qi.debugAbortIssued && qi.debugStage < 20 && age >= (ThumbnailSoftTimeoutSec + 6.0f))
                        {
                            qi.debugAbortIssued = true;
                            try
                            {
                                qi.hadError = true;
                                qi.errorText = "thumb worker never entered Process @" + age.ToString("0.000") + "s stg=" + qi.debugStage;
                                qi.cancel = true;
                                qi.processed = true;
                                qi.working = false;
                                System.Threading.Interlocked.Decrement(ref runningTasks);
                                System.Threading.Interlocked.Decrement(ref runningThumbnailTasks);
                            }
                            catch { }

                            try
                            {
                                if (qi.debugWorkerThread != null && qi.debugWorkerThread.IsAlive)
                                {
                                    // Last resort: background thread wedged before Process; abandon thread (orphan) but free loader slot.
                                    try { qi.debugWorkerThread.Interrupt(); } catch { }
                                }
                            }
                            catch { }

                            try
                            {
                                lock (_thumbHangLock)
                                {
                                    if (_thumbHangBlacklist == null) _thumbHangBlacklist = new HashSet<string>(StringComparer.Ordinal);
                                    if (!string.IsNullOrEmpty(qi.imgPath)) _thumbHangBlacklist.Add(qi.imgPath);
                                }
                            }
                            catch { }
                            break;
                        }

                        // Soft-timeout: mark hung thumb as canceled + blacklist path for session.
                        // Do NOT use Thread.Abort (unsafe; can cascade failures).
                        // Only soft-timeout once worker actually entered Process() (debugStage >= 20).
                        // If still at stg<=1, thread likely wedged before Process (or never started) — do not fake-finish
                        // or we leak background work + counters drift.
                        if (!qi.debugAbortIssued && age >= ThumbnailSoftTimeoutSec && qi.debugStage >= 20)
                        {
                            qi.debugAbortIssued = true;
                            try
                            {
                                qi.hadError = true;
                                qi.errorText = "thumb soft-timeout @" + age.ToString("0.000") + "s";
                                qi.cancel = true; // ensures callbacks see cancel and target will retry/blank safely
                                qi.processed = true;
                                qi.working = false;
                                System.Threading.Interlocked.Decrement(ref runningTasks);
                                System.Threading.Interlocked.Decrement(ref runningThumbnailTasks);
                            }
                            catch { }

                            try
                            {
                                lock (_thumbHangLock)
                                {
                                    if (_thumbHangBlacklist == null) _thumbHangBlacklist = new HashSet<string>(StringComparer.Ordinal);
                                    if (!string.IsNullOrEmpty(qi.imgPath)) _thumbHangBlacklist.Add(qi.imgPath);
                                }
                            }
                            catch { }
                            break;
                        }
                        break;
                    }
                }
            }
            catch { }

            // Dispatch new items from queue
            // Backpressure: if we have too many items pending decode (in dispatchedImages), stop dispatching to avoid memory exhaustion
            // and pipeline stalling.
			if (queuedImages != null && queuedImages.Count > 0)
			{
                while (runningTasks < maxTasks && dispatchedImages.Count < 200 && queuedImages.Count > 0)
                {
                    QueuedImage head = queuedImages.Peek();
                    if (head == null) break;
                    if (head.isThumbnail && runningThumbnailTasks >= maxThumb) break;

                    // Perform pre-checks (cache, web)
                    PreprocessImageQueue();

                    // Preprocess might have set processed=true or initialized web request
                    // We must dequeue it to move it to dispatched list
                    head = queuedImages.Dequeue();
                    try { head.debugDispatchRealtime = Time.realtimeSinceStartup; } catch { }
                    dispatchedImages.Add(head);

                    if (head.processed || head.cancel) continue; // Will be handled by PostProcess

                    if (head.webRequest != null && !head.webRequestDone)
                    {
                        // Waiting for web, do not start worker yet
                        continue;
                    }

                    if (head.thumbVarIoPending) continue;

                    StartWorker(head);
                }
			}

		}

        private void StartWorker(QueuedImage head)
        {
            // if (head.isThumbnail) LogUtil.Log("[VPB-Debug] StartWorker: " + head.imgPath);
            bool success = false;
            if (!head.isThumbnail)
            {
                head.working = true;
                try { head.debugWorkStartRealtime = Time.realtimeSinceStartup; } catch { }
                System.Threading.Interlocked.Increment(ref runningTasks);
                try
                {
                    success = ThreadPool.QueueUserWorkItem((state) => {
                        try
                        {
                            head.Process();
                        }
                        catch(Exception ex)
                        {
                            head.hadError = true;
                            head.errorText = ex.Message;
                        }
                        finally
                        {
                            head.processed = true;
                            head.working = false;
                            System.Threading.Interlocked.Decrement(ref runningTasks);
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] [Loader] ThreadPool.QueueUserWorkItem Exception: " + ex.Message);
                    success = false;
                }
            }
            else
            {
                // Thumbnails:
                // - VAR pkg:/ paths enqueue IO read on dedicated thread, then decode on per-thumb thread.
                // - Other paths decode directly on per-thumb thread.
                try
                {
                    try { head.debugStage = 1; head.debugStageAux = 0; head.debugStageRealtime = Time.realtimeSinceStartup; } catch { }

                    bool isVar = false;
                    try { isVar = IsVarPackageVfsPath(head.imgPath); } catch { isVar = false; }

                    if (isVar && head.webRequest == null && !head.cancel && !head.processed)
                    {
                        head.thumbVarIoPending = true;
                        try { head.debugStage = 10; head.debugStageAux = 0; head.debugStageRealtime = Time.realtimeSinceStartup; } catch { }
                        EnsureVarThumbIoWorker();
                        lock (_varThumbIoQueueLock) { _varThumbIoQueue.Enqueue(head); }
                        success = true;
                    }
                    else
                    {
                        StartThumbnailDecodeThread(head);
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] [Loader] Thumbnail worker start failed: " + ex.Message);
                    success = false;
                }
            }
            
            if (!success)
            {
                // Failed to queue worker (Thread pool exhausted?)
                // Revert state so it can be picked up again
                if (!head.isThumbnail)
                {
                    System.Threading.Interlocked.Decrement(ref runningTasks);
                    head.working = false;
                }
                // Note: It remains in dispatchedImages, so Update loop will try to start it again next frame.
            }
        }

		public virtual void OnDestroy()
		{
            try
            {
                _varThumbIoStop = true;
                if (_varThumbIoThread != null && _varThumbIoThread.IsAlive)
                {
                    try { _varThumbIoThread.Join(250); } catch { }
                }
            }
            catch { }
            // StopThreads removed
			if (loadFlag != null)
			{
				loadFlag.Raise();
			}
			PurgeAllTextures();
			PurgeAllImmediateTextures();
			PurgeAllThumbnails();
		}

		protected void OnApplicationQuit()
		{
            try { _varThumbIoStop = true; } catch { }
            // StopThreads removed
		}

		// Read-only counters for VpbPerfTelemetry. Snapshot only; safe to call any frame.
		public void GetTelemetryCounts(
			out int textureCacheCount,
			out int immediateTextureCacheCount,
			out int thumbnailCacheCount,
			out int queuedImagesCount,
			out int dispatchedImagesCount,
			out int pendingThumbnailCallbacksCount)
		{
			textureCacheCount = textureCache != null ? textureCache.Count : 0;
			immediateTextureCacheCount = immediateTextureCache != null ? immediateTextureCache.Count : 0;
			thumbnailCacheCount = thumbnailCache != null ? thumbnailCache.Count : 0;
			queuedImagesCount = queuedImages != null ? queuedImages.Count : 0;
			dispatchedImagesCount = dispatchedImages != null ? dispatchedImages.Count : 0;
			lock (pendingThumbnailLock)
			{
				pendingThumbnailCallbacksCount = pendingThumbnailCallbacks != null ? pendingThumbnailCallbacks.Count : 0;
			}
		}

		protected virtual void Awake()
		{
			if (singleton == null) singleton = this;
            LogUtil.Log("CustomImageLoaderThreaded initialized. ByteArrayPool ready.");
            EnsureVarThumbIoWorker();
			immediateTextureCache = new Dictionary<string, Texture2D>();
			textureCache = new Dictionary<string, Texture2D>();
			textureTrackedCache = new Dictionary<Texture2D, bool>();
			thumbnailCache = new Dictionary<string, Texture2D>();
			thumbnailCacheLru = new LinkedList<string>();
			thumbnailCacheLruNodes = new Dictionary<string, LinkedListNode<string>>();
			thumbnailUseCount = new Dictionary<Texture2D, int>();
			thumbnailEvicted = new HashSet<Texture2D>();
			pendingThumbnailCallbacks = new Dictionary<string, List<ImageLoaderCallback>>();
			textureUseCount = new Dictionary<Texture2D, int>();
			queuedImages = new PriorityQueue<QueuedImage>((a, b) => {
                int p = a.priority - b.priority;
                if (p != 0) return p;
                // Use LIFO for insertion index (newer items first)
                // This ensures that when scrolling, the most recently visible items are loaded first
                return b.insertionIndex.CompareTo(a.insertionIndex);
            });
		}

        private static long _insertionOrderCounter = 0;

        public class PriorityQueue<T>
        {
            public List<T> data;
            private Comparison<T> comparison;

            public PriorityQueue(Comparison<T> comparison)
            {
                this.data = new List<T>();
                this.comparison = comparison;
            }

            public void Remove(T item)
            {
                data.Remove(item);
                // Re-heapify is expensive, but for correctness we should do it.
                // Or just rebuild the heap.
                // Simple approach: data.Remove is O(N).
                // Rebuilding heap: O(N).
                // Just calling Sort() is O(N log N) but our structure is heap, not sorted list.
                // Actually, if we remove an item, we can just rebuild the heap from scratch or let it be.
                // Wait, if we remove an item, the heap property might be broken?
                // data.Remove fills the gap by shifting elements.
                // This breaks the heap structure indices.
                // We must rebuild.
                int count = data.Count;
                for (int i = count / 2; i >= 0; i--)
                {
                    HeapifyDown(i);
                }
            }

            private void HeapifyDown(int pi)
            {
                int li = data.Count - 1;
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
                if (li >= 0) HeapifyDown(0);
                
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
