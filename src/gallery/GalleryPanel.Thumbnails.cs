using System;
using System.Collections;
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
            if (file.Path.EndsWith(".json") || file.Path.EndsWith(".vap") || file.Path.EndsWith(".vam") || file.Path.EndsWith(".assetbundle") || file.Path.EndsWith(".unity3d"))
                imgPath = Regex.Replace(file.Path, "\\.(json|vac|vap|vam|scene|assetbundle|unity3d)$", ".jpg");
            else if (file.Path.EndsWith(".jpg") || file.Path.EndsWith(".png"))
                imgPath = file.Path;

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
            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.QIPool.Get();
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
                    StartCoroutine(GenerateAndCacheThumbnail(imgPath, res.tex, file.LastWriteTime.ToFileTime()));
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
                
                Texture2D newTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                newTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                newTex.Apply();
                
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                
                bytes = newTex.GetRawTextureData();
                Destroy(newTex);
            }

            if (bytes != null)
            {
                GalleryThumbnailCache.Instance.SaveThumbnail(path, bytes, bytes.Length, w, h, TextureFormat.RGB24, lastWriteTime);
            }
        }
    }
}
