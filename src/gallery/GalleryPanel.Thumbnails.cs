using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private struct ThumbnailCacheJob
        {
            public string Path;
            public Texture2D Texture;
            public long LastWriteTime;
            public string GroupId;
        }

        private IEnumerator ProcessThumbnailCacheQueue()
        {
            try
            {
                while (pendingThumbnailCacheJobs != null && pendingThumbnailCacheJobs.Count > 0)
                {
                    if (Time.unscaledTime - lastScrollTime <= 0.25f)
                    {
                        yield return null;
                        continue;
                    }

                    ThumbnailCacheJob job = pendingThumbnailCacheJobs.Dequeue();
                    if (string.IsNullOrEmpty(job.Path) || job.Texture == null) { yield return null; continue; }
                    if (!string.IsNullOrEmpty(job.GroupId) && job.GroupId != currentLoadingGroupId) { yield return null; continue; }

                    yield return StartCoroutine(GalleryThumbnailCache.Instance.GenerateAndSaveThumbnailRoutine(job.Path, job.Texture, job.LastWriteTime));
                    yield return null;
                }
            }
            finally
            {
                thumbnailCacheCoroutine = null;
            }
        }

        private void EnqueueThumbnailCacheJob(string path, Texture2D tex, long lastWriteTime, string groupId)
        {
            if (pendingThumbnailCacheJobs == null) pendingThumbnailCacheJobs = new Queue<ThumbnailCacheJob>();
            pendingThumbnailCacheJobs.Enqueue(new ThumbnailCacheJob { Path = path, Texture = tex, LastWriteTime = lastWriteTime, GroupId = groupId });
        }

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
                UpdateAspectRatio(target, tex);
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
                        UpdateAspectRatio(target, res.tex);
                    }
                    
                    long imgTime = file.LastWriteTime.ToFileTime();
                    if (imgPath != file.Path)
                    {
                        FileEntry fe = FileManager.GetFileEntry(imgPath);
                        if (fe != null) imgTime = fe.LastWriteTime.ToFileTime();
                    }
                    
                    if (!res.loadedFromGalleryCache)
                    {
                        EnqueueThumbnailCacheJob(imgPath, res.tex, imgTime, currentLoadingGroupId);
                    }
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }

        private void UpdateAspectRatio(RawImage target, Texture tex)
        {
            if (target == null || tex == null) return;
            AspectRatioFitter arf = target.GetComponent<AspectRatioFitter>();
            if (arf == null)
            {
                arf = target.gameObject.AddComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            }

            if (arf != null)
            {
                arf.aspectRatio = (float)tex.width / tex.height;
            }
        }
    }
}
