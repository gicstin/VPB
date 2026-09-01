using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        // Value matches old key so installs that already ran the backfill don't repeat it.
        internal const string ClothAttrPresentBackfillKey = "facet_tally_v1";

        // Same schema as cat_mem so loose and VAR rows are read identically; cat_mem stays VAR-only.
        internal const string LooseCatMemDdl =
            "CREATE TABLE IF NOT EXISTS loose_cat_mem (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, list_path TEXT, cloth_attr TEXT, PRIMARY KEY(category, pkg_uid, internal_path));" +
            "CREATE INDEX IF NOT EXISTS idx_lcm_cat ON loose_cat_mem(category);";

        // Caller must hold an open transaction; this method does not BEGIN/COMMIT.
        internal static void BackfillClothAttrPresentFlag(VpbSqlite3.Connection conn, out int repacked, out int unresolved)
        {
            repacked = 0;
            unresolved = 0;
            // Two-pass: collect while SELECT is open, then UPDATE; nested Prepare/Step on this connection wrapper is not safe.
            var categories = new List<string>();
            var pkgUids = new List<string>();
            var internalPaths = new List<string>();
            var attrValues = new List<int>();
            using (var sel = conn.Prepare(
                "SELECT category, pkg_uid, internal_path, list_path FROM cat_mem " +
                "WHERE (CAST(ifnull(cloth_attr,'0') AS INTEGER) & " + ClothingAttrPresentFlag + ") = 0 " +
                "AND category IN ('Clothing','Hair')"))
            {
                while (sel.Step() == VpbSqlite3.SqliteRow)
                {
                    string cat = sel.ColumnText(0);
                    string uid = sel.ColumnText(1);
                    string ip = sel.ColumnText(2);
                    string listPath = sel.ColumnText(3);
                    int packed = PackClothingGalleryAttrForVarListPath(string.IsNullOrEmpty(listPath) ? ip : listPath);
                    if (packed == 0)
                    {
                        unresolved++;
                        continue;
                    }
                    categories.Add(cat);
                    pkgUids.Add(uid);
                    internalPaths.Add(ip);
                    attrValues.Add(packed);
                }
            }
            if (categories.Count == 0) return;
            using (var upd = conn.Prepare("UPDATE cat_mem SET cloth_attr=? WHERE category=? AND pkg_uid=? AND internal_path=?"))
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    upd.BindText(1, attrValues[i].ToString());
                    upd.BindText(2, categories[i]);
                    upd.BindText(3, pkgUids[i]);
                    upd.BindText(4, internalPaths[i]);
                    upd.Step();
                    upd.Reset();
                    repacked++;
                }
            }
        }

        internal static int PackClothingGalleryAttrForLoosePath(string diskPath, bool isCustom)
        {
            if (string.IsNullOrEmpty(diskPath)) return 0;
            ClothingLoadingUtils.ResourceKind kind;
            ClothingLoadingUtils.ResourceGender gender;
            ClothingLoadingUtils.ClassifyClothingHairPath(diskPath, out kind, out gender);
            if (kind != ClothingLoadingUtils.ResourceKind.Clothing &&
                kind != ClothingLoadingUtils.ResourceKind.Hair)
                return 0;
            int baseAttr = PackClothingGalleryAttrForVarListPath(diskPath);
            if (baseAttr == 0) return 0;
            return baseAttr | (isCustom ? ClothingAttrIsCustomFlag : 0);
        }

        // Not lowercased: caller's dict is OrdinalIgnoreCase, matching the grid's family dict.
        private static void ParseUidFamilyVersion(string uid, out string family, out int version)
        {
            family = uid ?? "";
            version = 0;
            if (string.IsNullOrEmpty(uid)) return;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0) { family = uid; return; }
            family = uid.Substring(0, lastDot);
            if (lastDot < uid.Length - 1)
            {
                int v;
                if (int.TryParse(uid.Substring(lastDot + 1), out v)) version = v;
            }
        }

        private static readonly string[,] s_LooseRoots =
        {
            // root                        category    exts (split on |)
            { "Custom/Clothing",           "Clothing", "vam|vap" },
            { "Custom/Atom/Person/Clothing","Clothing", "vap"     },
            { "Saves/Person/Clothing",     "Clothing", "vam|vap" },
            { "Custom/Hair",               "Hair",     "vam|vap" },
            { "Custom/Atom/Person/Hair",   "Hair",     "vap"     },
            { "Saves/Person/Hair",         "Hair",     "vam|vap" },
        };

        // Self-transactional: do not call from within an existing transaction.
        internal static void EnsureLooseCatMemIndex(VpbSqlite3.Connection conn)
        {
            // Build combined mtime sig over all 6 roots in fixed order (missing dirs return 0).
            var sigSb = new StringBuilder(128);
            for (int i = 0; i < s_LooseRoots.GetLength(0); i++)
            {
                string root = s_LooseRoots[i, 0];
                long mtime = 0;
                try { mtime = DeepMaxDirMtimeBinary(root); } catch { mtime = 0; }
                if (i != 0) sigSb.Append('|');
                sigSb.Append(mtime.ToString());
            }
            string newSig = sigSb.ToString();

            string storedSig = MetaGet(conn, "loose_catmem_sig");
            if (string.Equals(storedSig, newSig, StringComparison.Ordinal)) return;

            try
            {
                conn.ExecUtf8("BEGIN IMMEDIATE;");

                conn.ExecUtf8("DELETE FROM loose_cat_mem WHERE category IN ('Clothing','Hair');");

                using (var ins = conn.Prepare(
                    "INSERT OR IGNORE INTO loose_cat_mem(category, pkg_uid, internal_path, list_path, cloth_attr) " +
                    "VALUES(?,?,?,?,?)"))
                {
                    for (int ri = 0; ri < s_LooseRoots.GetLength(0); ri++)
                    {
                        string root     = s_LooseRoots[ri, 0];
                        string category = s_LooseRoots[ri, 1];
                        string exts     = s_LooseRoots[ri, 2];

                        if (!Directory.Exists(root)) continue;

                        string[] extArr = exts.Split('|');
                        for (int ei = 0; ei < extArr.Length; ei++)
                        {
                            string ext = extArr[ei];
                            if (string.IsNullOrEmpty(ext)) continue;

                            var files = new List<string>();
                            try { FileManager.SafeGetFiles(root, "*." + ext, files); }
                            catch { continue; }

                            for (int fi = 0; fi < files.Count; fi++)
                            {
                                string raw = files[fi] ?? "";
                                if (raw.Length == 0) continue;
                                string norm = raw.Replace('\\', '/');

                                // Category-by-root: use the pass category, not the classifier's kind.
                                ClothingLoadingUtils.ResourceKind ck;
                                ClothingLoadingUtils.ResourceGender cg;
                                ClothingLoadingUtils.ClassifyClothingHairPath(norm, out ck, out cg);
                                if (category == "Clothing" && ck != ClothingLoadingUtils.ResourceKind.Clothing) continue;
                                if (category == "Hair"     && ck != ClothingLoadingUtils.ResourceKind.Hair)     continue;

                                bool isCustom =
                                    norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                    norm.StartsWith("Saves/",  StringComparison.OrdinalIgnoreCase) ||
                                    norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    norm.IndexOf("/Saves/",  StringComparison.OrdinalIgnoreCase) >= 0;

                                int attr = PackClothingGalleryAttrForLoosePath(norm, isCustom);
                                if (attr == 0) continue;

                                ins.BindText(1, category);
                                ins.BindText(2, "__local__");
                                ins.BindText(3, norm);
                                ins.BindText(4, norm);
                                ins.BindText(5, attr.ToString());
                                ins.Step();
                                ins.Reset();
                            }
                        }
                    }
                }

                MetaSet(conn, "loose_catmem_sig", newSig);

                conn.ExecUtf8("COMMIT;");
            }
            catch (Exception ex)
            {
                try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                try { LogUtil.LogWarning("EnsureLooseCatMemIndex failed: " + ex.Message); } catch { }
            }
        }

        private struct ClothCountGroupRow
        {
            public int Attr;
            public int Cnt;
            public string Family;
            public int Version;
        }

        internal struct ClothingChipCounts
        {
            public int Default;    // f==0 (no chip active)
            public int Real;
            public int Presets;
            public int Custom;
            public int CustomPreset;
            public int Items;
            public int Male;
            public int Female;
            public int Decals;
        }

        // sourceMode: 0=All, 1=Local-only, 2=VAR-only; loose rows are never version-filtered.
        // pkgVersionFilter: PkgVersionFilterOff / NewestOnly / OldOnly (library-global pkg.is_newest).
        internal static bool TryQueryClothingChipCounts(
            string creatorFilter,
            int loadedState,
            string[] nameTerms,
            List<string> pathExclusions,
            List<string> pathInclusions,
            HashSet<string> activeTags,
            HashSet<string> activeUserTags,
            bool userTagsUntaggedOnly,
            bool userTagsRequireAll,
            int sourceMode,
            int pkgVersionFilter,
            out ClothingChipCounts counts,
            HashSet<string> excludedUserTags = null,
            bool userTagsTaggedOnly = false,
            string licenseFilter = null)
        {
            return TryQueryClothingChipCounts(
                creatorFilter, loadedState, GallerySearchQuery.FromLegacyNameTerms(nameTerms),
                pathExclusions, pathInclusions, activeTags, activeUserTags,
                userTagsUntaggedOnly, userTagsRequireAll, sourceMode, pkgVersionFilter,
                out counts, excludedUserTags, userTagsTaggedOnly, licenseFilter);
        }

        /// <summary>Legacy bool overload: hideOldVersions → NewestOnly.</summary>
        internal static bool TryQueryClothingChipCounts(
            string creatorFilter,
            int loadedState,
            string[] nameTerms,
            List<string> pathExclusions,
            List<string> pathInclusions,
            HashSet<string> activeTags,
            HashSet<string> activeUserTags,
            bool userTagsUntaggedOnly,
            bool userTagsRequireAll,
            int sourceMode,
            bool hideOldVersions,
            out ClothingChipCounts counts,
            HashSet<string> excludedUserTags = null,
            bool userTagsTaggedOnly = false,
            string licenseFilter = null)
        {
            return TryQueryClothingChipCounts(
                creatorFilter, loadedState, nameTerms, pathExclusions, pathInclusions,
                activeTags, activeUserTags, userTagsUntaggedOnly, userTagsRequireAll, sourceMode,
                hideOldVersions ? PkgVersionFilterNewestOnly : PkgVersionFilterOff,
                out counts, excludedUserTags, userTagsTaggedOnly, licenseFilter);
        }

        internal static bool TryQueryClothingChipCounts(
            string creatorFilter,
            int loadedState,
            GallerySearchQuery searchQuery,
            List<string> pathExclusions,
            List<string> pathInclusions,
            HashSet<string> activeTags,
            HashSet<string> activeUserTags,
            bool userTagsUntaggedOnly,
            bool userTagsRequireAll,
            int sourceMode,
            int pkgVersionFilter,
            out ClothingChipCounts counts,
            HashSet<string> excludedUserTags = null,
            bool userTagsTaggedOnly = false,
            string licenseFilter = null)
        {
            counts = new ClothingChipCounts();
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan;
            string catSig;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureLooseCatMemIndex(conn);

                    var ctx = BuildGalleryCategoryWhere(
                        conn, "Clothing", creatorFilter, loadedState,
                        searchQuery ?? GallerySearchQuery.Empty, pathExclusions, pathInclusions,
                        activeTags, activeUserTags, userTagsUntaggedOnly, userTagsRequireAll, excludedUserTags,
                        pkgVersionFilter, userTagsTaggedOnly, licenseFilter);

                    string afterClothFragments = ctx.LoadedAndFragment + ctx.VersionAndFragment + ctx.LicenseAndFragment
                        + ctx.NameAndFragment
                        + ctx.SearchTimeAndFragment
                        + ctx.ExclusionAndFragment + ctx.InclusionAndFragment
                        + ctx.TagAndFragment + ctx.UserTagAndFragment + ctx.ExcludedUserTagAndFragment;

                    GalleryPanel.ClothingSubfilter[] chips = new GalleryPanel.ClothingSubfilter[]
                    {
                        0,
                        GalleryPanel.ClothingSubfilter.RealClothing,
                        GalleryPanel.ClothingSubfilter.Presets,
                        GalleryPanel.ClothingSubfilter.Custom,
                        GalleryPanel.ClothingSubfilter.CustomPreset,
                        GalleryPanel.ClothingSubfilter.Items,
                        GalleryPanel.ClothingSubfilter.Male,
                        GalleryPanel.ClothingSubfilter.Female,
                        GalleryPanel.ClothingSubfilter.Decals,
                    };

                    int[] varCounts = new int[chips.Length];

                    // cloth_attr unindexed (~11 values): C# classify per chip; list_path unique so COUNT(*) == COUNT(DISTINCT list_path).
                    if (sourceMode != 1) // skip VAR when source=Local
                    {
                        var rows = new List<ClothCountGroupRow>();

                        bool showHidden = false;
                        try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
                        bool hideInSql = false;
                        if (!showHidden)
                        {
                            try
                            {
                                VpbHideIndex.EnsureBuilt();
                                hideInSql = VpbHideIndex.SqlMirrorFresh;
                            }
                            catch { hideInSql = false; }
                        }

                        var groupSb = new StringBuilder(320);
                        groupSb.Append("SELECT CAST(ifnull(m.cloth_attr,'0') AS INTEGER) AS attr, m.pkg_uid, COUNT(*)")
                               .Append(" FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid WHERE m.category = ?")
                               .Append(ctx.CreatorAndFragment).Append(afterClothFragments);
                        if (hideInSql) AppendGalleryHideExclusionSql(groupSb, "m");
                        groupSb.Append(" GROUP BY attr, m.pkg_uid");

                        using (var st = conn.Prepare(groupSb.ToString()))
                        {
                            BindGalleryCategoryWhere(st, ctx, 1);
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(1) ?? "";
                                if (!hideInSql && PackageHidePrefs.IsVarPackageHiddenByUid(uid)) continue;
                                ClothCountGroupRow r;
                                r.Attr = (int)st.ColumnInt64(0);
                                r.Cnt = (int)st.ColumnInt64(2);
                                ParseUidFamilyVersion(uid, out r.Family, out r.Version);
                                rows.Add(r);
                            }
                        }

                        for (int ci = 0; ci < chips.Length; ci++)
                        {
                            var chip = chips[ci];
                            // Male/Female allows Unknown gender; VAR rows are never Custom/CustomPreset.
                            // Version filter already applied in SQL via pkg.is_newest — tally all matching rows.
                            for (int i = 0; i < rows.Count; i++)
                                if (ClothingPackedAttrMatchesSubfilter(rows[i].Attr, chip))
                                    varCounts[ci] += rows[i].Cnt;
                        }
                    }

                    // Gate per chip via PassesClothingGalleryFiltersForPath to match what the grid shows for loose entries.
                    int[] looseCounts = new int[chips.Length];
                    if (sourceMode != 2) // skip loose when source=VAR
                    {
                        using (var st = conn.Prepare(
                            "SELECT internal_path FROM loose_cat_mem WHERE category='Clothing'"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string loosePath = st.ColumnText(0) ?? "";
                                for (int ci = 0; ci < chips.Length; ci++)
                                {
                                    if (GalleryPanel.PassesClothingGalleryFiltersForPath(loosePath, chips[ci], false))
                                        looseCounts[ci]++;
                                }
                            }
                        }
                    }

                    counts.Default     = varCounts[0] + looseCounts[0];
                    counts.Real        = varCounts[1] + looseCounts[1];
                    counts.Presets     = varCounts[2] + looseCounts[2];
                    counts.Custom      = varCounts[3] + looseCounts[3];
                    counts.CustomPreset= varCounts[4] + looseCounts[4];
                    counts.Items       = varCounts[5] + looseCounts[5];
                    counts.Male        = varCounts[6] + looseCounts[6];
                    counts.Female      = varCounts[7] + looseCounts[7];
                    counts.Decals      = varCounts[8] + looseCounts[8];
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryQueryClothingChipCounts failed: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
