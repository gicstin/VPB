using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class VamHookPlugin
    {
        // Package Manager Window
        private class PackageManagerItem
        {
            public string Uid;
            public string Creator;
            public string Path;
            public string Type;
            public long Size;
            public DateTime LastWriteTime;
            public string AgeString;
            public string Description;
            public System.Collections.Generic.HashSet<string> AllDependencies;
            public int FilterScore;
            public int DependencyCount;
            public int LoadedDependencyCount;
            public List<string> MissingDependencies;
            public List<string> UnloadedDependencies;
            public List<string> NotFoundDependencies;
            public string HighlightedUid;
            public string HighlightedType;
            public string FilterMatchSummary;
            public string StatusPrefix;
            public GUIContent TypeContent;
            public GUIContent NameContent;
            public GUIContent DepContent;
            public GUIContent SizeContent;
            public bool Checked;
            public bool IsActive;
            public bool Locked;
            public bool AutoLoad;
            public bool IsLatest;
            public string GroupId;
            public bool IsInAddonList;
            public string ThumbnailPath;
        }
        private System.Collections.Generic.List<PackageManagerItem> m_AddonList = new System.Collections.Generic.List<PackageManagerItem>();
        private System.Collections.Generic.List<PackageManagerItem> m_AllList = new System.Collections.Generic.List<PackageManagerItem>();
        private System.Collections.Generic.List<PackageManagerItem> m_UnifiedList = new System.Collections.Generic.List<PackageManagerItem>();
        private string m_PkgMgrFilter = "";
        private string m_PkgMgrFilterLower = "";
        private string[] m_PkgMgrFilterTermsLower = new string[0];
        private bool m_PkgMgrUseRelevanceSort = true;
        private string m_PkgMgrCreatorFilter = "";
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryInclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryExclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.List<string> m_PkgMgrCategories = new System.Collections.Generic.List<string>();
        private System.Collections.Generic.Dictionary<string, int> m_PkgMgrCategoryCounts = new System.Collections.Generic.Dictionary<string, int>();
        private System.Collections.Generic.HashSet<string> m_LockedPackages = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> m_AutoLoadPackages = new System.Collections.Generic.HashSet<string>();
        private Coroutine m_ScanPkgMgrCo;
        private Coroutine m_PkgMgrIsolateCo;
        public System.Action OnPkgMgrListChanged;
        public System.Action<string> OnPkgMgrStatusChanged;
        private string m_PkgMgrSortField = "Name";
        private bool m_PkgMgrSortAscending = true;

        
        private struct PackageManagerVisibleRow
        {
            public int Index; // -1 for header
            public string Header;
        }

        private struct PackageManagerAction
        {
            public string Label;
            public System.Action Execute;

            public PackageManagerAction(string label, System.Action execute)
            {
                Label = label;
                Execute = execute;
            }
        }
        private System.Collections.Generic.List<PackageManagerVisibleRow> m_AddonVisibleRows = new System.Collections.Generic.List<PackageManagerVisibleRow>();
        private System.Collections.Generic.List<PackageManagerVisibleRow> m_AllVisibleRows = new System.Collections.Generic.List<PackageManagerVisibleRow>();
        private System.Collections.Generic.List<PackageManagerVisibleRow> m_UnifiedVisibleRows = new System.Collections.Generic.List<PackageManagerVisibleRow>();
        private bool m_PkgMgrIndicesDirty = true;
        private int m_PkgMgrSelectedCount = 0;
        private int m_PkgMgrTargetGroupCount = 0;

        private PackageManagerItem m_PkgMgrSelectedItem = null;
        private Texture2D m_PkgMgrSelectedThumbnail = null;
        private string m_PkgMgrSelectedDescription = "";
        private string m_PkgMgrLastThumbnailPath = "";
        private System.Collections.Generic.List<PackageManagerUndoOperation> m_PkgMgrUndoStack = new System.Collections.Generic.List<PackageManagerUndoOperation>();
        private const int PkgMgrMaxUndoSteps = 10;
        private System.Collections.Generic.Queue<PackageManagerItem> m_PkgMgrAnalysisQueue = new System.Collections.Generic.Queue<PackageManagerItem>();
        private Coroutine m_PkgMgrAnalysisCo;

        private class PackageCachedInfo
        {
             public string Type;
             public string Description;
             public long Size;
             public DateTime LastWriteTime;
        }
        private static System.Collections.Generic.Dictionary<string, PackageCachedInfo> m_PackageCache = new System.Collections.Generic.Dictionary<string, PackageCachedInfo>();

        private void RefreshVisibleIndices()
        {
            if (GalleryPanel.BenchmarkStartTime > 0)
            {
                 UnityEngine.Debug.Log("[Benchmark] RefreshVisibleIndices at " + Time.realtimeSinceStartup + " (+" + (Time.realtimeSinceStartup - GalleryPanel.BenchmarkStartTime).ToString("F3") + "s)");
            }
            RefreshVisibleRows(m_AddonList, m_AddonVisibleRows);
            RefreshVisibleRows(m_AllList, m_AllVisibleRows);

            // Build Unified List
            m_UnifiedList.Clear();
            var loadedUids = new System.Collections.Generic.HashSet<string>();
            for(int i=0; i<m_AddonList.Count; i++)
            {
                var item = m_AddonList[i];
                item.IsInAddonList = true;
                m_UnifiedList.Add(item);
                if (!string.IsNullOrEmpty(item.Uid)) loadedUids.Add(item.Uid);
            }
            for(int i=0; i<m_AllList.Count; i++)
            {
                var item = m_AllList[i];
                if (!string.IsNullOrEmpty(item.Uid) && loadedUids.Contains(item.Uid)) continue;
                item.IsInAddonList = false;
                m_UnifiedList.Add(item);
            }
            
            // Sort unified list as a whole to keep items in same place regardless of loaded status
            m_UnifiedList.Sort(ComparePackageManagerItems);

            RefreshVisibleRows(m_UnifiedList, m_UnifiedVisibleRows);

            m_PkgMgrIndicesDirty = false;
        }

        private System.Collections.IEnumerator ProcessAnalysisQueue()
        {
            var wait = new WaitForSeconds(0.1f);
            while (true)
            {
                if (m_PkgMgrAnalysisQueue.Count > 0)
                {
                    float startTime = Time.realtimeSinceStartup;
                    bool anyUpdated = false;

                    while (m_PkgMgrAnalysisQueue.Count > 0)
                    {
                        if (Time.realtimeSinceStartup - startTime > 0.016f) 
                        {
                            if (anyUpdated) m_PkgMgrIndicesDirty = true;
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                            anyUpdated = false;
                        }

                        var item = m_PkgMgrAnalysisQueue.Dequeue();
                        if (item == null) continue;

                        try
                        {
                            // 1. Fetch Metadata (Type, Description) if not yet known
                            if (item.Type == "Unknown")
                            {
                                string type = DeterminePackageType(item.Uid);
                                string description = "";
                                VarPackage pkg = FileManager.GetPackage(item.Uid, false);
                                if (pkg != null && !string.IsNullOrEmpty(pkg.Description)) description = pkg.Description;

                                item.Type = type;
                                item.Description = description;
                                item.TypeContent = new GUIContent(type, item.Path);
                                
                                // Update Cache
                                var cached = new PackageCachedInfo { 
                                    Type = type, 
                                    Description = description,
                                    Size = item.Size,
                                    LastWriteTime = item.LastWriteTime
                                };
                                if (m_PackageCache.ContainsKey(item.Uid)) m_PackageCache[item.Uid] = cached;
                                else m_PackageCache.Add(item.Uid, cached);

                                // Update categories (might be late, but better than never)
                                if (!m_PkgMgrCategories.Contains(type)) m_PkgMgrCategories.Add(type);
                                if (!m_PkgMgrCategoryCounts.ContainsKey(type)) m_PkgMgrCategoryCounts[type] = 0;
                                m_PkgMgrCategoryCounts[type]++;
                            }

                            // 2. Fetch Dependencies
                            var deepDeps = FileManager.GetDependenciesDeep(item.Uid, 2);
                            item.AllDependencies = deepDeps;
                            item.DependencyCount = deepDeps.Count;

                            int loadedDepCount = 0;
                            List<string> unloadedDeps = new List<string>();
                            List<string> notFoundDeps = new List<string>();
                            foreach (var dep in deepDeps)
                            {
                                var resolved = FileManager.ResolveDependency(dep);
                                if (resolved == null) notFoundDeps.Add(dep);
                                else if (resolved.Path.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) unloadedDeps.Add(dep);
                                else loadedDepCount++;
                            }

                            item.LoadedDependencyCount = loadedDepCount;
                            item.UnloadedDependencies = unloadedDeps;
                            item.NotFoundDependencies = notFoundDeps;
                            
                            List<string> missingDeps = new List<string>(unloadedDeps.Count + notFoundDeps.Count);
                            missingDeps.AddRange(unloadedDeps);
                            missingDeps.AddRange(notFoundDeps);
                            item.MissingDependencies = missingDeps;

                            UpdatePkgMgrItemCache(item);
                            anyUpdated = true;
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("Error analyzing package " + item.Uid + ": " + ex.Message);
                        }
                    }
                    if (anyUpdated) m_PkgMgrIndicesDirty = true;
                    
                    if (GalleryPanel.BenchmarkStartTime > 0 && m_PkgMgrAnalysisQueue.Count == 0)
                    {
                        float now = Time.realtimeSinceStartup;
                        UnityEngine.Debug.Log("[Benchmark] ProcessAnalysisQueue FINISHED at " + now + " (+" + (now - GalleryPanel.BenchmarkStartTime).ToString("F3") + "s)");
                        GalleryPanel.BenchmarkStartTime = 0;
                    }
                }
                yield return wait;
            }
        }

        private class PackageManagerUndoOperation
        {
            public string Label;
            public float CreatedAt;
            public System.Collections.Generic.List<PackageManagerMoveRecord> Moves = new System.Collections.Generic.List<PackageManagerMoveRecord>();
        }

        private struct PackageManagerMoveRecord
        {
            public string Uid;
            public string From;
            public string To;
            public PackageManagerMoveRecord(string uid, string from, string to)
            {
                Uid = uid;
                From = from;
                To = to;
            }
        }

        private bool IsPackageManagerBusy()
        {
            return m_ScanPkgMgrCo != null || m_PkgMgrIsolateCo != null || m_MovePkgMgrCo != null;
        }

        public void SetPkgMgrFilter(string filter)
        {
            string newPkgMgrFilter = filter ?? "";
            if (newPkgMgrFilter != m_PkgMgrFilter)
            {
                m_PkgMgrFilter = newPkgMgrFilter;
                m_PkgMgrFilterLower = m_PkgMgrFilter.ToLower();
                m_PkgMgrFilterTermsLower = m_PkgMgrFilterLower.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                m_PkgMgrIndicesDirty = true;
                UpdatePkgMgrHighlights();
            }
        }

        public void SetPkgMgrCreatorFilter(string creator)
        {
            creator = creator ?? "";
            if (creator != m_PkgMgrCreatorFilter)
            {
                m_PkgMgrCreatorFilter = creator;
                m_PkgMgrIndicesDirty = true;
            }
        }

        public void ClearPkgMgrCategoryFilters()
        {
            if (m_PkgMgrCategoryInclusive.Count > 0 || m_PkgMgrCategoryExclusive.Count > 0)
            {
                m_PkgMgrCategoryInclusive.Clear();
                m_PkgMgrCategoryExclusive.Clear();
                m_PkgMgrIndicesDirty = true;
            }
        }

        public void SetPkgMgrSortField(string field)
        {
            field = field ?? "Name";
            if (field != m_PkgMgrSortField)
            {
                m_PkgMgrSortField = field;
                SortPackageManagerList();
            }
        }

        public void SetPkgMgrSortDirection(bool ascending)
        {
            if (ascending != m_PkgMgrSortAscending)
            {
                m_PkgMgrSortAscending = ascending;
                SortPackageManagerList();
            }
        }

        public void SetPkgMgrCategoryFilterByType(string categoryType)
        {
            ClearPkgMgrCategoryFilters();
            if (!string.IsNullOrEmpty(categoryType) && categoryType != "All")
            {
                m_PkgMgrCategoryInclusive.Add(categoryType);
            }
            m_PkgMgrIndicesDirty = true;
        }


        private void RefreshVisibleRows(System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.List<PackageManagerVisibleRow> rows)
        {
            rows.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!IsPackageManagerItemVisible(item)) continue;
                rows.Add(new PackageManagerVisibleRow { Index = i, Header = null });
            }

            if (m_PkgMgrUseRelevanceSort && m_PkgMgrFilterTermsLower != null && m_PkgMgrFilterTermsLower.Length > 0)
            {
                bool hasHeaders = false;
                for (int i = 0; i < rows.Count; i++) { if (rows[i].Index == -1) { hasHeaders = true; break; } }
                if (!hasHeaders)
                {
                    rows.Sort((a, b) => {
                        int sa = list[a.Index].FilterScore;
                        int sb = list[b.Index].FilterScore;
                        int cmp = sa.CompareTo(sb);
                        if (cmp != 0) return cmp;
                        return a.Index.CompareTo(b.Index);
                    });
                }
            }
        }

        private int GetPackageManagerSelectionCount()
        {
            int count = 0;
            foreach (var item in m_AddonList) if (item.Checked) count++;
            foreach (var item in m_AllList) if (item.Checked) count++;
            return count;
        }

        private bool ConfirmPackageManagerAction(string actionLabel, int count)
        {
            if (count <= 10) return true;
            if (m_PkgMgrPendingAction == actionLabel && m_PkgMgrPendingActionCount == count && Time.realtimeSinceStartup < m_PkgMgrPendingActionExpiry)
            {
                m_PkgMgrPendingAction = "";
                m_PkgMgrPendingActionCount = 0;
                m_PkgMgrPendingActionExpiry = 0f;
                return true;
            }

            m_PkgMgrPendingAction = actionLabel;
            m_PkgMgrPendingActionCount = count;
            m_PkgMgrPendingActionExpiry = Time.realtimeSinceStartup + 4f;
            m_PkgMgrStatusMessage = string.Format("{0} {1} packages. Click again to confirm.", actionLabel, count);
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;
            return false;
        }

        private string m_PkgMgrStatusMessage = "";
        private float m_PkgMgrStatusTimer = 0f;
        private string m_PkgMgrPendingAction = "";
        private int m_PkgMgrPendingActionCount = 0;
        private float m_PkgMgrPendingActionExpiry = 0f;

        string PackageIDToCreator(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "Unknown";
            int firstDot = uid.IndexOf('.');
            if (firstDot > 0) return uid.Substring(0, firstDot);
            return "Unknown";
        }

        string DeterminePackageType(string uid)
        {
             VarPackage pkg = FileManager.GetPackage(uid, false);
             if (pkg == null) return "Unknown";
             
             // Check content
             if (pkg.ClothingFileEntryNames != null && pkg.ClothingFileEntryNames.Count > 0) return "Clothing";
             if (pkg.HairFileEntryNames != null && pkg.HairFileEntryNames.Count > 0) return "Hair";
             
             // Check file entries for other types
             if (pkg.FileEntries != null)
             {
                 foreach(var entry in pkg.FileEntries)
                 {
                     string f = entry.InternalPath;
                     if (f.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase)) return "Appearance";
                     if (f.StartsWith("Custom/Atom/Person/AnimationPresets", StringComparison.OrdinalIgnoreCase)) return "Animation";
                     if (f.StartsWith("Custom/Atom/Person/BreastPhysics", StringComparison.OrdinalIgnoreCase)) return "BreastPhysics";
                     if (f.StartsWith("Custom/Atom/Person/Clothing", StringComparison.OrdinalIgnoreCase)) return "Clothing";
                     if (f.StartsWith("Custom/Atom/Person/Hair", StringComparison.OrdinalIgnoreCase)) return "Hair";
                     if (f.StartsWith("Custom/Atom/Person/Morphs", StringComparison.OrdinalIgnoreCase)) return "Morphs";
                     if (f.StartsWith("Custom/Atom/Person/Plugins", StringComparison.OrdinalIgnoreCase)) return "Plugins";
                     if (f.StartsWith("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vac", StringComparison.OrdinalIgnoreCase)) return "Pose";
                     if (f.StartsWith("Custom/Atom/Person/Skin", StringComparison.OrdinalIgnoreCase)) return "Skin";
                     if (f.StartsWith("Custom/Atom/Person/General", StringComparison.OrdinalIgnoreCase)) return "General";
                     if (f.StartsWith("Custom/Clothing/", StringComparison.OrdinalIgnoreCase)) return "Clothing";
                     if (f.StartsWith("Custom/Hair/", StringComparison.OrdinalIgnoreCase)) return "Hair";
                     if (f.IndexOf("Saves/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0) return "Pose";
                     if (f.StartsWith("Custom/SubScene", StringComparison.OrdinalIgnoreCase)) return "SubScene";
                     if (f.StartsWith("Saves/scene", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "Scene";
                     if (f.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) >= 0 && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "Scene";
                     if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && f.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0) return "Scene";
                     if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && f.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0) return "Pose";
                     if (f.IndexOf("Custom/Assets", StringComparison.OrdinalIgnoreCase) >= 0 || f.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase)) return "CUA";
                     if (f.StartsWith("Custom/Scripts", StringComparison.OrdinalIgnoreCase)) return "Script";
                     if (f.StartsWith("Custom/Atom/Person", StringComparison.OrdinalIgnoreCase)) return "Person";
                 }
             }
             
             return "Other";
        }

        private string GetFirstScenePath(string uid)
        {
             VarPackage pkg = FileManager.GetPackage(uid, false);
             if (pkg == null || pkg.FileEntries == null) return null;
             
             foreach (var entry in pkg.FileEntries)
             {
                 string f = entry.InternalPath;
                 if (f.StartsWith("Saves/scene", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                 {
                     return uid + ":/" + f;
                 }
             }
             return null;
        }

        bool IsPackageManagerItemVisible(PackageManagerItem item)
        {
            if (!string.IsNullOrEmpty(m_PkgMgrCreatorFilter) && !item.Creator.Equals(m_PkgMgrCreatorFilter, StringComparison.OrdinalIgnoreCase)) return false;

            // Exclusive filters (must match NONE)
            if (m_PkgMgrCategoryExclusive.Count > 0)
            {
                foreach (var filter in m_PkgMgrCategoryExclusive)
                {
                    bool itemMatches = false;
                    if (filter == "Locked (L)") itemMatches = item.Locked;
                    else if (filter == "Auto-Load (AL)") itemMatches = item.AutoLoad;
                    else if (filter == "Active") itemMatches = item.IsActive;
                    else if (filter == "Latest") itemMatches = item.IsLatest;
                    else if (filter == "Old Version") itemMatches = !item.IsLatest;
                    else itemMatches = (item.Type == filter);

                    if (itemMatches) return false;
                }
            }

            // Inclusive filters (must match at least ONE if any are set)
            if (m_PkgMgrCategoryInclusive.Count > 0)
            {
                bool match = false;
                foreach (var filter in m_PkgMgrCategoryInclusive)
                {
                    if (filter == "Locked (L)") { if (item.Locked) { match = true; break; } }
                    else if (filter == "Auto-Load (AL)") { if (item.AutoLoad) { match = true; break; } }
                    else if (filter == "Active") { if (item.IsActive) { match = true; break; } }
                    else if (filter == "Latest") { if (item.IsLatest) { match = true; break; } }
                    else if (filter == "Old Version") { if (!item.IsLatest) { match = true; break; } }
                    else if (item.Type == filter) { match = true; break; }
                }
                if (!match) return false;
            }

            if (m_PkgMgrFilterTermsLower != null && m_PkgMgrFilterTermsLower.Length > 0)
            {
                int score = 0;
                var matchAreas = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int ti = 0; ti < m_PkgMgrFilterTermsLower.Length; ti++)
                {
                    string term = m_PkgMgrFilterTermsLower[ti];
                    if (string.IsNullOrEmpty(term)) continue;
                    string area;
                    int termScore = ComputePkgMgrTermScore(item, term, out area);
                    if (termScore == int.MaxValue) { item.FilterScore = int.MaxValue; return false; }
                    if (!string.IsNullOrEmpty(area)) matchAreas.Add(area);
                    score += termScore;
                }
                item.FilterScore = score;
                if (matchAreas.Count > 0)
                {
                    var areas = new System.Collections.Generic.List<string>(matchAreas);
                    areas.Sort(StringComparer.OrdinalIgnoreCase);
                    item.FilterMatchSummary = string.Join(", ", areas.ToArray());
                }
                else
                {
                    item.FilterMatchSummary = "";
                }

                // Update cached tooltip to reflect match reasons immediately (even if UpdatePkgMgrItemCache hasn't been called again).
                if (item.NameContent != null)
                {
                    string tooltip = item.Path;
                    if (!string.IsNullOrEmpty(item.FilterMatchSummary)) tooltip += "\nMatched: " + item.FilterMatchSummary;
                    item.NameContent.tooltip = tooltip;
                }
            }
            else
            {
                item.FilterScore = int.MaxValue;
                item.FilterMatchSummary = "";

                if (item.NameContent != null)
                {
                    item.NameContent.tooltip = item.Path;
                }
            }

            return true;
        }

        private int ComputePkgMgrTermScore(PackageManagerItem item, string termLower, out string bestArea)
        {
            bestArea = "";
            if (item == null) return int.MaxValue;
            if (string.IsNullOrEmpty(termLower)) return 0;

            // Status terms
            if (termLower == "active") { if (!item.IsActive) return int.MaxValue; bestArea = "Status"; return 1; }
            if (termLower == "locked") { if (!item.Locked) return int.MaxValue; bestArea = "Status"; return 1; }
            if (termLower == "autoload" || termLower == "auto-load" || termLower == "auto" || termLower == "auto_load") { if (!item.AutoLoad) return int.MaxValue; bestArea = "Status"; return 1; }

            int best = int.MaxValue;

            // Prefer name/creator/type over path/description/deps
            int s;
            s = ComputeFieldMatchScore(item.Uid, termLower, 0); if (s < best) { best = s; bestArea = "Name"; }
            s = ComputeFieldMatchScore(item.Creator, termLower, 1); if (s < best) { best = s; bestArea = "Creator"; }
            s = ComputeFieldMatchScore(item.Type, termLower, 2); if (s < best) { best = s; bestArea = "Category"; }
            s = ComputeFieldMatchScore(item.Path, termLower, 4); if (s < best) { best = s; bestArea = "Path"; }
            s = ComputeFieldMatchScore(item.Description, termLower, 6); if (s < best) { best = s; bestArea = "Description"; }

            if (item.AllDependencies != null)
            {
                foreach (var dep in item.AllDependencies)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    int idx = dep.IndexOf(termLower, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;
                    int local = 7 + (idx == 0 ? 0 : 2);
                    if (local < best) { best = local; bestArea = "Dependencies"; }
                    break;
                }
            }

            return best;
        }

        private int ComputeFieldMatchScore(string value, string termLower, int baseWeight)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(termLower)) return int.MaxValue;
            int idx = value.IndexOf(termLower, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return int.MaxValue;
            int local = baseWeight + (idx == 0 ? 0 : 2);
            if (string.Equals(value, termLower, StringComparison.OrdinalIgnoreCase)) local = Math.Min(local, baseWeight);
            return local;
        }





        void ToggleLockSelection()
        {
            ForEachPackageManagerItem(item =>
            {
                if (!item.Checked) return;
                item.Locked = !item.Locked;
                if (item.Locked)
                {
                    if (m_LockedPackages.Add(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]++;
                }
                else
                {
                    if (m_LockedPackages.Remove(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]--;
                }
                LockedPackagesManager.Instance.SetLocked(item.Uid, item.Locked, false);
            });
            LockedPackagesManager.Instance.Save();
        }

        void ToggleAutoLoadSelection()
        {
            bool needMove = false;
            ToggleAutoLoadForList(m_AddonList, ref needMove, false);
            ToggleAutoLoadForList(m_AllList, ref needMove, true);
            AutoLoadPackagesManager.Instance.Save();
            if (needMove) PerformMove(m_AllList, false);
        }

        void SelectAllPackageManager(System.Collections.Generic.List<PackageManagerItem> list, bool state, System.Collections.Generic.List<PackageManagerItem> otherList = null)
        {
            if (state && otherList != null)
            {
                foreach (var item in otherList) item.Checked = false;
            }

            int lockedSkipped = 0;
            foreach (var item in list)
            {
                if (IsPackageManagerItemVisible(item))
                {
                    if (item.Locked && state && !m_PkgMgrCategoryInclusive.Contains("Locked (L)")) { lockedSkipped++; continue; }
                    item.Checked = state;
                }
            }

            if (lockedSkipped > 0)
            {
                m_PkgMgrStatusMessage = string.Format("Skipped {0} locked packages. Enable 'Locked (L)' to include them.", lockedSkipped);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private void EnsurePackageManagerSingleSelection(PackageManagerItem item)
        {
            if (item == null) return;
            ForEachPackageManagerItem(it => it.Checked = false);
            item.Checked = true;
            bool isLoaded = m_AddonList.Contains(item);
            OnPackageManagerItemSelected(item, isLoaded);
        }

        private void SetPackageManagerAutoInstall(string packageUid, bool enable)
        {
            if (string.IsNullOrEmpty(packageUid)) return;
            if (enable) FileEntry.AutoInstallLookup.Add(packageUid);
            else FileEntry.AutoInstallLookup.Remove(packageUid);

            if (!Directory.Exists(GlobalInfo.PluginInfoDirectory))
            {
                Directory.CreateDirectory(GlobalInfo.PluginInfoDirectory);
            }

            SerializableNames sf = new SerializableNames();
            sf.Names = FileEntry.AutoInstallLookup.ToArray();
            File.WriteAllText(GlobalInfo.AutoInstallPath, JsonUtility.ToJson(sf));

            m_PkgMgrStatusMessage = enable ? "Added to auto-install: " + packageUid : "Removed from auto-install: " + packageUid;
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
        }

        private System.Collections.Generic.List<PackageManagerAction> BuildPackageManagerSingleItemActions(PackageManagerItem item)
        {
            System.Collections.Generic.List<PackageManagerAction> actions = new System.Collections.Generic.List<PackageManagerAction>();
            if (item == null) return actions;

            void AddAction(string label, System.Action execute)
            {
                if (actions.Count >= 10) return;
                actions.Add(new PackageManagerAction(label, execute));
            }

            bool isLoaded = m_AddonList.Contains(item);
            bool isAutoInstall = FileEntry.AutoInstallLookup.Contains(item.Uid);

            AddAction(isLoaded ? "Unload" : "Load", () => {
                EnsurePackageManagerSingleSelection(item);
                if (isLoaded) PerformMove(m_AddonList, true);
                else PerformMove(m_AllList, false);
            });

            if (item.Type == "Scene")
            {
                string scenePath = GetFirstScenePath(item.Uid);
                if (!string.IsNullOrEmpty(scenePath))
                {
                    AddAction("Launch Scene", () => {
                        EnsurePackageManagerSingleSelection(item);
                        LoadFromSceneWorldDialog(scenePath);
                    });
                }
            }

            AddAction(item.Locked ? "Unlock" : "Lock", () => {
                EnsurePackageManagerSingleSelection(item);
                ToggleLockSelection();
            });

            AddAction(item.AutoLoad ? "Disable Auto-Load" : "Enable Auto-Load", () => {
                EnsurePackageManagerSingleSelection(item);
                ToggleAutoLoadSelection();
            });

            AddAction(isAutoInstall ? "Disable Auto-Install" : "Enable Auto-Install", () => {
                EnsurePackageManagerSingleSelection(item);
                SetPackageManagerAutoInstall(item.Uid, !isAutoInstall);
            });

            AddAction("Isolate", () => {
                EnsurePackageManagerSingleSelection(item);
                PerformKeepSelectedUnloadRest();
            });

            if (item.MissingDependencies != null && item.MissingDependencies.Count > 0)
            {
                AddAction("Select Unloaded Dependencies (" + item.MissingDependencies.Count + ")", () => {
                    EnsurePackageManagerSingleSelection(item);
                    ResolveDependencies(item);
                });

                AddAction("Load Unloaded Dependencies", () => {
                    EnsurePackageManagerSingleSelection(item);
                    ResolveDependencies(item);
                    PerformMove(m_AllList, false);
                });
            }

            AddAction("Show in Explorer", () => {
                ShowInExplorer(item.Path);
            });

            AddAction("Filter by Creator (" + item.Creator + ")", () => {
                m_PkgMgrCreatorFilter = item.Creator;
                m_PkgMgrIndicesDirty = true;
            });

            AddAction("Select All by Creator (" + item.Creator + ")", () => {
                SelectAllByCreator(item.Creator);
            });

            AddAction("Copy Package Name", () => {
                GUIUtility.systemCopyBuffer = item.Uid;
                m_PkgMgrStatusMessage = "Copied name to clipboard: " + item.Uid;
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            });

            AddAction("Copy Dependencies (Deep)", () => {
                VarPackage pkg = FileManager.GetPackage(item.Uid, false);
                if (pkg != null)
                {
                    var deps = pkg.GetDependenciesDeep(2);
                    if (deps != null && deps.Count > 0)
                    {
                        GUIUtility.systemCopyBuffer = string.Join("\n", deps.ToArray());
                        LogUtil.Log("Copied " + deps.Count + " dependencies to clipboard.");
                    }
                }
            });

            AddAction("Copy Full Path", () => {
                GUIUtility.systemCopyBuffer = Path.GetFullPath(item.Path);
                m_PkgMgrStatusMessage = "Copied full path to clipboard.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            });

            int groupCount = CountGroupItems(item.GroupId);
            if (groupCount > 1)
            {
                AddAction("Select Group", () => {
                    SetGroupChecked(item.Uid, true);
                });

                AddAction("Unselect Group", () => {
                    SetGroupChecked(item.Uid, false);
                });

                AddAction("Copy Group Names", () => {
                    CopyGroupNames(item.Uid);
                });

                AddAction("Copy Group Dependencies (Deep)", () => {
                    CopyGroupDependenciesDeep(item.Uid);
                });
            }

            return actions;
        }

        private int GetPackageManagerActionHotkeyIndex(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    return 0;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    return 1;
                case KeyCode.Alpha3:
                case KeyCode.Keypad3:
                    return 2;
                case KeyCode.Alpha4:
                case KeyCode.Keypad4:
                    return 3;
                case KeyCode.Alpha5:
                case KeyCode.Keypad5:
                    return 4;
                case KeyCode.Alpha6:
                case KeyCode.Keypad6:
                    return 5;
                case KeyCode.Alpha7:
                case KeyCode.Keypad7:
                    return 6;
                case KeyCode.Alpha8:
                case KeyCode.Keypad8:
                    return 7;
                case KeyCode.Alpha9:
                case KeyCode.Keypad9:
                    return 8;
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    return 9;
            }
            return -1;
        }

        void HidePackageManagerContextMenu()
        {
            if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide();
        }

        void ShowPackageManagerContextMenu(PackageManagerItem item)
        {
            m_PkgMgrSelectedCount = CountSelectedItems();
            m_PkgMgrTargetGroupCount = CountGroupItems(item.GroupId);

            System.Collections.Generic.List<ContextMenuPanel.Option> options = new System.Collections.Generic.List<ContextMenuPanel.Option>();
            int groupCount = m_PkgMgrTargetGroupCount;
            int selectedCount = m_PkgMgrSelectedCount;
            string title = item.Uid;

            if (item.Checked && selectedCount > 1)
            {
                title = string.Format("{0} items selected", selectedCount);

                options.Add(new ContextMenuPanel.Option("Copy Selected Names", () => {
                    CopySelectedNames();
                }));

                options.Add(new ContextMenuPanel.Option("Copy Selected Dependencies (Deep)", () => {
                    CopySelectedDependenciesDeep();
                }));

                options.Add(new ContextMenuPanel.Option("Unselect All", () => {
                    SelectAllPackageManager(m_AddonList, false);
                    SelectAllPackageManager(m_AllList, false);
                }));

                options.Add(new ContextMenuPanel.Option("", null));
                options.Add(new ContextMenuPanel.Option("Keep Selected -> Unload Rest", () => {
                    PerformKeepSelectedUnloadRest();
                }));
            }
            else
            {
                if (item.Type == "Scene")
                {
                    options.Add(new ContextMenuPanel.Option("Launch Scene", () => {
                        string scenePath = GetFirstScenePath(item.Uid);
                        if (!string.IsNullOrEmpty(scenePath))
                        {
                            LoadFromSceneWorldDialog(scenePath);
                        }
                    }));
                }

                if (item.MissingDependencies != null && item.MissingDependencies.Count > 0)
                {
                    options.Add(new ContextMenuPanel.Option("Select Unloaded Dependencies (" + item.MissingDependencies.Count + ")", () => {
                        ResolveDependencies(item);
                    }));

                    options.Add(new ContextMenuPanel.Option("Load Unloaded Dependencies", () => {
                        ResolveDependencies(item);
                        PerformMove(m_AllList, false);
                    }));
                }

                if (selectedCount > 0)
                {
                    options.Add(new ContextMenuPanel.Option("Keep Selected -> Unload Rest", () => {
                        PerformKeepSelectedUnloadRest();
                    }));
                }

                options.Add(new ContextMenuPanel.Option("", null));
                options.Add(new ContextMenuPanel.Option("Filter by Creator (" + item.Creator + ")", () => {
                    m_PkgMgrCreatorFilter = item.Creator;
                    m_PkgMgrIndicesDirty = true;
                }));

                options.Add(new ContextMenuPanel.Option("Select All by Creator (" + item.Creator + ")", () => {
                    SelectAllByCreator(item.Creator);
                }));

                if (groupCount > 1)
                {
                    options.Add(new ContextMenuPanel.Option("", null));
                    options.Add(new ContextMenuPanel.Option("Select Group", () => {
                        SetGroupChecked(item.Uid, true);
                    }));

                    options.Add(new ContextMenuPanel.Option("Unselect Group", () => {
                        SetGroupChecked(item.Uid, false);
                    }));

                    options.Add(new ContextMenuPanel.Option("Copy Group Names", () => {
                        CopyGroupNames(item.Uid);
                    }));
                }

                options.Add(new ContextMenuPanel.Option("", null));
                options.Add(new ContextMenuPanel.Option("Show in Explorer", () => {
                    ShowInExplorer(item.Path);
                }));

                options.Add(new ContextMenuPanel.Option("Copy Package Name", () => {
                    GUIUtility.systemCopyBuffer = item.Uid;
                }));

                options.Add(new ContextMenuPanel.Option("Copy Dependencies (Deep)", () => {
                    VarPackage pkg = FileManager.GetPackage(item.Uid, false);
                    if (pkg != null)
                    {
                        var deps = pkg.GetDependenciesDeep(2);
                        if (deps != null && deps.Count > 0)
                        {
                            GUIUtility.systemCopyBuffer = string.Join("\n", deps.ToArray());
                            LogUtil.Log("Copied " + deps.Count + " dependencies to clipboard.");
                        }
                    }
                }));
            }

            if (ContextMenuPanel.Instance != null)
            {
                if (UnityEngine.XR.XRSettings.enabled)
                {
                    Vector3 position = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
                    ContextMenuPanel.Instance.Show(position, options, title);
                }
                else
                {
                    ContextMenuPanel.Instance.Show(Input.mousePosition, options, title);
                }
            }
        }

        private void ShowInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string fullPath = Path.GetFullPath(path).Replace('/', '\\');
            if (File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + fullPath + "\"");
            }
            else if (Directory.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + fullPath + "\"");
            }
        }

        private void SelectAllByCreator(string creator)
        {
            if (string.IsNullOrEmpty(creator)) return;
            ForEachPackageManagerItem(item =>
            {
                if (item.Creator.Equals(creator, StringComparison.OrdinalIgnoreCase)) item.Checked = true;
            });
        }

        private void ResolveDependencies(PackageManagerItem item)
        {
            if (item == null)
            {
                m_PkgMgrStatusMessage = "No package selected.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                return;
            }

            var unloaded = item.UnloadedDependencies;
            var notFound = item.NotFoundDependencies;
            if ((unloaded == null || unloaded.Count == 0) && (notFound == null || notFound.Count == 0))
            {
                m_PkgMgrStatusMessage = "No unloaded dependencies found.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                return;
            }

            int selectedCount = 0;
            int alreadyLoaded = 0;
            int lockedSkipped = 0;
            int notFoundCount = (notFound != null) ? notFound.Count : 0;

            if (unloaded != null)
            {
                foreach (var dep in unloaded)
                {
                    bool selected = false;
                    foreach (var other in m_AllList)
                    {
                        if (!other.Uid.Equals(dep, StringComparison.OrdinalIgnoreCase)) continue;
                        if (other.Locked) { lockedSkipped++; selected = true; break; }
                        other.Checked = true;
                        selectedCount++;
                        selected = true;
                        break;
                    }

                    if (!selected)
                    {
                        foreach (var other in m_AddonList)
                        {
                            if (other.Uid.Equals(dep, StringComparison.OrdinalIgnoreCase)) { alreadyLoaded++; break; }
                        }
                    }
                }
            }

            string message = string.Format("Selected {0} unloaded dependencies", selectedCount);
            if (alreadyLoaded > 0) message += string.Format(", {0} already loaded", alreadyLoaded);
            if (lockedSkipped > 0) message += string.Format(", {0} locked", lockedSkipped);
            if (notFoundCount > 0) message += string.Format(", {0} not found", notFoundCount);
            m_PkgMgrStatusMessage = message + ".";
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;
        }

        private int CountSelectedItems()
        {
            int count = 0;
            foreach (var item in m_AddonList) if (item.Checked) count++;
            foreach (var item in m_AllList) if (item.Checked) count++;
            return count;
        }

        private void CopySelectedNames()
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList) if (item.Checked) names.Add(item.Uid);
            foreach (var item in m_AllList) if (item.Checked) names.Add(item.Uid);
            
            if (names.Count > 0)
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", names.ToArray());
                m_PkgMgrStatusMessage = string.Format("Copied {0} selected names to clipboard.", names.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private void CopySelectedDependenciesDeep()
        {
            System.Collections.Generic.HashSet<string> allDeps = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            System.Collections.Generic.List<string> uids = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList) if (item.Checked) uids.Add(item.Uid);
            foreach (var item in m_AllList) if (item.Checked) uids.Add(item.Uid);

            foreach (var uid in uids)
            {
                var deps = FileManager.GetDependenciesDeep(uid, 2);
                foreach (var dep in deps) allDeps.Add(dep);
            }

            if (allDeps.Count > 0)
            {
                string[] depArray = new string[allDeps.Count];
                allDeps.CopyTo(depArray);
                GUIUtility.systemCopyBuffer = string.Join("\n", depArray);
                m_PkgMgrStatusMessage = string.Format("Copied {0} unique deep dependencies for selection to clipboard.", allDeps.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private int CountGroupItems(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return 1;
            int count = 0;
            foreach (var item in m_AddonList) if (item.GroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)) count++;
            foreach (var item in m_AllList) if (item.GroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }

        void ForEachPackageManagerItem(Action<PackageManagerItem> action)
        {
            foreach (var item in m_AddonList) action(item);
            foreach (var item in m_AllList) action(item);
        }

        void ClearPackageManagerSelection(System.Collections.Generic.List<PackageManagerItem> list)
        {
            foreach (var item in list) item.Checked = false;
        }

        void ToggleAutoLoadForList(System.Collections.Generic.List<PackageManagerItem> list, ref bool needMove, bool trackMove)
        {
            foreach (var item in list)
            {
                if (!item.Checked) continue;
                item.AutoLoad = !item.AutoLoad;
                if (item.AutoLoad)
                {
                    if (m_AutoLoadPackages.Add(item.Uid)) m_PkgMgrCategoryCounts["Auto-Load (AL)"]++;
                    if (trackMove) needMove = true;
                }
                else
                {
                    if (m_AutoLoadPackages.Remove(item.Uid)) m_PkgMgrCategoryCounts["Auto-Load (AL)"]--;
                }
                AutoLoadPackagesManager.Instance.SetAutoLoad(item.Uid, item.AutoLoad, false);
            }
        }



        string HighlightSearchText(string text, string search)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(text)) return text;
            int idx = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return text;
            
            string original = text.Substring(idx, search.Length);
            return text.Replace(original, "<color=yellow>" + original + "</color>");
        }

        private struct HighlightRange
        {
            public int Start;
            public int End;
            public HighlightRange(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        private string HighlightSearchTerms(string text, string[] termsLower)
        {
            if (string.IsNullOrEmpty(text) || termsLower == null || termsLower.Length == 0) return text;

            var ranges = new System.Collections.Generic.List<HighlightRange>();

            for (int t = 0; t < termsLower.Length; t++)
            {
                string term = termsLower[t];
                if (string.IsNullOrEmpty(term)) continue;

                int startIndex = 0;
                while (startIndex < text.Length)
                {
                    int idx = text.IndexOf(term, startIndex, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    ranges.Add(new HighlightRange(idx, idx + term.Length));
                    startIndex = idx + term.Length;
                }
            }

            if (ranges.Count == 0) return text;
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

            var merged = new System.Collections.Generic.List<HighlightRange>();
            int curStart = ranges[0].Start;
            int curEnd = ranges[0].End;
            for (int i = 1; i < ranges.Count; i++)
            {
                int s = ranges[i].Start;
                int e = ranges[i].End;
                if (s <= curEnd)
                {
                    if (e > curEnd) curEnd = e;
                }
                else
                {
                    merged.Add(new HighlightRange(curStart, curEnd));
                    curStart = s;
                    curEnd = e;
                }
            }
            merged.Add(new HighlightRange(curStart, curEnd));

            System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length + merged.Count * 20);
            int pos = 0;
            for (int i = 0; i < merged.Count; i++)
            {
                int s = merged[i].Start;
                int e = merged[i].End;
                if (s > pos) sb.Append(text.Substring(pos, s - pos));
                sb.Append("<color=yellow>");
                sb.Append(text.Substring(s, e - s));
                sb.Append("</color>");
                pos = e;
            }
            if (pos < text.Length) sb.Append(text.Substring(pos));
            return sb.ToString();
        }









        void PerformMove(System.Collections.Generic.List<PackageManagerItem> sourceList, bool isMovingToAll)
        {
            if (m_MovePkgMgrCo != null) StopCoroutine(m_MovePkgMgrCo);
            m_MovePkgMgrCo = StartCoroutine(PerformMoveCo(sourceList, isMovingToAll));
        }

        private Coroutine m_MovePkgMgrCo;
        private System.Collections.IEnumerator PerformMoveCo(System.Collections.Generic.List<PackageManagerItem> sourceList, bool isMovingToAll)
        {
            HashSet<string> toMoveUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lockedSelected = 0;
            foreach (var item in sourceList)
            {
                if (item.Checked)
                {
                    if (item.Locked)
                    {
                        lockedSelected++;
                        continue;
                    }
                    toMoveUids.Add(item.Uid);
                    if (!isMovingToAll && Settings.Instance.LoadDependenciesWithPackage.Value)
                    {
                        ProtectPackage(item.Uid, toMoveUids);
                    }
                }
            }

            string actionLabel = isMovingToAll ? "Unload" : "Load";
            if (toMoveUids.Count == 0)
            {
                if (lockedSelected > 0)
                {
                    m_PkgMgrStatusMessage = string.Format("Skipped {0} locked packages.", lockedSelected);
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                }
                else
                {
                    m_PkgMgrStatusMessage = "No packages selected.";
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                }
                yield break;
            }

            if (!ConfirmPackageManagerAction(actionLabel, toMoveUids.Count)) yield break;

            int movedCount = 0;
            int conflictCount = 0;
            int failedCount = 0;
            int processed = 0;

            var detailLines = new System.Collections.Generic.List<string>();
            var undoMoves = new System.Collections.Generic.List<PackageManagerMoveRecord>();

            string fromPrefix = isMovingToAll ? "AddonPackages" : "AllPackages";
            string toPrefix = isMovingToAll ? "AllPackages" : "AddonPackages";
            
            // We'll iterate through all items in our unified list to find those that need moving
            foreach (var item in m_UnifiedList)
            {
                if (toMoveUids.Contains(item.Uid))
                {
                    if (item.Locked) continue;

                    bool shouldMove = isMovingToAll ? item.IsInAddonList : !item.IsInAddonList;
                    bool pathMatches = item.Path.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase);

                    if (shouldMove && pathMatches)
                    {
                        string targetPath = toPrefix + item.Path.Substring(fromPrefix.Length);
                        string dir = Path.GetDirectoryName(targetPath);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        
                        if (File.Exists(targetPath)) 
                        { 
                            conflictCount++; 
                            if (detailLines.Count < 50) detailLines.Add("CONFLICT: " + item.Uid + " -> " + targetPath); 
                        }
                        else
                        {
                            try 
                            { 
                                File.Move(item.Path, targetPath); 
                                string oldPath = item.Path;
                                
                                // Update in-place
                                item.Path = targetPath;
                                item.IsInAddonList = !isMovingToAll;
                                UpdatePkgMgrItemCache(item);
                                FileManager.UpdatePackagePath(item.Uid, oldPath, targetPath);
                                
                                // Update Addon/All lists membership without re-sorting them yet
                                if (isMovingToAll)
                                {
                                    m_AddonList.Remove(item);
                                    if (!m_AllList.Contains(item)) m_AllList.Add(item);
                                }
                                else
                                {
                                    m_AllList.Remove(item);
                                    if (!m_AddonList.Contains(item)) m_AddonList.Add(item);
                                }

                                movedCount++; 
                                undoMoves.Add(new PackageManagerMoveRecord(item.Uid, oldPath, targetPath)); 
                            }
                            catch (Exception ex) 
                            { 
                                failedCount++; 
                                if (detailLines.Count < 50) detailLines.Add("FAILED: " + item.Uid + " -> " + targetPath + " | " + ex.Message); 
                                LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); 
                            }
                        }
                    }

                    processed++;
                    if ((processed % 10) == 0)
                    {
                        m_PkgMgrStatusMessage = string.Format("{0}ing... {1}/{2} processed", actionLabel, processed, toMoveUids.Count);
                        m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 1f;
                        yield return null;
                    }
                }
            }

            string status = string.Format("{0} complete: moved {1}", actionLabel, movedCount);
            if (lockedSelected > 0) status += string.Format(", locked {0}", lockedSelected);
            if (conflictCount > 0) status += string.Format(", conflicts {0}", conflictCount);
            if (failedCount > 0) status += string.Format(", failed {0}", failedCount);
            m_PkgMgrStatusMessage = status + ".";
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;

            if (undoMoves.Count > 0)
            {
                PushPkgMgrUndo(actionLabel, undoMoves);
                
                // Final cleanup and refresh
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                
                // Ensure UI is updated (status colors etc)
                RefreshPkgMgrUGUIList();
            }
            
            m_MovePkgMgrCo = null;
        }

        private void PerformKeepSelectedUnloadRest()
        {
            if (m_PkgMgrIsolateCo != null) StopCoroutine(m_PkgMgrIsolateCo);
            m_PkgMgrIsolateCo = StartCoroutine(PerformKeepSelectedUnloadRestCo());
        }

        private System.Collections.IEnumerator PerformKeepSelectedUnloadRestCo()
        {
            HashSet<string> keepUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in m_AddonList) if (item.Checked) ProtectPackage(item.Uid, keepUids);
            foreach (var item in m_AllList) if (item.Checked) ProtectPackage(item.Uid, keepUids);
            foreach (var item in m_AddonList) if (item.IsActive) ProtectPackage(item.Uid, keepUids);

            int candidateMoves = 0;
            int lockedSkipped = 0;
            foreach (var item in m_AddonList)
            {
                if (keepUids.Contains(item.Uid)) continue;
                if (item.Locked) { lockedSkipped++; continue; }
                candidateMoves++;
            }
            foreach (var item in m_AllList)
            {
                if (!keepUids.Contains(item.Uid)) continue;
                if (item.Locked) { lockedSkipped++; continue; }
                candidateMoves++;
            }

            if (candidateMoves == 0)
            {
                m_PkgMgrStatusMessage = lockedSkipped > 0 ? string.Format("No changes: {0} locked packages skipped.", lockedSkipped) : "No changes needed.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                m_PkgMgrIsolateCo = null;
                yield break;
            }

            if (!ConfirmPackageManagerAction("Isolate", candidateMoves))
            {
                m_PkgMgrIsolateCo = null;
                yield break;
            }

            int movedToAll = 0;
            int movedToAddon = 0;
            int conflictCount = 0;
            int failedCount = 0;
            int processed = 0;
            var undoMoves = new System.Collections.Generic.List<PackageManagerMoveRecord>();

            // Collect moves first to avoid list modification issues during iteration
            var toUnload = new List<PackageManagerItem>();
            foreach (var item in m_AddonList)
            {
                if (!item.Locked && !keepUids.Contains(item.Uid)) toUnload.Add(item);
            }
            
            var toLoad = new List<PackageManagerItem>();
            foreach (var item in m_AllList)
            {
                if (!item.Locked && keepUids.Contains(item.Uid)) toLoad.Add(item);
            }

            foreach (var item in toUnload)
            {
                string targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) { conflictCount++; processed++; continue; }

                try 
                { 
                    string oldPath = item.Path;
                    File.Move(item.Path, targetPath); 
                    
                    // Update in-place
                    item.Path = targetPath;
                    item.IsInAddonList = false;
                    UpdatePkgMgrItemCache(item);
                    FileManager.UpdatePackagePath(item.Uid, oldPath, targetPath);
                    
                    m_AddonList.Remove(item);
                    if (!m_AllList.Contains(item)) m_AllList.Add(item);

                    movedToAll++; 
                    undoMoves.Add(new PackageManagerMoveRecord(item.Uid, oldPath, targetPath)); 
                }
                catch (Exception ex) { failedCount++; LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }

                processed++;
                if ((processed % 25) == 0)
                {
                    m_PkgMgrStatusMessage = string.Format("Isolating... {0}/{1} changes processed", processed, candidateMoves);
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 1f;
                    yield return null;
                }
            }

            foreach (var item in toLoad)
            {
                string targetPath = "AddonPackages" + item.Path.Substring("AllPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) { conflictCount++; processed++; continue; }

                try 
                { 
                    string oldPath = item.Path;
                    File.Move(item.Path, targetPath); 
                    
                    // Update in-place
                    item.Path = targetPath;
                    item.IsInAddonList = true;
                    UpdatePkgMgrItemCache(item);
                    FileManager.UpdatePackagePath(item.Uid, oldPath, targetPath);

                    m_AllList.Remove(item);
                    if (!m_AddonList.Contains(item)) m_AddonList.Add(item);

                    movedToAddon++; 
                    undoMoves.Add(new PackageManagerMoveRecord(item.Uid, oldPath, targetPath)); 
                }
                catch (Exception ex) { failedCount++; LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }

                processed++;
                if ((processed % 25) == 0)
                {
                    m_PkgMgrStatusMessage = string.Format("Isolating... {0}/{1} changes processed", processed, candidateMoves);
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 1f;
                    yield return null;
                }
            }

            if (movedToAll > 0 || movedToAddon > 0)
            {
                string status = string.Format("Isolate complete: Kept {0} (Loaded {1}, Unloaded {2})", keepUids.Count, movedToAddon, movedToAll);
                if (lockedSkipped > 0) status += string.Format(", locked {0}", lockedSkipped);
                if (conflictCount > 0) status += string.Format(", conflicts {0}", conflictCount);
                if (failedCount > 0) status += string.Format(", failed {0}", failedCount);
                m_PkgMgrStatusMessage = status + ".";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;

                if (undoMoves.Count > 0)
                {
                    PushPkgMgrUndo("Isolate", undoMoves);
                }

                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                RefreshPkgMgrUGUIList();
            }
            else
            {
                m_PkgMgrStatusMessage = lockedSkipped > 0 ? string.Format("No changes: {0} locked packages skipped.", lockedSkipped) : "No changes needed.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }

            m_PkgMgrIsolateCo = null;
        }

        private void UndoLastPackageManagerOperation()
        {
            if (m_PkgMgrUndoStack == null || m_PkgMgrUndoStack.Count == 0)
            {
                m_PkgMgrStatusMessage = "Nothing to undo.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                return;
            }

            var op = m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1];
            if (op == null || op.Moves == null || op.Moves.Count == 0)
            {
                m_PkgMgrUndoStack.RemoveAt(m_PkgMgrUndoStack.Count - 1);
                m_PkgMgrStatusMessage = "Nothing to undo.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                return;
            }

            int undone = 0;
            int conflict = 0;
            int missing = 0;
            int failed = 0;

            var detailLines = new System.Collections.Generic.List<string>();
            var remaining = new System.Collections.Generic.List<PackageManagerMoveRecord>();

            var moves = op.Moves;
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                var mv = moves[i];
                if (string.IsNullOrEmpty(mv.To) || string.IsNullOrEmpty(mv.From)) { failed++; continue; }
                if (!File.Exists(mv.To)) { missing++; remaining.Add(mv); if (detailLines.Count < 50) detailLines.Add("MISSING: " + mv.Uid + " expected at " + mv.To); continue; }
                if (File.Exists(mv.From)) { conflict++; remaining.Add(mv); if (detailLines.Count < 50) detailLines.Add("CONFLICT: " + mv.Uid + " already exists at " + mv.From); continue; }

                try
                {
                    string dir = Path.GetDirectoryName(mv.From);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Move(mv.To, mv.From);

                    // Update in-place
                    PackageManagerItem item = null;
                    foreach (var it in m_UnifiedList) if (it.Uid == mv.Uid) { item = it; break; }

                    if (item != null)
                    {
                        item.Path = mv.From;
                        bool nowInAddon = mv.From.StartsWith("AddonPackages", StringComparison.OrdinalIgnoreCase);
                        item.IsInAddonList = nowInAddon;
                        UpdatePkgMgrItemCache(item);

                        if (nowInAddon)
                        {
                            m_AllList.Remove(item);
                            if (!m_AddonList.Contains(item)) m_AddonList.Add(item);
                        }
                        else
                        {
                            m_AddonList.Remove(item);
                            if (!m_AllList.Contains(item)) m_AllList.Add(item);
                        }
                    }
                    FileManager.UpdatePackagePath(mv.Uid, mv.To, mv.From);

                    undone++;
                }
                catch (Exception ex)
                {
                    failed++;
                    remaining.Add(mv);
                    if (detailLines.Count < 50) detailLines.Add("FAILED: " + mv.Uid + " undo " + mv.To + " -> " + mv.From + " | " + ex.Message);
                    LogUtil.LogError("Undo failed " + mv.To + " -> " + mv.From + ": " + ex.Message);
                }
            }

            string status = string.Format("Undo complete: moved {0}", undone);
            if (conflict > 0) status += string.Format(", conflicts {0}", conflict);
            if (missing > 0) status += string.Format(", missing {0}", missing);
            if (failed > 0) status += string.Format(", failed {0}", failed);
            m_PkgMgrStatusMessage = status + ".";
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;

            if (remaining.Count == 0)
            {
                m_PkgMgrUndoStack.RemoveAt(m_PkgMgrUndoStack.Count - 1);
            }
            else
            {
                op.Moves = remaining;
                m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1] = op;
            }

            if (undone > 0)
            {
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                RefreshPkgMgrUGUIList();
            }
        }

        private void PushPkgMgrUndo(string label, System.Collections.Generic.List<PackageManagerMoveRecord> moves)
        {
            if (moves == null || moves.Count == 0) return;
            if (m_PkgMgrUndoStack == null) m_PkgMgrUndoStack = new System.Collections.Generic.List<PackageManagerUndoOperation>();

            var op = new PackageManagerUndoOperation();
            op.Label = label;
            op.CreatedAt = Time.realtimeSinceStartup;
            op.Moves = moves;
            m_PkgMgrUndoStack.Add(op);

            if (m_PkgMgrUndoStack.Count > PkgMgrMaxUndoSteps)
            {
                m_PkgMgrUndoStack.RemoveAt(0);
            }
        }

        public void ScanPackageManagerPackages()
        {
            if (m_ScanPkgMgrCo != null) StopCoroutine(m_ScanPkgMgrCo);
            m_ScanPkgMgrCo = StartCoroutine(ScanPackageManagerPackagesCo());
            if (m_PkgMgrAnalysisCo == null) m_PkgMgrAnalysisCo = StartCoroutine(ProcessAnalysisQueue());
        }

        private System.Collections.IEnumerator ScanPackageManagerPackagesCo()
        {
            m_PkgMgrStatusMessage = "Scanning packages...";
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 1f;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            System.Collections.Generic.HashSet<string> protectedPackages = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (FileEntry.AutoInstallLookup != null)
            {
                foreach (var item in FileEntry.AutoInstallLookup)
                {
                    ProtectPackage(item, protectedPackages);
                    VarPackage p = FileManager.ResolveDependency(item);
                    if (p != null) ProtectPackage(p.Uid, protectedPackages);

                    if (sw.ElapsedMilliseconds > 16) { yield return null; sw.Reset(); sw.Start(); }
                }
            }

            string currentPackageUid = CurrentScenePackageUid;
            if (string.IsNullOrEmpty(currentPackageUid))
            {
                currentPackageUid = FileManager.CurrentPackageUid;
            }
            ProtectPackage(currentPackageUid, protectedPackages);

            var plugins = UnityEngine.Object.FindObjectsOfType<MVRScript>();
            foreach (var p in plugins)
            {
                try
                {
                    var fields = p.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach(var f in fields)
                    {
                        if (f.FieldType == typeof(JSONStorableUrl))
                        {
                            var jUrl = f.GetValue(p) as JSONStorableUrl;
                            if (jUrl != null && !string.IsNullOrEmpty(jUrl.val))
                            {
                                string pkg = GetPackageFromPath(jUrl.val);
                                if (pkg != null) ProtectPackage(pkg, protectedPackages);
                            }
                        }
                    }
                }
                catch (Exception) { }
                if (sw.ElapsedMilliseconds > 16) { yield return null; sw.Reset(); sw.Start(); }
            }

            // Build new categories/counts off-screen to avoid header flicker during scan.
            var newCategories = new System.Collections.Generic.List<string>();
            newCategories.Add("All");
            newCategories.Add("Active");
            newCategories.Add("Locked (L)");
            newCategories.Add("Auto-Load (AL)");
            newCategories.Add("Latest");
            newCategories.Add("Old Version");

            var newCategoryCounts = new System.Collections.Generic.Dictionary<string, int>();
            newCategoryCounts["All"] = 0;
            newCategoryCounts["Active"] = 0;
            newCategoryCounts["Locked (L)"] = 0;
            newCategoryCounts["Auto-Load (AL)"] = 0;
            newCategoryCounts["Latest"] = 0;
            newCategoryCounts["Old Version"] = 0;

            m_LockedPackages = LockedPackagesManager.Instance.GetLockedPackages();
            m_AutoLoadPackages = AutoLoadPackagesManager.Instance.GetAutoLoadPackages();

            System.Collections.Generic.HashSet<string> latestUids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = FileManager.GetPackageGroups();
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    if (g.NewestPackage != null) latestUids.Add(g.NewestPackage.Uid);
                    if (sw.ElapsedMilliseconds > 16) { yield return null; sw.Reset(); sw.Start(); }
                }
            }

            System.Collections.Generic.HashSet<string> types = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan into temporary lists to avoid clearing the visible UI lists (reduces "blink" during refresh)
            var newAddonList = new System.Collections.Generic.List<PackageManagerItem>();
            var newAllList = new System.Collections.Generic.List<PackageManagerItem>();

            yield return ScanDirectoryCo("AddonPackages", newAddonList, protectedPackages, types, latestUids, sw, newCategoryCounts);
            yield return ScanDirectoryCo("AllPackages", newAllList, protectedPackages, types, latestUids, sw, newCategoryCounts);

            // Swap in results atomically
            m_AddonList = newAddonList;
            m_AllList = newAllList;


            m_PkgMgrSelectedItem = null;
            m_PkgMgrSelectedThumbnail = null;
            m_PkgMgrSelectedDescription = "";
            
            var sortedTypes = new System.Collections.Generic.List<string>(types);
            sortedTypes.Sort();
            newCategories.AddRange(sortedTypes);

            // Swap in the new categories/counts at the end so the header doesn't disappear mid-scan.
            m_PkgMgrCategories = newCategories;
            m_PkgMgrCategoryCounts = newCategoryCounts;
            
            SortPackageManagerList();
            UpdatePkgMgrHighlights();
            if (GalleryPanel.BenchmarkStartTime > 0)
            {
                 UnityEngine.Debug.Log("[Benchmark] ScanPackageManagerPackagesCo FINISHED (List Populated) at " + Time.realtimeSinceStartup + " (+" + (Time.realtimeSinceStartup - GalleryPanel.BenchmarkStartTime).ToString("F3") + "s)");
            }
            m_PkgMgrIndicesDirty = true;
            m_ScanPkgMgrCo = null;
        }

        private struct GUIEnabled : IDisposable
        {
            private bool _previous;
            public GUIEnabled(bool enabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = enabled;
            }
            public void Dispose()
            {
                GUI.enabled = _previous;
            }
        }

        private void UpdatePkgMgrHighlights()
        {
            foreach (var item in m_AddonList) UpdatePkgMgrItemCache(item);
            foreach (var item in m_AllList) UpdatePkgMgrItemCache(item);
        }

        private void UpdatePkgMgrItemCache(PackageManagerItem item)
        {
            string highlighted = HighlightSearchTerms(item.Uid, m_PkgMgrFilterTermsLower);
            item.HighlightedUid = highlighted;
            item.HighlightedType = HighlightSearchTerms(item.Type, m_PkgMgrFilterTermsLower);
            string label = item.StatusPrefix + highlighted;
            if (item.AutoLoad) label = "<color=#add8e6>" + label + "</color>";
            string tooltip = item.Path;
            if (!string.IsNullOrEmpty(m_PkgMgrFilter) && !string.IsNullOrEmpty(item.FilterMatchSummary)) tooltip += "\nMatched: " + item.FilterMatchSummary;
            item.NameContent = new GUIContent(label, tooltip);
            
            string depTooltip = "";
            if ((item.UnloadedDependencies != null && item.UnloadedDependencies.Count > 0) || (item.NotFoundDependencies != null && item.NotFoundDependencies.Count > 0) || (item.MissingDependencies != null && item.MissingDependencies.Count > 0))
            {
                if (item.UnloadedDependencies != null && item.UnloadedDependencies.Count > 0)
                {
                    depTooltip += "Unloaded:\n" + string.Join("\n", item.UnloadedDependencies.ToArray());
                }
                if (item.NotFoundDependencies != null && item.NotFoundDependencies.Count > 0)
                {
                    if (!string.IsNullOrEmpty(depTooltip)) depTooltip += "\n\n";
                    depTooltip += "Not found:\n" + string.Join("\n", item.NotFoundDependencies.ToArray());
                }
                if (string.IsNullOrEmpty(depTooltip) && item.MissingDependencies != null && item.MissingDependencies.Count > 0)
                {
                    depTooltip = "Missing:\n" + string.Join("\n", item.MissingDependencies.ToArray());
                }
            }
            item.DepContent = new GUIContent(string.Format("{0} ({1})", item.DependencyCount, item.LoadedDependencyCount), depTooltip);
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024f).ToString("F1") + " KB";
            if (bytes < 1073741824) return (bytes / 1048576f).ToString("F1") + " MB";
            return (bytes / 1073741824f).ToString("F2") + " GB";
        }

        private string FormatAge(DateTime dt, DateTime now)
        {
            TimeSpan span = now - dt;
            if (span.TotalDays < 0) return "Future";
            if (span.TotalDays < 1) return "Today";
            if (span.TotalDays < 7) return (int)span.TotalDays + "d";
            if (span.TotalDays < 30) return (int)(span.TotalDays / 7) + "w";
            if (span.TotalDays < 365) return (int)(span.TotalDays / 30) + "m";
            return (int)(span.TotalDays / 365) + "y";
        }

        private void OnPackageManagerItemSelected(PackageManagerItem item, bool isLoaded)
        {
            if (item != null && item.Checked)
            {
                if (isLoaded)
                {
                    ClearPackageManagerSelection(m_AllList);
                }
                else
                {
                    ClearPackageManagerSelection(m_AddonList);
                }
            }

            if (m_PkgMgrSelectedItem == item) return;
            m_PkgMgrSelectedItem = item;
            m_PkgMgrSelectedDescription = "";
            m_PkgMgrSelectedThumbnail = null;
            m_PkgMgrLastThumbnailPath = "";

            if (item == null) return;

            VarPackage pkg = FileManager.GetPackage(item.Uid, false);
            if (pkg != null)
            {
                m_PkgMgrSelectedDescription = pkg.Description;
            }

            string imgPath = "";
            
            string scenePath = GetFirstScenePath(item.Uid);
            if (!string.IsNullOrEmpty(scenePath))
            {
                string sceneImg = Path.ChangeExtension(scenePath, ".jpg");
                if (FileManager.FileExists(sceneImg)) imgPath = sceneImg;
                else
                {
                    sceneImg = Path.ChangeExtension(scenePath, ".png");
                    if (FileManager.FileExists(sceneImg)) imgPath = sceneImg;
                }
            }

            if (string.IsNullOrEmpty(imgPath))
            {
                string testJpg = Path.ChangeExtension(item.Path, ".jpg");
                if (FileManager.FileExists(testJpg)) imgPath = testJpg;
                else
                {
                    string testPng = Path.ChangeExtension(item.Path, ".png");
                    if (FileManager.FileExists(testPng)) imgPath = testPng;
                }
            }

            if (string.IsNullOrEmpty(imgPath))
            {
                if (pkg != null && pkg.FileEntries != null)
                {
                    foreach (var entry in pkg.FileEntries)
                    {
                        string internalPath = entry.InternalPath.ToLowerInvariant();
                        if (internalPath.EndsWith(".jpg") || internalPath.EndsWith(".png") || internalPath.EndsWith(".jpeg"))
                        {
                            imgPath = item.Uid + ":/" + entry.InternalPath;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(imgPath))
            {
                m_PkgMgrLastThumbnailPath = imgPath;
                if (CustomImageLoaderThreaded.singleton != null)
                {
                    Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
                    if (tex != null)
                    {
                        m_PkgMgrSelectedThumbnail = tex;
                    }
                    else
                    {
                        CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
                        qi.imgPath = imgPath;
                        qi.isThumbnail = true;
                        qi.priority = 1;
                        qi.callback = (res) => {
                            if (res != null && res.tex != null && m_PkgMgrLastThumbnailPath == res.imgPath)
                            {
                                m_PkgMgrSelectedThumbnail = res.tex;
                                
                                if (!res.loadedFromGalleryCache)
                                {
                                    long writeTime = 0;
                                    if (GalleryThumbnailCache.Instance.IsPackagePath(res.imgPath))
                                    {
                                        writeTime = 0;
                                    }
                                    else
                                    {
                                        writeTime = item.LastWriteTime.ToFileTime();
                                        FileEntry fe = FileManager.GetFileEntry(res.imgPath);
                                        if (fe != null) writeTime = fe.LastWriteTime.ToFileTime();
                                    }
                                    StartCoroutine(GalleryThumbnailCache.Instance.GenerateAndSaveThumbnailRoutine(res.imgPath, res.tex, writeTime));
                                }
                            }
                        };
                        CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
                    }
                }
            }
        }

        private string GetItemThumbnailPath(PackageManagerItem item)
        {
            if (item == null) return "";
            if (item.ThumbnailPath != null) return item.ThumbnailPath;

            string imgPath = "";
            
            string scenePath = GetFirstScenePath(item.Uid);
            if (!string.IsNullOrEmpty(scenePath))
            {
                string sceneImg = Path.ChangeExtension(scenePath, ".jpg");
                if (FileManager.FileExists(sceneImg)) imgPath = sceneImg;
                else
                {
                    sceneImg = Path.ChangeExtension(scenePath, ".png");
                    if (FileManager.FileExists(sceneImg)) imgPath = sceneImg;
                }
            }

            if (string.IsNullOrEmpty(imgPath))
            {
                string testJpg = Path.ChangeExtension(item.Path, ".jpg");
                if (FileManager.FileExists(testJpg)) imgPath = testJpg;
                else
                {
                    string testPng = Path.ChangeExtension(item.Path, ".png");
                    if (FileManager.FileExists(testPng)) imgPath = testPng;
                }
            }

            if (string.IsNullOrEmpty(imgPath))
            {
                VarPackage pkg = FileManager.GetPackage(item.Uid, false);
                if (pkg != null && pkg.FileEntries != null)
                {
                    foreach (var entry in pkg.FileEntries)
                    {
                        string internalPath = entry.InternalPath.ToLowerInvariant();
                        if (internalPath.EndsWith(".jpg") || internalPath.EndsWith(".png") || internalPath.EndsWith(".jpeg"))
                        {
                            imgPath = item.Uid + ":/" + entry.InternalPath;
                            break;
                        }
                    }
                }
            }

            item.ThumbnailPath = imgPath;
            return imgPath;
        }

        private System.Collections.IEnumerator ScanDirectoryCo(string path, System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.HashSet<string> protectedPackages, System.Collections.Generic.HashSet<string> types, System.Collections.Generic.HashSet<string> latestUids, System.Diagnostics.Stopwatch sw, System.Collections.Generic.Dictionary<string, int> categoryCounts)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            DirectoryInfo di = new DirectoryInfo(path);
            
            FileInfo[] files = null;
            bool done = false;
            System.Threading.ThreadPool.QueueUserWorkItem((state) => {
                try {
                    List<string> fileList = new List<string>();
                    FileManager.SafeGetFiles(path, "*.var", fileList);
                    files = fileList.Select(f => new FileInfo(f)).ToArray();
                } catch (Exception ex) {
                    LogUtil.LogError("[VPB] ScanDirectoryCo GetFiles error: " + ex.Message);
                    files = new FileInfo[0];
                } finally {
                    done = true;
                }
            });

            while (!done) yield return null;
            if (sw.ElapsedMilliseconds > 16) { yield return null; sw.Reset(); sw.Start(); }

            DateTime now = DateTime.Now;
            foreach (var file in files)
            {
                string itemPath = file.FullName.Replace('\\', '/');
                string relativePath = itemPath;
                int idx = itemPath.IndexOf(path, StringComparison.OrdinalIgnoreCase);
                if (idx != -1) relativePath = itemPath.Substring(idx);

                string name = Path.GetFileNameWithoutExtension(itemPath);
                bool isProtected = protectedPackages.Contains(name);
                bool isLocked = m_LockedPackages.Contains(name);
                bool isAutoLoad = m_AutoLoadPackages.Contains(name);
                bool isLatest = latestUids.Contains(name);
                DateTime lwt = file.CreationTime;
                
                string type = "Unknown";
                string description = "";

                PackageCachedInfo cached;
                if (m_PackageCache.TryGetValue(name, out cached))
                {
                    if (cached.LastWriteTime == lwt && cached.Size == file.Length)
                    {
                        type = cached.Type;
                        description = cached.Description;
                    }
                }

                // If not cached, we skip DeterminePackageType and GetPackage(Description) to avoid disk I/O / Zip open
                // They will be populated by the analysis queue

                var item = new PackageManagerItem {
                    Uid = name,
                    Creator = PackageIDToCreator(name),
                    Path = relativePath,
                    Type = type,
                    Size = file.Length,
                    LastWriteTime = lwt,
                    AgeString = FormatAge(lwt, now),
                    Description = description,
                    AllDependencies = null,
                    DependencyCount = -1,
                    LoadedDependencyCount = 0,
                    MissingDependencies = null,
                    UnloadedDependencies = null,
                    NotFoundDependencies = null,
                    StatusPrefix = (isLocked ? "(L) " : "") + (isAutoLoad ? "(AL) " : "") + (isProtected ? "(A) " : "") + (!isLatest ? "(O) " : ""),
                    TypeContent = new GUIContent(type, relativePath),
                    SizeContent = new GUIContent(FormatSize(file.Length), file.Length.ToString("N0") + " bytes"),
                    Checked = false,
                    IsActive = isProtected,
                    Locked = isLocked,
                    AutoLoad = isAutoLoad,
                    IsLatest = isLatest,
                    GroupId = FileManager.PackageIDToPackageGroupID(name)
                };
                
                // Add to analysis queue (always, to check deps, but if cached metadata we can skip that part)
                m_PkgMgrAnalysisQueue.Enqueue(item);
                UpdatePkgMgrItemCache(item);
                list.Add(item);

                if (type != "Unknown") 
                {
                    types.Add(type);
                    if (!categoryCounts.ContainsKey(type)) categoryCounts[type] = 0;
                    categoryCounts[type]++;
                }
                
                categoryCounts["All"]++;
                if (isProtected) categoryCounts["Active"]++;
                if (isLocked) categoryCounts["Locked (L)"]++;
                if (isAutoLoad) categoryCounts["Auto-Load (AL)"]++;
                if (isLatest) categoryCounts["Latest"]++;
                else categoryCounts["Old Version"]++;

                if (sw.ElapsedMilliseconds > 16) { yield return null; sw.Reset(); sw.Start(); }
            }
        }

        private int ComparePackageManagerItems(PackageManagerItem a, PackageManagerItem b)
        {
            int result = 0;
            switch (m_PkgMgrSortField)
            {
                case "Creator": result = string.Compare(a.Creator, b.Creator, StringComparison.OrdinalIgnoreCase); break;
                case "Category": result = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase); break;
                case "Name": result = string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase); break;
                case "Path": result = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
                case "Size": result = a.Size.CompareTo(b.Size); break;
                case "Age": result = a.LastWriteTime.CompareTo(b.LastWriteTime); break;
                case "Deps": result = a.DependencyCount.CompareTo(b.DependencyCount); break;
            }
            
            if (result == 0 && m_PkgMgrSortField != "Name")
            {
                result = string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
            }
            return m_PkgMgrSortAscending ? result : -result;
        }

        void SortPackageManagerList()
        {
            m_AddonList.Sort(ComparePackageManagerItems);
            m_AllList.Sort(ComparePackageManagerItems);
        }

    }
}
