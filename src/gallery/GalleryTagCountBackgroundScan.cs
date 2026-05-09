using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace VPB
{
    /// <summary>
    /// Immutable inputs for a full tag/facet scan (built on the main thread).
    /// </summary>
    internal sealed class TagCountParallelInputs
    {
        public string Title;
        public string CurrentPath;
        public List<string> CurrentPathsCopy;
        public string CurrentCreator;
        public HashSet<string> ActiveTagsCopy;
        public GalleryPanel.ClothingSubfilter ClothingSubfilterVal;
        public GalleryPanel.HairSubfilter HairSubfilterVal;
        public GalleryPanel.AppearanceSubfilter AppearanceSubfilterVal;
        public HashSet<string> TagsToCount;
        public HashSet<string> SingleWordTags;
        public List<string> MultiWordTags;
        public string[] ExtensionsSplit;
        public bool IsClothingTitle;
        public bool IsHairTitle;
        public bool IsAppearanceTitle;
        public bool HasAnyTagsToCount;
    }

    /// <summary>Worker signals completion; main thread waits on <see cref="Finished"/>.</summary>
    internal sealed class TagParallelWaiter
    {
        public volatile bool Finished;
        public TagCountParallelOutcome Outcome;
    }

    internal sealed class TagCountParallelOutcome
    {
        public bool Aborted;
        public TagCountSnapshot Snapshot;
    }

    /// <summary>
    /// Runs the same tag/facet counting work as <see cref="GalleryPanel.CoCacheTagCountsInternal"/> on a ThreadPool thread,
    /// overlapping the file-list refresh. Only reads <see cref="GalleryPanel.GalleryFileRefreshSequence"/> from the panel.
    /// </summary>
    internal static class GalleryTagCountBackgroundScan
    {
        private static readonly char[] Separators = new char[] { '/', '\\', '.', '_', '-', ' ' };
        private static readonly char[] TokenSeps = new char[] { '/', '\\', '.', '_', '-', ' ', '(', ')', '[', ']', '{', '}', ',', ';', ':' };

        /// <summary>Mutable VAR-row scan totals (package loop or SQLite <c>cat_mem</c>).</summary>
        internal sealed class TagScanTotals
        {
            public int AppearanceSourceCountAll;
            public int AppearanceSourceCountPresets;
            public int AppearanceSourceCountCustom;
            public int ClothingSubfilterCountAll;
            public int ClothingSubfilterCountReal;
            public int ClothingSubfilterCountPresets;
            public int ClothingSubfilterCountCustom;
            public int ClothingSubfilterCountItems;
            public int ClothingSubfilterCountMale;
            public int ClothingSubfilterCountFemale;
            public int ClothingSubfilterCountDecals;
            public int HairSubfilterCountAll;
            public int HairSubfilterCountPresets;
            public int HairSubfilterCountCustom;
            public int HairSubfilterCountItems;
            public int HairSubfilterCountMale;
            public int HairSubfilterCountFemale;
            public int AppearanceSubfilterCountAll;
            public int AppearanceSubfilterCountPresets;
            public int AppearanceSubfilterCountCustom;
            public int AppearanceSubfilterCountMale;
            public int AppearanceSubfilterCountFemale;
            public int AppearanceSubfilterCountFuta;
            public int ClothingSubfilterFacetCountReal;
            public int ClothingSubfilterFacetCountPresets;
            public int ClothingSubfilterFacetCountCustom;
            public int ClothingSubfilterFacetCountItems;
            public int ClothingSubfilterFacetCountMale;
            public int ClothingSubfilterFacetCountFemale;
            public int ClothingSubfilterFacetCountDecals;
            public int HairSubfilterFacetCountPresets;
            public int HairSubfilterFacetCountCustom;
            public int HairSubfilterFacetCountItems;
            public int HairSubfilterFacetCountMale;
            public int HairSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountPresets;
            public int AppearanceSubfilterFacetCountCustom;
            public int AppearanceSubfilterFacetCountMale;
            public int AppearanceSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountFuta;
            public int AppearanceSubfilterCurrentCountAll;
            public int AppearanceSubfilterCurrentCountMale;
            public int AppearanceSubfilterCurrentCountFemale;
            public int AppearanceSubfilterCurrentCountFuta;
        }

        internal static string JoinExtensionsForTagScan(string[] split)
        {
            if (split == null || split.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < split.Length; i++)
            {
                string e = split[i] != null ? split[i].Trim() : "";
                if (e.Length == 0) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(e);
            }
            return sb.ToString();
        }

        /// <summary>One VAR internal_path row: extension + path rules + clothing/appearance facets + path/user tags.</summary>
        internal static void TagScanProcessOneVarRow(
            TagCountParallelInputs input,
            string internalPath,
            string pkgUid,
            HashSet<string> targetExts,
            Dictionary<string, int> tagCounts,
            HashSet<string> foundTags,
            TagScanTotals t)
        {
            if (string.IsNullOrEmpty(internalPath)) return;

            int lastDot = internalPath.LastIndexOf('.');
            if (lastDot < 0 || lastDot == internalPath.Length - 1) return;
            string ext = internalPath.Substring(lastDot + 1);
            if (Gallery.IsEverythingCategoryName(input.Title) && Gallery.IsEverythingExcludedPreviewExtension(ext)) return;
            if (!Gallery.IsEverythingCategoryName(input.Title) && !targetExts.Contains(ext)) return;

            if (!GalleryPanel.RefreshWorkerPathMatches(internalPath, input.CurrentPathsCopy, input.CurrentPath)) return;

            GalleryPanel.ClothingSubfilter clothingSubfilter = input.ClothingSubfilterVal;
            GalleryPanel.AppearanceSubfilter appearanceSubfilter = input.AppearanceSubfilterVal;
            GalleryPanel.HairSubfilter hairSubfilter = input.HairSubfilterVal;

            if (input.IsClothingTitle)
            {
                ClothingLoadingUtils.ResourceKind ck;
                ClothingLoadingUtils.ResourceGender cg;
                ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
                bool isClothingEntry = (ck == ClothingLoadingUtils.ResourceKind.Clothing);
                if (isClothingEntry)
                {
                    bool isPresetEntry = ext.Equals("vap", StringComparison.OrdinalIgnoreCase);
                    bool isCustomPreset = false;
                    bool isDecal = ClothingLoadingUtils.IsDecalLikePath(internalPath);
                    GalleryPanel.ClothingSubfilter cur = clothingSubfilter;

                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.RealClothing, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountReal++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Presets, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountPresets++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Custom, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountCustom++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Items, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountItems++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Male, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountMale++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Female, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountFemale++;
                    if (PassesClothingSubfilters(cur ^ GalleryPanel.ClothingSubfilter.Decals, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountDecals++;

                    t.ClothingSubfilterCountAll++;

                    if (isDecal)
                    {
                        t.ClothingSubfilterCountDecals++;
                        if (clothingSubfilter != 0)
                        {
                            bool wantsRealType = ((clothingSubfilter & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Presets | GalleryPanel.ClothingSubfilter.Items | GalleryPanel.ClothingSubfilter.Male | GalleryPanel.ClothingSubfilter.Female)) != 0);
                            bool wantsDecalType = ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Decals) != 0);
                            bool typeExplicit = ((clothingSubfilter & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Decals)) != 0);
                            if (typeExplicit)
                            {
                                if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Decals) == 0) return;
                            }
                            else
                            {
                                if (wantsRealType && !wantsDecalType) return;
                            }
                            if ((clothingSubfilter & (GalleryPanel.ClothingSubfilter.Presets | GalleryPanel.ClothingSubfilter.Items | GalleryPanel.ClothingSubfilter.Male | GalleryPanel.ClothingSubfilter.Female)) != 0) return;
                        }
                    }
                    else
                    {
                        t.ClothingSubfilterCountReal++;
                        if (isPresetEntry) t.ClothingSubfilterCountPresets++;
                        if (isCustomPreset) t.ClothingSubfilterCountCustom++;
                        if (!isPresetEntry) t.ClothingSubfilterCountItems++;
                        if (cg == ClothingLoadingUtils.ResourceGender.Male) t.ClothingSubfilterCountMale++;
                        else if (cg == ClothingLoadingUtils.ResourceGender.Female) t.ClothingSubfilterCountFemale++;

                        if (clothingSubfilter != 0)
                        {
                            bool typeExplicit = ((clothingSubfilter & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Decals)) != 0);
                            if (typeExplicit)
                            {
                                if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.RealClothing) == 0) return;
                            }
                            if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Presets) != 0) { if (!isPresetEntry) return; }
                            if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Custom) != 0) { if (!isCustomPreset) return; }
                            if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Items) != 0) { if (isPresetEntry) return; }
                            if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return; }
                            if ((clothingSubfilter & GalleryPanel.ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return; }
                        }
                    }
                }
                else return;
            }

            if (input.IsHairTitle)
            {
                ClothingLoadingUtils.ResourceKind ck;
                ClothingLoadingUtils.ResourceGender cg;
                ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
                if (ck != ClothingLoadingUtils.ResourceKind.Hair) return;

                bool isPresetEntry = ext.Equals("vap", StringComparison.OrdinalIgnoreCase);
                bool isCustomPreset = false; // VAR rows are never "Custom" (loose files only)

                bool PassesHairSubfilters(GalleryPanel.HairSubfilter f)
                {
                    if (f == 0) return !isPresetEntry;
                    bool wantsPresets = (f & GalleryPanel.HairSubfilter.Presets) != 0;
                    bool wantsCustom = (f & GalleryPanel.HairSubfilter.Custom) != 0;
                    if (wantsPresets) { if (!isPresetEntry) return false; }
                    if (wantsCustom) { if (!isCustomPreset) return false; }
                    if (!wantsPresets && !wantsCustom) { if (isPresetEntry) return false; }
                    if ((f & GalleryPanel.HairSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                    if ((f & GalleryPanel.HairSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                    if ((f & GalleryPanel.HairSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                    return true;
                }

                // Facet counts: how many would be shown if user toggled flag now.
                if (PassesHairSubfilters(hairSubfilter ^ GalleryPanel.HairSubfilter.Presets)) t.HairSubfilterFacetCountPresets++;
                if (PassesHairSubfilters(hairSubfilter ^ GalleryPanel.HairSubfilter.Custom)) t.HairSubfilterFacetCountCustom++;
                if (PassesHairSubfilters(hairSubfilter ^ GalleryPanel.HairSubfilter.Items)) t.HairSubfilterFacetCountItems++;
                if (PassesHairSubfilters(hairSubfilter ^ GalleryPanel.HairSubfilter.Male)) t.HairSubfilterFacetCountMale++;
                if (PassesHairSubfilters(hairSubfilter ^ GalleryPanel.HairSubfilter.Female)) t.HairSubfilterFacetCountFemale++;

                t.HairSubfilterCountAll++;
                if (isPresetEntry) t.HairSubfilterCountPresets++;
                if (isCustomPreset) t.HairSubfilterCountCustom++;
                if (!isPresetEntry) t.HairSubfilterCountItems++;
                if (cg == ClothingLoadingUtils.ResourceGender.Male) t.HairSubfilterCountMale++;
                else if (cg == ClothingLoadingUtils.ResourceGender.Female) t.HairSubfilterCountFemale++;

                // Apply active subfilters (if any) to tag counting.
                if (hairSubfilter != 0)
                {
                    if (!PassesHairSubfilters(hairSubfilter)) return;
                }
                else
                {
                    // Default-hide presets when no subfilter active (match UI behavior).
                    if (isPresetEntry) return;
                }
            }

            if (input.IsAppearanceTitle)
            {
                string p = internalPath.Replace('\\', '/');
                bool isAppearance = p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isAppearance) return;

                bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                string fileName = internalPath;
                int ls = internalPath.LastIndexOfAny(new char[] { '/', '\\' });
                if (ls >= 0 && ls + 1 < internalPath.Length) fileName = internalPath.Substring(ls + 1);
                string uid = (pkgUid ?? "") + ":/" + internalPath;
                HashSet<string> utags = TagsManager.Instance.GetTags(uid);
                int g = HeuristicAppearanceGender(internalPath, fileName, pkgUid ?? "", utags);

                t.AppearanceSubfilterCountAll++;
                if (isPresetAppearance) t.AppearanceSubfilterCountPresets++;
                if (isCustomAppearance) t.AppearanceSubfilterCountCustom++;
                if (g == 2) t.AppearanceSubfilterCountMale++;
                if (g == 1) t.AppearanceSubfilterCountFemale++;
                if (g == 3) t.AppearanceSubfilterCountFuta++;

                if (PassesAppearanceSubfilters(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Presets, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) t.AppearanceSubfilterFacetCountPresets++;
                if (PassesAppearanceSubfilters(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Custom, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) t.AppearanceSubfilterFacetCountCustom++;
                if (PassesAppearanceSubfilters(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Male, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) t.AppearanceSubfilterFacetCountMale++;
                if (PassesAppearanceSubfilters(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Female, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) t.AppearanceSubfilterFacetCountFemale++;
                if (PassesAppearanceSubfilters(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Futa, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) t.AppearanceSubfilterFacetCountFuta++;

                if (PassesAppearanceSubfilters(appearanceSubfilter, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g))
                {
                    t.AppearanceSubfilterCurrentCountAll++;
                    if (g == 2) t.AppearanceSubfilterCurrentCountMale++;
                    if (g == 1) t.AppearanceSubfilterCurrentCountFemale++;
                    if (g == 3) t.AppearanceSubfilterCurrentCountFuta++;
                }

                if (appearanceSubfilter != 0)
                {
                    if (!PassesAppearanceSubfilters(appearanceSubfilter, appearanceSubfilter, isPresetAppearance, isCustomAppearance, g)) return;
                }
            }

            if (input.IsAppearanceTitle)
            {
                if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase))
                {
                    if (internalPath.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                    {
                        t.AppearanceSourceCountPresets++;
                        t.AppearanceSourceCountAll++;
                    }
                }
            }

            if (input.HasAnyTagsToCount)
            {
                HashSet<string> singleWordTags = input.SingleWordTags;
                List<string> multiWordTags = input.MultiWordTags;
                HashSet<string> tagsToCount = input.TagsToCount;
                string[] tokens = internalPath.Split(Separators);
                foundTags.Clear();
                for (int k = 0; k < tokens.Length; k++)
                {
                    if (singleWordTags.Contains(tokens[k]))
                        foundTags.Add(tokens[k].ToLowerInvariant());
                }
                for (int k = 0; k < multiWordTags.Count; k++)
                {
                    if (internalPath.IndexOf(multiWordTags[k], StringComparison.OrdinalIgnoreCase) >= 0)
                        foundTags.Add(multiWordTags[k]);
                }
                string uid2 = (pkgUid ?? "") + ":/" + internalPath;
                HashSet<string> uTags2 = TagsManager.Instance.GetTags(uid2);
                foreach (string ut in uTags2)
                {
                    if (tagsToCount.Contains(ut)) foundTags.Add(ut);
                }
                foreach (string tag in foundTags)
                {
                    int cur;
                    tagCounts.TryGetValue(tag, out cur);
                    tagCounts[tag] = cur + 1;
                }
            }
        }

        /// <summary>Matches <see cref="GalleryPanel.Fields"/> AppearanceGender order: Unknown=0, Female=1, Male=2, Futa=3.</summary>
        private static int HeuristicAppearanceGender(string internalPath, string fileName, string pkgUid, HashSet<string> userTags)
        {
            if (userTags != null && userTags.Count > 0)
            {
                foreach (string t in userTags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    string tl = t.Trim().ToLowerInvariant();
                    if (tl == "futa" || tl == "herm" || tl == "shemale" || tl == "dickgirl") return 3;
                }
                foreach (string t in userTags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    string tl = t.Trim().ToLowerInvariant();
                    if (tl == "female" || tl == "woman" || tl == "girl") return 1;
                    if (tl == "male" || tl == "man" || tl == "boy") return 2;
                }
            }

            string s = (internalPath ?? "").Replace('\\', '/');
            string combined = (s + " " + (fileName ?? "") + " " + (pkgUid ?? "")).ToLowerInvariant();
            string[] tokens = combined.Split(TokenSeps, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok == "futa" || tok == "herm" || tok == "shemale" || tok == "dickgirl") return 3;
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok == "female" || tok == "woman" || tok == "girl") return 1;
                if (tok == "male" || tok == "man" || tok == "boy") return 2;
            }
            return 0;
        }

        internal static void QueueParallelScan(GalleryPanel panel, int refreshSequence, TagCountParallelInputs inputs, TagParallelWaiter waiter)
        {
            if (panel == null || inputs == null || waiter == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    waiter.Outcome = Run(panel, refreshSequence, inputs);
                }
                catch
                {
                    waiter.Outcome = new TagCountParallelOutcome { Aborted = true };
                }
                finally
                {
                    waiter.Finished = true;
                }
            });
        }

        internal static TagCountParallelOutcome Run(GalleryPanel panel, int refreshSequence, TagCountParallelInputs input)
        {
            var outcome = new TagCountParallelOutcome();
            if (FileManager.PackagesByUid == null)
            {
                outcome.Aborted = true;
                return outcome;
            }

            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var foundTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var t = new TagScanTotals();

            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (input.ExtensionsSplit != null)
            {
                for (int ei = 0; ei < input.ExtensionsSplit.Length; ei++)
                {
                    string ex = input.ExtensionsSplit[ei];
                    if (!string.IsNullOrEmpty(ex)) targetExts.Add(ex.Trim());
                }
            }

            GalleryPanel.ClothingSubfilter clothingSubfilter = input.ClothingSubfilterVal;
            GalleryPanel.HairSubfilter hairSubfilter = input.HairSubfilterVal;
            bool isClothingTitle = input.IsClothingTitle;
            bool isHairTitle = input.IsHairTitle;
            bool isAppearanceTitle = input.IsAppearanceTitle;

            bool varScanFromSql = false;
            if (VpbSqlite3.IsAvailable)
            {
                string extKey = JoinExtensionsForTagScan(input.ExtensionsSplit);
                VpbLocalDatabase.TagScanTotals sqlFacets;
                if (VpbLocalDatabase.TryReadTagCounts(input.Title, extKey, input.CurrentCreator ?? "", input.TagsToCount, tagCounts, out sqlFacets, input.ClothingSubfilterVal, input.HairSubfilterVal, input.AppearanceSubfilterVal, input.ActiveTagsCopy))
                {
                    varScanFromSql = true;
                    // Map sqlFacets back to our local totals object
                    t.AppearanceSourceCountAll = sqlFacets.AppearanceSourceCountAll;
                    t.AppearanceSourceCountPresets = sqlFacets.AppearanceSourceCountPresets;
                    t.AppearanceSourceCountCustom = sqlFacets.AppearanceSourceCountCustom;
                    t.ClothingSubfilterCountAll = sqlFacets.ClothingSubfilterCountAll;
                    t.ClothingSubfilterCountReal = sqlFacets.ClothingSubfilterCountReal;
                    t.ClothingSubfilterCountPresets = sqlFacets.ClothingSubfilterCountPresets;
                    t.ClothingSubfilterCountCustom = sqlFacets.ClothingSubfilterCountCustom;
                    t.ClothingSubfilterCountItems = sqlFacets.ClothingSubfilterCountItems;
                    t.ClothingSubfilterCountMale = sqlFacets.ClothingSubfilterCountMale;
                    t.ClothingSubfilterCountFemale = sqlFacets.ClothingSubfilterCountFemale;
                    t.ClothingSubfilterCountDecals = sqlFacets.ClothingSubfilterCountDecals;
                    t.HairSubfilterCountAll = sqlFacets.HairSubfilterCountAll;
                    t.HairSubfilterCountPresets = sqlFacets.HairSubfilterCountPresets;
                    t.HairSubfilterCountCustom = sqlFacets.HairSubfilterCountCustom;
                    t.HairSubfilterCountItems = sqlFacets.HairSubfilterCountItems;
                    t.HairSubfilterCountMale = sqlFacets.HairSubfilterCountMale;
                    t.HairSubfilterCountFemale = sqlFacets.HairSubfilterCountFemale;
                    t.AppearanceSubfilterCountAll = sqlFacets.AppearanceSubfilterCountAll;
                    t.AppearanceSubfilterCountPresets = sqlFacets.AppearanceSubfilterCountPresets;
                    t.AppearanceSubfilterCountCustom = sqlFacets.AppearanceSubfilterCountCustom;
                    t.AppearanceSubfilterCountMale = sqlFacets.AppearanceSubfilterCountMale;
                    t.AppearanceSubfilterCountFemale = sqlFacets.AppearanceSubfilterCountFemale;
                    t.AppearanceSubfilterCountFuta = sqlFacets.AppearanceSubfilterCountFuta;
                    t.ClothingSubfilterFacetCountReal = sqlFacets.ClothingSubfilterFacetCountReal;
                    t.ClothingSubfilterFacetCountPresets = sqlFacets.ClothingSubfilterFacetCountPresets;
                    t.ClothingSubfilterFacetCountCustom = sqlFacets.ClothingSubfilterFacetCountCustom;
                    t.ClothingSubfilterFacetCountItems = sqlFacets.ClothingSubfilterFacetCountItems;
                    t.ClothingSubfilterFacetCountMale = sqlFacets.ClothingSubfilterFacetCountMale;
                    t.ClothingSubfilterFacetCountFemale = sqlFacets.ClothingSubfilterFacetCountFemale;
                    t.ClothingSubfilterFacetCountDecals = sqlFacets.ClothingSubfilterFacetCountDecals;
                    t.HairSubfilterFacetCountPresets = sqlFacets.HairSubfilterFacetCountPresets;
                    t.HairSubfilterFacetCountCustom = sqlFacets.HairSubfilterFacetCountCustom;
                    t.HairSubfilterFacetCountItems = sqlFacets.HairSubfilterFacetCountItems;
                    t.HairSubfilterFacetCountMale = sqlFacets.HairSubfilterFacetCountMale;
                    t.HairSubfilterFacetCountFemale = sqlFacets.HairSubfilterFacetCountFemale;
                    t.AppearanceSubfilterFacetCountPresets = sqlFacets.AppearanceSubfilterFacetCountPresets;
                    t.AppearanceSubfilterFacetCountCustom = sqlFacets.AppearanceSubfilterFacetCountCustom;
                    t.AppearanceSubfilterFacetCountMale = sqlFacets.AppearanceSubfilterFacetCountMale;
                    t.AppearanceSubfilterFacetCountFemale = sqlFacets.AppearanceSubfilterFacetCountFemale;
                    t.AppearanceSubfilterFacetCountFuta = sqlFacets.AppearanceSubfilterFacetCountFuta;
                    t.AppearanceSubfilterCurrentCountAll = sqlFacets.AppearanceSubfilterCurrentCountAll;
                    t.AppearanceSubfilterCurrentCountMale = sqlFacets.AppearanceSubfilterCurrentCountMale;
                    t.AppearanceSubfilterCurrentCountFemale = sqlFacets.AppearanceSubfilterCurrentCountFemale;
                    t.AppearanceSubfilterCurrentCountFuta = sqlFacets.AppearanceSubfilterCurrentCountFuta;
                }
            }

            if (!varScanFromSql)
            {
            int pkgIter = 0;
            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if ((pkgIter++ & 0x3F) == 0 && panel.GalleryFileRefreshSequence != refreshSequence)
                {
                    outcome.Aborted = true;
                    return outcome;
                }

                if (pkg == null) continue;
                if (!string.IsNullOrEmpty(input.CurrentCreator))
                {
                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != input.CurrentCreator) continue;
                }

                List<string> names;
                List<long> ticks;
                List<long> sizes;
                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) continue;

                for (int i = 0; i < names.Count; i++)
                {
                    TagScanProcessOneVarRow(input, names[i], pkg.Uid ?? "", targetExts, tagCounts, foundTags, t);
                }
            }
            }

            if (isClothingTitle && string.IsNullOrEmpty(input.CurrentCreator))
            {
                List<string> pathsToSearch = new List<string>();
                if (input.CurrentPathsCopy != null && input.CurrentPathsCopy.Count > 0) pathsToSearch.AddRange(input.CurrentPathsCopy);
                else if (!string.IsNullOrEmpty(input.CurrentPath) && Directory.Exists(input.CurrentPath)) pathsToSearch.Add(input.CurrentPath);

                // Prefer SQLite-cached loose-file listings to avoid recursive filesystem enumeration on every scan.
                string sysCacheKey = null;
                string sysCacheSig = null;
                List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                bool sysCacheHit = false;
                try
                {
                    var sbKey = new StringBuilder(256);
                    sbKey.Append("tag:clothing|");
                    sbKey.Append("title=").Append(input.Title ?? "").Append('|');
                    sbKey.Append("ext=");
                    if (input.ExtensionsSplit != null && input.ExtensionsSplit.Length > 0)
                    {
                        var ex = new List<string>(input.ExtensionsSplit);
                        ex.Sort(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < ex.Count; i++)
                        {
                            if (i != 0) sbKey.Append(',');
                            sbKey.Append((ex[i] ?? "").Trim());
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
                        string sp = p2[i];
                        long tt = 0;
                        try { if (Directory.Exists(sp)) tt = Directory.GetLastWriteTimeUtc(sp).ToBinary(); } catch { tt = 0; }
                        if (i != 0) sbSig.Append('|');
                        sbSig.Append(tt.ToString());
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
                    for (int ei = 0; ei < input.ExtensionsSplit.Length; ei++)
                    {
                        string ext = input.ExtensionsSplit[ei];
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
                            if ((fi & 0x3F) == 0 && panel.GalleryFileRefreshSequence != refreshSequence)
                            {
                                outcome.Aborted = true;
                                return outcome;
                            }
                            string sysPath = sysFileList[fi] ?? "";
                            string norm = sysPath.Replace('\\', '/');
                            bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                            bool isCustomPreset =
                                (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

                            ClothingLoadingUtils.ResourceKind ck;
                            ClothingLoadingUtils.ResourceGender cg;
                            ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                            if (ck != ClothingLoadingUtils.ResourceKind.Clothing) continue;
                            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(sysPath);
                            GalleryPanel.ClothingSubfilter cur = clothingSubfilter;

                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.RealClothing, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountReal++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Presets, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountPresets++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Custom, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountCustom++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Items, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountItems++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Male, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountMale++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Female, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountFemale++;
                            if (PassesClothingSubfiltersLoose(cur ^ GalleryPanel.ClothingSubfilter.Decals, isDecal, isPresetEntry, isCustomPreset, cg)) t.ClothingSubfilterFacetCountDecals++;

                            t.ClothingSubfilterCountAll++;
                            if (isDecal) t.ClothingSubfilterCountDecals++;
                            else
                            {
                                t.ClothingSubfilterCountReal++;
                                if (isPresetEntry) t.ClothingSubfilterCountPresets++;
                                if (isCustomPreset) t.ClothingSubfilterCountCustom++;
                                if (!isPresetEntry) t.ClothingSubfilterCountItems++;
                                if (cg == ClothingLoadingUtils.ResourceGender.Male) t.ClothingSubfilterCountMale++;
                                else if (cg == ClothingLoadingUtils.ResourceGender.Female) t.ClothingSubfilterCountFemale++;
                            }
                        }
                    }

                // Populate cache after a full enumeration (do it once per scan; key includes all paths/exts).
                if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                {
                    try
                    {
                        var allRows = new List<VpbLocalDatabase.SystemFileRow>(512);
                        for (int wpi = 0; wpi < pathsToSearch.Count; wpi++)
                        {
                            string writeSearchPath = pathsToSearch[wpi];
                            if (string.IsNullOrEmpty(writeSearchPath) || !Directory.Exists(writeSearchPath)) continue;
                            for (int ei = 0; ei < input.ExtensionsSplit.Length; ei++)
                            {
                                string ext = input.ExtensionsSplit[ei];
                                if (string.IsNullOrEmpty(ext)) continue;
                                var list = new List<string>();
                                try { FileManager.SafeGetFiles(writeSearchPath, "*." + ext, list); }
                                catch { continue; }
                                for (int i = 0; i < list.Count; i++)
                                {
                                    string p = list[i] ?? "";
                                    if (p.Length == 0) continue;
                                    var r = new VpbLocalDatabase.SystemFileRow();
                                    r.Path = p;
                                    r.LastWriteBinaryOrInvalid = long.MinValue;
                                    r.SizeOrInvalid = long.MinValue;
                                    allRows.Add(r);
                                }
                            }
                        }
                        if (allRows.Count > 0)
                            VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, allRows);
                    }
                    catch { }
                }
                }
            }

            if (isHairTitle && string.IsNullOrEmpty(input.CurrentCreator))
            {
                List<string> pathsToSearch = new List<string>();
                if (input.CurrentPathsCopy != null && input.CurrentPathsCopy.Count > 0) pathsToSearch.AddRange(input.CurrentPathsCopy);
                else if (!string.IsNullOrEmpty(input.CurrentPath) && Directory.Exists(input.CurrentPath)) pathsToSearch.Add(input.CurrentPath);

                for (int pi = 0; pi < pathsToSearch.Count; pi++)
                {
                    string searchPath = pathsToSearch[pi];
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;
                    for (int ei = 0; ei < input.ExtensionsSplit.Length; ei++)
                    {
                        string ext = input.ExtensionsSplit[ei];
                        if (string.IsNullOrEmpty(ext)) continue;
                        var sysFileList = new List<string>();
                        try { FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList); }
                        catch { continue; }

                        for (int fi = 0; fi < sysFileList.Count; fi++)
                        {
                            if ((fi & 0x3F) == 0 && panel.GalleryFileRefreshSequence != refreshSequence)
                            {
                                outcome.Aborted = true;
                                return outcome;
                            }
                            string sysPath = sysFileList[fi] ?? "";
                            string norm = sysPath.Replace('\\', '/');
                            bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                            bool isCustomPreset =
                                (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

                            ClothingLoadingUtils.ResourceKind ck;
                            ClothingLoadingUtils.ResourceGender cg;
                            ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                            if (ck != ClothingLoadingUtils.ResourceKind.Hair) continue;

                            GalleryPanel.HairSubfilter cur = hairSubfilter;
                            bool PassesHairSubfiltersLoose(GalleryPanel.HairSubfilter f)
                            {
                                if (f == 0) return !isPresetEntry;
                                bool wantsPresets = (f & GalleryPanel.HairSubfilter.Presets) != 0;
                                bool wantsCustom = (f & GalleryPanel.HairSubfilter.Custom) != 0;
                                if (wantsPresets) { if (!isPresetEntry) return false; }
                                if (wantsCustom) { if (!isCustomPreset) return false; }
                                if (!wantsPresets && !wantsCustom) { if (isPresetEntry) return false; }
                                if ((f & GalleryPanel.HairSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                                if ((f & GalleryPanel.HairSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                                if ((f & GalleryPanel.HairSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female && cg != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                                return true;
                            }

                            if (PassesHairSubfiltersLoose(cur ^ GalleryPanel.HairSubfilter.Presets)) t.HairSubfilterFacetCountPresets++;
                            if (PassesHairSubfiltersLoose(cur ^ GalleryPanel.HairSubfilter.Custom)) t.HairSubfilterFacetCountCustom++;
                            if (PassesHairSubfiltersLoose(cur ^ GalleryPanel.HairSubfilter.Items)) t.HairSubfilterFacetCountItems++;
                            if (PassesHairSubfiltersLoose(cur ^ GalleryPanel.HairSubfilter.Male)) t.HairSubfilterFacetCountMale++;
                            if (PassesHairSubfiltersLoose(cur ^ GalleryPanel.HairSubfilter.Female)) t.HairSubfilterFacetCountFemale++;

                            t.HairSubfilterCountAll++;
                            if (isPresetEntry) t.HairSubfilterCountPresets++;
                            if (isCustomPreset) t.HairSubfilterCountCustom++;
                            if (!isPresetEntry) t.HairSubfilterCountItems++;
                            if (cg == ClothingLoadingUtils.ResourceGender.Male) t.HairSubfilterCountMale++;
                            else if (cg == ClothingLoadingUtils.ResourceGender.Female) t.HairSubfilterCountFemale++;
                        }
                    }
                }
            }

            if (isAppearanceTitle)
            {
                List<string> pathsToSearch = new List<string>();
                if (input.CurrentPathsCopy != null && input.CurrentPathsCopy.Count > 0) pathsToSearch.AddRange(input.CurrentPathsCopy);
                else if (!string.IsNullOrEmpty(input.CurrentPath) && Directory.Exists(input.CurrentPath)) pathsToSearch.Add(input.CurrentPath);

                // Prefer SQLite-cached loose .vap listings to avoid recursive filesystem enumeration on every scan.
                string sysCacheKey = null;
                string sysCacheSig = null;
                List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                bool sysCacheHit = false;
                try
                {
                    var sbKey = new StringBuilder(256);
                    sbKey.Append("tag:appearance|");
                    sbKey.Append("title=").Append(input.Title ?? "").Append('|');
                    sbKey.Append("ext=vap|paths=");
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
                        string sp = p2[i];
                        long tt = 0;
                        try { if (Directory.Exists(sp)) tt = Directory.GetLastWriteTimeUtc(sp).ToBinary(); } catch { tt = 0; }
                        if (i != 0) sbSig.Append('|');
                        sbSig.Append(tt.ToString());
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
                        if ((fi & 0x3F) == 0 && panel.GalleryFileRefreshSequence != refreshSequence)
                        {
                            outcome.Aborted = true;
                            return outcome;
                        }
                        string sysPath = sysFileList[fi] ?? "";
                        string norm = sysPath.Replace('\\', '/');
                        if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                            !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                            continue;
                        t.AppearanceSourceCountCustom++;
                        t.AppearanceSourceCountAll++;
                    }
                }

                if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                {
                    try
                    {
                        var allRows = new List<VpbLocalDatabase.SystemFileRow>(512);
                        for (int pi = 0; pi < pathsToSearch.Count; pi++)
                        {
                            string searchPath = pathsToSearch[pi];
                            if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;
                            var list = new List<string>();
                            try { FileManager.SafeGetFiles(searchPath, "*.vap", list); }
                            catch { continue; }
                            for (int i = 0; i < list.Count; i++)
                            {
                                string p = list[i] ?? "";
                                if (p.Length == 0) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                r.Path = p;
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                allRows.Add(r);
                            }
                        }
                        if (allRows.Count > 0)
                            VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, allRows);
                    }
                    catch { }
                }
            }

            var snap = new TagCountSnapshot
            {
                TagCounts = tagCounts,
                AppearanceSourceCountAll = t.AppearanceSourceCountAll,
                AppearanceSourceCountPresets = t.AppearanceSourceCountPresets,
                AppearanceSourceCountCustom = t.AppearanceSourceCountCustom,
                ClothingSubfilterCountAll = t.ClothingSubfilterCountAll,
                ClothingSubfilterCountReal = t.ClothingSubfilterCountReal,
                ClothingSubfilterCountPresets = t.ClothingSubfilterCountPresets,
                ClothingSubfilterCountCustom = t.ClothingSubfilterCountCustom,
                ClothingSubfilterCountItems = t.ClothingSubfilterCountItems,
                ClothingSubfilterCountMale = t.ClothingSubfilterCountMale,
                ClothingSubfilterCountFemale = t.ClothingSubfilterCountFemale,
                ClothingSubfilterCountDecals = t.ClothingSubfilterCountDecals,
                HairSubfilterCountAll = t.HairSubfilterCountAll,
                HairSubfilterCountPresets = t.HairSubfilterCountPresets,
                HairSubfilterCountCustom = t.HairSubfilterCountCustom,
                HairSubfilterCountItems = t.HairSubfilterCountItems,
                HairSubfilterCountMale = t.HairSubfilterCountMale,
                HairSubfilterCountFemale = t.HairSubfilterCountFemale,
                AppearanceSubfilterCountAll = t.AppearanceSubfilterCountAll,
                AppearanceSubfilterCountPresets = t.AppearanceSubfilterCountPresets,
                AppearanceSubfilterCountCustom = t.AppearanceSubfilterCountCustom,
                AppearanceSubfilterCountMale = t.AppearanceSubfilterCountMale,
                AppearanceSubfilterCountFemale = t.AppearanceSubfilterCountFemale,
                AppearanceSubfilterCountFuta = t.AppearanceSubfilterCountFuta,
                ClothingSubfilterFacetCountReal = t.ClothingSubfilterFacetCountReal,
                ClothingSubfilterFacetCountPresets = t.ClothingSubfilterFacetCountPresets,
                ClothingSubfilterFacetCountCustom = t.ClothingSubfilterFacetCountCustom,
                ClothingSubfilterFacetCountItems = t.ClothingSubfilterFacetCountItems,
                ClothingSubfilterFacetCountMale = t.ClothingSubfilterFacetCountMale,
                ClothingSubfilterFacetCountFemale = t.ClothingSubfilterFacetCountFemale,
                ClothingSubfilterFacetCountDecals = t.ClothingSubfilterFacetCountDecals,
                HairSubfilterFacetCountPresets = t.HairSubfilterFacetCountPresets,
                HairSubfilterFacetCountCustom = t.HairSubfilterFacetCountCustom,
                HairSubfilterFacetCountItems = t.HairSubfilterFacetCountItems,
                HairSubfilterFacetCountMale = t.HairSubfilterFacetCountMale,
                HairSubfilterFacetCountFemale = t.HairSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountPresets = t.AppearanceSubfilterFacetCountPresets,
                AppearanceSubfilterFacetCountCustom = t.AppearanceSubfilterFacetCountCustom,
                AppearanceSubfilterFacetCountMale = t.AppearanceSubfilterFacetCountMale,
                AppearanceSubfilterFacetCountFemale = t.AppearanceSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountFuta = t.AppearanceSubfilterFacetCountFuta,
                AppearanceSubfilterCurrentCountAll = t.AppearanceSubfilterCurrentCountAll,
                AppearanceSubfilterCurrentCountMale = t.AppearanceSubfilterCurrentCountMale,
                AppearanceSubfilterCurrentCountFemale = t.AppearanceSubfilterCurrentCountFemale,
                AppearanceSubfilterCurrentCountFuta = t.AppearanceSubfilterCurrentCountFuta,
            };
            outcome.Snapshot = snap;
            outcome.Aborted = false;
            return outcome;
        }

        private static bool PassesClothingSubfilters(GalleryPanel.ClothingSubfilter f, bool isDecal, bool isPresetEntry, bool isCustomPreset, ClothingLoadingUtils.ResourceGender cg)
        {
            if (f == 0) return true;
            bool wantsRealType = ((f & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Presets | GalleryPanel.ClothingSubfilter.Custom | GalleryPanel.ClothingSubfilter.Items | GalleryPanel.ClothingSubfilter.Male | GalleryPanel.ClothingSubfilter.Female)) != 0);
            bool wantsDecalType = ((f & GalleryPanel.ClothingSubfilter.Decals) != 0);
            bool typeExplicit = ((f & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Decals)) != 0);
            if (typeExplicit)
            {
                bool okType = (!isDecal && (f & GalleryPanel.ClothingSubfilter.RealClothing) != 0) ||
                              (isDecal && (f & GalleryPanel.ClothingSubfilter.Decals) != 0);
                if (!okType) return false;
            }
            else
            {
                if (wantsRealType && isDecal && !wantsDecalType) return false;
            }
            bool wantsPresets = (f & GalleryPanel.ClothingSubfilter.Presets) != 0;
            bool wantsCustom = (f & GalleryPanel.ClothingSubfilter.Custom) != 0;
            if (wantsPresets) { if (!isPresetEntry) return false; }
            if (wantsCustom) { if (!isCustomPreset) return false; }
            if ((f & GalleryPanel.ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
            if ((f & GalleryPanel.ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
            if ((f & GalleryPanel.ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }
            return true;
        }

        private static bool PassesClothingSubfiltersLoose(GalleryPanel.ClothingSubfilter f, bool isDecal, bool isPresetEntry, bool isCustomPreset, ClothingLoadingUtils.ResourceGender cg)
        {
            if (f == 0) return true;
            bool wantsRealType = ((f & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Presets | GalleryPanel.ClothingSubfilter.Custom | GalleryPanel.ClothingSubfilter.Items | GalleryPanel.ClothingSubfilter.Male | GalleryPanel.ClothingSubfilter.Female)) != 0);
            bool wantsDecalType = ((f & GalleryPanel.ClothingSubfilter.Decals) != 0);
            bool typeExplicit = ((f & (GalleryPanel.ClothingSubfilter.RealClothing | GalleryPanel.ClothingSubfilter.Decals)) != 0);
            if (typeExplicit)
            {
                bool okType = (!isDecal && (f & GalleryPanel.ClothingSubfilter.RealClothing) != 0) ||
                              (isDecal && (f & GalleryPanel.ClothingSubfilter.Decals) != 0);
                if (!okType) return false;
            }
            else
            {
                if (wantsRealType && isDecal && !wantsDecalType) return false;
            }
            bool wantsPresets = (f & GalleryPanel.ClothingSubfilter.Presets) != 0;
            bool wantsCustom = (f & GalleryPanel.ClothingSubfilter.Custom) != 0;
            if (wantsPresets || wantsCustom)
            {
                if (!isPresetEntry) return false;
                if (wantsPresets && !wantsCustom) { if (isCustomPreset) return false; }
                if (wantsCustom && !wantsPresets) { if (!isCustomPreset) return false; }
            }
            if ((f & GalleryPanel.ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
            if ((f & GalleryPanel.ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
            if ((f & GalleryPanel.ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }
            return true;
        }

        private static bool PassesAppearanceSubfilters(GalleryPanel.AppearanceSubfilter f, GalleryPanel.AppearanceSubfilter active, bool isPresetAppearance, bool isCustomAppearance, int g)
        {
            if (f == 0) return true;
            bool wantsPresets = (f & GalleryPanel.AppearanceSubfilter.Presets) != 0;
            bool wantsCustom = (f & GalleryPanel.AppearanceSubfilter.Custom) != 0;
            bool wantsMale = (f & GalleryPanel.AppearanceSubfilter.Male) != 0;
            bool wantsFemale = (f & GalleryPanel.AppearanceSubfilter.Female) != 0;
            bool wantsFuta = (f & GalleryPanel.AppearanceSubfilter.Futa) != 0;

            bool typeOk = true;
            if (wantsPresets || wantsCustom)
            {
                if (wantsPresets && wantsCustom) typeOk = true;
                else if (wantsPresets) typeOk = isPresetAppearance;
                else if (wantsCustom) typeOk = isCustomAppearance;
            }
            if (!typeOk) return false;

            bool wantsAnyGender = wantsMale || wantsFemale || wantsFuta;
            if (wantsAnyGender)
            {
                bool genderOk = false;
                if (wantsMale && g == 2) genderOk = true;
                if (wantsFemale && g == 1) genderOk = true;
                if (wantsFuta && g == 3) genderOk = true;
                if (!genderOk) return false;
            }
            return true;
        }
    }
}
