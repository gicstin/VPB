using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    // US-02: color-coded named tag categories. A tag may belong to one category; the category owns a
    // display color used to tint the resting (inactive) state of its rows in the Tag pane, and shown as a
    // swatch in the Tag Editor list. Category assignment + management live inside the Tag Editor overlay
    // (full-size, tap-based, VR-friendly) — no right-click required.
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

        private GameObject BuildTagCategoryModalShell(string title, out Transform rowsParent, out int bodyFont, out float rowH)
        {
            rowsParent = null;
            bodyFont = 16;
            rowH = 40f;

            Transform host = _userTagEditorRoot != null ? _userTagEditorRoot.transform
                : (backgroundBoxGO != null ? backgroundBoxGO.transform : null);
            if (host == null) return null;

            float s = ChromeScale;
            var typ = new GalleryModalTypography(s * 1.38f);
            bodyFont = typ.Body;
            rowH = 42f * s;

            GameObject overlay = new GameObject("VPB_TagCategoryModal");
            overlay.transform.SetParent(host, false);
            RectTransform ort = overlay.AddComponent<RectTransform>();
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.sizeDelta = Vector2.zero;
            Image odim = overlay.AddComponent<Image>();
            odim.color = new Color(0f, 0f, 0f, 0.55f);
            odim.raycastTarget = true;
            Button obtn = overlay.AddComponent<Button>();
            obtn.transition = Selectable.Transition.None;
            obtn.onClick.AddListener(CloseTagCategoryEditorModal);

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(overlay.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(470f * s, 60f);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            pbg.raycastTarget = true;
            // Swallow clicks on the panel so they do not fall through to the overlay close button.
            Button pbtn = panel.AddComponent<Button>();
            pbtn.transition = Selectable.Transition.None;

            VerticalLayoutGroup v = UI.AddVLG(panel, spacing: 5f * s, padding: UI.Pad(12, 12, 10, 10, s));
            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text tt = UI.CreateLabel(panel, title ?? "", typ.Prose, new Color(0.92f, 0.92f, 0.95f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Title");
            LayoutElement tle = UI.AddLE(tt.gameObject, minHeight: rowH, preferredHeight: rowH);

            SetLayerRecursive(overlay, host.gameObject.layer);
            overlay.transform.SetAsLastSibling();
            rowsParent = panel.transform;
            return overlay;
        }

        private GameObject AddCategoryModalRow(Transform parent, string label, Color? bg, float rowH, int font, TextAnchor align, Action onClick)
        {
            GameObject row = new GameObject("CatModalRow");
            row.transform.SetParent(parent, false);
            Image img = AddCategoryQuickRoundedBg(row, bg.HasValue ? bg.Value : UI.PopupRowBackdrop);
            LayoutElement le = UI.AddLE(row, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
            if (onClick != null)
            {
                Button b = row.AddComponent<Button>();
                b.targetGraphic = img;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { try { onClick(); } catch { } });
            }
            Text txt = UI.CreateLabel(row, label, font, Color.white, align, HorizontalWrapMode.Overflow, name: "Label");
            RectTransform trt = txt.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(14f, 0f);
            trt.offsetMax = new Vector2(-14f, 0f);
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
                HorizontalLayoutGroup hlg = UI.AddHLG(strip, spacing: 5f * s, childForceExpandWidth: false, childForceExpandHeight: true);
                LayoutElement stripLe = UI.AddLE(strip, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

                GameObject nameRow = AddCategoryModalRow(strip.transform, capName, swatch, rowH, font, TextAnchor.MiddleLeft, null);
                if (nameRow != null) { var le = nameRow.GetComponent<LayoutElement>(); if (le != null) { le.flexibleWidth = 1f; le.minWidth = 140f * s; } }

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
                if (colorBtn != null) { var le = colorBtn.GetComponent<LayoutElement>(); if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = 74f * s; le.minWidth = 74f * s; } }

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
                if (renameBtn != null) { var le = renameBtn.GetComponent<LayoutElement>(); if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = 86f * s; le.minWidth = 86f * s; } }

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
                if (delBtn != null) { var le = delBtn.GetComponent<LayoutElement>(); if (le != null) { le.flexibleWidth = 0f; le.preferredWidth = 82f * s; le.minWidth = 82f * s; } }
            }

            AddCategoryModalRow(rows,
                VPBTranslation.T("gallery.tagcat.back", "\u2190 Back"),
                new Color(0.3f, 0.3f, 0.34f, 1f), rowH, font, TextAnchor.MiddleCenter,
                OpenTagCategoryAssignView);
        }
    }
}
