using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

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
            public int DependencyCount;
            public int LoadedDependencyCount;
            public List<string> MissingDependencies;
            public string HighlightedUid;
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
        private string m_PkgMgrCreatorFilter = "";
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryInclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryExclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.List<string> m_PkgMgrCategories = new System.Collections.Generic.List<string>();
        private System.Collections.Generic.Dictionary<string, int> m_PkgMgrCategoryCounts = new System.Collections.Generic.Dictionary<string, int>();
        private System.Collections.Generic.HashSet<string> m_LockedPackages = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> m_AutoLoadPackages = new System.Collections.Generic.HashSet<string>();
        private Coroutine m_ScanPkgMgrCo;
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
        private string[] m_PkgMgrTabs = { "Info", "Deps" };
        private string m_PkgMgrLastThumbnailPath = "";
        private Vector2 m_PkgMgrInfoScroll = Vector2.zero;
        private bool m_PkgMgrSelectedInLoaded = true;
        private int m_AddonFirstVisible, m_AddonLastVisible;
        private int m_AllFirstVisible, m_AllLastVisible;
        private float m_PkgMgrTopHeight = 150f;
        private float m_PkgMgrFooterHeight = 40f;
        private float m_PkgMgrSplitRatio = 0.66f;
        private bool m_PkgMgrShowPreview = true;
        private float m_PkgMgrLoadedPaneHeight = 0f;
        private float m_PkgMgrAllPaneHeight = 0f;
        private string m_PkgMgrPreviewHint = "";
        
        private void RefreshVisibleIndices()
        {
            RefreshVisibleRows(m_AddonList, m_AddonVisibleRows);
            RefreshVisibleRows(m_AllList, m_AllVisibleRows);
            m_PkgMgrIndicesDirty = false;
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
            string lastGroup = null;
            bool useGrouping = (m_PkgMgrSortField == "Creator");

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!IsPackageManagerItemVisible(item)) continue;

                if (useGrouping)
                {
                    string currentGroup = "";
                    if (m_PkgMgrSortField == "Creator") currentGroup = item.Creator;
                    else if (m_PkgMgrSortField == "Category") currentGroup = item.Type;
                    else if (m_PkgMgrSortField == "Name") currentGroup = item.GroupId;

                    if (currentGroup != lastGroup && !string.IsNullOrEmpty(currentGroup))
                    {
                        rows.Add(new PackageManagerVisibleRow { Index = -1, Header = currentGroup });
                        lastGroup = currentGroup;
                    }
                }

                rows.Add(new PackageManagerVisibleRow { Index = i });
            }
        }

        private int GetPackageManagerSelectionCount()
        {
            int count = 0;
            foreach (var item in m_AddonList) if (item.Checked) count++;
            foreach (var item in m_AllList) if (item.Checked) count++;
            return count;
        }

        // Desktop Context Menu
        private bool m_ShowDesktopContextMenu = false;
        private Rect m_DesktopContextMenuRect = new Rect(0, 0, 250, 100);
        private PackageManagerItem m_ContextMenuTargetItem;
        private string m_PkgMgrStatusMessage = "";
        private float m_PkgMgrStatusTimer = 0f;

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

            if (!string.IsNullOrEmpty(m_PkgMgrFilter))
            {
                if (item.Uid.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0 && 
                    item.Type.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Path.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
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
            GUILayout.Label(new GUIContent("Filter:", "Search by package name, type or path"), GUILayout.Width(40));
            GUI.SetNextControlName("PkgMgrFilter");
            string newPkgMgrFilter = GUILayout.TextField(m_PkgMgrFilter, GUILayout.MinWidth(100), GUILayout.MaxWidth(400));
            if (newPkgMgrFilter != m_PkgMgrFilter)
            {
                m_PkgMgrFilter = newPkgMgrFilter;
                m_PkgMgrFilterLower = m_PkgMgrFilter.ToLower();
                m_PkgMgrIndicesDirty = true;
                UpdatePkgMgrHighlights();
            }

            if (GUI.GetNameOfFocusedControl() == "PkgMgrFilter" && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                m_PkgMgrFilter = "";
                m_PkgMgrFilterLower = "";
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
                Refresh();
                ScanPackageManagerPackages();
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
                if (h > 0) m_PkgMgrTopHeight = h;
            }
            
            GUILayout.Space(5);

            float previewWidth = m_PkgMgrShowPreview ? 320 : 0;
            float leftPaneWidth = windowWidth - previewWidth - (m_PkgMgrShowPreview ? 26 : 10); 
            
            const float verticalOverhead = 35f;
            
            float totalContentHeightAvailable = Mathf.Max(200f, windowHeight - m_PkgMgrTopHeight - verticalOverhead);
            float tablesContentHeight = totalContentHeightAvailable - m_PkgMgrFooterHeight - 15;
            
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

            GUILayout.BeginVertical(GUILayout.Width(leftPaneWidth));

            GUILayout.BeginVertical(GUILayout.Height(hTop + 35));
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Loaded ({0} | {1} vis | {2} sel | {3})", m_PkgMgrAddonCount, m_AddonVisibleRows.Count, addonSelectedCount, FormatSize(addonSelectedSize)), m_StyleSubHeader, GUILayout.Width(leftPaneWidth - 120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("All", "Select all currently visible loaded packages"), m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AddonList, true, m_AllList);
            if (GUILayout.Button(new GUIContent("None", "Deselect all packages in this list"), m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AddonList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AddonList, ref m_AddonScroll, ref m_AddonLastSelectedIndex, leftPaneWidth, hTop, ref m_AddonFirstVisible, ref m_AddonLastVisible);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal(m_StyleSection, GUILayout.Height(35));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("<b>▼ Unload ▼</b>", "Move selected packages to 'AllPackages' (Unloaded)"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) PerformMove(m_AddonList, true);
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("<b>[ Lock ]</b>", "Lock/Unlock selected packages to prevent accidental move/unload"), m_StyleButton, GUILayout.Width(75), GUILayout.Height(25))) ToggleLockSelection();
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("<b><color=#add8e6>[ Auto-Load ]</color></b>", "Toggle Auto-Load for selected packages. AL packages load automatically on startup"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) ToggleAutoLoadSelection();
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("<b>[ Isolate ]</b>", "Keep only selected/active packages and unload the rest"), m_StyleButton, GUILayout.Width(75), GUILayout.Height(25))) PerformKeepSelectedUnloadRest();
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("<b>▲ Load ▲</b>", "Move selected packages to 'AddonPackages' (Loaded)"), m_StyleButton, GUILayout.Width(110), GUILayout.Height(25))) PerformMove(m_AllList, false);
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
            GUILayout.EndHorizontal();

            if (Event.current.type != EventType.Layout)
            {
                Rect splitterRect = GUILayoutUtility.GetLastRect();
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
            GUILayout.Label(string.Format("Unloaded ({0} | {1} vis | {2} sel | {3})", m_PkgMgrAllCount, m_AllVisibleRows.Count, allSelectedCount, FormatSize(allSelectedSize)), m_StyleSubHeader, GUILayout.Width(leftPaneWidth - 120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("All", "Select all currently visible unloaded packages"), m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AllList, true, m_AddonList);
            if (GUILayout.Button(new GUIContent("None", "Deselect all packages in this list"), m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AllList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AllList, ref m_AllScroll, ref m_AllLastSelectedIndex, leftPaneWidth, hBottom, ref m_AllFirstVisible, ref m_AllLastVisible);
            GUILayout.EndVertical();

            GUILayout.Space(10);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal(m_StyleSection, GUILayout.Width(leftPaneWidth));
            string footerMessage;
            if (m_PkgMgrStatusTimer > Time.realtimeSinceStartup)
            {
                footerMessage = "<b>" + m_PkgMgrStatusMessage + "</b>";
            }
            else if (!string.IsNullOrEmpty(m_PkgMgrPreviewHint))
            {
                footerMessage = m_PkgMgrPreviewHint;
            }
            else
            {
                footerMessage = GUI.tooltip;
            }
            GUILayout.Label(footerMessage, m_StyleInfoCardText, GUILayout.Height(20), GUILayout.Width(leftPaneWidth - 20));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                float h = GUILayoutUtility.GetLastRect().height;
                if (h > 0) m_PkgMgrFooterHeight = h;
            }

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

            foreach (var item in list)
            {
                if (IsPackageManagerItemVisible(item))
                {
                    if (item.Locked && state && !m_PkgMgrCategoryInclusive.Contains("Locked (L)")) continue;
                    item.Checked = state;
                    if (state) m_PkgMgrSelectedInLoaded = (list == m_AddonList);
                }
            }
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
                    options.Add(new ContextMenuPanel.Option("Resolve Dependencies (Select " + item.MissingDependencies.Count + ")", () => {
                        ResolveDependencies(item);
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
            if (item.MissingDependencies == null) return;
            foreach (var dep in item.MissingDependencies)
            {
                foreach (var other in m_AllList)
                {
                    if (other.Uid.Equals(dep, StringComparison.OrdinalIgnoreCase))
                    {
                        other.Checked = true;
                        break;
                    }
                }
            }
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
                    if (GUILayout.Button("Resolve Dependencies (Select " + m_ContextMenuTargetItem.MissingDependencies.Count + ")", m_StyleButton))
                    {
                        ResolveDependencies(m_ContextMenuTargetItem);
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
            DrawPackageManagerRowContent(item, rowRect, col1Width, col2Width, col3Width, col4Width);
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

        void DrawPackageManagerRowContent(PackageManagerItem item, Rect rowRect, float col1Width, float col2Width, float col3Width, float col4Width)
        {
            var prevContentColor = GUI.contentColor;
            if (item.Locked) GUI.contentColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

            float x = rowRect.x + 2;
            DrawLabelWithEllipsis(new Rect(x, rowRect.y, col1Width, rowRect.height), item.Type, m_StylePkgMgrRow, item.Path, ref item.LastTypeWidth, ref item.TruncatedTypeContent);
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
                    previewHint = "Use Scroll wheel while hovering over image preview to change pacakge selection in table.";
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
                ref Vector2 scroll = ref (m_PkgMgrSelectedInLoaded ? ref m_AddonScroll : ref m_AllScroll);
                ref int lastIdx = ref (m_PkgMgrSelectedInLoaded ? ref m_AddonLastSelectedIndex : ref m_AllLastSelectedIndex);
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

            m_PkgMgrSelectedTab = GUILayout.SelectionGrid(m_PkgMgrSelectedTab, m_PkgMgrTabs, 2, m_StyleButtonSmall);

            GUILayout.Space(5);
            
            float previewScrollHeight = height - imgSize - 75;

            m_PkgMgrInfoScroll = GUILayout.BeginScrollView(m_PkgMgrInfoScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.box, GUILayout.Width(width - 20), GUILayout.Height(previewScrollHeight));
            if (m_PkgMgrSelectedTab == 0)
            {
                GUILayout.Label("<b>Description:</b>", m_StyleInfoCardTextWrapped);
                GUILayout.Label(string.IsNullOrEmpty(m_PkgMgrSelectedDescription) ? "No description available." : m_PkgMgrSelectedDescription, m_StyleInfoCardTextWrapped);
            }
            else if (m_PkgMgrSelectedTab == 1)
            {
                GUILayout.Label("<b>Dependencies:</b>", m_StyleInfoCardTextWrapped);
                if (m_PkgMgrSelectedItem.MissingDependencies != null && m_PkgMgrSelectedItem.MissingDependencies.Count > 0)
                {
                    GUILayout.Label("<color=red>Missing:</color>", m_StyleInfoCardTextWrapped);
                    foreach (var dep in m_PkgMgrSelectedItem.MissingDependencies)
                    {
                        GUILayout.Label("- " + dep, m_StyleInfoCardTextWrapped);
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
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        void PerformMove(System.Collections.Generic.List<PackageManagerItem> sourceList, bool isMovingToAll)
        {
            HashSet<string> toMoveUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in sourceList)
            {
                if (item.Checked && !item.Locked)
                {
                    toMoveUids.Add(item.Uid);
                    if (!isMovingToAll && Settings.Instance.LoadDependenciesWithPackage.Value)
                    {
                        ProtectPackage(item.Uid, toMoveUids);
                    }
                }
            }

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
                    if (File.Exists(targetPath)) continue;

                    try { File.Move(item.Path, targetPath); }
                    catch (Exception ex) { LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
                }
            }
            Refresh();
            RemoveEmptyFolder("AddonPackages");
            RemoveEmptyFolder("AllPackages");
            ScanPackageManagerPackages();
        }

        private void PerformKeepSelectedUnloadRest()
        {
            HashSet<string> keepUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in m_AddonList)
            {
                if (item.Checked)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }
            foreach (var item in m_AllList)
            {
                if (item.Checked)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }

            foreach (var item in m_AddonList)
            {
                if (item.IsActive)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }

            int movedToAll = 0;
            int movedToAddon = 0;

            foreach (var item in m_AddonList)
            {
                if (item.Locked) continue;
                if (keepUids.Contains(item.Uid)) continue;

                string targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) continue;

                try 
                { 
                    File.Move(item.Path, targetPath); 
                    movedToAll++;
                }
                catch (Exception ex) { LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
            }

            foreach (var item in m_AllList)
            {
                if (item.Locked) continue;
                if (!keepUids.Contains(item.Uid)) continue;

                string targetPath = "AddonPackages" + item.Path.Substring("AllPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) continue;

                try 
                { 
                    File.Move(item.Path, targetPath); 
                    movedToAddon++;
                }
                catch (Exception ex) { LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
            }

            if (movedToAll > 0 || movedToAddon > 0)
            {
                m_PkgMgrStatusMessage = string.Format("Isolate complete: Kept {0} (Loaded {1}, Unloaded {2}).", keepUids.Count, movedToAddon, movedToAll);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;
                Refresh();
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                ScanPackageManagerPackages();
            }
            else
            {
                m_PkgMgrStatusMessage = "No changes needed.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        public void ScanPackageManagerPackages()
        {
            if (m_ScanPkgMgrCo != null) StopCoroutine(m_ScanPkgMgrCo);
            m_ScanPkgMgrCo = StartCoroutine(ScanPackageManagerPackagesCo());
        }

        private System.Collections.IEnumerator ScanPackageManagerPackagesCo()
        {
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

            m_PkgMgrCategories.Clear();
            m_PkgMgrCategories.Add("All");
            m_PkgMgrCategories.Add("Active");
            m_PkgMgrCategories.Add("Locked (L)");
            m_PkgMgrCategories.Add("Auto-Load (AL)");
            m_PkgMgrCategories.Add("Latest");
            m_PkgMgrCategories.Add("Old Version");

            m_PkgMgrCategoryCounts.Clear();
            m_PkgMgrCategoryCounts["All"] = 0;
            m_PkgMgrCategoryCounts["Active"] = 0;
            m_PkgMgrCategoryCounts["Locked (L)"] = 0;
            m_PkgMgrCategoryCounts["Auto-Load (AL)"] = 0;
            m_PkgMgrCategoryCounts["Latest"] = 0;
            m_PkgMgrCategoryCounts["Old Version"] = 0;

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

            m_AddonList.Clear();
            yield return ScanDirectoryCo("AddonPackages", m_AddonList, protectedPackages, types, latestUids, sw);
            m_PkgMgrAddonCount = m_AddonList.Count;

            m_AllList.Clear();
            yield return ScanDirectoryCo("AllPackages", m_AllList, protectedPackages, types, latestUids, sw);
            m_PkgMgrAllCount = m_AllList.Count;
            
            var sortedTypes = new System.Collections.Generic.List<string>(types);
            sortedTypes.Sort();
            m_PkgMgrCategories.AddRange(sortedTypes);
            
            SortPackageManagerList();
            UpdatePkgMgrHighlights();
            m_PkgMgrIndicesDirty = true;
            m_ScanPkgMgrCo = null;
        }

        private void UpdatePkgMgrHighlights()
        {
            foreach (var item in m_AddonList) UpdatePkgMgrItemCache(item);
            foreach (var item in m_AllList) UpdatePkgMgrItemCache(item);
        }

        private void UpdatePkgMgrItemCache(PackageManagerItem item)
        {
            string highlighted = HighlightSearchText(item.Uid, m_PkgMgrFilter);
            item.HighlightedUid = highlighted;
            string label = item.StatusPrefix + highlighted;
            if (item.AutoLoad) label = "<color=#add8e6>" + label + "</color>";
            item.NameContent = new GUIContent(label, item.Path);
            
            string depTooltip = "";
            if (item.MissingDependencies != null && item.MissingDependencies.Count > 0)
            {
                depTooltip = "Missing:\n" + string.Join("\n", item.MissingDependencies.ToArray());
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

        private System.Collections.IEnumerator ScanDirectoryCo(string path, System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.HashSet<string> protectedPackages, System.Collections.Generic.HashSet<string> types, System.Collections.Generic.HashSet<string> latestUids, System.Diagnostics.Stopwatch sw)
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
                List<string> missingDeps = new List<string>();
                foreach (var dep in deepDeps)
                {
                    var resolved = FileManager.ResolveDependency(dep);
                    if (resolved != null && !resolved.Path.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) 
                    {
                        loadedDepCount++;
                    }
                    else
                    {
                        missingDeps.Add(dep);
                    }
                }

                var item = new PackageManagerItem {
                    Uid = name,
                    Creator = PackageIDToCreator(name),
                    Path = relativePath,
                    Type = type,
                    Size = file.Length,
                    LastWriteTime = lwt,
                    AgeString = FormatAge(lwt, now),
                    DependencyCount = depCount,
                    LoadedDependencyCount = loadedDepCount,
                    MissingDependencies = missingDeps,
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
                    if (!m_PkgMgrCategoryCounts.ContainsKey(type)) m_PkgMgrCategoryCounts[type] = 0;
                    m_PkgMgrCategoryCounts[type]++;
                }
                
                m_PkgMgrCategoryCounts["All"]++;
                if (isProtected) m_PkgMgrCategoryCounts["Active"]++;
                if (isLocked) m_PkgMgrCategoryCounts["Locked (L)"]++;
                if (isAutoLoad) m_PkgMgrCategoryCounts["Auto-Load (AL)"]++;
                if (isLatest) m_PkgMgrCategoryCounts["Latest"]++;
                else m_PkgMgrCategoryCounts["Old Version"]++;

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
