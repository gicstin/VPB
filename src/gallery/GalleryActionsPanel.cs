using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using SimpleJSON;
using MVR.FileManagementSecure;

namespace VPB
{
    public class GalleryActionsPanel
    {
        public GameObject actionsPaneGO;
        private GameObject backgroundBoxGO; // The gallery's background box
        private RectTransform actionsPaneRT;
        private GameObject contentGO;
        private List<FileEntry> selectedFiles = new List<FileEntry>();
        private FileEntry SelectedFile => (selectedFiles != null && selectedFiles.Count > 0) ? selectedFiles[0] : null;
        private Hub.GalleryHubItem selectedHubItem;
        private GalleryPanel parentPanel;
        private bool isOpen = false;

        private Dictionary<ActionUITabType, GalleryActionTabBase> tabs = new Dictionary<ActionUITabType, GalleryActionTabBase>();
        private ActionUITabType currentTabType = ActionUITabType.Primary;
        private GameObject tabsContainerGO;
        private List<GameObject> tabButtons = new List<GameObject>();

        public GalleryPanel ParentPanel => parentPanel;

        public GalleryActionsPanel(GalleryPanel parent, GameObject parentGO, GameObject galleryBackgroundBox)
        {
            this.parentPanel = parent;
            this.backgroundBoxGO = galleryBackgroundBox;
            CreatePane(parentGO);
            InitializeTabs();
        }

        private void InitializeTabs()
        {
            tabs[ActionUITabType.Primary] = new GalleryPrimaryActionTab(this, contentGO);
            tabs[ActionUITabType.Tags] = new GalleryTagsActionTab(this, contentGO);
            tabs[ActionUITabType.Info] = new GalleryInfoActionTab(this, contentGO);
            tabs[ActionUITabType.Dependencies] = new GalleryDependenciesActionTab(this, contentGO);
            tabs[ActionUITabType.Audio] = new GalleryAudioActionTab(this, contentGO);
            tabs[ActionUITabType.Position] = new GalleryPositionActionTab(this, contentGO);
        }

        public void UpdateInput()
        {
            if (!isOpen || !actionsPaneGO.activeInHierarchy) return;

            // Check if Alt is held down
            if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;

            // Check Alpha keys 1-9
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    if (tabs.ContainsKey(currentTabType))
                    {
                        tabs[currentTabType].ExecuteShortcut(i);
                    }
                }
            }
        }

        private void CreatePane(GameObject parentGO)
        {
            // Create a hidden container for logic/state
            actionsPaneGO = new GameObject("ActionsPane_Visible"); // Renamed for clarity
            actionsPaneGO.transform.SetParent(parentGO.transform, false);
            actionsPaneGO.SetActive(false);
            
            actionsPaneRT = actionsPaneGO.AddComponent<RectTransform>();
            actionsPaneRT.anchorMin = Vector2.zero;
            actionsPaneRT.anchorMax = Vector2.one;
            actionsPaneRT.offsetMin = Vector2.zero;
            actionsPaneRT.offsetMax = Vector2.zero;

            // Add background to block view of gallery behind it
            Image bg = actionsPaneGO.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f); // Very dark, nearly opaque

            // Tabs container at the top
            tabsContainerGO = new GameObject("TabsContainer");
            tabsContainerGO.transform.SetParent(actionsPaneGO.transform, false);
            RectTransform tabsRT = tabsContainerGO.AddComponent<RectTransform>();
            tabsRT.anchorMin = new Vector2(0, 1);
            tabsRT.anchorMax = new Vector2(1, 1);
            tabsRT.pivot = new Vector2(0.5f, 1);
            tabsRT.anchoredPosition = new Vector2(0, 0);
            tabsRT.sizeDelta = new Vector2(0, 40);

            HorizontalLayoutGroup hlg = tabsContainerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.spacing = 2;

            // Content container below tabs
            contentGO = new GameObject("ActionsContent_Hidden");
            contentGO.transform.SetParent(actionsPaneGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 0);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = new Vector2(0, 0);
            contentRT.offsetMax = new Vector2(0, -42); // Leave room for tabs

            VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 5;

            // Add Input Handler
            var inputHandler = actionsPaneGO.AddComponent<GalleryActionsInputHandler>();
            inputHandler.panel = this;

            CreateTabButtons();
        }

        private void CreateTabButtons()
        {
            foreach (ActionUITabType tabType in Enum.GetValues(typeof(ActionUITabType)))
            {
                ActionUITabType t = tabType;
                string label = tabType.ToString();
                GameObject btn = UI.CreateUIButton(tabsContainerGO, 0, 40, label, 16, 0, 0, AnchorPresets.middleCenter, () => SwitchTab(t));
                tabButtons.Add(btn);
            }
        }

        public void SwitchTab(ActionUITabType tabType)
        {
            if (currentTabType == tabType)
            {
                 // Still refresh even if same tab, as it might be a new selection
            }
            else
            {
                if (tabs.ContainsKey(currentTabType)) tabs[currentTabType].OnClose();
                currentTabType = tabType;
                if (tabs.ContainsKey(currentTabType)) tabs[currentTabType].OnOpen();
            }
            
            UpdateUI();
        }

        public void HandleSelectionChanged(List<FileEntry> files, Hub.GalleryHubItem hubItem)
        {
            selectedFiles.Clear();
            if (files != null) selectedFiles.AddRange(files);
            selectedHubItem = hubItem;

            UpdateUI();
        }

        public void Open()
        {
            isOpen = true;
            if (actionsPaneGO != null) actionsPaneGO.transform.SetAsLastSibling();
            if (tabs.ContainsKey(currentTabType)) tabs[currentTabType].OnOpen();
            UpdateUI();
        }

        public void Close()
        {
            isOpen = false;
            if (tabs.ContainsKey(currentTabType)) tabs[currentTabType].OnClose();
        }

        public void UpdateUI()
        {
            if (!isOpen) return;

            // If fixed and reduced height, move pane to bottom
            if (parentPanel.isFixedLocally && VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedHeightMode > 0)
            {
                float bottomAnchor = 0;
                if (VPBConfig.Instance.DesktopFixedHeightMode == 1) bottomAnchor = 1f / 3f;
                else if (VPBConfig.Instance.DesktopFixedHeightMode == 2) bottomAnchor = 0.5f;

                // Match the horizontal ratio used in GalleryPanel.Lifecycle.cs (Golden Ratio)
                float leftRatio = 1.618f / 2.618f;

                actionsPaneRT.anchorMin = new Vector2(leftRatio, 0);
                actionsPaneRT.anchorMax = new Vector2(1, bottomAnchor);
                actionsPaneRT.offsetMin = Vector2.zero;
                actionsPaneRT.offsetMax = Vector2.zero;
            }
            else
            {
                actionsPaneRT.anchorMin = Vector2.zero;
                actionsPaneRT.anchorMax = Vector2.one;
                actionsPaneRT.offsetMin = Vector2.zero;
                actionsPaneRT.offsetMax = Vector2.zero;
            }

            if (tabs.ContainsKey(currentTabType))
            {
                tabs[currentTabType].RefreshUI(selectedFiles, selectedHubItem);
            }
        }


        public Atom GetBestTargetAtom()
        {
            if (SuperController.singleton == null) return null;
            
            // 0. Prefer the target selected in the GalleryPanel dropdown
            if (parentPanel != null)
            {
                Atom selectedInDropdown = parentPanel.SelectedTargetAtom;
                if (selectedInDropdown != null) return selectedInDropdown;
            }

            // 1. Prefer selected atom if it's a Person
            Atom selected = SuperController.singleton.GetSelectedAtom();
            if (selected != null && selected.type == "Person") return selected;

            // 2. Fallback: Find any Person atom in the scene
            foreach (Atom a in SuperController.singleton.GetAtoms())
            {
                if (a.type == "Person") return a;
            }
            
            return null;
        }

        public void Hide() => actionsPaneGO?.SetActive(false);
        public void Show() 
        { 
            if (actionsPaneGO != null) 
            {
                actionsPaneGO.transform.SetAsLastSibling();
                actionsPaneGO.SetActive(true); 
            }
        }

        public bool ExecuteAutoAction()
        {
            if (tabs.ContainsKey(currentTabType))
            {
                return tabs[currentTabType].ExecuteAutoAction();
            }
            return false;
        }
    }

    public class GalleryActionsInputHandler : MonoBehaviour
    {
        public GalleryActionsPanel panel;

        void Update()
        {
            if (panel != null)
            {
                panel.UpdateInput();
            }
        }
    }
}
