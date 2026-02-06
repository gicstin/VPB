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
        private ScrollRect m_PkgMgrUGUIUnifiedScroll;
        private RectTransform m_PkgMgrUGUIUnifiedContent;
        private RectTransform m_PkgMgrUGUIUnifiedViewport;
        private readonly List<PkgMgrUGUIRow> m_PkgMgrUGUIUnifiedPool = new List<PkgMgrUGUIRow>();
        private int m_PkgMgrUGUIUnifiedFirst = -1;
        private float m_PkgMgrUGUIRowHeight = 80f;
        private int m_PkgMgrPage = -1;
        private int m_PkgMgrItemsPerPage = -1;
        private const int PkgMgrUGUIPoolSize = 100;
        private Button m_PkgMgrUGUIUndoBtn;
        private Text m_PkgMgrUGUIUndoBtnText;
        private GameObject m_PkgMgrUGUILoadBtn;
        private Text m_PkgMgrUGUILoadBtnText;
        private GameObject m_PkgMgrUGUIUnloadBtn;
        private Text m_PkgMgrUGUIUnloadBtnText;
        private GameObject m_PkgMgrUGUILockBtn;
        private Text m_PkgMgrUGUILockBtnText;
        private GameObject m_PkgMgrUGUIAutoLoadBtn;
        private Text m_PkgMgrUGUIAutoLoadBtnText;
        private GameObject m_PkgMgrUGUIIsolateBtn;
        private Text m_PkgMgrUGUIIsolateBtnText;
        private string m_PkgMgrUGUILastStatusMsg = "";
        private PackageManagerItem m_PkgMgrUGUILastSelectedItem;
        private int m_PkgMgrUGUILastVisibleUnifiedCount = -1;
        private int m_PkgMgrUGUIShiftAnchorVisibleIndex = -1;
        private bool m_PkgMgrUGUIIsDragging = false;
        private bool m_PkgMgrUGUIDragChecked = false;
        private int m_PkgMgrUGUIDragLastVisibleIdx = -1;

        private class PkgMgrUGUIRow
        {
            public GameObject Root;
            public RectTransform RT;
            public Image Bg;
            public Image StatusLine;
            public RawImage Thumbnail;
            public Text TypeText;
            public Text NameText;
            public Text SizeText;
            public int BoundVisibleIndex = -1;
            public string LoadedThumbnailPath;
        }

        private class PackageManagerUGUIUpdater : MonoBehaviour
        {
            public VamHookPlugin plugin;

            void Update()
            {
                if (plugin != null) 
                {
                    plugin.UpdatePkgMgrUGUI();
                    plugin.HandlePkgMgrUGUIKeyboard();
                }
            }
        }

        private class PkgMgrUGUIRowPointerHandler : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
        {
            public VamHookPlugin plugin;
            public int rowPoolIndex;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (plugin != null) plugin.OnPkgMgrRowPointerDown(rowPoolIndex, eventData);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (plugin != null) plugin.OnPkgMgrRowPointerEnter(rowPoolIndex, eventData);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                if (plugin != null) plugin.OnPkgMgrRowPointerUp(rowPoolIndex, eventData);
            }
        }

        private void EnsurePkgMgrEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var esGo = new GameObject("VPB_EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        public void EmbedPackageManager(RectTransform parent)
        {
            EnsurePkgMgrEventSystem();

            if (m_PkgMgrUGUIRoot == null)
            {
                ScanPackageManagerPackages();
                m_PkgMgrIndicesDirty = true;

                m_PkgMgrUGUIRoot = new GameObject("PackageManagerContent");
                m_PkgMgrUGUIRoot.transform.SetParent(parent, false);
                RectTransform rt = m_PkgMgrUGUIRoot.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                m_PkgMgrUGUIPanel = m_PkgMgrUGUIRoot;

                CreatePkgMgrUGUILists();

                var updater = m_PkgMgrUGUIRoot.AddComponent<PackageManagerUGUIUpdater>();
                updater.plugin = this;
            }
        }

        public void SetPackageManagerVisible(bool visible)
        {
            if (m_PkgMgrUGUIRoot != null) m_PkgMgrUGUIRoot.SetActive(visible);
        }

        private void CreatePkgMgrUGUILists()
        {
            if (m_PkgMgrUGUIPanel == null) return;

            float actionBarH = 50f;
            float bottomMargin = 60f;
            float topMargin = 10f;
            float sideMargin = 20f;

            // Actions Bar
            GameObject actions = UI.AddChildGOImage(m_PkgMgrUGUIPanel, new Color(0.10f, 0.10f, 0.10f, 0.95f), AnchorPresets.hStretchMiddle, 0, actionBarH, Vector2.zero);
            actions.name = "ActionsBar";
            RectTransform actionsRT = actions.GetComponent<RectTransform>();
            actionsRT.anchorMin = new Vector2(0, 0);
            actionsRT.anchorMax = new Vector2(1, 0);
            actionsRT.pivot = new Vector2(0.5f, 0);
            actionsRT.offsetMin = new Vector2(sideMargin, bottomMargin);
            actionsRT.offsetMax = new Vector2(-sideMargin, bottomMargin + actionBarH);

            GameObject leftActions = new GameObject("LeftActions");
            leftActions.transform.SetParent(actions.transform, false);
            RectTransform leftActionsRT = leftActions.AddComponent<RectTransform>();
            leftActionsRT.anchorMin = Vector2.zero;
            leftActionsRT.anchorMax = new Vector2(1, 1);
            leftActionsRT.offsetMin = new Vector2(10, 0);
            leftActionsRT.offsetMax = new Vector2(-100, 0); // Leave space for Undo

            HorizontalLayoutGroup hlg = leftActions.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            float w = 110f;
            float h = 40f;

            m_PkgMgrUGUILoadBtn = UI.CreateUIButton(leftActions, w, h, "Load", 18, 0, 0, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformMove(m_UnifiedList, false);
            });
            var loadCsf = m_PkgMgrUGUILoadBtn.AddComponent<ContentSizeFitter>();
            loadCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var loadLe = m_PkgMgrUGUILoadBtn.AddComponent<LayoutElement>();
            loadLe.minWidth = w;
            m_PkgMgrUGUILoadBtnText = m_PkgMgrUGUILoadBtn.GetComponentInChildren<Text>();
            if (m_PkgMgrUGUILoadBtnText != null) m_PkgMgrUGUILoadBtnText.rectTransform.sizeDelta = new Vector2(20, 0); // Padding for CSF

            m_PkgMgrUGUIUnloadBtn = UI.CreateUIButton(leftActions, w, h, "Unload", 18, 0, 0, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformMove(m_UnifiedList, true);
            });
            var unloadCsf = m_PkgMgrUGUIUnloadBtn.AddComponent<ContentSizeFitter>();
            unloadCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var unloadLe = m_PkgMgrUGUIUnloadBtn.AddComponent<LayoutElement>();
            unloadLe.minWidth = w;
            m_PkgMgrUGUIUnloadBtnText = m_PkgMgrUGUIUnloadBtn.GetComponentInChildren<Text>();

            m_PkgMgrUGUILockBtn = UI.CreateUIButton(leftActions, w, h, "Lock", 18, 0, 0, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                ToggleLockSelection();
            });
            var lockCsf = m_PkgMgrUGUILockBtn.AddComponent<ContentSizeFitter>();
            lockCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var lockLe = m_PkgMgrUGUILockBtn.AddComponent<LayoutElement>();
            lockLe.minWidth = w;
            m_PkgMgrUGUILockBtnText = m_PkgMgrUGUILockBtn.GetComponentInChildren<Text>();

            m_PkgMgrUGUIAutoLoadBtn = UI.CreateUIButton(leftActions, 130f, h, "Auto-Load", 18, 0, 0, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                ToggleAutoLoadSelection();
            });
            var alCsf = m_PkgMgrUGUIAutoLoadBtn.AddComponent<ContentSizeFitter>();
            alCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var alLe = m_PkgMgrUGUIAutoLoadBtn.AddComponent<LayoutElement>();
            alLe.minWidth = 130f;
            m_PkgMgrUGUIAutoLoadBtnText = m_PkgMgrUGUIAutoLoadBtn.GetComponentInChildren<Text>();

            m_PkgMgrUGUIIsolateBtn = UI.CreateUIButton(leftActions, 110f, h, "Isolate", 18, 0, 0, AnchorPresets.middleLeft, () => {
                if (IsPackageManagerBusy()) return;
                PerformKeepSelectedUnloadRest();
            });
            var isoCsf = m_PkgMgrUGUIIsolateBtn.AddComponent<ContentSizeFitter>();
            isoCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var isoLe = m_PkgMgrUGUIIsolateBtn.AddComponent<LayoutElement>();
            isoLe.minWidth = 110f;
            m_PkgMgrUGUIIsolateBtnText = m_PkgMgrUGUIIsolateBtn.GetComponentInChildren<Text>();

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

            // Unified List
            GameObject unifiedPane = CreatePkgMgrUGUIVirtualList(m_PkgMgrUGUIPanel, "UnifiedList", out m_PkgMgrUGUIUnifiedScroll, out m_PkgMgrUGUIUnifiedViewport, out m_PkgMgrUGUIUnifiedContent);
            RectTransform unifiedRT = unifiedPane.GetComponent<RectTransform>();
            unifiedRT.anchorMin = new Vector2(0, 0);
            unifiedRT.anchorMax = new Vector2(1, 1);
            unifiedRT.pivot = new Vector2(0.5f, 1);
            unifiedRT.offsetMin = new Vector2(sideMargin, bottomMargin + actionBarH + 10f);
            unifiedRT.offsetMax = new Vector2(-sideMargin, -topMargin);

            if (m_PkgMgrUGUIUnifiedScroll != null) m_PkgMgrUGUIUnifiedScroll.onValueChanged.AddListener((v) => RefreshPkgMgrUGUIList());

            EnsurePkgMgrUGUIRowPool(m_PkgMgrUGUIUnifiedContent, m_PkgMgrUGUIUnifiedPool);
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

        private void EnsurePkgMgrUGUIRowPool(RectTransform contentRT, List<PkgMgrUGUIRow> pool)
        {
            if (contentRT == null) return;
            if (pool.Count > 0) return;

            for (int i = 0; i < PkgMgrUGUIPoolSize; i++)
            {
                var row = CreatePkgMgrUGUIRow(contentRT);
                var handler = row.Root.AddComponent<PkgMgrUGUIRowPointerHandler>();
                handler.plugin = this;
                handler.rowPoolIndex = i;
                pool.Add(row);
            }
        }

        private PkgMgrUGUIRow CreatePkgMgrUGUIRow(RectTransform parent)
        {
            GameObject rowGO = new GameObject("Row");
            rowGO.transform.SetParent(parent, false);
            RectTransform rt = rowGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, m_PkgMgrUGUIRowHeight);

            Image bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

            // Status Line
            GameObject statusGO = new GameObject("StatusLine");
            statusGO.transform.SetParent(rowGO.transform, false);
            RectTransform statusRT = statusGO.AddComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0, 0);
            statusRT.anchorMax = new Vector2(0, 1);
            statusRT.pivot = new Vector2(0, 0.5f);
            statusRT.sizeDelta = new Vector2(6, 0);
            statusRT.anchoredPosition = new Vector2(0, 0);
            Image statusImg = statusGO.AddComponent<Image>();
            statusImg.color = Color.gray;

            // Thumbnail
            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(rowGO.transform, false);
            RectTransform thumbRT = thumbGO.AddComponent<RectTransform>();
            thumbRT.anchorMin = new Vector2(0, 0.5f);
            thumbRT.anchorMax = new Vector2(0, 0.5f);
            thumbRT.pivot = new Vector2(0, 0.5f);
            thumbRT.sizeDelta = new Vector2(76, 76);
            thumbRT.anchoredPosition = new Vector2(10, 0);
            RawImage thumbImg = thumbGO.AddComponent<RawImage>();
            thumbImg.color = Color.white;

            // Name Text
            Text nameText = CreatePkgMgrUGUIText(rowGO.transform, "Name", 18, TextAnchor.UpperLeft);
            nameText.fontStyle = FontStyle.Bold;
            RectTransform nameRT = nameText.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0, 1);
            nameRT.anchorMax = new Vector2(1, 1);
            nameRT.pivot = new Vector2(0, 1);
            nameRT.offsetMin = new Vector2(96, 0);
            nameRT.offsetMax = new Vector2(-10, -5);
            nameRT.sizeDelta = new Vector2(0, 30);

            // Type Text
            Text typeText = CreatePkgMgrUGUIText(rowGO.transform, "Type", 14, TextAnchor.LowerLeft);
            typeText.color = new Color(0.8f, 0.8f, 0.8f);
            RectTransform typeRT = typeText.GetComponent<RectTransform>();
            typeRT.anchorMin = new Vector2(0, 0);
            typeRT.anchorMax = new Vector2(0.6f, 0);
            typeRT.pivot = new Vector2(0, 0);
            typeRT.offsetMin = new Vector2(96, 5);
            typeRT.offsetMax = new Vector2(0, 25);

            // Size Text
            Text sizeText = CreatePkgMgrUGUIText(rowGO.transform, "Size", 14, TextAnchor.LowerRight);
            sizeText.color = new Color(0.7f, 0.7f, 0.7f);
            RectTransform sizeRT = sizeText.GetComponent<RectTransform>();
            sizeRT.anchorMin = new Vector2(0.6f, 0);
            sizeRT.anchorMax = new Vector2(1, 0);
            sizeRT.pivot = new Vector2(1, 0);
            sizeRT.offsetMin = new Vector2(0, 5);
            sizeRT.offsetMax = new Vector2(-10, 25);

            PkgMgrUGUIRow row = new PkgMgrUGUIRow
            {
                Root = rowGO,
                RT = rt,
                Bg = bg,
                StatusLine = statusImg,
                Thumbnail = thumbImg,
                TypeText = typeText,
                NameText = nameText,
                SizeText = sizeText,
                BoundVisibleIndex = -1,
            };

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

        private int m_PkgMgrUGUILastSelectedCount = -1;

        private void UpdatePkgMgrUGUI()
        {
            if (m_PkgMgrUGUIRoot == null || !m_PkgMgrUGUIRoot.activeSelf) return;
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

            int unifiedVis = m_UnifiedVisibleRows != null ? m_UnifiedVisibleRows.Count : 0;
            if (unifiedVis != m_PkgMgrUGUILastVisibleUnifiedCount)
            {
                m_PkgMgrUGUILastVisibleUnifiedCount = unifiedVis;
                needsRefresh = true;
            }

            int currentSelectedCount = 0;
            var list = m_UnifiedList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Checked) currentSelectedCount++;
            }

            if (currentSelectedCount != m_PkgMgrUGUILastSelectedCount)
            {
                m_PkgMgrUGUILastSelectedCount = currentSelectedCount;
                needsRefresh = true;
            }

            if (needsRefresh)
            {
                RefreshPkgMgrUGUIList();
                if (OnPkgMgrListChanged != null) OnPkgMgrListChanged();
            }

            string currentStatus = "";
            if (IsPackageManagerBusy())
            {
                currentStatus = (string.IsNullOrEmpty(m_PkgMgrStatusMessage) ? "Working..." : m_PkgMgrStatusMessage);
            }
            else if (m_PkgMgrStatusTimer > Time.realtimeSinceStartup)
            {
                currentStatus = m_PkgMgrStatusMessage;
            }
            
            if (m_PkgMgrUGUILastStatusMsg != currentStatus)
            {
                m_PkgMgrUGUILastStatusMsg = currentStatus;
                if (OnPkgMgrStatusChanged != null) OnPkgMgrStatusChanged(currentStatus);
            }
        }

        private void RefreshPkgMgrUGUIList()
        {
            if (m_PkgMgrUGUIRoot == null || !m_PkgMgrUGUIRoot.activeSelf) return;
            if (m_PkgMgrIndicesDirty) RefreshVisibleIndices();
            RefreshPkgMgrUGUIListInternal();
        }

        public void SetPkgMgrZoom(float rowHeight)
        {
            m_PkgMgrUGUIRowHeight = Mathf.Clamp(rowHeight, 40f, 300f);
            RefreshPkgMgrUGUIList();
        }

        public void SetPkgMgrPage(int page, int itemsPerPage)
        {
            m_PkgMgrPage = page;
            m_PkgMgrItemsPerPage = itemsPerPage;
            RefreshPkgMgrUGUIList();
        }

        public int GetPkgMgrTotalVisibleCount()
        {
            if (m_PkgMgrIndicesDirty) RefreshVisibleIndices();
            return m_UnifiedVisibleRows != null ? m_UnifiedVisibleRows.Count : 0;
        }

        private void UpdatePackageManagerActions()
        {
            if (m_PkgMgrUGUILoadBtn == null) return;

            int loadedSelected = 0;
            int unloadedSelected = 0;
            int totalSelected = 0;

            var list = m_UnifiedList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item.Checked)
                    {
                        totalSelected++;
                        if (item.IsInAddonList) loadedSelected++;
                        else unloadedSelected++;
                    }
                }
            }

            m_PkgMgrUGUILoadBtn.SetActive(unloadedSelected > 0);
            if (m_PkgMgrUGUILoadBtnText != null) m_PkgMgrUGUILoadBtnText.text = unloadedSelected > 1 ? string.Format("Load ({0})", unloadedSelected) : "Load";

            m_PkgMgrUGUIUnloadBtn.SetActive(loadedSelected > 0);
            if (m_PkgMgrUGUIUnloadBtnText != null) m_PkgMgrUGUIUnloadBtnText.text = loadedSelected > 1 ? string.Format("Unload ({0})", loadedSelected) : "Unload";

            m_PkgMgrUGUILockBtn.SetActive(totalSelected > 0);
            if (m_PkgMgrUGUILockBtnText != null) m_PkgMgrUGUILockBtnText.text = totalSelected > 1 ? string.Format("Lock ({0})", totalSelected) : "Lock";

            m_PkgMgrUGUIAutoLoadBtn.SetActive(totalSelected > 0);
            if (m_PkgMgrUGUIAutoLoadBtnText != null) m_PkgMgrUGUIAutoLoadBtnText.text = totalSelected > 1 ? string.Format("Auto-Load ({0})", totalSelected) : "Auto-Load";

            m_PkgMgrUGUIIsolateBtn.SetActive(loadedSelected > 0);
            if (m_PkgMgrUGUIIsolateBtnText != null) m_PkgMgrUGUIIsolateBtnText.text = loadedSelected > 1 ? string.Format("Isolate ({0})", loadedSelected) : "Isolate";
        }

        private void RefreshPkgMgrUGUIListInternal()
        {
            if (GalleryPanel.BenchmarkStartTime > 0)
            {
                float now = Time.realtimeSinceStartup;
                UnityEngine.Debug.Log("[Benchmark] RefreshPkgMgrUGUIListInternal called at " + now + " (+" + (now - GalleryPanel.BenchmarkStartTime).ToString("F3") + "s)");
            }

            ScrollRect sr = m_PkgMgrUGUIUnifiedScroll;
            RectTransform viewport = m_PkgMgrUGUIUnifiedViewport;
            RectTransform content = m_PkgMgrUGUIUnifiedContent;
            List<PkgMgrUGUIRow> pool = m_PkgMgrUGUIUnifiedPool;
            List<PackageManagerVisibleRow> visibleRows = m_UnifiedVisibleRows;
            List<PackageManagerItem> list = m_UnifiedList;

            if (sr == null || viewport == null || content == null) return;
            if (pool.Count == 0) return;

            UpdatePackageManagerActions();

            int total = visibleRows != null ? visibleRows.Count : 0;
            int first = 0;

            // Check pagination
            if (m_PkgMgrPage >= 0 && m_PkgMgrItemsPerPage > 0)
            {
                // Pagination Mode
                first = m_PkgMgrPage * m_PkgMgrItemsPerPage;
                // Clamp
                if (first < 0) first = 0;
                // Content height fits the page items (or less if last page)
                int itemsOnPage = Mathf.Min(m_PkgMgrItemsPerPage, total - first);
                if (itemsOnPage < 0) itemsOnPage = 0;
                float contentH = itemsOnPage * m_PkgMgrUGUIRowHeight;
                content.sizeDelta = new Vector2(0, contentH);
                
                // We force position to top for page mode usually, but maybe user wants to scroll WITHIN the page if it doesn't fit?
                // For now, let's assume page fits or standard scroll works. 
                // Actually if contentH < viewportH, scrollbar is hidden.
            }
            else
            {
                // Continuous Scroll Mode
                float contentH = total * m_PkgMgrUGUIRowHeight;
                content.sizeDelta = new Vector2(0, contentH);

                float viewportH = viewport.rect.height;
                if (viewportH <= 0) viewportH = 700f; // Fallback if not laid out yet

                float maxScroll = Mathf.Max(0f, contentH - viewportH);
                float scrollY = (1f - sr.verticalNormalizedPosition) * maxScroll;
                first = Mathf.FloorToInt(scrollY / m_PkgMgrUGUIRowHeight);
            }
            
            if (first < 0) first = 0;
            if (first > Mathf.Max(0, total - 1)) first = Mathf.Max(0, total - 1);

            m_PkgMgrUGUIUnifiedFirst = first;
            float thumbSize = m_PkgMgrUGUIRowHeight - 10f;

            for (int i = 0; i < pool.Count; i++)
            {
                int visibleIdx = first + i;
                PkgMgrUGUIRow row = pool[i];
                
                // If paginated, stop after page end
                if (m_PkgMgrPage >= 0 && m_PkgMgrItemsPerPage > 0 && i >= m_PkgMgrItemsPerPage)
                {
                    if (row.Root != null) row.Root.SetActive(false);
                    row.BoundVisibleIndex = -1;
                    continue;
                }

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
                    // For pagination, we want relative to top of content.
                    // If using page mode, content is small.
                    // If continuous, content is huge.
                    // Logic is same: -visibleIdx * RowHeight? 
                    // No. If paginated, visibleIdx is large (e.g. 1000). 
                    // We want it relative to the PAGE start.
                    
                    float yPos = 0;
                    if (m_PkgMgrPage >= 0 && m_PkgMgrItemsPerPage > 0)
                        yPos = -(i * m_PkgMgrUGUIRowHeight);
                    else
                        yPos = -(visibleIdx * m_PkgMgrUGUIRowHeight);
                        
                    row.RT.anchoredPosition = new Vector2(0, yPos);
                    row.RT.sizeDelta = new Vector2(0, m_PkgMgrUGUIRowHeight);
                }

                bool selected = item.Checked || (item == m_PkgMgrSelectedItem);
                if (row.Bg != null)
                {
                    if (item == m_PkgMgrSelectedItem) row.Bg.color = new Color(0.2f, 0.5f, 0.9f, 0.6f); // Active/Primary
                    else if (item.Checked) row.Bg.color = new Color(0.15f, 0.35f, 0.7f, 0.45f); // Selected
                    else row.Bg.color = (visibleIdx % 2 == 0) ? new Color(0.12f, 0.12f, 0.12f, 0.9f) : new Color(0.15f, 0.15f, 0.15f, 0.9f);
                }

                if (row.StatusLine != null)
                {
                    row.StatusLine.color = item.IsInAddonList ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.2f, 0.6f, 1.0f);
                }

                if (row.NameText != null)
                {
                    string baseLabel = item.StatusPrefix + (string.IsNullOrEmpty(item.HighlightedUid) ? (item.Uid ?? "") : item.HighlightedUid);
                    if (item.AutoLoad) baseLabel = "<color=#add8e6>" + baseLabel + "</color>";
                    row.NameText.text = baseLabel;
                    
                    float nameFontSize = Mathf.Lerp(24, 40, (m_PkgMgrUGUIRowHeight - 40f) / 160f);
                    row.NameText.fontSize = Mathf.RoundToInt(nameFontSize);
                    
                    RectTransform nrt = row.NameText.rectTransform;
                    if (m_PkgMgrUGUIRowHeight < 60)
                    {
                        nrt.offsetMin = new Vector2(thumbSize + 20, 0);
                        nrt.offsetMax = new Vector2(-10, 0);
                        nrt.sizeDelta = new Vector2(0, m_PkgMgrUGUIRowHeight);
                        row.NameText.alignment = TextAnchor.MiddleLeft;
                    }
                    else
                    {
                        nrt.offsetMin = new Vector2(thumbSize + 20, 0);
                        nrt.offsetMax = new Vector2(-10, -5);
                        nrt.sizeDelta = new Vector2(0, m_PkgMgrUGUIRowHeight * 0.5f);
                        row.NameText.alignment = TextAnchor.UpperLeft;
                    }
                }

                if (row.TypeText != null)
                {
                    if (m_PkgMgrUGUIRowHeight < 60)
                    {
                        row.TypeText.gameObject.SetActive(false);
                    }
                    else
                    {
                        row.TypeText.gameObject.SetActive(true);
                        row.TypeText.text = string.IsNullOrEmpty(item.HighlightedType) ? (item.Type ?? "") : item.HighlightedType;
                        float typeFontSize = Mathf.Lerp(16, 24, (m_PkgMgrUGUIRowHeight - 60f) / 140f);
                        row.TypeText.fontSize = Mathf.RoundToInt(typeFontSize);
                        
                        RectTransform trt = row.TypeText.rectTransform;
                        trt.offsetMin = new Vector2(thumbSize + 20, 5);
                        trt.offsetMax = new Vector2(0, m_PkgMgrUGUIRowHeight * 0.4f + 5);
                    }
                }

                if (row.SizeText != null)
                {
                    if (m_PkgMgrUGUIRowHeight < 60)
                    {
                        row.SizeText.gameObject.SetActive(false);
                    }
                    else
                    {
                        row.SizeText.gameObject.SetActive(true);
                        row.SizeText.text = string.Format("{0}   Deps: {1}", FormatSize(item.Size), item.DependencyCount < 0 ? "?" : item.DependencyCount.ToString());
                        float sizeFontSize = Mathf.Lerp(16, 24, (m_PkgMgrUGUIRowHeight - 60f) / 140f);
                        row.SizeText.fontSize = Mathf.RoundToInt(sizeFontSize);
                        
                        RectTransform srt = row.SizeText.rectTransform;
                        srt.offsetMin = new Vector2(0, 5);
                        srt.offsetMax = new Vector2(-10, m_PkgMgrUGUIRowHeight * 0.4f + 5);
                    }
                }

                if (row.Thumbnail != null)
                {
                    row.Thumbnail.rectTransform.sizeDelta = new Vector2(thumbSize, thumbSize);
                    
                    string thumbPath = GetItemThumbnailPath(item);
                    if (row.LoadedThumbnailPath != thumbPath)
                    {
                        row.LoadedThumbnailPath = thumbPath;
                        row.Thumbnail.texture = null;
                        row.Thumbnail.color = new Color(1, 1, 1, 0.1f);

                        if (!string.IsNullOrEmpty(thumbPath))
                        {
                            if (CustomImageLoaderThreaded.singleton != null)
                            {
                                Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(thumbPath);
                                if (tex != null)
                                {
                                    row.Thumbnail.texture = tex;
                                    row.Thumbnail.color = Color.white;
                                }
                                else
                                {
                                    CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
                                    qi.imgPath = thumbPath;
                                    qi.isThumbnail = true;
                                    qi.priority = 1;
                                    qi.callback = (res) => {
                                        if (res != null && res.tex != null && row.LoadedThumbnailPath == res.imgPath)
                                        {
                                            if (row.Thumbnail != null)
                                            {
                                                row.Thumbnail.texture = res.tex;
                                                row.Thumbnail.color = Color.white;
                                            }
                                        }
                                    };
                                    CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
                                }
                            }
                        }
                    }
                    else if (row.Thumbnail.texture == null && !string.IsNullOrEmpty(thumbPath))
                    {
                        if (CustomImageLoaderThreaded.singleton != null)
                        {
                            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(thumbPath);
                            if (tex != null)
                            {
                                row.Thumbnail.texture = tex;
                                row.Thumbnail.color = Color.white;
                            }
                        }
                    }
                }
            }
        }

        public void OnPkgMgrRowPointerDown(int rowPoolIndex, PointerEventData eventData)
        {
            var pool = m_PkgMgrUGUIUnifiedPool;
            if (rowPoolIndex < 0 || rowPoolIndex >= pool.Count) return;
            var row = pool[rowPoolIndex];
            int visibleIdx = row.BoundVisibleIndex;
            if (visibleIdx < 0) return;

            var visibleRows = m_UnifiedVisibleRows;
            var list = m_UnifiedList;
            if (visibleIdx >= visibleRows.Count) return;
            int listIdx = visibleRows[visibleIdx].Index;
            if (listIdx < 0 || listIdx >= list.Count) return;

            var item = list[listIdx];

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (shift && m_PkgMgrUGUIShiftAnchorVisibleIndex != -1)
            {
                PkgMgrUGUISelectRange(m_PkgMgrUGUIShiftAnchorVisibleIndex, visibleIdx, true, true);
            }
            else if (ctrl)
            {
                item.Checked = !item.Checked;
                m_PkgMgrUGUIShiftAnchorVisibleIndex = visibleIdx;
            }
            else
            {
                // Single select
                foreach (var it in list) it.Checked = false;
                item.Checked = true;
                m_PkgMgrUGUIShiftAnchorVisibleIndex = visibleIdx;
            }

            m_PkgMgrSelectedItem = item;
            
            // Start drag
            m_PkgMgrUGUIIsDragging = true;
            m_PkgMgrUGUIDragChecked = item.Checked;
            m_PkgMgrUGUIDragLastVisibleIdx = visibleIdx;

            RefreshPkgMgrUGUIList();
        }

        public void OnPkgMgrRowPointerEnter(int rowPoolIndex, PointerEventData eventData)
        {
            if (!m_PkgMgrUGUIIsDragging) return;

            var pool = m_PkgMgrUGUIUnifiedPool;
            if (rowPoolIndex < 0 || rowPoolIndex >= pool.Count) return;
            var row = pool[rowPoolIndex];
            int visibleIdx = row.BoundVisibleIndex;
            if (visibleIdx < 0) return;

            if (visibleIdx != m_PkgMgrUGUIDragLastVisibleIdx)
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                PkgMgrUGUISelectRange(m_PkgMgrUGUIDragLastVisibleIdx, visibleIdx, m_PkgMgrUGUIDragChecked, shift);
                m_PkgMgrUGUIDragLastVisibleIdx = visibleIdx;
                RefreshPkgMgrUGUIList();
            }
        }

        public void OnPkgMgrRowPointerUp(int rowPoolIndex, PointerEventData eventData)
        {
            m_PkgMgrUGUIIsDragging = false;
        }

        public void HandlePkgMgrUGUIKeyboard()
        {
            var visibleRows = m_UnifiedVisibleRows;

            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.A))
            {
                if (visibleRows != null)
                {
                    foreach (var vr in visibleRows)
                    {
                        if (vr.Index >= 0 && vr.Index < m_UnifiedList.Count)
                            m_UnifiedList[vr.Index].Checked = true;
                    }
                    RefreshPkgMgrUGUIList();
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (m_PkgMgrSelectedItem != null)
                {
                    m_PkgMgrSelectedItem.Checked = !m_PkgMgrSelectedItem.Checked;
                    RefreshPkgMgrUGUIList();
                }
                return;
            }

            bool up = Input.GetKeyDown(KeyCode.UpArrow);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            if (!up && !down) return;

            if (visibleRows == null || visibleRows.Count == 0) return;

            // Find current visible index
            int currentVisIdx = -1;
            if (m_PkgMgrSelectedItem != null)
            {
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    if (m_UnifiedList[visibleRows[i].Index] == m_PkgMgrSelectedItem)
                    {
                        currentVisIdx = i;
                        break;
                    }
                }
            }

            int nextVisIdx = currentVisIdx;
            if (up) nextVisIdx--;
            else if (down) nextVisIdx++;

            if (nextVisIdx < 0) nextVisIdx = 0;
            if (nextVisIdx >= visibleRows.Count) nextVisIdx = visibleRows.Count - 1;

            if (nextVisIdx != currentVisIdx || currentVisIdx == -1)
            {
                UpdatePkgMgrUGUISelection(nextVisIdx, currentVisIdx);
            }
        }

        private void UpdatePkgMgrUGUISelection(int visibleIdx, int currentVisIdx)
        {
            var visibleRows = m_UnifiedVisibleRows;
            var list = m_UnifiedList;
            if (visibleIdx < 0 || visibleIdx >= visibleRows.Count) return;

            var item = list[visibleRows[visibleIdx].Index];
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (shift)
            {
                if (m_PkgMgrUGUIShiftAnchorVisibleIndex == -1) 
                    m_PkgMgrUGUIShiftAnchorVisibleIndex = currentVisIdx != -1 ? currentVisIdx : 0;
                PkgMgrUGUISelectRange(m_PkgMgrUGUIShiftAnchorVisibleIndex, visibleIdx, true, true);
            }
            else if (ctrl)
            {
                // Keyboard nav with Ctrl usually just moves the focus
                m_PkgMgrUGUIShiftAnchorVisibleIndex = visibleIdx;
            }
            else
            {
                foreach (var it in list) it.Checked = false;
                item.Checked = true;
                m_PkgMgrUGUIShiftAnchorVisibleIndex = visibleIdx;
            }

            m_PkgMgrSelectedItem = item;
            ScrollToPkgMgrUGUISelection(visibleIdx);
            RefreshPkgMgrUGUIList();
        }

        private void PkgMgrUGUISelectRange(int startVisibleIdx, int endVisibleIdx, bool state, bool clearOthers = false)
        {
            var visibleRows = m_UnifiedVisibleRows;
            var list = m_UnifiedList;
            if (visibleRows == null || list == null) return;

            if (clearOthers)
            {
                foreach (var it in list) it.Checked = false;
            }

            int min = Mathf.Min(startVisibleIdx, endVisibleIdx);
            int max = Mathf.Max(startVisibleIdx, endVisibleIdx);
            
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < visibleRows.Count)
                {
                    list[visibleRows[i].Index].Checked = state;
                }
            }
        }

        private void ScrollToPkgMgrUGUISelection(int visibleIdx)
        {
            if (m_PkgMgrUGUIUnifiedScroll == null || m_PkgMgrUGUIUnifiedViewport == null) return;
            
            float viewportHeight = m_PkgMgrUGUIUnifiedViewport.rect.height;
            float contentHeight = m_PkgMgrUGUIUnifiedContent.sizeDelta.y;
            if (contentHeight <= viewportHeight) return;

            float itemTop = visibleIdx * m_PkgMgrUGUIRowHeight;
            float itemBottom = itemTop + m_PkgMgrUGUIRowHeight;

            float currentScrollY = (1f - m_PkgMgrUGUIUnifiedScroll.verticalNormalizedPosition) * (contentHeight - viewportHeight);
            float currentViewBottom = currentScrollY + viewportHeight;

            if (itemTop < currentScrollY)
            {
                float newScrollY = itemTop;
                m_PkgMgrUGUIUnifiedScroll.verticalNormalizedPosition = 1f - (newScrollY / (contentHeight - viewportHeight));
            }
            else if (itemBottom > currentViewBottom)
            {
                float newScrollY = itemBottom - viewportHeight;
                m_PkgMgrUGUIUnifiedScroll.verticalNormalizedPosition = 1f - (newScrollY / (contentHeight - viewportHeight));
            }
        }

    }
}
