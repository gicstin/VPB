using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VPB.Hub;

namespace VPB
{
    public partial class GalleryPanel
    {
        private string currentHubCategory = "All";
        private string currentHubCreator = "All";
        private string currentHubPayType = "All";
        private string currentHubSearch = "";
        private HashSet<string> currentHubTags = new HashSet<string>();
        private int hubTagPage = 0;
        private int hubCreatorPage = 0;
        private const int HubTagsPerPage = 40;
        private const int HubCreatorsPerPage = 40;
        
        public bool IsHubMode 
        {
            get 
            {
                return (leftActiveContent == ContentType.Hub || rightActiveContent == ContentType.Hub);
            }
        }

        private bool hubLogSubscribed = false;

        private void UpdateHubCategories(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            
            foreach (var btn in trackedButtons) ReturnTabButton(btn);
            trackedButtons.Clear();

            // Separator
            CreateTabButton(container.transform, "--- CATEGORIES ---", Color.black, false, null, trackedButtons);

            GalleryHubController.Instance.GetInfo((info) => {
                 bool stillActive = IsHubMode;
                 if (!stillActive) return;

                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();

                 foreach (var cat in info.Categories)
                 {
                    bool isActive = (currentHubCategory == cat);
                    Color btnColor = isActive ? ColorHub : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, cat, btnColor, isActive, () => {
                        currentHubCategory = cat;
                        currentPage = 0;
                        if (titleText != null) titleText.text = "HUB: " + cat;
                        RefreshHubItems();
                        UpdateTabs(); 
                    }, trackedButtons);
                 }
            }, (err) => {
                 if (!IsHubMode) return;
                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();
                 CreateTabButton(container.transform, "Error: " + err, Color.red, false, null, trackedButtons);
            });
        }

        private void UpdateHubPayTypes(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            
            foreach (var btn in trackedButtons) ReturnTabButton(btn);
            trackedButtons.Clear();

            // Separator
            CreateTabButton(container.transform, "--- PAY TYPE ---", Color.black, false, null, trackedButtons);

            GalleryHubController.Instance.GetInfo((info) => {
                 bool stillActive = IsHubMode;
                 if (!stillActive) return;

                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();

                 foreach (var pt in info.PayTypes)
                 {
                    bool isActive = (currentHubPayType == pt);
                    Color btnColor = isActive ? new Color(0.2f, 0.6f, 0.8f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, pt, btnColor, isActive, () => {
                        currentHubPayType = pt;
                        currentPage = 0;
                        RefreshHubItems();
                        UpdateTabs(); 
                    }, trackedButtons);
                 }
            }, null);
        }

        private void UpdateHubCreators(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            
            foreach (var btn in trackedButtons) ReturnTabButton(btn);
            trackedButtons.Clear();

            // Separator
            CreateTabButton(container.transform, "--- CREATORS ---", Color.black, false, null, trackedButtons);

            GalleryHubController.Instance.GetInfo((info) => {
                 bool stillActive = IsHubMode;
                 if (!stillActive) return;

                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();
                 
                 // Filter creators based on search if needed (creatorFilter)
                 var displayCreators = new List<string>();
                 if (string.IsNullOrEmpty(creatorFilter))
                 {
                     displayCreators.AddRange(info.Creators);
                 }
                 else
                 {
                     foreach (var c in info.Creators)
                     {
                         if (c.IndexOf(creatorFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                             displayCreators.Add(c);
                     }
                 }

                 int totalCreatorPages = Mathf.CeilToInt((float)displayCreators.Count / HubCreatorsPerPage);
                 if (hubCreatorPage >= totalCreatorPages && totalCreatorPages > 0) hubCreatorPage = 0;
                 
                 if (hubCreatorPage > 0)
                 {
                     CreateTabButton(container.transform, "<< Prev Creators", new Color(0.8f, 0.8f, 0.2f), false, () => {
                         hubCreatorPage--;
                         UpdateTabs();
                     }, trackedButtons);
                 }
                 
                 var pageCreators = displayCreators.Skip(hubCreatorPage * HubCreatorsPerPage).Take(HubCreatorsPerPage);
                 foreach (var creator in pageCreators)
                 {
                    bool isActive = (currentHubCreator == creator);
                    Color btnColor = isActive ? ColorCreator : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, creator, btnColor, isActive, () => {
                        currentHubCreator = creator;
                        currentPage = 0;
                        RefreshHubItems();
                        UpdateTabs(); 
                    }, trackedButtons);
                 }

                 if (hubCreatorPage < totalCreatorPages - 1)
                 {
                     CreateTabButton(container.transform, "Next Creators >>", new Color(0.8f, 0.8f, 0.2f), false, () => {
                         hubCreatorPage++;
                         UpdateTabs();
                     }, trackedButtons);
                 }

            }, null);
        }

        private void UpdateHubTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            
            if (!hubLogSubscribed)
            {
                hubLogSubscribed = true;
                GalleryHubController.Instance.OnLog += (msg) => {
                    if (statusBarText != null) statusBarText.text = msg;
                };
            }

            // In new layout, this might not be used directly if we use the specific ones above.
            // But let's keep it as a fallback or for Category view.
            UpdateHubCategories(container, trackedButtons, isLeft);
        }

        private void UpdateHubTags(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (container == null) return;
            
            foreach (var btn in trackedButtons) ReturnTabButton(btn);
            trackedButtons.Clear();

            // Separator
            CreateTabButton(container.transform, "--- TAGS ---", Color.black, false, null, trackedButtons);

            // Create loading button
            CreateTabButton(container.transform, "Loading Tags...", Color.gray, false, null, trackedButtons);

             GalleryHubController.Instance.GetTags((tags) => {
                 bool stillActive = isLeft ? (leftActiveContent == ContentType.Hub) : (rightActiveContent == ContentType.Hub);
                 if (!stillActive) return;
                 
                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();

                 // Filter tags based on search
                 var displayTags = new List<string>();
                 if (string.IsNullOrEmpty(tagFilter))
                 {
                     displayTags.AddRange(tags);
                 }
                 else
                 {
                     foreach (var t in tags)
                     {
                         if (t.IndexOf(tagFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                             displayTags.Add(t);
                     }
                 }

                 // Pagination
                 int totalPages = Mathf.CeilToInt((float)displayTags.Count / HubTagsPerPage);
                 if (hubTagPage >= totalPages && totalPages > 0) hubTagPage = 0;
                 if (totalPages == 0) hubTagPage = 0;

                 // Prev Button
                 if (hubTagPage > 0)
                 {
                     CreateTabButton(container.transform, "<< Prev Page", new Color(0.8f, 0.8f, 0.2f), false, () => {
                         hubTagPage--;
                         UpdateTabs();
                     }, trackedButtons);
                 }

                 // Page Info
                 if (totalPages > 1)
                 {
                     CreateTabButton(container.transform, $"Page {hubTagPage + 1}/{totalPages}", Color.gray, false, null, trackedButtons);
                 }
                 
                 var pageTags = displayTags.Skip(hubTagPage * HubTagsPerPage).Take(HubTagsPerPage);

                 foreach (var tag in pageTags)
                 {
                    bool isActive = currentHubTags.Contains(tag);
                    Color btnColor = isActive ? new Color(0.5f, 0.2f, 0.5f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);

                    CreateTabButton(container.transform, tag, btnColor, isActive, () => {
                        if (currentHubTags.Contains(tag)) currentHubTags.Remove(tag);
                        else currentHubTags.Add(tag);
                        
                        currentPage = 0;
                        RefreshHubItems();
                        UpdateTabs();
                    }, trackedButtons);
                 }
                 
                 // Next Button
                 if (hubTagPage < totalPages - 1)
                 {
                     CreateTabButton(container.transform, "Next Page >>", new Color(0.8f, 0.8f, 0.2f), false, () => {
                         hubTagPage++;
                         UpdateTabs();
                     }, trackedButtons);
                 }

                 // Update Clear Button
                 GameObject clearBtn = isLeft ? leftSubClearBtn : rightSubClearBtn;
                 Text clearBtnText = isLeft ? leftSubClearBtnText : rightSubClearBtnText;
                
                 if (clearBtn != null)
                 {
                    if (currentHubTags.Count > 0)
                    {
                        clearBtn.SetActive(true);
                        if (clearBtnText != null) clearBtnText.text = "Clear Tags (" + currentHubTags.Count + ")";
                    }
                    else
                    {
                        clearBtn.SetActive(false);
                    }
                 }
                 
            }, (err) => {
                 bool stillActive = isLeft ? (leftActiveContent == ContentType.Hub) : (rightActiveContent == ContentType.Hub);
                 if (!stillActive) return;
                 
                 foreach (var btn in trackedButtons) ReturnTabButton(btn);
                 trackedButtons.Clear();
                 CreateTabButton(container.transform, "Error: " + err, Color.red, false, null, trackedButtons);
            });
        }

        public void RefreshHubItems()
        {
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
            refreshCoroutine = StartCoroutine(RefreshHubItemsRoutine());
        }

        private IEnumerator RefreshHubItemsRoutine()
        {
            // Clear existing buttons
            foreach (var btn in activeButtons)
            {
                btn.SetActive(false);
                if (btn.name.StartsWith("NavButton_")) navButtonPool.Push(btn);
                else fileButtonPool.Push(btn);
            }
            activeButtons.Clear();
            fileButtonImages.Clear();

            // Show loading...
            if (paginationText != null) paginationText.text = "Loading Hub...";

            List<GalleryHubItem> items = null;
            string error = null;

            GalleryHubController.Instance.GetResources(currentHubCategory, currentHubCreator, currentHubPayType, currentHubSearch, new List<string>(currentHubTags), currentPage + 1, (result) => {
                items = result;
            }, (err) => {
                error = err;
            });

            // Wait for callback (this is a bit hacky, normally would use a flag)
            // Since GetResources starts a coroutine, we can't yield return it directly unless we refactor Controller.
            // But we can wait for items or error to be non-null.
            
            float timeout = 10f;
            while (items == null && error == null && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (items == null)
            {
                string msg = !string.IsNullOrEmpty(error) ? error : "Request Timeout";
                if (paginationText != null) paginationText.text = "Error: " + msg;
                refreshCoroutine = null;
                yield break;
            }

            int totalFiles = items.Count; // API doesn't return total count in this simple impl, assume items.Count for now
             // To support pagination properly, Controller needs to return total pages/count. 
             // For now, let's just display what we got.

            if (paginationText != null) 
                paginationText.text = "Page " + (currentPage + 1);

            // Pagination Controls logic
            if (paginationPrevBtn != null) 
                paginationPrevBtn.GetComponent<Button>().interactable = (currentPage > 0);
            if (paginationNextBtn != null) 
                paginationNextBtn.GetComponent<Button>().interactable = true; // Assume more pages always for now

            // Render Items
            foreach (var item in items)
            {
                CreateHubItemButton(item);
            }
            
            refreshCoroutine = null;
        }

        private void CreateHubItemButton(GalleryHubItem item)
        {
            GameObject btnGO;
            if (fileButtonPool.Count > 0)
            {
                btnGO = fileButtonPool.Pop();
                btnGO.SetActive(true);
            }
            else
            {
                btnGO = CreateNewFileButtonGO();
            }
            
            btnGO.transform.SetParent(contentGO.transform, false);
            btnGO.name = "HubItem_" + item.ResourceId;
            btnGO.transform.SetAsLastSibling();
            activeButtons.Add(btnGO);

            // Reset State (similar to BindFileButton but for HubItem)
            
            // Image
            Image img = btnGO.GetComponent<Image>();
            if (img != null) img.color = Color.white;
            
            // Thumbnail
            Transform thumbTr = btnGO.transform.Find("Thumbnail");
            RawImage thumbImg = null;
            if (thumbTr != null)
            {
                thumbTr.gameObject.SetActive(true);
                thumbImg = thumbTr.GetComponent<RawImage>();
                if (thumbImg != null)
                {
                    thumbImg.texture = null;
                    thumbImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                }
            }

            // Load Thumbnail
            if (!string.IsNullOrEmpty(item.ThumbnailUrl) && thumbImg != null)
            {
                 CustomImageLoaderThreaded.QueuedImage qi = CustomImageLoaderThreaded.singleton.GetQI();
                 qi.imgPath = item.ThumbnailUrl;
                 qi.callback = (q) => {
                     if (thumbImg != null && q.tex != null)
                     {
                         thumbImg.texture = q.tex;
                         thumbImg.color = Color.white;
                     }
                 };
                 CustomImageLoaderThreaded.singleton.QueueThumbnail(qi);
            }

            // Label
            Transform labelTr = btnGO.transform.Find("Card/Label");
            if (labelTr != null)
            {
                Text labelText = labelTr.GetComponent<Text>();
                if (labelText != null)
                {
                    labelText.text = $"<b>{item.Title}</b>\n<size=14>{item.Creator}</size>";
                }
            }

            // Click Action
            Button btn = btnGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => {
                    // Deselect old file
                    if (selectedFile != null && fileButtonImages.ContainsKey(selectedFile.Path))
                    {
                        if (fileButtonImages[selectedFile.Path] != null)
                            fileButtonImages[selectedFile.Path].color = Color.gray;
                    }

                    selectedFile = null;
                    selectedPath = null;
                    selectedHubItem = item;
                    actionsPanel?.HandleSelectionChanged(selectedFile, selectedHubItem);
                });
            }
            
            // Hide Favorite for now as not implemented for Hub items yet
            Transform favTr = btnGO.transform.Find("Button_Fav");
            if (favTr != null) favTr.gameObject.SetActive(false);

            // Disable HoverReveal for file path since it's a hub item
            UIHoverReveal hover = btnGO.GetComponent<UIHoverReveal>();
            if (hover != null) hover.enabled = false; 
            
            // Show Card immediately or on hover? 
            // The template logic hides card by default and UIHoverReveal shows it.
            // Since we disabled UIHoverReveal, let's enable the card so we can see the title always?
            // Or maybe we want hover behavior.
            // Let's keep hover behavior but update what it shows.
            if (hover != null)
            {
                hover.enabled = true;
                hover.file = null; // No file entry
                // We might need to subclass UIHoverReveal to support Hub Items or just set the label text and let it show.
            }
        }
    }
}
