using System;
using System.Collections;
using System.Collections.Generic;
using VPB.src.util;

namespace VPB
{
    /// <summary>Appearance preset gender: Unknown=0, Female=1, Male=2, Futa=3 (Futa folded to Male in UI).</summary>
    internal enum AppearanceGender
    {
        Unknown = 0,
        Female = 1,
        Male = 2,
        Futa = 3,
    }

    /// <summary>Unified appearance gender resolution: gallery user tags, legacy UserTags.json, loose .vap probe, path tokens.</summary>
    internal static class AppearanceGenderClassifier
    {
        private static readonly char[] s_TokenSeps = { '/', '\\', '.', '_', '-', ' ', '(', ')', '[', ']', '{', '}', ',', ';', ':' };

        internal static int ToGenderCode(AppearanceGender g) => (int)g;

        internal static AppearanceGender FromGenderCode(int code)
        {
            if (code < 0 || code > 3) return AppearanceGender.Unknown;
            return (AppearanceGender)code;
        }

        /// <summary>Facet badge: preview selecting only <paramref name="genderFlag"/> (replace gender bits, keep type/local flags).</summary>
        internal static GalleryPanel.AppearanceSubfilter HypotheticalGenderFacet(GalleryPanel.AppearanceSubfilter active, GalleryPanel.AppearanceSubfilter genderFlag)
        {
            if ((active & genderFlag) != 0) return active;
            const GalleryPanel.AppearanceSubfilter genderMask =
                GalleryPanel.AppearanceSubfilter.Male
                | GalleryPanel.AppearanceSubfilter.Female
                | GalleryPanel.AppearanceSubfilter.Futa
                | GalleryPanel.AppearanceSubfilter.Unknown;
            return (active & ~genderMask) | genderFlag;
        }

        /// <summary>VaM-relative path used for appearance folder / preset vs custom checks.</summary>
        internal static bool TryGetAppearanceScopePath(FileEntry entry, out string scopePath)
        {
            scopePath = "";
            if (entry == null) return false;

            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null)
            {
                scopePath = (vfe.InternalPath ?? "").Replace('\\', '/');
                return scopePath.Length > 0;
            }

            string normalized;
            if (GalleryPanel.TryNormalizeGalleryPathUnderKnownRoots(entry.Path ?? "", out normalized))
            {
                scopePath = normalized;
                return true;
            }

            scopePath = (entry.Path ?? "").Replace('\\', '/');
            return scopePath.Length > 0;
        }

        internal static bool IsAppearanceFolderBrowsePath(string browsePath)
        {
            if (string.IsNullOrEmpty(browsePath)) return false;
            string cp = browsePath.Replace('\\', '/').TrimEnd('/');
            return cp.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase)
                || cp.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool EntryMatchesAppearanceBrowseScope(FileEntry entry, string browsePath, System.Collections.Generic.List<string> browsePaths)
        {
            string scopePath;
            if (!TryGetAppearanceScopePath(entry, out scopePath)) return false;

            if (browsePaths != null && browsePaths.Count > 0)
            {
                for (int i = 0; i < browsePaths.Count; i++)
                {
                    string pref = browsePaths[i];
                    if (GalleryPanel.GalleryInternalPathStartsWithPrefix(scopePath, pref))
                        return true;
                }
                return false;
            }

            if (string.IsNullOrEmpty(browsePath)) return true;
            return GalleryPanel.GalleryInternalPathStartsWithPrefix(scopePath, browsePath);
        }

        internal static bool ResolveIsCustomAppearance(FileEntry entry)
        {
            string scopePath;
            if (!TryGetAppearanceScopePath(entry, out scopePath)) return false;
            return scopePath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ResolveIsPresetAppearance(FileEntry entry)
        {
            if (entry == null) return false;
            string scopePath;
            if (!TryGetAppearanceScopePath(entry, out scopePath)) return false;
            if (!scopePath.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)) return false;
            return scopePath.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
        }

        internal static AppearanceGender Classify(FileEntry entry, string categoryTitle)
            => Classify(entry, categoryTitle, null);

        /// <summary>Gender filter path: tags + path tokens only (no .vap file probe).</summary>
        internal static AppearanceGender ClassifyForGenderFilter(FileEntry entry, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            if (entry == null) return AppearanceGender.Unknown;

            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null)
            {
                string pkgUid = "";
                try
                {
                    if (vfe.Package != null) pkgUid = vfe.Package.Uid ?? "";
                    else pkgUid = vfe.GetRowPackageUid() ?? "";
                }
                catch { }
                return ClassifyVarAppearance(categoryTitle, vfe.InternalPath ?? "", entry.Name ?? "", pkgUid, tagsByRowKey);
            }

            HashSet<string> userTags = CollectUserTagsForFileEntry(entry, categoryTitle, tagsByRowKey);
            AppearanceGender? fromTags = ClassifyFromUserTags(userTags);
            if (fromTags.HasValue) return fromTags.Value;

            SystemFileEntry sfe = entry as SystemFileEntry;
            if (sfe != null && !sfe.isVar)
            {
                string loosePath = sfe.Path ?? "";
                if (loosePath.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                {
                    long wtBin = 0, sz = 0;
                    try
                    {
                        var fi = new System.IO.FileInfo(loosePath);
                        if (fi.Exists)
                        {
                            wtBin = fi.LastWriteTimeUtc.ToBinary();
                            sz = fi.Length;
                        }
                    }
                    catch { }
                    int dbGender;
                    if (VpbLocalDatabase.TryReadLooseVapGender(loosePath, wtBin, sz, out dbGender))
                    {
                        AppearanceGender cached = FromGenderCode(dbGender);
                        if (cached == AppearanceGender.Futa) cached = AppearanceGender.Male;
                        if (cached != AppearanceGender.Unknown) return cached;
                    }
                }
            }

            string scopePath;
            if (!TryGetAppearanceScopePath(entry, out scopePath)) scopePath = entry.Path ?? "";
            return ClassifyFromPathTokens(scopePath, entry.Name ?? "", "");
        }

        internal static AppearanceGender Classify(FileEntry entry, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            if (entry == null) return AppearanceGender.Unknown;

            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null)
            {
                string pkgUid = "";
                try
                {
                    if (vfe.Package != null) pkgUid = vfe.Package.Uid ?? "";
                    else pkgUid = vfe.GetRowPackageUid() ?? "";
                }
                catch { }
                return ClassifyVarAppearance(categoryTitle, vfe.InternalPath ?? "", entry.Name ?? "", pkgUid, tagsByRowKey);
            }

            HashSet<string> userTags = CollectUserTagsForFileEntry(entry, categoryTitle, tagsByRowKey);
            AppearanceGender? fromTags = ClassifyFromUserTags(userTags);
            if (fromTags.HasValue) return fromTags.Value;

            string p = entry.Path ?? "";

            if (entry is SystemFileEntry sfeProbe
                && !sfeProbe.isVar
                && !string.IsNullOrEmpty(p)
                && p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
            {
                AppearanceGender fromProbe = ClassifyFromLooseVapProbe(p);
                if (fromProbe != AppearanceGender.Unknown) return fromProbe;
            }

            string pathForTokens = p.Replace('\\', '/');
            return ClassifyFromPathTokens(pathForTokens, entry.Name ?? "", "");
        }

        internal static AppearanceGender ClassifyLooseVapPath(string absolutePath, string categoryTitle)
            => ClassifyLooseVapPath(absolutePath, categoryTitle, null);

        internal static AppearanceGender ClassifyLooseVapPath(string absolutePath, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            if (string.IsNullOrEmpty(absolutePath)) return AppearanceGender.Unknown;

            HashSet<string> userTags = CollectUserTagsForLoosePath(absolutePath, categoryTitle, tagsByRowKey);
            AppearanceGender? fromTags = ClassifyFromUserTags(userTags);
            if (fromTags.HasValue) return fromTags.Value;

            AppearanceGender fromProbe = ClassifyFromLooseVapProbe(absolutePath);
            if (fromProbe != AppearanceGender.Unknown) return fromProbe;

            string norm = absolutePath.Replace('\\', '/');
            int slash = norm.LastIndexOf('/');
            string fileName = slash >= 0 && slash + 1 < norm.Length ? norm.Substring(slash + 1) : norm;
            return ClassifyFromPathTokens(norm, fileName, "");
        }

        internal static AppearanceGender ClassifyVarAppearance(string categoryTitle, string internalPath, string fileName, string pkgUid)
            => ClassifyVarAppearance(categoryTitle, internalPath, fileName, pkgUid, null);

        internal static AppearanceGender ClassifyVarAppearance(string categoryTitle, string internalPath, string fileName, string pkgUid, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            HashSet<string> userTags = CollectUserTagsForVarRow(categoryTitle, pkgUid, internalPath, tagsByRowKey);
            AppearanceGender? fromTags = ClassifyFromUserTags(userTags);
            if (fromTags.HasValue) return fromTags.Value;
            return ClassifyFromPathTokens(internalPath ?? "", fileName ?? "", pkgUid ?? "");
        }

        internal static HashSet<string> CollectUserTagsForFileEntry(FileEntry entry, string categoryTitle)
            => CollectUserTagsForFileEntry(entry, categoryTitle, null);

        internal static HashSet<string> CollectUserTagsForFileEntry(FileEntry entry, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry == null || string.IsNullOrEmpty(categoryTitle)) return tags;

            SystemFileEntry sfe = entry as SystemFileEntry;
            if (sfe != null && !sfe.isVar)
            {
                MergeLooseGalleryAndLegacyTags(tags, sfe.Path, categoryTitle, tagsByRowKey);
                return tags;
            }

            try { MergeLegacyTags(tags, TagsManager.Instance.GetTags(entry.Uid)); } catch { }
            return tags;
        }

        internal static HashSet<string> CollectUserTagsForLoosePath(string absolutePath, string categoryTitle)
            => CollectUserTagsForLoosePath(absolutePath, categoryTitle, null);

        internal static HashSet<string> CollectUserTagsForLoosePath(string absolutePath, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(categoryTitle)) return tags;
            MergeLooseGalleryAndLegacyTags(tags, absolutePath, categoryTitle, tagsByRowKey);
            return tags;
        }

        internal static HashSet<string> CollectUserTagsForVarRow(string categoryTitle, string pkgUid, string internalPath)
            => CollectUserTagsForVarRow(categoryTitle, pkgUid, internalPath, null);

        internal static HashSet<string> CollectUserTagsForVarRow(string categoryTitle, string pkgUid, string internalPath, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(categoryTitle)) return tags;
            if (!string.IsNullOrEmpty(pkgUid) && !string.IsNullOrEmpty(internalPath))
            {
                string rowKey = VpbLocalDatabase.FormatCatMemRowLookupKey(pkgUid, internalPath);
                if (!MergeTagsFromRowKeyMap(tagsByRowKey, rowKey, tags) && tagsByRowKey == null)
                {
                    try { VpbLocalDatabase.TryGetGalleryUserTagsForRow(categoryTitle, pkgUid, internalPath, tags); } catch { }
                }
                MergeLegacyTags(tags, TagsManager.Instance.GetTags((pkgUid ?? "") + ":/" + internalPath));
            }
            return tags;
        }

        private static bool MergeTagsFromRowKeyMap(Dictionary<string, HashSet<string>> tagsByRowKey, string rowKey, HashSet<string> dest)
        {
            if (tagsByRowKey == null || dest == null || string.IsNullOrEmpty(rowKey)) return false;
            HashSet<string> src;
            if (!tagsByRowKey.TryGetValue(rowKey, out src) || src == null || src.Count == 0) return false;
            foreach (string t in src)
            {
                if (!string.IsNullOrEmpty(t)) dest.Add(t);
            }
            return true;
        }

        internal static bool PassesAppearanceGenderSubfilter(AppearanceGender g, GalleryPanel.AppearanceSubfilter f)
        {
            bool wantsMale = (f & GalleryPanel.AppearanceSubfilter.Male) != 0;
            bool wantsFemale = (f & GalleryPanel.AppearanceSubfilter.Female) != 0;
            bool wantsFuta = (f & GalleryPanel.AppearanceSubfilter.Futa) != 0;
            bool wantsUnknown = (f & GalleryPanel.AppearanceSubfilter.Unknown) != 0;
            if (!wantsMale && !wantsFemale && !wantsFuta && !wantsUnknown) return true;

            if (wantsMale && g == AppearanceGender.Male) return true;
            if (wantsFemale && g == AppearanceGender.Female) return true;
            if (wantsFuta && g == AppearanceGender.Futa) return true;
            if (wantsUnknown && g == AppearanceGender.Unknown) return true;
            return false;
        }

        internal static bool PassesAppearanceGenderSubfilter(int genderCode, GalleryPanel.AppearanceSubfilter f)
            => PassesAppearanceGenderSubfilter(FromGenderCode(genderCode), f);

        internal static bool TagsAffectAppearanceGender(IEnumerable<string> tagNames)
        {
            if (tagNames == null) return false;
            foreach (string t in tagNames)
            {
                if (string.IsNullOrEmpty(t)) continue;
                string tl = t.Trim().ToLowerInvariant();
                if (tl == "futa" || tl == "herm" || tl == "shemale" || tl == "dickgirl") return true;
                if (tl == "female" || tl == "woman" || tl == "girl") return true;
                if (tl == "male" || tl == "man" || tl == "boy") return true;
                if (tl == "unknown") return true;
            }
            return false;
        }

        internal static AppearanceGender? GenderFromTagNamesOnly(IEnumerable<string> tagNames)
        {
            if (tagNames == null) return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in tagNames)
            {
                if (!string.IsNullOrEmpty(t)) set.Add(t);
            }
            return ClassifyFromUserTags(set);
        }

        private static void MergeLooseGalleryAndLegacyTags(HashSet<string> tags, string absolutePath, string categoryTitle)
            => MergeLooseGalleryAndLegacyTags(tags, absolutePath, categoryTitle, null);

        private static void MergeLooseGalleryAndLegacyTags(HashSet<string> tags, string absolutePath, string categoryTitle, Dictionary<string, HashSet<string>> tagsByRowKey)
        {
            if (tags == null || string.IsNullOrEmpty(absolutePath)) return;
            string norm = VpbLocalDatabase.NormalizeLoosePathForGalleryUserTag(absolutePath);
            if (!string.IsNullOrEmpty(norm) && !string.IsNullOrEmpty(categoryTitle))
            {
                string rowKey = VpbLocalDatabase.FormatCatMemRowLookupKey(VpbLocalDatabase.GalleryUserTagLoosePkgUid, norm);
                if (!MergeTagsFromRowKeyMap(tagsByRowKey, rowKey, tags) && tagsByRowKey == null)
                {
                    try { VpbLocalDatabase.TryGetGalleryUserTagsForRow(categoryTitle, VpbLocalDatabase.GalleryUserTagLoosePkgUid, norm, tags); } catch { }
                }
            }
            try { MergeLegacyTags(tags, TagsManager.Instance.GetTags(absolutePath)); } catch { }
            if (!string.IsNullOrEmpty(norm))
            {
                try { MergeLegacyTags(tags, TagsManager.Instance.GetTags(norm)); } catch { }
            }
        }

        private static void MergeLegacyTags(HashSet<string> dest, HashSet<string> src)
        {
            if (dest == null || src == null || src.Count == 0) return;
            foreach (string t in src)
            {
                if (!string.IsNullOrEmpty(t)) dest.Add(t);
            }
        }

        private static AppearanceGender ClassifyFromLooseVapProbe(string filePath)
        {
            LooseVapGenderProbe.Gender pg;
            try { pg = LooseVapGenderProbe.Classify(filePath); }
            catch { return AppearanceGender.Unknown; }

            if (pg == LooseVapGenderProbe.Gender.Female) return AppearanceGender.Female;
            if (pg == LooseVapGenderProbe.Gender.Male) return AppearanceGender.Male;
            if (pg == LooseVapGenderProbe.Gender.Futa) return AppearanceGender.Male;
            return AppearanceGender.Unknown;
        }

        private static AppearanceGender? ClassifyFromUserTags(HashSet<string> userTags)
        {
            if (userTags == null || userTags.Count == 0) return null;

            foreach (var t in userTags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                string tl = t.Trim().ToLowerInvariant();
                if (tl == "futa" || tl == "herm" || tl == "shemale" || tl == "dickgirl") return AppearanceGender.Male;
            }
            foreach (var t in userTags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                string tl = t.Trim().ToLowerInvariant();
                if (tl == "female" || tl == "woman" || tl == "girl") return AppearanceGender.Female;
                if (tl == "male" || tl == "man" || tl == "boy") return AppearanceGender.Male;
            }
            return null;
        }

        private static AppearanceGender ClassifyFromPathTokens(string pathOrInternalPath, string fileName, string pkgUid)
        {
            string s = (pathOrInternalPath ?? "").Replace('\\', '/');
            string combined = (s + " " + (fileName ?? "") + " " + (pkgUid ?? "")).ToLowerInvariant();
            string[] tokens = combined.Split(s_TokenSeps, StringSplitOptions.RemoveEmptyEntries);

            if (HasToken(tokens, "futa") || HasToken(tokens, "herm") || HasToken(tokens, "shemale") || HasToken(tokens, "dickgirl"))
                return AppearanceGender.Male;
            if (HasToken(tokens, "female") || HasToken(tokens, "woman") || HasToken(tokens, "girl"))
                return AppearanceGender.Female;
            if (HasToken(tokens, "male") || HasToken(tokens, "man") || HasToken(tokens, "boy"))
                return AppearanceGender.Male;
            return AppearanceGender.Unknown;
        }

        private static bool HasToken(string[] tokens, string tok)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == tok) return true;
            }
            return false;
        }
    }
}
