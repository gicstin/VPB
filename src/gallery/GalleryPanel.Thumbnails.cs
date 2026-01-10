using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void LoadThumbnail(FileEntry file, RawImage target)
        {
            string imgPath = "";
            string lowerPath = file.Path.ToLowerInvariant();
            if (lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".png"))
            {
                imgPath = file.Path;
            }
            else
            {
                // Sister-file rule: same name, .jpg or .png extension
                // Optimized discovery via archive flattening (FileManager.FileExists)
                string testJpg = Path.ChangeExtension(file.Path, ".jpg");
                if (FileManager.FileExists(testJpg))
                {
                    imgPath = testJpg;
                }
                else
                {
                    string testPng = Path.ChangeExtension(file.Path, ".png");
                    if (FileManager.FileExists(testPng))
                    {
                        imgPath = testPng;
                    }
                }
            }

            if (string.IsNullOrEmpty(imgPath)) return;

            if (CustomImageLoaderThreaded.singleton == null) return;

            // 1. Memory Cache
            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
            if (tex != null)
            {
                target.texture = tex;
                target.color = Color.white;
                return;
            }

            // 2. Disk Cache - Removed to prevent blocking main thread
            // Now handled asynchronously by CustomImageLoaderThreaded
            // if (GalleryThumbnailCache.Instance.TryGetThumbnail(...)) { ... }

            // 3. Request Load
            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = imgPath;
            qi.isThumbnail = true;
            qi.priority = 10; 
            qi.groupId = currentLoadingGroupId;
            qi.callback = (res) => {
                if (res != null && res.tex != null) {
                    if (target != null) {
                        target.texture = res.tex;
                        target.color = Color.white;
                    }
                    
                    long imgTime = file.LastWriteTime.ToFileTime();
                    if (imgPath != file.Path)
                    {
                        FileEntry fe = FileManager.GetFileEntry(imgPath);
                        if (fe != null) imgTime = fe.LastWriteTime.ToFileTime();
                    }
                    
                    if (!res.loadedFromGalleryCache)
                    {
                        StartCoroutine(GenerateAndCacheThumbnail(imgPath, res.tex, imgTime));
                    }
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }

        private IEnumerator GenerateAndCacheThumbnail(string path, Texture2D sourceTex, long lastWriteTime)
        {
            yield return null;

            if (sourceTex == null) yield break;

            int maxDim = 256;
            byte[] bytes = null;
            int w = sourceTex.width;
            int h = sourceTex.height;

            TextureFormat format = sourceTex.format;

            if (w <= maxDim && h <= maxDim)
            {
                bytes = sourceTex.GetRawTextureData();
            }
            else
            {
                float aspect = (float)w / h;
                if (w > h) { w = maxDim; h = Mathf.RoundToInt(maxDim / aspect); }
                else { h = maxDim; w = Mathf.RoundToInt(maxDim * aspect); }

                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
                Graphics.Blit(sourceTex, rt);
                
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                
                format = TextureFormat.RGB24;
                Texture2D newTex = new Texture2D(w, h, format, false);
                newTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                newTex.Apply();
                
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                
                bytes = newTex.GetRawTextureData();
                Destroy(newTex);
            }

            if (bytes != null)
            {
                GalleryThumbnailCache.Instance.SaveThumbnail(path, bytes, bytes.Length, w, h, format, lastWriteTime);
            }
        }
    }
}
