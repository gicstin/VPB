using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    internal class ThumbnailBindingTag : MonoBehaviour
    {
        public string ExpectedTag;
        public Texture2D CurrentTexture;

        private void OnDisable()
        {
            try
            {
                if (CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                {
                    CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(CurrentTexture);
                }
            }
            catch { }
            CurrentTexture = null;
        }

        private void OnDestroy()
        {
            OnDisable();
        }
    }

    public partial class GalleryPanel
    {
        // Cache for package list thumbnails: package UID -> internal image path (within the package).
        // Keeps package preview lookups cheap while scrolling.
        private readonly Dictionary<string, string> _packagePreviewInternalPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool IsImagePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.ToLowerInvariant();
            return p.EndsWith(".jpg") || p.EndsWith(".png");
        }

        private string GetOrChoosePackagePreviewInternalPath(VarPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                string uid = pkg.Uid;
                if (!string.IsNullOrEmpty(uid) && _packagePreviewInternalPathCache.TryGetValue(uid, out string cached))
                    return cached;

                List<string> names; List<long> ticks; List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return null;

                string chosen = null;

                // Prefer preview-ish names.
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (!IsImagePath(n)) continue;
                    string ln = n.ToLowerInvariant();
                    if (ln.Contains("preview") || ln.Contains("thumbnail") || ln.Contains("thumb") || ln.Contains("screenshot"))
                    {
                        chosen = n;
                        break;
                    }
                }

                // Fallback: first image found.
                if (chosen == null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        string n = names[i];
                        if (IsImagePath(n)) { chosen = n; break; }
                    }
                }

                if (!string.IsNullOrEmpty(uid))
                {
                    // Cap growth to avoid unbounded memory use.
                    if (_packagePreviewInternalPathCache.Count > 8000) _packagePreviewInternalPathCache.Clear();
                    _packagePreviewInternalPathCache[uid] = chosen;
                }

                return chosen;
            }
            catch
            {
                return null;
            }
        }

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
                    // Gate 1: wait for scroll to settle (1 s idle instead of 0.25 s — gives the
                    // image-loader background threads time to finish their own SaveThumbnail calls
                    // and release the cache write-lock before we add more pressure from the main thread).
                    if (Time.unscaledTime - lastScrollTime <= 1.0f)
                    {
                        yield return null;
                        continue;
                    }

                    // Gate 2: wait until the image loader has no thumbnails actively decoding.
                    // While the loader is busy its background threads are calling SaveThumbnail
                    // (holding the write-lock + doing disk flushes); adding our own saves on top
                    // causes severe lock contention and disk saturation.
                    if (CustomImageLoaderThreaded.singleton != null &&
                        CustomImageLoaderThreaded.singleton.PendingThumbnailCount > 0)
                    {
                        yield return null;
                        continue;
                    }

                    // Gate 3: skip this frame if we're already running slow (< ~40 FPS).
                    // ReadPixels + disk flush cost 10–50 ms; adding that to an already-slow
                    // frame makes scrolling impossible.
                    if (Time.unscaledDeltaTime > 0.025f)
                    {
                        yield return null;
                        continue;
                    }

                    ThumbnailCacheJob job = pendingThumbnailCacheJobs.Dequeue();
                    if (string.IsNullOrEmpty(job.Path) || job.Texture == null) { yield return null; continue; }
                    if (!string.IsNullOrEmpty(job.GroupId) && job.GroupId != currentLoadingGroupId) { yield return null; continue; }

                    yield return StartCoroutine(GalleryThumbnailCache.Instance.GenerateAndSaveThumbnailRoutine(job.Path, job.Texture, job.LastWriteTime));
                    _thumbCacheSaved++;

                    // Pause at least 2 frames between saves so ReadPixels/flush don't stack up
                    // back-to-back and starve the render thread.
                    yield return null;
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
            _thumbCacheTotalEnqueued++;
            _thumbCacheFinishTime = -1f;
            ShowThumbnailCacheProgress();
        }

        private void LoadThumbnail(FileEntry file, RawImage target)
        {
            // Skip thumbnails for missing/virtual entries
            if (file is VirtualFileEntry || file is MissingPackageListEntry)
            {
                ClearThumbnailTarget(target);
                return;
            }

            try
            {
                LoadThumbnailInternal(file, target);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] LoadThumbnail exception for file {file?.Name ?? "null"}: {ex}");
            }
        }

        private void LoadThumbnailInternal(FileEntry file, RawImage target)
        {
            // Skip thumbnails for missing/virtual entries - clear any existing texture
            if (file is VirtualFileEntry || file is MissingPackageListEntry)
            {
                ClearThumbnailTarget(target);
                return;
            }

            string imgPath = "";
            string lowerPath = file.Path.ToLowerInvariant();
            if (lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".png"))
            {
                imgPath = file.Path;
            }
            else if (file is PackageListEntry ple && ple.Package != null)
            {
                // For package list rows, pick an internal image (jpg/png) inside the .var.
                // IMPORTANT: do not request thumbnails from the .var file itself; that can trigger package
                // ensure-install paths and fails on some setups. Use an internal image path instead.
                string chosen = GetOrChoosePackagePreviewInternalPath(ple.Package);
                if (!string.IsNullOrEmpty(chosen))
                    imgPath = ple.Package.Path + ":/" + chosen.Replace('\\', '/');
            }
            else if (file is VarFileEntry vfe && vfe.Package != null)
            {
                // First try per-item sister file: same internal path but .jpg/.png extension.
                // This gives each clothing variation its own thumbnail instead of sharing the
                // package-wide preview image.
                string internalNoExt = System.IO.Path.GetFileNameWithoutExtension(vfe.InternalPath);
                string internalDir   = System.IO.Path.GetDirectoryName(vfe.InternalPath);
                string baseInternal  = string.IsNullOrEmpty(internalDir)
                    ? internalNoExt
                    : internalDir.Replace('\\', '/') + "/" + internalNoExt;

                string sisterJpg = vfe.Package.Path + ":/" + baseInternal + ".jpg";
                string sisterPng = vfe.Package.Path + ":/" + baseInternal + ".png";

                if (FileManager.FileExists(sisterJpg))
                    imgPath = sisterJpg;
                else if (FileManager.FileExists(sisterPng))
                    imgPath = sisterPng;
                else
                {
                    // Fall back to the package-wide preview image
                    string chosen = GetOrChoosePackagePreviewInternalPath(vfe.Package);
                    if (!string.IsNullOrEmpty(chosen))
                        imgPath = vfe.Package.Path + ":/" + chosen.Replace('\\', '/');
                }
            }
            else
            {
                // Sister-file rule: same name, .jpg or .png extension
                // Optimized discovery via archive flattening (FileManager.FileExists)
                try
                {
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
                catch (ArgumentException)
                {
                    // file.Path contains invalid characters (e.g., from VarFileEntry with internal paths)
                    // Skip sister-file lookup for such paths
                }
            }

            // IMPORTANT: if we can't resolve a thumbnail path for this row, explicitly clear any
            // previous binding/texture so recycled list rows don't show stale thumbnails.
            if (string.IsNullOrEmpty(imgPath))
            {
                ClearThumbnailTarget(target);
                return;
            }

            // Debug Log
            // LogUtil.Log($"[VPB] LoadThumbnail requested for {file.Name} (GroupId: {currentLoadingGroupId})");

            if (CustomImageLoaderThreaded.singleton == null) return;

            string capturedGroupId = currentLoadingGroupId;
            string expectedTag = capturedGroupId + "|" + imgPath;
            ThumbnailBindingTag bind = null;
            if (target != null)
            {
                bind = target.GetComponent<ThumbnailBindingTag>();
                if (bind == null) bind = target.gameObject.AddComponent<ThumbnailBindingTag>();
                bind.ExpectedTag = expectedTag;

                if (bind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                {
                    CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(bind.CurrentTexture);
                    bind.CurrentTexture = null;
                }
            }

            // 1. Memory Cache
            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
            if (tex != null)
            {
                if (bind != null)
                {
                    bind.CurrentTexture = tex;
                    CustomImageLoaderThreaded.singleton.RegisterThumbnailUse(tex);
                }
                target.texture = tex;
                target.color = Color.white;
                UpdateAspectRatio(target, tex);
                return;
            }

            // 3. Request Load
            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = imgPath;
            qi.isThumbnail = true;
            qi.compress = false;
            qi.priority = _nextThumbPriority;
            qi.groupId = currentLoadingGroupId;
            qi.callback = (res) => {
                if (res != null && res.tex != null)
                {
                    ThumbnailBindingTag cbBind = null;
                    if (target != null) cbBind = target.GetComponent<ThumbnailBindingTag>();
                    if (cbBind != null && cbBind.ExpectedTag == expectedTag)
                    {
                        if (cbBind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                        {
                            CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(cbBind.CurrentTexture);
                        }
                        cbBind.CurrentTexture = res.tex;
                        if (CustomImageLoaderThreaded.singleton != null)
                        {
                            CustomImageLoaderThreaded.singleton.RegisterThumbnailUse(res.tex);
                        }
                        target.texture = res.tex;
                        target.color = Color.white;
                        UpdateAspectRatio(target, res.tex);
                    }

                    long imgTime = 0;
                    if (GalleryThumbnailCache.Instance.IsPackagePath(imgPath))
                    {
                        imgTime = 0;
                    }
                    else if (imgPath == file.Path)
                    {
                        imgTime = file.LastWriteTime.ToFileTime();
                    }
                    else
                    {
                        FileEntry fe = FileManager.GetFileEntry(imgPath);
                        if (fe != null) imgTime = fe.LastWriteTime.ToFileTime();
                        else imgTime = file.LastWriteTime.ToFileTime();
                    }

                    if (!res.loadedFromGalleryCache && capturedGroupId == currentLoadingGroupId)
                    {
                        EnqueueThumbnailCacheJob(imgPath, res.tex, imgTime, capturedGroupId);
                    }
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }

        private static void ClearThumbnailTarget(RawImage target)
        {
            if (target == null) return;
            try
            {
                var bind = target.GetComponent<ThumbnailBindingTag>();
                if (bind != null)
                {
                    bind.ExpectedTag = null;
                    if (bind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                    {
                        CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(bind.CurrentTexture);
                    }
                    bind.CurrentTexture = null;
                }

                target.texture = null;
                if (target.material != null) target.material.mainTexture = null;
                target.color = new Color(0, 0, 0, 0);
            }
            catch { }
        }

        private void UpdateAspectRatio(RawImage target, Texture tex)
        {
            if (target == null || tex == null) return;
            AspectRatioFitter arf = target.GetComponent<AspectRatioFitter>();
            if (arf != null)
            {
                arf.aspectRatio = (float)tex.width / tex.height;
            }
        }
    }
}
