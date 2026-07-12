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
        private bool editMode;

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
            backdropGO = UI.CreateChildRT(parent, "QuickFiltersBackdrop", AnchorPresets.stretchAll);
            Image backdropImg = UI.AddImage(backdropGO, new Color(0, 0, 0, 0)); // Transparent but raycast target
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.onClick.AddListener(() => SetVisible(false));

            // Dropdown container
            // Aligned under title-bar P (see title bar anchoredPosition = -228)
            containerGO = UI.CreateChildRT(parent, "QuickFiltersDropdown", AnchorPresets.topMiddle, new Vector2(330, 500), new Vector2(-228, -70));

            Image bgImg = UI.AddImage(containerGO, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f));

            // Scroll View
            ScrollRect scrollRect = containerGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25;

            // Viewport
            GameObject viewport = UI.CreateChildRT(containerGO, "Viewport", AnchorPresets.stretchAll, new Vector2(-18f, -20f), new Vector2(-9f, -10f));
            RectTransform vpRT = viewport.GetComponent<RectTransform>();
            viewport.AddComponent<RectMask2D>();
            
            scrollRect.viewport = vpRT;

            // Scrollbar
            float scrollBarWidth = 18f;
            GameObject scrollbarGO = UI.CreateScrollBar(containerGO, scrollBarWidth, 0f, Scrollbar.Direction.BottomToTop);
            Scrollbar scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1, 0);
            sbRT.anchorMax = new Vector2(1, 1);
            sbRT.pivot = new Vector2(1, 0.5f);
            sbRT.sizeDelta = new Vector2(scrollBarWidth, 0);

            scrollRect.verticalScrollbar = null; // Decouple
            
            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbar;
            sync.minSizePixels = 20f;

            // Content
            scrollContentGO = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRT = scrollContentGO.GetComponent<RectTransform>();
            
            scrollRect.content = contentRT;

            // Vertical Layout Group
            VerticalLayoutGroup vlg = UI.AddVLG(scrollContentGO, spacing: 2, padding: UI.Pad(10, 10, 10, 10), childAlignment: TextAnchor.UpperCenter, childControlHeight: false);

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

            // 2. Edit Presets Button - Index 1
            CreateEditButton();

            // 3. Splitter - Index 2
            CreateSplitter();

            // 4. Existing Filters - Index 3+
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
            GameObject btn = UI.CreateUIButton(scrollContentGO, 240, 50, VPBTranslation.T("quickfilters.save_preset", "Save Preset"), 20, 0, 0, AnchorPresets.middleCenter, () => {
                CaptureCurrentFilter();
                // Removed SetVisible(false) so list stays open
            });
            btn.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.2f, 1f);
            
            var le = UI.AddLE(btn, preferredHeight: 50);

            // Hover tooltip (status line)
            var del = btn.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) =>
            {
                if (panel == null) return;
                if (enter) panel.SetStatus(VPBTranslation.T("quickfilters.tip.save", "Capture current filters into new preset (added to list below)."));
                else panel.SetStatus(null);
            };

            activeButtons.Add(btn);
        }

        private void CreateEditButton()
        {
            string label = editMode
                ? VPBTranslation.T("quickfilters.done_editing", "Done Editing")
                : VPBTranslation.T("quickfilters.edit_presets", "Edit Presets");

            GameObject btn = UI.CreateUIButton(scrollContentGO, 240, 45, label, 18, 0, 0, AnchorPresets.middleCenter, () =>
            {
                editMode = !editMode;
                Refresh();
            });

            var img = btn.GetComponent<Image>();
            if (img != null) img.color = editMode ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

            var le = UI.AddLE(btn, preferredHeight: 45);

            activeButtons.Add(btn);
        }

        private void CreateSplitter()
        {
            GameObject splitter = new GameObject("Splitter");
            splitter.transform.SetParent(scrollContentGO.transform, false);
            var rt = splitter.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 15);
            
            var le = UI.AddLE(splitter, preferredHeight: 15);

            GameObject line = UI.CreateChildRT(splitter, "Line", AnchorPresets.hStretchMiddle, new Vector2(-10, 1));

            var img = UI.AddImage(line, new Color(0.5f, 0.5f, 0.5f, 0.3f));

            activeButtons.Add(splitter);
        }

        private void CreateFilterButton(QuickFilterEntry entry)
        {
            GameObject btn = UI.CreateUIButton(scrollContentGO, 240, 45, entry.Name, 18, 0, 0, AnchorPresets.middleCenter, null);
            var b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() =>
                {
                    if (editMode)
                    {
                        ShowContextMenu(btn, entry);
                        return;
                    }

                    ApplyFilter(entry);
                    SetVisible(false); // Close dropdown on action
                });
            }
            
            Image img = btn.GetComponent<Image>();
            img.color = entry.ButtonColor;
            
            Text txt = btn.GetComponentInChildren<Text>();
            txt.color = entry.TextColor;
            txt.alignment = TextAnchor.MiddleLeft;
            RectTransform txtRT = txt.GetComponent<RectTransform>();
            txtRT.offsetMin = new Vector2(15, 0); // More indent for text
            if (editMode)
                txtRT.offsetMax = new Vector2(-78, 0); // space for icon buttons

            var le = UI.AddLE(btn, preferredHeight: 45);

            // Reorderable (allow drag in edit mode too)
            var reorder = btn.AddComponent<UIListReorderable>();
            reorder.target = btn.GetComponent<RectTransform>();
            reorder.minIndex = 3; // Below Save, Edit, Splitter
            reorder.OnReorder = SyncFiltersFromUI;

            if (editMode)
            {
                float sq = 32f;
                float padR = 6f;
                float gap = 6f;

                Sprite sprRename = UI.LoadIconSprite("vpb_icons/rename.png", Color.white);
                Sprite sprDelete = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);

                GameObject renameBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject deleteBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (renameBtn != null && deleteBtn != null)
                {
                    void setupSquare(GameObject go, Sprite icon, Color iconTint, float x, Color backdrop)
                    {
                        if (go == null) return;
                        var img2 = go.GetComponent<Image>();
                        if (img2 != null) img2.color = backdrop;
                        var t = go.GetComponentInChildren<Text>(true);
                        if (t != null) t.gameObject.SetActive(false);
                        var rt2 = go.GetComponent<RectTransform>();
                        if (rt2 != null)
                        {
                            rt2.anchorMin = new Vector2(1, 0.5f);
                            rt2.anchorMax = new Vector2(1, 0.5f);
                            rt2.pivot = new Vector2(1, 0.5f);
                            rt2.anchoredPosition = new Vector2(x, 0f);
                            rt2.sizeDelta = new Vector2(sq, sq);
                        }
                        if (icon != null)
                        {
                            UI.AddIconToButton(go, icon, padding: 6f);
                            var iconImg = go.transform.Find("Icon") != null ? go.transform.Find("Icon").GetComponent<Image>() : null;
                            if (iconImg != null) iconImg.color = iconTint;
                        }
                    }

                    Color renameBackdrop = new Color(0.20f, 0.26f, 0.34f, 1f);
                    Color deleteBackdrop = new Color(0.45f, 0.22f, 0.22f, 1f);
                    setupSquare(deleteBtn, sprDelete, Color.white, -padR, deleteBackdrop);
                    setupSquare(renameBtn, sprRename, Color.white, -(padR + sq + gap), renameBackdrop);

                    // Drag-reorder should work even if user grabs icon buttons.
                    void addReorderHandle(GameObject handle)
                    {
                        if (handle == null) return;
                        var h = handle.AddComponent<UIListReorderable>();
                        h.target = reorder.target;
                        h.minIndex = reorder.minIndex;
                        h.OnReorder = reorder.OnReorder;
                    }
                    addReorderHandle(renameBtn);
                    addReorderHandle(deleteBtn);

                    var rb = renameBtn.GetComponent<Button>();
                    if (rb != null)
                    {
                        rb.onClick.RemoveAllListeners();
                        rb.onClick.AddListener(() =>
                        {
                            if (panel == null) return;
                            panel.DisplayTextInput(
                                VPBTranslation.T("quickfilters.rename_title", "Rename Filter"),
                                entry.Name,
                                (string val) =>
                                {
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        QuickFilterSettings.Instance.RenameFilter(entry, val);
                                        Refresh();
                                    }
                                });
                        });
                    }

                    var db = deleteBtn.GetComponent<Button>();
                    if (db != null)
                    {
                        db.onClick.RemoveAllListeners();
                        db.onClick.AddListener(() =>
                        {
                            if (panel == null) return;
                            panel.DisplayConfirm(
                                VPBTranslation.T("quickfilters.delete_title", "Delete Filter"),
                                string.Format(VPBTranslation.T("quickfilters.delete_confirm", "Are you sure you want to delete '{0}'?"), entry.Name),
                                () =>
                                {
                                    QuickFilterSettings.Instance.RemoveFilter(entry);
                                    Refresh();
                                });
                        });
                    }

                    // Hover tooltips (status line)
                    var rh = renameBtn.AddComponent<UIHoverDelegate>();
                    rh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(string.Format(VPBTranslation.T("quickfilters.tip.rename", "Rename '{0}'"), entry.Name));
                        else panel.SetStatus(null);
                    };
                    var dh = deleteBtn.AddComponent<UIHoverDelegate>();
                    dh.OnHoverChange += (enter) =>
                    {
                        if (panel == null) return;
                        if (enter) panel.SetStatus(string.Format(VPBTranslation.T("quickfilters.tip.delete", "Delete '{0}'"), entry.Name));
                        else panel.SetStatus(null);
                    };
                }
            }

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
                    string info = editMode
                        ? string.Format(VPBTranslation.T("quickfilters.edit_hint", "Edit '{0}' (Drag to reorder. Use icons for rename/delete.)"), entry.Name)
                        : string.Format(VPBTranslation.T("quickfilters.apply_hint", "Apply '{0}' (Drag to reorder. Edit mode for rename/delete.)"), entry.Name);
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
            
            // Siblings at index 3+ are the filters (0=Save, 1=Edit, 2=Splitter)
            for (int i = 3; i < scrollContentGO.transform.childCount; i++)
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
            
            options.Add(new ContextMenuPanel.Option(VPBTranslation.T("quickfilters.ctx.rename", "Rename"), () => {
                panel.DisplayTextInput(VPBTranslation.T("quickfilters.rename_title", "Rename Filter"), entry.Name, (string val) => {
                    if (!string.IsNullOrEmpty(val))
                    {
                        QuickFilterSettings.Instance.RenameFilter(entry, val);
                        Refresh();
                    }
                });
            }));

            options.Add(new ContextMenuPanel.Option(VPBTranslation.T("quickfilters.ctx.change_color", "Change Color"), () => {
                panel.DisplayColorPicker(VPBTranslation.T("quickfilters.edit_color_title", "Edit Color"), entry.ButtonColor, (Color val) => {
                    entry.ButtonColor = val;
                    QuickFilterSettings.Instance.Save();
                    Refresh();
                });
            }));

            options.Add(new ContextMenuPanel.Option(VPBTranslation.T("quickfilters.ctx.delete", "Delete"), () => {
                panel.DisplayConfirm(VPBTranslation.T("quickfilters.delete_title", "Delete Filter"), string.Format(VPBTranslation.T("quickfilters.delete_confirm", "Are you sure you want to delete '{0}'?"), entry.Name), () => {
                    QuickFilterSettings.Instance.RemoveFilter(entry);
                    Refresh();
                });
            }));

            ContextMenuPanel.Instance.Show(btn.transform.position, options, VPBTranslation.T("quickfilters.ctx.header_prefix", "Filter: ") + entry.Name);
        }

        public void RefreshLocalizedUi()
        {
            Refresh();
            if (containerGO != null)
            {
                foreach (Text t in containerGO.GetComponentsInChildren<Text>(true))
                    VPBUiFont.ApplyTo(t);
            }
        }
    }
}
