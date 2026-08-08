using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    [Serializable]
    public partial class QuickFilterEntry
    {
        /// <summary>SQLite row id; 0 = not yet assigned.</summary>
        public int Id;
        public string Name;
        /// <summary>Pinned presets appear as one-click randomize actions in title-bar overflow.</summary>
        public bool Pinned;
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
        /// <summary>
        /// Title-bar Source filter: 0=All, 1=Local, 2=Var (<see cref="VPBConfig.GlobalSourceFilterValue"/>).
        /// Legacy presets without this field migrate from Scene/Appearance "local" → Local, else All.
        /// </summary>
        public int GlobalSourceFilter = 0;
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
        public int BrowseOldVersionsMode = 1;
        public int BrowseLoadedMode = 0;
        public int BrowseUnusedMode = 0;
        /// <summary>Title-bar Filter license type. Empty = off.</summary>
        public string LicenseFilter = "";

        /// <summary>
        /// Embedded leaf snapshots for a merged multi-random preset.
        /// Browse applies OR-combined leaf filters; dice applies each member in order.
        /// Null/empty = normal single preset.
        /// </summary>
        public List<QuickFilterEntry> MergeMembers;

        /// <summary>True when this preset randomizes multiple leaf filter sets in sequence. Browse applies OR-combined leaves.</summary>
        public bool IsMerged
        {
            get { return MergeMembers != null && MergeMembers.Count >= 2; }
        }

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
            node["Id"].AsInt = Id;
            node["Name"] = Name;
            node["Pinned"].AsBool = Pinned;
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
            node["GlobalSourceFilter"].AsInt = ClampGlobalSourceFilter(GlobalSourceFilter);
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
            node["LicenseFilter"] = LicenseFilter ?? "";

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

            if (MergeMembers != null && MergeMembers.Count > 0)
            {
                var mergeArr = new JSONArray();
                for (int i = 0; i < MergeMembers.Count; i++)
                {
                    QuickFilterEntry m = MergeMembers[i];
                    if (m == null) continue;
                    // Leaves only — never nest merges in serialized members.
                    QuickFilterEntry leaf = CloneLeafSnapshot(m);
                    if (leaf != null) mergeArr.Add(leaf.ToJSON());
                }
                if (mergeArr.Count > 0)
                    node["MergeMembers"] = mergeArr;
            }

            return node;
        }

        /// <summary>Deep-ish clone via JSON for merge snapshots (strips nested MergeMembers).</summary>
        public static QuickFilterEntry CloneLeafSnapshot(QuickFilterEntry src)
        {
            if (src == null) return null;
            try
            {
                // Avoid recursion through MergeMembers during leaf clone.
                List<QuickFilterEntry> keep = src.MergeMembers;
                src.MergeMembers = null;
                JSONNode n = src.ToJSON();
                src.MergeMembers = keep;
                QuickFilterEntry clone = FromJSON(n);
                if (clone != null)
                {
                    clone.Id = 0;
                    clone.Pinned = false;
                    clone.MergeMembers = null;
                }
                return clone;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Flatten merge sources into leaf snapshots (cap). Nested merges expand.
        /// </summary>
        public static void CollectMergeLeaves(QuickFilterEntry src, List<QuickFilterEntry> into, int maxLeaves)
        {
            if (src == null || into == null || maxLeaves <= 0) return;
            if (into.Count >= maxLeaves) return;

            if (src.IsMerged)
            {
                for (int i = 0; i < src.MergeMembers.Count; i++)
                {
                    if (into.Count >= maxLeaves) return;
                    CollectMergeLeaves(src.MergeMembers[i], into, maxLeaves);
                }
                return;
            }

            QuickFilterEntry leaf = CloneLeafSnapshot(src);
            if (leaf != null) into.Add(leaf);
        }

        // Warm browse-apply for merged presets — reuse buffer (Unity scripting / GC).
        private static readonly StringBuilder s_MergeBrowseSb = new StringBuilder(128);

        /// <summary>
        /// Browse snapshot from merge leaves: OR of each leaf's search/tags/creator (title-search
        /// branches), union of user tags, shared session filters. Not IsMerged — apply as one filter.
        /// Dice still walks leaves via <see cref="CollectMergeLeaves"/>.
        /// </summary>
        public static QuickFilterEntry BuildCombinedBrowseEntry(IList<QuickFilterEntry> leaves)
        {
            if (leaves == null || leaves.Count == 0) return null;
            QuickFilterEntry first = null;
            for (int i = 0; i < leaves.Count; i++)
            {
                if (leaves[i] != null) { first = leaves[i]; break; }
            }
            if (first == null) return null;
            if (leaves.Count == 1) return CloneLeafSnapshot(first);

            QuickFilterEntry dest = CloneLeafSnapshot(first);
            if (dest == null) return null;
            dest.MergeMembers = null;
            dest.Id = 0;
            dest.Pinned = false;

            bool sameCategory = true;
            string catPath = first.CategoryPath ?? "";
            string catTitle = first.CategoryTitle ?? "";
            for (int i = 0; i < leaves.Count; i++)
            {
                QuickFilterEntry L = leaves[i];
                if (L == null) continue;
                if (!string.Equals(L.CategoryPath ?? "", catPath, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(catTitle)
                        && !string.IsNullOrEmpty(L.CategoryTitle)
                        && !string.Equals(L.CategoryTitle, catTitle, StringComparison.OrdinalIgnoreCase)))
                {
                    sameCategory = false;
                    break;
                }
            }

            // OR-combine per-leaf clauses into title search (AND within leaf, OR across leaves).
            s_MergeBrowseSb.Length = 0;
            bool anyClause = false;
            for (int i = 0; i < leaves.Count; i++)
            {
                QuickFilterEntry L = leaves[i];
                if (L == null) continue;
                string clause = BuildLeafBrowseSearchClause(L);
                if (string.IsNullOrEmpty(clause)) continue;
                if (anyClause) s_MergeBrowseSb.Append(" OR ");
                s_MergeBrowseSb.Append(clause);
                anyClause = true;
            }
            dest.SearchText = anyClause ? s_MergeBrowseSb.ToString() : "";
            // Package tags live in OR search as #tag — clear AND side-tab tags.
            if (dest.Tags != null) dest.Tags.Clear();
            else dest.Tags = new List<string>();
            dest.Creator = "";

            // User tags: union (Compound = any; Isolate = all — global setting).
            var utSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var xutSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dest.UserTags = new List<string>();
            dest.ExcludedUserTags = new List<string>();
            int utMode = first.UserTagAvailFilterMode;
            int utInherit = first.UserTagInheritVarToChildren;
            for (int i = 0; i < leaves.Count; i++)
            {
                QuickFilterEntry L = leaves[i];
                if (L == null) continue;
                if (L.UserTagAvailFilterMode == 2 || L.UserTagAvailFilterMode == 3)
                    utMode = L.UserTagAvailFilterMode; // presence browse wins if any leaf
                else if (utMode != 2 && utMode != 3 && L.UserTagAvailFilterMode == 1) utMode = 1;
                if (L.UserTagInheritVarToChildren != 0) utInherit = 1;
                AppendUniqueStrings(dest.UserTags, utSeen, L.UserTags);
                AppendUniqueStrings(dest.ExcludedUserTags, xutSeen, L.ExcludedUserTags);
            }
            dest.UserTagAvailFilterMode = utMode;
            dest.UserTagInheritVarToChildren = utInherit;

            // Session filters: agree → keep; else least restrictive / first.
            dest.GlobalSourceFilter = AgreeIntField(leaves, 0, 0);
            dest.PackagePathFilter = AgreePackagePath(leaves);
            dest.LicenseFilter = AgreeLicense(leaves);
            dest.BrowseHiddenMode = AgreeIntField(leaves, 1, 0);
            dest.BrowseAlwaysLoadedMode = AgreeIntField(leaves, 2, 0);
            dest.BrowseOldVersionsMode = AgreeIntField(leaves, 3, 1);
            dest.BrowseLoadedMode = AgreeIntField(leaves, 4, 0);
            dest.BrowseUnusedMode = AgreeIntField(leaves, 5, 0);

            if (sameCategory)
            {
                dest.ClothingSubfilter = AgreeIntField(leaves, 6, 0);
                dest.HairSubfilter = AgreeIntField(leaves, 7, 0);
                dest.AppearanceSubfilter = AgreeIntField(leaves, 8, 0);
                dest.PosePeopleFilter = AgreeIntField(leaves, 9, 0);
            }
            else
            {
                // Multi-category browse: do not apply one leaf's region subfilter to others.
                dest.ClothingSubfilter = 0;
                dest.HairSubfilter = 0;
                dest.AppearanceSubfilter = 0;
                dest.PosePeopleFilter = 0;
            }

            dest.SceneSourceFilter = "";
            dest.AppearanceSourceFilter = "";
            return dest;
        }

        private static string BuildLeafBrowseSearchClause(QuickFilterEntry leaf)
        {
            if (leaf == null) return "";
            // Local SB — must not touch s_MergeBrowseSb (caller joins OR clauses there).
            var sb = new StringBuilder(64);
            string search = leaf.SearchText != null ? leaf.SearchText.Trim() : "";
            if (search.Length > 0) sb.Append(search);

            if (leaf.Tags != null)
            {
                for (int i = 0; i < leaf.Tags.Count; i++)
                {
                    string t = leaf.Tags[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append('#');
                    sb.Append(t.Trim());
                }
            }

            if (!string.IsNullOrEmpty(leaf.Creator))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('@');
                sb.Append(leaf.Creator.Trim());
            }

            return sb.ToString();
        }

        private static void AppendUniqueStrings(List<string> dest, HashSet<string> seen, List<string> src)
        {
            if (dest == null || seen == null || src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                string t = src[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (!seen.Add(t)) continue;
                dest.Add(t);
            }
        }

        /// <summary>field: 0=GlobalSource 1=BrowseHidden 2=AlwaysLoaded 3=OldVersions 4=Loaded 5=Unused 6=Clothing 7=Hair 8=Appearance 9=PosePeople</summary>
        private static int AgreeIntField(IList<QuickFilterEntry> leaves, int field, int fallback)
        {
            bool have = false;
            int v = fallback;
            for (int i = 0; i < leaves.Count; i++)
            {
                QuickFilterEntry L = leaves[i];
                if (L == null) continue;
                int cur;
                switch (field)
                {
                    case 0: cur = L.GlobalSourceFilter; break;
                    case 1: cur = L.BrowseHiddenMode; break;
                    case 2: cur = L.BrowseAlwaysLoadedMode; break;
                    case 3: cur = L.BrowseOldVersionsMode; break;
                    case 4: cur = L.BrowseLoadedMode; break;
                    case 5: cur = L.BrowseUnusedMode; break;
                    case 6: cur = L.ClothingSubfilter; break;
                    case 7: cur = L.HairSubfilter; break;
                    case 8: cur = L.AppearanceSubfilter; break;
                    case 9: cur = L.PosePeopleFilter; break;
                    default: cur = fallback; break;
                }
                if (!have) { v = cur; have = true; continue; }
                if (cur != v) return fallback;
            }
            return have ? v : fallback;
        }

        private static string AgreePackagePath(IList<QuickFilterEntry> leaves)
        {
            return AgreeStringField(leaves, true);
        }

        private static string AgreeLicense(IList<QuickFilterEntry> leaves)
        {
            return AgreeStringField(leaves, false);
        }

        private static string AgreeStringField(IList<QuickFilterEntry> leaves, bool packagePath)
        {
            bool have = false;
            string v = "";
            for (int i = 0; i < leaves.Count; i++)
            {
                QuickFilterEntry L = leaves[i];
                if (L == null) continue;
                string cur = packagePath ? (L.PackagePathFilter ?? "") : (L.LicenseFilter ?? "");
                if (!have) { v = cur; have = true; continue; }
                if (!string.Equals(cur, v, StringComparison.OrdinalIgnoreCase)) return "";
            }
            return have ? v : "";
        }

        public static QuickFilterEntry FromJSON(JSONNode node)
        {
            var entry = new QuickFilterEntry();
            entry.Id = node["Id"] != null ? node["Id"].AsInt : 0;
            entry.Name = node["Name"] ?? "New Filter";
            entry.Pinned = node["Pinned"] != null && node["Pinned"].AsBool;
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
            if (node["GlobalSourceFilter"] != null)
                entry.GlobalSourceFilter = ClampGlobalSourceFilter(node["GlobalSourceFilter"].AsInt);
            else if (IsLegacyLocalSourceFilter(entry.SceneSourceFilter)
                     || IsLegacyLocalSourceFilter(entry.AppearanceSourceFilter))
                entry.GlobalSourceFilter = (int)VPBConfig.GlobalSourceFilterValue.Local;
            else
                entry.GlobalSourceFilter = (int)VPBConfig.GlobalSourceFilterValue.All;
            entry.PackagePathFilter = node["PackagePathFilter"] ?? "";
            entry.ClothingSubfilter = node["ClothingSubfilter"] != null ? node["ClothingSubfilter"].AsInt : 0;
            entry.HairSubfilter = node["HairSubfilter"] != null ? node["HairSubfilter"].AsInt : 0;
            entry.AppearanceSubfilter = node["AppearanceSubfilter"] != null ? node["AppearanceSubfilter"].AsInt : 0;
            entry.PosePeopleFilter = node["PosePeopleFilter"] != null ? node["PosePeopleFilter"].AsInt : 0;
            if (node["BrowseHiddenMode"] != null || node["BrowseAlwaysLoadedMode"] != null || node["BrowseOldVersionsMode"] != null || node["BrowseLoadedMode"] != null || node["BrowseUnusedMode"] != null)
            {
                entry.BrowseHiddenMode = node["BrowseHiddenMode"] != null ? node["BrowseHiddenMode"].AsInt : 0;
                entry.BrowseAlwaysLoadedMode = node["BrowseAlwaysLoadedMode"] != null ? node["BrowseAlwaysLoadedMode"].AsInt : 0;
                entry.BrowseOldVersionsMode = node["BrowseOldVersionsMode"] != null ? node["BrowseOldVersionsMode"].AsInt : 1;
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

            entry.LicenseFilter = node["LicenseFilter"] != null ? (node["LicenseFilter"].Value ?? "") : "";

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

            var mergeArr = node["MergeMembers"] != null ? node["MergeMembers"].AsArray : null;
            if (mergeArr != null && mergeArr.Count > 0)
            {
                entry.MergeMembers = new List<QuickFilterEntry>();
                for (int i = 0; i < mergeArr.Count; i++)
                {
                    if (mergeArr[i] == null) continue;
                    try
                    {
                        QuickFilterEntry m = FromJSON(mergeArr[i]);
                        if (m == null) continue;
                        m.Id = 0;
                        m.Pinned = false;
                        m.MergeMembers = null; // flatten: stored members are leaves
                        entry.MergeMembers.Add(m);
                    }
                    catch { }
                }
                if (entry.MergeMembers.Count < 2)
                    entry.MergeMembers = null;
            }

            return entry;
        }

        internal static int ClampGlobalSourceFilter(int v)
        {
            if (v < 0) return 0;
            if (v > 2) return 2;
            return v;
        }

        private static bool IsLegacyLocalSourceFilter(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (string.Equals(raw, "local", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "Custom Scenes", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "custom", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

        /// <summary>Restore a soft-deleted preset at a clamped index.</summary>
        public void InsertFilterAt(QuickFilterEntry entry, int index)
        {
            if (entry == null) return;
            if (Filters == null) Filters = new List<QuickFilterEntry>();
            if (index < 0) index = 0;
            if (index > Filters.Count) index = Filters.Count;
            Filters.Insert(index, entry);
            Save();
        }

        public int IndexOfFilter(QuickFilterEntry entry)
        {
            if (entry == null || Filters == null) return -1;
            return Filters.IndexOf(entry);
        }

        public void RenameFilter(QuickFilterEntry entry, string newName)
        {
            if (entry != null && !string.IsNullOrEmpty(newName))
            {
                entry.Name = newName;
                Save();
            }
        }

        public void SetPinned(QuickFilterEntry entry, bool pinned)
        {
            if (entry == null) return;
            entry.Pinned = pinned;
            Save();
        }

        public void TogglePinned(QuickFilterEntry entry)
        {
            if (entry == null) return;
            SetPinned(entry, !entry.Pinned);
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

        /// <summary>Pinned presets in list order (for overflow one-click randomize).</summary>
        public void CollectPinnedFilters(List<QuickFilterEntry> into)
        {
            if (into == null) return;
            into.Clear();
            if (Filters == null) return;
            for (int i = 0; i < Filters.Count; i++)
            {
                QuickFilterEntry f = Filters[i];
                if (f != null && f.Pinned) into.Add(f);
            }
        }

        public void Load()
        {
            Filters.Clear();

            // Prefer SQLite; migrate legacy JSON once when table empty.
            try
            {
                if (VpbSqlite3.IsAvailable)
                {
                    try { VpbLocalDatabase.TryMigrateFilterPresetsFromJsonFile(filePath); } catch { }

                    var sqlList = new List<QuickFilterEntry>();
                    if (VpbLocalDatabase.TryLoadFilterPresets(sqlList) && sqlList.Count > 0)
                    {
                        Filters = sqlList;
                        return;
                    }
                    // SQL empty after migrate attempt — fall through to JSON if present.
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to load filter presets from SQLite: " + ex.Message);
            }

            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var root = JSON.Parse(json);
                var arr = root.AsArray;
                
                Filters.Clear();
                if (arr == null) return;
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
            // SQL is source of truth when available; JSON kept as portable mirror.
            bool sqlOk = false;
            try
            {
                if (VpbSqlite3.IsAvailable)
                    sqlOk = VpbLocalDatabase.TrySaveFilterPresets(Filters);
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Failed to save filter presets to SQLite: " + ex.Message);
            }

            try
            {
                var arr = new JSONArray();
                if (Filters != null)
                {
                    for (int i = 0; i < Filters.Count; i++)
                    {
                        QuickFilterEntry f = Filters[i];
                        if (f != null) arr.Add(f.ToJSON());
                    }
                }
                File.WriteAllText(filePath, VPB.src.util.JsonSerializationUtil.Serialize(arr, 4096));
            }
            catch (Exception ex)
            {
                if (!sqlOk)
                    Debug.LogError("[VPB] Failed to save quick filters: " + ex.Message);
                else
                    Debug.LogWarning("[VPB] Filter presets saved to SQLite; JSON mirror failed: " + ex.Message);
            }
        }
    }
}
