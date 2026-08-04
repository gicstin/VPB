using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public class QuickFiltersUI
    {
        private const float PanelMaxHeightRef = 500f;
        private const float ScrollBarWidthRef = 14f;
        private const float RowTextPadLeftRef = 12f;
        private const float RowTextPadRightRef = 8f;
        private const float SplitterHeightRef = 8f;

        private GalleryPanel panel;
        private GameObject containerGO;
        private GameObject backdropGO;
        private GameObject scrollContentGO;
        private ScrollRect scrollRect;
        private RectTransform containerRT;
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

            if (visible)
            {
                try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
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
            // Backdrop to close when clicking outside (same near-transparent pattern as CreatePopupMenuRoot).
            backdropGO = UI.CreateChildRT(parent, "QuickFiltersBackdrop", AnchorPresets.stretchAll);
            Image backdropImg = UI.AddImage(backdropGO, new Color(0, 0, 0, 0.001f));
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.targetGraphic = backdropImg;
            backdropBtn.onClick.AddListener(() => SetVisible(false));

            // Dropdown panel — same PopupBackdrop / VLG chrome as language + sort menus.
            containerGO = UI.CreateChildRT(
                parent,
                "QuickFiltersDropdown",
                AnchorPresets.topMiddle,
                new Vector2(GalleryUiDesignTokens.PopupMenuPanelWidthRef, 50f),
                new Vector2(-228f, -72f));
            containerRT = containerGO.GetComponent<RectTransform>();
            UI.AddImage(containerGO, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f));

            scrollRect = containerGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25f;

            GameObject viewport = UI.CreateChildRT(containerGO, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRT = viewport.GetComponent<RectTransform>();
            viewport.AddComponent<RectMask2D>();
            scrollRect.viewport = vpRT;

            GameObject scrollbarGO = UI.CreateScrollBar(containerGO, ScrollBarWidthRef, 0f, Scrollbar.Direction.BottomToTop);
            Scrollbar scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 0.5f);
            sbRT.sizeDelta = new Vector2(ScrollBarWidthRef, 0f);
            scrollRect.verticalScrollbar = null;

            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbar;
            sync.minSizePixels = 20f;

            scrollContentGO = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRT = scrollContentGO.GetComponent<RectTransform>();
            scrollRect.content = contentRT;

            UI.AddVLG(
                scrollContentGO,
                spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef,
                padding: UI.Pad(
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef),
                childAlignment: TextAnchor.UpperCenter,
                childControlHeight: false);

            ContentSizeFitter csf = scrollContentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public void ApplyLayout(float s)
        {
            if (containerGO == null) return;
            if (s <= 0f) s = 1f;

            float panelW = GalleryUiDesignTokens.PopupMenuPanelWidthRef * s;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            float maxH = PanelMaxHeightRef * s;
            int pad = Mathf.RoundToInt(GalleryUiDesignTokens.PopupMenuPaddingRef * s);
            float spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
            float sbW = ScrollBarWidthRef * s;

            VerticalLayoutGroup vlg = scrollContentGO != null ? scrollContentGO.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg != null)
            {
                vlg.padding = new RectOffset(pad, pad, pad, pad);
                vlg.spacing = spacing;
            }

            float splitH = SplitterHeightRef * s;
            float textPadL = RowTextPadLeftRef * s;
            float textPadR = RowTextPadRightRef * s;
            int rowCount = 0;
            bool hasSplitter = false;

            if (scrollContentGO != null)
            {
                for (int i = 0; i < scrollContentGO.transform.childCount; i++)
                {
                    Transform ch = scrollContentGO.transform.GetChild(i);
                    if (ch == null) continue;
                    if (ch.name == "Splitter")
                    {
                        hasSplitter = true;
                        LayoutElement splitLe = ch.GetComponent<LayoutElement>();
                        if (splitLe != null) splitLe.preferredHeight = splitH;
                        RectTransform splitRT = ch as RectTransform;
                        if (splitRT != null)
                            splitRT.sizeDelta = new Vector2(panelW - pad * 2f - sbW, splitH);
                        Transform line = ch.Find("Line");
                        if (line != null)
                        {
                            RectTransform lineRT = line as RectTransform;
                            if (lineRT != null)
                                lineRT.sizeDelta = new Vector2(-20f * s, Mathf.Max(1f, 1f * s));
                        }
                        continue;
                    }

                    rowCount++;
                    LayoutElement le = ch.GetComponent<LayoutElement>();
                    if (le != null) le.preferredHeight = rowH;

                    RectTransform rowRT = ch as RectTransform;
                    if (rowRT != null)
                        rowRT.sizeDelta = new Vector2(panelW - pad * 2f - sbW, rowH);

                    float editReserve = (editMode && ch.Find("RenameBtn") != null) ? 78f * s : 0f;
                    ApplyPopupRowTextInsets(ch, textPadL, textPadR, editReserve);
                    Text t = ch.Find("Text") != null ? ch.Find("Text").GetComponent<Text>() : null;
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);

                    // Edit-mode icon squares track row height.
                    float sq = Mathf.Max(18f, rowH - 6f * s);
                    float padR = 4f * s;
                    float gap = 4f * s;
                    Transform renameT = ch.Find("RenameBtn");
                    Transform deleteT = ch.Find("DeleteBtn");
                    if (renameT != null)
                    {
                        RectTransform irt = renameT as RectTransform;
                        if (irt != null)
                        {
                            irt.sizeDelta = new Vector2(sq, sq);
                            irt.anchoredPosition = new Vector2(-(padR + sq + gap), 0f);
                        }
                    }
                    if (deleteT != null)
                    {
                        RectTransform irt = deleteT as RectTransform;
                        if (irt != null)
                        {
                            irt.sizeDelta = new Vector2(sq, sq);
                            irt.anchoredPosition = new Vector2(-padR, 0f);
                        }
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContentGO.GetComponent<RectTransform>());
            }

            // Splitter is thin — do not count it as a full row (that left empty gap under Edit).
            int childCount = scrollContentGO != null ? scrollContentGO.transform.childCount : 0;
            float contentH = pad * 2f
                + rowCount * rowH
                + (hasSplitter ? splitH : 0f)
                + Mathf.Max(0, childCount - 1) * spacing;
            RectTransform contentRT = scrollContentGO != null ? scrollContentGO.GetComponent<RectTransform>() : null;
            if (contentRT != null && contentRT.rect.height > 1f)
                contentH = contentRT.rect.height;
            float panelH = Mathf.Min(maxH, contentH);

            if (containerRT != null)
                containerRT.sizeDelta = new Vector2(panelW, panelH);

            Transform vp = containerGO.transform.Find("Viewport");
            if (vp != null)
            {
                RectTransform vpRT = vp as RectTransform;
                if (vpRT != null)
                {
                    vpRT.offsetMin = new Vector2(0f, 0f);
                    vpRT.offsetMax = new Vector2(-sbW, 0f);
                }
            }

            Transform sb = containerGO.transform.Find("Scrollbar");
            if (sb != null)
            {
                RectTransform sbRT = sb as RectTransform;
                if (sbRT != null)
                    sbRT.sizeDelta = new Vector2(sbW, 0f);
            }

            // Anchor under filter-presets title-bar chip (same gap as language menu).
            if (containerRT != null && panel != null)
            {
                RectTransform btnRT = panel.QuickFiltersToggleBtnRT;
                containerRT.anchorMin = new Vector2(0.5f, 1f);
                containerRT.anchorMax = new Vector2(0.5f, 1f);
                containerRT.pivot = new Vector2(0.5f, 1f);
                float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
                float x = btnRT != null ? btnRT.anchoredPosition.x : -228f * s;
                containerRT.anchoredPosition = new Vector2(
                    x,
                    -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
            }
        }

        public void Refresh()
        {
            // Clear existing
            foreach (var btn in activeButtons) GameObject.Destroy(btn);
            activeButtons.Clear();
            buttonToEntry.Clear();

            // 1. Save Preset Button - Index 0
            CreateSaveButton();

            // 2. Edit Presets Button - Index 1
            CreateEditButton();

            // 3. Splitter - Index 2
            CreateSplitter();

            // 4. Existing Filters - Index 3+
            foreach (var filter in QuickFilterSettings.Instance.Filters)
            {
                CreateFilterButton(filter);
            }

            // Ensure correct layer for new items
            if (containerGO != null)
                SetLayerRecursive(containerGO, containerGO.layer);

            try { ApplyLayout(panel != null ? panel.ChromeScale : 1f); } catch { }
        }

        private static void ApplyPopupRowTextInsets(Transform row, float padLeft, float padRight, float editIconsReserve = 0f)
        {
            if (row == null) return;
            Transform textT = row.Find("Text");
            if (textT == null) return;
            RectTransform txtRT = textT as RectTransform;
            if (txtRT == null) return;
            float right = editIconsReserve > 0f ? Mathf.Max(padRight, editIconsReserve) : padRight;
            txtRT.offsetMin = new Vector2(padLeft, 0f);
            txtRT.offsetMax = new Vector2(-right, 0f);
        }

        private void CreateSaveButton()
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.PopupMenuPanelWidthRef - 12f;
            GameObject btn = UI.CreateUIButton(
                scrollContentGO,
                rowW,
                rowH,
                VPBTranslation.T("quickfilters.save_preset", "Save Preset"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                () => { CaptureCurrentFilter(); });

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = UI.PopupRowBackdrop;
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.alignment = TextAnchor.MiddleLeft;
                txt.color = UI.PopupText;
                VPBUiFont.ApplyTo(txt);
            }
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef);
            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

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

            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.PopupMenuPanelWidthRef - 12f;
            GameObject btn = UI.CreateUIButton(
                scrollContentGO,
                rowW,
                rowH,
                label,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                () =>
                {
                    editMode = !editMode;
                    Refresh();
                });

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = editMode ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.alignment = TextAnchor.MiddleLeft;
                txt.color = editMode ? UI.PopupText : UI.PopupMutedText;
                VPBUiFont.ApplyTo(txt);
            }
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef);
            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

            activeButtons.Add(btn);
        }

        private void CreateSplitter()
        {
            GameObject splitter = new GameObject("Splitter");
            splitter.transform.SetParent(scrollContentGO.transform, false);
            var splitRT = splitter.AddComponent<RectTransform>();
            splitRT.sizeDelta = new Vector2(0f, SplitterHeightRef);
            UI.AddLE(splitter, preferredHeight: SplitterHeightRef, flexibleWidth: 1f);

            GameObject line = UI.CreateChildRT(splitter, "Line", AnchorPresets.hStretchMiddle, new Vector2(-20f, 1f));
            UI.AddImage(line, new Color(0.5f, 0.5f, 0.5f, 0.35f));

            activeButtons.Add(splitter);
        }

        private void CreateFilterButton(QuickFilterEntry entry)
        {
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            float rowW = GalleryUiDesignTokens.PopupMenuPanelWidthRef - 12f;
            GameObject btn = UI.CreateUIButton(
                scrollContentGO,
                rowW,
                rowH,
                entry.Name,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                null);
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
            if (img != null) img.color = entry.ButtonColor;

            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                txt.color = entry.TextColor;
                txt.alignment = TextAnchor.MiddleLeft;
                VPBUiFont.ApplyTo(txt);
            }
            ApplyPopupRowTextInsets(btn.transform, RowTextPadLeftRef, RowTextPadRightRef, editMode ? 78f : 0f);

            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);

            // Reorderable (allow drag in edit mode too)
            var reorder = btn.AddComponent<UIListReorderable>();
            reorder.target = btn.GetComponent<RectTransform>();
            reorder.minIndex = 3; // Below Save, Edit, Splitter
            reorder.OnReorder = SyncFiltersFromUI;

            if (editMode)
            {
                float sq = 26f;
                float padR = 4f;
                float gap = 4f;

                Sprite sprRename = UI.LoadIconSprite("vpb_icons/rename.png", Color.white);
                Sprite sprDelete = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);

                GameObject renameBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                GameObject deleteBtn = UI.CreateUIButton(btn, sq, sq, " ", 16, 0, 0, AnchorPresets.middleRight, null);
                if (renameBtn != null) renameBtn.name = "RenameBtn";
                if (deleteBtn != null) deleteBtn.name = "DeleteBtn";
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

            var rightClick = btn.AddComponent<UIRightClickDelegate>();
            rightClick.OnRightClick = () => {
                ShowContextMenu(btn, entry);
            };

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

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.rename", "Rename"),
                VPBTranslation.T("quickfilters.ctx.rename_sub", "Change filter name"),
                () => {
                    panel.DisplayTextInput(VPBTranslation.T("quickfilters.rename_title", "Rename Filter"), entry.Name, (string val) => {
                        if (!string.IsNullOrEmpty(val))
                        {
                            QuickFilterSettings.Instance.RenameFilter(entry, val);
                            Refresh();
                        }
                    });
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.change_color", "Change Color"),
                VPBTranslation.T("quickfilters.ctx.color_sub", "Button tint"),
                () => {
                    panel.DisplayColorPicker(VPBTranslation.T("quickfilters.edit_color_title", "Edit Color"), entry.ButtonColor, (Color val) => {
                        entry.ButtonColor = val;
                        QuickFilterSettings.Instance.Save();
                        Refresh();
                    });
                },
                ContextMenuPanel.OptionKind.Normal));

            options.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("quickfilters.ctx.delete", "Delete"),
                VPBTranslation.T("quickfilters.ctx.delete_sub", "Remove this quick filter"),
                () => {
                    panel.DisplayConfirm(VPBTranslation.T("quickfilters.delete_title", "Delete Filter"), string.Format(VPBTranslation.T("quickfilters.delete_confirm", "Are you sure you want to delete '{0}'?"), entry.Name), () => {
                        QuickFilterSettings.Instance.RemoveFilter(entry);
                        Refresh();
                    });
                },
                ContextMenuPanel.OptionKind.Destructive));

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
