using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using SimpleJSON;

namespace VPB
{
    public class GalleryActionsPanel
    {
        public GameObject actionsPaneGO;
        private GameObject backgroundBoxGO; // The gallery's background box
        private RectTransform actionsPaneRT;
        private GameObject contentGO;
        private FileEntry selectedFile;
        private Hub.GalleryHubItem selectedHubItem;
        private GalleryPanel parentPanel;
        private bool isOpen = false;

        private List<UnityAction<UIDraggableItem>> activeActions = new List<UnityAction<UIDraggableItem>>();
        private List<UIDraggableItem> activeDraggables = new List<UIDraggableItem>();

        public GalleryActionsPanel(GalleryPanel parent, GameObject galleryBackgroundBox)
        {
            this.parentPanel = parent;
            this.backgroundBoxGO = galleryBackgroundBox;
            CreatePane();
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
                    ExecuteAction(i);
                }
            }
        }

        private void ExecuteAction(int index)
        {
            if (index >= 0 && index < activeActions.Count)
            {
                try
                {
                    activeActions[index]?.Invoke(activeDraggables[index]);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error executing action shortcut: " + ex);
                }
            }
        }

        private void CreatePane()
        {
            // Create a hidden container for logic/state
            actionsPaneGO = new GameObject("ActionsPane_Hidden");
            actionsPaneGO.transform.SetParent(backgroundBoxGO.transform, false);
            actionsPaneGO.SetActive(false);
            
            actionsPaneRT = actionsPaneGO.AddComponent<RectTransform>();
            actionsPaneRT.sizeDelta = Vector2.zero;

            // Content container (for potential children components/logic)
            contentGO = new GameObject("ActionsContent_Hidden");
            contentGO.transform.SetParent(actionsPaneGO.transform, false);
            contentGO.AddComponent<RectTransform>();

            // Add Input Handler
            var inputHandler = actionsPaneGO.AddComponent<GalleryActionsInputHandler>();
            inputHandler.panel = this;
        }
        public void HandleSelectionChanged(FileEntry file, Hub.GalleryHubItem hubItem)
        {
            selectedFile = file;
            selectedHubItem = hubItem;

            if (selectedFile == null && selectedHubItem == null)
            {
                return;
            }

            UpdateUI();
        }

        public void Open()
        {
            isOpen = true;
        }

        public void Close()
        {
            isOpen = false;
        }

        private void UpdateUI()
        {
            activeActions.Clear();
            activeDraggables.Clear();
            int buttonCount = 0;

            if (selectedHubItem != null)
            {
                CreateButton(++buttonCount, "Download", (dragger) => LogUtil.Log("Downloading: " + selectedHubItem.Title));
                CreateButton(++buttonCount, "View on HUB", (dragger) => Application.OpenURL("https://hub.virtamate.com/resources/" + selectedHubItem.ResourceId));
                CreateButton(++buttonCount, "Install Dependencies*", (dragger) => {});
                CreateButton(++buttonCount, "Quick Look*", (dragger) => {});
            }
            else if (selectedFile != null)
            {
                string pathLower = selectedFile.Path.ToLowerInvariant();
                string category = parentPanel.CurrentCategoryTitle ?? "";
                
                if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing"))
                {
                    CreateButton(++buttonCount, "Load Clothing\nto Person", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadClothing(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    });
                    CreateButton(++buttonCount, "Set as Default*", (dragger) => {});
                    CreateButton(++buttonCount, "Quick load*", (dragger) => {});
                    CreateButton(++buttonCount, "Wear Selected*", (dragger) => {});
                    CreateButton(++buttonCount, "Remove All Clothing*", (dragger) => {});
                }
                else if ((pathLower.EndsWith(".json") && (pathLower.Contains("/scenes/") || pathLower.Contains("\\scenes\\"))) || category.Contains("Scene"))
                {
                    CreateButton(++buttonCount, "Load Scene", (dragger) => dragger.LoadSceneFile(selectedFile.Uid));
                    CreateButton(++buttonCount, "Merge Scene", (dragger) => dragger.MergeSceneFile(selectedFile.Uid, false));
                }
                else if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair"))
                {
                    CreateButton(++buttonCount, "Load Hair", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadHair(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    });
                    CreateButton(++buttonCount, "Quick Hair*", (dragger) => {});
                    CreateButton(++buttonCount, "Wear Selected*", (dragger) => {});
                    CreateButton(++buttonCount, "Remove All Hair*", (dragger) => {});
                }
                else if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose"))
                {
                    CreateButton(++buttonCount, "Load Pose", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadPose(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); }
                    });
                    CreateButton(++buttonCount, "Load Pose (Silent)*", (dragger) => {});
                    CreateButton(++buttonCount, "Mirror Pose*", (dragger) => {});
                    CreateButton(++buttonCount, "Transition to Pose*", (dragger) => {});
                }
                else if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene"))
                {
                    CreateButton(++buttonCount, "Load SubScene", (dragger) => dragger.MergeSceneFile(selectedFile.Uid, false));
                }
                else
                {
                    CreateButton(++buttonCount, "Add to Scene", (dragger) => LogUtil.Log("Adding to scene: " + selectedFile.Name));
                }
            }
        }

        private GameObject CreateButton(int number, string label, UnityAction<UIDraggableItem> action, GameObject parent = null)
        {
            string prefix = number <= 9 ? number + ". " : "";
            string fullLabel = prefix + label;
            
            GameObject targetParent = parent != null ? parent : contentGO;
            GameObject btn = UI.CreateUIButton(targetParent, 340, 80, fullLabel, 20, 0, 0, AnchorPresets.middleCenter, () => {});

            RectTransform btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0, 1);
            btnRT.anchorMax = new Vector2(1, 1);
            btnRT.pivot = new Vector2(0.5f, 1);
            btnRT.sizeDelta = new Vector2(0, 80);

            LayoutElement btnLE = btn.GetComponent<LayoutElement>();
            if (btnLE == null) btnLE = btn.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 80;
            btnLE.flexibleWidth = 1;
            
            // Interaction support
            UIDraggableItem draggable = btn.AddComponent<UIDraggableItem>();
            draggable.FileEntry = selectedFile;
            draggable.HubItem = selectedHubItem;
            draggable.Panel = parentPanel;

            // Set the button action to call our delegate with the dragger
            btn.GetComponent<Button>().onClick.AddListener(() => action(draggable));
            
            // Store for keyboard shortcuts
            if (number <= 9)
            {
                activeActions.Add(action);
                activeDraggables.Add(draggable);
            }
            return btn;
        }

        private void CreateExpandableButton(int number, string label, UnityAction<UIDraggableItem> mainAction, Action<Transform, UIDraggableItem> populateOptions)
        {
            string prefix = number <= 9 ? number + ". " : "";
            string fullLabel = prefix + label;

            GameObject rowGO = new GameObject("Row_" + number);
            rowGO.transform.SetParent(contentGO.transform, false);
            RectTransform rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0, 1);
            rowRT.anchorMax = new Vector2(1, 1);
            rowRT.pivot = new Vector2(0.5f, 1);
            rowRT.sizeDelta = new Vector2(0, 0);

            LayoutElement rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.flexibleWidth = 1;

            VerticalLayoutGroup rowVLG = rowGO.AddComponent<VerticalLayoutGroup>();
            rowVLG.spacing = 6;
            rowVLG.childAlignment = TextAnchor.UpperLeft;
            rowVLG.childControlHeight = true;
            rowVLG.childControlWidth = true;
            rowVLG.childForceExpandHeight = false;
            rowVLG.childForceExpandWidth = true;

            ContentSizeFitter rowCSF = rowGO.AddComponent<ContentSizeFitter>();
            rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rowCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            GameObject headerGO = new GameObject("Header");
            headerGO.transform.SetParent(rowGO.transform, false);
            RectTransform headerRT = headerGO.AddComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0, 1);
            headerRT.anchorMax = new Vector2(1, 1);
            headerRT.pivot = new Vector2(0.5f, 1);
            headerRT.sizeDelta = new Vector2(0, 80);

            LayoutElement headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 80;
            headerLE.flexibleWidth = 1;

            HorizontalLayoutGroup headerHLG = headerGO.AddComponent<HorizontalLayoutGroup>();
            headerHLG.spacing = 6;
            headerHLG.childAlignment = TextAnchor.MiddleLeft;
            headerHLG.childControlHeight = true;
            headerHLG.childControlWidth = true;
            headerHLG.childForceExpandHeight = false;
            headerHLG.childForceExpandWidth = false;

            GameObject btn = UI.CreateUIButton(headerGO, 10, 80, fullLabel, 20, 0, 0, AnchorPresets.middleCenter, () => {});
            RectTransform btnRT = btn.GetComponent<RectTransform>();
            // No need for manual anchor stretching when childControlWidth is true
            btnRT.sizeDelta = new Vector2(0, 80);

            LayoutElement btnLE = btn.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 80;
            btnLE.flexibleWidth = 1;

            UIDraggableItem draggable = btn.AddComponent<UIDraggableItem>();
            draggable.FileEntry = selectedFile;
            draggable.HubItem = selectedHubItem;
            draggable.Panel = parentPanel;
            btn.GetComponent<Button>().onClick.AddListener(() => mainAction(draggable));

            if (number <= 9)
            {
                activeActions.Add(mainAction);
                activeDraggables.Add(draggable);
            }
        }

        private Toggle CreateInlineToggle(Transform parent, string label, bool defaultOn, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = UI.CreateToggle(parent.gameObject, label, 300, 50, 0, 0, AnchorPresets.middleCenter, onValueChanged);
            RectTransform rt = toggleGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 50);
            LayoutElement le = toggleGO.AddComponent<LayoutElement>();
            le.preferredHeight = 50;

            Toggle t = toggleGO.GetComponent<Toggle>();
            if (t != null) t.isOn = defaultOn;
            return t;
        }

        private Dropdown CreateInlineDropdown(Transform parent, string label, List<string> options, int currentIdx, UnityAction<int> onValueChanged)
        {
            GameObject ddGO = UI.CreateDropdown(parent.gameObject, label, 300, 60, options, currentIdx, onValueChanged);
            RectTransform rt = ddGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 60);
            LayoutElement le = ddGO.AddComponent<LayoutElement>();
            le.preferredHeight = 60;
            return ddGO.GetComponent<Dropdown>();
        }

        private Button CreateInlineButton(Transform parent, string label, UnityAction onClick)
        {
            GameObject btn = UI.CreateUIButton(parent.gameObject, 300, 60, label, 16, 0, 0, AnchorPresets.middleCenter, () => {});
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 60);
            LayoutElement le = btn.AddComponent<LayoutElement>();
            le.preferredHeight = 60;

            Button b = btn.GetComponent<Button>();
            b.onClick.AddListener(onClick);
            return b;
        }

        public void Hide() => actionsPaneGO?.SetActive(false);
        public void Show() { actionsPaneGO?.SetActive(false); }

        private void LoadThumbnail(FileEntry file, RawImage target)
        {
            if (file == null || target == null) return;
            target.texture = null;
            target.color = new Color(0, 0, 0, 0.5f);

            string imgPath = "";
            string lowerPath = file.Path.ToLowerInvariant();
            if (lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".png"))
            {
                imgPath = file.Path;
            }
            else
            {
                string testJpg = System.IO.Path.ChangeExtension(file.Path, ".jpg");
                if (FileManager.FileExists(testJpg)) imgPath = testJpg;
                else
                {
                    string testPng = System.IO.Path.ChangeExtension(file.Path, ".png");
                    if (FileManager.FileExists(testPng)) imgPath = testPng;
                }
            }

            if (string.IsNullOrEmpty(imgPath)) return;
            if (CustomImageLoaderThreaded.singleton == null) return;

            Texture2D tex = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
            if (tex != null)
            {
                target.texture = tex;
                target.color = Color.white;
                return;
            }

            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = imgPath;
            qi.isThumbnail = true;
            qi.priority = 20; 
            qi.callback = (res) => {
                if (res != null && res.tex != null && target != null) {
                    target.texture = res.tex;
                    target.color = Color.white;
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }

        private void LoadHubThumbnail(string url, RawImage target)
        {
            if (string.IsNullOrEmpty(url) || target == null) return;
            target.texture = null;
            target.color = new Color(0, 0, 0, 0.5f);

            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
            qi.imgPath = url;
            qi.priority = 20;
            qi.callback = (res) => {
                if (res != null && res.tex != null && target != null) {
                    target.texture = res.tex;
                    target.color = Color.white;
                }
            };
            CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
        }
        private Atom GetBestTargetAtom()
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

        public bool ExecuteAutoAction()
        {
            if (activeActions.Count > 0 && activeDraggables.Count > 0)
            {
                 try
                 {
                     if (activeActions.Count >= 1)
                     {
                         parentPanel?.ShowTemporaryStatus("Auto-applying...", 1.0f);
                         activeActions[0]?.Invoke(activeDraggables[0]);
                         return true;
                     }
                 }
                 catch (Exception ex)
                 {
                     LogUtil.LogError("[VPB] Auto-Execute failed: " + ex);
                 }
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
