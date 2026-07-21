using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private float EffectiveGridSpacingX()
        {
            float v = 10f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryGridSpacingX; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 10f;
            return Mathf.Clamp(v, 0f, 80f);
        }

        private float EffectiveGridSpacingY()
        {
            float v = 10f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryGridSpacingY; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 10f;
            return Mathf.Clamp(v, 0f, 80f);
        }

        private float EffectiveGridThumbnailPadding()
        {
            float v = 3f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryGridThumbnailPadding; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 3f;
            return Mathf.Clamp(v, 0f, 40f);
        }

        private float EffectiveGridHoverBorderWidth()
        {
            float v = 2f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryGridHoverBorderWidth; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 2f;
            return Mathf.Clamp(v, 0f, 20f);
        }

        private float EffectiveGridSelectedBorderWidth()
        {
            float v = 4f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryGridSelectedBorderWidth; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 4f;
            return Mathf.Clamp(v, 0f, 30f);
        }

        private bool EffectiveGridBorderInward()
        {
            try
            {
                if (VPBConfig.Instance == null) return false;
                if (!VPBConfig.Instance.GalleryGridBorderInwardWhenSquare) return false;
                return EffectiveGridThumbnailPadding() <= 0.01f;
            }
            catch { return false; }
        }

        /// <summary>List + grid share border width config; inward edge strip uses same toggle as grid flush cells (list rows treated as flush).</summary>
        private Color EffectiveGalleryGridBorderColor()
        {
            try
            {
                if (VPBConfig.Instance != null) return VPBConfig.Instance.GetGalleryGridBorderColor();
            }
            catch { }
            return new Color(1f, 1f, 0f, 1f);
        }

        private bool EffectiveGridBorderInwardForGalleryCell()
        {
            try
            {
                if (VPBConfig.Instance == null) return false;
                if (!VPBConfig.Instance.GalleryGridBorderInwardWhenSquare) return false;
                if (layoutMode == GalleryLayoutMode.List || settingsListViewActive) return true;
                return EffectiveGridThumbnailPadding() <= 0.01f;
            }
            catch { return false; }
        }

        private bool EffectiveGalleryScanWlBorderEnabled()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlBorderEnabled; } catch { }
            return true;
        }

        private bool EffectiveGalleryScanWlBorderShowInGrid()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlBorderShowInGrid; } catch { }
            return true;
        }

        private bool EffectiveGalleryScanWlBorderShowInList()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlBorderShowInList; } catch { }
            return true;
        }

        private float EffectiveGalleryScanWlBorderWidth()
        {
            float v = 4f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlBorderWidth; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 4f;
            return Mathf.Clamp(v, 0f, 20f);
        }

        private float EffectiveGalleryScanWlGridFrameInset()
        {
            float v = 0f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlGridFrameInset; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            return Mathf.Clamp(v, 0f, 24f);
        }

        private float EffectiveGalleryScanWlListFrameInset()
        {
            float v = 2f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlListFrameInset; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 2f;
            return Mathf.Clamp(v, 0f, 24f);
        }

        private bool EffectiveGalleryScanWlBorderOnThumbnail()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlBorderOnThumbnail; } catch { }
            return true;
        }

        private Color EffectiveGalleryScanWlBorderColor()
        {
            try
            {
                if (VPBConfig.Instance != null) return VPBConfig.Instance.GetGalleryScanWlBorderColor();
            }
            catch { }
            return new Color(0.2f, 0.95f, 1f, 1f);
        }

        private bool EffectiveGalleryScanWlTempBorderEnabled()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlTempBorderEnabled; } catch { }
            return true;
        }

        private bool EffectiveGalleryScanWlTempBorderShowInGrid()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlTempBorderShowInGrid; } catch { }
            return true;
        }

        private bool EffectiveGalleryScanWlTempBorderShowInList()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlTempBorderShowInList; } catch { }
            return true;
        }

        private float EffectiveGalleryScanWlTempBorderWidth()
        {
            float v = 4f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlTempBorderWidth; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 4f;
            return Mathf.Clamp(v, 0f, 20f);
        }

        private float EffectiveGalleryScanWlTempGridFrameInset()
        {
            float v = 0f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlTempGridFrameInset; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            return Mathf.Clamp(v, 0f, 24f);
        }

        private float EffectiveGalleryScanWlTempListFrameInset()
        {
            float v = 2f;
            try { if (VPBConfig.Instance != null) v = VPBConfig.Instance.GalleryScanWlTempListFrameInset; } catch { }
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 2f;
            return Mathf.Clamp(v, 0f, 24f);
        }

        private bool EffectiveGalleryScanWlTempBorderOnThumbnail()
        {
            try { if (VPBConfig.Instance != null) return VPBConfig.Instance.GalleryScanWlTempBorderOnThumbnail; } catch { }
            return true;
        }

        private Color EffectiveGalleryScanWlTempBorderColor()
        {
            try
            {
                if (VPBConfig.Instance != null) return VPBConfig.Instance.GetGalleryScanWlTempBorderColor();
            }
            catch { }
            return new Color(1f, 0.15f, 1f, 1f);
        }

        /// <summary>
        /// Splits a search query into lowercase terms (whitespace separated), removing empties.
        /// </summary>
        internal static string[] SplitSearchTerms(string query)
        {
            // .NET 3.5 compatibility: no string.IsNullOrWhiteSpace / Array.Empty<T>()
            if (query == null) return new string[0];
            query = query.Trim();
            if (query.Length == 0) return new string[0];

            // Avoid allocations for common small queries.
            string[] raw = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (raw.Length == 0) return new string[0];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = raw[i].ToLowerInvariant();
            return raw;
        }

        /// <summary>True if every term appears in either <paramref name="a"/> or <paramref name="b"/> (case-insensitive).</summary>
        internal static bool MatchesAllTermsInEither(string a, string b, string[] termsLower)
        {
            if (termsLower == null || termsLower.Length == 0) return true;
            if (a == null) a = "";
            if (b == null) b = "";
            for (int i = 0; i < termsLower.Length; i++)
            {
                string t = termsLower[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (b.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return false;
            }
            return true;
        }

        /// <summary>NameStartsWith scope: every term must be a case-insensitive prefix of <paramref name="name"/>. Different shape from substring matcher above; can't be expressed via IndexOf alone.</summary>
        internal static bool MatchesAllTermsStartsWith(string name, string[] termsLower)
        {
            if (termsLower == null || termsLower.Length == 0) return true;
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < termsLower.Length; i++)
            {
                string t = termsLower[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (!name.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// Single entry point for FileEntry-based search matching. Reads <see cref="VPBConfig.GallerySearchScope"/> and picks a strategy.
        /// Name field is always the pretty form so search couples to what the user sees on the tile.
        /// Path field engaged only in PathAndName mode.
        /// </summary>
        internal static bool MatchesFileEntryByScope(FileEntry file, string[] termsLower)
        {
            if (termsLower == null || termsLower.Length == 0) return true;
            string scope = VPBConfig.Instance != null
                ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope)
                : "PathAndName";
            string name = GetPrettyEntryDisplayName(file);
            switch (scope)
            {
                case "NameOnly":
                    return MatchesAllTermsInEither(name, "", termsLower);
                case "NameStartsWith":
                    return MatchesAllTermsStartsWith(name, termsLower);
                default:
                    string path = null;
                    try { path = file != null ? file.Path : null; } catch { path = null; }
                    return MatchesAllTermsInEither(path, name, termsLower);
            }
        }

        private bool HasActiveNameFilter()
        {
            return nameFilterQuery != null && !nameFilterQuery.IsEmpty;
        }

        /// <summary>
        /// Fallback package-scan match when category SQL index unavailable.
        /// Structured-only queries (tag:/creator:/status) cannot be evaluated here → reject.
        /// </summary>
        private static bool MatchesPackageFallbackSearch(GallerySearchQuery query, string packageUid, string packagePath, string internalPath)
        {
            if (query == null || query.IsEmpty) return true;
            if (query.Branches == null || query.Branches.Count == 0) return true;

            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;
                bool structured = (br.TagInclude != null && br.TagInclude.Count > 0)
                    || (br.TagExclude != null && br.TagExclude.Count > 0)
                    || (br.CreatorTerms != null && br.CreatorTerms.Count > 0)
                    || br.Status != GallerySearchQuery.StatusFlags.None;
                string[] broad = br.BroadTerms != null && br.BroadTerms.Count > 0
                    ? br.BroadTerms.ToArray()
                    : new string[0];
                bool hasBroad = broad.Length > 0;
                bool hasBroadExcl = br.BroadExclude != null && br.BroadExclude.Count > 0;
                if (structured && !hasBroad && !hasBroadExcl) continue; // this OR-branch cannot match on fallback
                if (!hasBroad && !hasBroadExcl) continue;
                if (hasBroad && !MatchesPackageByScope(packageUid, packagePath, internalPath, broad)) continue;
                if (hasBroadExcl)
                {
                    bool hitExcl = false;
                    for (int xi = 0; xi < br.BroadExclude.Count; xi++)
                    {
                        string xt = br.BroadExclude[xi];
                        if (string.IsNullOrEmpty(xt)) continue;
                        if (MatchesPackageByScope(packageUid, packagePath, internalPath, new string[] { xt }))
                        {
                            hitExcl = true;
                            break;
                        }
                    }
                    if (hitExcl) continue;
                }
                if (br.CreatorTerms != null && br.CreatorTerms.Count > 0)
                {
                    string creator = "";
                    if (!string.IsNullOrEmpty(packageUid))
                    {
                        int dot = packageUid.IndexOf('.');
                        if (dot > 0) creator = packageUid.Substring(0, dot);
                    }
                    bool creatorOk = true;
                    for (int i = 0; i < br.CreatorTerms.Count; i++)
                    {
                        string t = br.CreatorTerms[i];
                        if (string.IsNullOrEmpty(t)) continue;
                        if (creator.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            creatorOk = false;
                            break;
                        }
                    }
                    if (!creatorOk) continue;
                }
                if (br.TagInclude != null && br.TagInclude.Count > 0) continue;
                if (br.TagExclude != null && br.TagExclude.Count > 0) continue;
                return true;
            }
            return false;
        }

        /// <summary>Keep <see cref="nameFilter"/> / terms / parsed AST in sync.</summary>
        private void AssignNameFilterState(string raw)
        {
            string f = raw ?? "";
            nameFilter = f;
            nameFilterLower = f.Length == 0 ? "" : f.ToLowerInvariant();
            nameFilterQuery = GallerySearchQuery.Parse(f);
            nameFilterTerms = nameFilterQuery != null ? nameFilterQuery.BroadTermsArray() : new string[0];
            _searchTagKeysCache = null;
            _searchTagKeysCacheFor = null;
        }

        private void ClearNameFilterState()
        {
            nameFilter = "";
            nameFilterLower = "";
            nameFilterQuery = GallerySearchQuery.Empty;
            nameFilterTerms = new string[0];
            _searchTagKeysCache = null;
            _searchTagKeysCacheFor = null;
            ClearTitleSearchChipsState();
        }

        private Dictionary<string, HashSet<string>> GetSearchTagKeysCached()
        {
            string key = nameFilter ?? "";
            if (_searchTagKeysCache != null && string.Equals(_searchTagKeysCacheFor, key, StringComparison.Ordinal))
                return _searchTagKeysCache;
            _searchTagKeysCache = BuildTagKeyLookupForSearch(nameFilterQuery ?? GallerySearchQuery.Empty);
            _searchTagKeysCacheFor = key;
            return _searchTagKeysCache;
        }

        /// <summary>VAR / package-list rows come from category SQL (loaded/tagged already applied).</summary>
        private static bool IsGallerySqlIndexedSearchEntry(FileEntry entry)
        {
            if (entry == null) return false;
            if (entry is VarFileEntry) return true;
            if (entry is PackageListEntry) return true;
            return false;
        }

        private static string TryGetFileEntryCreatorHint(FileEntry file)
        {
            // Never resolve Package here — deferred gallery rows would stall the search keystroke path.
            if (file == null) return "";
            try
            {
                string uid = null;
                VarFileEntry vfe = file as VarFileEntry;
                if (vfe != null)
                {
                    try { uid = vfe.GetRowPackageUid(); } catch { uid = null; }
                }
                if (string.IsNullOrEmpty(uid))
                {
                    PackageListEntry ple = file as PackageListEntry;
                    if (ple != null)
                    {
                        try { uid = ple.GetPackageUidForGalleryUserTags(); } catch { uid = null; }
                    }
                }
                if (string.IsNullOrEmpty(uid))
                {
                    try { uid = file.Uid; } catch { uid = null; }
                }
                if (string.IsNullOrEmpty(uid)) return "";
                int cut = uid.IndexOf(":/", StringComparison.Ordinal);
                if (cut > 0) uid = uid.Substring(0, cut);
                int dot = uid.IndexOf('.');
                if (dot > 0) return uid.Substring(0, dot);
            }
            catch { }
            return "";
        }

        /// <summary>
        /// In-memory match for parsed title-bar search. OR of AND-branches.
        /// <paramref name="tagKeysByTerm"/> maps each tag substring → row keys (<c>pkg\0path</c>).
        /// When <paramref name="skipSqlOwnedPredicates"/> is true (post-SQL VAR rows), loaded/tagged
        /// already applied in SQL are not re-checked here.
        /// </summary>
        private bool MatchesFileEntryBySearchQuery(
            FileEntry file,
            GallerySearchQuery query,
            Dictionary<string, HashSet<string>> tagKeysByTerm,
            bool skipSqlOwnedPredicates = false)
        {
            if (query == null || query.IsEmpty) return true;
            if (file == null) return false;
            if (query.Branches == null || query.Branches.Count == 0) return true;

            string rowKey = null;
            try
            {
                string pkg, ip;
                if (TryGetGalleryRowKeysForUserTags(file, out pkg, out ip))
                    rowKey = VpbLocalDatabase.MakeGalleryRowKey(pkg, ip);
            }
            catch { rowKey = null; }

            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;
                if (MatchesFileEntrySearchBranch(file, br, tagKeysByTerm, rowKey, skipSqlOwnedPredicates))
                    return true;
            }
            return false;
        }

        private bool MatchesFileEntrySearchBranch(
            FileEntry file,
            GallerySearchBranch br,
            Dictionary<string, HashSet<string>> tagKeysByTerm,
            string rowKey,
            bool skipSqlOwnedPredicates)
        {
            if (br == null) return true;

            bool needUserTagProbe = !skipSqlOwnedPredicates
                && (br.HasFlag(GallerySearchQuery.StatusFlags.Tagged)
                    || br.HasFlag(GallerySearchQuery.StatusFlags.Untagged));
            bool hasUserTag = false;
            if (needUserTagProbe)
                hasUserTag = IsGalleryUserTagBadgeVisible(file);

            if (br.TagInclude != null)
            {
                for (int i = 0; i < br.TagInclude.Count; i++)
                {
                    string t = br.TagInclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    HashSet<string> keys = null;
                    if (tagKeysByTerm == null || !tagKeysByTerm.TryGetValue(t, out keys) || keys == null
                        || string.IsNullOrEmpty(rowKey) || !keys.Contains(rowKey))
                        return false;
                }
            }
            if (br.TagExclude != null)
            {
                for (int i = 0; i < br.TagExclude.Count; i++)
                {
                    string t = br.TagExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    HashSet<string> keys = null;
                    if (tagKeysByTerm != null && tagKeysByTerm.TryGetValue(t, out keys) && keys != null
                        && !string.IsNullOrEmpty(rowKey) && keys.Contains(rowKey))
                        return false;
                }
            }

            if (br.CreatorTerms != null && br.CreatorTerms.Count > 0)
            {
                string creator = TryGetFileEntryCreatorHint(file) ?? "";
                for (int i = 0; i < br.CreatorTerms.Count; i++)
                {
                    string t = br.CreatorTerms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    if (creator.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
            }

            if (!skipSqlOwnedPredicates)
            {
                if (br.HasFlag(GallerySearchQuery.StatusFlags.Loaded))
                {
                    if (!IsFileEntryLoadedForSearch(file)) return false;
                }
                else if (br.HasFlag(GallerySearchQuery.StatusFlags.Unloaded))
                {
                    if (IsFileEntryLoadedForSearch(file)) return false;
                }

                if (br.HasFlag(GallerySearchQuery.StatusFlags.Tagged) && !hasUserTag)
                    return false;
                if (br.HasFlag(GallerySearchQuery.StatusFlags.Untagged) && hasUserTag)
                    return false;
            }

            if (br.HasFlag(GallerySearchQuery.StatusFlags.Starred))
            {
                int r = 0;
                try { r = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(file) : 0; }
                catch { r = 0; }
                if (r <= 0) return false;
            }
            if (br.HasFlag(GallerySearchQuery.StatusFlags.AutoInstall))
            {
                bool ai = false;
                try { ai = file.IsAutoInstall(); } catch { ai = false; }
                if (!ai) return false;
            }
            if (br.HasFlag(GallerySearchQuery.StatusFlags.Hidden))
            {
                bool hid = false;
                try { hid = PackageHidePrefs.IsGalleryHideBadgeVisible(file); } catch { hid = false; }
                if (!hid) return false;
            }
            if (br.HasFlag(GallerySearchQuery.StatusFlags.ScanExcluded))
            {
                bool w = false;
                try { w = ScanWhitelistManager.IsScanExcludedBadgeVisible(file); } catch { w = false; }
                if (!w) return false;
            }

            string creatorHint = TryGetFileEntryCreatorHint(file) ?? "";
            string uidHint = "";
            try { uidHint = file.Uid ?? ""; } catch { uidHint = ""; }

            if (br.BroadExclude != null && br.BroadExclude.Count > 0)
            {
                for (int i = 0; i < br.BroadExclude.Count; i++)
                {
                    string t = br.BroadExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    if (FileEntryMatchesBroadTerm(file, t, creatorHint, uidHint, tagKeysByTerm, rowKey))
                        return false;
                }
            }

            if (br.BroadTerms == null || br.BroadTerms.Count == 0)
                return true;

            for (int i = 0; i < br.BroadTerms.Count; i++)
            {
                string t = br.BroadTerms[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (FileEntryMatchesBroadTerm(file, t, creatorHint, uidHint, tagKeysByTerm, rowKey))
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>Same surface as include broad: name/path (scope), creator, uid, or user-tag substring.</summary>
        private bool FileEntryMatchesBroadTerm(
            FileEntry file,
            string t,
            string creatorHint,
            string uidHint,
            Dictionary<string, HashSet<string>> tagKeysByTerm,
            string rowKey)
        {
            if (file == null || string.IsNullOrEmpty(t)) return false;

            string scope = VPBConfig.Instance != null
                ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope)
                : "PathAndName";
            string pretty = GetPrettyEntryDisplayName(file);
            bool namePathOk;
            if (scope == "NameStartsWith")
                namePathOk = !string.IsNullOrEmpty(pretty) && pretty.StartsWith(t, StringComparison.OrdinalIgnoreCase);
            else if (scope == "NameOnly")
                namePathOk = !string.IsNullOrEmpty(pretty) && pretty.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
            else
            {
                string path = null;
                try { path = file.Path; } catch { path = null; }
                namePathOk = (!string.IsNullOrEmpty(pretty) && pretty.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(path) && path.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (namePathOk) return true;
            if (!string.IsNullOrEmpty(creatorHint) && creatorHint.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(uidHint) && uidHint.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            HashSet<string> keys = null;
            if (tagKeysByTerm != null && tagKeysByTerm.TryGetValue(t, out keys) && keys != null
                && !string.IsNullOrEmpty(rowKey) && keys.Contains(rowKey))
                return true;

            return false;
        }

        private static bool IsFileEntryLoadedForSearch(FileEntry file)
        {
            if (file == null) return false;
            try
            {
                SystemFileEntry sfe = file as SystemFileEntry;
                if (sfe != null && !sfe.isVar) return true; // loose files always "loaded"
            }
            catch { }
            try
            {
                string path = null;
                VarFileEntry vfe = file as VarFileEntry;
                if (vfe != null)
                {
                    try
                    {
                        if (vfe.Package != null) path = vfe.Package.Path;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(path))
                    {
                        // Indexed rows: treat as loaded unless we know otherwise via Package resolve.
                        return true;
                    }
                }
                PackageListEntry ple = file as PackageListEntry;
                if (ple != null)
                {
                    try { path = ple.Path; } catch { path = null; }
                }
                if (string.IsNullOrEmpty(path)) return true;
                return VpbLocalDatabase.ComputePackageLoadedFlagFromVarPath(path) != 0;
            }
            catch { return true; }
        }

        /// <summary>One SQLite connection for all tag substrings; reused across base-list filter.</summary>
        private Dictionary<string, HashSet<string>> BuildTagKeyLookupForSearch(GallerySearchQuery query)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (query == null || query.IsEmpty) return map;
            if (!VpbSqlite3.IsAvailable) return map;

            // Explicit tag: always. Broad terms only when length≥2 (avoid LIKE '%x%' on every letter).
            var need = new List<string>();
            bool hasExplicitTag = (query.TagInclude != null && query.TagInclude.Count > 0)
                || (query.TagExclude != null && query.TagExclude.Count > 0);
            if (query.TagInclude != null)
                for (int i = 0; i < query.TagInclude.Count; i++)
                    if (!string.IsNullOrEmpty(query.TagInclude[i])) need.Add(query.TagInclude[i]);
            if (query.TagExclude != null)
                for (int i = 0; i < query.TagExclude.Count; i++)
                    if (!string.IsNullOrEmpty(query.TagExclude[i])) need.Add(query.TagExclude[i]);
            if (query.BroadTerms != null)
            {
                for (int i = 0; i < query.BroadTerms.Count; i++)
                {
                    string t = query.BroadTerms[i];
                    if (string.IsNullOrEmpty(t) || t.Length < 2) continue;
                    need.Add(t);
                }
            }
            if (query.BroadExclude != null)
            {
                for (int i = 0; i < query.BroadExclude.Count; i++)
                {
                    string t = query.BroadExclude[i];
                    if (string.IsNullOrEmpty(t) || t.Length < 2) continue;
                    need.Add(t);
                }
            }
            if (need.Count == 0) return map;

            // No user tags in DB → skip connection (cache empty probe).
            if (hasExplicitTag)
                _searchUserTagVocabEmpty = false;
            else
            {
                if (!_searchUserTagVocabEmptyKnown)
                {
                    _searchUserTagVocabEmptyKnown = true;
                    _searchUserTagVocabEmpty = true;
                    try
                    {
                        var probe = new List<string>();
                        if (VpbLocalDatabase.TryReadAllGalleryUserTagNames(probe) && probe.Count > 0)
                            _searchUserTagVocabEmpty = false;
                    }
                    catch { _searchUserTagVocabEmpty = true; }
                }
                if (_searchUserTagVocabEmpty) return map;
            }

            string cat = currentCategoryTitle ?? "";
            if (string.IsNullOrEmpty(cat) && titleText != null) cat = titleText.text ?? "";
            try { VpbLocalDatabase.TryCollectRowKeysWithUserTagSubstringsPerTerm(cat, need, map); }
            catch { map.Clear(); }
            return map;
        }

        /// <summary>
        /// Package-level overload for sites that pass raw strings (no FileEntry available). Package uid is the name field; internalPath optionally folded into the path field for PathAndName mode.
        /// </summary>
        internal static bool MatchesPackageByScope(string packageUid, string packagePath, string internalPath, string[] termsLower)
        {
            if (termsLower == null || termsLower.Length == 0) return true;
            string scope = VPBConfig.Instance != null
                ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope)
                : "PathAndName";
            string name = packageUid ?? "";
            switch (scope)
            {
                case "NameOnly":
                    return MatchesAllTermsInEither(name, "", termsLower);
                case "NameStartsWith":
                    return MatchesAllTermsStartsWith(name, termsLower);
                default:
                    string pathField = packagePath ?? "";
                    if (!string.IsNullOrEmpty(internalPath))
                        pathField = pathField.Length == 0 ? internalPath : (pathField + " " + internalPath);
                    return MatchesAllTermsInEither(pathField, name, termsLower);
            }
        }

        /// <summary>VAR zip paths often use '\'; category roots use '/'.</summary>
        private static string GalleryNormalizePathSlashes(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.Replace('\\', '/');
        }

        /// <summary>True if internal path is under prefix (after slash normalization).</summary>
        internal static bool GalleryInternalPathStartsWithPrefix(string internalPath, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            return GalleryNormalizePathSlashes(internalPath).StartsWith(GalleryNormalizePathSlashes(prefix), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Path rules for VAR file worker / SQLite index path (matches <see cref="GalleryPanel.IO"/> RefreshFilesRoutine).</summary>
        internal static bool RefreshWorkerPathMatches(string checkPath, List<string> currentPaths, string currentPath)
        {
            bool pathOk = true;
            if (currentPaths != null && currentPaths.Count > 0)
            {
                pathOk = false;
                for (int p = 0; p < currentPaths.Count; p++)
                {
                    string pref = currentPaths[p];
                    if (GalleryInternalPathStartsWithPrefix(checkPath, pref))
                    {
                        string prefN = GalleryNormalizePathSlashes(pref).TrimEnd('/');
                        if (string.Equals(prefN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GalleryNormalizePathSlashes(checkPath).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        pathOk = true;
                        break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(currentPath))
            {
                pathOk = false;
                if (GalleryInternalPathStartsWithPrefix(checkPath, currentPath))
                {
                    string curN = GalleryNormalizePathSlashes(currentPath).TrimEnd('/');
                    if (string.Equals(curN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!GalleryNormalizePathSlashes(checkPath).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                            pathOk = true;
                    }
                    else
                    {
                        pathOk = true;
                    }
                }
            }
            return pathOk;
        }

        public string CurrentCategoryTitle => currentCategoryTitle;
        public GalleryLayoutMode LayoutMode => layoutMode;

        public static float BenchmarkStartTime = 0f;

        public void SetLayoutMode(GalleryLayoutMode mode, bool persistConfig = true, bool keepInternalSettingsMode = false)
        {
            // Any explicit middle-layout switch must leave internal settings mode.
            if (!keepInternalSettingsMode && (IsSettingsPanelOpen() || settingsListViewActive))
                ExitInternalSettingsMode(true);
            if (layoutMode == mode) return;
            
            if (mode == GalleryLayoutMode.List)
            {
                 BenchmarkStartTime = Time.realtimeSinceStartup;
                 VPBLogger.OneShot("Benchmark").LogInfo("Starting Switch to List Mode at " + BenchmarkStartTime);
            }

            layoutMode = mode;

            // Persist across restarts unless this is a temporary mode switch.
            if (persistConfig)
            {
                try
                {
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.GalleryLayoutMode = (int)layoutMode;
                        VPBConfig.Instance.Save(true, true);
                    }
                }
                catch { }
            }
            
            // ALWAYS use internal UI now
            if (scrollRect != null) scrollRect.gameObject.SetActive(true);

            UpdateFooterLayoutState();
            if (!keepInternalSettingsMode)
                UpdateLayout();

            // Layout switch should not force a full RefreshFiles().
            // The grid items support both modes, so we just reconfigure and rebind visible rows.
            try
            {
                if (contentGO != null)
                {
                    var rgv = contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        try { rgv.preserveCenterItemIndex = rgv.GetCenterItemIndex(); } catch { }

                        bool deferGridRefresh = keepInternalSettingsMode;
                        if (layoutMode == GalleryLayoutMode.List)
                        {
                            rgv.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferGridRefresh);
                            rgv.SetAdaptiveConfig(true, 0f, 1, true, deferGridRefresh);
                        }
                        else
                        {
                            int cols = GridColumnCount;
                            rgv.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), cols, deferGridRefresh);
                            rgv.SetAdaptiveConfig(true, 200f, cols, false, deferGridRefresh);
                        }
                        if (!deferGridRefresh)
                            rgv.Refresh();
                    }
                }
            }
            catch { }

            try
            {
                RefreshTboxGridRateControlState();
                RefreshTboxFlexButtonLayout();
            }
            catch { }
        }

        /// <summary>
        /// Re-applies grid config height and rebinds all visible cells.
        /// Call after grid label settings or column count changes (label strip height depends on visibility).
        /// </summary>
        public void RebuildGridLayout()
        {
            try
            {
                if (layoutMode != GalleryLayoutMode.Grid) return;
                if (contentGO == null) return;
                var rgv = contentGO.GetComponent<RecyclingGridView>();
                if (rgv == null) return;
                int cols = GridColumnCount;
                rgv.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), cols);
                rgv.SetAdaptiveConfig(true, 200f, cols, false);
                rgv.Refresh();
            }
            catch { }
        }

        public Atom SelectedTargetAtom
        {
            get
            {
                if (personAtoms == null || targetDropdownValue < 0 || targetDropdownValue >= personAtoms.Count)
                    return null;
                Atom a = personAtoms[targetDropdownValue];
                if (a == null) return null;
                try { _ = a.uid; return a; } catch { return null; }
            }
        }

        public bool IsSubSceneTargetMode()
        {
            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            return !string.IsNullOrEmpty(title) && title.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CacheCategoryCounts()
        {
            if (categories == null) return;
            categoryCounts.Clear();
            foreach (var c in categories) categoryCounts[c.name] = 0;

            // Category side list should only "filter down" by creator selection.
            // If tags are active but no creator selected, keep counts global so categories don't disappear.
            var tagFilterForCategoryCounts = HasCreatorFilter() ? activeTags : null;
            if (VpbLocalDatabase.TryReadCategoryMemberCounts(categoryCounts, GetCreatorFilterForQueries(), tagFilterForCategoryCounts, currentPackagePathFilter, null))
            {
                // SQL path succeeded.
            }
            else
            {
                // Fallback: manual scan (O(N_files))
                // Build optimized lookup map for categories by extension
                // Map: Extension (lowercase, no dot) -> List of Categories
                Dictionary<string, List<Gallery.Category>> extToCats = new Dictionary<string, List<Gallery.Category>>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var c in categories) 
                {
                    if (string.IsNullOrEmpty(c.extension)) continue;
                    // Skip package-level pseudo-extension categories from file-entry scans
                    if (string.Equals(c.extension, "varpkg", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Gallery.IsEverythingCategoryExtension(c.extension)) continue;
                    string[] exts = c.extension.Split('|');
                    foreach(string ext in exts)
                    {
                        if (string.IsNullOrEmpty(ext)) continue;
                        string e = ext.Trim();
                        if (!extToCats.ContainsKey(e)) extToCats[e] = new List<Gallery.Category>();
                        extToCats[e].Add(c);
                    }
                }

                if (FileManager.PackagesByUid != null)
                {
                    var snapshot = FileManager.PackagesByUid.Values;
                    foreach (var pkg in snapshot)
                    {
                        if (pkg == null) continue;
                        // Filter by creator if set
                        if (!CreatorFilterMatchesPackageCreator(pkg.Creator)) continue;
                        if (!string.IsNullOrEmpty(currentPackagePathFilter) &&
                            !GalleryPathFilterMatchesRawPath(pkg.Path, currentPackagePathFilter))
                            continue;

                        if (pkg.FileEntries == null) continue;
                        
                        int count = pkg.FileEntries.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var entry = pkg.FileEntries[i];
                            string internalPath = entry.InternalPath;
                            
                            // Fast extension extraction
                            int lastDot = internalPath.LastIndexOf('.');
                            if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                            
                            string ext = internalPath.Substring(lastDot + 1);
                            
                            List<Gallery.Category> candidates;
                            if (extToCats.TryGetValue(ext, out candidates))
                            {
                                int candCount = candidates.Count;
                                for (int j = 0; j < candCount; j++)
                                {
                                    var cat = candidates[j];
                                    // Check path match
                                    bool pathMatch = false;
                                    if (cat.paths != null && cat.paths.Count > 0)
                                    {
                                        int pCount = cat.paths.Count;
                                        for(int k=0; k<pCount; k++)
                                        {
                                            if (GalleryInternalPathStartsWithPrefix(internalPath, cat.paths[k]))
                                            {
                                                pathMatch = true;
                                                break;
                                            }
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(cat.path))
                                    {
                                        if (GalleryInternalPathStartsWithPrefix(internalPath, cat.path))
                                            pathMatch = true;
                                    }
                                    else
                                    {
                                        // No path specified means match all (unlikely for category but possible)
                                        pathMatch = true;
                                    }

                                    if (pathMatch)
                                    {
                                        // Issue #101: top-level Clothing/Hair counts reflect base items (.vam) only,
                                        // excluding .vap presets that share the same category.
                                        if ((string.Equals(cat.name, "Clothing", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(cat.name, "Hair", StringComparison.OrdinalIgnoreCase)) &&
                                            !string.Equals(ext, "vam", StringComparison.OrdinalIgnoreCase))
                                        {
                                            break;
                                        }
                                        categoryCounts[cat.name]++;
                                        break; // File belongs to one category
                                    }
                                }
                            }
                        }
                    }

                    if (categoryCounts.ContainsKey(Gallery.EverythingCategoryName))
                    {
                        int ev = 0;
                        foreach (var pkg in snapshot)
                        {
                            if (pkg == null) continue;
                            if (!CreatorFilterMatchesPackageCreator(pkg.Creator)) continue;
                            if (!string.IsNullOrEmpty(currentPackagePathFilter) &&
                                !GalleryPathFilterMatchesRawPath(pkg.Path, currentPackagePathFilter))
                                continue;
                            if (pkg.FileEntries == null) continue;
                            for (int ei = 0; ei < pkg.FileEntries.Count; ei++)
                            {
                                string internalPath = pkg.FileEntries[ei].InternalPath;
                                int ld = internalPath.LastIndexOf('.');
                                if (ld <= 0 || ld >= internalPath.Length - 1) continue;
                                if (Gallery.IsEverythingExcludedPreviewExtension(internalPath.Substring(ld + 1))) continue;
                                ev++;
                            }
                        }
                        categoryCounts[Gallery.EverythingCategoryName] = ev;
                    }
                }
            }

            // Package-level pseudo-category counts: varpkg counts packages (use SQL pkg table).
            try
            {
                for (int ci = 0; ci < categories.Count; ci++)
                {
                    var c = categories[ci];
                    if (string.IsNullOrEmpty(c.name) || string.IsNullOrEmpty(c.extension)) continue;
                    if (!string.Equals(c.extension, "varpkg", StringComparison.OrdinalIgnoreCase)) continue;
                    string pkgPathFilterForVarPkg = HasCreatorFilter() ? currentPackagePathFilter : "";
                    if (VpbLocalDatabase.TryCountVarPackages(GetCreatorFilterForQueries(), pkgPathFilterForVarPkg, applyPathOnlyWhenCreator: true, out int n))
                        categoryCounts[c.name] = n;
                }
            }
            catch { }

            // Tab counts are VAR-only above; Custom/Scripts plugins live on local disk (same tree RefreshFiles scans).
            AddLocalCustomScriptsCountToCategory(categoryCounts, currentPackagePathFilter);
            AddLocalCustomAppearanceCountToCategory(categoryCounts, currentPackagePathFilter);

            if (CategoryCountsLookStaleAfterBuild())
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery] CacheCategoryCounts deferred (stale/zero while inventory loaded)");
                }
                catch { }
                categoriesCached = false;
                return;
            }

            categoriesCached = true;
            unchecked { categorySideTabDataRevision++; }
            StampSideTabCountsForCurrentScan();
        }

        private bool CategoryCountsLookStaleAfterBuild()
        {
            if (categoryCounts == null || categoryCounts.Count == 0) return true;
            int sum = 0;
            foreach (var kv in categoryCounts)
            {
                if (kv.Value > 0) sum += kv.Value;
            }
            if (sum > 0) return false;

            try
            {
                int pkgCount = 0;
                lock (FileManager.packagesLock)
                {
                    if (FileManager.PackagesByUid != null)
                        pkgCount = FileManager.PackagesByUid.Count;
                }
                return pkgCount > 64;
            }
            catch { return false; }
        }

        private void StampSideTabCountsForCurrentScan()
        {
            try { _lastCategoryCountsScanTime = FileManager.lastPackageRefreshTime; } catch { }
        }

        /// <summary>Rebuild category/creator tabs when package scan advanced since last side-tab count build.</summary>
        private void EnsureSideTabsFreshForPackageScan()
        {
            bool countsRefreshed = false;
            try { countsRefreshed = EnsureSideTabCountsFreshAfterGridReady(force: false); } catch { }
            if (!IsVisible && !hasLoadedContent) return;
            // rebuildSubPaneSideTabLists must be true when main strips rebuild — (true, false) clears split sub lists with no refill.
            if (countsRefreshed)
            {
                try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: true); } catch { }
            }
        }

        /// <summary>Rebuild side-tab counts once SQL/index + package scan are ready.</summary>
        /// <returns>True when category/creator counts were rebuilt (caller should refresh side-tab UI).</returns>
        private bool EnsureSideTabCountsFreshAfterGridReady(bool force)
        {
            DateTime scanNow = DateTime.MinValue;
            try { scanNow = FileManager.lastPackageRefreshTime; } catch { }
            bool stale = force
                || !categoriesCached
                || !creatorsCached
                || (scanNow > DateTime.MinValue && scanNow > _lastCategoryCountsScanTime);
            if (!stale) return false;

            categoriesCached = false;
            creatorsCached = false;
            try { InvalidateSharedSideMetaIfPackageScanAdvanced(); } catch { }
            try { CacheCategoryCounts(); } catch { }
            try { CacheCreators(); } catch { }
            try
            {
                int sc = 0;
                if (categoryCounts != null) categoryCounts.TryGetValue("Scenes", out sc);
                LogUtil.Log("[VPB.Gallery] EnsureSideTabCountsFreshAfterGridReady scenes=" + sc
                    + " cached=" + (categoriesCached ? "1" : "0"));
            }
            catch { }
            return true;
        }

        /// <summary>
        /// SQLite-cached recursive file count under <paramref name="root"/> for the given extensions.
        /// </summary>
        private static int CountLooseFilesCached(string root, string[] exts, string cacheKeyPrefix)
        {
            string sig = "0";
            try { sig = VpbLocalDatabase.DeepMaxDirMtimeBinary(root).ToString(); } catch { sig = "0"; }

            string extList = string.Join(",", exts);
            string cacheKey = cacheKeyPrefix + "|root=" + (Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/')) + "|exts=" + extList;

            int n = 0;
            try
            {
                var cached = new List<VpbLocalDatabase.SystemFileRow>();
                bool hit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached);
                if (hit && cached.Count > 0)
                {
                    n = cached.Count;
                }
                else
                {
                    var rows = new List<VpbLocalDatabase.SystemFileRow>(256);
                    for (int ei = 0; ei < exts.Length; ei++)
                    {
                        string ext = exts[ei];
                        var buf = new List<string>();
                        try
                        {
                            FileManager.SafeGetFiles(root, "*." + ext, buf);
                            n += buf.Count;
                            for (int i = 0; i < buf.Count; i++)
                            {
                                string p = buf[i];
                                if (string.IsNullOrEmpty(p)) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                rows.Add(r);
                            }
                        }
                        catch { }
                    }
                    if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Count .cs / .cslist / .dll under Custom/Scripts on disk so the Plugins category is not stuck at 0.
        /// (Package-only counting misses almost all session plugins.)
        /// </summary>
        private static void AddLocalCustomScriptsCountToCategory(Dictionary<string, int> counts, string selectedPathFilter = "")
        {
            if (counts == null || !counts.ContainsKey("Plugins")) return;
            const string root = "Custom/Scripts";
            if (!GalleryPathFilterMatchesFolder(root, selectedPathFilter)) return;
            try
            {
                if (!Directory.Exists(root)) return;
            }
            catch { return; }

            int n = CountLooseFilesCached(root, new[] { "cs", "cslist", "dll" }, "plugins:custom_scripts");

            // Ref cache has its own sig so it rebuilds even when the file-list cache is warm.
            string sig = "0";
            try { sig = VpbLocalDatabase.DeepMaxDirMtimeBinary(root).ToString(); } catch { sig = "0"; }
            const string refKey = "plugins:cslist_referenced_disk|root=Custom/Scripts";
            try
            {
                var refProbe = new List<VpbLocalDatabase.SystemFileRow>(1);
                bool refHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(refKey, sig, refProbe);
                if (!refHit)
                {
                    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var cslistBuf = new List<string>();
                    try { FileManager.SafeGetFiles(root, "*.cslist", cslistBuf); } catch { }
                    for (int i = 0; i < cslistBuf.Count; i++)
                    {
                        string cslistRel = cslistBuf[i];
                        if (string.IsNullOrEmpty(cslistRel)) continue;
                        try
                        {
                            string cslistFullForIO;
                            try { cslistFullForIO = Path.GetFullPath(cslistRel); } catch { cslistFullForIO = cslistRel; }
                            string cslistRelN = cslistRel.Replace('\\', '/');
                            string cslistRelDir;
                            int lastSlash = cslistRelN.LastIndexOf('/');
                            cslistRelDir = lastSlash > 0 ? cslistRelN.Substring(0, lastSlash) : string.Empty;
                            using (var fs = new FileStream(cslistFullForIO, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var refs = VPB.src.util.CslistParser.ParseReferencedCsPaths(fs, cslistRelDir);
                                for (int ri = 0; ri < refs.Count; ri++)
                                {
                                    string rp = refs[ri];
                                    if (!string.IsNullOrEmpty(rp)) referenced.Add(rp);
                                }
                            }
                        }
                        catch { }
                    }
                    var refRows = new List<VpbLocalDatabase.SystemFileRow>(referenced.Count);
                    foreach (var rp in referenced)
                    {
                        var rr = new VpbLocalDatabase.SystemFileRow();
                        rr.Path = rp;
                        rr.LastWriteBinaryOrInvalid = long.MinValue;
                        rr.SizeOrInvalid = long.MinValue;
                        refRows.Add(rr);
                    }
                    try { VpbLocalDatabase.TryWriteSystemFilesForCacheKey(refKey, sig, refRows); } catch { }
                }
            }
            catch { }
            counts["Plugins"] += n;
        }

        private static void AddLocalCustomAppearanceCountToCategory(Dictionary<string, int> counts, string selectedPathFilter = "")
        {
            if (counts == null || !counts.ContainsKey("Appearance")) return;
            string[] roots = new[] { "Saves/Person/appearance", "Custom/Atom/Person/Appearance" };
            int total = 0;
            foreach (var root in roots)
            {
                if (!GalleryPathFilterMatchesFolder(root, selectedPathFilter)) continue;
                try
                {
                    if (!Directory.Exists(root)) continue;
                }
                catch { continue; }

                total += CountLooseFilesCached(root, new[] { "vap" }, "appearance:custom_presets");
            }
            counts["Appearance"] += total;
        }

        /// <summary>
        /// Fill dest from count map, sort by name (culture, matches former LINQ OrderBy).
        /// Clear+reuse dest — no Select/ToList alloc. Safe on worker threads if dest is thread-local.
        /// </summary>
        internal static void FillCreatorCacheEntriesSorted(Dictionary<string, int> counts, List<CreatorCacheEntry> dest)
        {
            if (dest == null) return;
            dest.Clear();
            if (counts == null || counts.Count == 0) return;
            if (dest.Capacity < counts.Count) dest.Capacity = counts.Count;
            foreach (KeyValuePair<string, int> kv in counts)
            {
                CreatorCacheEntry e;
                e.Name = kv.Key;
                e.Count = kv.Value;
                dest.Add(e);
            }
            dest.Sort(CompareCreatorCacheEntryByName);
        }

        private static int CompareCreatorCacheEntryByName(CreatorCacheEntry a, CreatorCacheEntry b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// Fill dest from count map, sort path OrdinalIgnoreCase (matches former LINQ OrderBy).
        /// </summary>
        internal static void FillPathCacheEntriesSorted(Dictionary<string, int> counts, List<PathCacheEntry> dest)
        {
            if (dest == null) return;
            dest.Clear();
            if (counts == null || counts.Count == 0) return;
            if (dest.Capacity < counts.Count) dest.Capacity = counts.Count;
            foreach (KeyValuePair<string, int> kv in counts)
            {
                PathCacheEntry e;
                e.Path = kv.Key;
                e.Count = kv.Value;
                dest.Add(e);
            }
            dest.Sort(ComparePathCacheEntryByPathIgnoreCase);
        }

        private static int ComparePathCacheEntryByPathIgnoreCase(PathCacheEntry a, PathCacheEntry b)
        {
            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        }

        private void CacheCreators()
        {
            if (FileManager.PackagesByUid == null) return;
            PushCreatorFilterSqlModeForDatabase();

            Dictionary<string, int> counts = new Dictionary<string, int>();
            // Package-only category: creators list must be package creators (not internal-file creators).
            if (string.Equals(currentExtension, "varpkg", StringComparison.OrdinalIgnoreCase))
            {
                if (!VpbLocalDatabase.TryReadVarPackageCreatorCounts(counts, currentPackagePathFilter))
                {
                    foreach (var pkg in FileManager.PackagesByUid.Values)
                    {
                        if (pkg == null) continue;
                        if (string.IsNullOrEmpty(pkg.Creator)) continue;
                        if (!string.IsNullOrEmpty(currentPackagePathFilter) &&
                            !GalleryPathFilterMatchesRawPath(pkg.Path, currentPackagePathFilter))
                            continue;
                        int cur;
                        counts.TryGetValue(pkg.Creator, out cur);
                        counts[pkg.Creator] = cur + 1;
                    }
                }
            }
            else if (!VpbLocalDatabase.TryReadCreatorFileCounts(counts, currentExtension, currentPaths, currentPath, activeTags, currentCategoryTitle, currentPackagePathFilter, null))
            {
                string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
                HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());
                bool everythingExtForCreators = Gallery.IsEverythingCategoryExtension(currentExtension);

                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    if (string.IsNullOrEmpty(pkg.Creator)) continue;
                    if (pkg.FileEntries == null) continue;
                    if (!string.IsNullOrEmpty(currentPackagePathFilter) &&
                        !GalleryPathFilterMatchesRawPath(pkg.Path, currentPackagePathFilter))
                        continue;

                    int count = pkg.FileEntries.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = pkg.FileEntries[i];
                        string internalPath = entry.InternalPath;

                        int lastDot = internalPath.LastIndexOf('.');
                        if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                        string ext = internalPath.Substring(lastDot + 1);
                        if (everythingExtForCreators && Gallery.IsEverythingExcludedPreviewExtension(ext)) continue;
                        if (!everythingExtForCreators && !targetExts.Contains(ext)) continue;

                        bool match = false;
                        if (currentPaths != null && currentPaths.Count > 0)
                        {
                            for (int k = 0; k < currentPaths.Count; k++)
                            {
                                if (GalleryInternalPathStartsWithPrefix(internalPath, currentPaths[k])) { match = true; break; }
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentPath))
                        {
                            if (GalleryInternalPathStartsWithPrefix(internalPath, currentPath)) match = true;
                        }
                        else
                        {
                            match = true;
                        }

                        if (match)
                        {
                            int cur;
                            counts.TryGetValue(pkg.Creator, out cur);
                            counts[pkg.Creator] = cur + 1;
                        }
                    }
                }
            }

            FillCreatorCacheEntriesSorted(counts, cachedCreators);
            creatorsCached = true;
            unchecked { creatorSideTabDataRevision++; }
        }

        private static readonly string[] GalleryPathFilterRoots = new[] { "AddonPackages/", "AllPackages/", "Custom/", "Saves/" };

        internal static bool TryNormalizeGalleryPathUnderKnownRoots(string rawPath, out string normalizedPath)
        {
            normalizedPath = "";
            if (string.IsNullOrEmpty(rawPath)) return false;

            string p = rawPath.Replace('\\', '/');
            int varSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (varSep > 0) p = p.Substring(0, varSep);
            p = p.Trim();
            if (p.Length == 0) return false;

            for (int i = 0; i < GalleryPathFilterRoots.Length; i++)
            {
                string root = GalleryPathFilterRoots[i];
                int idx = p.IndexOf(root, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                string rel = p.Substring(idx).TrimStart('/');
                if (rel.Length == 0) return false;
                normalizedPath = rel.TrimEnd('/');
                return normalizedPath.Length > 0;
            }

            return false;
        }

        /// <summary>
        /// Parent folder of a normalized file path (slash-safe; avoids <see cref="Path.GetDirectoryName"/> quirks).
        /// </summary>
        internal static string TryGetParentFolderFromNormalizedPath(string normalizedFilePath)
        {
            if (string.IsNullOrEmpty(normalizedFilePath)) return null;
            string p = normalizedFilePath.Replace('\\', '/').TrimEnd('/');
            int slash = p.LastIndexOf('/');
            if (slash <= 0) return null;
            string folder = p.Substring(0, slash).TrimEnd('/');
            return folder.Length == 0 ? null : folder;
        }

        internal static string TryGetPathFilterFolderForEntry(FileEntry entry)
        {
            if (entry == null) return null;
            string normalized;
            if (!TryNormalizeGalleryPathUnderKnownRoots(entry.Path, out normalized)) return null;
            return TryGetParentFolderFromNormalizedPath(normalized);
        }

        internal static bool GalleryPathFilterMatchesFolder(string folderPath, string selectedFilter)
        {
            if (string.IsNullOrEmpty(selectedFilter)) return true;
            if (string.IsNullOrEmpty(folderPath)) return false;
            return folderPath.Equals(selectedFilter, StringComparison.OrdinalIgnoreCase)
                || folderPath.StartsWith(selectedFilter + "/", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool GalleryPathFilterMatchesRawPath(string rawPath, string selectedFilter)
        {
            if (string.IsNullOrEmpty(selectedFilter)) return true;
            string normalized;
            if (!TryNormalizeGalleryPathUnderKnownRoots(rawPath, out normalized)) return false;
            string folder = TryGetParentFolderFromNormalizedPath(normalized);
            if (string.IsNullOrEmpty(folder)) return false;
            return GalleryPathFilterMatchesFolder(folder, selectedFilter);
        }

        /// <summary>
        /// True when <paramref name="filter"/> still matches at least one live .var path or an on-disk folder.
        /// Used to drop Path filters whose folder was deleted in Explorer.
        /// </summary>
        internal static bool PackagePathFilterStillResolves(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string f = filter.Replace('\\', '/').Trim().TrimEnd('/');
            if (f.Length == 0) return true;

            // Folder still on disk (incl. empty AddonPackages/new) — keep filter.
            try
            {
                if (Directory.Exists(f)) return true;
            }
            catch { }

            try
            {
                lock (FileManager.packagesLock)
                {
                    Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                    if (byUid != null)
                    {
                        foreach (KeyValuePair<string, VarPackage> kv in byUid)
                        {
                            VarPackage pkg = kv.Value;
                            if (pkg == null) continue;
                            if (GalleryPathFilterMatchesRawPath(pkg.Path, f))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Clears <see cref="currentPackagePathFilter"/> when the selected folder no longer exists
        /// (Explorer delete/move). Returns true when the filter was cleared.
        /// </summary>
        internal bool TryClearStalePackagePathFilter()
        {
            if (string.IsNullOrEmpty(currentPackagePathFilter)) return false;
            if (PackagePathFilterStillResolves(currentPackagePathFilter)) return false;

            try
            {
                LogUtil.Log("[VPB.Gallery] cleared stale Path filter '" + currentPackagePathFilter + "'");
            }
            catch { }
            currentPackagePathFilter = "";
            pathsCached = false;
            return true;
        }

        /// <summary>
        /// Seed Custom/Saves roots that exist on disk (count 0). Folder tree stays visible even when
        /// current category has no loose hits under those roots.
        /// </summary>
        private static void EnsureLoosePathRootsSeeded(Dictionary<string, int> folders)
        {
            if (folders == null) return;
            for (int i = 0; i < GalleryPathFilterRoots.Length; i++)
            {
                string rootWithSlash = GalleryPathFilterRoots[i];
                if (rootWithSlash.StartsWith("AddonPackages", StringComparison.OrdinalIgnoreCase)) continue;
                if (rootWithSlash.StartsWith("AllPackages", StringComparison.OrdinalIgnoreCase)) continue;

                string root = rootWithSlash.TrimEnd('/');
                try
                {
                    if (!Directory.Exists(root)) continue;
                }
                catch { continue; }

                if (!folders.ContainsKey(root))
                    folders[root] = 0;
            }
        }

        /// <summary>
        /// Category-agnostic Path folder tree: every known package/loose folder, counts start at 0.
        /// Category-scoped counts overlay later in <see cref="CachePaths"/>.
        /// </summary>
        private static void SeedAgnosticPathFolders(Dictionary<string, int> folders)
        {
            if (folders == null) return;
            try
            {
                lock (FileManager.packagesLock)
                {
                    Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                    if (byUid != null)
                    {
                        foreach (KeyValuePair<string, VarPackage> kv in byUid)
                        {
                            VarPackage pkg = kv.Value;
                            if (pkg == null || string.IsNullOrEmpty(pkg.Path)) continue;
                            string normalized;
                            if (!TryNormalizeGalleryPathUnderKnownRoots(pkg.Path, out normalized)) continue;
                            string folder = TryGetParentFolderFromNormalizedPath(normalized);
                            if (string.IsNullOrEmpty(folder)) continue;
                            SeedPathFolderHierarchy(folders, folder);
                        }
                    }
                }
            }
            catch { }

            try { VpbLocalDatabase.SeedPackageFoldersFromVarPathInventory(folders); } catch { }

            EnsureLoosePathRootsSeeded(folders);
        }

        /// <summary>
        /// When SQLite Path counts are unavailable, build AddonPackages/AllPackages folder hierarchy
        /// from the live registry (full tree, not the path-filtered grid).
        /// </summary>
        private static void AddPathCountsFromLiveVarPackages(Dictionary<string, int> counts)
        {
            if (counts == null) return;
            try
            {
                lock (FileManager.packagesLock)
                {
                    Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                    if (byUid == null || byUid.Count == 0) return;
                    foreach (KeyValuePair<string, VarPackage> kv in byUid)
                    {
                        VarPackage pkg = kv.Value;
                        if (pkg == null || string.IsNullOrEmpty(pkg.Path)) continue;
                        string normalized;
                        if (!TryNormalizeGalleryPathUnderKnownRoots(pkg.Path, out normalized)) continue;
                        string folder = TryGetParentFolderFromNormalizedPath(normalized);
                        if (string.IsNullOrEmpty(folder)) continue;
                        AddPathHierarchyCount(counts, folder);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Ensure folder + parents exist in the map with count 0 (do not inflate existing counts).
        /// </summary>
        private static void SeedPathFolderHierarchy(Dictionary<string, int> folders, string folderPath)
        {
            if (folders == null || string.IsNullOrEmpty(folderPath)) return;
            string p = folderPath.Replace('\\', '/').Trim('/');
            if (p.Length == 0) return;

            for (int i = 0; i < GalleryPathFilterRoots.Length; i++)
            {
                string root = GalleryPathFilterRoots[i].TrimEnd('/');
                if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                string[] seg = p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (seg.Length == 0) return;
                string running = seg[0];
                for (int si = 1; si <= seg.Length; si++)
                {
                    if (!folders.ContainsKey(running))
                        folders[running] = 0;
                    if (si < seg.Length) running += "/" + seg[si];
                }
                return;
            }
        }

        private static void AddPathHierarchyCount(Dictionary<string, int> counts, string folderPath)
        {
            if (counts == null || string.IsNullOrEmpty(folderPath)) return;
            string p = folderPath.Replace('\\', '/').Trim('/');
            if (p.Length == 0) return;

            for (int i = 0; i < GalleryPathFilterRoots.Length; i++)
            {
                string root = GalleryPathFilterRoots[i].TrimEnd('/');
                if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                string[] seg = p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (seg.Length == 0) return;
                string running = seg[0];
                for (int si = 1; si <= seg.Length; si++)
                {
                    int cur;
                    counts.TryGetValue(running, out cur);
                    counts[running] = cur + 1;
                    if (si < seg.Length) running += "/" + seg[si];
                }
                return;
            }
        }

        private static void AddPathCountsFromEntries(Dictionary<string, int> counts, IList<FileEntry> files, bool includeVarRows, bool includeLooseRows)
        {
            if (counts == null || files == null) return;
            for (int i = 0; i < files.Count; i++)
            {
                FileEntry fe = files[i];
                if (fe == null) continue;
                bool isVar = fe is VarFileEntry;
                if (isVar && !includeVarRows) continue;
                if (!isVar && !includeLooseRows) continue;
                string folder = TryGetPathFilterFolderForEntry(fe);
                if (string.IsNullOrEmpty(folder)) continue;
                AddPathHierarchyCount(counts, folder);
            }
        }

        private void CachePaths()
        {
            // 1) Category-agnostic folder tree (presence only — zeros until overlay).
            var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            SeedAgnosticPathFolders(folders);

            // 2) Category-scoped counts (SQL + loose grid; live registry only when SQL unavailable).
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool sqlOk = VpbLocalDatabase.TryReadPackageFolderCounts(
                counts,
                currentExtension,
                currentPaths,
                currentPath,
                activeTags,
                currentCategoryTitle,
                GetCreatorFilterForQueries(),
                null);

            IList<FileEntry> source = currentFilteredFiles != null && currentFilteredFiles.Count > 0
                ? (IList<FileEntry>)currentFilteredFiles
                : (IList<FileEntry>)lastFilteredFiles;
            AddPathCountsFromEntries(counts, source, includeVarRows: !sqlOk, includeLooseRows: true);

            if (!sqlOk)
                AddPathCountsFromLiveVarPackages(counts);

            // 3) Overlay category counts onto agnostic tree (folders with no hits stay 0).
            foreach (KeyValuePair<string, int> kv in counts)
                folders[kv.Key] = kv.Value;

            // Mid-refresh with nothing useful yet — keep previous Path list, rebuild when grid finishes.
            if (refreshCoroutine != null && folders.Count == 0)
            {
                pathsCached = false;
                return;
            }

            FillPathCacheEntriesSorted(folders, cachedPaths);
            pathsCached = true;
        }

        private void CacheUserTagsSideTab()
        {
            cachedUserTagSideTab.Clear();
            string cat = currentCategoryTitle ?? "";
            if (titleText != null && string.IsNullOrEmpty(cat)) cat = titleText.text ?? "";

            var allNames = new List<string>(128);
            if (!VpbLocalDatabase.TryReadAllGalleryUserTagNames(allNames))
            {
                userTagsCached = true;
                unchecked { userTagSideTabDataRevision++; }
                return;
            }

            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool countsOk = false;
            if (!string.IsNullOrEmpty(cat))
                countsOk = VpbLocalDatabase.TryReadGalleryUserTagSideTabCounts(cat, "", "", dict);

            for (int i = 0; i < allNames.Count; i++)
            {
                string name = allNames[i];
                int c = 0;
                if (countsOk) dict.TryGetValue(name, out c);
                cachedUserTagSideTab.Add(new UserTagSideTabEntry { Name = name, Count = c });
            }
            userTagsCached = countsOk;
            unchecked { userTagSideTabDataRevision++; }
        }

        public void InvalidateTags()
        {
            ClearAppearanceGenderRefreshCaches();
            tagsCached = false;
            userTagsCached = false;
            GalleryTagCountSnapshotCache.Clear();
        }

        /// <summary>When max slice ms is below this, <see cref="CoCacheTagCountsInternal"/> yields so the UI thread stays responsive.</summary>
        private const int TagCountScanNoSliceMs = 1_000_000;

        /// <summary>Per-frame budget when tag counting runs from <see cref="GalleryPanel.DeferredGallerySideTabsAfterGridReady"/>.</summary>
        private const int TagCountScanDeferredSliceMs = 20;

        private static bool TagCountScanShouldYieldFrame(int maxMsPerSlice, Stopwatch sliceWatch, int deferredSessionId, int currentDeferredSessionId, out bool cancelled)
        {
            cancelled = false;
            if (maxMsPerSlice >= TagCountScanNoSliceMs || sliceWatch == null) return false;
            if (sliceWatch.ElapsedMilliseconds < maxMsPerSlice) return false;
            sliceWatch.Reset();
            sliceWatch.Start();
            if (deferredSessionId >= 0 && deferredSessionId != currentDeferredSessionId)
            {
                cancelled = true;
                return false;
            }
            return true;
        }

        /// <summary>Runs the full tag/facet scan on the current thread (can block for many seconds). Prefer <see cref="ScheduleTagCountsForSideTabsNonBlocking"/> from UI paths.</summary>
        private void CacheTagCounts()
        {
            var e = CoCacheTagCountsInternal(TagCountScanNoSliceMs, -1);
            while (e.MoveNext()) { }
        }

        /// <summary>
        /// Used from <see cref="GalleryPanel.UpdateTabs"/> when <c>!tagsCached</c>: same work as <see cref="CacheTagCounts"/> but time-sliced so Clothing subfilter clicks do not freeze the UI
        /// while <see cref="GalleryPanel.RefreshFiles"/> is also queued (previously both compounded on the main thread).
        /// </summary>
        private void ScheduleTagCountsForSideTabsNonBlocking()
        {
            if (tagsCached) return;
            if (_sideTabsTagCountSliceCo != null)
                return;
            int sessionSnap = _deferredSubPaneSessionId;
            _sideTabsTagCountSliceCo = StartCoroutine(CoTagCountsForSideTabsSlice(sessionSnap));
        }

        private IEnumerator CoTagCountsForSideTabsSlice(int sessionWhenStarted)
        {
            try
            {
                IEnumerator scan = CoCacheTagCountsInternal(TagCountScanDeferredSliceMs, sessionWhenStarted);
                while (scan.MoveNext())
                {
                    if (sessionWhenStarted != _deferredSubPaneSessionId)
                        yield break;
                    yield return scan.Current;
                }
                if (!tagsCached || sessionWhenStarted != _deferredSubPaneSessionId)
                    yield break;
                try { RebuildSubPaneSideTabListsOnly(); } catch { }
            }
            finally
            {
                _sideTabsTagCountSliceCo = null;
            }
        }

        private void ApplyTagScanTotalsFromWorker(GalleryTagCountBackgroundScan.TagScanTotals t)
        {
            if (t == null) return;
            appearanceSourceCountAll = t.AppearanceSourceCountAll;
            appearanceSourceCountPresets = t.AppearanceSourceCountPresets;
            appearanceSourceCountCustom = t.AppearanceSourceCountCustom;
            clothingSubfilterCountAll = t.ClothingSubfilterCountAll;
            clothingSubfilterCountReal = t.ClothingSubfilterCountReal;
            clothingSubfilterCountPresets = t.ClothingSubfilterCountPresets;
            clothingSubfilterCountCustom = t.ClothingSubfilterCountCustom;
            clothingSubfilterCountCustomPreset = t.ClothingSubfilterCountCustomPreset;
            clothingSubfilterCountItems = t.ClothingSubfilterCountItems;
            clothingSubfilterCountMale = t.ClothingSubfilterCountMale;
            clothingSubfilterCountFemale = t.ClothingSubfilterCountFemale;
            clothingSubfilterCountDecals = t.ClothingSubfilterCountDecals;
            hairSubfilterCountAll = t.HairSubfilterCountAll;
            hairSubfilterCountPresets = t.HairSubfilterCountPresets;
            hairSubfilterCountCustom = t.HairSubfilterCountCustom;
            hairSubfilterCountCustomPreset = t.HairSubfilterCountCustomPreset;
            hairSubfilterCountItems = t.HairSubfilterCountItems;
            hairSubfilterCountMale = t.HairSubfilterCountMale;
            hairSubfilterCountFemale = t.HairSubfilterCountFemale;
            appearanceSubfilterCountAll = t.AppearanceSubfilterCountAll;
            appearanceSubfilterCountPresets = t.AppearanceSubfilterCountPresets;
            appearanceSubfilterCountCustom = t.AppearanceSubfilterCountCustom;
            appearanceSubfilterCountMale = t.AppearanceSubfilterCountMale;
            appearanceSubfilterCountFemale = t.AppearanceSubfilterCountFemale;
            appearanceSubfilterCountFuta = t.AppearanceSubfilterCountFuta;
            appearanceSubfilterCountUnknown = t.AppearanceSubfilterCountUnknown;
            clothingSubfilterFacetCountReal = t.ClothingSubfilterFacetCountReal;
            clothingSubfilterFacetCountPresets = t.ClothingSubfilterFacetCountPresets;
            clothingSubfilterFacetCountCustom = t.ClothingSubfilterFacetCountCustom;
            clothingSubfilterFacetCountCustomPreset = t.ClothingSubfilterFacetCountCustomPreset;
            clothingSubfilterFacetCountItems = t.ClothingSubfilterFacetCountItems;
            clothingSubfilterFacetCountMale = t.ClothingSubfilterFacetCountMale;
            clothingSubfilterFacetCountFemale = t.ClothingSubfilterFacetCountFemale;
            clothingSubfilterFacetCountDecals = t.ClothingSubfilterFacetCountDecals;
            hairSubfilterFacetCountPresets = t.HairSubfilterFacetCountPresets;
            hairSubfilterFacetCountCustom = t.HairSubfilterFacetCountCustom;
            hairSubfilterFacetCountCustomPreset = t.HairSubfilterFacetCountCustomPreset;
            hairSubfilterFacetCountItems = t.HairSubfilterFacetCountItems;
            hairSubfilterFacetCountMale = t.HairSubfilterFacetCountMale;
            hairSubfilterFacetCountFemale = t.HairSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountPresets = t.AppearanceSubfilterFacetCountPresets;
            appearanceSubfilterFacetCountCustom = t.AppearanceSubfilterFacetCountCustom;
            appearanceSubfilterFacetCountMale = t.AppearanceSubfilterFacetCountMale;
            appearanceSubfilterFacetCountFemale = t.AppearanceSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountFuta = t.AppearanceSubfilterFacetCountFuta;
            appearanceSubfilterFacetCountUnknown = t.AppearanceSubfilterFacetCountUnknown;
            appearanceSubfilterCurrentCountAll = t.AppearanceSubfilterCurrentCountAll;
            appearanceSubfilterCurrentCountMale = t.AppearanceSubfilterCurrentCountMale;
            appearanceSubfilterCurrentCountFemale = t.AppearanceSubfilterCurrentCountFemale;
            appearanceSubfilterCurrentCountFuta = t.AppearanceSubfilterCurrentCountFuta;
            appearanceSubfilterCurrentCountUnknown = t.AppearanceSubfilterCurrentCountUnknown;
        }

        /// <summary>Prime Hair/Clothing subfilter chip counts from SQLite VAR rows so split sub-pane buttons appear immediately.</summary>
        private bool TryApplyHairClothingSubfilterCountsFromSql()
        {
            if (!VpbSqlite3.IsAvailable) return false;
            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isClothing && !isHair) return false;

            string cp = currentPath ?? "";
            int sourceFilterMode = ResolveEffectiveSourceFilterMode(false, cp);
            if (sourceFilterMode == 1) return false;

            string extJ = string.IsNullOrEmpty(currentExtension) ? "" : currentExtension;
            var tagCountsScratch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            VpbLocalDatabase.TagScanTotals sqlFacets;
            if (!VpbLocalDatabase.TryReadTagCounts(
                    title,
                    extJ,
                    GetCreatorFilterForQueries(),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    tagCountsScratch,
                    out sqlFacets,
                    clothingSubfilter,
                    hairSubfilter,
                    appearanceSubfilter,
                    activeTags))
                return false;

            clothingSubfilterCountAll = sqlFacets.ClothingSubfilterCountAll;
            clothingSubfilterCountReal = sqlFacets.ClothingSubfilterCountReal;
            clothingSubfilterCountPresets = sqlFacets.ClothingSubfilterCountPresets;
            clothingSubfilterCountCustom = sqlFacets.ClothingSubfilterCountCustom;
            clothingSubfilterCountCustomPreset = sqlFacets.ClothingSubfilterCountCustomPreset;
            clothingSubfilterCountItems = sqlFacets.ClothingSubfilterCountItems;
            clothingSubfilterCountMale = sqlFacets.ClothingSubfilterCountMale;
            clothingSubfilterCountFemale = sqlFacets.ClothingSubfilterCountFemale;
            clothingSubfilterCountDecals = sqlFacets.ClothingSubfilterCountDecals;
            hairSubfilterCountAll = sqlFacets.HairSubfilterCountAll;
            hairSubfilterCountPresets = sqlFacets.HairSubfilterCountPresets;
            hairSubfilterCountCustom = sqlFacets.HairSubfilterCountCustom;
            hairSubfilterCountCustomPreset = sqlFacets.HairSubfilterCountCustomPreset;
            hairSubfilterCountItems = sqlFacets.HairSubfilterCountItems;
            hairSubfilterCountMale = sqlFacets.HairSubfilterCountMale;
            hairSubfilterCountFemale = sqlFacets.HairSubfilterCountFemale;
            clothingSubfilterFacetCountReal = sqlFacets.ClothingSubfilterFacetCountReal;
            clothingSubfilterFacetCountPresets = sqlFacets.ClothingSubfilterFacetCountPresets;
            clothingSubfilterFacetCountCustom = sqlFacets.ClothingSubfilterFacetCountCustom;
            clothingSubfilterFacetCountCustomPreset = sqlFacets.ClothingSubfilterFacetCountCustomPreset;
            clothingSubfilterFacetCountItems = sqlFacets.ClothingSubfilterFacetCountItems;
            clothingSubfilterFacetCountMale = sqlFacets.ClothingSubfilterFacetCountMale;
            clothingSubfilterFacetCountFemale = sqlFacets.ClothingSubfilterFacetCountFemale;
            clothingSubfilterFacetCountDecals = sqlFacets.ClothingSubfilterFacetCountDecals;
            hairSubfilterFacetCountPresets = sqlFacets.HairSubfilterFacetCountPresets;
            hairSubfilterFacetCountCustom = sqlFacets.HairSubfilterFacetCountCustom;
            hairSubfilterFacetCountCustomPreset = sqlFacets.HairSubfilterFacetCountCustomPreset;
            hairSubfilterFacetCountItems = sqlFacets.HairSubfilterFacetCountItems;
            hairSubfilterFacetCountMale = sqlFacets.HairSubfilterFacetCountMale;
            hairSubfilterFacetCountFemale = sqlFacets.HairSubfilterFacetCountFemale;
            return true;
        }

        private IEnumerator CoCacheTagCountsInternal(int maxMsPerSlice, int deferredSessionId)
        {
            tagCounts.Clear();
            if (FileManager.PackagesByUid == null) yield break;

            string tagCountCacheKey;
            if (TryBuildTagCountCacheKey(out tagCountCacheKey))
            {
                TagCountSnapshot cachedSnap;
                if (GalleryTagCountSnapshotCache.TryGet(tagCountCacheKey, out cachedSnap))
                {
                    RestoreTagCountSnapshot(cachedSnap);
                    tagsCached = true;
                    yield break;
                }
            }

            if (IsAppearanceLooseScopedBrowsing())
            {
                TryRecomputeAppearanceGenderFacetCountsScoped();
                tagsCached = true;
                if (TryBuildTagCountCacheKey(out tagCountCacheKey))
                {
                    try { GalleryTagCountSnapshotCache.Put(tagCountCacheKey, CaptureTagCountSnapshot()); } catch { }
                }
                yield break;
            }

            Stopwatch sliceWatch = (maxMsPerSlice < TagCountScanNoSliceMs) ? Stopwatch.StartNew() : null;

            appearanceSourceCountAll = 0;
            appearanceSourceCountPresets = 0;
            appearanceSourceCountCustom = 0;

            clothingSubfilterCountAll = 0;
            clothingSubfilterCountReal = 0;
            clothingSubfilterCountPresets = 0;
            clothingSubfilterCountCustom = 0;
            clothingSubfilterCountCustomPreset = 0;
            clothingSubfilterCountItems = 0;
            clothingSubfilterCountMale = 0;
            clothingSubfilterCountFemale = 0;
            clothingSubfilterCountDecals = 0;

            hairSubfilterCountAll = 0;
            hairSubfilterCountPresets = 0;
            hairSubfilterCountCustom = 0;
            hairSubfilterCountCustomPreset = 0;
            hairSubfilterCountItems = 0;
            hairSubfilterCountMale = 0;
            hairSubfilterCountFemale = 0;

            appearanceSubfilterCountAll = 0;
            appearanceSubfilterCountPresets = 0;
            appearanceSubfilterCountCustom = 0;
            appearanceSubfilterCountMale = 0;
            appearanceSubfilterCountFemale = 0;
            appearanceSubfilterCountFuta = 0;
            appearanceSubfilterCountUnknown = 0;

            clothingSubfilterFacetCountReal = 0;
            clothingSubfilterFacetCountPresets = 0;
            clothingSubfilterFacetCountCustom = 0;
            clothingSubfilterFacetCountCustomPreset = 0;
            clothingSubfilterFacetCountItems = 0;
            clothingSubfilterFacetCountMale = 0;
            clothingSubfilterFacetCountFemale = 0;
            clothingSubfilterFacetCountDecals = 0;

            hairSubfilterFacetCountPresets = 0;
            hairSubfilterFacetCountCustom = 0;
            hairSubfilterFacetCountCustomPreset = 0;
            hairSubfilterFacetCountItems = 0;
            hairSubfilterFacetCountMale = 0;
            hairSubfilterFacetCountFemale = 0;

            appearanceSubfilterFacetCountPresets = 0;
            appearanceSubfilterFacetCountCustom = 0;
            appearanceSubfilterFacetCountMale = 0;
            appearanceSubfilterFacetCountFemale = 0;
            appearanceSubfilterFacetCountFuta = 0;
            appearanceSubfilterFacetCountUnknown = 0;

            appearanceSubfilterCurrentCountAll = 0;
            appearanceSubfilterCurrentCountMale = 0;
            appearanceSubfilterCurrentCountFemale = 0;
            appearanceSubfilterCurrentCountFuta = 0;
            appearanceSubfilterCurrentCountUnknown = 0;

            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            // Build extension set for fast lookup
            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());
            bool everythingExtMode = Gallery.IsEverythingCategoryExtension(currentExtension);

            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            string cp = currentPath ?? "";
            bool isClothingTitle = (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                || cp.IndexOf("/Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHairTitle = (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                || cp.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAppearanceTitle = (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0);
            int sourceFilterMode = ResolveEffectiveSourceFilterMode(isAppearanceTitle, cp);
            bool countVarRows = (sourceFilterMode != 1);
            bool countLooseFiles = (sourceFilterMode != 2);
            if (isClothingTitle)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (isHairTitle)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            
            // Include user-defined tags
            tagsToCount.UnionWith(TagsManager.Instance.GetAllUserTags());

            bool hasAnyTagsToCount = (tagsToCount.Count > 0);

            // Split tags into single-word and multi-word
            HashSet<string> singleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> multiWordTags = new List<string>();
            char[] separators = new char[] { '/', '\\', '.', '_', '-', ' ' };
            char[] multiWordSeparators = new char[] { ' ', '_', '-' };

            if (hasAnyTagsToCount)
            {
                foreach (var t in tagsToCount)
                {
                    if (t.IndexOfAny(multiWordSeparators) >= 0) multiWordTags.Add(t);
                    else singleWordTags.Add(t);
                }
            }

            HashSet<string> foundTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TagCountParallelInputs tagParForScan;
            TryBuildTagCountParallelInputs(out tagParForScan);
            var coTagScanTotals = new GalleryTagCountBackgroundScan.TagScanTotals();
            bool coVarFromSql = false;
            if (tagParForScan != null && VpbSqlite3.IsAvailable && sliceWatch != null)
            {
                if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;
                // One frame for RefreshFilesRoutine / file-list worker to start before we synchronously load huge SQL row lists here.
                yield return null;
                if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;
            }
            // Skip the SQL fast path under Source: Local. cat_mem holds var-backed rows only, and the var counters
            // must be zero in Local mode; the live-loop walk below will produce loose-only counts correctly.
            if (tagParForScan != null && VpbSqlite3.IsAvailable && countVarRows)
            {
                VpbLocalDatabase.TagScanTotals sqlFacets;
                string extJ = GalleryTagCountBackgroundScan.JoinExtensionsForTagScan(tagParForScan.ExtensionsSplit);
                if (VpbLocalDatabase.TryReadTagCounts(tagParForScan.Title, extJ, tagParForScan.CurrentCreator ?? "", tagsToCount, tagCounts, out sqlFacets, clothingSubfilter, hairSubfilter, appearanceSubfilter, activeTags))
                {
                    coVarFromSql = true;
                    // Map sqlFacets back to our local variables
                    appearanceSourceCountAll = sqlFacets.AppearanceSourceCountAll;
                    appearanceSourceCountPresets = sqlFacets.AppearanceSourceCountPresets;
                    appearanceSourceCountCustom = sqlFacets.AppearanceSourceCountCustom;
                    clothingSubfilterCountAll = sqlFacets.ClothingSubfilterCountAll;
                    clothingSubfilterCountReal = sqlFacets.ClothingSubfilterCountReal;
                    clothingSubfilterCountPresets = sqlFacets.ClothingSubfilterCountPresets;
                    clothingSubfilterCountCustom = sqlFacets.ClothingSubfilterCountCustom;
                    clothingSubfilterCountCustomPreset = sqlFacets.ClothingSubfilterCountCustomPreset;
                    clothingSubfilterCountItems = sqlFacets.ClothingSubfilterCountItems;
                    clothingSubfilterCountMale = sqlFacets.ClothingSubfilterCountMale;
                    clothingSubfilterCountFemale = sqlFacets.ClothingSubfilterCountFemale;
                    clothingSubfilterCountDecals = sqlFacets.ClothingSubfilterCountDecals;
                    hairSubfilterCountAll = sqlFacets.HairSubfilterCountAll;
                    hairSubfilterCountPresets = sqlFacets.HairSubfilterCountPresets;
                    hairSubfilterCountCustom = sqlFacets.HairSubfilterCountCustom;
                    hairSubfilterCountCustomPreset = sqlFacets.HairSubfilterCountCustomPreset;
                    hairSubfilterCountItems = sqlFacets.HairSubfilterCountItems;
                    hairSubfilterCountMale = sqlFacets.HairSubfilterCountMale;
                    hairSubfilterCountFemale = sqlFacets.HairSubfilterCountFemale;
                    appearanceSubfilterCountAll = sqlFacets.AppearanceSubfilterCountAll;
                    appearanceSubfilterCountPresets = sqlFacets.AppearanceSubfilterCountPresets;
                    appearanceSubfilterCountCustom = sqlFacets.AppearanceSubfilterCountCustom;
                    appearanceSubfilterCountMale = sqlFacets.AppearanceSubfilterCountMale;
                    appearanceSubfilterCountFemale = sqlFacets.AppearanceSubfilterCountFemale;
                    appearanceSubfilterCountFuta = sqlFacets.AppearanceSubfilterCountFuta;
                    appearanceSubfilterCountUnknown = sqlFacets.AppearanceSubfilterCountUnknown;
                    clothingSubfilterFacetCountReal = sqlFacets.ClothingSubfilterFacetCountReal;
                    clothingSubfilterFacetCountPresets = sqlFacets.ClothingSubfilterFacetCountPresets;
                    clothingSubfilterFacetCountCustom = sqlFacets.ClothingSubfilterFacetCountCustom;
                    clothingSubfilterFacetCountCustomPreset = sqlFacets.ClothingSubfilterFacetCountCustomPreset;
                    clothingSubfilterFacetCountItems = sqlFacets.ClothingSubfilterFacetCountItems;
                    clothingSubfilterFacetCountMale = sqlFacets.ClothingSubfilterFacetCountMale;
                    clothingSubfilterFacetCountFemale = sqlFacets.ClothingSubfilterFacetCountFemale;
                    clothingSubfilterFacetCountDecals = sqlFacets.ClothingSubfilterFacetCountDecals;
                    hairSubfilterFacetCountPresets = sqlFacets.HairSubfilterFacetCountPresets;
                    hairSubfilterFacetCountCustom = sqlFacets.HairSubfilterFacetCountCustom;
                    hairSubfilterFacetCountCustomPreset = sqlFacets.HairSubfilterFacetCountCustomPreset;
                    hairSubfilterFacetCountItems = sqlFacets.HairSubfilterFacetCountItems;
                    hairSubfilterFacetCountMale = sqlFacets.HairSubfilterFacetCountMale;
                    hairSubfilterFacetCountFemale = sqlFacets.HairSubfilterFacetCountFemale;
                    appearanceSubfilterFacetCountPresets = sqlFacets.AppearanceSubfilterFacetCountPresets;
                    appearanceSubfilterFacetCountCustom = sqlFacets.AppearanceSubfilterFacetCountCustom;
                    appearanceSubfilterFacetCountMale = sqlFacets.AppearanceSubfilterFacetCountMale;
                    appearanceSubfilterFacetCountFemale = sqlFacets.AppearanceSubfilterFacetCountFemale;
                    appearanceSubfilterFacetCountFuta = sqlFacets.AppearanceSubfilterFacetCountFuta;
                    appearanceSubfilterFacetCountUnknown = sqlFacets.AppearanceSubfilterFacetCountUnknown;
                    appearanceSubfilterCurrentCountAll = sqlFacets.AppearanceSubfilterCurrentCountAll;
                    appearanceSubfilterCurrentCountMale = sqlFacets.AppearanceSubfilterCurrentCountMale;
                    appearanceSubfilterCurrentCountFemale = sqlFacets.AppearanceSubfilterCurrentCountFemale;
                    appearanceSubfilterCurrentCountFuta = sqlFacets.AppearanceSubfilterCurrentCountFuta;
                    appearanceSubfilterCurrentCountUnknown = sqlFacets.AppearanceSubfilterCurrentCountUnknown;
                }
            }

            if (!coVarFromSql)
            {
            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledPkg))
                    yield return null;
                if (cancelledPkg) yield break;

                if (pkg.FileEntries == null) continue;
                
                // If filtering by creator, respect it
                if (!CreatorFilterMatchesPackageCreator(pkg.Creator)) continue;

                int count = pkg.FileEntries.Count;
                for (int i = 0; i < count; i++)
                {
                    if ((i & 0xFF) == 0xFF)
                    {
                        if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledEntry))
                            yield return null;
                        if (cancelledEntry) yield break;
                    }

                    var entry = pkg.FileEntries[i];
                    string internalPath = entry.InternalPath;

                    // 1. Check extension
                    int lastDot = internalPath.LastIndexOf('.');
                    if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                    string ext = internalPath.Substring(lastDot + 1);
                    if (everythingExtMode && Gallery.IsEverythingExcludedPreviewExtension(ext)) continue;
                    if (!everythingExtMode && !targetExts.Contains(ext)) continue;

                    // 2. Check path match (Inline IsMatch logic)
                    bool match = false;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                        for(int k=0; k<currentPaths.Count; k++)
                        {
                            if (internalPath.StartsWith(currentPaths[k], StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                         if (internalPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        match = true;
                    }

                    if (!match) continue;

                    if (isClothingTitle)
                    {
						ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
						ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
						bool isClothingEntry = false;
						bool isPresetEntry = false;
						bool isCustomPreset = false;

						ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
						isClothingEntry = (ck == ClothingLoadingUtils.ResourceKind.Clothing);
						if (isClothingEntry)
                        {
                            // For Clothing category we include both .vam and .vap, and subfilters split them.
							isPresetEntry = (ext.Equals("vap", StringComparison.OrdinalIgnoreCase));
							// VAR entries are never considered "Custom".
							isCustomPreset = false;

                            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(internalPath);

                            ClothingSubfilter cur = clothingSubfilter;
                            bool PassesClothingSubfilters(ClothingSubfilter f)
                            {
                                // Issue #101: with no flags set, default-hide .vap presets so the
                                // grid does not show duplicate base + preset pairs.
                                if (f == 0) return !isPresetEntry;

                                bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.CustomPreset | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                if (typeExplicit)
                                {
                                    bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                  (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                    if (!okType) return false;
                                }
                                else
                                {
                                    if (wantsRealType && isDecal && !wantsDecalType) return false;
                                }

                                bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
								bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
                                bool wantsCustomPreset = (f & ClothingSubfilter.CustomPreset) != 0;
								if (wantsPresets) { if (!isPresetEntry) return false; }
								if (wantsCustom) { if (!isCustomPreset) return false; }
                                if (wantsCustomPreset) return false;
                                // Default-hide presets unless Presets/Custom/Custom Preset toggle is on.
                                if (!wantsPresets && !wantsCustom && !wantsCustomPreset) { if (isPresetEntry) return false; }
								if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
								if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
								if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }

                                return true;
                            }

                            // Facet counts: how many would be shown if the user toggled that flag now.
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
                            if (PassesClothingSubfilters(cur ^ ClothingSubfilter.CustomPreset)) clothingSubfilterFacetCountCustomPreset++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

							// All Clothing includes everything: real clothing + decals
							clothingSubfilterCountAll++;

                            // Decals are counted separately and excluded from real clothing filters by default.
                            if (isDecal)
                            {
                                clothingSubfilterCountDecals++;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool wantsRealType = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((clothingSubfilter & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.Decals) == 0) continue;
                                    }
                                    else
                                    {
                                        if (wantsRealType && !wantsDecalType) continue;
                                    }

                                    // If user also selected real-only constraints, decals won't match.
                                    if ((clothingSubfilter & (ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.CustomPreset | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0) continue;
                                }
                            }
                            else
                            {
                                clothingSubfilterCountReal++;
                                if (isPresetEntry) clothingSubfilterCountPresets++;
								if (isCustomPreset) clothingSubfilterCountCustom++;
								if (!isPresetEntry) clothingSubfilterCountItems++;
                                if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;

                                // Issue #101: default-hide presets when no subfilter is active so
                                // tag counts reflect what is actually shown in the grid.
                                if (clothingSubfilter == 0 && isPresetEntry) continue;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.RealClothing) == 0) continue;
                                    }
                                    // Additional constraints
                                    bool wantsPresets = (clothingSubfilter & ClothingSubfilter.Presets) != 0;
                                    bool wantsCustom = (clothingSubfilter & ClothingSubfilter.Custom) != 0;
                                    bool wantsCustomPreset = (clothingSubfilter & ClothingSubfilter.CustomPreset) != 0;
                                    if (wantsPresets) { if (!isPresetEntry) continue; }
                                    if (wantsCustom) { if (!isCustomPreset) continue; }
                                    if (wantsCustomPreset) continue;
                                    // Default-hide presets unless Presets/Custom/Custom Preset toggle is on.
                                    if (!wantsPresets && !wantsCustom && !wantsCustomPreset && isPresetEntry) continue;
                                    if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPresetEntry) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male && cg != ClothingLoadingUtils.ResourceGender.Unknown) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female && cg != ClothingLoadingUtils.ResourceGender.Unknown) continue; }
                                }
                            }
                        }
                        else
                        {
                            // When browsing Clothing, ignore non-clothing entries for tag counts.
                            continue;
                        }
                    }

                    if (isAppearanceTitle)
                    {
                        // Source-filter gate: var rows are var-backed by definition, so a Local-only filter must
                        // exclude them from every appearance counter (matches the grid's PassesFilters behavior).
                        if (!countVarRows) continue;

                        string p = internalPath.Replace('\\', '/');
                        bool isAppearance = p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isAppearance)
                        {
                            // When browsing Appearance, ignore non-appearance entries for tag counts.
                            continue;
                        }

                        bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                        bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                        AppearanceGender g = AppearanceGender.Unknown;
                        try { g = GetAppearanceGender(entry); } catch { g = AppearanceGender.Unknown; }

                        appearanceSubfilterCountAll++;
                        if (isPresetAppearance) appearanceSubfilterCountPresets++;
                        if (isCustomAppearance) appearanceSubfilterCountCustom++;
                        if (g == AppearanceGender.Male) appearanceSubfilterCountMale++;
                        if (g == AppearanceGender.Female) appearanceSubfilterCountFemale++;
                        if (g == AppearanceGender.Futa) appearanceSubfilterCountFuta++;
                        if (g == AppearanceGender.Unknown) appearanceSubfilterCountUnknown++;

                        AppearanceSubfilter cur = appearanceSubfilter;
                        bool PassesAppearanceSubfilters(AppearanceSubfilter f)
                        {
                            if (f == 0) return true;
                            bool wantsPresets = (f & AppearanceSubfilter.Presets) != 0;
                            bool wantsCustom = (f & AppearanceSubfilter.Custom) != 0;

                            bool typeOk = true;
                            if (wantsPresets || wantsCustom)
                            {
                                if (wantsPresets && wantsCustom) typeOk = true;
                                else if (wantsPresets) typeOk = isPresetAppearance;
                                else if (wantsCustom) typeOk = isCustomAppearance;
                            }
                            if (!typeOk) return false;
                            if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(g, f)) return false;
                            return true;
                        }

                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Presets)) appearanceSubfilterFacetCountPresets++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Custom)) appearanceSubfilterFacetCountCustom++;
                        if (PassesAppearanceSubfilters(AppearanceGenderClassifier.HypotheticalGenderFacet(cur, AppearanceSubfilter.Male))) appearanceSubfilterFacetCountMale++;
                        if (PassesAppearanceSubfilters(AppearanceGenderClassifier.HypotheticalGenderFacet(cur, AppearanceSubfilter.Female))) appearanceSubfilterFacetCountFemale++;
                        if (PassesAppearanceSubfilters(AppearanceGenderClassifier.HypotheticalGenderFacet(cur, AppearanceSubfilter.Futa))) appearanceSubfilterFacetCountFuta++;
                        if (PassesAppearanceSubfilters(AppearanceGenderClassifier.HypotheticalGenderFacet(cur, AppearanceSubfilter.Unknown))) appearanceSubfilterFacetCountUnknown++;

                        if (PassesAppearanceSubfilters(appearanceSubfilter))
                        {
                            appearanceSubfilterCurrentCountAll++;
                            if (g == AppearanceGender.Male) appearanceSubfilterCurrentCountMale++;
                            if (g == AppearanceGender.Female) appearanceSubfilterCurrentCountFemale++;
                            if (g == AppearanceGender.Futa) appearanceSubfilterCurrentCountFuta++;
                            if (g == AppearanceGender.Unknown) appearanceSubfilterCurrentCountUnknown++;
                        }

                        // Apply active subfilters (if any) to tag counting.
                        if (appearanceSubfilter != 0)
                        {
                            if (!PassesAppearanceSubfilters(appearanceSubfilter)) continue;
                        }
                    }

                    // Appearance split-pane counts (All/Presets/Custom)
                    if (isAppearanceTitle && countVarRows)
                    {
                        if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase))
                        {
                            if (internalPath.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                            {
                                // Presets = appearance .vap inside .var packages
                                appearanceSourceCountPresets++;
                                appearanceSourceCountAll++;
                            }
                        }
                    }

                    if (hasAnyTagsToCount)
                    {
                        // 3. Count tags
                        // Tokenize path for single-word tags; singleWordTags uses OrdinalIgnoreCase so no lowering needed
                        string[] tokens = internalPath.Split(separators);

                        foundTags.Clear();

                        // Check tokens against single word tags
                        for (int k = 0; k < tokens.Length; k++)
                        {
                            if (singleWordTags.Contains(tokens[k]))
                            {
                                foundTags.Add(tokens[k].ToLowerInvariant());
                            }
                        }

                        // Check multi-word tags using case-insensitive IndexOf
                        for (int k = 0; k < multiWordTags.Count; k++)
                        {
                            if (internalPath.IndexOf(multiWordTags[k], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foundTags.Add(multiWordTags[k]);
                            }
                        }

                        // Check user-defined tags specifically for this entry
                        var uTags = TagsManager.Instance.GetTags(entry.Uid);
                        foreach (var ut in uTags)
                        {
                            if (tagsToCount.Contains(ut)) foundTags.Add(ut);
                        }

                        // Increment counts
                        foreach (var tag in foundTags)
                        {
                            int cur;
                            tagCounts.TryGetValue(tag, out cur);
                            tagCounts[tag] = cur + 1;
                        }
                    }
                }
            }
            }
            if (coVarFromSql)
                ApplyTagScanTotalsFromWorker(coTagScanTotals);

            // Count Clothing (local filesystem) entries for subfilter facet counts.
            // This is intentionally separate from the package loop above.
            if (isClothingTitle)
            {
                if (!HasCreatorFilter())
                {
                    List<string> pathsToSearch = new List<string>();
                    if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                    else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                    // Prefer SQLite-cached loose-file enumeration (same mechanism as RefreshFilesRoutine).
                    string sysCacheKey = null;
                    string sysCacheSig = null;
                    List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                    bool sysCacheHit = false;
                    try
                    {
                        var sbKey = new StringBuilder(256);
                        sbKey.Append("tags:loose:clothing|");
                        sbKey.Append("ext=");
                        if (extensions != null && extensions.Length > 0)
                        {
                            var ex = new List<string>(extensions);
                            ex.Sort(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < ex.Count; i++)
                            {
                                if (i != 0) sbKey.Append(',');
                                sbKey.Append(ex[i] ?? "");
                            }
                        }
                        sbKey.Append("|paths=");
                        var p2 = new List<string>(pathsToSearch);
                        p2.Sort(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            if (i != 0) sbKey.Append(';');
                            sbKey.Append((p2[i] ?? "").Replace('\\', '/').TrimEnd('/'));
                        }
                        sysCacheKey = sbKey.ToString();

                        var sbSig = new StringBuilder(128);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            // Deep dir-mtime so additions in subfolders invalidate this cache.
                            long t = 0;
                            try { t = VpbLocalDatabase.DeepMaxDirMtimeBinary(p2[i]); } catch { t = 0; }
                            if (i != 0) sbSig.Append('|');
                            sbSig.Append(t.ToString());
                        }
                        sysCacheSig = sbSig.ToString();

                        sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
                    }
                    catch { sysCacheHit = false; sysCached = null; }

                    for (int pi = 0; pi < pathsToSearch.Count; pi++)
                    {
                        string searchPath = pathsToSearch[pi];
                        if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                        for (int ei = 0; ei < extensions.Length; ei++)
                        {
                            string ext = extensions[ei];
                            if (string.IsNullOrEmpty(ext)) continue;

                            List<string> sysFileList = null;
                            if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                            {
                                sysFileList = new List<string>();
                                for (int i = 0; i < sysCached.Count; i++)
                                {
                                    string p = sysCached[i].Path ?? "";
                                    if (p.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                                        sysFileList.Add(p);
                                }
                            }
                            else
                            {
                                sysFileList = new List<string>();
                                try { FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList); }
                                catch { continue; }
                            }

                            for (int fi = 0; fi < sysFileList.Count; fi++)
                            {
                                if ((fi & 0x7F) == 0x7F)
                                {
                                    if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledClothFs))
                                        yield return null;
                                    if (cancelledClothFs) yield break;
                                }

                                string sysPath = sysFileList[fi] ?? "";
                                string norm = sysPath.Replace('\\', '/');
                                bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                                bool isCustomItem = ClothingLoadingUtils.IsLooseCustomClothingItemPath(norm);
                                bool isCustomPresetLoose = ClothingLoadingUtils.IsLooseCustomClothingPresetPath(norm);

                                ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
                                ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
                                ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                                if (ck != ClothingLoadingUtils.ResourceKind.Clothing) continue;

                                bool isDecal = ClothingLoadingUtils.IsDecalLikePath(sysPath);

                                ClothingSubfilter cur = clothingSubfilter;
                                bool PassesClothingSubfilters(ClothingSubfilter f)
                                {
                                    if (f == 0) return !isPresetEntry;

                                    bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.CustomPreset | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                      (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                        if (!okType) return false;
                                    }
                                    else
                                    {
                                        if (wantsRealType && isDecal && !wantsDecalType) return false;
                                    }

                                    bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
                                    bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
                                    bool wantsCustomPreset = (f & ClothingSubfilter.CustomPreset) != 0;
                                    if (wantsPresets) { if (!isPresetEntry || isCustomItem || isCustomPresetLoose) return false; }
                                    if (wantsCustom) { if (!isCustomItem) return false; }
                                    if (wantsCustomPreset) { if (!isCustomPresetLoose || !isPresetEntry) return false; }
                                    if (!wantsPresets && !wantsCustom && !wantsCustomPreset) { if (isPresetEntry) return false; }
                                    if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                                    if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
                                    if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }

                                    return true;
                                }

                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.CustomPreset)) clothingSubfilterFacetCountCustomPreset++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

                                clothingSubfilterCountAll++;
                                if (isDecal)
                                {
                                    clothingSubfilterCountDecals++;
                                }
                                else
                                {
                                    clothingSubfilterCountReal++;
                                    if (isPresetEntry && !isCustomItem && !isCustomPresetLoose) clothingSubfilterCountPresets++;
                                    if (isCustomItem) clothingSubfilterCountCustom++;
                                    if (isCustomPresetLoose && isPresetEntry) clothingSubfilterCountCustomPreset++;
                                    if (!isPresetEntry) clothingSubfilterCountItems++;
                                    if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                    else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;
                                }
                            }
                        }
                    }

                    if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                    {
                        try
                        {
                            var rows = new List<VpbLocalDatabase.SystemFileRow>(512);
                            for (int pi = 0; pi < pathsToSearch.Count; pi++)
                            {
                                string sp = pathsToSearch[pi];
                                if (string.IsNullOrEmpty(sp) || !Directory.Exists(sp)) continue;
                                for (int ei = 0; ei < extensions.Length; ei++)
                                {
                                    string ext = extensions[ei];
                                    if (string.IsNullOrEmpty(ext)) continue;
                                    var buf = new List<string>();
                                    try { FileManager.SafeGetFiles(sp, "*." + ext, buf); }
                                    catch { continue; }
                                    for (int i = 0; i < buf.Count; i++)
                                    {
                                        string p = buf[i] ?? "";
                                        if (p.Length == 0) continue;
                                        var r = new VpbLocalDatabase.SystemFileRow();
                                        r.Path = p;
                                        r.LastWriteBinaryOrInvalid = long.MinValue;
                                        r.SizeOrInvalid = long.MinValue;
                                        rows.Add(r);
                                    }
                                }
                            }
                            if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, rows);
                        }
                        catch { }
                    }
                }
            }

            // Tally override moved to BuildTagsTabs (last writer before chip render; covers the cache-hit and parallel-scan paths this coroutine doesn't).

            // Count Hair (local filesystem) entries for subfilter facet counts.
            // Separate from package loop above (mirrors Clothing block).
            if (isHairTitle)
            {
                if (!HasCreatorFilter())
                {
                    List<string> pathsToSearch = new List<string>();
                    if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                    else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                    string sysCacheKey = null;
                    string sysCacheSig = null;
                    List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                    bool sysCacheHit = false;
                    try
                    {
                        var sbKey = new StringBuilder(256);
                        sbKey.Append("tags:loose:hair|");
                        sbKey.Append("ext=");
                        if (extensions != null && extensions.Length > 0)
                        {
                            var ex = new List<string>(extensions);
                            ex.Sort(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < ex.Count; i++)
                            {
                                if (i != 0) sbKey.Append(',');
                                sbKey.Append(ex[i] ?? "");
                            }
                        }
                        sbKey.Append("|paths=");
                        var p2 = new List<string>(pathsToSearch);
                        p2.Sort(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            if (i != 0) sbKey.Append(';');
                            sbKey.Append((p2[i] ?? "").Replace('\\', '/').TrimEnd('/'));
                        }
                        sysCacheKey = sbKey.ToString();

                        var sbSig = new StringBuilder(128);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            // Deep dir-mtime so additions in subfolders invalidate this cache.
                            long t = 0;
                            try { t = VpbLocalDatabase.DeepMaxDirMtimeBinary(p2[i]); } catch { t = 0; }
                            if (i != 0) sbSig.Append('|');
                            sbSig.Append(t.ToString());
                        }
                        sysCacheSig = sbSig.ToString();

                        sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
                    }
                    catch { sysCacheHit = false; sysCached = null; }

                    for (int pi = 0; pi < pathsToSearch.Count; pi++)
                    {
                        string searchPath = pathsToSearch[pi];
                        if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                        for (int ei = 0; ei < extensions.Length; ei++)
                        {
                            string ext = extensions[ei];
                            if (string.IsNullOrEmpty(ext)) continue;

                            List<string> sysFileList = null;
                            if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                            {
                                sysFileList = new List<string>();
                                for (int i = 0; i < sysCached.Count; i++)
                                {
                                    string p = sysCached[i].Path ?? "";
                                    if (p.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                                        sysFileList.Add(p);
                                }
                            }
                            else
                            {
                                sysFileList = new List<string>();
                                try { FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList); }
                                catch { continue; }
                            }

                            for (int fi = 0; fi < sysFileList.Count; fi++)
                            {
                                if ((fi & 0x7F) == 0x7F)
                                {
                                    if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledHairFs))
                                        yield return null;
                                    if (cancelledHairFs) yield break;
                                }

                                string sysPath = sysFileList[fi] ?? "";
                                string norm = sysPath.Replace('\\', '/');
                                bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                                bool isCustomItem = ClothingLoadingUtils.IsLooseCustomHairItemPath(norm);
                                bool isCustomPresetLoose = ClothingLoadingUtils.IsLooseCustomHairPresetPath(norm);

                                ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
                                ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
                                ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                                if (ck != ClothingLoadingUtils.ResourceKind.Hair) continue;

                                HairSubfilter cur = hairSubfilter;
                                bool PassesHairSubfilters(HairSubfilter f)
                                {
                                    if (f == 0) return !isPresetEntry;
                                    bool wantsPresets = (f & HairSubfilter.Presets) != 0;
                                    bool wantsCustom = (f & HairSubfilter.Custom) != 0;
                                    bool wantsCustomPreset = (f & HairSubfilter.CustomPreset) != 0;
                                    if (wantsPresets) { if (!isPresetEntry || isCustomItem || isCustomPresetLoose) return false; }
                                    if (wantsCustom) { if (!isCustomItem) return false; }
                                    if (wantsCustomPreset) { if (!isCustomPresetLoose || !isPresetEntry) return false; }
                                    if (!wantsPresets && !wantsCustom && !wantsCustomPreset) { if (isPresetEntry) return false; }
                                    if ((f & HairSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                                    if ((f & HairSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                                    if ((f & HairSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                                    return true;
                                }

                                if (PassesHairSubfilters(cur ^ HairSubfilter.Presets)) hairSubfilterFacetCountPresets++;
                                if (PassesHairSubfilters(cur ^ HairSubfilter.Custom)) hairSubfilterFacetCountCustom++;
                                if (PassesHairSubfilters(cur ^ HairSubfilter.CustomPreset)) hairSubfilterFacetCountCustomPreset++;
                                if (PassesHairSubfilters(cur ^ HairSubfilter.Items)) hairSubfilterFacetCountItems++;
                                if (PassesHairSubfilters(cur ^ HairSubfilter.Male)) hairSubfilterFacetCountMale++;
                                if (PassesHairSubfilters(cur ^ HairSubfilter.Female)) hairSubfilterFacetCountFemale++;

                                hairSubfilterCountAll++;
                                if (isPresetEntry && !isCustomItem && !isCustomPresetLoose) hairSubfilterCountPresets++;
                                if (isCustomItem) hairSubfilterCountCustom++;
                                if (isCustomPresetLoose && isPresetEntry) hairSubfilterCountCustomPreset++;
                                if (!isPresetEntry) hairSubfilterCountItems++;
                                if (cg == ClothingLoadingUtils.ResourceGender.Male) hairSubfilterCountMale++;
                                else if (cg == ClothingLoadingUtils.ResourceGender.Female) hairSubfilterCountFemale++;
                            }
                        }
                    }

                    if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                    {
                        try
                        {
                            var rows = new List<VpbLocalDatabase.SystemFileRow>(512);
                            for (int pi = 0; pi < pathsToSearch.Count; pi++)
                            {
                                string sp = pathsToSearch[pi];
                                if (string.IsNullOrEmpty(sp) || !Directory.Exists(sp)) continue;
                                for (int ei = 0; ei < extensions.Length; ei++)
                                {
                                    string ext = extensions[ei];
                                    if (string.IsNullOrEmpty(ext)) continue;
                                    var buf = new List<string>();
                                    try { FileManager.SafeGetFiles(sp, "*." + ext, buf); }
                                    catch { continue; }
                                    for (int i = 0; i < buf.Count; i++)
                                    {
                                        string p = buf[i] ?? "";
                                        if (p.Length == 0) continue;
                                        var r = new VpbLocalDatabase.SystemFileRow();
                                        r.Path = p;
                                        r.LastWriteBinaryOrInvalid = long.MinValue;
                                        r.SizeOrInvalid = long.MinValue;
                                        rows.Add(r);
                                    }
                                }
                            }
                            if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, rows);
                        }
                        catch { }
                    }
                }
            }

            // Count Custom (local filesystem) appearances for split-pane counts.
            // This is intentionally separate from the package loop above.
            // Source-filter gate: skip the loose-file scan entirely when the user is on Source: Var.
            if (isAppearanceTitle && countLooseFiles)
            {
                List<string> pathsToSearch = new List<string>();
                if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                string sysCacheKey = null;
                string sysCacheSig = null;
                List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                bool sysCacheHit = false;
                try
                {
                    var p2 = new List<string>(pathsToSearch);
                    p2.Sort(StringComparer.OrdinalIgnoreCase);
                    var sbKey = new StringBuilder(256);
                    sbKey.Append("tags:loose:appearance|ext=vap|paths=");
                    for (int i = 0; i < p2.Count; i++)
                    {
                        if (i != 0) sbKey.Append(';');
                        sbKey.Append((p2[i] ?? "").Replace('\\', '/').TrimEnd('/'));
                    }
                    sysCacheKey = sbKey.ToString();

                    var sbSig = new StringBuilder(128);
                    for (int i = 0; i < p2.Count; i++)
                    {
                        // Deep dir-mtime so additions in subfolders invalidate this cache.
                        long t = 0;
                        try { t = VpbLocalDatabase.DeepMaxDirMtimeBinary(p2[i]); } catch { t = 0; }
                        if (i != 0) sbSig.Append('|');
                        sbSig.Append(t.ToString());
                    }
                    sysCacheSig = sbSig.ToString();

                    sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                    sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
                }
                catch { sysCacheHit = false; sysCached = null; }

                for (int pi = 0; pi < pathsToSearch.Count; pi++)
                {
                    string searchPath = pathsToSearch[pi];
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                    List<string> sysFileList = null;
                    if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                    {
                        sysFileList = new List<string>();
                        for (int i = 0; i < sysCached.Count; i++)
                        {
                            string p = sysCached[i].Path ?? "";
                            if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                                sysFileList.Add(p);
                        }
                    }
                    else
                    {
                        sysFileList = new List<string>();
                        try { FileManager.SafeGetFiles(searchPath, "*.vap", sysFileList); }
                        catch { continue; }
                    }

                    for (int fi = 0; fi < sysFileList.Count; fi++)
                    {
                        if ((fi & 0x7F) == 0x7F)
                        {
                            if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledAppFs))
                                yield return null;
                            if (cancelledAppFs) yield break;
                        }

                        string sysPath = sysFileList[fi] ?? "";
                        string norm = sysPath.Replace('\\', '/');
                        if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                            !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        appearanceSourceCountCustom++;
                        appearanceSourceCountAll++;

                        // Gender + subfilter counting for loose .vap. Matches the merge contract used by the var path
                        // so totals add up when SourceFilterMode == All.
                        bool isCustomLoose = norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                        bool isPresetLoose = norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                        AppearanceGender lg = AppearanceGender.Unknown;
                        try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? ""); }
                        catch { lg = AppearanceGender.Unknown; }

                        appearanceSubfilterCountAll++;
                        if (isPresetLoose) appearanceSubfilterCountPresets++;
                        if (isCustomLoose) appearanceSubfilterCountCustom++;
                        if (lg == AppearanceGender.Male) appearanceSubfilterCountMale++;
                        if (lg == AppearanceGender.Female) appearanceSubfilterCountFemale++;
                        if (lg == AppearanceGender.Futa) appearanceSubfilterCountFuta++;
                        if (lg == AppearanceGender.Unknown) appearanceSubfilterCountUnknown++;

                        bool LoosePasses(AppearanceSubfilter f)
                        {
                            if (f == 0) return true;
                            bool wPresets = (f & AppearanceSubfilter.Presets) != 0;
                            bool wCustom  = (f & AppearanceSubfilter.Custom) != 0;
                            bool typeOk = true;
                            if (wPresets || wCustom)
                            {
                                if (wPresets && wCustom) typeOk = true;
                                else if (wPresets) typeOk = isPresetLoose;
                                else if (wCustom) typeOk = isCustomLoose;
                            }
                            if (!typeOk) return false;
                            if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(lg, f)) return false;
                            return true;
                        }

                        AppearanceSubfilter aSub = appearanceSubfilter;
                        if (LoosePasses(aSub ^ AppearanceSubfilter.Presets)) appearanceSubfilterFacetCountPresets++;
                        if (LoosePasses(aSub ^ AppearanceSubfilter.Custom))  appearanceSubfilterFacetCountCustom++;
                        if (LoosePasses(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Male)))    appearanceSubfilterFacetCountMale++;
                        if (LoosePasses(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Female)))  appearanceSubfilterFacetCountFemale++;
                        if (LoosePasses(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Futa)))    appearanceSubfilterFacetCountFuta++;
                        if (LoosePasses(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Unknown))) appearanceSubfilterFacetCountUnknown++;
                        if (LoosePasses(aSub))
                        {
                            appearanceSubfilterCurrentCountAll++;
                            if (lg == AppearanceGender.Male) appearanceSubfilterCurrentCountMale++;
                            if (lg == AppearanceGender.Female) appearanceSubfilterCurrentCountFemale++;
                            if (lg == AppearanceGender.Futa) appearanceSubfilterCurrentCountFuta++;
                            if (lg == AppearanceGender.Unknown) appearanceSubfilterCurrentCountUnknown++;
                        }
                    }
                }

                if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                {
                    try
                    {
                        var rows = new List<VpbLocalDatabase.SystemFileRow>(512);
                        for (int pi = 0; pi < pathsToSearch.Count; pi++)
                        {
                            string sp = pathsToSearch[pi];
                            if (string.IsNullOrEmpty(sp) || !Directory.Exists(sp)) continue;
                            var buf = new List<string>();
                            try { FileManager.SafeGetFiles(sp, "*.vap", buf); }
                            catch { continue; }
                            for (int i = 0; i < buf.Count; i++)
                            {
                                string p = buf[i] ?? "";
                                if (p.Length == 0) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                r.Path = p;
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                rows.Add(r);
                            }
                        }
                        if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, rows);
                    }
                    catch { }
                }
            }

            tagsCached = true;
            if (TryBuildTagCountCacheKey(out tagCountCacheKey))
            {
                try { GalleryTagCountSnapshotCache.Put(tagCountCacheKey, CaptureTagCountSnapshot()); } catch { }
            }
        }

        /// <summary>Stable key for <see cref="GalleryTagCountSnapshotCache"/> when tag/facet counts depend only on category + filters + package scan.</summary>
        /// <summary>
        /// Resolves the effective source filter for the tag scan / cache key. The per-category Local toggle on
        /// Appearance / Scenes overrides the global filter; otherwise the global filter wins. Returned values:
        /// 0=All, 1=Local, 2=Var.
        /// </summary>
        private int ResolveEffectiveSourceFilterMode(bool isAppearanceTitle, string cp)
        {
            bool localOverride = false;
            try
            {
                if (isAppearanceTitle && string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
                    localOverride = true;
                bool isScene = (!string.IsNullOrEmpty(cp)) && cp.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isScene && string.Equals(currentSceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
                    localOverride = true;
            }
            catch { }
            if (localOverride) return 1;
            if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local) return 1;
            if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var)   return 2;
            return 0;
        }

        private bool TryBuildTagCountCacheKey(out string key)
        {
            key = null;
            try
            {
                var sb = new StringBuilder(384);
                sb.Append(titleText != null ? titleText.text : "").Append('\u001E');
                sb.Append(currentPath ?? "").Append('\u001E');
                if (currentPaths != null)
                {
                    for (int i = 0; i < currentPaths.Count; i++)
                    {
                        sb.Append(currentPaths[i] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
                sb.Append(currentExtension ?? "").Append('\u001E');
                sb.Append(currentCreator ?? "").Append('\u001E');
                sb.Append((int)clothingSubfilter).Append('\u001E');
                sb.Append((int)hairSubfilter).Append('\u001E');
                sb.Append((int)appearanceSubfilter).Append('\u001E');
                sb.Append(currentAppearanceSourceFilter ?? "").Append('\u001E');
                long pr = 0;
                string ckTitle = !string.IsNullOrEmpty(currentCategoryTitle)
                    ? currentCategoryTitle
                    : (titleText != null ? titleText.text : "");
                bool ckIsAppearance = (ckTitle != null) && ckTitle.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0;
                sb.Append(ResolveEffectiveSourceFilterMode(ckIsAppearance, currentPath ?? "")).Append((char)0x1E);
                try { pr = FileManager.lastPackageRefreshTime.ToBinary(); } catch { pr = 0; }
                sb.Append(pr).Append('\u001E');
                int utc = 0;
                try { utc = TagsManager.Instance.GetAllUserTags().Count; } catch { utc = 0; }
                sb.Append(utc).Append('\u001E');
                sb.Append(userTagSideTabDataRevision);
                bool hov = false;
                try { hov = Settings.Instance != null && Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value; } catch { hov = false; }
                sb.Append('').Append(hov ? 1 : 0);
                key = sb.ToString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Builds immutable inputs for <see cref="GalleryTagCountBackgroundScan"/> (main thread only).</summary>
        internal bool TryBuildTagCountParallelInputs(out TagCountParallelInputs inputs)
        {
            inputs = null;
            if (FileManager.PackagesByUid == null) return false;

            PushCreatorFilterSqlModeForDatabase();
            inputs = new TagCountParallelInputs();
            inputs.Title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            inputs.CurrentPath = currentPath ?? "";
            inputs.CurrentPathsCopy = currentPaths != null ? new List<string>(currentPaths) : null;
            inputs.CurrentCreator = GetCreatorFilterForQueries();
            inputs.ActiveTagsCopy = activeTags != null && activeTags.Count > 0 ? new HashSet<string>(activeTags, StringComparer.OrdinalIgnoreCase) : null;
            inputs.ClothingSubfilterVal = clothingSubfilter;
            inputs.HairSubfilterVal = hairSubfilter;
            inputs.AppearanceSubfilterVal = appearanceSubfilter;
            inputs.ExtensionsSplit = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');

            string cp = currentPath ?? "";
            inputs.IsClothingTitle = (inputs.Title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                || cp.IndexOf("/Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            inputs.IsHairTitle = (inputs.Title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                || cp.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            inputs.IsAppearanceTitle = (inputs.Title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0);
            inputs.SourceFilterMode = ResolveEffectiveSourceFilterMode(inputs.IsAppearanceTitle, cp);

            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (inputs.IsClothingTitle)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (inputs.IsHairTitle)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            tagsToCount.UnionWith(TagsManager.Instance.GetAllUserTags());
            inputs.TagsToCount = tagsToCount;
            inputs.HasAnyTagsToCount = (tagsToCount.Count > 0);

            inputs.SingleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            inputs.MultiWordTags = new List<string>();
            if (inputs.HasAnyTagsToCount)
            {
                char[] multiWordSeparators = new char[] { ' ', '_', '-' };
                foreach (string t in tagsToCount)
                {
                    if (t.IndexOfAny(multiWordSeparators) >= 0) inputs.MultiWordTags.Add(t);
                    else inputs.SingleWordTags.Add(t);
                }
            }

            return true;
        }

        // Memo keyed on the refresh-session id (bumped in RefreshFiles, the routine that rebuilds the grid).
        // The sub-pane rebuilds several times per refresh (left+right panes, multiple hooks); counts can't
        // change within one session, so compute once and reuse. New refresh -> new session -> recompute,
        // in lockstep with the grid, so the chip numbers stay consistent with what the grid shows.
        private int _clothingChipCountsSession = -1;
        private VpbLocalDatabase.ClothingChipCounts _clothingChipCountsCached;

        // Counts each chip using the grid's own SQL WHERE builder (BuildGalleryCategoryWhere),
        // so the number on each chip = what the grid shows when only that chip is active.
        // Memoized per refresh session; called on every clothing sub-pane build.
        private void ApplyClothingChipCountsFromSqlIfEnabled()
        {
            try
            {
                string cp = currentPath ?? "";
                int sourceFilterMode = ResolveEffectiveSourceFilterMode(false, cp);
                string creator = currentCreator ?? "";

                // loadedState from current sort state (mirrors how RefreshFilesRoutine derives it).
                int loadedState = -1;
                try
                {
                    var st = GetSortState("Files");
                    if (st.Type == SortType.LoadedOnly) loadedState = 1;
                    else if (st.Type == SortType.UnloadedOnly) loadedState = 0;
                }
                catch { }

                // User tag filter params (mirrors RefreshFilesRoutine worker snapshot).
                bool utFilterByTags = _userTagAvailMode == UserTagAvailMode.FilterByTags;
                bool utUntaggedOnly = _userTagAvailMode == UserTagAvailMode.FilterUntagged;
                bool utRequireAll = utFilterByTags && UserTagFilterRequiresAllTags();
                HashSet<string> utNames = null;
                if (utFilterByTags && activeUserTags != null && activeUserTags.Count > 0)
                    utNames = new HashSet<string>(activeUserTags, StringComparer.OrdinalIgnoreCase);
                HashSet<string> utExcludeNames = null;
                if (utFilterByTags && excludedUserTags != null && excludedUserTags.Count > 0)
                    utExcludeNames = new HashSet<string>(excludedUserTags, StringComparer.OrdinalIgnoreCase);

                int session = _deferredSubPaneSessionId;
                bool memoHit = session == _clothingChipCountsSession;

                VpbLocalDatabase.ClothingChipCounts chips;
                if (memoHit)
                {
                    chips = _clothingChipCountsCached;
                }
                else
                {
                    bool hideOldVersions = false;
                    try { hideOldVersions = Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value; }
                    catch { }

                    if (!VpbLocalDatabase.TryQueryClothingChipCounts(
                        creator, loadedState, nameFilterQuery ?? GallerySearchQuery.Empty,
                        null, null, // pathExclusions/pathInclusions: both null for Clothing
                        activeTags, utNames, utUntaggedOnly, utRequireAll,
                        sourceFilterMode, hideOldVersions, out chips, utExcludeNames)) return;
                    _clothingChipCountsCached = chips;
                    _clothingChipCountsSession = session;
                }

                // Single-select: each chip shows count for that chip alone (not XOR toggle).
                // Assign both Count and FacetCount equal so the chip shows the same number active or inactive.
                clothingSubfilterCountAll       = chips.Default;
                clothingSubfilterCountReal      = chips.Real;
                clothingSubfilterCountPresets   = chips.Presets;
                clothingSubfilterCountCustom    = chips.Custom;
                clothingSubfilterCountCustomPreset = chips.CustomPreset;
                clothingSubfilterCountItems     = chips.Items;
                clothingSubfilterCountMale      = chips.Male;
                clothingSubfilterCountFemale    = chips.Female;
                clothingSubfilterCountDecals    = chips.Decals;

                clothingSubfilterFacetCountReal     = chips.Real;
                clothingSubfilterFacetCountPresets  = chips.Presets;
                clothingSubfilterFacetCountCustom   = chips.Custom;
                clothingSubfilterFacetCountCustomPreset = chips.CustomPreset;
                clothingSubfilterFacetCountItems    = chips.Items;
                clothingSubfilterFacetCountMale     = chips.Male;
                clothingSubfilterFacetCountFemale   = chips.Female;
                clothingSubfilterFacetCountDecals   = chips.Decals;
            }
            catch (Exception ex) { try { LogUtil.LogWarning("[VPB] ApplyClothingChipCountsFromSql failed: " + ex.Message); } catch { } }
        }

        private TagCountSnapshot CaptureTagCountSnapshot()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tagCounts)
                d[kv.Key] = kv.Value;
            return new TagCountSnapshot
            {
                TagCounts = d,
                AppearanceSourceCountAll = appearanceSourceCountAll,
                AppearanceSourceCountPresets = appearanceSourceCountPresets,
                AppearanceSourceCountCustom = appearanceSourceCountCustom,
                ClothingSubfilterCountAll = clothingSubfilterCountAll,
                ClothingSubfilterCountReal = clothingSubfilterCountReal,
                ClothingSubfilterCountPresets = clothingSubfilterCountPresets,
                ClothingSubfilterCountCustom = clothingSubfilterCountCustom,
                ClothingSubfilterCountCustomPreset = clothingSubfilterCountCustomPreset,
                ClothingSubfilterCountItems = clothingSubfilterCountItems,
                ClothingSubfilterCountMale = clothingSubfilterCountMale,
                ClothingSubfilterCountFemale = clothingSubfilterCountFemale,
                ClothingSubfilterCountDecals = clothingSubfilterCountDecals,
                HairSubfilterCountAll = hairSubfilterCountAll,
                HairSubfilterCountPresets = hairSubfilterCountPresets,
                HairSubfilterCountCustom = hairSubfilterCountCustom,
                HairSubfilterCountCustomPreset = hairSubfilterCountCustomPreset,
                HairSubfilterCountItems = hairSubfilterCountItems,
                HairSubfilterCountMale = hairSubfilterCountMale,
                HairSubfilterCountFemale = hairSubfilterCountFemale,
                AppearanceSubfilterCountAll = appearanceSubfilterCountAll,
                AppearanceSubfilterCountPresets = appearanceSubfilterCountPresets,
                AppearanceSubfilterCountCustom = appearanceSubfilterCountCustom,
                AppearanceSubfilterCountMale = appearanceSubfilterCountMale,
                AppearanceSubfilterCountFemale = appearanceSubfilterCountFemale,
                AppearanceSubfilterCountFuta = appearanceSubfilterCountFuta,
                AppearanceSubfilterCountUnknown = appearanceSubfilterCountUnknown,
                ClothingSubfilterFacetCountReal = clothingSubfilterFacetCountReal,
                ClothingSubfilterFacetCountPresets = clothingSubfilterFacetCountPresets,
                ClothingSubfilterFacetCountCustom = clothingSubfilterFacetCountCustom,
                ClothingSubfilterFacetCountCustomPreset = clothingSubfilterFacetCountCustomPreset,
                ClothingSubfilterFacetCountItems = clothingSubfilterFacetCountItems,
                ClothingSubfilterFacetCountMale = clothingSubfilterFacetCountMale,
                ClothingSubfilterFacetCountFemale = clothingSubfilterFacetCountFemale,
                ClothingSubfilterFacetCountDecals = clothingSubfilterFacetCountDecals,
                HairSubfilterFacetCountPresets = hairSubfilterFacetCountPresets,
                HairSubfilterFacetCountCustom = hairSubfilterFacetCountCustom,
                HairSubfilterFacetCountCustomPreset = hairSubfilterFacetCountCustomPreset,
                HairSubfilterFacetCountItems = hairSubfilterFacetCountItems,
                HairSubfilterFacetCountMale = hairSubfilterFacetCountMale,
                HairSubfilterFacetCountFemale = hairSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountPresets = appearanceSubfilterFacetCountPresets,
                AppearanceSubfilterFacetCountCustom = appearanceSubfilterFacetCountCustom,
                AppearanceSubfilterFacetCountMale = appearanceSubfilterFacetCountMale,
                AppearanceSubfilterFacetCountFemale = appearanceSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountFuta = appearanceSubfilterFacetCountFuta,
                AppearanceSubfilterFacetCountUnknown = appearanceSubfilterFacetCountUnknown,
                AppearanceSubfilterCurrentCountAll = appearanceSubfilterCurrentCountAll,
                AppearanceSubfilterCurrentCountMale = appearanceSubfilterCurrentCountMale,
                AppearanceSubfilterCurrentCountFemale = appearanceSubfilterCurrentCountFemale,
                AppearanceSubfilterCurrentCountFuta = appearanceSubfilterCurrentCountFuta,
                AppearanceSubfilterCurrentCountUnknown = appearanceSubfilterCurrentCountUnknown,
            };
        }

        private void RestoreTagCountSnapshot(TagCountSnapshot s)
        {
            if (s == null) return;
            tagCounts.Clear();
            if (s.TagCounts != null)
            {
                foreach (var kv in s.TagCounts)
                    tagCounts[kv.Key] = kv.Value;
            }
            appearanceSourceCountAll = s.AppearanceSourceCountAll;
            appearanceSourceCountPresets = s.AppearanceSourceCountPresets;
            appearanceSourceCountCustom = s.AppearanceSourceCountCustom;
            clothingSubfilterCountAll = s.ClothingSubfilterCountAll;
            clothingSubfilterCountReal = s.ClothingSubfilterCountReal;
            clothingSubfilterCountPresets = s.ClothingSubfilterCountPresets;
            clothingSubfilterCountCustom = s.ClothingSubfilterCountCustom;
            clothingSubfilterCountCustomPreset = s.ClothingSubfilterCountCustomPreset;
            clothingSubfilterCountItems = s.ClothingSubfilterCountItems;
            clothingSubfilterCountMale = s.ClothingSubfilterCountMale;
            clothingSubfilterCountFemale = s.ClothingSubfilterCountFemale;
            clothingSubfilterCountDecals = s.ClothingSubfilterCountDecals;
            hairSubfilterCountAll = s.HairSubfilterCountAll;
            hairSubfilterCountPresets = s.HairSubfilterCountPresets;
            hairSubfilterCountCustom = s.HairSubfilterCountCustom;
            hairSubfilterCountCustomPreset = s.HairSubfilterCountCustomPreset;
            hairSubfilterCountItems = s.HairSubfilterCountItems;
            hairSubfilterCountMale = s.HairSubfilterCountMale;
            hairSubfilterCountFemale = s.HairSubfilterCountFemale;
            appearanceSubfilterCountAll = s.AppearanceSubfilterCountAll;
            appearanceSubfilterCountPresets = s.AppearanceSubfilterCountPresets;
            appearanceSubfilterCountCustom = s.AppearanceSubfilterCountCustom;
            appearanceSubfilterCountMale = s.AppearanceSubfilterCountMale;
            appearanceSubfilterCountFemale = s.AppearanceSubfilterCountFemale;
            appearanceSubfilterCountFuta = s.AppearanceSubfilterCountFuta;
            appearanceSubfilterCountUnknown = s.AppearanceSubfilterCountUnknown;
            clothingSubfilterFacetCountReal = s.ClothingSubfilterFacetCountReal;
            clothingSubfilterFacetCountPresets = s.ClothingSubfilterFacetCountPresets;
            clothingSubfilterFacetCountCustom = s.ClothingSubfilterFacetCountCustom;
            clothingSubfilterFacetCountCustomPreset = s.ClothingSubfilterFacetCountCustomPreset;
            clothingSubfilterFacetCountItems = s.ClothingSubfilterFacetCountItems;
            clothingSubfilterFacetCountMale = s.ClothingSubfilterFacetCountMale;
            clothingSubfilterFacetCountFemale = s.ClothingSubfilterFacetCountFemale;
            clothingSubfilterFacetCountDecals = s.ClothingSubfilterFacetCountDecals;
            hairSubfilterFacetCountPresets = s.HairSubfilterFacetCountPresets;
            hairSubfilterFacetCountCustom = s.HairSubfilterFacetCountCustom;
            hairSubfilterFacetCountCustomPreset = s.HairSubfilterFacetCountCustomPreset;
            hairSubfilterFacetCountItems = s.HairSubfilterFacetCountItems;
            hairSubfilterFacetCountMale = s.HairSubfilterFacetCountMale;
            hairSubfilterFacetCountFemale = s.HairSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountPresets = s.AppearanceSubfilterFacetCountPresets;
            appearanceSubfilterFacetCountCustom = s.AppearanceSubfilterFacetCountCustom;
            appearanceSubfilterFacetCountMale = s.AppearanceSubfilterFacetCountMale;
            appearanceSubfilterFacetCountFemale = s.AppearanceSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountFuta = s.AppearanceSubfilterFacetCountFuta;
            appearanceSubfilterFacetCountUnknown = s.AppearanceSubfilterFacetCountUnknown;
            appearanceSubfilterCurrentCountAll = s.AppearanceSubfilterCurrentCountAll;
            appearanceSubfilterCurrentCountMale = s.AppearanceSubfilterCurrentCountMale;
            appearanceSubfilterCurrentCountFemale = s.AppearanceSubfilterCurrentCountFemale;
            appearanceSubfilterCurrentCountFuta = s.AppearanceSubfilterCurrentCountFuta;
            appearanceSubfilterCurrentCountUnknown = s.AppearanceSubfilterCurrentCountUnknown;
        }

        public void SetCategories(List<Gallery.Category> cats)
        {
            categories = cats;
            categoriesCached = false;

            // Try to restore last tab if currentPath is not yet set (e.g. freshly created panels
            // that were not yet shown via Show()). Panels that already have a category displayed
            // are left unchanged.
            string lastPageName = null;
            if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.LastGalleryCategory))
                lastPageName = VPBConfig.Instance.LastGalleryCategory;
            else if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                lastPageName = Settings.Instance.LastGalleryPage.Value;
            LogUtil.Log("[Gallery] SetCategories: currentPath='" + currentPath + "' memoryLastCat='" + (VPBConfig.Instance != null ? VPBConfig.Instance.LastGalleryCategory : "null") + "' resolvedLastPage='" + (lastPageName ?? "null") + "'");

            if (string.IsNullOrEmpty(currentPath) && !string.IsNullOrEmpty(lastPageName))
            {
                // Normalize legacy enum-style names ("CategoryHair" -> "Hair", "PresetHair" -> "Hair")
                lastPageName = lastPageName.Trim();
                if (lastPageName.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                    lastPageName = lastPageName.Substring("Category ".Length);
                else if (lastPageName.StartsWith("Category", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Category".Length)
                    lastPageName = lastPageName.Substring("Category".Length);

                if (lastPageName.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                    lastPageName = lastPageName.Substring("Preset ".Length);
                else if (lastPageName.StartsWith("Preset", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Preset".Length)
                    lastPageName = lastPageName.Substring("Preset".Length);

                lastPageName = lastPageName.Trim();
                if (string.Equals(lastPageName, "Scene", StringComparison.OrdinalIgnoreCase))
                    lastPageName = "Scenes";

                var cat = categories.FirstOrDefault(c => string.Equals(c.name, lastPageName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentPaths = cat.paths;
                    currentExtension = cat.extension;
                    currentCategoryTitle = cat.name;
                    titleText.text = cat.name;
                    activeTags.Clear();
                }
            }

            if (string.IsNullOrEmpty(currentPath) && categories.Count > 0)
            {
                // Fallback to first category
                currentPath = categories[0].path;
                currentPaths = categories[0].paths;
                currentExtension = categories[0].extension;
                currentCategoryTitle = categories[0].name;
                titleText.text = categories[0].name;
                activeTags.Clear();
            }

            LogUtil.Log("[Gallery] SetCategories resolved: currentPath='" + currentPath + "' currentCategoryTitle='" + currentCategoryTitle + "'");
            // Full UpdateTabs() runs synchronous CacheCategoryCounts/CacheCreators and can take many seconds on large libraries.
            // New panes defer that work to RefreshFilesRoutine (background cache + one UpdateTabs at the end).
            if (hasLoadedContent)
                UpdateTabs();
            else
            {
                _sideTabsNeedFullRebuildAfterFirstRefresh = true;
                UpdateTabsImpl(rebuildSideTabLists: false);
            }
            // If we have categories but no path, set title to first category
            if (categories.Count > 0 && string.IsNullOrEmpty(currentPath))
            {
                titleText.text = categories[0].name;
            }
        }

        public void PushUndo(Action action)
        {
            if (action == null) return;
            undoStack.Push(action);
            if (!isApplyingUndoRedo)
            {
                try { redoStack.Clear(); } catch { }
            }
            TrimUndoRedoStacks();
            UpdateUndoRedoButtonLabels();
        }

        private const int MaxUndoRedoHistory = 6;

        private static void TrimStackToMax<T>(ref Stack<T> stack, int max)
        {
            if (stack == null) { stack = new Stack<T>(); return; }
            if (max < 0) max = 0;
            if (stack.Count <= max) return;

            // Stack.ToArray() returns items in LIFO order (top first). Keep the most recent entries.
            T[] keptTopFirst = stack.ToArray();
            if (max < keptTopFirst.Length)
                Array.Resize(ref keptTopFirst, max);

            // Rebuild stack preserving order for the kept subset.
            stack = new Stack<T>(keptTopFirst.Reverse());
        }

        private void TrimUndoRedoStacks()
        {
            TrimStackToMax(ref undoStack, MaxUndoRedoHistory);
            TrimStackToMax(ref redoStack, MaxUndoRedoHistory);
        }

        private void UpdateUndoRedoButtonLabels()
        {
            try
            {
                string undoText = VPBTranslation.T("gallery.footer.undo_abbrev", "U") + " (" + (undoStack != null ? undoStack.Count : 0) + ")";
                string redoText = VPBTranslation.T("gallery.footer.redo_abbrev", "R") + " (" + (redoStack != null ? redoStack.Count : 0) + ")";

                if (footerUndoBtnGO != null)
                {
                    Text t = null;
                    try { t = footerUndoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = undoText;
                }

                if (footerRedoBtnGO != null)
                {
                    Text t = null;
                    try { t = footerRedoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = redoText;
                }
            }
            catch { }
        }

        private Atom GetBestUndoRedoTargetAtom()
        {
            Atom a = null;
            try { a = GetBestTargetAtom(); } catch { a = null; }
            if (a == null)
            {
                try { a = SelectedTargetAtom; } catch { a = null; }
            }
            if (a == null)
            {
                try
                {
                    if (SuperController.singleton != null)
                    {
                        var atoms = SuperController.singleton.GetAtoms();
                        if (atoms != null) a = atoms.FirstOrDefault(x => x != null && SceneUtils.IsPersonLikeAtom(x));
                    }
                }
                catch { a = null; }
            }
            return a;
        }

        private Action CaptureAtomSnapshotAction(Atom atom)
        {
            if (atom == null) return null;
            string atomUid = null;
            try { atomUid = atom.uid; } catch { atomUid = null; }
            if (string.IsNullOrEmpty(atomUid)) return null;

            ClothingLoadingUtils.ClothingHairUndoState clothingHairSnapshot =
                ClothingLoadingUtils.CaptureClothingHairUndoState(atom);
            List<JSONClass> additionalStorableSnapshots = new List<JSONClass>();

            bool ShouldSnapshotAdditionalStorableId(string sid)
            {
                if (string.IsNullOrEmpty(sid)) return false;
                if (ClothingLoadingUtils.ClothingHairUndoStateContainsStorable(clothingHairSnapshot, sid)) return false;
                if (string.Equals(sid, "geometry", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(sid, "Skin", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Presets", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.IndexOf("appearance", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sid.IndexOf("morph", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }

            try
            {
                List<string> ids = null;
                try { ids = atom.GetStorableIDs(); } catch { ids = null; }
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string sid = ids[i];
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotAdditionalStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = atom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        JSONClass snap = null;
                        try { snap = s.GetJSON(); } catch { snap = null; }
                        if (snap != null) additionalStorableSnapshots.Add(snap);
                    }
                }
            }
            catch { }

            return () =>
            {
                Atom targetAtom = null;
                try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { targetAtom = null; }
                if (targetAtom == null) return;

                try { ClothingLoadingUtils.RestoreClothingHairUndoState(targetAtom, clothingHairSnapshot); } catch { }

                try
                {
                    for (int i = 0; i < additionalStorableSnapshots.Count; i++)
                    {
                        JSONClass snap = additionalStorableSnapshots[i];
                        if (snap == null) continue;
                        string sid = null;
                        try { sid = snap["id"].Value; } catch { sid = null; }
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotAdditionalStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = targetAtom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        try { s.RestoreFromJSON(snap); } catch { }
                    }
                }
                catch { }
            };
        }

        private Action CaptureUndoRedoSnapshotAction()
        {
            Atom a = GetBestUndoRedoTargetAtom();
            if (a != null && SceneUtils.IsPersonLikeAtom(a))
            {
                Action atomSnap = CaptureAtomSnapshotAction(a);
                if (atomSnap != null) return atomSnap;
            }
            return CaptureSceneSnapshotAction();
        }

        private Action CaptureSceneSnapshotAction()
        {
            try
            {
                if (SuperController.singleton == null) return null;
                string tempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_redo_scene_" + Guid.NewGuid().ToString() + ".json");

                JSONNode sceneRoot = null;
                try
                {
                    SuperController sc = SuperController.singleton;
                    if (sc == null) return null;

                    string[] candidates = new[]
                    {
                        "GetSaveJSON",
                        "GetSaveSceneJSON",
                        "GetSceneJSON",
                        "GetJSON",
                        "GetSaveJson",
                        "GetSceneJson",
                    };

                    object TryInvoke(MethodInfo mi)
                    {
                        if (mi == null) return null;
                        ParameterInfo[] ps = null;
                        try { ps = mi.GetParameters(); }
                        catch { ps = null; }

                        Atom bestAtom = null;
                        try { bestAtom = GetBestTargetAtom(); } catch { }
                        if (bestAtom == null)
                        {
                            try { bestAtom = SelectedTargetAtom; } catch { bestAtom = null; }
                        }
                        if (bestAtom == null)
                        {
                            try
                            {
                                if (SuperController.singleton != null)
                                {
                                    var atoms = SuperController.singleton.GetAtoms();
                                    if (atoms != null) bestAtom = atoms.FirstOrDefault(a => a != null && SceneUtils.IsPersonLikeAtom(a));
                                }
                            }
                            catch { bestAtom = null; }
                        }

                        object[] args = null;
                        if (ps != null && ps.Length > 0)
                        {
                            args = new object[ps.Length];
                            for (int pi = 0; pi < ps.Length; pi++)
                            {
                                Type t = ps[pi].ParameterType;
                                bool isByRef = false;
                                try { isByRef = t != null && t.IsByRef; } catch { isByRef = false; }
                                if (isByRef)
                                {
                                    try { t = t.GetElementType(); }
                                    catch { t = ps[pi].ParameterType; }
                                }

                                if (t == typeof(bool)) args[pi] = false;
                                else if (t == typeof(int)) args[pi] = 0;
                                else if (t == typeof(float)) args[pi] = 0f;
                                else if (t == typeof(string)) args[pi] = "";
                                else if (t == typeof(JSONNode) || t == typeof(JSONClass)) args[pi] = new JSONClass();
                                else if (t == typeof(Atom)) args[pi] = bestAtom;
                                else
                                {
                                    return null;
                                }
                            }
                        }

                        try { return mi.Invoke(sc, args); }
                        catch { return null; }
                    }

                    bool TrySetSceneRootFromResult(object result)
                    {
                        if (result == null) return false;
                        try
                        {
                            if (result is JSONNode node)
                            {
                                sceneRoot = node;
                                return true;
                            }

                            string s = null;
                            try { s = result.ToString(); }
                            catch { s = null; }
                            if (string.IsNullOrEmpty(s)) return false;

                            try
                            {
                                JSONNode parsed = JSON.Parse(s);
                                if (parsed != null)
                                {
                                    sceneRoot = parsed;
                                    return true;
                                }
                            }
                            catch { }
                        }
                        catch { }
                        return false;
                    }

                    for (int i = 0; i < candidates.Length && sceneRoot == null; i++)
                    {
                        MethodInfo[] methods = null;
                        try { methods = sc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => string.Equals(m.Name, candidates[i], StringComparison.Ordinal)).ToArray(); }
                        catch { methods = null; }
                        if (methods == null || methods.Length == 0) continue;

                        for (int m = 0; m < methods.Length && sceneRoot == null; m++)
                        {
                            object result = TryInvoke(methods[m]);
                            if (TrySetSceneRootFromResult(result)) break;
                        }
                    }
                }
                catch { sceneRoot = null; }

                if (sceneRoot == null) return null;

                try
                {
                    File.WriteAllText(tempPath, JsonSerializationUtil.Serialize(sceneRoot, 100_000));
                }
                catch
                {
                    return null;
                }

                string loadPath = null;
                try { loadPath = UI.NormalizePath(tempPath); }
                catch { loadPath = tempPath; }

                return () =>
                {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(tempPath)) return;
                        SceneLoadingUtils.LoadScene(loadPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        public void RefreshTargetDropdown()
        {
            string currentSelectionUid = null;
            if (targetDropdownValue >= 0 && targetDropdownValue < personAtoms.Count)
            {
                Atom cur = personAtoms[targetDropdownValue];
                if (cur != null) try { currentSelectionUid = cur.uid; } catch { }
            }

            personAtoms.Clear();
            targetDropdownOptions.Clear();

            bool subSceneMode = IsSubSceneTargetMode();
            if (SuperController.singleton != null)
            {
                List<Atom> allAtoms = null;
                try { allAtoms = SuperController.singleton.GetAtoms(); } catch { }
                if (allAtoms != null)
                {
                    foreach (Atom a in allAtoms)
                    {
                        if (a == null) continue;
                        try
                        {
                            bool include = subSceneMode ? SceneUtils.IsSubSceneAtom(a) : SceneUtils.IsPersonLikeAtom(a);
                            if (include)
                            {
                                string uid = a.uid;
                                if (uid != null)
                                {
                                    personAtoms.Add(a);
                                    targetDropdownOptions.Add(uid);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            if (targetDropdownOptions.Count == 0)
            {
                targetDropdownOptions.Add("None");
                personAtoms.Add(null);
            }

            // Restore previous selection by UID, or default to first
            if (currentSelectionUid != null)
            {
                int idx = -1;
                for (int i = 0; i < personAtoms.Count; i++)
                {
                    Atom a = personAtoms[i];
                    if (a == null) continue;
                    try { if (a.uid == currentSelectionUid) { idx = i; break; } } catch { }
                }
                targetDropdownValue = idx >= 0 ? idx : 0;
            }
            else
            {
                targetDropdownValue = 0;
            }

            UpdateTargetDropdownUI();

            // Keep toolbox person-atom buttons in sync whenever the target list changes (same timing as category Show()).
            try { RefreshTboxPersonAtomButtonsAfterSceneLoad(); } catch { }
        }

        /// <summary>Called after scene load/merge (deferred) and when the Target side tab may need rebuilt buttons.</summary>
        public static void NotifyAllPanelsSceneTargetsChanged()
        {
            try
            {
                if (Gallery.singleton == null) return;
                var panels = Gallery.singleton.Panels;
                if (panels == null) return;
                for (int i = 0; i < panels.Count; i++)
                {
                    GalleryPanel p = panels[i];
                    if (p == null) continue;
                    try { p.SyncTargetListWithScene(); } catch { }
                }
            }
            catch { }
        }

        public void SyncTargetListWithScene()
        {
            RefreshTargetDropdown();
            try
            {
                if (leftActiveContent == ContentType.Target || rightActiveContent == ContentType.Target)
                    UpdateTabs();
            }
            catch { }
        }

        public void CycleTarget(bool forward)
        {
            bool wasShowingNone = personAtoms.Count == 1 && personAtoms[0] == null;
            RefreshTargetDropdown();

            bool hasRealPersons = personAtoms.Count > 0 && personAtoms[0] != null;

            // First click when stale "None" was displayed: just reveal the first person, don't cycle past it
            if (wasShowingNone && hasRealPersons)
                return;

            // Nothing to cycle if still None-only
            if (!hasRealPersons)
                return;

            int prev = targetDropdownValue;
            if (forward)
                targetDropdownValue = (targetDropdownValue + 1) % targetDropdownOptions.Count;
            else
                targetDropdownValue = (targetDropdownValue - 1 + targetDropdownOptions.Count) % targetDropdownOptions.Count;
            UpdateTargetDropdownUI();
            if (prev != targetDropdownValue) OnTargetAtomChanged("cycle");
        }

        /// <summary>
        /// Syncs visible target labels when text/icon target picker controls exist on the side rail.
        /// This branch does not declare those controls in GalleryPanel.Fields — keep a no-op so RefreshTargetDropdown / Init compile.
        /// </summary>
        private void UpdateTargetDropdownUI()
        {
            try
            {
                // Toolbox dropup label sync
                if (tboxTargetDropdownBtnText != null)
                {
                    string label = "Target";
                    try
                    {
                        int i = targetDropdownValue;
                        Atom a = (personAtoms != null && i >= 0 && i < personAtoms.Count) ? personAtoms[i] : null;
                        string uid = (targetDropdownOptions != null && i >= 0 && i < targetDropdownOptions.Count) ? targetDropdownOptions[i] : null;
                        if (a != null)
                            label = GetTargetAtomDisplayLabel(a, uid ?? "Unknown");
                    }
                    catch { }
                    tboxTargetDropdownBtnText.text = label + "  ▲";
                }

                // If menu open, rebuild checkmarks.
                if (tboxTargetMenuOpen)
                {
                    try { RebuildTboxTargetMenuOptions(); } catch { }
                }
            }
            catch { }
        }

        private void Undo()
        {
            if (undoStack.Count > 0)
            {
                Action action = undoStack.Pop();
                try
                {
                    Action redoAction = CaptureUndoRedoSnapshotAction();
                    if (redoAction != null) redoStack.Push(redoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Undo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                TrimUndoRedoStacks();
                UpdateUndoRedoButtonLabels();
                try
                {
                    // Ensure context submenus refresh immediately after Undo restores items.
                    Atom tgt = null;
                    try { tgt = GetBestTargetAtom(); } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Undo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private void Redo()
        {
            if (redoStack.Count > 0)
            {
                Action action = redoStack.Pop();
                try
                {
                    Action undoAction = CaptureUndoRedoSnapshotAction();
                    if (undoAction != null) undoStack.Push(undoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Redo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                TrimUndoRedoStacks();
                UpdateUndoRedoButtonLabels();
                try
                {
                    Atom tgt = null;
                    try { tgt = GetBestTargetAtom(); } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Redo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private bool IsMatch(FileEntry entry, List<string> paths, string singlePath, string[] extensions)
        {
            if (entry == null) return false;

            string checkPath = entry.Path;
            if (entry is VarFileEntry vfe)
            {
                checkPath = vfe.InternalPath;
            }
            
            // Extension Filter
            bool extMatch = false;
            if (extensions == null || extensions.Length == 0 || (extensions.Length == 1 && string.IsNullOrEmpty(extensions[0])))
            {
                extMatch = true;
            }
            else
            {
                string entryExt = Path.GetExtension(checkPath);
                if (!string.IsNullOrEmpty(entryExt))
                {
                    entryExt = entryExt.Substring(1); // remove dot
                    foreach (var ext in extensions)
                    {
                        if (string.Equals(entryExt, ext, StringComparison.OrdinalIgnoreCase))
                        {
                            extMatch = true;
                            break;
                        }
                    }
                }
            }
            if (!extMatch) return false;

            // Path Filter
            if (paths != null && paths.Count > 0)
            {
                foreach (var p in paths)
                {
                    if (checkPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) 
                    {
                        // Special Case: "Saves/Person" is often used for Poses, but "Saves/Person/appearance" are Appearances.
                        // If we are looking for Poses (Saves/Person) and found an appearance, skip it unless specifically requested.
                        if (string.Equals(p, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                        {
                            if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        return true;
                    }
                }
                return false;
            }
            
            if (!string.IsNullOrEmpty(singlePath))
            {
                if (checkPath.StartsWith(singlePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(singlePath, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(singlePath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                }
                return false;
            }

            return true;
        }

        private void ClearCurrentFilter(bool isRight)
        {
            ContentType? type = isRight ? rightActiveContent : leftActiveContent;
            
            if (!type.HasValue) return;

            // Simply close the panel (toggle off)
            if (isRight) ToggleRight(type.Value);
            else ToggleLeft(type.Value);
            
            // Optionally clear filters if desired, but "X" on a side tab usually implies "Close this tab"
            // If the user meant "Clear Filter" specifically for search text, that's inside the panel.
            // "the X button should be on the outside of the side buttons... side buttons that are being hidden"
            // This strongly suggests a close button for the side panel overlay.
            
            UpdateTabs();
        }
    }
}
