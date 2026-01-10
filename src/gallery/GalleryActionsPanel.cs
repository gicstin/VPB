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
        private RawImage previewImage;
        private Text previewTitle;
        private GameObject previewContainer;
        private AspectRatioFitter previewARF;
        private FileEntry selectedFile;
        private Hub.GalleryHubItem selectedHubItem;
        private GalleryPanel parentPanel;
        private bool isOpen = false;
        private bool isExpanded = false;
        public bool IsExpanded => isExpanded;

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

        public void ToggleExpand()
        {
            isExpanded = !isExpanded;
            if (isExpanded)
            {
                if (selectedFile != null || selectedHubItem != null)
                    Open();
            }
            else
            {
                Close();
            }
        }

        private void CreatePane()
        {
            // Create anchored to the bottom of the mother pane
            // Daughter pane 1200 wide (matching mother), fixed height 400
            actionsPaneGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0.05f, 0.05f, 0.05f, 0.95f), AnchorPresets.bottomMiddle, 1200, 400, new Vector2(0, -10));
            actionsPaneRT = actionsPaneGO.GetComponent<RectTransform>();
            
            // Add Input Handler
            var inputHandler = actionsPaneGO.AddComponent<GalleryActionsInputHandler>();
            inputHandler.panel = this;

            // Anchoring: Bottom of parent, pivot top
            actionsPaneRT.anchorMin = new Vector2(0.5f, 0);
            actionsPaneRT.anchorMax = new Vector2(0.5f, 0);
            actionsPaneRT.pivot = new Vector2(0.5f, 1);
            actionsPaneRT.anchoredPosition = new Vector2(0, -10); // 10px gap below bottom
            
            // Ensure it's on top of other siblings
            actionsPaneGO.transform.SetAsLastSibling();

            UIHoverColor bgHover = actionsPaneGO.AddComponent<UIHoverColor>();
            bgHover.normalColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
            bgHover.hoverColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);

            // 1. Main ScrollView Container (Left Column)
            GameObject scrollViewGO = new GameObject("ScrollView");
            scrollViewGO.transform.SetParent(actionsPaneGO.transform, false);
            
            Image scrollBg = scrollViewGO.AddComponent<Image>();
            scrollBg.color = new Color(0, 0, 0, 0);

            RectTransform scrollViewRT = scrollViewGO.GetComponent<RectTransform>();
            scrollViewRT.anchorMin = new Vector2(0, 0);
            scrollViewRT.anchorMax = new Vector2(1, 1);
            scrollViewRT.pivot = new Vector2(0, 1);
            scrollViewRT.offsetMin = new Vector2(20, 10);
            scrollViewRT.offsetMax = new Vector2(-320, -10);

            ScrollRect sr = scrollViewGO.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 2.5f;
            
            // Viewport
            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollViewGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = Vector2.zero;
            viewportRT.pivot = new Vector2(0, 1);
            
            viewportGO.AddComponent<RectMask2D>();
            
            // Content
            contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0, 1);
            contentRT.sizeDelta = Vector2.zero;

            sr.content = contentRT;
            sr.viewport = viewportRT;

            VerticalLayoutGroup glg = contentGO.AddComponent<VerticalLayoutGroup>();
            glg.spacing = 15;
            glg.padding = new RectOffset(10, 10, 10, 40); // Added bottom padding for scrolling room
            glg.childAlignment = TextAnchor.UpperLeft;
            glg.childControlHeight = true;
            glg.childControlWidth = true;
            glg.childForceExpandHeight = false;
            glg.childForceExpandWidth = true;
            
            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Vertical Scrollbar
            GameObject scrollbarGO = UI.CreateScrollBar(scrollViewGO, 15, 0, Scrollbar.Direction.BottomToTop);
            RectTransform scrollbarRT = scrollbarGO.GetComponent<RectTransform>();
            scrollbarRT.anchorMin = new Vector2(1, 0);
            scrollbarRT.anchorMax = new Vector2(1, 1);
            scrollbarRT.pivot = new Vector2(1, 0.5f);
            scrollbarRT.sizeDelta = new Vector2(15, 0);
            scrollbarRT.anchoredPosition = Vector2.zero;

            sr.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.verticalScrollbarSpacing = 5;

            // Adjust viewport to accommodate scrollbar
            viewportRT.offsetMax = new Vector2(-20, 0);

            // 3. Preview Container (Right Side)
            previewContainer = new GameObject("PreviewContainer");
            previewContainer.transform.SetParent(actionsPaneGO.transform, false);
            RectTransform pRT = previewContainer.AddComponent<RectTransform>();
            pRT.anchorMin = new Vector2(1, 0.5f);
            pRT.anchorMax = new Vector2(1, 0.5f);
            pRT.pivot = new Vector2(1, 0.5f);
            pRT.anchoredPosition = new Vector2(0, 0); 
            pRT.sizeDelta = new Vector2(300, 360); // Slightly wider

            // Thumbnail Area (Top)
            GameObject thumbAreaGO = new GameObject("ThumbnailArea");
            thumbAreaGO.transform.SetParent(previewContainer.transform, false);
            RectTransform taRT = thumbAreaGO.AddComponent<RectTransform>();
            taRT.anchorMin = new Vector2(0, 1);
            taRT.anchorMax = new Vector2(1, 1);
            taRT.pivot = new Vector2(0.5f, 1);
            taRT.anchoredPosition = new Vector2(0, 0);
            taRT.sizeDelta = new Vector2(0, 260);

            GameObject thumbGO = new GameObject("Thumbnail");
            thumbGO.transform.SetParent(thumbAreaGO.transform, false);
            previewImage = thumbGO.AddComponent<RawImage>();
            previewImage.color = new Color(0, 0, 0, 0.5f);

            previewARF = thumbGO.AddComponent<AspectRatioFitter>();
            previewARF.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            previewARF.aspectRatio = 1.333f;

            // Package Name Text with Border
            GameObject titleBorderGO = new GameObject("TitleBorder");
            titleBorderGO.transform.SetParent(thumbGO.transform, false);
            Image borderImg = titleBorderGO.AddComponent<Image>();
            borderImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            
            // 1px Outline as a border
            Outline outline = titleBorderGO.AddComponent<Outline>();
            outline.effectColor = new Color(1, 1, 1, 0.3f);
            outline.effectDistance = new Vector2(1, 1);

            RectTransform borderRT = titleBorderGO.GetComponent<RectTransform>();
            borderRT.anchorMin = new Vector2(0, 0); // Stretch horizontally relative to thumb
            borderRT.anchorMax = new Vector2(1, 0);
            borderRT.pivot = new Vector2(0.5f, 1); // Pivot top of border to bottom of thumb
            borderRT.anchoredPosition = new Vector2(0, -5); // Small gap below image
            borderRT.sizeDelta = new Vector2(0, 90);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBorderGO.transform, false);
            previewTitle = titleGO.AddComponent<Text>();
            previewTitle.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            previewTitle.fontSize = 18;
            previewTitle.color = Color.white;
            previewTitle.alignment = TextAnchor.MiddleCenter;
            previewTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            previewTitle.verticalOverflow = VerticalWrapMode.Truncate;
            previewTitle.supportRichText = true;
            previewTitle.text = "Package Name";
            
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = Vector2.zero;
            titleRT.anchorMax = Vector2.one;
            titleRT.sizeDelta = new Vector2(-10, -10); // Margin inside border
            titleRT.anchoredPosition = Vector2.zero;

            actionsPaneGO.SetActive(false);
        }

        public void HandleSelectionChanged(FileEntry file, Hub.GalleryHubItem hubItem)
        {
            selectedFile = file;
            selectedHubItem = hubItem;

            if (selectedFile == null && selectedHubItem == null)
            {
                Close();
                return;
            }

            if (isExpanded) Open();
            UpdateUI();
        }

        public void Open()
        {
            isOpen = true;
            actionsPaneGO.SetActive(true);
            // Refresh curvature on parent change if needed
            parentPanel.TriggerCurvatureRefresh();
            parentPanel.UpdateLayout();
        }

        public void Close()
        {
            isOpen = false;
            actionsPaneGO.SetActive(false);
            parentPanel.UpdateLayout();
        }

        private void UpdateUI()
        {
            foreach (Transform child in contentGO.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            // Persistence: Don't clear expandedRow fields here.
            // They will be matched in CreateExpandableButton.

            activeActions.Clear();
            activeDraggables.Clear();
            int buttonCount = 0;

            if (selectedHubItem != null)
            {
                previewTitle.text = "<b>" + selectedHubItem.Title + "</b>\n<size=14>" + selectedHubItem.Creator + "</size>";
                LoadHubThumbnail(selectedHubItem.ThumbnailUrl, previewImage);

                CreateButton(++buttonCount, "Download", (dragger) => LogUtil.Log("Downloading: " + selectedHubItem.Title));
                CreateButton(++buttonCount, "View on HUB", (dragger) => Application.OpenURL("https://hub.virtamate.com/resources/" + selectedHubItem.ResourceId));
                CreateButton(++buttonCount, "Install Dependencies*", (dragger) => {});
                CreateButton(++buttonCount, "Quick Look*", (dragger) => {});
            }
            else if (selectedFile != null)
            {
                string title = "<b>" + selectedFile.Name + "</b>";
                if (selectedFile is VarFileEntry vfe)
                {
                    title = "<b>" + vfe.Package.Uid + "</b>\n<size=14>" + vfe.InternalPath + "</size>";
                }
                previewTitle.text = title;
                LoadThumbnail(selectedFile, previewImage);

                string pathLower = selectedFile.Path.ToLowerInvariant();
                string category = parentPanel.CurrentCategoryTitle ?? "";
                
                if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing"))
                {
                    CreateButton(++buttonCount, "Load Clothing\nto Person", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadClothing(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); Open(); }
                    });
                    CreateButton(++buttonCount, "Add to Favorites", (dragger) => selectedFile.SetFavorite(true));
                    CreateButton(++buttonCount, "Set as Default*", (dragger) => {});
                    CreateButton(++buttonCount, "Quick load*", (dragger) => {});
                    CreateButton(++buttonCount, "Wear Selected*", (dragger) => {});
                    CreateButton(++buttonCount, "Remove All Clothing*", (dragger) => {});
                }
                else if ((pathLower.EndsWith(".json") && (pathLower.Contains("/scenes/") || pathLower.Contains("\\scenes\\"))) || category.Contains("Scene"))
                {
                    CreateButton(++buttonCount, "Load Scene", (dragger) => dragger.LoadSceneFile(selectedFile.Uid));
                    CreateButton(++buttonCount, "Merge Scene", (dragger) => dragger.MergeSceneFile(selectedFile.Uid, false));
                    CreateExpandableButton(++buttonCount, "Merge Person\nOnly",
                        (dragger) => dragger.MergeScenePersonsOnly(selectedFile.Uid),
                        (optionsParent, dragger) => {
                            bool ignoreRoot = true;
                            bool renameUnique = true;
                            string selectedPerson = null;
                            string applyToTarget = null; // null means "New Person"
                            List<string> personIds = new List<string> { "All Persons" };
                            List<string> scenePersonIds = new List<string> { "New Person" };

                            try
                            {
                                if (selectedFile == null) LogUtil.LogError("SelectedFile is null in submenu");
                                else
                                {
                                    // Use native VaM LoadJSON with fallback for manual read if needed
                                    JSONNode node = UI.LoadJSONWithFallback(selectedFile.Uid, selectedFile);
                                    if (node != null && node["atoms"] != null)
                                    {
                                        int count = 0;
                                        List<string> foundTypes = new List<string>();
                                        foreach (JSONNode atom in node["atoms"].AsArray)
                                        {
                                            string type = atom["type"].Value;
                                            if (!foundTypes.Contains(type)) foundTypes.Add(type);
                                            
                                            if (type == "Person")
                                            {
                                                string pid = atom["id"];
                                                if (!string.IsNullOrEmpty(pid))
                                                {
                                                    personIds.Add(pid);
                                                    count++;
                                                }
                                            }
                                        }
                                        LogUtil.Log($"[VPB] Found {count} persons in file {selectedFile.Name}. Found atom types: {string.Join(", ", foundTypes.ToArray())}");
                                    }
                                }

                                // Populate scene persons
                                if (SuperController.singleton != null)
                                {
                                    foreach (Atom a in SuperController.singleton.GetAtoms())
                                    {
                                        if (a.type == "Person") scenePersonIds.Add(a.uid);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError($"[VPB] Error parsing persons: {ex.Message}");
                            }

                            CreateInlineToggle(optionsParent, "Ignore Root (Teleport)", true, (val) => ignoreRoot = val);
                            CreateInlineToggle(optionsParent, "Import as New (Rename)", true, (val) => renameUnique = val);
                            
                            Button mergeBtn = null;
                            var sourceDd = CreateInlineDropdown(optionsParent, "Source Person", personIds, 0, (idx) => {
                                selectedPerson = (idx == 0) ? null : personIds[idx];
                                if (mergeBtn != null)
                                {
                                    Text t = mergeBtn.GetComponentInChildren<Text>();
                                    if (t != null) t.text = (selectedPerson == null) ? "Merge All Persons" : "Merge " + selectedPerson;
                                }
                            });
                            // Set custom color for Source dropdown
                            Image sourceImg = sourceDd.GetComponent<Image>();
                            if (sourceImg != null) sourceImg.color = new Color(0.25f, 0.25f, 0.25f, 1f);

                            var applyDd = CreateInlineDropdown(optionsParent, "Apply To", scenePersonIds, 0, (idx) => {
                                applyToTarget = (idx == 0) ? null : scenePersonIds[idx];
                            });
                            // Set custom color for Apply To dropdown
                            Image applyImg = applyDd.GetComponent<Image>();
                            if (applyImg != null) applyImg.color = new Color(0.2f, 0.25f, 0.3f, 1f);

                            mergeBtn = CreateInlineButton(optionsParent, "Merge All Persons", () => {
                                dragger.MergeScenePersonsOnly(selectedFile.Uid, ignoreRoot, selectedPerson, renameUnique, applyToTarget);
                            });

                            CreateInlineButton(optionsParent, "Merge Appearance", () => {
                                if (string.IsNullOrEmpty(selectedPerson) || selectedPerson == "All Persons")
                                {
                                    LogUtil.LogWarning("[VPB] Please select a specific source person for Appearance merge.");
                                    return;
                                }
                                dragger.MergeSceneAppearanceOnly(selectedFile.Uid, selectedPerson, renameUnique, applyToTarget);
                            });

                            CreateInlineButton(optionsParent, "Merge Pose", () => {
                                if (string.IsNullOrEmpty(selectedPerson) || selectedPerson == "All Persons")
                                {
                                    LogUtil.LogWarning("[VPB] Please select a specific source person for Pose merge.");
                                    return;
                                }
                                dragger.MergeScenePoseOnly(selectedFile.Uid, selectedPerson, renameUnique, applyToTarget);
                            });
                        });
                    CreateButton(++buttonCount, "Replace Scene\nKeep Person", (dragger) => dragger.ReplaceSceneKeepPersons(selectedFile.Uid));
                    CreateButton(++buttonCount, "Save as template*", (dragger) => {});
                    CreateButton(++buttonCount, "Export as package*", (dragger) => {});
                    CreateButton(++buttonCount, "Load Random*", (dragger) => {});
                }
                else if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair"))
                {
                    CreateButton(++buttonCount, "Load Hair", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadHair(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); Open(); }
                    });
                    CreateButton(++buttonCount, "Quick Hair*", (dragger) => {});
                    CreateButton(++buttonCount, "Favorite Hair*", (dragger) => {});
                    CreateButton(++buttonCount, "Wear Selected*", (dragger) => {});
                    CreateButton(++buttonCount, "Remove All Hair*", (dragger) => {});
                }
                else if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose"))
                {
                    CreateButton(++buttonCount, "Load Pose", (dragger) => {
                        Atom target = GetBestTargetAtom();
                        if (target != null) dragger.LoadPose(target);
                        else { LogUtil.LogWarning("[VPB] Please select a Person atom."); Open(); }
                    });
                    CreateButton(++buttonCount, "Load Pose (Silent)*", (dragger) => {});
                    CreateButton(++buttonCount, "Mirror Pose*", (dragger) => {});
                    CreateButton(++buttonCount, "Transition to Pose*", (dragger) => {});
                }
                else if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene"))
                {
                    CreateButton(++buttonCount, "Load SubScene", (dragger) => dragger.MergeSceneFile(selectedFile.Uid, false));
                    CreateButton(++buttonCount, "Merge SubScene*", (dragger) => {});
                    CreateButton(++buttonCount, "Export SubScene*", (dragger) => {});
                }
                else if (pathLower.Contains("/assets/") || pathLower.Contains("\\assets\\") || category.Contains("Asset") || category.Contains("CUA"))
                {
                    CreateButton(++buttonCount, "Spawn Asset*", (dragger) => {});
                    CreateButton(++buttonCount, "Quick Spawn*", (dragger) => {});
                    CreateButton(++buttonCount, "CUA Load Options*", (dragger) => {});
                }
                else if (pathLower.Contains("/morphs/") || pathLower.Contains("\\morphs\\") || category.Contains("Morph"))
                {
                    CreateButton(++buttonCount, "Apply Morph*", (dragger) => {});
                    CreateButton(++buttonCount, "Favorite Morph*", (dragger) => {});
                    CreateButton(++buttonCount, "Reset Morph*", (dragger) => {});
                }
                else if (pathLower.Contains("/audio/") || pathLower.Contains("\\audio\\") || category.Contains("Audio"))
                {
                    CreateButton(++buttonCount, "Play Audio*", (dragger) => {});
                    CreateButton(++buttonCount, "Queue Audio*", (dragger) => {});
                    CreateButton(++buttonCount, "Test Audio*", (dragger) => {});
                    CreateButton(++buttonCount, "Stop All Audio*", (dragger) => {});
                }
                else if (pathLower.Contains("/plugins/") || pathLower.Contains("\\plugins\\") || category.Contains("Plugin"))
                {
                    CreateButton(++buttonCount, "Load Plugin*", (dragger) => {});
                    CreateButton(++buttonCount, "Toggle Plugin*", (dragger) => {});
                    CreateButton(++buttonCount, "Plugin Settings*", (dragger) => {});
                }
                else if (pathLower.EndsWith(".var") || category.Contains("All") || category.Contains("Package"))
                {
                    CreateButton(++buttonCount, "Enable VAR*", (dragger) => {});
                    CreateButton(++buttonCount, "Disable VAR*", (dragger) => {});
                    CreateButton(++buttonCount, "Offload VAR*", (dragger) => {});
                    CreateButton(++buttonCount, "Scan for Deps*", (dragger) => {});
                }
                else
                {
                    CreateButton(++buttonCount, "Add to Scene", (dragger) => LogUtil.Log("Adding to scene: " + selectedFile.Name));
                    CreateButton(++buttonCount, "Quick Look*", (dragger) => {});
                }

                // Global actions for any file
                if (selectedFile != null)
                {
                    CreateButton(++buttonCount, "Delete File*", (dragger) => {});
                    CreateButton(++buttonCount, "Open in Explorer*", (dragger) => {});
                    CreateButton(++buttonCount, "Adjust Position*", (dragger) => {});
                    CreateButton(++buttonCount, "View Dependencies*", (dragger) => {});
                    CreateButton(++buttonCount, "Used By VARs*", (dragger) => {});
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

        private GameObject expandedRowOptionsContainer;
        private Text expandedRowArrowText;
        private string expandedRowLabel;
        private Action<Transform, UIDraggableItem> activeExpandedOptionsBuilder;

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

            GameObject arrowBtn = UI.CreateUIButton(headerGO, 50, 80, "▼", 18, 0, 0, AnchorPresets.middleCenter, () => {});
            LayoutElement arrowLE = arrowBtn.AddComponent<LayoutElement>();
            arrowLE.preferredWidth = 50;
            arrowLE.preferredHeight = 80;

            Text arrowText = arrowBtn.GetComponentInChildren<Text>();

            GameObject optionsGO = new GameObject("Options");
            optionsGO.transform.SetParent(rowGO.transform, false);
            RectTransform optionsRT = optionsGO.AddComponent<RectTransform>();
            optionsRT.anchorMin = new Vector2(0, 1);
            optionsRT.anchorMax = new Vector2(1, 1);
            optionsRT.pivot = new Vector2(0.5f, 1);
            optionsRT.sizeDelta = new Vector2(0, 0);

            Image optionsImg = optionsGO.AddComponent<Image>();
            optionsImg.color = new Color(0, 0, 0, 0.15f);

            LayoutElement optionsLE = optionsGO.AddComponent<LayoutElement>();
            optionsLE.flexibleWidth = 1;

            VerticalLayoutGroup optionsVLG = optionsGO.AddComponent<VerticalLayoutGroup>();
            optionsVLG.spacing = 6;
            optionsVLG.padding = new RectOffset(10, 10, 10, 20);
            optionsVLG.childAlignment = TextAnchor.UpperLeft;
            optionsVLG.childControlHeight = true;
            optionsVLG.childControlWidth = true;
            optionsVLG.childForceExpandHeight = false;
            optionsVLG.childForceExpandWidth = true;

            ContentSizeFitter optionsCSF = optionsGO.AddComponent<ContentSizeFitter>();
            optionsCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            optionsCSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            optionsGO.SetActive(false);

            Button arrowButton = arrowBtn.GetComponent<Button>();
            arrowButton.onClick.RemoveAllListeners();
            arrowButton.onClick.AddListener(() => {
                if (expandedRowOptionsContainer != null && expandedRowOptionsContainer != optionsGO)
                {
                    expandedRowOptionsContainer.SetActive(false);
                    if (expandedRowArrowText != null) expandedRowArrowText.text = "▼";
                }

                if (optionsGO.activeSelf && activeExpandedOptionsBuilder == populateOptions)
                {
                    optionsGO.SetActive(false);
                    arrowText.text = "▼";
                    expandedRowOptionsContainer = null;
                    expandedRowArrowText = null;
                    expandedRowLabel = null;
                    activeExpandedOptionsBuilder = null;
                    return;
                }

                foreach (Transform child in optionsGO.transform)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }

                optionsGO.SetActive(true);
                arrowText.text = "▲";
                expandedRowOptionsContainer = optionsGO;
                expandedRowArrowText = arrowText;
                expandedRowLabel = label;
                activeExpandedOptionsBuilder = populateOptions;
                populateOptions(optionsGO.transform, draggable);
            });

            // Check if this button was previously expanded
            if (activeExpandedOptionsBuilder == populateOptions && expandedRowLabel == label)
            {
                optionsGO.SetActive(true);
                arrowText.text = "▲";
                expandedRowOptionsContainer = optionsGO;
                expandedRowArrowText = arrowText;
                populateOptions(optionsGO.transform, draggable);
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
        public void Show() { if (isOpen) actionsPaneGO?.SetActive(true); }

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
                if (previewARF != null) previewARF.aspectRatio = (float)tex.width / (float)tex.height;
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
                    if (previewARF != null) previewARF.aspectRatio = (float)res.tex.width / (float)res.tex.height;
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
                    if (previewARF != null) previewARF.aspectRatio = (float)res.tex.width / (float)res.tex.height;
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
