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
            public float LastWidth;
            public float LastTypeWidth;
            public GUIContent TruncatedNameContent;
            public GUIContent TruncatedTypeContent;
        }
        private bool m_ShowPackageManagerWindow = false;
        private System.Collections.Generic.List<PackageManagerItem> m_AddonList = new System.Collections.Generic.List<PackageManagerItem>();
        private System.Collections.Generic.List<PackageManagerItem> m_AllList = new System.Collections.Generic.List<PackageManagerItem>();
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
        private Vector2 m_AddonScroll = Vector2.zero;
        private Vector2 m_AllScroll = Vector2.zero;
        private Vector2 m_PkgMgrCategoryScroll = Vector2.zero;
        private Rect m_PackageManagerWindowRect = new Rect(100, 100, 1300, 600);
        private string m_PkgMgrSortField = "Name";
        private bool m_PkgMgrSortAscending = true;
        private int m_AddonLastSelectedIndex = -1;
        private int m_AllLastSelectedIndex = -1;
        private bool m_PkgMgrIsDragging = false;
        private bool m_PkgMgrDragChecked = false;
        private int m_PkgMgrAddonCount = 0;
        private int m_PkgMgrAllCount = 0;
        private int m_PkgMgrDragLastIdx = -1;
        private int m_AddonShiftAnchorIndex = -1;
        private int m_AllShiftAnchorIndex = -1;
        
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
        private bool m_PkgMgrIndicesDirty = true;
        private int m_PkgMgrSelectedCount = 0;
        private int m_PkgMgrTargetGroupCount = 0;
        private bool m_PkgMgrIsVR = false;
        private PackageManagerItem m_PkgMgrSelectedItem = null;
        private Texture2D m_PkgMgrSelectedThumbnail = null;
        private string m_PkgMgrSelectedDescription = "";
        private int m_PkgMgrSelectedTab = 0;
        private string[] m_PkgMgrTabs = { "Info", "Deps", "Actions" };
        private string m_PkgMgrLastThumbnailPath = "";
        private Vector2 m_PkgMgrInfoScroll = Vector2.zero;
        private bool m_PkgMgrSelectedInLoaded = true;
        private int m_AddonFirstVisible, m_AddonLastVisible;
        private int m_AllFirstVisible, m_AllLastVisible;
        private float m_PkgMgrTopHeight = 150f;
        private float m_PkgMgrFrozenTopHeight = 0f;
        private bool m_PkgMgrWasBusy = false;
        private const float PkgMgrMessageBarHeight = 24f;
        private float m_PkgMgrSplitRatio = 0.66f;
        private bool m_PkgMgrShowPreview = true;
        private float m_PkgMgrLoadedPaneHeight = 0f;
        private float m_PkgMgrAllPaneHeight = 0f;
        private string m_PkgMgrPreviewHint = "";
        private string m_PkgMgrLastTooltip = "";
        private string m_PkgMgrLastOperationDetails = "";
        private System.Collections.Generic.List<PackageManagerUndoOperation> m_PkgMgrUndoStack = new System.Collections.Generic.List<PackageManagerUndoOperation>();
        private const int PkgMgrMaxUndoSteps = 10;

        private void RefreshVisibleIndices()
        {
            RefreshVisibleRows(m_AddonList, m_AddonVisibleRows);
            RefreshVisibleRows(m_AllList, m_AllVisibleRows);
            m_PkgMgrIndicesDirty = false;
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
            return m_ScanPkgMgrCo != null || m_PkgMgrIsolateCo != null;
        }

        private void SetPkgMgrFilter(string filter)
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

        private void TogglePackageManagerUI()
        {
            bool useUGUI = Settings.Instance != null && Settings.Instance.UseUGUIPackageManager != null && Settings.Instance.UseUGUIPackageManager.Value;
            if (useUGUI)
            {
                if (IsPackageManagerUGUIVisible()) ClosePackageManagerUGUI();
                else OpenPackageManagerUGUI();
                return;
            }

            if (IsPackageManagerUGUIVisible()) ClosePackageManagerUGUI();
            if (m_ShowPackageManagerWindow) m_ShowPackageManagerWindow = false;
            else OpenPackageManagerWindow();
        }

        private int GetPkgMgrShiftAnchorIndex(System.Collections.Generic.List<PackageManagerItem> list)
        {
            return (list == m_AddonList) ? m_AddonShiftAnchorIndex : m_AllShiftAnchorIndex;
        }

        private void SetPkgMgrShiftAnchorIndex(System.Collections.Generic.List<PackageManagerItem> list, int value)
        {
            if (list == m_AddonList) m_AddonShiftAnchorIndex = value;
            else m_AllShiftAnchorIndex = value;
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

        // Desktop Context Menu
        private bool m_ShowDesktopContextMenu = false;
        private Rect m_DesktopContextMenuRect = new Rect(0, 0, 250, 100);
        private PackageManagerItem m_ContextMenuTargetItem;
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

        void DrawPackageManagerHeader(string label, string field, string tooltip = "", float width = -1)
        {
            string sortIndicator = "";
            if (m_PkgMgrSortField == field)
            {
                sortIndicator = m_PkgMgrSortAscending ? " ▲" : " ▼";
            }

            bool clicked;
            GUIContent content = new GUIContent(label + sortIndicator, tooltip);
            if (width > 0) clicked = GUILayout.Button(content, m_StylePkgMgrHeader, GUILayout.Width(width));
            else clicked = GUILayout.Button(content, m_StylePkgMgrHeader);

            if (clicked)
            {
                if (m_PkgMgrSortField == field) m_PkgMgrSortAscending = !m_PkgMgrSortAscending;
                else
                {
                    m_PkgMgrSortField = field;
                    m_PkgMgrSortAscending = true;
                }
                Settings.Instance.PackageManagerSortField.Value = m_PkgMgrSortField;
                Settings.Instance.PackageManagerSortAscending.Value = m_PkgMgrSortAscending;
                SortPackageManagerList();
            }
        }

        void DrawPackageManagerWindow(int windowID)
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) 
            {
                Input.ResetInputAxes();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.A)
            {
                if (Event.current.mousePosition.x < m_PackageManagerWindowRect.width / 2) SelectAllPackageManager(m_AddonList, true);
                else SelectAllPackageManager(m_AllList, true);
                Event.current.Use();
            }

            if (m_PkgMgrIndicesDirty) RefreshVisibleIndices();

            float windowWidth = m_PackageManagerWindowRect.width;
            float windowHeight = m_PackageManagerWindowRect.height;
            Rect packageManagerRect = new Rect(0, 0, windowWidth, windowHeight);
            bool isEventInsidePackageManager = packageManagerRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.ScrollWheel && isEventInsidePackageManager)
            {
                Input.ResetInputAxes();
            }

            if (Event.current.type == EventType.Repaint)
            {
                m_StylePanel.Draw(new Rect(0, 0, windowWidth, windowHeight), false, false, false, false);
            }

            GUILayout.BeginArea(new Rect(8, 8, windowWidth - 16, windowHeight - 16));
            GUILayout.BeginVertical();

            GUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Package Manager", m_StyleHeader, GUILayout.ExpandWidth(false));
            
            GUILayout.Space(20);
            GUILayout.Label(new GUIContent("Filter:", "Search name, creator, description, type, path, dependencies, or status"), GUILayout.Width(40));
            GUI.SetNextControlName("PkgMgrFilter");
            string newPkgMgrFilter = GUILayout.TextField(m_PkgMgrFilter, GUILayout.MinWidth(100), GUILayout.MaxWidth(400));
            if (newPkgMgrFilter != m_PkgMgrFilter)
            {
                m_PkgMgrFilter = newPkgMgrFilter;
                m_PkgMgrFilterLower = m_PkgMgrFilter.ToLower();
                m_PkgMgrFilterTermsLower = m_PkgMgrFilterLower.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                m_PkgMgrIndicesDirty = true;
                UpdatePkgMgrHighlights();
            }

            if (m_PkgMgrFilterTermsLower != null && m_PkgMgrFilterTermsLower.Length > 0)
            {
                GUILayout.Space(10);
                bool newRel = GUILayout.Toggle(m_PkgMgrUseRelevanceSort, new GUIContent("Relevance", "When enabled, filtered results are ordered by closest match."), GUILayout.ExpandWidth(false));
                if (newRel != m_PkgMgrUseRelevanceSort) { m_PkgMgrUseRelevanceSort = newRel; m_PkgMgrIndicesDirty = true; }
            }

            if (GUI.GetNameOfFocusedControl() == "PkgMgrFilter" && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                m_PkgMgrFilter = "";
                m_PkgMgrFilterLower = "";
                m_PkgMgrFilterTermsLower = new string[0];
                m_PkgMgrIndicesDirty = true;
                UpdatePkgMgrHighlights();
                GUI.FocusControl("");
                Event.current.Use();
            }

            if (!string.IsNullOrEmpty(m_PkgMgrFilter))
            {
                if (GUILayout.Button(new GUIContent("X", "Clear search filter"), m_StyleButtonSmall, GUILayout.Width(25)))
                {
                    m_PkgMgrFilter = "";
                    m_PkgMgrFilterLower = "";
                    m_PkgMgrFilterTermsLower = new string[0];
                    m_PkgMgrIndicesDirty = true;
                    UpdatePkgMgrHighlights();
                    GUI.FocusControl("");
                }
            }

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(m_PkgMgrCreatorFilter))
            {
                GUILayout.Space(20);
                if (GUILayout.Button(new GUIContent("Creator: " + m_PkgMgrCreatorFilter + " [X]", "Clear creator filter"), m_StyleButtonSmall, GUILayout.ExpandWidth(false)))
                {
                    m_PkgMgrCreatorFilter = "";
                    m_PkgMgrIndicesDirty = true;
                }
            }

            GUILayout.Space(20);
            if (GUILayout.Button(new GUIContent("Refresh", "Rescan packages on disk"), m_StyleButtonSmall, GUILayout.Width(60)))
            {
                if (IsPackageManagerBusy())
                {
                    m_PkgMgrStatusMessage = "Busy. Please wait for the current operation to finish.";
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                }
                else
                {
                    Refresh();
                    ScanPackageManagerPackages();
                }
            }

            GUILayout.Space(30);
            bool autoDeps = Settings.Instance.LoadDependenciesWithPackage.Value;
            bool newAutoDeps = GUILayout.Toggle(autoDeps, new GUIContent("Auto-Deps", "Automatically load dependencies when loading a package"), GUILayout.ExpandWidth(false));
            if (newAutoDeps != autoDeps) Settings.Instance.LoadDependenciesWithPackage.Value = newAutoDeps;
            GUILayout.Space(10);
            if (GUILayout.Button(new GUIContent("X", "Close Package Manager"), m_StyleButtonSmall, GUILayout.Width(30))) m_ShowPackageManagerWindow = false;
            GUILayout.EndHorizontal();

            if (m_PkgMgrCategories.Count > 0)
            {
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                float currentWidth = 0;
                float maxWidth = windowWidth - 32;
                
                foreach (var cat in m_PkgMgrCategories)
                {
                    bool isInc = m_PkgMgrCategoryInclusive.Contains(cat);
                    bool isExc = m_PkgMgrCategoryExclusive.Contains(cat);
                    bool isAll = (cat == "All");
                    bool isSelected = isAll ? (m_PkgMgrCategoryInclusive.Count == 0 && m_PkgMgrCategoryExclusive.Count == 0) : (isInc || isExc);
                    
                    int count = 0;
                    m_PkgMgrCategoryCounts.TryGetValue(cat, out count);
                    
                    string prefix = isInc ? "+ " : (isExc ? "- " : "");
                    string label = string.Format("{0}{1} ({2})", prefix, cat, count);
                    GUIContent content = new GUIContent(label, string.Format("Category: {0}. Left Click to include (+), Right Click to exclude (-)", cat));
                    float btnWidth = m_StyleButtonSmall.CalcSize(content).x + 10;

                    if (currentWidth + btnWidth + 5 > maxWidth && currentWidth > 0)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        currentWidth = 0;
                    }
                    currentWidth += btnWidth + 5;
                    
                    Rect r = GUILayoutUtility.GetRect(content, m_StyleButtonSmall, GUILayout.ExpandWidth(false));
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.button == 0)
                        {
                            if (isAll)
                            {
                                m_PkgMgrCategoryInclusive.Clear();
                                m_PkgMgrCategoryExclusive.Clear();
                            }
                            else
                            {
                                if (isInc) m_PkgMgrCategoryInclusive.Remove(cat);
                                else { m_PkgMgrCategoryInclusive.Add(cat); m_PkgMgrCategoryExclusive.Remove(cat); }
                            }
                            m_PkgMgrIndicesDirty = true;
                            Event.current.Use();
                        }
                        else if (Event.current.button == 1)
                        {
                            if (!isAll)
                            {
                                if (isExc) m_PkgMgrCategoryExclusive.Remove(cat);
                                else { m_PkgMgrCategoryExclusive.Add(cat); m_PkgMgrCategoryInclusive.Remove(cat); }
                                m_PkgMgrIndicesDirty = true;
                                Event.current.Use();
                            }
                        }
                    }
                    GUI.Toggle(r, isSelected, content, m_StyleButtonSmall);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) 
            {
                float h = GUILayoutUtility.GetLastRect().height;
                bool busyNow = IsPackageManagerBusy();
                if (busyNow)
                {
                    if (!m_PkgMgrWasBusy)
                    {
                        // Freeze to whatever the user last saw so the layout doesn't jump during operations
                        m_PkgMgrFrozenTopHeight = (m_PkgMgrTopHeight > 0) ? m_PkgMgrTopHeight : h;
                        m_PkgMgrWasBusy = true;
                    }
                    if (m_PkgMgrFrozenTopHeight <= 0f) m_PkgMgrFrozenTopHeight = h;
                    if (m_PkgMgrFrozenTopHeight < h) m_PkgMgrFrozenTopHeight = h;
                    m_PkgMgrTopHeight = m_PkgMgrFrozenTopHeight;
                }
                else
                {
                    // Not busy: allow normal dynamic layout
                    m_PkgMgrWasBusy = false;
                    m_PkgMgrFrozenTopHeight = 0f;
                    if (h > 0) m_PkgMgrTopHeight = h;
                }
            }
            
            GUILayout.Space(5);

            float previewWidth = m_PkgMgrShowPreview ? 320 : 0;
            float leftPaneWidth = windowWidth - previewWidth - (m_PkgMgrShowPreview ? 26 : 10); 
            
            const float verticalOverhead = 35f;
            
            float totalContentHeightAvailable = Mathf.Max(200f, windowHeight - m_PkgMgrTopHeight - verticalOverhead);
            float tablesContentHeight = totalContentHeightAvailable - PkgMgrMessageBarHeight - 5;
            
            float totalTableHeightAvailable = Mathf.Max(100f, tablesContentHeight - 105);
            
            float hTop = totalTableHeightAvailable * m_PkgMgrSplitRatio;
            float hBottom = totalTableHeightAvailable * (1f - m_PkgMgrSplitRatio);
            m_PkgMgrLoadedPaneHeight = Mathf.Max(0f, hTop);
            m_PkgMgrAllPaneHeight = Mathf.Max(0f, hBottom);

            GUILayout.BeginHorizontal(GUILayout.Height(totalContentHeightAvailable));
            
            int addonSelectedCount = 0;
            long addonSelectedSize = 0;
            foreach (var item in m_AddonList) if (item.Checked) { addonSelectedCount++; addonSelectedSize += item.Size; }
            int allSelectedCount = 0;
            long allSelectedSize = 0;
            foreach (var item in m_AllList) if (item.Checked) { allSelectedCount++; allSelectedSize += item.Size; }

            string filterSummary = string.IsNullOrEmpty(m_PkgMgrFilter) ? "" : " | filter '" + m_PkgMgrFilter + "'";

            GUILayout.BeginVertical(GUILayout.Width(leftPaneWidth));

            GUILayout.BeginVertical(GUILayout.Height(hTop + 35));
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Loaded ({0} | {1} vis | {2} sel | {3}){4}", m_PkgMgrAddonCount, m_AddonVisibleRows.Count, addonSelectedCount, FormatSize(addonSelectedSize), filterSummary), m_StyleSubHeader, GUILayout.Width(leftPaneWidth - 120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("All", "Select all currently visible loaded packages"), m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AddonList, true, m_AllList);
            if (GUILayout.Button(new GUIContent("None", "Deselect all packages in this list"), m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AddonList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AddonList, ref m_AddonScroll, ref m_AddonLastSelectedIndex, leftPaneWidth, hTop, ref m_AddonFirstVisible, ref m_AddonLastVisible);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal(m_StyleSection, GUILayout.Height(35));
            GUILayout.FlexibleSpace();
            bool isBusy = IsPackageManagerBusy();
            using (new GUIEnabled(!isBusy))
            {
                if (GUILayout.Button(new GUIContent("<b>▼ Unload ▼</b>", "Move selected packages to 'AllPackages' (Unloaded)"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) PerformMove(m_AddonList, true);
            GUILayout.Space(15);
                if (GUILayout.Button(new GUIContent("<b>[ Lock ]</b>", "Lock/Unlock selected packages to prevent accidental move/unload"), m_StyleButton, GUILayout.Width(75), GUILayout.Height(25))) ToggleLockSelection();
            GUILayout.Space(15);
                if (GUILayout.Button(new GUIContent("<b><color=#add8e6>[ Auto-Load ]</color></b>", "Toggle Auto-Load for selected packages. AL packages load automatically on startup"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) ToggleAutoLoadSelection();
            GUILayout.Space(15);
                if (GUILayout.Button(new GUIContent("<b>[ Isolate ]</b>", "Keep only selected/active packages and unload the rest"), m_StyleButton, GUILayout.Width(75), GUILayout.Height(25))) PerformKeepSelectedUnloadRest();
            GUILayout.Space(15);
                if (GUILayout.Button(new GUIContent("<b>▲ Load ▲</b>", "Move selected packages to 'AddonPackages' (Loaded)"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) PerformMove(m_AllList, false);
            }
            GUILayout.FlexibleSpace();
            
            string ratioLabel = "1:1";
            if (m_PkgMgrSplitRatio > 0.6f) ratioLabel = "2:1";
            else if (m_PkgMgrSplitRatio < 0.4f) ratioLabel = "1:2";

            if (GUILayout.Button(new GUIContent(ratioLabel, "Cycle table height ratio (Loaded vs Unloaded)"), m_StyleButtonSmall, GUILayout.Width(40), GUILayout.Height(25)))
            {
                if (ratioLabel == "2:1") m_PkgMgrSplitRatio = 0.5f;
                else if (ratioLabel == "1:1") m_PkgMgrSplitRatio = 0.33f;
                else m_PkgMgrSplitRatio = 0.66f;
                Settings.Instance.PackageManagerSplitRatio.Value = m_PkgMgrSplitRatio;
            }

            if (GUILayout.Button(new GUIContent(m_PkgMgrShowPreview ? ">" : "<", m_PkgMgrShowPreview ? "Hide preview pane" : "Show preview pane"), m_StyleButtonSmall, GUILayout.Width(25), GUILayout.Height(25)))
            {
                m_PkgMgrShowPreview = !m_PkgMgrShowPreview;
                Settings.Instance.PackageManagerShowPreview.Value = m_PkgMgrShowPreview;
            }

            bool canUndo = (m_PkgMgrUndoStack != null && m_PkgMgrUndoStack.Count > 0 && m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1].Moves != null && m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1].Moves.Count > 0);
            if (canUndo)
            {
                var top = m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1];
                string undoTip = string.Format("Undo ({0}/{1}): {2} ({3} moves)", m_PkgMgrUndoStack.Count, PkgMgrMaxUndoSteps, top.Label, top.Moves.Count);
                if (GUILayout.Button(new GUIContent("Undo", undoTip), m_StyleButtonSmall, GUILayout.Width(55), GUILayout.Height(25)))
                {
                    if (!IsPackageManagerBusy() && ConfirmPackageManagerAction("Undo", top.Moves.Count))
                    {
                        UndoLastPackageManagerOperation();
                    }
                }
            }
            GUILayout.EndHorizontal();

            Rect splitterRectForDrag = GUILayoutUtility.GetLastRect();

            GUILayout.BeginHorizontal(m_StyleSection, GUILayout.Width(leftPaneWidth), GUILayout.Height(PkgMgrMessageBarHeight));
            string footerMessage;
            if (IsPackageManagerBusy())
            {
                footerMessage = "<b>" + (string.IsNullOrEmpty(m_PkgMgrStatusMessage) ? "Working..." : m_PkgMgrStatusMessage) + "</b>";
            }
            else if (m_PkgMgrStatusTimer > Time.realtimeSinceStartup)
            {
                footerMessage = "<b>" + m_PkgMgrStatusMessage + "</b>";
            }
            else if (!string.IsNullOrEmpty(m_PkgMgrPreviewHint))
            {
                footerMessage = m_PkgMgrPreviewHint;
            }
            else
            {
                footerMessage = m_PkgMgrLastTooltip;
            }
            GUILayout.Label(footerMessage, m_StyleInfoCardText, GUILayout.Height(20), GUILayout.ExpandWidth(true));

            if (!string.IsNullOrEmpty(m_PkgMgrLastOperationDetails))
            {
                if (GUILayout.Button(new GUIContent("Copy details", "Copy details of conflicts/failed items from the last operation"), m_StyleButtonSmall, GUILayout.Width(90), GUILayout.Height(20)))
                {
                    GUIUtility.systemCopyBuffer = m_PkgMgrLastOperationDetails;
                    m_PkgMgrStatusMessage = "Copied details to clipboard.";
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                }
            }
            GUILayout.EndHorizontal();

            if (Event.current.type != EventType.Layout)
            {
                Rect splitterRect = splitterRectForDrag;
                int splitterControlID = GUIUtility.GetControlID(FocusType.Passive);
                switch (Event.current.GetTypeForControl(splitterControlID))
                {
                    case EventType.MouseDown:
                        if (splitterRect.Contains(Event.current.mousePosition) && Event.current.button == 0)
                        {
                            GUIUtility.hotControl = splitterControlID;
                            Event.current.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == splitterControlID)
                        {
                            GUIUtility.hotControl = 0;
                            Settings.Instance.PackageManagerSplitRatio.Value = m_PkgMgrSplitRatio;
                            Event.current.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == splitterControlID)
                        {
                            m_PkgMgrSplitRatio = Mathf.Clamp(m_PkgMgrSplitRatio + Event.current.delta.y / totalTableHeightAvailable, 0.1f, 0.9f);
                            Event.current.Use();
                        }
                        break;
                }
            }

            GUILayout.BeginVertical(GUILayout.Height(hBottom + 35));
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Unloaded ({0} | {1} vis | {2} sel | {3}){4}", m_PkgMgrAllCount, m_AllVisibleRows.Count, allSelectedCount, FormatSize(allSelectedSize), filterSummary), m_StyleSubHeader, GUILayout.Width(leftPaneWidth - 120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("All", "Select all currently visible unloaded packages"), m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AllList, true, m_AddonList);
            if (GUILayout.Button(new GUIContent("None", "Deselect all packages in this list"), m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AllList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AllList, ref m_AllScroll, ref m_AllLastSelectedIndex, leftPaneWidth, hBottom, ref m_AllFirstVisible, ref m_AllLastVisible);
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            if (m_PkgMgrShowPreview)
            {
                GUILayout.Space(8);
                DrawPackageManagerPreview(previewWidth, totalContentHeightAvailable);
            }
            else
            {
                m_PkgMgrPreviewHint = "";
            }

            if (Event.current.type == EventType.Repaint)
            {
                m_PkgMgrLastTooltip = GUI.tooltip;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUILayoutUtility.GetRect(windowWidth, windowHeight);

            var resizeRect = new Rect(windowWidth - 30, windowHeight - 30, 30, 30);
            GUI.Box(new Rect(windowWidth - 20, windowHeight - 20, 20, 20), "◢", m_StyleInfoIcon);
            int resizeControlID = GUIUtility.GetControlID(FocusType.Passive);
            switch (Event.current.GetTypeForControl(resizeControlID))
            {
                case EventType.MouseDown:
                    if (resizeRect.Contains(Event.current.mousePosition)) { GUIUtility.hotControl = resizeControlID; Event.current.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == resizeControlID) { 
                        GUIUtility.hotControl = 0; 
                        Settings.Instance.PackageManagerWindowRect.Value = m_PackageManagerWindowRect;
                        Event.current.Use(); 
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        m_PackageManagerWindowRect.width = Mathf.Max(m_PackageManagerWindowRect.width + Event.current.delta.x, 600);
                        m_PackageManagerWindowRect.height = Mathf.Max(m_PackageManagerWindowRect.height + Event.current.delta.y, 400);
                        Event.current.Use();
                    }
                    break;
            }

            if (Event.current.type != EventType.MouseDrag || !resizeRect.Contains(Event.current.mousePosition)) GUI.DragWindow();

            if (Event.current.type == EventType.MouseUp)
            {
                Settings.Instance.PackageManagerWindowRect.Value = m_PackageManagerWindowRect;
            }

            if (isEventInsidePackageManager && Event.current.type != EventType.Used)
            {
                switch (Event.current.type)
                {
                    case EventType.ScrollWheel:
                    case EventType.MouseDown:
                    case EventType.MouseUp:
                    case EventType.MouseDrag:
                    case EventType.KeyDown:
                    case EventType.KeyUp:
                        Event.current.Use();
                        break;
                }
            }
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
                    if (state) m_PkgMgrSelectedInLoaded = (list == m_AddonList);
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
                        m_ShowPackageManagerWindow = false;
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
            m_ShowDesktopContextMenu = false;
            if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide();
        }

        void ShowPackageManagerContextMenu(PackageManagerItem item)
        {
            m_PkgMgrSelectedCount = CountSelectedItems();
            m_PkgMgrTargetGroupCount = CountGroupItems(item.GroupId);
            m_PkgMgrIsVR = false;
            try { m_PkgMgrIsVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            if (!m_PkgMgrIsVR)
            {
                m_ContextMenuTargetItem = item;
                m_ShowDesktopContextMenu = true;
                
                Vector2 mousePos = new Vector2(Input.mousePosition.x / m_UIScale, (Screen.height - Input.mousePosition.y) / m_UIScale);
                
                float offsetX = -5f;
                float offsetY = -5f; 
                
                m_DesktopContextMenuRect = new Rect(mousePos.x + offsetX, mousePos.y + offsetY, 250, 0);
                
                float screenWidthScaled = Screen.width / m_UIScale;
                float screenHeightScaled = Screen.height / m_UIScale;
                if (m_DesktopContextMenuRect.xMax > screenWidthScaled) m_DesktopContextMenuRect.x = screenWidthScaled - m_DesktopContextMenuRect.width;
                
                return;
            }

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
                            m_ShowPackageManagerWindow = false;
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

            Vector3 position = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
            ContextMenuPanel.Instance.Show(position, options, title);
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

        void DrawDesktopContextMenu(int windowID)
        {
            if (m_ContextMenuTargetItem == null) 
            {
                m_ShowDesktopContextMenu = false;
                return;
            }

            GUILayout.BeginVertical(m_StylePanel);
            
            int groupCount = m_PkgMgrTargetGroupCount;
            int selectedCount = m_PkgMgrSelectedCount;
            string title = m_ContextMenuTargetItem.Uid;

            if (m_ContextMenuTargetItem.Checked && selectedCount > 1)
            {
                title = string.Format("{0} items selected", selectedCount);
            }
            else if (groupCount > 1)
            {
                title = string.Format("Group: {0} ({1} items)", FileManager.PackageIDToPackageGroupID(m_ContextMenuTargetItem.Uid), groupCount);
            }

            GUILayout.Label("<b>" + title + "</b>", m_StyleInfoCardText);
            GUILayout.Space(5);

            if (!(m_ContextMenuTargetItem.Checked && selectedCount > 1) && groupCount <= 1)
            {
                if (GUILayout.Button("Show in Explorer", m_StyleButton))
                {
                    ShowInExplorer(m_ContextMenuTargetItem.Path);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Full Path", m_StyleButton))
                {
                    GUIUtility.systemCopyBuffer = Path.GetFullPath(m_ContextMenuTargetItem.Path);
                    m_PkgMgrStatusMessage = "Copied full path to clipboard.";
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Filter by Creator (" + m_ContextMenuTargetItem.Creator + ")", m_StyleButton))
                {
                    m_PkgMgrCreatorFilter = m_ContextMenuTargetItem.Creator;
                    m_PkgMgrIndicesDirty = true;
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Select All by Creator (" + m_ContextMenuTargetItem.Creator + ")", m_StyleButton))
                {
                    SelectAllByCreator(m_ContextMenuTargetItem.Creator);
                    m_ShowDesktopContextMenu = false;
                }

                if (m_ContextMenuTargetItem.MissingDependencies != null && m_ContextMenuTargetItem.MissingDependencies.Count > 0)
                {
                    if (GUILayout.Button("Select Unloaded Dependencies (" + m_ContextMenuTargetItem.MissingDependencies.Count + ")", m_StyleButton))
                    {
                        ResolveDependencies(m_ContextMenuTargetItem);
                        m_ShowDesktopContextMenu = false;
                    }

                    if (GUILayout.Button("Load Unloaded Dependencies", m_StyleButton))
                    {
                        ResolveDependencies(m_ContextMenuTargetItem);
                        PerformMove(m_AllList, false);
                        m_ShowDesktopContextMenu = false;
                    }
                }
                GUILayout.Space(5);
            }

            if (m_ContextMenuTargetItem.Checked && selectedCount > 1)
            {
                if (GUILayout.Button("Copy Selected Names", m_StyleButton))
                {
                    CopySelectedNames();
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Selected Dependencies (Deep)", m_StyleButton))
                {
                    CopySelectedDependenciesDeep();
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Unselect All", m_StyleButton))
                {
                    SelectAllPackageManager(m_AddonList, false);
                    SelectAllPackageManager(m_AllList, false);
                    m_ShowDesktopContextMenu = false;
                }

                GUILayout.Space(5);
                if (GUILayout.Button("<b>Keep Selected -> Unload Rest</b>", m_StyleButton))
                {
                    PerformKeepSelectedUnloadRest();
                    m_ShowDesktopContextMenu = false;
                }
            }
            else if (groupCount > 1)
            {
                if (GUILayout.Button("Select Group", m_StyleButton))
                {
                    SetGroupChecked(m_ContextMenuTargetItem.Uid, true);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Unselect Group", m_StyleButton))
                {
                    SetGroupChecked(m_ContextMenuTargetItem.Uid, false);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Group Names", m_StyleButton))
                {
                    CopyGroupNames(m_ContextMenuTargetItem.Uid);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Group Dependencies (Deep)", m_StyleButton))
                {
                    CopyGroupDependenciesDeep(m_ContextMenuTargetItem.Uid);
                    m_ShowDesktopContextMenu = false;
                }
            }
            else
            {
                if (GUILayout.Button("Copy Package Name", m_StyleButton))
                {
                    GUIUtility.systemCopyBuffer = m_ContextMenuTargetItem.Uid;
                    m_PkgMgrStatusMessage = "Copied name to clipboard: " + m_ContextMenuTargetItem.Uid;
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Dependencies (Deep)", m_StyleButton))
                {
                    VarPackage pkg = FileManager.GetPackage(m_ContextMenuTargetItem.Uid, false);
                    if (pkg != null)
                    {
                        var deps = pkg.GetDependenciesDeep(2);
                        if (deps != null && deps.Count > 0)
                        {
                            GUIUtility.systemCopyBuffer = string.Join("\n", deps.ToArray());
                            m_PkgMgrStatusMessage = string.Format("Copied {0} dependencies to clipboard.", deps.Count);
                            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                            LogUtil.Log("Copied " + deps.Count + " dependencies to clipboard.");
                        }
                        else
                        {
                            m_PkgMgrStatusMessage = "No dependencies found for " + m_ContextMenuTargetItem.Uid;
                            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                            LogUtil.Log("No dependencies found for " + m_ContextMenuTargetItem.Uid);
                        }
                    }
                    m_ShowDesktopContextMenu = false;
                }

                if (m_ContextMenuTargetItem.Type == "Scene")
                {
                    if (GUILayout.Button("<b>Launch Scene</b>", m_StyleButton))
                    {
                        string scenePath = GetFirstScenePath(m_ContextMenuTargetItem.Uid);
                        if (!string.IsNullOrEmpty(scenePath))
                        {
                            LoadFromSceneWorldDialog(scenePath);
                            m_ShowPackageManagerWindow = false;
                        }
                        m_ShowDesktopContextMenu = false;
                    }
                }

                if (selectedCount > 0)
                {
                    GUILayout.Space(5);
                    if (GUILayout.Button("<b>Keep Selected -> Unload Rest</b>", m_StyleButton))
                    {
                        PerformKeepSelectedUnloadRest();
                        m_ShowDesktopContextMenu = false;
                    }
                }
            }

            GUILayout.Space(5);
            if (GUILayout.Button("Cancel", m_StyleButtonDanger))
            {
                m_ShowDesktopContextMenu = false;
            }

            GUILayout.EndVertical();

            if (Event.current.type == EventType.MouseDown && !new Rect(0, 0, m_DesktopContextMenuRect.width, m_DesktopContextMenuRect.height).Contains(Event.current.mousePosition))
            {
                m_ShowDesktopContextMenu = false;
                Event.current.Use();
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

        void DrawLabelWithEllipsis(Rect rect, string text, GUIStyle style, string tooltip, ref float lastWidth, ref GUIContent cachedContent)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (cachedContent == null || Math.Abs(lastWidth - rect.width) > 0.1f)
            {
                lastWidth = rect.width;
                GUIContent content = new GUIContent(text, tooltip);
                
                if (style.CalcSize(content).x <= rect.width)
                {
                    cachedContent = content;
                }
                else
                {
                    string truncated = TruncateRichText(text, rect.width, style);
                    cachedContent = new GUIContent(truncated, tooltip);
                }
            }
            
            GUI.Label(rect, cachedContent, style);
        }

        string TruncateRichText(string text, float maxWidth, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return "";
            
            GUIContent ellipsis = new GUIContent("...");
            float ellipsisWidth = style.CalcSize(ellipsis).x;
            float availableWidth = maxWidth - ellipsisWidth;
            if (availableWidth <= 0) return "...";

            int low = 0;
            int high = text.Length;
            int best = 0;
            
            while (low <= high)
            {
                int mid = (low + high) / 2;
                string sub = CloseRichTextTags(text.Substring(0, mid));
                
                if (style.CalcSize(new GUIContent(sub)).x <= availableWidth)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return CloseRichTextTags(text.Substring(0, best)) + "...";
        }

        string CloseRichTextTags(string text)
        {
            int lastLT = text.LastIndexOf('<');
            int lastGT = text.LastIndexOf('>');
            if (lastLT > lastGT)
            {
                text = text.Substring(0, lastLT);
            }

            int colorOpen = 0;
            int pos = 0;
            while (true)
            {
                int nextOpen = text.IndexOf("<color", pos, StringComparison.OrdinalIgnoreCase);
                int nextClose = text.IndexOf("</color>", pos, StringComparison.OrdinalIgnoreCase);

                if (nextOpen != -1 && (nextClose == -1 || nextOpen < nextClose))
                {
                    colorOpen++;
                    pos = nextOpen + 6;
                }
                else if (nextClose != -1)
                {
                    colorOpen--;
                    pos = nextClose + 8;
                }
                else break;
            }

            for (int i = 0; i < colorOpen; i++) text += "</color>";
            return text;
        }

        void DrawPackageManagerPane(System.Collections.Generic.List<PackageManagerItem> list, ref Vector2 scroll, ref int lastIdx, float paneWidth, float paneHeight, ref int firstVisible, ref int lastVisible)
        {
            GUILayout.BeginVertical(m_StyleSection, GUILayout.Height(paneHeight));
            
            float col1Width = 110;
            float col3Width = 60;
            float col4Width = 65;
            float col2Width = paneWidth - col1Width - col3Width - col4Width - 50;
            
            GUILayout.BeginHorizontal();
            DrawPackageManagerHeader("Category", "Category", "Sort by Category/Type", col1Width);
            DrawPackageManagerHeader("Name", "Name", "Sort by Package Name", col2Width);
            DrawPackageManagerHeader("Dep", "Deps", "Sort by Dependency Status", col3Width);
            DrawPackageManagerHeader("Size", "Size", "Sort by File Size", col4Width);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.box, GUILayout.Height(Mathf.Max(10f, paneHeight - 35)));

            if (Event.current.type == EventType.ScrollWheel)
            {
                if (new Rect(0, 0, paneWidth, paneHeight).Contains(Event.current.mousePosition))
                {
                    scroll.y += Event.current.delta.y * 20;
                    scroll.y = Mathf.Max(0, scroll.y); 
                    Event.current.Use();
                }
            }
            
            System.Collections.Generic.List<PackageManagerVisibleRow> visibleRows = (list == m_AddonList) ? m_AddonVisibleRows : m_AllVisibleRows;
            float rowHeight = 22;
            bool isLoadedList = list == m_AddonList;
            bool isActiveList = isLoadedList == m_PkgMgrSelectedInLoaded;

            int direction = GetPackageManagerKeyboardDirection();
            if (direction != 0 && isActiveList)
            {
                HandlePackageManagerKeyboardNavigation(list, visibleRows, direction, ref scroll, ref lastIdx, rowHeight, paneHeight);
            }

            if (Event.current.type == EventType.MouseUp) m_PkgMgrIsDragging = false;

            if (Event.current.type == EventType.Layout)
            {
                firstVisible = Mathf.Max(0, (int)(scroll.y / rowHeight));
                lastVisible = Mathf.Min(visibleRows.Count - 1, (int)((scroll.y + paneHeight) / rowHeight));
            }

            GUILayout.Space(firstVisible * rowHeight);

            for (int j = firstVisible; j <= lastVisible; j++)
            {
                if (j >= visibleRows.Count) break;
                Rect rowRect = new Rect(0, j * rowHeight, paneWidth - 25, rowHeight);
                GUILayoutUtility.GetRect(paneWidth - 25, rowHeight);
                ProcessPackageManagerRow(list, visibleRows, j, rowRect, col1Width, col2Width, col3Width, col4Width, ref lastIdx);
            }

            GUILayout.Space(Mathf.Max(0, (visibleRows.Count - 1 - lastVisible) * rowHeight));

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        int GetPackageManagerKeyboardDirection()
        {
            if (Event.current.type != EventType.KeyDown) return 0;
            if (Event.current.keyCode == KeyCode.UpArrow) return -1;
            if (Event.current.keyCode == KeyCode.DownArrow) return 1;
            return 0;
        }

        void HandlePackageManagerKeyboardNavigation(System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.List<PackageManagerVisibleRow> visibleRows, int direction, ref Vector2 scroll, ref int lastIdx, float rowHeight, float paneHeight)
        {
            int currentRowIdx = -1;
            if (lastIdx != -1)
            {
                for (int k = 0; k < visibleRows.Count; k++) { if (visibleRows[k].Index == lastIdx) { currentRowIdx = k; break; } }
            }

            int nextRowIdx = currentRowIdx + direction;
            while (nextRowIdx >= 0 && nextRowIdx < visibleRows.Count)
            {
                if (visibleRows[nextRowIdx].Index != -1)
                {
                    int newIdx = visibleRows[nextRowIdx].Index;
                    var newItem = list[newIdx];
                    bool shiftSelect = Event.current.shift && lastIdx != -1;
                    if (shiftSelect)
                    {
                        int anchorIdx = GetPkgMgrShiftAnchorIndex(list);
                        if (anchorIdx == -1) anchorIdx = lastIdx;
                        if (anchorIdx != -1)
                        {
                            if (GetPkgMgrShiftAnchorIndex(list) == -1) SetPkgMgrShiftAnchorIndex(list, anchorIdx);
                            int anchorVisible = -1;
                            for (int k = 0; k < visibleRows.Count; k++) { if (visibleRows[k].Index == anchorIdx) { anchorVisible = k; break; } }
                            if (anchorVisible != -1)
                            {
                                bool newState = list[anchorIdx].Checked;
                                int minRow = Math.Min(anchorVisible, nextRowIdx);
                                int maxRow = Math.Max(anchorVisible, nextRowIdx);
                                foreach (var it in list) it.Checked = false;
                                for (int k = minRow; k <= maxRow; k++)
                                {
                                    if (visibleRows[k].Index != -1) list[visibleRows[k].Index].Checked = newState;
                                }
                            }
                            else
                            {
                                foreach (var it in list) it.Checked = false;
                                newItem.Checked = true;
                                SetPkgMgrShiftAnchorIndex(list, newIdx);
                            }
                        }
                        else
                        {
                            foreach (var it in list) it.Checked = false;
                            newItem.Checked = true;
                            SetPkgMgrShiftAnchorIndex(list, newIdx);
                        }
                    }
                    else
                    {
                        foreach (var it in list) it.Checked = false;
                        newItem.Checked = true;
                        SetPkgMgrShiftAnchorIndex(list, newIdx);
                    }

                    lastIdx = newIdx;
                    OnPackageManagerItemSelected(newItem, list == m_AddonList);
                    
                    float targetY = nextRowIdx * rowHeight;
                    if (targetY < scroll.y) scroll.y = targetY;
                    else if (targetY + rowHeight > scroll.y + paneHeight - 35) scroll.y = targetY - (paneHeight - 35) + rowHeight;
                    
                    Event.current.Use();
                    break;
                }
                nextRowIdx += direction;
            }
        }

        void ProcessPackageManagerRow(System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.List<PackageManagerVisibleRow> visibleRows, int visibleRowIndex, Rect rowRect, float col1Width, float col2Width, float col3Width, float col4Width, ref int lastIdx)
        {
            var row = visibleRows[visibleRowIndex];
            if (row.Index == -1)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    var headerCol = new Color(1f, 1f, 1f, 0.1f);
                    var prevCol = GUI.color;
                    GUI.color = headerCol;
                    GUI.DrawTexture(rowRect, Texture2D.whiteTexture);
                    GUI.color = prevCol;
                    GUI.Label(new Rect(rowRect.x + 5, rowRect.y, rowRect.width, rowRect.height), row.Header, m_StyleSubHeader);
                }
                return;
            }

            int i = row.Index;
            var item = list[i];
            HandlePackageManagerRowSelection(list, visibleRows, visibleRowIndex, rowRect, item, i, ref lastIdx);
            if (Event.current.type == EventType.Repaint)
            {
                DrawPackageManagerRowBackground(item, rowRect, visibleRowIndex);
            }
            DrawPackageManagerRow(item, rowRect, col1Width, col2Width, col3Width, col4Width);
        }

        void HandlePackageManagerRowSelection(System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.List<PackageManagerVisibleRow> visibleRows, int visibleRowIndex, Rect rowRect, PackageManagerItem item, int itemIndex, ref int lastIdx)
        {
            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                {
                    if (!item.Checked)
                    {
                        foreach (var it in list) it.Checked = false;
                        item.Checked = true;
                        lastIdx = itemIndex;
                    }
                    OnPackageManagerItemSelected(item, list == m_AddonList);
                    ShowPackageManagerContextMenu(item);
                }
                else if (Event.current.shift && lastIdx != -1)
                {
                    int startRowIdx = -1;
                    for (int k = 0; k < visibleRows.Count; k++) { if (visibleRows[k].Index == lastIdx) { startRowIdx = k; break; } }
                    if (startRowIdx != -1)
                    {
                        int min = Math.Min(startRowIdx, visibleRowIndex);
                        int max = Math.Max(startRowIdx, visibleRowIndex);
                        bool newState = list[lastIdx].Checked;
                        for (int k = min; k <= max; k++)
                        {
                            if (visibleRows[k].Index != -1) list[visibleRows[k].Index].Checked = newState;
                        }
                        HidePackageManagerContextMenu();
                    }
                    else
                    {
                        item.Checked = !item.Checked;
                        HidePackageManagerContextMenu();
                    }
                    lastIdx = itemIndex;
                }
                else if (Event.current.control)
                {
                    item.Checked = !item.Checked;
                    lastIdx = itemIndex;
                    HidePackageManagerContextMenu();
                }
                else
                {
                    bool wasChecked = item.Checked;
                    foreach (var it in list) it.Checked = false;
                    item.Checked = !wasChecked;
                    lastIdx = itemIndex;
                    OnPackageManagerItemSelected(item, list == m_AddonList);
                    m_PkgMgrIsDragging = true;
                    m_PkgMgrDragChecked = item.Checked;
                    m_PkgMgrDragLastIdx = visibleRowIndex;
                    HidePackageManagerContextMenu();
                }
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDrag && m_PkgMgrIsDragging && rowRect.Contains(Event.current.mousePosition))
            {
                if (m_PkgMgrDragLastIdx != visibleRowIndex)
                {
                    int min = Math.Min(m_PkgMgrDragLastIdx, visibleRowIndex);
                    int max = Math.Max(m_PkgMgrDragLastIdx, visibleRowIndex);
                    for (int k = min; k <= max; k++)
                    {
                        int idx = visibleRows[k].Index;
                        if (idx != -1) list[idx].Checked = m_PkgMgrDragChecked;
                    }
                    lastIdx = itemIndex;
                    m_PkgMgrDragLastIdx = visibleRowIndex;
                    HidePackageManagerContextMenu();
                }
                Event.current.Use();
            }
        }

        void DrawPackageManagerRowBackground(PackageManagerItem item, Rect rowRect, int visibleRowIndex)
        {
            if (item.Locked)
            {
                GUI.Box(rowRect, "", m_StyleButton);
                var prevCol = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                GUI.DrawTexture(rowRect, Texture2D.whiteTexture);
                GUI.color = prevCol;
            }
            else if (item.Checked)
            {
                m_StyleRowHover.Draw(rowRect, false, false, false, false);
            }
            else
            {
                GUIStyle style = (visibleRowIndex % 2 == 0) ? m_StyleRowAlternate : m_StyleRow;
                style.Draw(rowRect, false, false, false, false);
            }
        }

        void DrawPackageManagerRow(PackageManagerItem item, Rect rowRect, float col1Width, float col2Width, float col3Width, float col4Width)
        {
            var prevContentColor = GUI.contentColor;
            if (item.Locked) GUI.contentColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

            float x = rowRect.x + 2;
            DrawLabelWithEllipsis(new Rect(x, rowRect.y, col1Width, rowRect.height), string.IsNullOrEmpty(item.HighlightedType) ? item.Type : item.HighlightedType, m_StylePkgMgrRow, item.Path, ref item.LastTypeWidth, ref item.TruncatedTypeContent);
            x += col1Width + 4;
            DrawLabelWithEllipsis(new Rect(x, rowRect.y, col2Width, rowRect.height), item.NameContent.text, m_StylePkgMgrRow, item.NameContent.tooltip, ref item.LastWidth, ref item.TruncatedNameContent);

            x += col2Width + 4;
            GUI.Label(new Rect(x, rowRect.y, col3Width, rowRect.height), item.DepContent, m_StylePkgMgrRowCentered);
            x += col3Width + 4;
            GUI.Label(new Rect(x, rowRect.y, col4Width, rowRect.height), item.SizeContent, m_StylePkgMgrRowCentered);

            GUI.contentColor = prevContentColor;
        }

        void DrawPackageManagerPreview(float width, float height)
        {
            GUILayout.BeginVertical(m_StyleSection, GUILayout.Width(width), GUILayout.Height(height));
            
            if (m_PkgMgrSelectedItem == null)
            {
                GUILayout.Label("Select a package to see details", m_StyleInfoCardText);
                GUILayout.EndVertical();
                return;
            }

            float imgSize = width - 10;
            Rect imgRect = GUILayoutUtility.GetRect(imgSize, imgSize);

            int selectionCount = GetPackageManagerSelectionCount();
            bool multipleSelection = selectionCount > 1;
            bool isHoveringImage = imgRect.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                string previewHint = multipleSelection ? "Multiple packages selected; preview disabled." : "";
                if (isHoveringImage)
                {
                    previewHint = "Scroll over the preview image to cycle package selection.";
                }
                m_PkgMgrPreviewHint = previewHint;
            }

            float rowCycleDelta = 0;
            bool rowCycleConsumed = false;
            if (Event.current.type == EventType.ScrollWheel && isHoveringImage)
            {
                rowCycleDelta = Event.current.delta.y;
                rowCycleConsumed = true;
            }

            if (rowCycleDelta != 0)
            {
                int direction = (rowCycleDelta > 0) ? 1 : -1;
                System.Collections.Generic.List<PackageManagerItem> list = m_PkgMgrSelectedInLoaded ? m_AddonList : m_AllList;
                System.Collections.Generic.List<PackageManagerVisibleRow> visibleRows = m_PkgMgrSelectedInLoaded ? m_AddonVisibleRows : m_AllVisibleRows;
                Vector2 scroll = m_PkgMgrSelectedInLoaded ? m_AddonScroll : m_AllScroll;
                int lastIdx = m_PkgMgrSelectedInLoaded ? m_AddonLastSelectedIndex : m_AllLastSelectedIndex;
                float paneHeight = m_PkgMgrSelectedInLoaded ? m_PkgMgrLoadedPaneHeight : m_PkgMgrAllPaneHeight;
                if (paneHeight <= 0f)
                {
                    paneHeight = m_PkgMgrSelectedInLoaded ? m_PkgMgrTopHeight : Mathf.Max(100f, m_PackageManagerWindowRect.height - m_PkgMgrTopHeight - 150f);
                }
                float rowHeight = 22;

                int currentRowIdx = -1;
                if (lastIdx != -1)
                {
                    for (int k = 0; k < visibleRows.Count; k++) { if (visibleRows[k].Index == lastIdx) { currentRowIdx = k; break; } }
                }

                int nextRowIdx = currentRowIdx + direction;
                while (nextRowIdx >= 0 && nextRowIdx < visibleRows.Count)
                {
                    if (visibleRows[nextRowIdx].Index != -1)
                    {
                        int newIdx = visibleRows[nextRowIdx].Index;
                        var newItem = list[newIdx];
                        foreach (var it in list) it.Checked = false;
                        newItem.Checked = true;
                        lastIdx = newIdx;
                        OnPackageManagerItemSelected(newItem, m_PkgMgrSelectedInLoaded);
                        
                        float targetY = nextRowIdx * rowHeight;
                        if (targetY < scroll.y) scroll.y = targetY;
                        else if (targetY + rowHeight > scroll.y + paneHeight - 35) scroll.y = targetY - (paneHeight - 35) + rowHeight;
                        
                        if (rowCycleConsumed) Event.current.Use();
                        break;
                    }
                    nextRowIdx += direction;
                }

                if (m_PkgMgrSelectedInLoaded)
                {
                    m_AddonScroll = scroll;
                    m_AddonLastSelectedIndex = lastIdx;
                }
                else
                {
                    m_AllScroll = scroll;
                    m_AllLastSelectedIndex = lastIdx;
                }
            }

            if (!multipleSelection && m_PkgMgrSelectedThumbnail != null)
            {
                GUI.DrawTexture(imgRect, m_PkgMgrSelectedThumbnail, ScaleMode.ScaleToFit);
            }
            else
            {
                string boxMessage = multipleSelection ? "Multiple packages selected\nPreview disabled while cycling." : "No Preview";
                GUI.Box(imgRect, boxMessage, m_StyleSection);
            }

            GUILayout.Space(10);

            m_PkgMgrSelectedTab = GUILayout.SelectionGrid(m_PkgMgrSelectedTab, m_PkgMgrTabs, 3, m_StyleButtonSmall);

            GUILayout.Space(5);
            
            float previewScrollHeight = height - imgSize - 75;

            m_PkgMgrInfoScroll = GUILayout.BeginScrollView(m_PkgMgrInfoScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.box, GUILayout.Width(width - 20), GUILayout.Height(previewScrollHeight));
            if (m_PkgMgrSelectedTab == 0)
            {
                var it = m_PkgMgrSelectedItem;
                GUILayout.Label("<b>Package:</b>", m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Name: {0}", it.Uid), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Creator: {0}", it.Creator), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Type: {0}", it.Type), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Size: {0}", FormatSize(it.Size)), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Age: {0}", it.AgeString), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Path: {0}", it.Path), m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.Format("Flags: {0}{1}{2}{3}{4}", it.IsActive ? "Active" : "", it.Locked ? (it.IsActive ? ", Locked" : "Locked") : "", it.AutoLoad ? ((it.IsActive || it.Locked) ? ", Auto-Load" : "Auto-Load") : "", it.IsLatest ? ((it.IsActive || it.Locked || it.AutoLoad) ? ", Latest" : "Latest") : ((it.IsActive || it.Locked || it.AutoLoad) ? ", Old Version" : "Old Version"), (!it.IsActive && !it.Locked && !it.AutoLoad && it.IsLatest) ? "" : ""), m_StyleInfoCardTextWrapped);

                GUILayout.Space(8);
                GUILayout.Label("<b>Description:</b>", m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.IsNullOrEmpty(m_PkgMgrSelectedDescription) ? "No description available." : m_PkgMgrSelectedDescription, m_StyleInfoCardTextWrapped);
            }
            else if (m_PkgMgrSelectedTab == 1)
            {
                GUILayout.Label("<b>Dependencies:</b>", m_StyleInfoCardTextWrapped);
                if (m_PkgMgrSelectedItem.UnloadedDependencies != null && m_PkgMgrSelectedItem.UnloadedDependencies.Count > 0)
                {
                    GUILayout.Label("<color=orange>Unloaded:</color>", m_StyleInfoCardTextWrapped);
                    foreach (var dep in m_PkgMgrSelectedItem.UnloadedDependencies)
                    {
                        GUILayout.Label("- " + dep, m_StyleInfoCardTextWrapped);
                    }
                }

                if (m_PkgMgrSelectedItem.NotFoundDependencies != null && m_PkgMgrSelectedItem.NotFoundDependencies.Count > 0)
                {
                    GUILayout.Label("<color=red>Not found:</color>", m_StyleInfoCardTextWrapped);
                    foreach (var dep in m_PkgMgrSelectedItem.NotFoundDependencies)
                    {
                        GUILayout.Label("- " + dep, m_StyleInfoCardTextWrapped);
                    }
                }

                if ((m_PkgMgrSelectedItem.UnloadedDependencies == null || m_PkgMgrSelectedItem.UnloadedDependencies.Count == 0) && (m_PkgMgrSelectedItem.NotFoundDependencies == null || m_PkgMgrSelectedItem.NotFoundDependencies.Count == 0))
                {
                    if (m_PkgMgrSelectedItem.MissingDependencies != null && m_PkgMgrSelectedItem.MissingDependencies.Count > 0)
                    {
                        GUILayout.Label("<color=red>Missing:</color>", m_StyleInfoCardTextWrapped);
                        foreach (var dep in m_PkgMgrSelectedItem.MissingDependencies)
                        {
                            GUILayout.Label("- " + dep, m_StyleInfoCardTextWrapped);
                        }
                    }
                }
                
                VarPackage pkg = FileManager.GetPackage(m_PkgMgrSelectedItem.Uid, false);
                if (pkg != null)
                {
                    var deps = pkg.RecursivePackageDependencies;
                    if (deps != null && deps.Count > 0)
                    {
                        GUILayout.Label("<color=green>All Dependencies:</color>", m_StyleInfoCardTextWrapped);
                        foreach (var dep in deps)
                        {
                            GUILayout.Label("- " + dep, m_StyleInfoCardTextWrapped);
                        }
                    }
                    else
                    {
                        GUILayout.Label("No dependencies.", m_StyleInfoCardTextWrapped);
                    }
                }
            }
            else if (m_PkgMgrSelectedTab == 2)
            {
                if (selectionCount != 1)
                {
                    GUILayout.Label("Actions are available for single package selection.", m_StyleInfoCardTextWrapped);
                }
                else
                {
                    System.Collections.Generic.List<PackageManagerAction> actions = BuildPackageManagerSingleItemActions(m_PkgMgrSelectedItem);
                    int actionCount = Mathf.Min(actions.Count, 10);
                    if (Event.current.type == EventType.KeyDown && Event.current.alt)
                    {
                        int hotkeyIndex = GetPackageManagerActionHotkeyIndex(Event.current.keyCode);
                        if (hotkeyIndex >= 0 && hotkeyIndex < actionCount)
                        {
                            actions[hotkeyIndex].Execute();
                            Event.current.Use();
                        }
                    }

                    if (actionCount == 0)
                    {
                        GUILayout.Label("No actions available.", m_StyleInfoCardTextWrapped);
                    }
                    else
                    {
                        for (int i = 0; i < actionCount; i++)
                        {
                            string numberLabel = (i == 9) ? "0" : (i + 1).ToString();
                            string label = string.Format("{0}. {1}", numberLabel, actions[i].Label);
                            if (GUILayout.Button(label, m_StyleButton)) actions[i].Execute();
                        }
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        void PerformMove(System.Collections.Generic.List<PackageManagerItem> sourceList, bool isMovingToAll)
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
                return;
            }

            if (!ConfirmPackageManagerAction(actionLabel, toMoveUids.Count)) return;

            int movedCount = 0;
            int conflictCount = 0;
            int failedCount = 0;

            m_PkgMgrLastOperationDetails = "";
            var detailLines = new System.Collections.Generic.List<string>();
            var undoMoves = new System.Collections.Generic.List<PackageManagerMoveRecord>();

            string fromPrefix = isMovingToAll ? "AddonPackages" : "AllPackages";
            string toPrefix = isMovingToAll ? "AllPackages" : "AddonPackages";
            var fromList = isMovingToAll ? m_AddonList : m_AllList;

            foreach (var item in fromList)
            {
                if (toMoveUids.Contains(item.Uid))
                {
                    if (item.Locked) continue;
                    if (!item.Path.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    string targetPath = toPrefix + item.Path.Substring(fromPrefix.Length);
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(targetPath)) { conflictCount++; if (detailLines.Count < 50) detailLines.Add("CONFLICT: " + item.Uid + " -> " + targetPath); continue; }

                    try { File.Move(item.Path, targetPath); movedCount++; undoMoves.Add(new PackageManagerMoveRecord(item.Uid, item.Path, targetPath)); }
                    catch (Exception ex) { failedCount++; if (detailLines.Count < 50) detailLines.Add("FAILED: " + item.Uid + " -> " + targetPath + " | " + ex.Message); LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
                }
            }

            if (detailLines.Count > 0)
            {
                m_PkgMgrLastOperationDetails = string.Join("\n", detailLines.ToArray());
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
            }

            if (movedCount > 0)
            {
                Refresh();
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                ScanPackageManagerPackages();
            }
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

            foreach (var item in m_AddonList)
            {
                if (item.Locked) continue;
                if (keepUids.Contains(item.Uid)) continue;

                string targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) { conflictCount++; processed++; continue; }

                try { File.Move(item.Path, targetPath); movedToAll++; undoMoves.Add(new PackageManagerMoveRecord(item.Uid, item.Path, targetPath)); }
                catch (Exception ex) { failedCount++; LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }

                processed++;
                if ((processed % 25) == 0)
                {
                    m_PkgMgrStatusMessage = string.Format("Isolating... {0}/{1} changes processed", processed, candidateMoves);
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 1f;
                    yield return null;
                }
            }

            foreach (var item in m_AllList)
            {
                if (item.Locked) continue;
                if (!keepUids.Contains(item.Uid)) continue;

                string targetPath = "AddonPackages" + item.Path.Substring("AllPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) { conflictCount++; processed++; continue; }

                try { File.Move(item.Path, targetPath); movedToAddon++; undoMoves.Add(new PackageManagerMoveRecord(item.Uid, item.Path, targetPath)); }
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

                Refresh();
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                ScanPackageManagerPackages();
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

            m_PkgMgrLastOperationDetails = "";
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

            if (detailLines.Count > 0)
            {
                m_PkgMgrLastOperationDetails = string.Join("\n", detailLines.ToArray());
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
                Refresh();
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                ScanPackageManagerPackages();
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
            m_PkgMgrAddonCount = m_AddonList.Count;
            m_PkgMgrAllCount = m_AllList.Count;
            m_AddonLastSelectedIndex = -1;
            m_AllLastSelectedIndex = -1;
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

            m_PkgMgrSelectedInLoaded = isLoaded;
            if (m_PkgMgrSelectedItem == item) return;
            m_PkgMgrSelectedItem = item;
            m_PkgMgrSelectedDescription = "";
            m_PkgMgrSelectedThumbnail = null;
            m_PkgMgrLastThumbnailPath = "";
            m_PkgMgrInfoScroll = Vector2.zero;

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
                                    long writeTime = item.LastWriteTime.ToFileTime();
                                    FileEntry fe = FileManager.GetFileEntry(res.imgPath);
                                    if (fe != null) writeTime = fe.LastWriteTime.ToFileTime();
                                    StartCoroutine(GalleryThumbnailCache.Instance.GenerateAndSaveThumbnailRoutine(res.imgPath, res.tex, writeTime));
                                }
                            }
                        };
                        CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
                    }
                }
            }
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
                string type = DeterminePackageType(name);
                bool isLocked = m_LockedPackages.Contains(name);
                bool isAutoLoad = m_AutoLoadPackages.Contains(name);
                bool isLatest = latestUids.Contains(name);
                DateTime lwt = file.CreationTime;

                var deepDeps = FileManager.GetDependenciesDeep(name, 2);
                int depCount = deepDeps.Count;
                int loadedDepCount = 0;
                List<string> unloadedDeps = new List<string>();
                List<string> notFoundDeps = new List<string>();
                foreach (var dep in deepDeps)
                {
                    var resolved = FileManager.ResolveDependency(dep);
                    if (resolved == null)
                    {
                        notFoundDeps.Add(dep);
                    }
                    else if (resolved.Path.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                    {
                        unloadedDeps.Add(dep);
                    }
                    else
                    {
                        loadedDepCount++;
                    }
                }

                List<string> missingDeps = new List<string>(unloadedDeps.Count + notFoundDeps.Count);
                missingDeps.AddRange(unloadedDeps);
                missingDeps.AddRange(notFoundDeps);

                string description = "";
                VarPackage pkg = FileManager.GetPackage(name, false);
                if (pkg != null && !string.IsNullOrEmpty(pkg.Description)) description = pkg.Description;

                var item = new PackageManagerItem {
                    Uid = name,
                    Creator = PackageIDToCreator(name),
                    Path = relativePath,
                    Type = type,
                    Size = file.Length,
                    LastWriteTime = lwt,
                    AgeString = FormatAge(lwt, now),
                    Description = description,
                    AllDependencies = deepDeps,
                    DependencyCount = depCount,
                    LoadedDependencyCount = loadedDepCount,
                    MissingDependencies = missingDeps,
                    UnloadedDependencies = unloadedDeps,
                    NotFoundDependencies = notFoundDeps,
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
                UpdatePkgMgrItemCache(item);
                list.Add(item);

                if (!string.IsNullOrEmpty(type)) 
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

        void SortPackageManagerList()
        {
            System.Comparison<PackageManagerItem> comp = (a, b) => {
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
            };
            m_AddonList.Sort(comp);
            m_AllList.Sort(comp);
        }

        void OpenPackageManagerWindow()
        {
            ScanPackageManagerPackages();
            m_ShowPackageManagerWindow = true;
        }
    }
}
