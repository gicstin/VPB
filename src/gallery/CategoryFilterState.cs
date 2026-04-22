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
        public string SceneSourceFilter = "";
        public string AppearanceSourceFilter = "";
        public string PackagePathFilter = "";
        public int ClothingSubfilter = 0;
        public int AppearanceSubfilter = 0;
        public int PosePeopleFilter = 0;
        public SortState FileSortState = null;

        public string ToJson()
        {
            var node = new JSONClass();
            node["n"] = NameFilter ?? "";
            node["c"] = Creator ?? "";
            node["ss"] = SceneSourceFilter ?? "";
            node["as"] = AppearanceSourceFilter ?? "";
            node["pp"] = PackagePathFilter ?? "";
            node["csf"].AsInt = ClothingSubfilter;
            node["asf"].AsInt = AppearanceSubfilter;
            node["ppf"].AsInt = PosePeopleFilter;

            var tagsArr = new JSONArray();
            if (Tags != null)
                foreach (var t in Tags) tagsArr.Add(t);
            node["tags"] = tagsArr;

            if (FileSortState != null)
            {
                var sortNode = new JSONClass();
                sortNode["t"].AsInt = (int)FileSortState.Type;
                sortNode["d"].AsInt = (int)FileSortState.Direction;
                node["sort"] = sortNode;
            }

            return node.ToString();
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
                s.SceneSourceFilter = node["ss"] ?? "";
                s.AppearanceSourceFilter = node["as"] ?? "";
                s.PackagePathFilter = node["pp"] ?? "";
                s.ClothingSubfilter = node["csf"].AsInt;
                s.AppearanceSubfilter = node["asf"].AsInt;
                s.PosePeopleFilter = node["ppf"].AsInt;

                var tagsArr = node["tags"].AsArray;
                if (tagsArr != null)
                    foreach (JSONNode t in tagsArr)
                        if (!string.IsNullOrEmpty(t)) s.Tags.Add(t);

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
