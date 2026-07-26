using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    [Serializable]
    public class QuickFilterEntry
    {
        public string Name;
        public string CategoryPath;
        public string CategoryTitle;
        public string SearchText;
        public string Creator;
        public List<string> Tags = new List<string>();
        public List<string> UserTags = new List<string>();
        public List<string> ExcludedUserTags = new List<string>();
        /// <summary><see cref="UserTagAvailMode"/> as int (0=tag, 1=filter by tags, 2=untagged only).</summary>
        public int UserTagAvailFilterMode = 0;
        public int UserTagInheritVarToChildren = 0;
        public string SceneSourceFilter = "";
        public string AppearanceSourceFilter = "";
        public string PackagePathFilter = "";
        public int ClothingSubfilter = 0;
        public int HairSubfilter = 0;
        public int AppearanceSubfilter = 0;
        public int PosePeopleFilter = 0;
        public SortState SortState;
        public int BrowseHiddenMode = 0;
        public int BrowseAlwaysLoadedMode = 0;
        public int BrowseOldVersionsMode = 0;
        public int BrowseLoadedMode = 0;
        public int BrowseUnusedMode = 0;

        /// <summary>True when side-tab layout was captured with this preset (distinguishes legacy presets).</summary>
        public bool HasSideTabState = false;
        /// <summary><see cref="ContentType"/> as int, or -1 when that side panel was closed.</summary>
        public int LeftActiveContent = -1;
        public int RightActiveContent = -1;
        public string CategorySideFilter = "";
        public string CreatorSideFilter = "";
        public string UserTagSideFilter = "";
        public string PathSideFilter = "";
        public string HistoryTabFilter = "";
        public string TagSubPaneFilter = "";
        public int HistoryFilterMode = 0;
        public List<QuickFilterSideTabSortEntry> SideTabSortStates = new List<QuickFilterSideTabSortEntry>();
        
        // Visual customization
        public Color ButtonColor = UI.ChromePanel;
        public Color TextColor = Color.white;

        public QuickFilterEntry() { }

        public JSONNode ToJSON()
        {
            var node = new JSONClass();
            node["Name"] = Name;
            node["CategoryPath"] = CategoryPath;
            node["CategoryTitle"] = CategoryTitle ?? "";
            node["SearchText"] = SearchText;
            node["Creator"] = Creator;
            
            var tagsArr = new JSONArray();
            foreach (var t in Tags) tagsArr.Add(t);
            node["Tags"] = tagsArr;

            var userTagsArr = new JSONArray();
            if (UserTags != null)
                foreach (var t in UserTags) userTagsArr.Add(t);
            node["UserTags"] = userTagsArr;

            var excludedUserTagsArr = new JSONArray();
            if (ExcludedUserTags != null)
                foreach (var t in ExcludedUserTags) excludedUserTagsArr.Add(t);
            node["ExcludedUserTags"] = excludedUserTagsArr;

            node["UserTagAvailFilterMode"].AsInt = UserTagAvailFilterMode;
            node["UserTagInheritVarToChildren"].AsInt = UserTagInheritVarToChildren;
            node["SceneSourceFilter"] = SceneSourceFilter ?? "";
            node["AppearanceSourceFilter"] = AppearanceSourceFilter ?? "";
            node["PackagePathFilter"] = PackagePathFilter ?? "";
            node["ClothingSubfilter"].AsInt = ClothingSubfilter;
            node["HairSubfilter"].AsInt = HairSubfilter;
            node["AppearanceSubfilter"].AsInt = AppearanceSubfilter;
            node["PosePeopleFilter"].AsInt = PosePeopleFilter;
            node["BrowseHiddenMode"].AsInt = BrowseHiddenMode;
            node["BrowseAlwaysLoadedMode"].AsInt = BrowseAlwaysLoadedMode;
            node["BrowseOldVersionsMode"].AsInt = BrowseOldVersionsMode;
            node["BrowseLoadedMode"].AsInt = BrowseLoadedMode;
            node["BrowseUnusedMode"].AsInt = BrowseUnusedMode;

            if (SortState != null)
            {
                var sortNode = new JSONClass();
                sortNode["Type"].AsInt = (int)SortState.Type;
                sortNode["Direction"].AsInt = (int)SortState.Direction;
                node["SortState"] = sortNode;
            }

            node["HasSideTabState"].AsBool = HasSideTabState;
            node["LeftActiveContent"].AsInt = LeftActiveContent;
            node["RightActiveContent"].AsInt = RightActiveContent;
            node["CategorySideFilter"] = CategorySideFilter ?? "";
            node["CreatorSideFilter"] = CreatorSideFilter ?? "";
            node["UserTagSideFilter"] = UserTagSideFilter ?? "";
            node["PathSideFilter"] = PathSideFilter ?? "";
            node["HistoryTabFilter"] = HistoryTabFilter ?? "";
            node["TagSubPaneFilter"] = TagSubPaneFilter ?? "";
            node["HistoryFilterMode"].AsInt = HistoryFilterMode;

            var sideSortArr = new JSONArray();
            if (SideTabSortStates != null)
            {
                foreach (var s in SideTabSortStates)
                {
                    if (s == null || string.IsNullOrEmpty(s.Context) || s.SortState == null) continue;
                    var sn = new JSONClass();
                    sn["Context"] = s.Context;
                    sn["Type"].AsInt = (int)s.SortState.Type;
                    sn["Direction"].AsInt = (int)s.SortState.Direction;
                    sideSortArr.Add(sn);
                }
            }
            node["SideTabSortStates"] = sideSortArr;

            // Colors
            node["ButtonColor"] = ColorToHex(ButtonColor);
            node["TextColor"] = ColorToHex(TextColor);

            return node;
        }

        public static QuickFilterEntry FromJSON(JSONNode node)
        {
            var entry = new QuickFilterEntry();
            entry.Name = node["Name"] ?? "New Filter";
            entry.CategoryPath = node["CategoryPath"] ?? "";
            entry.CategoryTitle = node["CategoryTitle"] ?? "";
            entry.SearchText = node["SearchText"] ?? "";
            entry.Creator = node["Creator"] ?? "";

            var tagsArr = node["Tags"].AsArray;
            if (tagsArr != null)
            {
                foreach (JSONNode t in tagsArr) entry.Tags.Add(t);
            }

            var userTagsArr = node["UserTags"].AsArray;
            if (userTagsArr != null)
            {
                foreach (JSONNode t in userTagsArr)
                    if (!string.IsNullOrEmpty(t)) entry.UserTags.Add(t);
            }

            var excludedUserTagsArr = node["ExcludedUserTags"].AsArray;
            if (excludedUserTagsArr != null)
            {
                foreach (JSONNode t in excludedUserTagsArr)
                    if (!string.IsNullOrEmpty(t)) entry.ExcludedUserTags.Add(t);
            }

            entry.UserTagAvailFilterMode = node["UserTagAvailFilterMode"] != null ? node["UserTagAvailFilterMode"].AsInt : 0;
            entry.UserTagInheritVarToChildren = node["UserTagInheritVarToChildren"] != null ? node["UserTagInheritVarToChildren"].AsInt : 0;
            entry.SceneSourceFilter = node["SceneSourceFilter"] ?? "";
            entry.AppearanceSourceFilter = node["AppearanceSourceFilter"] ?? "";
            entry.PackagePathFilter = node["PackagePathFilter"] ?? "";
            entry.ClothingSubfilter = node["ClothingSubfilter"] != null ? node["ClothingSubfilter"].AsInt : 0;
            entry.HairSubfilter = node["HairSubfilter"] != null ? node["HairSubfilter"].AsInt : 0;
            entry.AppearanceSubfilter = node["AppearanceSubfilter"] != null ? node["AppearanceSubfilter"].AsInt : 0;
            entry.PosePeopleFilter = node["PosePeopleFilter"] != null ? node["PosePeopleFilter"].AsInt : 0;
            if (node["BrowseHiddenMode"] != null || node["BrowseAlwaysLoadedMode"] != null || node["BrowseOldVersionsMode"] != null || node["BrowseLoadedMode"] != null || node["BrowseUnusedMode"] != null)
            {
                entry.BrowseHiddenMode = node["BrowseHiddenMode"] != null ? node["BrowseHiddenMode"].AsInt : 0;
                entry.BrowseAlwaysLoadedMode = node["BrowseAlwaysLoadedMode"] != null ? node["BrowseAlwaysLoadedMode"].AsInt : 0;
                entry.BrowseOldVersionsMode = node["BrowseOldVersionsMode"] != null ? node["BrowseOldVersionsMode"].AsInt : 0;
                entry.BrowseLoadedMode = node["BrowseLoadedMode"] != null ? node["BrowseLoadedMode"].AsInt : 0;
                entry.BrowseUnusedMode = node["BrowseUnusedMode"] != null ? node["BrowseUnusedMode"].AsInt : 0;
            }
            else
            {
                // Legacy bool only-flags
                if (node["BrowseHiddenOnly"] != null && node["BrowseHiddenOnly"].AsInt != 0)
                    entry.BrowseHiddenMode = 2;
                if (node["BrowseAlwaysLoadedOnly"] != null && node["BrowseAlwaysLoadedOnly"].AsInt != 0)
                    entry.BrowseAlwaysLoadedMode = 2;
            }

            var sortNode = node["SortState"];
            if (sortNode != null)
            {
                int ti = sortNode["Type"].AsInt;
                int di = sortNode["Direction"].AsInt;
                if (Enum.IsDefined(typeof(SortType), ti) && Enum.IsDefined(typeof(SortDirection), di))
                    entry.SortState = new SortState((SortType)ti, (SortDirection)di);
            }

            // Legacy exclusive sort → browse filter cycles
            if (entry.SortState != null)
            {
                if (entry.SortState.Type == SortType.HiddenOnly)
                {
                    entry.BrowseHiddenMode = 2;
                    entry.SortState.Type = SortType.Name;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
                else if (entry.SortState.Type == SortType.AutoInstallOnly)
                {
                    entry.BrowseAlwaysLoadedMode = 2;
                    entry.SortState.Type = SortType.Name;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
                else if (entry.SortState.Type == SortType.LoadedOnly)
                {
                    entry.BrowseLoadedMode = 1;
                    entry.SortState.Type = SortType.Name;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
                else if (entry.SortState.Type == SortType.UnloadedOnly)
                {
                    entry.BrowseLoadedMode = 2;
                    entry.SortState.Type = SortType.Name;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
                else if (entry.SortState.Type == SortType.Hidden)
                {
                    if (entry.BrowseHiddenMode == 0) entry.BrowseHiddenMode = 1;
                    entry.SortState.Type = SortType.Name;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
                else if (entry.SortState.Type == SortType.UnusedOnly)
                {
                    entry.BrowseUnusedMode = 2;
                    entry.SortState.Type = SortType.UsageCount;
                    entry.SortState.Direction = SortDirection.Ascending;
                }
            }

            entry.HasSideTabState = node["HasSideTabState"] != null && node["HasSideTabState"].AsBool;
            entry.LeftActiveContent = node["LeftActiveContent"] != null ? node["LeftActiveContent"].AsInt : -1;
            entry.RightActiveContent = node["RightActiveContent"] != null ? node["RightActiveContent"].AsInt : -1;
            entry.CategorySideFilter = node["CategorySideFilter"] ?? "";
            entry.CreatorSideFilter = node["CreatorSideFilter"] ?? "";
            entry.UserTagSideFilter = node["UserTagSideFilter"] ?? "";
            entry.PathSideFilter = node["PathSideFilter"] ?? "";
            entry.HistoryTabFilter = node["HistoryTabFilter"] ?? "";
            entry.TagSubPaneFilter = node["TagSubPaneFilter"] ?? "";
            entry.HistoryFilterMode = node["HistoryFilterMode"] != null ? node["HistoryFilterMode"].AsInt : 0;

            var sideSortArr = node["SideTabSortStates"].AsArray;
            if (sideSortArr != null)
            {
                foreach (JSONNode sn in sideSortArr)
                {
                    if (sn == null) continue;
                    string ctx = sn["Context"] ?? "";
                    if (string.IsNullOrEmpty(ctx)) continue;
                    int ti = sn["Type"].AsInt;
                    int di = sn["Direction"].AsInt;
                    if (!Enum.IsDefined(typeof(SortType), ti) || !Enum.IsDefined(typeof(SortDirection), di)) continue;
                    entry.SideTabSortStates.Add(new QuickFilterSideTabSortEntry
                    {
                        Context = ctx,
                        SortState = new SortState((SortType)ti, (SortDirection)di)
                    });
                }
            }

            if (node["ButtonColor"] != null) entry.ButtonColor = HexToColor(node["ButtonColor"]);
            if (node["TextColor"] != null) entry.TextColor = HexToColor(node["TextColor"]);

            return entry;
        }

        public static string ColorToHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        public static Color HexToColor(string hex)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
            return Color.white;
        }
    }

    [Serializable]
    public class QuickFilterSideTabSortEntry
    {
        public string Context;
        public SortState SortState;
    }

    public class QuickFilterSettings
    {
        private static QuickFilterSettings _instance;
        public static QuickFilterSettings Instance
        {
            get
            {
                if (_instance == null) _instance = new QuickFilterSettings();
                return _instance;
            }
        }

        public List<QuickFilterEntry> Filters = new List<QuickFilterEntry>();
        private string filePath;

        public QuickFilterSettings()
        {
            string cacheDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            filePath = Path.Combine(cacheDir, "quick_filters.json");
            Load();
        }

        public void AddFilter(QuickFilterEntry entry)
        {
            Filters.Add(entry);
            Save();
        }

        public void RemoveFilter(QuickFilterEntry entry)
        {
            if (Filters.Contains(entry))
            {
                Filters.Remove(entry);
                Save();
            }
        }

        public void RenameFilter(QuickFilterEntry entry, string newName)
        {
            if (entry != null && !string.IsNullOrEmpty(newName))
            {
                entry.Name = newName;
                Save();
            }
        }
        
        public void MoveFilter(QuickFilterEntry entry, int direction)
        {
            int index = Filters.IndexOf(entry);
            if (index < 0) return;
            
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < Filters.Count)
            {
                Filters.RemoveAt(index);
                Filters.Insert(newIndex, entry);
                Save();
            }
        }

        public void Load()
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var root = JSON.Parse(json);
                var arr = root.AsArray;
                
                Filters.Clear();
                foreach (JSONNode node in arr)
                {
                    Filters.Add(QuickFilterEntry.FromJSON(node));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to load quick filters: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                var arr = new JSONArray();
                foreach (var f in Filters) arr.Add(f.ToJSON());
                File.WriteAllText(filePath, VPB.src.util.JsonSerializationUtil.Serialize(arr, 4096));
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to save quick filters: " + ex.Message);
            }
        }
    }
}
