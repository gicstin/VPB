using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private GameObject m_DepWhitelistUGUIRoot;
        private GameObject m_DepWhitelistUGUIPanel;
        private InputField m_DepWhitelistUGUIFilterInput;
        private ScrollRect m_DepWhitelistUGUIScroll;
        private RectTransform m_DepWhitelistUGUIContent;
        private RectTransform m_DepWhitelistUGUIViewport;
        private string m_DepWhitelistFilter = "";
        private bool m_DepWhitelistSuppressToggle;
        private List<string> m_DepWhitelistAllGroupsCache;
        private float m_DepWhitelistAllGroupsCacheTime = -9999f;
        private bool m_DepWhitelistOnlyWhitelisted;
        private Text m_DepWhitelistOnlyBtnText;
        private readonly List<DepWhitelistRow> m_DepWhitelistPool = new List<DepWhitelistRow>();
        private int m_DepWhitelistFirst = -1;
        private List<string> m_DepWhitelistVisibleGroups = new List<string>();
        private const float DepWhitelistRowHeight = 34f;
        private const int DepWhitelistPoolSize = 60;

        private class DepWhitelistRow
        {
            public GameObject Root;
            public Toggle Toggle;
            public Text Label;
            public string Group;
            public int BoundVisibleIndex = -1;
        }

        private readonly List<DepWhitelistRow> m_DepWhitelistRows = new List<DepWhitelistRow>();

        private class DepWhitelistDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target;
            public void OnBeginDrag(PointerEventData eventData) { }
            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null) return;
                Target.anchoredPosition += eventData.delta;
            }
        }

        private class DepWhitelistResizeHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target;
            public Vector2 MinSize = new Vector2(650, 520);
            public Vector2 MaxSize = new Vector2(1600, 1200);

            public void OnBeginDrag(PointerEventData eventData) { }
            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null) return;
                Vector2 size = Target.sizeDelta;
                size.x += eventData.delta.x;
                size.y -= eventData.delta.y;
                size.x = Mathf.Clamp(size.x, MinSize.x, MaxSize.x);
                size.y = Mathf.Clamp(size.y, MinSize.y, MaxSize.y);
                Target.sizeDelta = size;
            }
        }

        private bool IsDependencyWhitelistUGUIVisible()
        {
            return m_DepWhitelistUGUIRoot != null && m_DepWhitelistUGUIRoot.activeSelf;
        }

        private static HashSet<string> ParsePackageGroupListLocal(string raw)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return set;
            string[] parts = System.Text.RegularExpressions.Regex.Split(raw, "[\\s,;]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                p = p.Trim();
                if (p.Length == 0) continue;
                set.Add(p);
            }
            return set;
        }

        private static string SerializePackageGroupListLocal(IEnumerable<string> groups)
        {
            if (groups == null) return "";
            return string.Join(", ", groups.Where(g => !string.IsNullOrEmpty(g)).Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static bool TryGetPackageGroupFromVarFileName(string fileNameNoExt, out string group)
        {
            group = null;
            if (string.IsNullOrEmpty(fileNameNoExt)) return false;

            // Expected: Author.Package.123 (but tolerate malformed versions)
            int lastDot = fileNameNoExt.LastIndexOf('.');
            if (lastDot <= 0) return false;

            string maybeGroup = fileNameNoExt.Substring(0, lastDot);
            if (string.IsNullOrEmpty(maybeGroup)) return false;

            // Ensure group looks like Author.Package (two segments)
            int firstDot = maybeGroup.IndexOf('.');
            if (firstDot <= 0) return false;
            if (maybeGroup.IndexOf('.', firstDot + 1) != -1) return false;

            group = maybeGroup;
            return true;
        }

        private List<string> GetAllPackageGroupsFromDiskCached()
        {
            // Cache for a short time to avoid repeated scans while typing.
            float now = Time.unscaledTime;
            if (m_DepWhitelistAllGroupsCache != null && (now - m_DepWhitelistAllGroupsCacheTime) < 10f)
                return m_DepWhitelistAllGroupsCache;

            HashSet<string> groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                List<string> files = new List<string>();
                if (System.IO.Directory.Exists("AddonPackages")) FileManager.SafeGetFiles("AddonPackages", "*.var", files);
                if (System.IO.Directory.Exists("AllPackages")) FileManager.SafeGetFiles("AllPackages", "*.var", files);

                for (int i = 0; i < files.Count; i++)
                {
                    string f = files[i];
                    if (string.IsNullOrEmpty(f)) continue;
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (TryGetPackageGroupFromVarFileName(name, out string g)) groups.Add(g);
                }
            }
            catch { }

            var list = groups.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            m_DepWhitelistAllGroupsCache = list;
            m_DepWhitelistAllGroupsCacheTime = now;
            return m_DepWhitelistAllGroupsCache;
        }

        private HashSet<string> GetDependencyIgnoreSet()
        {
            try
            {
                return DependencyWhitelistManager.Instance.GetWhitelistedPackageGroups();
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void PersistDependencyIgnoreSet(HashSet<string> set)
        {
            try
            {
                var mgr = DependencyWhitelistManager.Instance;
                var existing = mgr.GetWhitelistedPackageGroups();
                foreach (var g in existing) mgr.SetWhitelisted(g, false, false);
                if (set != null)
                {
                    foreach (var g in set) mgr.SetWhitelisted(g, true, false);
                }
                mgr.Save();
            }
            catch { }
        }

        private void EnsureDependencyWhitelistUGUI()
        {
            if (m_DepWhitelistUGUIRoot != null) return;

            EnsurePkgMgrEventSystem();

            m_DepWhitelistUGUIRoot = new GameObject("VPB_DependencyWhitelistUGUI");
            var canvas = m_DepWhitelistUGUIRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2001;
            m_DepWhitelistUGUIRoot.AddComponent<GraphicRaycaster>();

            var scaler = m_DepWhitelistUGUIRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var rootRt = m_DepWhitelistUGUIRoot.GetComponent<RectTransform>();
            if (rootRt != null)
            {
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.sizeDelta = Vector2.zero;
            }

            var blocker = UI.AddChildGOImage(m_DepWhitelistUGUIRoot, new Color(0f, 0f, 0f, 0.35f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            blocker.name = "Blocker";

            m_DepWhitelistUGUIPanel = UI.AddChildGOImage(m_DepWhitelistUGUIRoot, new Color(0.12f, 0.12f, 0.12f, 0.97f), AnchorPresets.middleCenter, 900, 720, Vector2.zero);
            m_DepWhitelistUGUIPanel.name = "Panel";

            var panelRT = m_DepWhitelistUGUIPanel.GetComponent<RectTransform>();

            // Large draggable header hit area (behind title/search/buttons so it won't steal clicks).
            var dragBar = UI.AddChildGOImage(m_DepWhitelistUGUIPanel, new Color(0f, 0f, 0f, 0f), AnchorPresets.hStretchTop, 0, 0, Vector2.zero);
            dragBar.name = "DragBar";
            dragBar.transform.SetAsFirstSibling();
            var dragRT = dragBar.GetComponent<RectTransform>();
            dragRT.anchorMin = new Vector2(0, 1);
            dragRT.anchorMax = new Vector2(1, 1);
            dragRT.pivot = new Vector2(0.5f, 1);
            dragRT.offsetMin = new Vector2(0, -130);
            dragRT.offsetMax = new Vector2(0, 0);
            var dragger = dragBar.AddComponent<DepWhitelistDragHandler>();
            dragger.Target = panelRT;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(m_DepWhitelistUGUIPanel.transform, false);
            var titleText = titleGo.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.text = "Dependency Whitelist";
            titleText.fontSize = 30;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.sizeDelta = new Vector2(-120, 60);
            titleRt.anchoredPosition = new Vector2(60, -10);

            // Draggable overlay for the whole title bar (excluding the close button area).
            var titleDrag = UI.AddChildGOImage(m_DepWhitelistUGUIPanel, new Color(0f, 0f, 0f, 0.01f), AnchorPresets.hStretchTop, 0, 60, Vector2.zero);
            titleDrag.name = "TitleDragArea";
            var titleDragRT = titleDrag.GetComponent<RectTransform>();
            titleDragRT.anchorMin = new Vector2(0, 1);
            titleDragRT.anchorMax = new Vector2(1, 1);
            titleDragRT.pivot = new Vector2(0.5f, 1);
            titleDragRT.offsetMin = new Vector2(0, -60);
            titleDragRT.offsetMax = new Vector2(-70, 0);
            var titleDragger = titleDrag.AddComponent<DepWhitelistDragHandler>();
            titleDragger.Target = panelRT;

            var closeBtn = UI.CreateUIButton(m_DepWhitelistUGUIPanel, 50, 50, "X", 26, -10, -10, AnchorPresets.topRight, CloseDependencyWhitelistUGUI);
            closeBtn.name = "CloseButton";

            var filterGo = UI.CreateTextInput(m_DepWhitelistUGUIPanel, 520, 42, "Search...", 20, 20, -80, AnchorPresets.topLeft, null);
            filterGo.name = "FilterInput";
            m_DepWhitelistUGUIFilterInput = filterGo.GetComponent<InputField>();
            var filterRt = filterGo.GetComponent<RectTransform>();
            if (filterRt != null)
            {
                // Stretch with panel width; leave space on the right for buttons.
                filterRt.anchorMin = new Vector2(0, 1);
                filterRt.anchorMax = new Vector2(1, 1);
                filterRt.pivot = new Vector2(0, 1);
                filterRt.offsetMin = new Vector2(20, -122);
                // Add a small gap before the right-side buttons.
                filterRt.offsetMax = new Vector2(-330, -80);
            }
            if (m_DepWhitelistUGUIFilterInput != null)
            {
                m_DepWhitelistUGUIFilterInput.text = m_DepWhitelistFilter ?? "";
                m_DepWhitelistUGUIFilterInput.onValueChanged.AddListener((val) => { SetDependencyWhitelistFilter(val); });
                m_DepWhitelistUGUIFilterInput.onEndEdit.AddListener((val) => { SetDependencyWhitelistFilter(val); });
            }

            var onlyBtnGO = UI.CreateUIButton(m_DepWhitelistUGUIPanel, 170, 42, "Only Whitelisted: OFF", 16, 560, -80, AnchorPresets.topLeft, () => {
                m_DepWhitelistOnlyWhitelisted = !m_DepWhitelistOnlyWhitelisted;
                if (m_DepWhitelistOnlyBtnText != null)
                    m_DepWhitelistOnlyBtnText.text = m_DepWhitelistOnlyWhitelisted ? "Only Whitelisted: ON" : "Only Whitelisted: OFF";
                RefreshDependencyWhitelistUGUIList();
            });
            onlyBtnGO.name = "OnlyWhitelistedButton";
            m_DepWhitelistOnlyBtnText = onlyBtnGO.GetComponentInChildren<Text>();

            var onlyBtnRt = onlyBtnGO.GetComponent<RectTransform>();
            if (onlyBtnRt != null)
            {
                onlyBtnRt.anchorMin = new Vector2(1, 1);
                onlyBtnRt.anchorMax = new Vector2(1, 1);
                onlyBtnRt.pivot = new Vector2(1, 1);
                onlyBtnRt.sizeDelta = new Vector2(170, 42);
                onlyBtnRt.anchoredPosition = new Vector2(-150, -80);
            }

            var refreshBtnGO = UI.CreateUIButton(m_DepWhitelistUGUIPanel, 120, 42, "Refresh", 16, 740, -80, AnchorPresets.topLeft, () => {
                m_DepWhitelistAllGroupsCache = null;
                m_DepWhitelistAllGroupsCacheTime = -9999f;
                RefreshDependencyWhitelistUGUIList();
            });
            refreshBtnGO.name = "RefreshButton";

            var refreshBtnRt = refreshBtnGO.GetComponent<RectTransform>();
            if (refreshBtnRt != null)
            {
                refreshBtnRt.anchorMin = new Vector2(1, 1);
                refreshBtnRt.anchorMax = new Vector2(1, 1);
                refreshBtnRt.pivot = new Vector2(1, 1);
                refreshBtnRt.sizeDelta = new Vector2(120, 42);
                refreshBtnRt.anchoredPosition = new Vector2(-20, -80);
            }

            var helpGo = new GameObject("Help");
            helpGo.transform.SetParent(m_DepWhitelistUGUIPanel.transform, false);
            var helpText = helpGo.AddComponent<Text>();
            helpText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            helpText.text = "Checked = whitelisted (ignored by Force Latest).";
            helpText.fontSize = 16;
            helpText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            helpText.alignment = TextAnchor.MiddleLeft;
            var helpRt = helpGo.GetComponent<RectTransform>();
            helpRt.anchorMin = new Vector2(0, 1);
            helpRt.anchorMax = new Vector2(1, 1);
            helpRt.pivot = new Vector2(0.5f, 1);
            helpRt.sizeDelta = new Vector2(-40, 30);
            helpRt.anchoredPosition = new Vector2(20, -130);

            var scrollBg = UI.AddChildGOImage(m_DepWhitelistUGUIPanel, new Color(0.10f, 0.10f, 0.10f, 0.95f), AnchorPresets.stretchAll, -40, -190, new Vector2(0, -30));
            scrollBg.name = "ScrollBG";
            var scrollBgRt = scrollBg.GetComponent<RectTransform>();
            scrollBgRt.anchorMin = new Vector2(0, 0);
            scrollBgRt.anchorMax = new Vector2(1, 1);
            scrollBgRt.pivot = new Vector2(0.5f, 0.5f);
            scrollBgRt.offsetMin = new Vector2(20, 20);
            scrollBgRt.offsetMax = new Vector2(-20, -170);

            CreateDependencyWhitelistScroll(scrollBg);

            // Resize handle (triangle style, like Gallery panel).
            var resizeHandle = new GameObject("ResizeHandle");
            resizeHandle.transform.SetParent(m_DepWhitelistUGUIPanel.transform, false);
            var rhImg = resizeHandle.AddComponent<Image>();
            rhImg.color = new Color(0f, 0f, 0f, 0.01f); // Invisible hit area
            var rhRT = resizeHandle.GetComponent<RectTransform>();
            rhRT.anchorMin = new Vector2(1, 0);
            rhRT.anchorMax = new Vector2(1, 0);
            rhRT.pivot = new Vector2(1, 0);
            rhRT.sizeDelta = new Vector2(60, 60);
            rhRT.anchoredPosition = new Vector2(20, -20);

            var triGO = new GameObject("Triangle");
            triGO.transform.SetParent(resizeHandle.transform, false);
            var triText = triGO.AddComponent<Text>();
            triText.raycastTarget = false;
            triText.text = "◢";
            triText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            triText.fontSize = 36;
            triText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            triText.alignment = TextAnchor.MiddleCenter;
            var triRT = triGO.GetComponent<RectTransform>();
            triRT.anchorMin = Vector2.zero;
            triRT.anchorMax = Vector2.one;
            triRT.sizeDelta = Vector2.zero;

            // Hover indicator: turn triangle green on hover/drag.
            var triHover = resizeHandle.AddComponent<UIHoverColor>();
            triHover.targetText = triText;
            triHover.normalColor = triText.color;
            triHover.hoverColor = Color.green;

            var resizer = resizeHandle.AddComponent<DepWhitelistResizeHandler>();
            resizer.Target = panelRT;

            m_DepWhitelistUGUIRoot.SetActive(false);
        }

        private void CreateDependencyWhitelistScroll(GameObject parent)
        {
            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(parent.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.pivot = new Vector2(0.5f, 0.5f);
            viewportRT.offsetMin = new Vector2(0, 0);
            viewportRT.offsetMax = new Vector2(-18, 0);
            viewportGO.AddComponent<RectMask2D>();

            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);

            var scrollbarGO = UI.CreateScrollBar(parent, 15f, 0f, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1, 0);
                sbRT.anchorMax = new Vector2(1, 1);
                sbRT.pivot = new Vector2(1, 0.5f);
                sbRT.sizeDelta = new Vector2(15f, 0);
            }

            var scrollRect = parent.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = null; // Decouple for manual sync
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sync.minSizePixels = 30f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            m_DepWhitelistUGUIScroll = scrollRect;
            m_DepWhitelistUGUIViewport = viewportRT;
            m_DepWhitelistUGUIContent = contentRT;

            scrollRect.onValueChanged.AddListener((v) => { RefreshDependencyWhitelistUGUIVirtualRows(); });
            EnsureDependencyWhitelistUGUIRowPool(contentRT);
        }

        private void EnsureDependencyWhitelistUGUIRowPool(RectTransform contentRT)
        {
            if (contentRT == null) return;
            if (m_DepWhitelistPool.Count > 0) return;

            for (int i = 0; i < DepWhitelistPoolSize; i++)
            {
                m_DepWhitelistPool.Add(CreateDependencyWhitelistUGUIRow(contentRT));
            }
        }

        private DepWhitelistRow CreateDependencyWhitelistUGUIRow(RectTransform parent)
        {
            GameObject rowGO = UI.CreateUIToggle(parent.gameObject, 0, (int)DepWhitelistRowHeight, "", 18, 0, 0, AnchorPresets.hStretchTop, (val) => { });
            rowGO.name = "Row";

            var rt = rowGO.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.sizeDelta = new Vector2(0, DepWhitelistRowHeight);
            }

            Toggle t = rowGO.GetComponent<Toggle>();
            if (t != null)
            {
                t.navigation = new Navigation { mode = Navigation.Mode.None };
                t.onValueChanged.RemoveAllListeners();
            }

            Text label = rowGO.GetComponentInChildren<Text>();

            DepWhitelistRow row = new DepWhitelistRow
            {
                Root = rowGO,
                Toggle = t,
                Label = label,
                Group = null
            };

            if (t != null)
            {
                t.onValueChanged.AddListener((val) => {
                    if (m_DepWhitelistSuppressToggle) return;
                    if (row.BoundVisibleIndex < 0) return;
                    if (m_DepWhitelistVisibleGroups == null || row.BoundVisibleIndex >= m_DepWhitelistVisibleGroups.Count) return;
                    string group = m_DepWhitelistVisibleGroups[row.BoundVisibleIndex];
                    if (string.IsNullOrEmpty(group)) return;
                    try { DependencyWhitelistManager.Instance.SetWhitelisted(group, val, true); } catch { }
                });
            }

            return row;
        }

        private void SetDependencyWhitelistFilter(string filter)
        {
            string newFilter = filter ?? "";
            if (newFilter == m_DepWhitelistFilter) return;
            m_DepWhitelistFilter = newFilter;
            RefreshDependencyWhitelistUGUIList();
        }

        private void RefreshDependencyWhitelistUGUIList()
        {
            if (m_DepWhitelistUGUIRoot == null || !m_DepWhitelistUGUIRoot.activeSelf) return;
            if (m_DepWhitelistUGUIContent == null) return;

            HashSet<string> ignore = GetDependencyIgnoreSet();
            string f = (m_DepWhitelistFilter ?? "").Trim().ToLowerInvariant();

            List<string> groups;
            if (m_DepWhitelistOnlyWhitelisted)
            {
                groups = ignore.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                groups = GetAllPackageGroupsFromDiskCached();
                if (!string.IsNullOrEmpty(f))
                    groups = groups.Where(g => g != null && g.ToLowerInvariant().Contains(f)).ToList();
            }

            m_DepWhitelistVisibleGroups = groups ?? new List<string>();

            float h = m_DepWhitelistVisibleGroups.Count * DepWhitelistRowHeight;
            m_DepWhitelistUGUIContent.sizeDelta = new Vector2(0, h);

            m_DepWhitelistFirst = -1;
            RefreshDependencyWhitelistUGUIVirtualRows();
        }

        private void RefreshDependencyWhitelistUGUIVirtualRows()
        {
            if (m_DepWhitelistUGUIRoot == null || !m_DepWhitelistUGUIRoot.activeSelf) return;
            if (m_DepWhitelistUGUIScroll == null || m_DepWhitelistUGUIViewport == null || m_DepWhitelistUGUIContent == null) return;
            if (m_DepWhitelistPool == null || m_DepWhitelistPool.Count == 0) return;
            if (m_DepWhitelistVisibleGroups == null) return;

            int total = m_DepWhitelistVisibleGroups.Count;
            float contentH = total * DepWhitelistRowHeight;
            m_DepWhitelistUGUIContent.sizeDelta = new Vector2(0, contentH);

            float viewportH = m_DepWhitelistUGUIViewport.rect.height;
            float maxScroll = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - m_DepWhitelistUGUIScroll.verticalNormalizedPosition) * maxScroll;
            int first = Mathf.FloorToInt(scrollY / DepWhitelistRowHeight);
            if (first < 0) first = 0;
            if (first > Mathf.Max(0, total - 1)) first = Mathf.Max(0, total - 1);

            m_DepWhitelistFirst = first;

            HashSet<string> ignore = GetDependencyIgnoreSet();

            m_DepWhitelistSuppressToggle = true;
            try
            {
                for (int i = 0; i < m_DepWhitelistPool.Count; i++)
                {
                    int visibleIdx = first + i;
                    var row = m_DepWhitelistPool[i];
                    if (row == null || row.Root == null) continue;

                    if (visibleIdx >= total)
                    {
                        if (row.Root.activeSelf) row.Root.SetActive(false);
                        row.BoundVisibleIndex = -1;
                        row.Group = null;
                        continue;
                    }

                    string group = m_DepWhitelistVisibleGroups[visibleIdx];
                    if (string.IsNullOrEmpty(group))
                    {
                        if (row.Root.activeSelf) row.Root.SetActive(false);
                        row.BoundVisibleIndex = -1;
                        row.Group = null;
                        continue;
                    }

                    if (!row.Root.activeSelf) row.Root.SetActive(true);
                    row.BoundVisibleIndex = visibleIdx;
                    row.Group = group;

                    var rt = row.Root.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchoredPosition = new Vector2(0, -visibleIdx * DepWhitelistRowHeight);
                    }

                    if (row.Label != null) row.Label.text = group;
                    if (row.Toggle != null) row.Toggle.isOn = ignore.Contains(group);
                }
            }
            finally
            {
                m_DepWhitelistSuppressToggle = false;
            }
        }

        private void OnDependencyWhitelistToggle(string group, bool isWhitelisted)
        {
            if (m_DepWhitelistSuppressToggle) return;
            if (string.IsNullOrEmpty(group)) return;
            try
            {
                DependencyWhitelistManager.Instance.SetWhitelisted(group, isWhitelisted, true);
            }
            catch { }
        }

        private void OpenDependencyWhitelistUGUI()
        {
            EnsureDependencyWhitelistUGUI();
            m_DepWhitelistOnlyWhitelisted = false;
            if (m_DepWhitelistOnlyBtnText != null) m_DepWhitelistOnlyBtnText.text = "Only Whitelisted: OFF";
            if (m_DepWhitelistUGUIFilterInput != null) m_DepWhitelistUGUIFilterInput.text = m_DepWhitelistFilter ?? "";
            m_DepWhitelistUGUIRoot.SetActive(true);
            RefreshDependencyWhitelistUGUIList();
        }

        private void CloseDependencyWhitelistUGUI()
        {
            if (m_DepWhitelistUGUIRoot != null) m_DepWhitelistUGUIRoot.SetActive(false);
        }
    }
}
