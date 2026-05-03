using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    internal class ThumbnailBindingTag : MonoBehaviour
    {
        public string ExpectedTag;
        public Texture2D CurrentTexture;
        /// <summary>Decode retries for this binding (resets when ExpectedTag changes or load succeeds).</summary>
        public int ThumbRetryCount;

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
        private const int MaxThumbnailDecodeRetries = 3;
        private const float ThumbnailHangWatchDelaySec = 0.35f;

        // Cache for package list thumbnails: package UID -> internal image path (within the package).
        // Keeps package preview lookups cheap while scrolling.
        private readonly Dictionary<string, string> _packagePreviewInternalPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool IsImagePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.ToLowerInvariant();
            return p.EndsWith(".jpg") || p.EndsWith(".jpeg") || p.EndsWith(".png");
        }

        /// <summary>SQLite / ALL VAR rows often use relative var paths; <see cref="FileManager.GetPackage"/> expects package UID.</summary>
        private static string CanonicalVarPackageUidFromPathOrHint(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Trim().Replace('\\', '/');
            if (s.Length == 0) return null;
            if (s.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) s = s.Substring("AddonPackages/".Length);
            else if (s.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) s = s.Substring("AllPackages/".Length);
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            if (s.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            else if (s.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static void AppendUniquePackageLookupKey(List<string> keys, string hint)
        {
            if (keys == null || string.IsNullOrEmpty(hint)) return;
            string a = hint.Trim();
            if (a.Length == 0) return;
            string b = CanonicalVarPackageUidFromPathOrHint(a);
            string[] two = new string[] { a, b };
            for (int ti = 0; ti < two.Length; ti++)
            {
                string cand = two[ti];
                if (string.IsNullOrEmpty(cand)) continue;
                bool dup = false;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (string.Equals(keys[i], cand, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) keys.Add(cand);
            }
        }

        private static VarPackage TryResolveVarPackageForPackageListEntry(PackageListEntry ple)
        {
            if (ple == null) return null;
            VarPackage pkg = null;
            try { pkg = ple.Package; } catch { pkg = null; }
            if (pkg != null) return pkg;

            List<string> keys = new List<string>(4);
            try { AppendUniquePackageLookupKey(keys, ple.GetPackageUidForGalleryUserTags()); } catch { }
            try { AppendUniquePackageLookupKey(keys, ple.Path); } catch { }
            try { AppendUniquePackageLookupKey(keys, ple.Uid); } catch { }

            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (string.IsNullOrEmpty(k)) continue;
                try { pkg = FileManager.GetPackage(k, ensureInstalled: false); } catch { pkg = null; }
                if (pkg != null) return pkg;
                try { pkg = FileManager.GetPackageForDependency(k, false); } catch { pkg = null; }
                if (pkg != null) return pkg;
            }
            return null;
        }

        /// <returns>Best non-image sibling extension priority for this basename group (lower = better).</returns>
        private static int SisterExtRank(string extWithDotLower)
        {
            if (string.IsNullOrEmpty(extWithDotLower)) return 50;
            if (extWithDotLower == ".vam") return 0;
            if (extWithDotLower == ".vac") return 1;
            if (extWithDotLower == ".json") return 2;
            if (extWithDotLower == ".vap") return 3;
            if (extWithDotLower == ".vab") return 4;
            if (extWithDotLower == ".cs") return 20;
            if (extWithDotLower == ".dll") return 20;
            return 15;
        }

        private static bool IsNonImageSiblingExt(string extWithDotLower)
        {
            if (string.IsNullOrEmpty(extWithDotLower)) return false;
            return extWithDotLower != ".jpg" && extWithDotLower != ".jpeg" && extWithDotLower != ".png";
        }

        private struct VarInternalMember
        {
            public string FullPathNorm;
            public string ExtLower;
            public bool IsImage;
        }

        /// <summary>
        /// Package-row preview: scan var index for <c>foo.jpg</c> + same-folder non-image sibling <c>foo.*</c> (e.g. <c>foo.vam</c>). No filesystem walk.
        /// </summary>
        private static string PickPackagePreviewInternalPathFromFileList(List<string> names)
        {
            if (names == null || names.Count == 0) return null;

            var groups = new Dictionary<string, List<VarInternalMember>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string norm = n.Replace('\\', '/');
                try
                {
                    string dir = Path.GetDirectoryName(norm);
                    if (!string.IsNullOrEmpty(dir)) dir = dir.Replace('\\', '/');
                    else dir = "";

                    string leaf = Path.GetFileName(norm);
                    if (string.IsNullOrEmpty(leaf)) continue;
                    string baseNo = Path.GetFileNameWithoutExtension(leaf);
                    if (string.IsNullOrEmpty(baseNo)) continue;
                    string ext = Path.GetExtension(leaf).ToLowerInvariant();
                    if (string.IsNullOrEmpty(ext)) continue;
                    bool isImg = ext == ".jpg" || ext == ".jpeg" || ext == ".png";
                    string key = dir + "|" + baseNo;
                    List<VarInternalMember> list;
                    if (!groups.TryGetValue(key, out list) || list == null)
                    {
                        list = new List<VarInternalMember>(2);
                        groups[key] = list;
                    }
                    VarInternalMember vm;
                    vm.FullPathNorm = norm;
                    vm.ExtLower = ext;
                    vm.IsImage = isImg;
                    list.Add(vm);
                }
                catch { }
            }

            string bestImg = null;
            int bestScore = int.MaxValue;
            string tieBreak = null;

            foreach (KeyValuePair<string, List<VarInternalMember>> kvp in groups)
            {
                List<VarInternalMember> list = kvp.Value;
                if (list == null || list.Count < 2) continue;

                bool hasNonImage = false;
                int bestSisterRank = int.MaxValue;
                for (int j = 0; j < list.Count; j++)
                {
                    VarInternalMember m = list[j];
                    if (!m.IsImage && IsNonImageSiblingExt(m.ExtLower))
                    {
                        hasNonImage = true;
                        int r = SisterExtRank(m.ExtLower);
                        if (r < bestSisterRank) bestSisterRank = r;
                    }
                }
                if (!hasNonImage || bestSisterRank >= int.MaxValue) continue;

                for (int j = 0; j < list.Count; j++)
                {
                    VarInternalMember m = list[j];
                    if (!m.IsImage || !IsImagePath(m.FullPathNorm)) continue;
                    string imgPath = m.FullPathNorm;
                    int score = bestSisterRank * 10000 + imgPath.Length;
                    if (score < bestScore || (score == bestScore && (tieBreak == null || string.CompareOrdinal(imgPath, tieBreak) < 0)))
                    {
                        bestScore = score;
                        bestImg = imgPath;
                        tieBreak = imgPath;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestImg)) return bestImg;

            // Legacy: preview-ish names first, then first image.
            string chosenLegacy = null;
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!IsImagePath(n)) continue;
                string ln = n.ToLowerInvariant();
                if (ln.Contains("preview") || ln.Contains("thumbnail") || ln.Contains("thumb") || ln.Contains("screenshot"))
                {
                    chosenLegacy = n;
                    break;
                }
            }
            if (chosenLegacy == null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (IsImagePath(n)) return n;
                }
            }
            return chosenLegacy;
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

                // Sister rule (foo.jpg ↔ foo.vam / any non-image) then legacy preview-name / first-image.
                string chosen = PickPackagePreviewInternalPathFromFileList(names);

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
            /// <summary>Matches <see cref="CustomImageLoaderThreaded.QueuedImage.turboJpegScaleDenom"/> for disk cache key <c>|tjN</c>.</summary>
            public int TurboJpegScaleDenom;
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

                    yield return StartCoroutine(GalleryThumbnailCache.Instance.GenerateAndSaveThumbnailRoutine(job.Path, job.Texture, job.LastWriteTime, job.TurboJpegScaleDenom));
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

        private void EnqueueThumbnailCacheJob(string path, Texture2D tex, long lastWriteTime, string groupId, int turboJpegScaleDenom)
        {
            if (pendingThumbnailCacheJobs == null) pendingThumbnailCacheJobs = new Queue<ThumbnailCacheJob>();
            pendingThumbnailCacheJobs.Enqueue(new ThumbnailCacheJob { Path = path, Texture = tex, LastWriteTime = lastWriteTime, GroupId = groupId, TurboJpegScaleDenom = turboJpegScaleDenom });
            _thumbCacheTotalEnqueued++;
            _thumbCacheFinishTime = -1f;
            ShowThumbnailCacheProgress();
        }

        private void LoadThumbnail(FileEntry file, RawImage target, bool gridThumbnailContext = true, int turboJpegThumbnailDenom = 0, bool thumbnailUnityDecodeOnly = false)
        {
            if (file is MissingPackageListEntry)
            {
                ClearThumbnailTarget(target);
                return;
            }

            if (file is VirtualFileEntry vfeOuter)
            {
                string thumbUrl;
                if (_hubThumbnailUrlCache.TryGetValue(vfeOuter.Uid, out thumbUrl) && !string.IsNullOrEmpty(thumbUrl))
                    LoadHubThumbnailToTarget(thumbUrl, vfeOuter.Uid, target);
                else
                    ClearThumbnailTarget(target);
                return;
            }

            try
            {
                LoadThumbnailInternal(file, target, gridThumbnailContext, turboJpegThumbnailDenom, thumbnailUnityDecodeOnly);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] LoadThumbnail exception for file {file?.Name ?? "null"}: {ex}");
            }
        }

        /// <summary>Plugin script paths under Custom/Scripts.</summary>
        private static bool IsPluginScriptGalleryFile(FileEntry file)
        {
            if (file == null || string.IsNullOrEmpty(file.Path)) return false;
            string p = file.Path.Replace('\\', '/');
            if (p.IndexOf("Custom/Scripts/", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string lower = p.ToLowerInvariant();
            return lower.EndsWith(".cs") || lower.EndsWith(".cslist") || lower.EndsWith(".dll");
        }

        private void LoadThumbnailInternal(FileEntry file, RawImage target, bool gridThumbnailContext, int turboJpegThumbnailDenom, bool thumbnailUnityDecodeOnly)
        {
            // Virtual/missing entries are handled before reaching here
            if (file is VirtualFileEntry || file is MissingPackageListEntry)
            {
                ClearThumbnailTarget(target);
                return;
            }

            if (gridThumbnailContext &&
                VPBConfig.Instance != null &&
                !VPBConfig.Instance.PluginGalleryGridThumbnails &&
                IsPluginScriptGalleryFile(file))
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
            else if (file is CleanupFileEntry cfe && cfe.Candidate != null)
            {
                var cand = cfe.Candidate;
                if (cand.SourceKind == CleanupCandidateSourceKind.VarPackage)
                {
                    VarPackage pkg = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(cand.PackageUid))
                            pkg = FileManager.GetPackageForDependency(cand.PackageUid, false);
                    }
                    catch { pkg = null; }

                    if (pkg != null)
                    {
                        string chosen = GetOrChoosePackagePreviewInternalPath(pkg);
                        if (!string.IsNullOrEmpty(chosen))
                            imgPath = pkg.Path + ":/" + chosen.Replace('\\', '/');
                    }
                }
                else
                {
                    // Local cleanup rows: prefer sidecar image next to source (.json -> .jpg/.png).
                    try
                    {
                        string testJpg = Path.ChangeExtension(file.Path, ".jpg");
                        if (File.Exists(testJpg) || FileManager.FileExists(testJpg))
                        {
                            imgPath = testJpg;
                        }
                        else
                        {
                            string testPng = Path.ChangeExtension(file.Path, ".png");
                            if (File.Exists(testPng) || FileManager.FileExists(testPng))
                                imgPath = testPng;
                        }
                    }
                    catch { }
                }
            }
            else if (file is PackageListEntry ple)
            {
                // Package list row: resolve VarPackage then pick internal preview (sister JPG/PNG + non-image sibling, etc.).
                VarPackage pkg = TryResolveVarPackageForPackageListEntry(ple);
                if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
                {
                    string chosen = GetOrChoosePackagePreviewInternalPath(pkg);
                    if (!string.IsNullOrEmpty(chosen))
                        imgPath = pkg.Path + ":/" + chosen.Replace('\\', '/');
                }
            }
            else if (file is VarFileEntry vfe)
            {
                // Avoid resolving packages while scrolling search results (can stall/crash).
                // Prefer deriving package path from vfe.Path (indexed) and use sister-image rule only.
                string pkgPath = null;
                try
                {
                    string p = vfe.Path ?? "";
                    int split = p.IndexOf(":/", StringComparison.Ordinal);
                    if (split > 0) pkgPath = p.Substring(0, split);
                }
                catch { pkgPath = null; }
                if (string.IsNullOrEmpty(pkgPath))
                {
                    // If already resolved elsewhere, allow richer fallback.
                    try
                    {
                        var pkg2 = vfe.Package;
                        if (pkg2 != null && !string.IsNullOrEmpty(pkg2.Path)) pkgPath = pkg2.Path;
                    }
                    catch { pkgPath = null; }
                }
                if (string.IsNullOrEmpty(pkgPath))
                {
                    ClearThumbnailTarget(target);
                    return;
                }

                // First try per-item sister file: same internal path but .jpg/.png extension.
                // This gives each clothing variation its own thumbnail instead of sharing the
                // package-wide preview image.
                string internalNoExt = System.IO.Path.GetFileNameWithoutExtension(vfe.InternalPath);
                string internalDir   = System.IO.Path.GetDirectoryName(vfe.InternalPath);
                string baseInternal  = string.IsNullOrEmpty(internalDir)
                    ? internalNoExt
                    : internalDir.Replace('\\', '/') + "/" + internalNoExt;

                string sisterJpg = pkgPath + ":/" + baseInternal + ".jpg";
                string sisterPng = pkgPath + ":/" + baseInternal + ".png";

                if (FileManager.FileExists(sisterJpg))
                    imgPath = sisterJpg;
                else if (FileManager.FileExists(sisterPng))
                    imgPath = sisterPng;
                else
                {
                    // Package-wide preview requires package resolve; skip to keep scroll cheap/stable.
                    // If caller already resolved package, they will hit the PackageListEntry path for package rows.
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
                try
                {
                    LogUtil.LogTextureTrace("THUMB_NO_PATH:" + (file != null ? file.Path : "null"),
                        "[VPB THUMB] resolve imgPath failed. fileType=" + (file != null ? file.GetType().Name : "null") + " filePath=" + (file != null ? file.Path : "null"));
                }
                catch { }
                ClearThumbnailTarget(target);
                return;
            }

            try
            {
                LogUtil.LogTextureTrace("THUMB_REQ:" + imgPath,
                    "[VPB THUMB] request. fileType=" + (file != null ? file.GetType().Name : "null") +
                    " filePath=" + (file != null ? file.Path : "null") +
                    " imgPath=" + imgPath +
                    " isPkgPath=" + (GalleryThumbnailCache.Instance != null && GalleryThumbnailCache.Instance.IsPackagePath(imgPath)) +
                    " unityOnly=" + thumbnailUnityDecodeOnly);
            }
            catch { }

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

                // Rebinding the same visible item after a hide/show should keep the current
                // thumbnail in place. Otherwise the grid briefly blanks every image, then
                // immediately restores it from cache, which looks like a full redraw.
                if (bind.ExpectedTag == expectedTag && bind.CurrentTexture != null && target.texture == bind.CurrentTexture)
                {
                    target.color = Color.white;
                    UpdateAspectRatio(target, bind.CurrentTexture);
                    return;
                }

                if (bind.ExpectedTag != expectedTag)
                    bind.ThumbRetryCount = 0;
                bind.ExpectedTag = expectedTag;

                if (bind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                {
                    CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(bind.CurrentTexture);
                    bind.CurrentTexture = null;
                }

                // New binding: immediately blank old texture so pooled rows never "flash" stale previews
                // while async load resolves (notably visible in ALL VAR package list).
                try
                {
                    target.texture = null;
                    if (target.material != null) target.material.mainTexture = null;
                    target.color = new Color(0, 0, 0, 0);
                }
                catch { }
            }

            // 1. Memory Cache (tier: optional full-res for hover; else TurboJPEG scale from grid columns)
            int thumbTd = turboJpegThumbnailDenom > 0
                ? TurboJpegNative.NormalizeScaleDenom(turboJpegThumbnailDenom)
                : TurboJpegNative.ScaleDenomFromGridColumns(EffectiveGridColumnsForThumbDecode());
            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath, thumbTd, thumbnailUnityDecodeOnly);
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

            QueueThumbnailDecode(file, target, imgPath, expectedTag, capturedGroupId, skipCache: false, scheduleHangWatchdog: true, turboJpegScaleDenom: thumbTd, thumbnailUnityDecodeOnly: thumbnailUnityDecodeOnly);
        }

        private void QueueThumbnailDecode(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, bool skipCache, bool scheduleHangWatchdog, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly)
        {
            if (CustomImageLoaderThreaded.singleton == null || target == null) return;

            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = imgPath;
            qi.isThumbnail = true;
            qi.turboJpegScaleDenom = turboJpegScaleDenom;
            qi.thumbnailUnityDecodeOnly = thumbnailUnityDecodeOnly;
            qi.compress = false;
            qi.skipCache = skipCache;
            qi.priority = skipCache ? Mathf.Min(-2, _nextThumbPriority - 30) : _nextThumbPriority;
            qi.groupId = currentLoadingGroupId;
            qi.callback = (res) =>
            {
                if (res != null && res.tex != null && !res.cancel)
                {
                    ThumbnailBindingTag cbBind = target.GetComponent<ThumbnailBindingTag>();
                    if (cbBind != null && cbBind.ExpectedTag == expectedTag)
                    {
                        if (cbBind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
                            CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(cbBind.CurrentTexture);
                        cbBind.CurrentTexture = res.tex;
                        cbBind.ThumbRetryCount = 0;
                        if (CustomImageLoaderThreaded.singleton != null)
                            CustomImageLoaderThreaded.singleton.RegisterThumbnailUse(res.tex);
                        target.texture = res.tex;
                        target.color = Color.white;
                        UpdateAspectRatio(target, res.tex);
                    }

                    long imgTime = 0;
                    if (GalleryThumbnailCache.Instance.IsPackagePath(imgPath))
                        imgTime = 0;
                    else if (imgPath == file.Path)
                        imgTime = file.LastWriteTime.ToFileTime();
                    else
                    {
                        FileEntry fe = FileManager.GetFileEntry(imgPath);
                        if (fe != null) imgTime = fe.LastWriteTime.ToFileTime();
                        else imgTime = file.LastWriteTime.ToFileTime();
                    }

                    if (!res.loadedFromGalleryCache && capturedGroupId == currentLoadingGroupId)
                        EnqueueThumbnailCacheJob(imgPath, res.tex, imgTime, capturedGroupId, res.turboJpegScaleDenom);
                    return;
                }

                if (res != null && res.cancel) return;
                ThumbnailBindingTag failBind = target.GetComponent<ThumbnailBindingTag>();
                if (failBind == null || failBind.ExpectedTag != expectedTag) return;
                if (capturedGroupId != currentLoadingGroupId) return;
                RequestThumbnailRetryAfterFailure(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly);
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
            if (scheduleHangWatchdog)
                StartCoroutine(ThumbnailHangWatchdogCo(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly));
        }

        private void RequestThumbnailRetryAfterFailure(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly)
        {
            if (target == null) return;
            ThumbnailBindingTag b = target.GetComponent<ThumbnailBindingTag>();
            if (b == null || b.ExpectedTag != expectedTag) return;
            if (b.ThumbRetryCount >= MaxThumbnailDecodeRetries) return;
            b.ThumbRetryCount++;
            StartCoroutine(ThumbnailRetryAfterDelayCo(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly));
        }

        private IEnumerator ThumbnailRetryAfterDelayCo(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly)
        {
            yield return new WaitForSecondsRealtime(0.02f);
            if (target == null) yield break;
            ThumbnailBindingTag b = target.GetComponent<ThumbnailBindingTag>();
            if (b == null || b.ExpectedTag != expectedTag) yield break;
            if (capturedGroupId != currentLoadingGroupId) yield break;
            if (target.texture != null)
            {
                if (b.ThumbRetryCount > 0) b.ThumbRetryCount--;
                yield break;
            }
            if (CustomImageLoaderThreaded.singleton == null) yield break;
            CustomImageLoaderThreaded.singleton.ClearCacheThumbnail(imgPath, turboJpegScaleDenom, thumbnailUnityDecodeOnly);
            QueueThumbnailDecode(file, target, imgPath, expectedTag, capturedGroupId, skipCache: true, scheduleHangWatchdog: false, turboJpegScaleDenom: turboJpegScaleDenom, thumbnailUnityDecodeOnly: thumbnailUnityDecodeOnly);
        }

        private IEnumerator ThumbnailHangWatchdogCo(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly)
        {
            yield return new WaitForSecondsRealtime(ThumbnailHangWatchDelaySec);
            if (target == null) yield break;
            ThumbnailBindingTag b = target.GetComponent<ThumbnailBindingTag>();
            if (b == null || b.ExpectedTag != expectedTag) yield break;
            if (capturedGroupId != currentLoadingGroupId) yield break;
            if (target.texture != null) yield break;
            if (b.ThumbRetryCount > 0) yield break;
            RequestThumbnailRetryAfterFailure(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly);
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

        /// <summary>
        /// Downloads and applies a Hub CDN thumbnail URL to a gallery RawImage target.
        /// Uses HubImageLoaderThreaded so it benefits from its in-memory cache and download queue.
        /// Uses ThumbnailBindingTag to avoid applying stale textures to recycled list rows.
        /// </summary>
        private void LoadHubThumbnailToTarget(string thumbUrl, string uid, RawImage target)
        {
            if (string.IsNullOrEmpty(thumbUrl) || target == null) return;
            if (HubImageLoaderThreaded.singleton == null) { ClearThumbnailTarget(target); return; }

            string expectedTag = "hub|" + uid;

            ThumbnailBindingTag bind = target.GetComponent<ThumbnailBindingTag>();
            if (bind == null) bind = target.gameObject.AddComponent<ThumbnailBindingTag>();

            // Already showing this Hub thumbnail — keep it
            if (bind.ExpectedTag == expectedTag && target.texture != null)
            {
                target.color = Color.white;
                return;
            }

            // Release any previously bound local texture before switching to a Hub one
            if (bind.CurrentTexture != null && CustomImageLoaderThreaded.singleton != null)
            {
                CustomImageLoaderThreaded.singleton.DeregisterThumbnailUse(bind.CurrentTexture);
                bind.CurrentTexture = null;
            }
            bind.ExpectedTag = expectedTag;

            HubImageLoaderThreaded.QueuedImage qi = HubImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = thumbUrl;
            qi.isThumbnail = true;
            qi.groupId = currentLoadingGroupId;
            qi.callback = (res) => {
                if (res?.tex == null) return;
                if (target == null) return;
                ThumbnailBindingTag cbBind = target.GetComponent<ThumbnailBindingTag>();
                if (cbBind == null || cbBind.ExpectedTag != expectedTag) return;
                target.texture = res.tex;
                target.color = Color.white;
                UpdateAspectRatio(target, res.tex);
            };
            HubImageLoaderThreaded.singleton.QueueThumbnailImmediate(qi);
        }
    }
}
