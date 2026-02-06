using System;
using System.Collections.Generic;
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
            }

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
			public bool loadedFromGalleryCache;
			public bool loadedFromCache;
			public bool loadedFromDownscaledCache;
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
                return TextureUtil.GetZstdCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, setSize ? width : 0, setSize ? height : 0, bumpStrength);
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
					if (width <= 0 || height <= 0)
					{
						return;
					}
					try
					{
						tex = new Texture2D(width, height, textureFormat, createMipMaps, linear);
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
				}
			}

			protected void ProcessFromStream(Stream st)
			{
				try
				{
					byte[] buffer = new byte[16384];
					using (MemoryStream ms = new MemoryStream())
					{
						int read;
						while ((read = st.Read(buffer, 0, buffer.Length)) > 0)
						{
							ms.Write(buffer, 0, read);
						}
						byte[] bytes = ms.ToArray();
						rawLength = bytes.Length;
						raw = ByteArrayPool.Rent(rawLength);
						Buffer.BlockCopy(bytes, 0, raw, 0, rawLength);
						needsDecoding = true;
					}
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
						bool loadedFromGalleryCache = false;
						if (isThumbnail)
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
                                FileEntry fe = FileManager.GetFileEntry(imgPath);
                                if (fe != null)
                                {
                                    lastWriteTime = fe.LastWriteTime.ToFileTime();
                                    foundTime = true;
                                }
                            }

							if (foundTime)
							{
								int w, h;
								TextureFormat fmt;
								byte[] data;
								if (GalleryThumbnailCache.Instance.TryGetThumbnail(imgPath, lastWriteTime, out data, out w, out h, out fmt))
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

						string vpbCachePath = isThumbnail ? null : GetVPBCachePath();
						string diskCachePath = isThumbnail ? null : GetDiskCachePath();

						if (!loadedFromGalleryCache && vpbCachePath != null && File.Exists(vpbCachePath))
						{
							try
							{
								string metaPath = vpbCachePath + "meta";
								if (File.Exists(metaPath))
								{
									string jsonString = File.ReadAllText(metaPath);
									ReadMetaJson(jsonString);
									byte[] compressed = File.ReadAllBytes(vpbCachePath);
									raw = ZstdCompressor.Decompress(compressed);
									preprocessed = true;
									loadedFromCache = true;
								}
							}
							catch (Exception ex)
							{
								LogUtil.LogError("Exception during VPB cache file read " + ex);
							}
						}

						if (!loadedFromGalleryCache && !preprocessed && MVR.FileManagement.CacheManager.CachingEnabled && diskCachePath != null && FileManager.FileExists(diskCachePath))
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
									loadedFromCache = true;
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
							if (!loadedFromGalleryCache)
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
					Texture2D tempTex = new Texture2D(2, 2);
					byte[] dataToLoad = raw;
					if (raw.Length != rawLength)
					{
						dataToLoad = new byte[rawLength];
						Buffer.BlockCopy(raw, 0, dataToLoad, 0, rawLength);
					}
					
					if (tempTex.LoadImage(dataToLoad))
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

                        // Copy Color32 to raw bytes (Unity's Color32 is RGBA)
                        // We need to match the original loader's expected format if it was doing something special.
                        // The original loader was doing some swaps:
                        /*
                        for (int i = 0; i < num8; i += num3)
                        {
                            byte b = raw[i];
                            raw[i] = raw[i + 2];
                            raw[i + 2] = b;
                            ...
                        }
                        */
                        // Unity Color32 is: r, g, b, a.
                        // The original loader was copying from System.Drawing (BGRA usually) and swapping R and B.
                        // So it ended up as RGBA.
                        
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
                    // This is the most complex one. 
                    // I'll copy the logic from the original loader but adapted to RGBA
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
				CreateTexture();
                if (tex == null)
                {
                    LogUtil.LogError("Failed to create texture in Finish() for " + imgPath);
                    hadError = true;
                    return;
                }

				if (preprocessed)
				{
					try
					{
                        TextureUtil.SafeLoadRawTextureData(tex, raw, width, height, textureFormat);
					}
					catch (Exception ex)
					{
                        LogUtil.LogError($"[VPB] LoadRawTextureData failed (preprocessed) for {imgPath}. RawLength: {(raw != null ? raw.Length : -1)} | Format: {textureFormat} | Size: {width}x{height} | Error: {ex.Message}");
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
					tex.Apply(false);
					if (canCompress && textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5)
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
					    texture2D.Apply();
					    if (canCompress)
					    {
						    try { texture2D.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed (dxt) " + ex + " path=" + imgPath); canCompress = false; }
					    }
					    byte[] rawTextureData = texture2D.GetRawTextureData();
					    tex.LoadRawTextureData(rawTextureData);
					    tex.Apply();
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
					    tex.Apply();
					    if (canCompress)
					    {
						    try { tex.Compress(true); } catch (Exception ex) { LogUtil.LogError("Compress failed " + ex + " path=" + imgPath); canCompress = false; }
					    }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[VPB] LoadRawTextureData failed (standard path) for {imgPath}: {ex.Message}");
                    }
                    bool savedToGalleryCache = loadedFromCache || loadedFromGalleryCache;
                    if (isThumbnail && GalleryThumbnailCache.Instance != null && !Regex.IsMatch(imgPath, "^http") && !loadedFromGalleryCache)
                    {
                         try
                         {
                             if (FileManager.FileExists(imgPath))
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
                                     FileEntry fe = FileManager.GetFileEntry(imgPath);
                                     if (fe != null)
                                     {
                                         lastWriteTime = fe.LastWriteTime.ToFileTime();
                                         foundTime = true;
                                     }
                                 }

                                 if (foundTime)
                                 {
                                     // Only save to thumbnail cache if it's reasonably small
                                     if (tex.width <= 512 && tex.height <= 512)
                                     {
                                         byte[] rawTextureData2 = tex.GetRawTextureData();
                                         GalleryThumbnailCache.Instance.SaveThumbnail(imgPath, rawTextureData2, rawTextureData2.Length, tex.width, tex.height, tex.format, lastWriteTime);
                                         savedToGalleryCache = true;
                                         loadedFromGalleryCache = true;
                                     }
                                 }
                             }
                         }
                         catch (Exception ex)
                         {
                             LogUtil.LogError("Exception during gallery caching " + ex);
                         }
                    }

					if (!savedToGalleryCache && MVR.FileManagement.CacheManager.CachingEnabled)
					{
						string text = ((!Regex.IsMatch(imgPath, "^http")) ? GetDiskCachePath() : GetWebCachePath());
						if (text != null && !FileManager.FileExists(text))
						{
							try
							{
								JSONClass jSONClass = new JSONClass();
								jSONClass["type"] = "image";
								jSONClass["width"] = tex.width.ToString();
								jSONClass["height"] = tex.height.ToString();
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

		protected class ImageLoaderTaskInfo
		{
			public string name;

			public AutoResetEvent resetEvent;

			public Thread thread;

			public volatile bool working;

			public volatile bool kill;
		}

		public static VPB.CustomImageLoaderThreaded singleton;

		public GameObject progressHUD;

		public Slider progressSlider;

		public Text progressText;

		protected ImageLoaderTaskInfo imageLoaderTask;

		protected bool _threadsRunning;

		protected Dictionary<string, Texture2D> thumbnailCache;
		private const int ThumbnailCacheMaxItems = 512;
		private LinkedList<string> thumbnailCacheLru;
		private Dictionary<string, LinkedListNode<string>> thumbnailCacheLruNodes;
		private Dictionary<Texture2D, int> thumbnailUseCount;
		private HashSet<Texture2D> thumbnailEvicted;

		private readonly object pendingThumbnailLock = new object();
		private Dictionary<string, List<ImageLoaderCallback>> pendingThumbnailCallbacks;

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
				try
				{
					imageLoaderTaskInfo.resetEvent.WaitOne(-1, true);
					if (imageLoaderTaskInfo.kill)
					{
						break;
					}
					ProcessImageQueueThreaded();
				}
				catch (Exception ex)
				{
					LogUtil.LogError("ImageLoaderThread Error: " + ex);
				}
				finally
				{
					imageLoaderTaskInfo.working = false;
				}
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

		public void ClearCacheThumbnail(string imgPath)
		{
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(imgPath, out value))
			{
				thumbnailCache.Remove(imgPath);
				RemoveThumbnailCacheLru(imgPath);
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

		public Texture2D GetCachedThumbnail(string path)
		{
			Texture2D value;
			if (thumbnailCache != null && thumbnailCache.TryGetValue(path, out value))
			{
				if (value == null)
				{
					thumbnailCache.Remove(path);
					RemoveThumbnailCacheLru(path);
					return null;
				}
				TouchThumbnailCacheLru(path);
				return value;
			}
			return null;
		}

		public void AddCachedThumbnail(string path, Texture2D tex)
		{
			CacheThumbnail(path, tex);
		}

		private void CacheThumbnail(string path, Texture2D tex)
		{
			if (thumbnailCache == null || string.IsNullOrEmpty(path) || tex == null) return;
			if (!thumbnailCache.ContainsKey(path)) thumbnailCache.Add(path, tex);
			TouchThumbnailCacheLru(path);
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
			List<ImageLoaderCallback> callbacks = null;
			lock (pendingThumbnailLock)
			{
				if (pendingThumbnailCallbacks != null && pendingThumbnailCallbacks.TryGetValue(res.imgPath, out callbacks))
				{
					pendingThumbnailCallbacks.Remove(res.imgPath);
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
			qi.isThumbnail = true;
			if (!string.IsNullOrEmpty(qi.imgPath))
			{
				lock (pendingThumbnailLock)
				{
					if (pendingThumbnailCallbacks == null) pendingThumbnailCallbacks = new Dictionary<string, List<ImageLoaderCallback>>();
					List<ImageLoaderCallback> list;
					if (pendingThumbnailCallbacks.TryGetValue(qi.imgPath, out list))
					{
						if (qi.callback != null) list.Add(qi.callback);
						pool.Return(qi);
						return;
					}
					list = new List<ImageLoaderCallback>(4);
					if (qi.callback != null) list.Add(qi.callback);
					pendingThumbnailCallbacks[qi.imgPath] = list;
					qi.callback = (res) => { DispatchPendingThumbnailCallbacks(res); };
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
            int maxPerFrame = 20;
			while (queuedImages != null && queuedImages.Count > 0 && maxPerFrame > 0)
			{
                maxPerFrame--;
				QueuedImage value = queuedImages.Peek();
				if (value.processed || value.cancel)
				{
					queuedImages.Dequeue();
                    if (value.cancel)
                    {
						if (value.isThumbnail && !string.IsNullOrEmpty(value.imgPath))
						{
							lock (pendingThumbnailLock)
							{
								if (pendingThumbnailCallbacks != null) pendingThumbnailCallbacks.Remove(value.imgPath);
							}
						}
                        pool.Return(value);
                        continue;
                    }

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
							if (value.tex != null) CacheThumbnail(value.imgPath, value.tex);
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
					pool.Return(value);
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
					Texture2D cached = GetCachedThumbnail(value.imgPath);
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

		protected virtual void Update()
		{
			StartThreads();
			if (!imageLoaderTask.working)
			{
				PostProcessImageQueue();
				if (queuedImages != null && queuedImages.Count > 0)
				{
					PreprocessImageQueue();
                    
                    QueuedImage head = queuedImages.Peek();
                    if (head != null && !head.processed && !head.cancel && (head.webRequestDone || head.webRequest == null))
                    {
					    imageLoaderTask.working = true;
					    imageLoaderTask.resetEvent.Set();
                    }
				}
			}
		}

		public virtual void OnDestroy()
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
			PurgeAllThumbnails();
		}

		protected void OnApplicationQuit()
		{
			if (Application.isPlaying)
			{
				StopThreads();
			}
		}

		protected virtual void Awake()
		{
			if (singleton == null) singleton = this;
            LogUtil.Log("CustomImageLoaderThreaded initialized. ByteArrayPool ready.");
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
                return a.insertionIndex.CompareTo(b.insertionIndex);
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
