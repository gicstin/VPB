using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

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
        private bool isExpanded = true;
        public bool IsExpanded => isExpanded;

        public GalleryActionsPanel(GalleryPanel parent, GameObject galleryBackgroundBox)
        {
            this.parentPanel = parent;
            this.backgroundBoxGO = galleryBackgroundBox;
            CreatePane();
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

            // Content Container
            contentGO = new GameObject("Content");
            contentGO.transform.SetParent(actionsPaneGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.sizeDelta = Vector2.zero;
            contentRT.offsetMin = new Vector2(20, 20);
            contentRT.offsetMax = new Vector2(-320, -20); // No header space needed

            GridLayoutGroup glg = contentGO.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(280, 80);
            glg.spacing = new Vector2(15, 15);
            glg.childAlignment = TextAnchor.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;

            // Preview Container (Right Side)
            previewContainer = new GameObject("PreviewContainer");
            previewContainer.transform.SetParent(actionsPaneGO.transform, false);
            RectTransform pRT = previewContainer.AddComponent<RectTransform>();
            pRT.anchorMin = new Vector2(1, 0.5f);
            pRT.anchorMax = new Vector2(1, 0.5f);
            pRT.pivot = new Vector2(1, 0.5f);
            pRT.anchoredPosition = new Vector2(-20, 0); 
            pRT.sizeDelta = new Vector2(280, 360);

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

            if (selectedHubItem != null)
            {
                previewTitle.text = "<b>" + selectedHubItem.Title + "</b>\n<size=14>" + selectedHubItem.Creator + "</size>";
                LoadHubThumbnail(selectedHubItem.ThumbnailUrl, previewImage);

                CreateButton("Download", () => LogUtil.Log("Downloading: " + selectedHubItem.Title));
                CreateButton("View on HUB", () => Application.OpenURL("https://hub.virtamate.com/resources/" + selectedHubItem.ResourceId));
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
                
                if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\"))
                {
                    CreateButton("Load Clothing\nto Person AtomX", () => LogUtil.Log("Loading clothing: " + selectedFile.Name));
                    CreateButton("Add to Favorites", () => selectedFile.SetFavorite(true));
                }
                else if (pathLower.EndsWith(".json") && (pathLower.Contains("/scenes/") || pathLower.Contains("\\scenes\\")))
                {
                    CreateButton("Load Scene", () => LogUtil.Log("Loading scene: " + selectedFile.Name));
                    CreateButton("Merge Scene", () => LogUtil.Log("Merging scene: " + selectedFile.Name));
                }
                else
                {
                    CreateButton("Add to Scene", () => LogUtil.Log("Adding to scene: " + selectedFile.Name));
                }
            }
        }

        private void CreateButton(string label, UnityAction action)
        {
            GameObject btn = UI.CreateUIButton(contentGO, 360, 80, label, 20, 0, 0, AnchorPresets.middleCenter, action);
            
            // Interaction support
            UIDraggableItem draggable = btn.AddComponent<UIDraggableItem>();
            draggable.FileEntry = selectedFile;
            draggable.HubItem = selectedHubItem;
            draggable.Panel = parentPanel;
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

            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.QIPool.Get();
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

            CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.QIPool.Get();
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
    }
}
