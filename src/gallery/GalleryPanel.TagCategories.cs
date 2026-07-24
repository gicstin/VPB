using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    // US-02: color-coded named tag categories. A tag may belong to one category; the category owns a
    // display color used to tint the resting (inactive) state of its rows in the Tag pane, and shown as a
    // swatch in the Tag Editor list. Category assign + manage live as nested chrome centered on the
    // floating tag editor panel (popup tokens + hover), not a separate full-canvas modal.
    public partial class GalleryPanel
    {
        private Dictionary<string, Color> _userTagCategoryColorByTag;
        private bool _userTagCategoryColorCacheValid;

        private GameObject _tagCategoryModalGo;

        private static readonly Color TagCategoryDefaultNewColor = new Color(0.30f, 0.42f, 0.55f, 1f);

        private void InvalidateUserTagCategoryColorCache()
        {
            _userTagCategoryColorCacheValid = false;
        }

        private void EnsureUserTagCategoryColorCache()
        {
            if (_userTagCategoryColorCacheValid && _userTagCategoryColorByTag != null) return;
            if (_userTagCategoryColorByTag == null)
                _userTagCategoryColorByTag = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            else
                _userTagCategoryColorByTag.Clear();

            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (VpbLocalDatabase.TryReadGalleryUserTagColorMap(raw))
                {
                    foreach (var kv in raw)
                    {
                        Color c;
                        if (UIColorPicker.TryParseFlexibleColor(kv.Value, Color.white, out c))
                            _userTagCategoryColorByTag[kv.Key] = c;
                    }
                }
            }
            catch { }
            _userTagCategoryColorCacheValid = true;
        }

        /// <summary>Category color for a tag's resting row state / editor swatch, or null when the tag has no category.</summary>
        private Color? TryGetUserTagCategoryColor(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return null;
            EnsureUserTagCategoryColorCache();
            Color c;
            if (_userTagCategoryColorByTag != null && _userTagCategoryColorByTag.TryGetValue(tagName, out c))
                return c;
            return null;
        }

        private static string ColorToHexRgb(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        private void CloseTagCategoryEditorModal()
        {
            if (_tagCategoryModalGo != null)
            {
                UnityEngine.Object.Destroy(_tagCategoryModalGo);
                _tagCategoryModalGo = null;
            }
        }

        /// <summary>Invalidate color cache and refresh every surface that shows category color.</summary>
        private void AfterTagCategoryChange()
        {
            InvalidateUserTagCategoryColorCache();
            try { RebuildUserTagEditorRows(); } catch { }
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            try { RefreshUserTagsAvailPaneInPlace(true); } catch { }
            try { RefreshUserTagsAvailPaneInPlace(false); } catch { }
        }

        // ---- Tag Editor "Category" action ------------------------------------------------

        private void UserTagEditorOpenCategoryDialog()
        {
            if (_userTagEditorRowSelection == null || _userTagEditorRowSelection.Count == 0)
            {
                try { ShowTemporaryStatus(VPBTranslation.T("gallery.tagcat.need_selection", "Select one or more tags first")); } catch { }
                return;
            }
            OpenTagCategoryAssignView();
        }

        private void AssignCategoryToEditorSelection(long categoryId)
        {
            if (_userTagEditorRowSelection == null) return;
            var names = new List<string>(_userTagEditorRowSelection);
            VpbLocalDatabase.TryAssignGalleryUserTagCategoryBatch(names, categoryId);
            AfterTagCategoryChange();
        }

        // ---- Import / export round-trip --------------------------------------------------

        /// <summary>Snapshot every category (name + color) with the tags currently assigned to it, for YAML export.</summary>
        private List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> BuildUserTagCategoryExportList()
        {
            var outList = new List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml>();
            var cats = new List<VpbLocalDatabase.GalleryUserTagCategoryInfo>();
            if (!VpbLocalDatabase.TryListGalleryUserTagCategories(cats) || cats.Count == 0)
                return outList;

            var assign = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            VpbLocalDatabase.TryReadGalleryUserTagCategoryAssignments(assign);

            var tagsByCat = new Dictionary<long, List<string>>();
            foreach (var kv in assign)
            {
                if (!tagsByCat.TryGetValue(kv.Value, out List<string> lst))
                {
                    lst = new List<string>();
                    tagsByCat[kv.Value] = lst;
                }
                lst.Add(kv.Key);
            }

            for (int i = 0; i < cats.Count; i++)
            {
                VpbLocalDatabase.GalleryUserTagCategoryInfo ci = cats[i];
                List<string> tags;
                if (!tagsByCat.TryGetValue(ci.Id, out tags)) tags = new List<string>();
                outList.Add(new GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml
                {
                    Name = ci.Name,
                    Color = ci.Color,
                    Tags = tags
                });
            }
            return outList;
        }

        /// <summary>Create/update categories (name+color) from an import and assign their tags. Returns number of categories touched.</summary>
        private int UserTagEditorApplyImportedCategories(List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories)
        {
            if (categories == null || categories.Count == 0) return 0;

            var existing = new List<VpbLocalDatabase.GalleryUserTagCategoryInfo>();
            VpbLocalDatabase.TryListGalleryUserTagCategories(existing);
            var idByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < existing.Count; i++)
                idByName[existing[i].Name] = existing[i].Id;

            int applied = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml cat = categories[i];
                string name = VpbLocalDatabase.NormalizeGalleryUserTagCategoryName(cat.Name);
                if (string.IsNullOrEmpty(name)) continue;

                string color = VpbLocalDatabase.NormalizeGalleryUserTagCategoryColor(cat.Color);
                if (string.IsNullOrEmpty(color)) color = ColorToHexRgb(TagCategoryDefaultNewColor);

                long id;
                if (idByName.TryGetValue(name, out id))
                {
                    VpbLocalDatabase.TrySetGalleryUserTagCategoryColor(id, color);
                }
                else
                {
                    if (!VpbLocalDatabase.TryCreateGalleryUserTagCategory(name, color, out id) || id < 0)
                        continue;
                    idByName[name] = id;
                }

                if (cat.Tags != null && cat.Tags.Count > 0)
                    VpbLocalDatabase.TryAssignGalleryUserTagCategoryBatch(cat.Tags, id);
                applied++;
            }
            return applied;
        }

        // ---- Modal chrome ----------------------------------------------------------------

        /// <summary>
        /// Category assign/manage shell: centered on floating tag editor panel, popup tokens + hover
        /// (Jakob: match tag-menu chrome; Fitts: dense scaled rows).
        /// </summary>
        private GameObject BuildTagCategoryModalShell(string title, out Transform rowsParent, out int bodyFont, out float rowH)
        {
            rowsParent = null;
            bodyFont = GalleryUiDesignTokens.PopupMenuRowFontRef;
            rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef;

            Transform host = DetailStripTagMenuCategoryModalHost();
            if (host == null) return null;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            bodyFont = GalleryUiMetrics.ScaledFontSize(
                GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
            float panelW = 440f * s;

            GameObject panel;
            // Opaque work-surface fill — same family as DetailStripTagMenu panel.
            Color panelBg = new Color(0.08f, 0.08f, 0.10f, 1f);
            GameObject overlay = UI.CreateModalChrome(
                host.gameObject, "VPB_TagCategoryModal", panelW, 80f * s,
                panelBg, CloseTagCategoryEditorModal, out panel, dimAlpha: 0.45f);
            // Swallow clicks on the panel so they do not fall through to the dim close button.
            Button pbtn = panel.AddComponent<Button>();
            pbtn.transition = Selectable.Transition.None;

            UI.AddVLG(
                panel,
                spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                padding: UI.Pad(
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    GalleryUiDesignTokens.PopupMenuPaddingRef + 2f,
                    GalleryUiDesignTokens.PopupMenuPaddingRef,
                    s));
            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            RectTransform panelRT = panel.GetComponent<RectTransform>();
            if (panelRT != null)
            {
                panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                panelRT.pivot = new Vector2(0.5f, 0.5f);
                panelRT.sizeDelta = new Vector2(panelW, panelRT.sizeDelta.y);
            }

            // Title bar hairline group — match tag-menu header weight.
            GameObject titleRow = UI.CreateChildRT(panel, "TitleRow");
            UI.AddHLG(titleRow, spacing: 4f * s, childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false);
            float titleH = DetailStripTagMenuChromeBtnRef * s;
            UI.AddLE(titleRow, minHeight: titleH, preferredHeight: titleH, flexibleWidth: 1f);

            Text tt = UI.CreateLabel(
                titleRow, title ?? "", bodyFont, UI.PopupText, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow, name: "Title");
            tt.fontStyle = FontStyle.Bold;
            GalleryUiMetrics.ApplyFont(tt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(tt.gameObject, flexibleWidth: 1f, minHeight: titleH, preferredHeight: titleH);

            GameObject titleRule = UI.CreateChildRT(panel, "TitleRule");
            UI.AddImage(titleRule, new Color(0.32f, 0.34f, 0.40f, 1f), raycastTarget: false);
            UI.AddLE(titleRule, preferredHeight: 1f, minHeight: 1f, flexibleWidth: 1f, flexibleHeight: 0f);

            GameObject listHost = UI.CreateChildRT(panel, "Rows");
            UI.AddVLG(
                listHost,
                spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childForceExpandWidth: true,
                childForceExpandHeight: false);
            UI.AddLE(listHost, flexibleWidth: 1f, flexibleHeight: 0f);

            SetLayerRecursive(overlay, host.gameObject.layer);
            overlay.transform.SetAsLastSibling();
            rowsParent = listHost.transform;
            return overlay;
        }

        private GameObject AddCategoryModalRow(Transform parent, string label, Color? bg, float rowH, int font, TextAnchor align, Action onClick)
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            GameObject row = new GameObject("CatModalRow");
            row.transform.SetParent(parent, false);
            Color fill = bg.HasValue ? bg.Value : UI.PopupRowBackdrop;
            Image img = AddCategoryQuickRoundedBg(row, fill);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

            if (onClick != null)
            {
                Button b = row.AddComponent<Button>();
                b.targetGraphic = img;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { try { onClick(); } catch { } });

                UIHoverBorder hb = row.AddComponent<UIHoverBorder>();
                hb.hoverColor = DetailStripActionPrimary;
                hb.borderSize = 2f;
                hb.inward = true;
                // Destructive / cancel rows: danger hover cue (von Restorff restraint — only on risk).
                if (fill.r > 0.4f && fill.g < 0.3f && fill.b < 0.3f)
                    hb.hoverColor = DetailStripActionDanger;
            }

            Text txt = UI.CreateLabel(row, label, font, UI.PopupText, align, HorizontalWrapMode.Overflow, name: "Label");
            GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            RectTransform trt = txt.GetComponent<RectTransform>();
            float padX = GalleryUiDesignTokens.PopupMenuRowTextPadXRef * s;
            trt.offsetMin = new Vector2(padX, 0f);
            trt.offsetMax = new Vector2(-padX, 0f);
            return row;
        }

        // ---- Assign view -----------------------------------------------------------------

        private void OpenTagCategoryAssignView()
        {
            CloseTagCategoryEditorModal();
            int selCount = _userTagEditorRowSelection != null ? _userTagEditorRowSelection.Count : 0;
            if (selCount == 0) return;

            var cats = new List<VpbLocalDatabase.GalleryUserTagCategoryInfo>();
            try { VpbLocalDatabase.TryListGalleryUserTagCategories(cats); } catch { }
            var assign = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try { VpbLocalDatabase.TryReadGalleryUserTagCategoryAssignments(assign); } catch { }

            // Shared category across the whole selection (for the check mark), else -2 = mixed, -1 = all none.
            long sharedId = -3;
            bool mixed = false;
            foreach (var name in _userTagEditorRowSelection)
            {
                string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(name);
                long id;
                long cur = assign.TryGetValue(norm, out id) ? id : -1;
                if (sharedId == -3) sharedId = cur;
                else if (sharedId != cur) { mixed = true; break; }
            }

            Transform rows; int font; float rowH;
            GameObject overlay = BuildTagCategoryModalShell(
                VPBTranslation.T("gallery.tagcat.assign_title", "Category") + " \u2192 " + selCount + " " + VPBTranslation.T("gallery.tagcat.tags_word", "tag(s)"),
                out rows, out font, out rowH);
            if (overlay == null) return;
            _tagCategoryModalGo = overlay;

            bool allNone = !mixed && sharedId == -1;
            AddCategoryModalRow(rows,
                (allNone ? "\u2713 " : "") + VPBTranslation.T("gallery.tagcat.none", "No category"),
                allNone ? UI.PopupRowActiveBackdrop : (Color?)null,
                rowH, font, TextAnchor.MiddleLeft,
                () => { AssignCategoryToEditorSelection(-1); CloseTagCategoryEditorModal(); });

            for (int i = 0; i < cats.Count; i++)
            {
                var cat = cats[i];
                Color swatch;
                if (!UIColorPicker.TryParseFlexibleColor(cat.Color, TagCategoryDefaultNewColor, out swatch))
                    swatch = TagCategoryDefaultNewColor;
                swatch.a = 1f;
                bool isShared = !mixed && sharedId == cat.Id;
                long capId = cat.Id;
                AddCategoryModalRow(rows,
                    (isShared ? "\u2713 " : "") + cat.Name,
                    swatch, rowH, font, TextAnchor.MiddleLeft,
                    () => { AssignCategoryToEditorSelection(capId); CloseTagCategoryEditorModal(); });
            }

            AddCategoryModalRow(rows,
                VPBTranslation.T("gallery.tagcat.new", "+ New category\u2026"),
                new Color(0.22f, 0.4f, 0.25f, 1f), rowH, font, TextAnchor.MiddleLeft,
                () =>
                {
                    CloseTagCategoryEditorModal();
                    DisplayTextInput(VPBTranslation.T("gallery.tagcat.new_name", "New category name"), "", newName =>
                    {
                        string cn = VpbLocalDatabase.NormalizeGalleryUserTagCategoryName(newName);
                        if (string.IsNullOrEmpty(cn)) return;
                        DisplayColorPicker(VPBTranslation.T("gallery.tagcat.pick_color", "Category color") + ": " + cn, TagCategoryDefaultNewColor, chosen =>
                        {
                            long newId;
                            if (VpbLocalDatabase.TryCreateGalleryUserTagCategory(cn, ColorToHexRgb(chosen), out newId) && newId >= 0)
                                AssignCategoryToEditorSelection(newId);
                        });
                    });
                });

            AddCategoryModalRow(rows,
                VPBTranslation.T("gallery.tagcat.manage", "Manage categories\u2026"),
                new Color(0.28f, 0.28f, 0.34f, 1f), rowH, font, TextAnchor.MiddleLeft,
                OpenTagCategoryManageView);

            AddCategoryModalRow(rows,
                VPBTranslation.T("hook.qmbutton.cancel", "Cancel"),
                new Color(0.5f, 0.2f, 0.2f, 1f), rowH, font, TextAnchor.MiddleCenter,
                CloseTagCategoryEditorModal);
        }

        // ---- Manage view (rename / recolor / delete) -------------------------------------

        private void OpenTagCategoryManageView()
        {
            CloseTagCategoryEditorModal();

            var cats = new List<VpbLocalDatabase.GalleryUserTagCategoryInfo>();
            try { VpbLocalDatabase.TryListGalleryUserTagCategories(cats); } catch { }

            Transform rows; int font; float rowH;
            GameObject overlay = BuildTagCategoryModalShell(
                VPBTranslation.T("gallery.tagcat.manage_title", "Manage categories"), out rows, out font, out rowH);
            if (overlay == null) return;
            _tagCategoryModalGo = overlay;

            if (cats.Count == 0)
                AddCategoryModalRow(rows, VPBTranslation.T("gallery.tagcat.none_exist", "(no categories yet)"), null, rowH, font, TextAnchor.MiddleLeft, null);

            float s = ChromeScale;
            for (int i = 0; i < cats.Count; i++)
            {
                var cat = cats[i];
                long capId = cat.Id;
                string capName = cat.Name;
                Color swatch;
                if (!UIColorPicker.TryParseFlexibleColor(cat.Color, TagCategoryDefaultNewColor, out swatch))
                    swatch = TagCategoryDefaultNewColor;
                swatch.a = 1f;

                GameObject strip = new GameObject("CatManageRow");
                strip.transform.SetParent(rows, false);
                strip.AddComponent<RectTransform>();
                UI.AddHLG(
                    strip,
                    spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                    childForceExpandWidth: false,
                    childForceExpandHeight: true);
                UI.AddLE(strip, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

                GameObject nameRow = AddCategoryModalRow(strip.transform, capName, swatch, rowH, font, TextAnchor.MiddleLeft, null);
                if (nameRow != null)
                {
                    var le = nameRow.GetComponent<LayoutElement>();
                    if (le != null) { le.flexibleWidth = 1f; le.minWidth = 120f * s; }
                }

                float actionW = 72f * s;
                GameObject colorBtn = AddCategoryModalRow(strip.transform, VPBTranslation.T("gallery.tagcat.color", "Color"), new Color(0.28f, 0.34f, 0.4f, 1f), rowH, font, TextAnchor.MiddleCenter,
                    () =>
                    {
                        CloseTagCategoryEditorModal();
                        DisplayColorPicker(VPBTranslation.T("gallery.tagcat.pick_color", "Category color") + ": " + capName, swatch, chosen =>
                        {
                            VpbLocalDatabase.TrySetGalleryUserTagCategoryColor(capId, ColorToHexRgb(chosen));
                            AfterTagCategoryChange();
                            OpenTagCategoryManageView();
                        });
                    });
                if (colorBtn != null)
                {
                    var le = colorBtn.GetComponent<LayoutElement>();
                    if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = actionW; le.minWidth = actionW; }
                }

                GameObject renameBtn = AddCategoryModalRow(strip.transform, VPBTranslation.T("gallery.tagcat.rename", "Rename"), new Color(0.26f, 0.34f, 0.46f, 1f), rowH, font, TextAnchor.MiddleCenter,
                    () =>
                    {
                        CloseTagCategoryEditorModal();
                        DisplayTextInput(VPBTranslation.T("gallery.tagcat.rename_title", "Rename category"), capName, newName =>
                        {
                            VpbLocalDatabase.TryRenameGalleryUserTagCategory(capId, newName);
                            AfterTagCategoryChange();
                            OpenTagCategoryManageView();
                        });
                    });
                if (renameBtn != null)
                {
                    var le = renameBtn.GetComponent<LayoutElement>();
                    if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = 80f * s; le.minWidth = 80f * s; }
                }

                GameObject delBtn = AddCategoryModalRow(strip.transform, VPBTranslation.T("gallery.tagcat.delete", "Delete"), new Color(0.5f, 0.2f, 0.2f, 1f), rowH, font, TextAnchor.MiddleCenter,
                    () =>
                    {
                        CloseTagCategoryEditorModal();
                        DisplayConfirm(VPBTranslation.T("gallery.tagcat.delete_title", "Delete category"),
                            VPBTranslation.T("gallery.tagcat.delete_msg", "Delete category and unassign its tags?") + "\n\n" + capName,
                            () =>
                            {
                                VpbLocalDatabase.TryDeleteGalleryUserTagCategory(capId);
                                AfterTagCategoryChange();
                                OpenTagCategoryManageView();
                            });
                    });
                if (delBtn != null)
                {
                    var le = delBtn.GetComponent<LayoutElement>();
                    if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = actionW; le.minWidth = actionW; }
                }
            }

            AddCategoryModalRow(rows,
                VPBTranslation.T("gallery.tagcat.back", "\u2190 Back"),
                new Color(0.3f, 0.3f, 0.34f, 1f), rowH, font, TextAnchor.MiddleCenter,
                OpenTagCategoryAssignView);
        }
    }
}
