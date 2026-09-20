using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        internal const int SearchRescueCountCap = 500;

        internal static bool TryCountGalleryCategoryRows(
            string categoryTitle,
            string currentExtension,
            string creatorFilter,
            GallerySearchQuery searchQuery,
            GalleryPanel.ClothingSubfilter clothingSubfilterForSql,
            GalleryPanel.HairSubfilter hairSubfilterForSql,
            GalleryPanel.SceneHubSubfilter sceneHubSubfilterForSql,
            int loadedState,
            List<string> pathExclusions,
            List<string> pathInclusions,
            HashSet<string> activeTags,
            HashSet<string> activeUserTags,
            bool userTagsUntaggedOnly,
            bool userTagsRequireAll,
            HashSet<string> excludedUserTags,
            int pkgVersionFilter,
            bool userTagsTaggedOnly,
            string licenseFilter,
            List<string> everythingCategoryScope,
            int cap,
            out int count)
        {
            count = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(categoryTitle)) return false;
            if (s_RebuildRunning) return false;
            if (cap <= 0) cap = SearchRescueCountCap;

            lock (s_Sync)
            {
                if (string.IsNullOrEmpty(s_ReadyCategoriesSig)) return false;
            }

            try
            {
                Gallery g = Gallery.singleton;
                if (g == null) return false;
                Gallery.Category catDef = g.FindCategoryByName(categoryTitle);
                if (string.IsNullOrEmpty(catDef.name)) return false;
                if (!ExtensionSetsEqual(catDef.extension ?? "", currentExtension ?? "")) return false;

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    var ctx = BuildGalleryCategoryWhere(
                        conn, categoryTitle, creatorFilter, loadedState,
                        searchQuery ?? GallerySearchQuery.Empty, pathExclusions, pathInclusions,
                        activeTags, activeUserTags, userTagsUntaggedOnly, userTagsRequireAll, excludedUserTags,
                        pkgVersionFilter, userTagsTaggedOnly, licenseFilter);

                    string clothSqlAnd = BuildClothingSubfilterSqlAnd(conn, categoryTitle, clothingSubfilterForSql);
                    string hairSqlAnd = BuildHairSubfilterSqlAnd(conn, categoryTitle, hairSubfilterForSql, true);
                    string sceneHubSqlAnd = BuildSceneHubSubfilterSqlAnd(conn, categoryTitle, sceneHubSubfilterForSql);

                    var sbSql = new StringBuilder(512);
                    sbSql.Append("SELECT COUNT(*) FROM (SELECT ");
                    sbSql.Append(ctx.IsEverything ? "DISTINCT m.pkg_uid, m.internal_path" : "1");
                    sbSql.Append(" FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid WHERE ");
                    if (ctx.IsEverything)
                        sbSql.Append("1=1").Append(BuildEverythingNonPreviewAnd("m.internal_path"))
                             .Append(BuildEverythingCategoryScopeAnd(true, everythingCategoryScope));
                    else
                        sbSql.Append("m.category = ?");
                    sbSql.Append(ctx.CreatorAndFragment);
                    sbSql.Append(clothSqlAnd);
                    sbSql.Append(hairSqlAnd);
                    sbSql.Append(sceneHubSqlAnd);
                    sbSql.Append(ctx.LoadedAndFragment).Append(ctx.VersionAndFragment).Append(ctx.LicenseAndFragment)
                         .Append(ctx.NameAndFragment)
                         .Append(ctx.SearchTimeAndFragment)
                         .Append(ctx.ExclusionAndFragment).Append(ctx.InclusionAndFragment)
                         .Append(ctx.TagAndFragment).Append(ctx.UserTagAndFragment)
                         .Append(ctx.ExcludedUserTagAndFragment);
                    sbSql.Append(" LIMIT ").Append(cap).Append(")");

                    string sql = sbSql.ToString();
                    if (CountSqlPlaceholders(sql) != CountGalleryCategoryWhereBinds(ctx)) return false;

                    using (var stmt = conn.Prepare(sql))
                    {
                        BindGalleryCategoryWhere(stmt, ctx, 1);
                        if (stmt.Step() != VpbSqlite3.SqliteRow) return false;
                        count = (int)stmt.ColumnInt64(0);
                    }
                }
                return true;
            }
            catch
            {
                count = 0;
                return false;
            }
        }
    }
}
