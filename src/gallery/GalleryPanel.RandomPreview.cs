using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>
    /// Panel side of the quick-menu / wrist-watch random hover preview: sample a few candidates from the
    /// same pool a random button would draw from, hand them out for the preview thumbnail, then launch the
    /// exact entry the user saw. Sampling is by rejection so no pool copy happens on the hover path.
    /// </summary>
    public partial class GalleryPanel
    {
        /// <summary>True when this panel is already listing <paramref name="categoryName"/> (null/empty = current view).</summary>
        internal bool QuickMenu_IsShowingRandomCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return true;
            string cur = currentCategoryTitle ?? "";
            if (string.Equals(cur, categoryName, StringComparison.OrdinalIgnoreCase)) return true;
            // Same alias the launch path uses: quick-menu "Skin" is category "Person Skin" in some builds.
            if (string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur, "Person Skin", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Fill <paramref name="dest"/> with up to <paramref name="max"/> distinct random candidates from the
        /// live filtered view. Rejection sampling — never copies or sorts the pool. First pass also rejects
        /// anything inside the scope's recency window; the relaxed pass only runs if that starves the sample.
        /// </summary>
        internal int QuickMenu_FillRandomSampleFromCurrentView(List<FileEntry> dest, int max)
        {
            if (dest == null) return 0;
            dest.Clear();
            if (max <= 0) return 0;

            try
            {
                var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                    ? currentFilteredFiles
                    : lastFilteredFiles;
                if (pool == null || pool.Count == 0) return 0;

                bool appearanceCat, subSceneCat, sceneCat;
                bool gated = ComputeRandomPoolCategoryGates(out appearanceCat, out subSceneCat, out sceneCat);

                string scope = GetRandomHistoryScope();
                int window = VpbRandomHistory.ComputeWindow(pool.Count);

                int want = Mathf.Min(max, pool.Count);
                int attempts = Mathf.Min(pool.Count * 3 + 8, 512);

                for (int pass = 0; pass < 2 && dest.Count < want; pass++)
                {
                    int passWindow = (pass == 0) ? window : 0;
                    for (int a = 0; a < attempts && dest.Count < want; a++)
                    {
                        FileEntry cand = pool[VpbRandom.Next(pool.Count)];
                        if (cand == null) continue;
                        if (gated && !IsRandomPoolEntryAllowedForGates(cand, appearanceCat, subSceneCat, sceneCat)) continue;
                        if (passWindow > 0 && VpbRandomHistory.IsRecent(scope, cand, passWindow)) continue;

                        bool dup = false;
                        for (int i = 0; i < dest.Count; i++)
                        {
                            if (ReferenceEquals(dest[i], cand)) { dup = true; break; }
                            string a1 = dest[i].Path ?? dest[i].Uid;
                            string a2 = cand.Path ?? cand.Uid;
                            if (!string.IsNullOrEmpty(a1) && string.Equals(a1, a2, StringComparison.OrdinalIgnoreCase))
                            { dup = true; break; }
                        }
                        if (dup) continue;

                        dest.Add(cand);
                    }
                }
            }
            catch { }
            return dest.Count;
        }

        /// <summary>
        /// Navigate to <paramref name="categoryName"/>, wait for the refresh, take a sample, restore the
        /// previous view. Cold path only — the caller caches the sample so repeat hovers cost nothing.
        /// </summary>
        internal Coroutine QuickMenu_PrepareRandomSampleForCategory(string categoryName, List<FileEntry> dest, int max, Action<int> onDone)
        {
            try { return StartCoroutine(QuickMenu_PrepareRandomSampleRoutine(categoryName, dest, max, onDone)); }
            catch
            {
                if (onDone != null) { try { onDone(0); } catch { } }
                return null;
            }
        }

        private IEnumerator QuickMenu_PrepareRandomSampleRoutine(string categoryName, List<FileEntry> dest, int max, Action<int> onDone)
        {
            int filled = 0;

            if (QuickMenu_IsShowingRandomCategory(categoryName))
            {
                filled = QuickMenu_FillRandomSampleFromCurrentView(dest, max);
                if (onDone != null) { try { onDone(filled); } catch { } }
                yield break;
            }

            string prevTitle = null;
            string prevExt = null;
            string prevPath = null;
            try { prevTitle = currentCategoryTitle; } catch { prevTitle = null; }
            try { prevExt = currentExtension; } catch { prevExt = null; }
            try { prevPath = currentPath; } catch { prevPath = null; }

            string targetUid = null;
            try { targetUid = QuickMenu_GetSelectedTargetPersonUid(); } catch { targetUid = null; }

            Gallery.Category cat;
            if (!QuickMenu_TryResolveCategory(categoryName, out cat))
            {
                if (onDone != null) { try { onDone(0); } catch { } }
                yield break;
            }

            try { Show(cat.name, cat.extension, cat.path); } catch { }

            yield return null;
            int guard = 0;
            while (refreshCoroutine != null && guard < 600)
            {
                guard++;
                yield return null;
            }
            if (guard == 0) yield return null;

            filled = QuickMenu_FillRandomSampleFromCurrentView(dest, max);

            // Preview must never leave the panel parked on another category.
            if (!string.IsNullOrEmpty(prevTitle))
            {
                try { Show(prevTitle, prevExt, prevPath); } catch { }
                if (!string.IsNullOrEmpty(targetUid))
                {
                    try { QuickMenu_SetSelectedTargetPersonUid(targetUid); } catch { }
                }
            }

            if (onDone != null) { try { onDone(filled); } catch { } }
        }

        internal bool QuickMenu_TryResolveCategory(string categoryName, out Gallery.Category cat)
        {
            cat = default(Gallery.Category);
            if (string.IsNullOrEmpty(categoryName)) return false;
            try
            {
                if (categories == null) return false;
                bool skinAlias = string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase);
                for (int pass = 0; pass < (skinAlias ? 2 : 1); pass++)
                {
                    string name = (pass == 0) ? categoryName : "Person Skin";
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            cat = c;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Bind a preview thumbnail (same decode + cache path as a grid tile) to an arbitrary RawImage.</summary>
        internal void QuickMenu_LoadPreviewThumbnail(FileEntry file, RawImage target)
        {
            if (target == null) return;
            if (file == null)
            {
                ClearThumbnailTarget(target);
                return;
            }
            try { LoadThumbnail(file, target, gridThumbnailContext: false); }
            catch { ClearThumbnailTarget(target); }
        }

        internal void QuickMenu_ClearPreviewThumbnail(RawImage target)
        {
            ClearThumbnailTarget(target);
        }

        /// <summary>Short display name for the preview caption (file name, no extension, no package prefix).</summary>
        internal static string QuickMenu_GetPreviewLabel(FileEntry file)
        {
            if (file == null) return "";
            string s = null;
            try { s = file.Name; } catch { s = null; }
            if (string.IsNullOrEmpty(s))
            {
                try { s = file.Path ?? file.Uid; } catch { s = null; }
            }
            if (string.IsNullOrEmpty(s)) return "";

            s = s.Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            int dot = s.LastIndexOf('.');
            if (dot > 0) s = s.Substring(0, dot);
            return s;
        }

        /// <summary>
        /// Two-band preview caption in the grid's own shape: <paramref name="primary"/> is the leaf (or
        /// sole package) name, <paramref name="secondary"/> the muted creator plus package line.
        /// Warm path — runs once per hover draw, not per frame.
        /// </summary>
        internal void QuickMenu_GetPreviewLabelLines(FileEntry file, out string primary, out string secondary)
        {
            primary = "";
            secondary = "";
            if (file == null) return;

            string p;
            string pkg;
            string creator;
            try { GetGridItemLabelLines(file, out p, out pkg, out creator); }
            catch { p = null; pkg = null; creator = null; }

            if (string.IsNullOrEmpty(p)) p = QuickMenu_GetPreviewLabel(file);
            primary = p ?? "";

            if (!string.IsNullOrEmpty(creator)
                && string.Equals(creator, primary, StringComparison.OrdinalIgnoreCase))
                creator = null;
            if (!string.IsNullOrEmpty(pkg)
                && string.Equals(pkg, primary, StringComparison.OrdinalIgnoreCase))
                pkg = null;

            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(pkg))
                secondary = creator + "  ·  " + pkg;
            else if (!string.IsNullOrEmpty(creator))
                secondary = creator;
            else
                secondary = pkg ?? "";
        }

        /// <summary>Launch one preselected entry from <paramref name="categoryName"/> (hover preview click).</summary>
        internal void QuickMenu_LoadPickedFromCategory(string categoryName, FileEntry file, bool preserveUi, bool preserveTarget)
        {
            try
            {
                if (file == null)
                {
                    QuickMenu_LoadRandomFromCategory(categoryName, preserveUi, preserveTarget);
                    return;
                }
                if (string.IsNullOrEmpty(categoryName))
                {
                    if (!ApplyPickedRandomEntry(file)) LoadRandom();
                    return;
                }
                StartCoroutine(QuickMenu_LoadRandomFromCategoryRoutine(categoryName, preserveUi, preserveTarget, file));
            }
            catch { }
        }
    }
}
