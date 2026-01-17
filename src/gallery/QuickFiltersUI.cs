using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class QuickFiltersUI
    {
        private GalleryPanel panel;
        private GameObject containerGO;
        private GameObject backdropGO;
        private GameObject scrollContentGO;
        private List<GameObject> activeButtons = new List<GameObject>();
        private Dictionary<GameObject, QuickFilterEntry> buttonToEntry = new Dictionary<GameObject, QuickFilterEntry>();

        public QuickFiltersUI(GalleryPanel panel, GameObject parent)
        {
            this.panel = panel;
            CreateUI(parent);
            SetLayerRecursive(containerGO, parent.layer);
            SetLayerRecursive(backdropGO, parent.layer);
            SetVisible(false); // Default hidden
            Refresh();
        }

        private void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        public GameObject ContainerGO => containerGO;

        public void SetVisible(bool visible)
        {
            if (backdropGO != null)
            {
                backdropGO.SetActive(visible);
                if (visible) backdropGO.transform.SetAsLastSibling();
            }
            if (containerGO != null) 
            {
                containerGO.SetActive(visible);
                if (visible) containerGO.transform.SetAsLastSibling();
            }
            
            // Sync toggle button color if needed
            if (panel != null && !visible)
            {
                // This ensures if we close via backdrop, the button color resets
                panel.SyncQuickFilterToggleState();
            }
        }

        public bool IsVisible => containerGO != null && containerGO.activeSelf;

        private void CreateUI(GameObject parent)
        {
            // Backdrop to close when clicking outside
            backdropGO = new GameObject("QuickFiltersBackdrop");
            backdropGO.transform.SetParent(parent.transform, false);
            RectTransform backdropRT = backdropGO.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.sizeDelta = Vector2.zero;
            Image backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0, 0, 0, 0); // Transparent but raycast target
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.onClick.AddListener(() => SetVisible(false));

            // Dropdown container
            containerGO = new GameObject("QuickFiltersDropdown");
            containerGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = containerGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            // Positioned below the "Filter Presets" button (now at -240)
            rt.anchoredPosition = new Vector2(-240, -105); 
            rt.sizeDelta = new Vector2(260, 400); // Larger width and height for VR
            
            Image bgImg = containerGO.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            
            // Add a subtle border
            var outline = containerGO.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            outline.effectDistance = new Vector2(1, -1);

            // Scroll View
            ScrollRect scrollRect = containerGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25;

            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(containerGO.transform, false);
            RectTransform vpRT = viewport.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.sizeDelta = new Vector2(-10, -10); // padding
            vpRT.pivot = new Vector2(0.5f, 1);
            viewport.AddComponent<RectMask2D>();
            
            scrollRect.viewport = vpRT;

            // Content
            scrollContentGO = new GameObject("Content");
            scrollContentGO.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = scrollContentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);
            
            scrollRect.content = contentRT;

            // Vertical Layout Group
            VerticalLayoutGroup vlg = scrollContentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4; // Slightly more spacing
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = scrollContentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public void Refresh()
        {
            // Clear existing
            foreach(var btn in activeButtons) GameObject.Destroy(btn);
            activeButtons.Clear();
            buttonToEntry.Clear();

            // 1. Save Preset Button (Green) - Index 0
            CreateSaveButton();

            // 2. Splitter - Index 1
            CreateSplitter();

            // 3. Existing Filters - Index 2+
            foreach(var filter in QuickFilterSettings.Instance.Filters)
            {
                CreateFilterButton(filter);
            }

            // Ensure correct layer for new items
            if (containerGO != null)
                SetLayerRecursive(containerGO, containerGO.layer);
        }

        private void CreateSaveButton()
        {
            GameObject btn = UI.CreateUIButton(scrollContentGO, 240, 50, "Save Preset", 20, 0, 0, AnchorPresets.middleCenter, () => {
                CaptureCurrentFilter();
                // Removed SetVisible(false) so list stays open
            });
            btn.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.2f, 1f);
            
            var le = btn.AddComponent<LayoutElement>();
            le.preferredHeight = 50;

            activeButtons.Add(btn);
        }

        private void CreateSplitter()
        {
            GameObject splitter = new GameObject("Splitter");
            splitter.transform.SetParent(scrollContentGO.transform, false);
            var rt = splitter.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 15);
            
            var le = splitter.AddComponent<LayoutElement>();
            le.preferredHeight = 15;

            GameObject line = new GameObject("Line");
            line.transform.SetParent(splitter.transform, false);
            var lineRT = line.AddComponent<RectTransform>();
            lineRT.anchorMin = new Vector2(0, 0.5f);
            lineRT.anchorMax = new Vector2(1, 0.5f);
            lineRT.sizeDelta = new Vector2(-10, 1);
            
            var img = line.AddComponent<Image>();
            img.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

            activeButtons.Add(splitter);
        }

        private void CreateFilterButton(QuickFilterEntry entry)
        {
            GameObject btn = UI.CreateUIButton(scrollContentGO, 240, 45, entry.Name, 18, 0, 0, AnchorPresets.middleCenter, () => {
                ApplyFilter(entry);
                SetVisible(false); // Close dropdown on action
            });
            
            Image img = btn.GetComponent<Image>();
            img.color = entry.ButtonColor;
            
            Text txt = btn.GetComponentInChildren<Text>();
            txt.color = entry.TextColor;
            txt.alignment = TextAnchor.MiddleLeft;
            RectTransform txtRT = txt.GetComponent<RectTransform>();
            txtRT.offsetMin = new Vector2(15, 0); // More indent for text

            var le = btn.AddComponent<LayoutElement>();
            le.preferredHeight = 45;

            // Reorderable
            var reorder = btn.AddComponent<UIListReorderable>();
            reorder.target = btn.GetComponent<RectTransform>();
            reorder.minIndex = 2; // Below Save and Splitter
            reorder.OnReorder = SyncFiltersFromUI;

            // Right click to manage
            var rightClick = btn.AddComponent<UIRightClickDelegate>();
            rightClick.OnRightClick = () => {
                ShowContextMenu(btn, entry);
            };

            // Tooltip
            var del = btn.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) => {
                if (enter && panel != null) 
                {
                    string info = $"Apply '{entry.Name}' (Drag to reorder, Right-click to manage)";
                    panel.SetStatus(info);
                }
                else if (panel != null) panel.SetStatus(null);
            };

            activeButtons.Add(btn);
            buttonToEntry[btn] = entry;
        }

        private void SyncFiltersFromUI()
        {
            // Gather all filter entries based on current sibling index
            var newList = new List<QuickFilterEntry>();
            
            // Siblings at index 2+ are the filters
            for (int i = 2; i < scrollContentGO.transform.childCount; i++)
            {
                GameObject go = scrollContentGO.transform.GetChild(i).gameObject;
                if (buttonToEntry.TryGetValue(go, out QuickFilterEntry entry))
                {
                    newList.Add(entry);
                }
            }
            
            QuickFilterSettings.Instance.Filters = newList;
            QuickFilterSettings.Instance.Save();
        }

        private void CaptureCurrentFilter()
        {
            if (panel == null) return;

            var entry = panel.CaptureQuickFilterState();
            if (entry != null)
            {
                QuickFilterSettings.Instance.AddFilter(entry);
                Refresh();
            }
        }

        private void ApplyFilter(QuickFilterEntry entry)
        {
            if (panel == null) return;
            panel.ApplyQuickFilterState(entry);
        }

        private void ShowContextMenu(GameObject btn, QuickFilterEntry entry)
        {
            var options = new List<ContextMenuPanel.Option>();
            
            options.Add(new ContextMenuPanel.Option("Rename", () => {
                panel.DisplayTextInput("Rename Filter", entry.Name, (string val) => {
                    if (!string.IsNullOrEmpty(val))
                    {
                        QuickFilterSettings.Instance.RenameFilter(entry, val);
                        Refresh();
                    }
                });
            }));

            options.Add(new ContextMenuPanel.Option("Change Color", () => {
                panel.DisplayColorPicker("Edit Color", entry.ButtonColor, (Color val) => {
                    entry.ButtonColor = val;
                    QuickFilterSettings.Instance.Save();
                    Refresh();
                });
            }));

            options.Add(new ContextMenuPanel.Option("Delete", () => {
                panel.DisplayConfirm("Delete Filter", $"Are you sure you want to delete '{entry.Name}'?", () => {
                    QuickFilterSettings.Instance.RemoveFilter(entry);
                    Refresh();
                });
            }));

            ContextMenuPanel.Instance.Show(btn.transform.position, options, "Filter: " + entry.Name);
        }
    }
}
