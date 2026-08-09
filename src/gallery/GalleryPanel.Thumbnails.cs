using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private const float ThumbnailHangWatchMaxDelaySec = 1.50f;
        private const float ThumbnailHangWatchScrollQuietSec = 0.25f;
        private const int AllVarThumbQueuePressureThreshold = 80;
        private static readonly Color ThumbnailPlaceholderBackdrop = new Color(0.25f, 0.25f, 0.25f, 0.55f);

        // Cache for package list thumbnails: package UID -> internal image path (within the package).
        // Keeps package preview lookups cheap while scrolling.
        private readonly Dictionary<string, string> _packagePreviewInternalPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Cache for fast ALL VAR sister JPG existence checks: package UID -> set of internal .jpg paths (normalized, no leading "/").
        private readonly Dictionary<string, HashSet<string>> _packageInternalJpgSetCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        // Loose-file (local scene) sister JPG: source Path -> jpg path, or "" if none. Avoids FileExists per scroll bind.
        private readonly Dictionary<string, string> _looseSisterJpgPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Loose thumb imgPath -> last-write filetime. Avoids GetFullPath/Exists/mtime per scroll bind.
        private readonly Dictionary<string, long> _looseThumbWriteTimeCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gallery thumbnails / previews: <c>.jpg</c> only (no <c>.png</c> / <c>.jpeg</c> probes).</summary>
        private static bool IsImagePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>pkg.var_path</c> should be the .var on disk; if wrong column / bad data yields an internal image path,
        /// do not use it for <see cref="FileManager.GetPackage"/> or loose-file thumbnail shortcuts (ALL VAR grid).
        /// </summary>
        private static bool IndexedVarPathHintLooksUsableForPackageResolve(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            string n = p.Trim().Replace('\\', '/');
            if (n.Length == 0) return false;
            if (n.IndexOf(":/", StringComparison.Ordinal) >= 0) return true;
            if (IsImagePath(n)) return false;
            string nl = n.ToLowerInvariant();
            if (nl.EndsWith(".png", StringComparison.Ordinal) || nl.EndsWith(".jpeg", StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Gallery <see cref="VarFileEntry.Path"/>: <c>pkg.var:/internal</c> or bare <c>…/pkg.var</c> (e.g. SQLite <c>meta.json</c> row uses var_path only — no <c>:/</c>).
        /// </summary>
        private static bool TryGetVarPackageRootPathFromGalleryPath(string galleryPath, out string pkgRoot)
        {
            pkgRoot = null;
            if (string.IsNullOrEmpty(galleryPath)) return false;
            try
            {
                string p = galleryPath.Trim().Replace('\\', '/');
                int split = p.IndexOf(":/", StringComparison.Ordinal);
                if (split > 0)
                {
                    pkgRoot = p.Substring(0, split);
                    return !string.IsNullOrEmpty(pkgRoot);
                }
                if (p.EndsWith(".var", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    pkgRoot = p;
                    return true;
                }
            }
            catch { pkgRoot = null; }
            return false;
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
            string rowPath = null;
            try { rowPath = ple.Path; } catch { rowPath = null; }
            if (IndexedVarPathHintLooksUsableForPackageResolve(rowPath))
                try { AppendUniquePackageLookupKey(keys, rowPath); } catch { }
            try
            {
                string u = ple.Uid;
                if (!string.IsNullOrEmpty(u) && !string.Equals(u, rowPath, StringComparison.OrdinalIgnoreCase))
                    AppendUniquePackageLookupKey(keys, u);
            }
            catch { }

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

        private static bool IsNonImageSiblingExt(string extWithDotLower)
        {
            if (string.IsNullOrEmpty(extWithDotLower)) return false;
            return extWithDotLower != ".jpg" && extWithDotLower != ".jpeg" && extWithDotLower != ".png";
        }

        /// <summary>
        /// <c>Saves/scene</c> and any nested folder (e.g. <c>Saves/scene/Sharr LOOKS/*.jpg</c>). Uses <see cref="NormalizeVarInternalEntryPath"/>.
        /// </summary>
        private static bool IsUnderSavesSceneTree(string pathNormalizedOrRaw)
        {
            if (string.IsNullOrEmpty(pathNormalizedOrRaw)) return false;
            string n = NormalizeVarInternalEntryPath(pathNormalizedOrRaw);
            if (string.IsNullOrEmpty(n)) return false;
            return string.Equals(n, "Saves/scene", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// VAR zip paths: parent via last <c>/</c> only (Unicode, apostrophe, leading spaces in folder names — e.g.
        /// <c>Custom/Hair/Female/Miki/ C├┤te d'Azur Hair/file.jpg</c>). Avoids Windows <see cref="Path.GetDirectoryName"/> mangling.
        /// </summary>
        private static string GetInternalPathParentDirectory(string normSlashPath)
        {
            if (string.IsNullOrEmpty(normSlashPath)) return "";
            string n = NormalizeVarInternalEntryPath(normSlashPath);
            int li = n.LastIndexOf('/');
            if (li > 0)
                return n.Substring(0, li);
            try
            {
                string mixed = n.Replace('/', Path.DirectorySeparatorChar);
                string d = Path.GetDirectoryName(mixed);
                if (!string.IsNullOrEmpty(d))
                    return NormalizeVarInternalEntryPath(d.Replace('\\', '/'));
            }
            catch { }
            return "";
        }

        /// <summary>File name segment after last <c>/</c> (zip-internal; same rules as <see cref="GetInternalPathParentDirectory"/>).</summary>
        private static string GetZipInternalLeafFileName(string normSlashPath)
        {
            if (string.IsNullOrEmpty(normSlashPath)) return "";
            string n = NormalizeVarInternalEntryPath(normSlashPath);
            int li = n.LastIndexOf('/');
            if (li < 0) return n;
            if (li >= n.Length - 1) return "";
            return n.Substring(li + 1);
        }

        /// <summary>Matches zip/FileEntry paths with <see cref="FileManager"/> (<c>pkg:/</c>) — trim leading slash, unify slashes. (No Unicode NFC: not on .NET 3.5 ref Assemblies.)</summary>
        private static string NormalizeVarInternalEntryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string n = path.Replace('\\', '/').TrimStart('/');
            for (int guard = 0; guard < 64 && n.IndexOf("//", StringComparison.Ordinal) >= 0; guard++)
                n = n.Replace("//", "/");
            return n;
        }

        /// <summary>Heuristic: UTF-8 bytes were decoded as ISO-8859-1 (common <c>Ã´</c> vs <c>ô</c> in SQLite vs zip).</summary>
        private static bool LooksLikeUtf8MisreadAsLatin1(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\uFFFD') return true;
                if (c == 'Ã' || c == 'Â' || c == 'Ä' || c == 'Å' || c == 'Ð' || c == 'Ñ' || c == 'Ó')
                    return true;
            }
            return false;
        }

        /// <summary>Repair UTF-8 bytes misread as legacy 8-bit encoding (per segment only — full paths mix UTF-8 + mojibake).</summary>
        private static string TryRepairUtf8MisreadAsLatin1(string segmentNoSlashes)
        {
            if (string.IsNullOrEmpty(segmentNoSlashes)) return segmentNoSlashes;
            try
            {
                Encoding latin1 = Encoding.GetEncoding("iso-8859-1");
                byte[] bytes = latin1.GetBytes(segmentNoSlashes);
                string repaired = Encoding.UTF8.GetString(bytes);
                if (!string.IsNullOrEmpty(repaired) && repaired.IndexOf('\uFFFD') < 0)
                    return repaired;
                Encoding cp1252 = Encoding.GetEncoding(1252);
                bytes = cp1252.GetBytes(segmentNoSlashes);
                repaired = Encoding.UTF8.GetString(bytes);
                if (!string.IsNullOrEmpty(repaired) && repaired.IndexOf('\uFFFD') < 0)
                    return repaired;
            }
            catch { }
            return segmentNoSlashes;
        }

        /// <summary>Repair each <c>/</c> segment separately so mixed UTF-8 + mojibake (e.g. <c>d'Azur</c> + <c>CÃ´te</c>) does not corrupt the path.</summary>
        private static string NormalizeVarInternalPathForThumbKeys(string path)
        {
            string n = NormalizeVarInternalEntryPath(path);
            if (string.IsNullOrEmpty(n)) return n;
            StringBuilder sb = new StringBuilder(n.Length + 16);
            int start = 0;
            for (int i = 0; i <= n.Length; i++)
            {
                if (i < n.Length && n[i] != '/')
                    continue;
                string seg = n.Substring(start, i - start);
                if (LooksLikeUtf8MisreadAsLatin1(seg))
                {
                    string r = TryRepairUtf8MisreadAsLatin1(seg);
                    if (!string.IsNullOrEmpty(r) && r.IndexOf('\uFFFD') < 0)
                        seg = r;
                }
                if (sb.Length > 0) sb.Append('/');
                sb.Append(seg);
                start = i + 1;
            }
            return sb.ToString();
        }

        private static bool PathsEqualWithUtf8Latin1Alias(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string ac = NormalizeVarInternalPathForThumbKeys(a);
            string bc = NormalizeVarInternalPathForThumbKeys(b);
            if (string.Equals(ac, bc, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(ac, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(a, bc, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Returns zip-listed path string to pass to <c>pkg:/</c> (canonical member from set).</summary>
        private static string FindMatchingInternalJpgPathInSet(HashSet<string> set, string keyNorm)
        {
            if (set == null || string.IsNullOrEmpty(keyNorm)) return null;
            if (set.Contains(keyNorm)) return keyNorm;
            foreach (string member in set)
            {
                if (PathsEqualWithUtf8Latin1Alias(member, keyNorm)) return member;
            }
            return null;
        }

        private struct VarInternalMember
        {
            public string FullPathNorm;
            public string ExtLower;
            public bool IsImage;
        }

        /// <summary>
        /// Package-row preview: sister pairs (<c>foo.jpg</c> + non-image <c>foo.*</c>) — first match in entry order.
        /// EVERYTHING mode: prefer pairs under <c>Saves/scene</c>, then other pairs; orphan <c>.jpg</c> prefers <c>Saves/scene</c> path.
        /// </summary>
        private static string PickPackagePreviewInternalPathFromFileList(List<string> names, bool prioritizeSavesSceneForEverything)
        {
            if (names == null || names.Count == 0) return null;

            var groups = new Dictionary<string, List<VarInternalMember>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string normRaw = NormalizeVarInternalEntryPath(n);
                if (normRaw.Length == 0) continue;
                string normKey = NormalizeVarInternalPathForThumbKeys(n);
                try
                {
                    string dir = GetInternalPathParentDirectory(normKey);

                    string leaf = GetZipInternalLeafFileName(normKey);
                    if (string.IsNullOrEmpty(leaf)) continue;
                    string baseNo = Path.GetFileNameWithoutExtension(leaf);
                    if (string.IsNullOrEmpty(baseNo)) continue;
                    string ext = Path.GetExtension(leaf).ToLowerInvariant();
                    if (string.IsNullOrEmpty(ext)) continue;
                    bool isImg = ext == ".jpg";
                    string key = dir + "|" + baseNo;
                    List<VarInternalMember> list;
                    if (!groups.TryGetValue(key, out list) || list == null)
                    {
                        list = new List<VarInternalMember>(2);
                        groups[key] = list;
                    }
                    VarInternalMember vm;
                    vm.FullPathNorm = normRaw;
                    vm.ExtLower = ext;
                    vm.IsImage = isImg;
                    list.Add(vm);
                }
                catch { }
            }

            string PickFirstSisterJpg(bool savesSceneDirOnly)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (string.IsNullOrEmpty(n)) continue;
                    string normRaw = NormalizeVarInternalEntryPath(n);
                    if (normRaw.Length == 0 || !IsImagePath(normRaw)) continue;
                    string normKey = NormalizeVarInternalPathForThumbKeys(n);
                    try
                    {
                        string dir = GetInternalPathParentDirectory(normKey);

                        if (savesSceneDirOnly && !IsUnderSavesSceneTree(dir))
                            continue;

                        string leaf = GetZipInternalLeafFileName(normKey);
                        if (string.IsNullOrEmpty(leaf)) continue;
                        string baseNo = Path.GetFileNameWithoutExtension(leaf);
                        if (string.IsNullOrEmpty(baseNo)) continue;
                        if (string.IsNullOrEmpty(Path.GetExtension(leaf))) continue;
                        string key = dir + "|" + baseNo;
                        if (!groups.TryGetValue(key, out List<VarInternalMember> list) || list == null || list.Count < 2)
                            continue;

                        bool hasNonImage = false;
                        for (int j = 0; j < list.Count; j++)
                        {
                            VarInternalMember m = list[j];
                            if (!m.IsImage && IsNonImageSiblingExt(m.ExtLower))
                            {
                                hasNonImage = true;
                                break;
                            }
                        }
                        if (!hasNonImage) continue;

                        return normRaw;
                    }
                    catch { }
                }
                return null;
            }

            if (prioritizeSavesSceneForEverything)
            {
                string sceneFirst = PickFirstSisterJpg(savesSceneDirOnly: true);
                if (!string.IsNullOrEmpty(sceneFirst))
                    return sceneFirst;
            }

            string anySister = PickFirstSisterJpg(savesSceneDirOnly: false);
            if (!string.IsNullOrEmpty(anySister))
                return anySister;

            if (prioritizeSavesSceneForEverything)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (string.IsNullOrEmpty(n)) continue;
                    string normRaw = NormalizeVarInternalEntryPath(n);
                    if (normRaw.Length > 0 && IsImagePath(normRaw) && IsUnderSavesSceneTree(normRaw))
                        return normRaw;
                }
            }

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string normRaw = NormalizeVarInternalEntryPath(n);
                if (normRaw.Length > 0 && IsImagePath(normRaw))
                    return normRaw;
            }

            return null;
        }

        private string GetOrChoosePackagePreviewInternalPath(VarPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                string uid = pkg.Uid;
                bool prioritizeSavesScene = false;
                try { prioritizeSavesScene = Gallery.IsEverythingCategoryName((CurrentCategoryTitle ?? "").Trim()); } catch { prioritizeSavesScene = false; }

                string cacheKey = null;
                if (!string.IsNullOrEmpty(uid))
                    cacheKey = uid + "\x1F" + (prioritizeSavesScene ? "EV" : "DEF");

                if (!string.IsNullOrEmpty(cacheKey) && _packagePreviewInternalPathCache.TryGetValue(cacheKey, out string cached))
                    return cached;

                List<string> names; List<long> ticks; List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return null;

                string chosen = PickPackagePreviewInternalPathFromFileList(names, prioritizeSavesScene);

                if (!string.IsNullOrEmpty(cacheKey))
                {
                    if (_packagePreviewInternalPathCache.Count > 8000) _packagePreviewInternalPathCache.Clear();
                    _packagePreviewInternalPathCache[cacheKey] = chosen;
                }

                return chosen;
            }
            catch
            {
                return null;
            }
        }

        private HashSet<string> GetOrBuildPackageInternalJpgSet(VarPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                string uid = pkg.Uid;
                if (!string.IsNullOrEmpty(uid) && _packageInternalJpgSetCache.TryGetValue(uid, out HashSet<string> cached) && cached != null)
                    return cached;

                List<string> names; List<long> ticks; List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) return null;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++)
                {
                    string n = names[i];
                    if (!IsImagePath(n)) continue;
                    try
                    {
                        string normRaw = NormalizeVarInternalEntryPath(n);
                        if (normRaw.Length == 0) continue;
                        set.Add(normRaw);
                        string canon = NormalizeVarInternalPathForThumbKeys(n);
                        if (!string.Equals(canon, normRaw, StringComparison.Ordinal))
                            set.Add(canon);
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(uid))
                {
                    if (_packageInternalJpgSetCache.Count > 4000) _packageInternalJpgSetCache.Clear();
                    _packageInternalJpgSetCache[uid] = set;
                }
                return set;
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

        /// <summary>After save/overwrite: bust VPB/VaM thumb caches and force visible grid cells to reload.</summary>
        internal void OnGalleryAssetSavedInvalidateThumbnails(string savedAssetPath)
        {
            refreshOnNextShow = true;

            try
            {
                if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                    CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
                currentLoadingGroupId = Guid.NewGuid().ToString();
            }
            catch { }

            try { InvalidateLooseThumbCachesForAsset(savedAssetPath); } catch { }

            try { RefreshVisibleGridVisualsOnly(); } catch { }
        }

        private void InvalidateLooseThumbCachesForAsset(string savedAssetPath)
        {
            if (string.IsNullOrEmpty(savedAssetPath))
            {
                _looseSisterJpgPathCache.Clear();
                _looseThumbWriteTimeCache.Clear();
                return;
            }

            string norm = savedAssetPath.Replace('\\', '/');
            try { _looseSisterJpgPathCache.Remove(norm); } catch { }
            try { _looseSisterJpgPathCache.Remove(savedAssetPath); } catch { }

            string jpg = null;
            try { jpg = Path.ChangeExtension(norm, ".jpg"); } catch { jpg = null; }
            if (!string.IsNullOrEmpty(jpg))
            {
                try { _looseThumbWriteTimeCache.Remove(jpg); } catch { }
                // Also drop sister resolve for the JSON row this jpg belongs to.
                string json = null;
                try { json = Path.ChangeExtension(norm, ".json"); } catch { json = null; }
                if (!string.IsNullOrEmpty(json))
                {
                    try { _looseSisterJpgPathCache.Remove(json); } catch { }
                }
            }
        }

        /// <summary>
        /// Sister <c>.jpg</c> next to a loose gallery file (local scene JSON). Cached — scroll binds must not
        /// re-hit <see cref="FileManager.FileExists"/> every cell.
        /// </summary>
        private string ResolveLooseSisterJpgPathCached(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            string cached;
            if (_looseSisterJpgPathCache.TryGetValue(filePath, out cached))
                return string.IsNullOrEmpty(cached) ? null : cached;

            string result = null;
            try
            {
                string testJpg = Path.ChangeExtension(filePath, ".jpg");
                if (!string.IsNullOrEmpty(testJpg) && FileManager.FileExists(testJpg))
                    result = testJpg;
            }
            catch (ArgumentException)
            {
                // Invalid path characters — treat as no sister.
            }
            catch { }

            if (_looseSisterJpgPathCache.Count > 8000)
                _looseSisterJpgPathCache.Clear();
            _looseSisterJpgPathCache[filePath] = result ?? string.Empty;
            return result;
        }

        private long GetLooseThumbWriteTimeCached(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return 0;

            try
            {
                if (GalleryThumbnailCache.Instance != null && GalleryThumbnailCache.Instance.IsPackagePath(imgPath))
                    return 0;
            }
            catch { }

            long cached;
            if (_looseThumbWriteTimeCache.TryGetValue(imgPath, out cached))
                return cached;

            long wt = 0;
            try
            {
                string full = FileManager.GetFullPath(imgPath.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(full) && File.Exists(full))
                    wt = File.GetLastWriteTimeUtc(full).ToFileTimeUtc();
            }
            catch { }

            if (wt == 0)
            {
                try
                {
                    FileEntry fe = FileManager.GetFileEntry(imgPath);
                    if (fe != null) wt = fe.LastWriteTime.ToFileTime();
                }
                catch { }
            }

            if (_looseThumbWriteTimeCache.Count > 8000)
                _looseThumbWriteTimeCache.Clear();
            _looseThumbWriteTimeCache[imgPath] = wt;
            return wt;
        }

        private void EnqueueThumbnailCacheJob(string path, Texture2D tex, long lastWriteTime, string groupId, int turboJpegScaleDenom)
        {
            if (pendingThumbnailCacheJobs == null) pendingThumbnailCacheJobs = new Queue<ThumbnailCacheJob>();
            pendingThumbnailCacheJobs.Enqueue(new ThumbnailCacheJob { Path = path, Texture = tex, LastWriteTime = lastWriteTime, GroupId = groupId, TurboJpegScaleDenom = turboJpegScaleDenom });
            _thumbCacheTotalEnqueued++;
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
            // Package list rows use Path as indexed var_path hint; never treat that as a loose disk image
            // (bad/mis-typed DB → wrong branch → FileExists miss → decode retry storm when grid relayouts).
            if (!(file is PackageListEntry) && IsImagePath(file.Path))
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
                    // Local cleanup rows: sidecar .jpg next to source (.json -> .jpg).
                    imgPath = ResolveLooseSisterJpgPathCached(file.Path) ?? "";
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
                // Cached package index (jpgSet + package preview pick); FileExists sister only if package unresolved / still empty.
                string pkgPath = null;
                try
                {
                    if (!TryGetVarPackageRootPathFromGalleryPath(vfe.Path, out pkgPath))
                        pkgPath = null;
                }
                catch { pkgPath = null; }
                if (string.IsNullOrEmpty(pkgPath))
                {
                    ClearThumbnailTarget(target);
                    return;
                }

                string pkgNorm = NormalizeVarInternalEntryPath(pkgPath);

                VarPackage vPkg = null;
                string rowPkgUid = null;
                try
                {
                    string u = vfe.Uid ?? "";
                    int ix = u.IndexOf(":/", StringComparison.Ordinal);
                    rowPkgUid = ix > 0 ? u.Substring(0, ix) : u;
                }
                catch { rowPkgUid = null; }
                // Indexed resolve (SQLite UID + var_path): matches packagesByPath / filename fallback when bare GetPackage(uid) misses (Unicode paths).
                if (!string.IsNullOrEmpty(rowPkgUid) && !string.IsNullOrEmpty(pkgNorm))
                {
                    try
                    {
                        if (FileManager.TryResolveVarPackageForIndexedGalleryRow(rowPkgUid, pkgNorm, out VarPackage pIx))
                            vPkg = pIx;
                    }
                    catch { }
                }
                if (vPkg == null)
                {
                    try { vPkg = vfe.Package; } catch { vPkg = null; }
                }
                if (vPkg == null)
                {
                    try
                    {
                        string uid = CanonicalVarPackageUidFromPathOrHint(pkgPath);
                        if (!string.IsNullOrEmpty(uid))
                            vPkg = FileManager.GetPackage(uid, ensureInstalled: false);
                    }
                    catch { vPkg = null; }
                }

                // Per-row sister: same basename, .jpg only (then package-wide preview if missing).
                // Use encoding-repaired internal path for folder/file segments so sister path matches zip listing when SQLite has Latin1-mojibake (Ã´ vs ô).
                string ipKey = NormalizeVarInternalPathForThumbKeys(vfe.InternalPath ?? "");
                string leafInternal = GetZipInternalLeafFileName(ipKey);
                string internalNoExt = string.IsNullOrEmpty(leafInternal)
                    ? ""
                    : Path.GetFileNameWithoutExtension(leafInternal);
                string internalDir = GetInternalPathParentDirectory(ipKey);
                string baseInternal = string.IsNullOrEmpty(internalDir)
                    ? internalNoExt
                    : internalDir + "/" + internalNoExt;

                string internalSisterJpg = (baseInternal + ".jpg").Replace('\\', '/');
                if (internalSisterJpg.StartsWith("/")) internalSisterJpg = internalSisterJpg.Substring(1);
                string sisterKeyNorm = NormalizeVarInternalPathForThumbKeys(internalSisterJpg);

                if (vPkg != null)
                {
                    HashSet<string> jpgSet = GetOrBuildPackageInternalJpgSet(vPkg);
                    string matchedJpg = null;
                    if (jpgSet != null && sisterKeyNorm.Length > 0)
                        matchedJpg = FindMatchingInternalJpgPathInSet(jpgSet, sisterKeyNorm);
                    if (!string.IsNullOrEmpty(matchedJpg))
                    {
                        imgPath = vPkg.Path + ":/" + matchedJpg;
                    }
                    else
                    {
                        string chosen = GetOrChoosePackagePreviewInternalPath(vPkg);
                        if (!string.IsNullOrEmpty(chosen))
                            imgPath = vPkg.Path + ":/" + chosen.Replace('\\', '/');
                    }
                }

                if (string.IsNullOrEmpty(imgPath))
                {
                    string sisterJpg = pkgNorm + ":/" + internalSisterJpg;
                    if (FileManager.FileExists(sisterJpg))
                        imgPath = sisterJpg;
                    else if (!string.Equals(sisterKeyNorm, internalSisterJpg, StringComparison.Ordinal))
                    {
                        string sisterAlt = pkgNorm + ":/" + sisterKeyNorm;
                        if (FileManager.FileExists(sisterAlt))
                            imgPath = sisterAlt;
                    }
                }
            }
            else
            {
                // Sister-file rule: same basename, .jpg only (cached — local scenes scroll-hot).
                imgPath = ResolveLooseSisterJpgPathCached(file.Path) ?? "";
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
            string expectedTag = capturedGroupId + "|" + imgPath + "|" + GetLooseThumbWriteTimeCached(imgPath);
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
                    try { SyncThumbPlaceholderForFile(target.transform, target, file); } catch { }
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
                    target.color = ThumbnailPlaceholderBackdrop;
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
                try { SyncThumbPlaceholderForFile(target.transform, target, file); } catch { }
                return;
            }

            QueueThumbnailDecode(file, target, imgPath, expectedTag, capturedGroupId, skipCache: false, scheduleHangWatchdog: true, turboJpegScaleDenom: thumbTd, thumbnailUnityDecodeOnly: thumbnailUnityDecodeOnly, bind: bind);
        }

        private void QueueThumbnailDecode(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, bool skipCache, bool scheduleHangWatchdog, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly, ThumbnailBindingTag bind = null)
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
            // Capture bind — post-process callback must stay RawImage-only (no GetComponent / listener rewiring).
            ThumbnailBindingTag bindForCallback = bind;
            FileEntry fileForCallback = file;
            qi.callback = (res) =>
            {
                if (res != null && res.tex != null && !res.cancel)
                {
                    ThumbnailBindingTag cbBind = bindForCallback;
                    if (cbBind != null && cbBind.ExpectedTag == expectedTag && target != null)
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
                        try { SyncThumbPlaceholderForFile(target.transform, target, fileForCallback); } catch { }
                    }

                    // Disk enqueue idle-gated in ProcessThumbnailCacheQueue.
                    // Use sister-jpg mtime (cached) — never JSON FileEntry.LastWriteTime (cache key mismatch vs decode).
                    if (!res.loadedFromGalleryCache && capturedGroupId == currentLoadingGroupId && res.tex != null)
                    {
                        long imgTime = GetLooseThumbWriteTimeCached(imgPath);
                        EnqueueThumbnailCacheJob(imgPath, res.tex, imgTime, capturedGroupId, res.turboJpegScaleDenom);
                    }
                    return;
                }

                if (res != null && res.cancel) return;
                ThumbnailBindingTag failBind = bindForCallback;
                if (failBind == null || failBind.ExpectedTag != expectedTag) return;
                if (capturedGroupId != currentLoadingGroupId) return;
                RequestThumbnailRetryAfterFailure(fileForCallback, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly, aggressiveSkipCache: true);
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
            if (scheduleHangWatchdog)
                StartCoroutine(ThumbnailHangWatchdogCo(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly));
        }

        private void RequestThumbnailRetryAfterFailure(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly, bool aggressiveSkipCache)
        {
            if (target == null) return;
            ThumbnailBindingTag b = target.GetComponent<ThumbnailBindingTag>();
            if (b == null || b.ExpectedTag != expectedTag) return;
            if (b.ThumbRetryCount >= MaxThumbnailDecodeRetries) return;
            b.ThumbRetryCount++;
            StartCoroutine(ThumbnailRetryAfterDelayCo(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly, aggressiveSkipCache));
        }

        private IEnumerator ThumbnailRetryAfterDelayCo(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly, bool aggressiveSkipCache)
        {
            float delay = aggressiveSkipCache ? 0.02f : 0.10f;
            yield return new WaitForSecondsRealtime(delay);
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
            if (aggressiveSkipCache)
            {
                CustomImageLoaderThreaded.singleton.ClearCacheThumbnail(imgPath, turboJpegScaleDenom, thumbnailUnityDecodeOnly);
                QueueThumbnailDecode(file, target, imgPath, expectedTag, capturedGroupId, skipCache: true, scheduleHangWatchdog: false, turboJpegScaleDenom: turboJpegScaleDenom, thumbnailUnityDecodeOnly: thumbnailUnityDecodeOnly, bind: b);
            }
            else
            {
                QueueThumbnailDecode(file, target, imgPath, expectedTag, capturedGroupId, skipCache: false, scheduleHangWatchdog: false, turboJpegScaleDenom: turboJpegScaleDenom, thumbnailUnityDecodeOnly: thumbnailUnityDecodeOnly, bind: b);
            }
        }

        private IEnumerator ThumbnailHangWatchdogCo(FileEntry file, RawImage target, string imgPath, string expectedTag, string capturedGroupId, int turboJpegScaleDenom, bool thumbnailUnityDecodeOnly)
        {
            float startRt = Time.realtimeSinceStartup;
            float wait = ThumbnailHangWatchDelaySec;
            while (true)
            {
                yield return new WaitForSecondsRealtime(wait);
                if (target == null) yield break;
                ThumbnailBindingTag b = target.GetComponent<ThumbnailBindingTag>();
                if (b == null || b.ExpectedTag != expectedTag) yield break;
                if (capturedGroupId != currentLoadingGroupId) yield break;
                if (target.texture != null) yield break;
                if (b.ThumbRetryCount > 0) yield break;

                float now = Time.realtimeSinceStartup;
                float sinceScroll = now - RecyclingGridView.LastScrollRealtime;
                float sinceDrag = now - ScrollbarSync.LastScrollbarDragRealtime;
                bool scrolling = sinceScroll < ThumbnailHangWatchScrollQuietSec || sinceDrag < ThumbnailHangWatchScrollQuietSec;

                int pendTh = 0;
                try { if (CustomImageLoaderThreaded.singleton != null) pendTh = CustomImageLoaderThreaded.singleton.PendingThumbnailCount; } catch { pendTh = 0; }
                bool queuePressure = pendTh >= AllVarThumbQueuePressureThreshold;

                if (scrolling || queuePressure)
                {
                    if ((now - startRt) < ThumbnailHangWatchMaxDelaySec)
                    {
                        // Still scrolling / backlog high: do not amplify with skip-cache retries.
                        wait = 0.25f;
                        continue;
                    }
                }

                // Timeout after quiet + low-pressure window: re-queue once, but do not clear cache / skip-cache.
                RequestThumbnailRetryAfterFailure(file, target, imgPath, expectedTag, capturedGroupId, turboJpegScaleDenom, thumbnailUnityDecodeOnly, aggressiveSkipCache: false);
                yield break;
            }
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
                target.color = ThumbnailPlaceholderBackdrop;
            }
            catch { }
        }

        private const float ThumbCropRatioMin = 0.75f;
        private const float ThumbCropRatioMax = 1.33f;

        private void UpdateAspectRatio(RawImage target, Texture tex)
        {
            if (target == null || tex == null) return;
            float ratio = (float)tex.width / Mathf.Max(1, tex.height);
            AspectRatioFitter arf = target.GetComponent<AspectRatioFitter>();

            if (arf != null)
            {
                // List rows: cell resizes to natural image ratio via ARF.
                target.uvRect = new Rect(0f, 0f, 1f, 1f);
                arf.aspectRatio = ratio;
                return;
            }

            // Grid cells: always center-crop to square via uvRect — no stretching for any ratio.
            float uSize = ratio >= 1f ? 1f / ratio : 1f;
            float vSize = ratio >= 1f ? 1f : ratio;
            target.uvRect = new Rect((1f - uSize) * 0.5f, (1f - vSize) * 0.5f, uSize, vSize);
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
