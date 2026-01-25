using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private GameObject m_PkgMgrUGUIRoot;
        private GameObject m_PkgMgrUGUIPanel;
        private InputField m_PkgMgrUGUIFilterInput;
        private ScrollRect m_PkgMgrUGUILoadedScroll;
        private ScrollRect m_PkgMgrUGUIAllScroll;
        private RectTransform m_PkgMgrUGUILoadedContent;
        private RectTransform m_PkgMgrUGUIAllContent;
        private RectTransform m_PkgMgrUGUILoadedViewport;
        private RectTransform m_PkgMgrUGUIAllViewport;
        private readonly List<PkgMgrUGUIRow> m_PkgMgrUGUILoadedPool = new List<PkgMgrUGUIRow>();
        private readonly List<PkgMgrUGUIRow> m_PkgMgrUGUIAllPool = new List<PkgMgrUGUIRow>();
        private int m_PkgMgrUGUILoadedFirst = -1;
        private int m_PkgMgrUGUIAllFirst = -1;
        private const float PkgMgrUGUIRowHeight = 28f;
        private const int PkgMgrUGUIPoolSize = 40;
        private RawImage m_PkgMgrUGUIPreviewRaw;
        private Text m_PkgMgrUGUIDetailText;
        private Text m_PkgMgrUGUIDepsText;
        private Button m_PkgMgrUGUIUndoBtn;
        private Text m_PkgMgrUGUIUndoBtnText;
        private PackageManagerItem m_PkgMgrUGUILastSelectedItem;
        private int m_PkgMgrUGUILastVisibleAddonCount = -1;
        private int m_PkgMgrUGUILastVisibleAllCount = -1;

        private class PkgMgrUGUIRow
        {
            public GameObject Root;
            public RectTransform RT;
            public Image Bg;
            public Text TypeText;
            public Text NameText;
            public Text DepText;
            public Text SizeText;
            public int BoundVisibleIndex = -1;
        }

        private class PackageManagerUGUIUpdater : MonoBehaviour
        {
            public VamHookPlugin plugin;

            void Update()
            {
                if (plugin != null) plugin.UpdatePkgMgrUGUI();
            }
        }

        private bool IsPackageManagerUGUIVisible()
        {
            return m_PkgMgrUGUIRoot != null && m_PkgMgrUGUIRoot.activeSelf;
        }

        private void EnsurePkgMgrEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var esGo = new GameObject("VPB_EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        private void EnsurePkgMgrUGUI()
        {
            if (m_PkgMgrUGUIRoot != null) return;

            EnsurePkgMgrEventSystem();

            m_PkgMgrUGUIRoot = new GameObject("VPB_PackageManagerUGUI");
            var canvas = m_PkgMgrUGUIRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            m_PkgMgrUGUIRoot.AddComponent<GraphicRaycaster>();

            var scaler = m_PkgMgrUGUIRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var rootRt = m_PkgMgrUGUIRoot.GetComponent<RectTransform>();
            if (rootRt != null)
            {
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.sizeDelta = Vector2.zero;
            }

            var blocker = UI.AddChildGOImage(m_PkgMgrUGUIRoot, new Color(0f, 0f, 0f, 0.35f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            blocker.name = "Blocker";

            m_PkgMgrUGUIPanel = UI.AddChildGOImage(m_PkgMgrUGUIRoot, new Color(0.12f, 0.12f, 0.12f, 0.97f), AnchorPresets.middleCenter, 1200, 700, Vector2.zero);
            m_PkgMgrUGUIPanel.name = "Panel";

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(m_PkgMgrUGUIPanel.transform, false);
            var titleText = titleGo.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.text = "Package Manager";
            titleText.fontSize = 30;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.sizeDelta = new Vector2(-120, 60);
            titleRt.anchoredPosition = new Vector2(60, -10);

            var closeBtn = UI.CreateUIButton(m_PkgMgrUGUIPanel, 50, 50, "X", 26, -10, -10, AnchorPresets.topRight, ClosePackageManagerUGUI);
            closeBtn.name = "CloseButton";

            var filterGo = UI.CreateTextInput(m_PkgMgrUGUIPanel, 520, 42, "Filter...", 20, 20, -80, AnchorPresets.topLeft, null);
            filterGo.name = "FilterInput";
            m_PkgMgrUGUIFilterInput = filterGo.GetComponent<InputField>();
            if (m_PkgMgrUGUIFilterInput != null)
            {
                m_PkgMgrUGUIFilterInput.text = m_PkgMgrFilter ?? "";
                m_PkgMgrUGUIFilterInput.onValueChanged.AddListener((val) => { SetPkgMgrFilter(val); });
                m_PkgMgrUGUIFilterInput.onEndEdit.AddListener((val) => { SetPkgMgrFilter(val); });
            }

            CreatePkgMgrUGUILists();
            CreatePkgMgrUGUIDetails();

            var updater = m_PkgMgrUGUIRoot.AddComponent<PackageManagerUGUIUpdater>();
            updater.plugin = this;

            m_PkgMgrUGUIRoot.SetActive(false);
        }

        private void CreatePkgMgrUGUILists()
        {
            if (m_PkgMgrUGUIPanel == null) return;

            GameObject leftGO = new GameObject("Left");
            leftGO.transform.SetParent(m_PkgMgrUGUIPanel.transform, false);
            RectTransform leftRT = leftGO.AddComponent<RectTransform>();
            leftRT.anchorMin = new Vector2(0, 0);
            leftRT.anchorMax = new Vector2(0.68f, 1);
            leftRT.pivot = new Vector2(0, 0.5f);
            leftRT.offsetMin = new Vector2(20, 20);
            leftRT.offsetMax = new Vector2(-10, -140);

            float actionBarH = 54f;
            float gap = 10f;
            float topListMinY = 0.5f;

            GameObject loadedPane = CreatePkgMgrUGUIVirtualList(leftGO, "LoadedList", out m_PkgMgrUGUILoadedScroll, out m_PkgMgrUGUILoadedViewport, out m_PkgMgrUGUILoadedContent);
            RectTransform loadedRT = loadedPane.GetComponent<RectTransform>();
            loadedRT.anchorMin = new Vector2(0, topListMinY);
            loadedRT.anchorMax = new Vector2(1, 1);
            loadedRT.pivot = new Vector2(0.5f, 1);
            loadedRT.offsetMin = new Vector2(0, actionBarH + gap);
            loadedRT.offsetMax = new Vector2(0, 0);

            GameObject allPane = CreatePkgMgrUGUIVirtualList(leftGO, "AllList", out m_PkgMgrUGUIAllScroll, out m_PkgMgrUGUIAllViewport, out m_PkgMgrUGUIAllContent);
            RectTransform allRT = allPane.GetComponent<RectTransform>();
            allRT.anchorMin = new Vector2(0, 0);
            allRT.anchorMax = new Vector2(1, topListMinY);
            allRT.pivot = new Vector2(0.5f, 0);
            allRT.offsetMin = new Vector2(0, 0);
            allRT.offsetMax = new Vector2(0, -actionBarH - gap);

            GameObject actions = UI.AddChildGOImage(leftGO, new Color(0.10f, 0.10f, 0.10f, 0.95f), AnchorPresets.hStretchMiddle, 0, actionBarH, Vector2.zero);
            actions.name = "ActionsBar";
            RectTransform actionsRT = actions.GetComponent<RectTransform>();
            actionsRT.anchorMin = new Vector2(0, topListMinY);
            actionsRT.anchorMax = new Vector2(1, topListMinY);
            actionsRT.pivot = new Vector2(0.5f, 0.5f);
            actionsRT.sizeDelta = new Vector2(0, actionBarH);
            actionsRT.anchoredPosition = Vector2.zero;

            float x = 10f;
            float y = 0f;
            float w = 90f;
            float h = 40f;
            float btnGap = 8f;

            UI.CreateUIButton(actions, w, h, "Load", 18, x, y, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformMove(m_AllList, false);
            });
            x += w + btnGap;
            UI.CreateUIButton(actions, w, h, "Unload", 18, x, y, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformMove(m_AddonList, true);
            });
            x += w + btnGap;
            UI.CreateUIButton(actions, w, h, "Lock", 18, x, y, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                ToggleLockSelection();
            });
            x += w + btnGap;
            UI.CreateUIButton(actions, 110f, h, "Auto-Load", 18, x, y, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                ToggleAutoLoadSelection();
            });
            x += 110f + btnGap;
            UI.CreateUIButton(actions, 95f, h, "Isolate", 18, x, y, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformKeepSelectedUnloadRest();
            });

            var undoBtnGO = UI.CreateUIButton(actions, 80f, h, "Undo", 18, -10f, 0f, AnchorPresets.middleRight, () => {
                if (IsPackageManagerBusy()) return;
                if (m_PkgMgrUndoStack == null || m_PkgMgrUndoStack.Count == 0) return;
                var top = m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1];
                if (top == null || top.Moves == null) return;
                if (!ConfirmPackageManagerAction("Undo", top.Moves.Count)) return;
                UndoLastPackageManagerOperation();
            });
            m_PkgMgrUGUIUndoBtn = undoBtnGO.GetComponent<Button>();
            m_PkgMgrUGUIUndoBtnText = undoBtnGO.GetComponentInChildren<Text>();

            if (m_PkgMgrUGUILoadedScroll != null) m_PkgMgrUGUILoadedScroll.onValueChanged.AddListener((v) => RefreshPkgMgrUGUIList(true, false));
            if (m_PkgMgrUGUIAllScroll != null) m_PkgMgrUGUIAllScroll.onValueChanged.AddListener((v) => RefreshPkgMgrUGUIList(false, true));

            EnsurePkgMgrUGUIRowPool(m_PkgMgrUGUILoadedContent, m_PkgMgrUGUILoadedPool, true);
            EnsurePkgMgrUGUIRowPool(m_PkgMgrUGUIAllContent, m_PkgMgrUGUIAllPool, false);
        }

        private void CreatePkgMgrUGUIDetails()
        {
            if (m_PkgMgrUGUIPanel == null) return;
            if (m_PkgMgrUGUIDetailText != null || m_PkgMgrUGUIPreviewRaw != null || m_PkgMgrUGUIDepsText != null) return;

            GameObject details = UI.AddChildGOImage(m_PkgMgrUGUIPanel, new Color(0.09f, 0.09f, 0.09f, 0.92f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            details.name = "Details";
            RectTransform detailsRT = details.GetComponent<RectTransform>();
            detailsRT.anchorMin = new Vector2(0.68f, 0);
            detailsRT.anchorMax = new Vector2(1f, 1f);
            detailsRT.pivot = new Vector2(1f, 0.5f);
            detailsRT.offsetMin = new Vector2(10, 20);
            detailsRT.offsetMax = new Vector2(-20, -140);

            GameObject headerGO = new GameObject("Header");
            headerGO.transform.SetParent(details.transform, false);
            Text headerText = headerGO.AddComponent<Text>();
            headerText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            headerText.fontSize = 22;
            headerText.color = Color.white;
            headerText.alignment = TextAnchor.MiddleLeft;
            headerText.text = "Details";
            RectTransform headerRT = headerGO.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0, 1);
            headerRT.anchorMax = new Vector2(1, 1);
            headerRT.pivot = new Vector2(0.5f, 1);
            headerRT.sizeDelta = new Vector2(-20, 40);
            headerRT.anchoredPosition = new Vector2(10, -10);

            GameObject imgGO = new GameObject("Preview");
            imgGO.transform.SetParent(details.transform, false);
            RectTransform imgRT = imgGO.AddComponent<RectTransform>();
            imgRT.anchorMin = new Vector2(0, 1);
            imgRT.anchorMax = new Vector2(0, 1);
            imgRT.pivot = new Vector2(0, 1);
            imgRT.sizeDelta = new Vector2(180, 180);
            imgRT.anchoredPosition = new Vector2(10, -60);
            Image imgBg = imgGO.AddComponent<Image>();
            imgBg.color = new Color(0f, 0f, 0f, 0.25f);
            m_PkgMgrUGUIPreviewRaw = imgGO.AddComponent<RawImage>();
            m_PkgMgrUGUIPreviewRaw.texture = null;

            GameObject textGO = new GameObject("DetailText");
            textGO.transform.SetParent(details.transform, false);
            m_PkgMgrUGUIDetailText = textGO.AddComponent<Text>();
            m_PkgMgrUGUIDetailText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_PkgMgrUGUIDetailText.fontSize = 16;
            m_PkgMgrUGUIDetailText.color = Color.white;
            m_PkgMgrUGUIDetailText.alignment = TextAnchor.UpperLeft;
            m_PkgMgrUGUIDetailText.supportRichText = true;
            m_PkgMgrUGUIDetailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_PkgMgrUGUIDetailText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0, 0);
            textRT.anchorMax = new Vector2(1, 1);
            textRT.pivot = new Vector2(0, 0);
            textRT.offsetMin = new Vector2(10, 180);
            textRT.offsetMax = new Vector2(-10, -250);

            GameObject depsScrollRoot = new GameObject("DepsScroll");
            depsScrollRoot.transform.SetParent(details.transform, false);
            RectTransform depsRootRT = depsScrollRoot.AddComponent<RectTransform>();
            depsRootRT.anchorMin = new Vector2(0, 0);
            depsRootRT.anchorMax = new Vector2(1, 0);
            depsRootRT.pivot = new Vector2(0.5f, 0);
            depsRootRT.sizeDelta = new Vector2(-20, 160);
            depsRootRT.anchoredPosition = new Vector2(0, 10);

            GameObject depsViewport = new GameObject("Viewport");
            depsViewport.transform.SetParent(depsScrollRoot.transform, false);
            RectTransform depsVP_RT = depsViewport.AddComponent<RectTransform>();
            depsVP_RT.anchorMin = Vector2.zero;
            depsVP_RT.anchorMax = Vector2.one;
            depsVP_RT.pivot = new Vector2(0.5f, 0.5f);
            depsVP_RT.offsetMin = new Vector2(0, 0);
            depsVP_RT.offsetMax = new Vector2(-18, 0);
            depsViewport.AddComponent<RectMask2D>();

            GameObject depsContent = new GameObject("Content");
            depsContent.transform.SetParent(depsViewport.transform, false);
            RectTransform depsContentRT = depsContent.AddComponent<RectTransform>();
            depsContentRT.anchorMin = new Vector2(0, 1);
            depsContentRT.anchorMax = new Vector2(1, 1);
            depsContentRT.pivot = new Vector2(0.5f, 1);
            depsContentRT.anchoredPosition = Vector2.zero;
            depsContentRT.sizeDelta = new Vector2(0, 0);

            GameObject depsScrollbarGO = UI.CreateScrollBar(depsScrollRoot, 15f, 0, Scrollbar.Direction.BottomToTop);
            RectTransform depsSbRT = depsScrollbarGO.GetComponent<RectTransform>();
            if (depsSbRT != null)
            {
                depsSbRT.anchorMin = new Vector2(1, 0);
                depsSbRT.anchorMax = new Vector2(1, 1);
                depsSbRT.pivot = new Vector2(1, 0.5f);
                depsSbRT.sizeDelta = new Vector2(15f, 0);
            }

            ScrollRect depsSR = depsScrollRoot.AddComponent<ScrollRect>();
            depsSR.content = depsContentRT;
            depsSR.viewport = depsVP_RT;
            depsSR.horizontal = false;
            depsSR.vertical = true;
            depsSR.verticalScrollbar = depsScrollbarGO.GetComponent<Scrollbar>();
            depsSR.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            depsSR.movementType = ScrollRect.MovementType.Clamped;

            m_PkgMgrUGUIDepsText = CreatePkgMgrUGUIText(depsContent.transform, "DepsText", 14, TextAnchor.UpperLeft);
            m_PkgMgrUGUIDepsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_PkgMgrUGUIDepsText.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform depsTextRT = m_PkgMgrUGUIDepsText.GetComponent<RectTransform>();
            depsTextRT.anchorMin = new Vector2(0, 1);
            depsTextRT.anchorMax = new Vector2(1, 1);
            depsTextRT.pivot = new Vector2(0, 1);
            depsTextRT.anchoredPosition = Vector2.zero;
            depsTextRT.sizeDelta = new Vector2(0, 0);
        }

        private GameObject CreatePkgMgrUGUIVirtualList(GameObject parent, string name, out ScrollRect scrollRect, out RectTransform viewportRT, out RectTransform contentRT)
        {
            GameObject root = UI.AddChildGOImage(parent, new Color(0.08f, 0.08f, 0.08f, 0.6f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            root.name = name;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(root.transform, false);
            viewportRT = viewport.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.pivot = new Vector2(0.5f, 0.5f);
            viewportRT.offsetMin = new Vector2(0, 0);
            viewportRT.offsetMax = new Vector2(-18, 0);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);

            GameObject scrollbarGO = UI.CreateScrollBar(root, 15f, 0, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1, 0);
                sbRT.anchorMax = new Vector2(1, 1);
                sbRT.pivot = new Vector2(1, 0.5f);
                sbRT.sizeDelta = new Vector2(15f, 0);
            }

            scrollRect = root.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            return root;
        }

        private void EnsurePkgMgrUGUIRowPool(RectTransform contentRT, List<PkgMgrUGUIRow> pool, bool isLoaded)
        {
            if (contentRT == null) return;
            if (pool.Count > 0) return;

            for (int i = 0; i < PkgMgrUGUIPoolSize; i++)
            {
                pool.Add(CreatePkgMgrUGUIRow(contentRT, isLoaded));
            }
        }

        private PkgMgrUGUIRow CreatePkgMgrUGUIRow(RectTransform parent, bool isLoaded)
        {
            GameObject rowGO = new GameObject("Row");
            rowGO.transform.SetParent(parent, false);
            RectTransform rt = rowGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, PkgMgrUGUIRowHeight);

            Image bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

            Button btn = rowGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            Text typeText = CreatePkgMgrUGUIText(rowGO.transform, "Type", 16, TextAnchor.MiddleLeft);
            RectTransform typeRT = typeText.GetComponent<RectTransform>();
            typeRT.anchorMin = new Vector2(0, 0);
            typeRT.anchorMax = new Vector2(0, 1);
            typeRT.pivot = new Vector2(0, 0.5f);
            typeRT.sizeDelta = new Vector2(120, 0);
            typeRT.anchoredPosition = new Vector2(10, 0);

            Text depText = CreatePkgMgrUGUIText(rowGO.transform, "Deps", 16, TextAnchor.MiddleRight);
            RectTransform depRT = depText.GetComponent<RectTransform>();
            depRT.anchorMin = new Vector2(1, 0);
            depRT.anchorMax = new Vector2(1, 1);
            depRT.pivot = new Vector2(1, 0.5f);
            depRT.sizeDelta = new Vector2(90, 0);
            depRT.anchoredPosition = new Vector2(-170, 0);

            Text sizeText = CreatePkgMgrUGUIText(rowGO.transform, "Size", 16, TextAnchor.MiddleRight);
            RectTransform sizeRT = sizeText.GetComponent<RectTransform>();
            sizeRT.anchorMin = new Vector2(1, 0);
            sizeRT.anchorMax = new Vector2(1, 1);
            sizeRT.pivot = new Vector2(1, 0.5f);
            sizeRT.sizeDelta = new Vector2(100, 0);
            sizeRT.anchoredPosition = new Vector2(-60, 0);

            Text nameText = CreatePkgMgrUGUIText(rowGO.transform, "Name", 16, TextAnchor.MiddleLeft);
            RectTransform nameRT = nameText.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0, 0);
            nameRT.anchorMax = new Vector2(1, 1);
            nameRT.pivot = new Vector2(0, 0.5f);
            nameRT.offsetMin = new Vector2(135, 0);
            nameRT.offsetMax = new Vector2(-280, 0);

            PkgMgrUGUIRow row = new PkgMgrUGUIRow
            {
                Root = rowGO,
                RT = rt,
                Bg = bg,
                TypeText = typeText,
                NameText = nameText,
                DepText = depText,
                SizeText = sizeText,
                BoundVisibleIndex = -1,
            };

            btn.onClick.AddListener(() => {
                if (row.BoundVisibleIndex < 0) return;
                var visibleRows = isLoaded ? m_AddonVisibleRows : m_AllVisibleRows;
                var list = isLoaded ? m_AddonList : m_AllList;
                if (row.BoundVisibleIndex >= visibleRows.Count) return;
                int idx = visibleRows[row.BoundVisibleIndex].Index;
                if (idx < 0 || idx >= list.Count) return;
                EnsurePackageManagerSingleSelection(list[idx]);
                RefreshPkgMgrUGUIList(true, true);
            });

            return row;
        }

        private Text CreatePkgMgrUGUIText(Transform parent, string name, int fontSize, TextAnchor anchor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = anchor;
            t.supportRichText = true;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            return t;
        }

        private void UpdatePkgMgrUGUI()
        {
            if (!IsPackageManagerUGUIVisible()) return;
            bool needsRefresh = false;

            if (m_PkgMgrIndicesDirty)
            {
                RefreshVisibleIndices();
                needsRefresh = true;
            }

            if (m_PkgMgrUGUILastSelectedItem != m_PkgMgrSelectedItem)
            {
                m_PkgMgrUGUILastSelectedItem = m_PkgMgrSelectedItem;
                needsRefresh = true;
            }

            int addonVis = m_AddonVisibleRows != null ? m_AddonVisibleRows.Count : 0;
            int allVis = m_AllVisibleRows != null ? m_AllVisibleRows.Count : 0;
            if (addonVis != m_PkgMgrUGUILastVisibleAddonCount || allVis != m_PkgMgrUGUILastVisibleAllCount)
            {
                m_PkgMgrUGUILastVisibleAddonCount = addonVis;
                m_PkgMgrUGUILastVisibleAllCount = allVis;
                needsRefresh = true;
            }

            if (needsRefresh)
            {
                RefreshPkgMgrUGUIList(true, true);
            }

            RefreshPkgMgrUGUIDetails();
        }

        private void RefreshPkgMgrUGUIDetails()
        {
            if (!IsPackageManagerUGUIVisible()) return;

            if (m_PkgMgrUGUIPreviewRaw != null)
            {
                m_PkgMgrUGUIPreviewRaw.texture = m_PkgMgrSelectedThumbnail;
            }

            if (m_PkgMgrUGUIDetailText != null)
            {
                if (m_PkgMgrSelectedItem == null)
                {
                    m_PkgMgrUGUIDetailText.text = "Select a package to see details.";
                }
                else
                {
                    var it = m_PkgMgrSelectedItem;
                    string desc = m_PkgMgrSelectedDescription ?? "";
                    if (desc.Length > 400) desc = desc.Substring(0, 400) + "...";
                    m_PkgMgrUGUIDetailText.text = string.Format("<b>{0}</b>\nType: {1}   Size: {2}   Deps: {3} ({4})\n{5}",
                        it.Uid,
                        it.Type,
                        FormatSize(it.Size),
                        it.DependencyCount,
                        it.LoadedDependencyCount,
                        string.IsNullOrEmpty(desc) ? "" : desc);
                }
            }

            if (m_PkgMgrUGUIDepsText != null)
            {
                if (m_PkgMgrSelectedItem == null)
                {
                    m_PkgMgrUGUIDepsText.text = "";
                }
                else
                {
                    var it = m_PkgMgrSelectedItem;
                    var sb = new StringBuilder();
                    sb.Append("<b>Dependencies</b>\n");

                    int shown = 0;
                    if (it.UnloadedDependencies != null && it.UnloadedDependencies.Count > 0)
                    {
                        sb.Append("<color=yellow>Unloaded:</color>\n");
                        for (int i = 0; i < it.UnloadedDependencies.Count && shown < 60; i++)
                        {
                            sb.Append("- ").Append(it.UnloadedDependencies[i]).Append("\n");
                            shown++;
                        }
                    }

                    if (it.NotFoundDependencies != null && it.NotFoundDependencies.Count > 0 && shown < 60)
                    {
                        sb.Append("<color=red>Not Found:</color>\n");
                        for (int i = 0; i < it.NotFoundDependencies.Count && shown < 60; i++)
                        {
                            sb.Append("- ").Append(it.NotFoundDependencies[i]).Append("\n");
                            shown++;
                        }
                    }

                    if (it.AllDependencies != null && it.AllDependencies.Count > 0 && shown < 60)
                    {
                        sb.Append("<color=green>All:</color>\n");
                        foreach (var dep in it.AllDependencies)
                        {
                            if (shown >= 60) break;
                            if (string.IsNullOrEmpty(dep)) continue;
                            sb.Append("- ").Append(dep).Append("\n");
                            shown++;
                        }
                    }

                    if (shown >= 60) sb.Append("...\n");

                    m_PkgMgrUGUIDepsText.text = sb.ToString();

                    var depsRT = m_PkgMgrUGUIDepsText.GetComponent<RectTransform>();
                    if (depsRT != null)
                    {
                        float h = Mathf.Max(160f, 20f * (shown + 2));
                        depsRT.sizeDelta = new Vector2(0, h);
                    }
                }
            }

            bool canUndo = (m_PkgMgrUndoStack != null && m_PkgMgrUndoStack.Count > 0 && m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1] != null && m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1].Moves != null && m_PkgMgrUndoStack[m_PkgMgrUndoStack.Count - 1].Moves.Count > 0);
            if (m_PkgMgrUGUIUndoBtn != null) m_PkgMgrUGUIUndoBtn.interactable = canUndo && !IsPackageManagerBusy();
            if (m_PkgMgrUGUIUndoBtnText != null)
            {
                m_PkgMgrUGUIUndoBtnText.text = "Undo";
            }
        }

        private void RefreshPkgMgrUGUIList(bool refreshLoaded, bool refreshAll)
        {
            if (m_PkgMgrUGUIRoot == null || !m_PkgMgrUGUIRoot.activeSelf) return;
            if (!refreshLoaded && !refreshAll)
            {
                refreshLoaded = true;
                refreshAll = true;
            }

            if (m_PkgMgrIndicesDirty) RefreshVisibleIndices();

            if (refreshLoaded) RefreshPkgMgrUGUIListInternal(true);
            if (refreshAll) RefreshPkgMgrUGUIListInternal(false);
        }

        private void RefreshPkgMgrUGUIListInternal(bool isLoaded)
        {
            ScrollRect sr = isLoaded ? m_PkgMgrUGUILoadedScroll : m_PkgMgrUGUIAllScroll;
            RectTransform viewport = isLoaded ? m_PkgMgrUGUILoadedViewport : m_PkgMgrUGUIAllViewport;
            RectTransform content = isLoaded ? m_PkgMgrUGUILoadedContent : m_PkgMgrUGUIAllContent;
            List<PkgMgrUGUIRow> pool = isLoaded ? m_PkgMgrUGUILoadedPool : m_PkgMgrUGUIAllPool;
            List<PackageManagerVisibleRow> visibleRows = isLoaded ? m_AddonVisibleRows : m_AllVisibleRows;
            List<PackageManagerItem> list = isLoaded ? m_AddonList : m_AllList;

            if (sr == null || viewport == null || content == null) return;
            if (pool.Count == 0) return;

            int total = visibleRows != null ? visibleRows.Count : 0;
            float contentH = total * PkgMgrUGUIRowHeight;
            content.sizeDelta = new Vector2(0, contentH);

            float viewportH = viewport.rect.height;
            float maxScroll = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - sr.verticalNormalizedPosition) * maxScroll;
            int first = Mathf.FloorToInt(scrollY / PkgMgrUGUIRowHeight);
            if (first < 0) first = 0;
            if (first > Mathf.Max(0, total - 1)) first = Mathf.Max(0, total - 1);

            if (isLoaded) m_PkgMgrUGUILoadedFirst = first;
            else m_PkgMgrUGUIAllFirst = first;

            for (int i = 0; i < pool.Count; i++)
            {
                int visibleIdx = first + i;
                PkgMgrUGUIRow row = pool[i];
                if (visibleIdx >= total)
                {
                    if (row.Root != null) row.Root.SetActive(false);
                    row.BoundVisibleIndex = -1;
                    continue;
                }

                int listIdx = visibleRows[visibleIdx].Index;
                if (listIdx < 0 || listIdx >= list.Count)
                {
                    if (row.Root != null) row.Root.SetActive(false);
                    row.BoundVisibleIndex = -1;
                    continue;
                }

                PackageManagerItem item = list[listIdx];

                if (row.Root != null && !row.Root.activeSelf) row.Root.SetActive(true);
                row.BoundVisibleIndex = visibleIdx;

                if (row.RT != null)
                {
                    row.RT.anchoredPosition = new Vector2(0, -visibleIdx * PkgMgrUGUIRowHeight);
                }

                bool selected = (item == m_PkgMgrSelectedItem);
                if (row.Bg != null)
                {
                    if (selected) row.Bg.color = new Color(0.15f, 0.45f, 0.85f, 0.55f);
                    else row.Bg.color = (visibleIdx % 2 == 0) ? new Color(0.12f, 0.12f, 0.12f, 0.9f) : new Color(0.10f, 0.10f, 0.10f, 0.9f);
                }

                if (row.TypeText != null)
                {
                    row.TypeText.text = string.IsNullOrEmpty(item.HighlightedType) ? (item.Type ?? "") : item.HighlightedType;
                }

                if (row.NameText != null)
                {
                    string baseLabel = item.StatusPrefix + (string.IsNullOrEmpty(item.HighlightedUid) ? (item.Uid ?? "") : item.HighlightedUid);
                    if (item.AutoLoad) baseLabel = "<color=#add8e6>" + baseLabel + "</color>";
                    row.NameText.text = baseLabel;
                }

                if (row.DepText != null)
                {
                    row.DepText.text = string.Format("{0} ({1})", item.DependencyCount, item.LoadedDependencyCount);
                }

                if (row.SizeText != null)
                {
                    row.SizeText.text = FormatSize(item.Size);
                }
            }
        }

        private void OpenPackageManagerUGUI()
        {
            m_ShowPackageManagerWindow = false;
            ScanPackageManagerPackages();
            EnsurePkgMgrUGUI();
            if (m_PkgMgrUGUIFilterInput != null) m_PkgMgrUGUIFilterInput.text = m_PkgMgrFilter ?? "";
            m_PkgMgrUGUIRoot.SetActive(true);

            m_PkgMgrUGUILastSelectedItem = null;
            m_PkgMgrUGUILastVisibleAddonCount = -1;
            m_PkgMgrUGUILastVisibleAllCount = -1;
            RefreshPkgMgrUGUIList(true, true);
            RefreshPkgMgrUGUIDetails();
        }

        private void ClosePackageManagerUGUI()
        {
            if (m_PkgMgrUGUIRoot != null) m_PkgMgrUGUIRoot.SetActive(false);
        }
    }
}
