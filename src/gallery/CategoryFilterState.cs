using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    internal class CategoryFilterState
    {
        public string NameFilter = "";
        public string Creator = "";
        public List<string> Tags = new List<string>();
        public List<string> UserTags = new List<string>();
        /// <summary>Excluded (none-of) user tags in filter-by-tags mode.</summary>
        public List<string> ExcludedUserTags = new List<string>();
        /// <summary><see cref="UserTagAvailMode"/> as int (0=tag, 1=filter by tags, 2=untagged only). Legacy: 1 meant filter-by-tags only.</summary>
        public int UserTagAvailFilterMode = 0;
        /// <summary>1 when ALL VAR user-tag apply/removal also propagates to child items inside VAR.</summary>
        public int UserTagInheritVarToChildren = 0;
        public string SceneSourceFilter = "";
        public string AppearanceSourceFilter = "";
        public string PackagePathFilter = "";
        public int ClothingSubfilter = 0;
        public int HairSubfilter = 0;
        public int AppearanceSubfilter = 0;
        public int PosePeopleFilter = 0;
        public SortState FileSortState = null;

        // Maps legacy source-filter string values to the binary toggle the global-filter
        // design now uses: "" means toggle OFF (global filter applies), "local" means toggle ON
        // (override to local-only on this category).
        private static string MigrateLegacySourceFilter(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (string.Equals(raw, "local", StringComparison.OrdinalIgnoreCase)) return "local";
            if (string.Equals(raw, "Custom Scenes", StringComparison.OrdinalIgnoreCase)) return "local";
            if (string.Equals(raw, "custom", StringComparison.OrdinalIgnoreCase)) return "local";
            return "";
        }

        public string ToJson()
        {
            var node = new JSONClass();
            node["n"] = NameFilter ?? "";
            node["c"] = Creator ?? "";
            node["ss"] = SceneSourceFilter ?? "";
            node["as"] = AppearanceSourceFilter ?? "";
            node["pp"] = PackagePathFilter ?? "";
            node["csf"].AsInt = ClothingSubfilter;
            node["hsf"].AsInt = HairSubfilter;
            node["asf"].AsInt = AppearanceSubfilter;
            node["ppf"].AsInt = PosePeopleFilter;

            var tagsArr = new JSONArray();
            if (Tags != null)
                foreach (var t in Tags) tagsArr.Add(t);
            node["tags"] = tagsArr;

            var utArr = new JSONArray();
            if (UserTags != null)
                foreach (var t in UserTags) utArr.Add(t);
            node["utags"] = utArr;
            var xutArr = new JSONArray();
            if (ExcludedUserTags != null)
                foreach (var t in ExcludedUserTags) xutArr.Add(t);
            node["xutags"] = xutArr;
            node["utfm"].AsInt = UserTagAvailFilterMode;
            node["utin"].AsInt = UserTagInheritVarToChildren;

            if (FileSortState != null)
            {
                var sortNode = new JSONClass();
                sortNode["t"].AsInt = (int)FileSortState.Type;
                sortNode["d"].AsInt = (int)FileSortState.Direction;
                node["sort"] = sortNode;
            }

            return VPB.src.util.JsonSerializationUtil.Serialize(node, 4096);
        }

        public static CategoryFilterState FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var node = JSON.Parse(json);
                if (node == null) return null;

                var s = new CategoryFilterState();
                s.NameFilter = node["n"] ?? "";
                s.Creator = node["c"] ?? "";
                s.SceneSourceFilter = MigrateLegacySourceFilter(node["ss"] ?? "");
                s.AppearanceSourceFilter = MigrateLegacySourceFilter(node["as"] ?? "");
                s.PackagePathFilter = node["pp"] ?? "";
                s.ClothingSubfilter = node["csf"].AsInt;
                s.HairSubfilter = node["hsf"] != null ? node["hsf"].AsInt : 0;
                s.AppearanceSubfilter = node["asf"].AsInt;
                s.PosePeopleFilter = node["ppf"].AsInt;

                var tagsArr = node["tags"].AsArray;
                if (tagsArr != null)
                    foreach (JSONNode t in tagsArr)
                        if (!string.IsNullOrEmpty(t)) s.Tags.Add(t);

                var utArr = node["utags"].AsArray;
                if (utArr != null)
                    foreach (JSONNode t in utArr)
                        if (!string.IsNullOrEmpty(t)) s.UserTags.Add(t);

                var xutArr = node["xutags"].AsArray;
                if (xutArr != null)
                    foreach (JSONNode t in xutArr)
                        if (!string.IsNullOrEmpty(t)) s.ExcludedUserTags.Add(t);

                s.UserTagAvailFilterMode = node["utfm"] != null ? node["utfm"].AsInt : 0;
                s.UserTagInheritVarToChildren = node["utin"] != null ? node["utin"].AsInt : 0;

                var sortNode = node["sort"];
                if (sortNode != null && sortNode.Count > 0)
                {
                    int ti = sortNode["t"].AsInt;
                    int di = sortNode["d"].AsInt;
                    if (Enum.IsDefined(typeof(SortType), ti) && Enum.IsDefined(typeof(SortDirection), di))
                        s.FileSortState = new SortState((SortType)ti, (SortDirection)di);
                }

                return s;
            }
            catch { return null; }
        }
    }
}
