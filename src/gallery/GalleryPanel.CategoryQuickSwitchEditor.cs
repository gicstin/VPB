using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private GameObject _catQuickEditorRoot;
        private Transform _catQuickEditorVisibleParent;
        private Transform _catQuickEditorHiddenParent;

        private readonly List<string> _catQuickVisibleDraft = new List<string>(32);
        private readonly HashSet<string> _catQuickHiddenDraft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void HideCategoryQuickEditor()
        {
            if (_catQuickEditorRoot != null)
            {
                try { UnityEngine.Object.Destroy(_catQuickEditorRoot); } catch { }
                _catQuickEditorRoot = null;
            }
            _catQuickEditorVisibleParent = null;
            _catQuickEditorHiddenParent = null;
        }

        private static void ParseNameTokens(string spec, List<string> dest)
        {
            // Reuse exact parsing rules from quick-switch runtime.
            ParseQuickSwitchNameTokens(spec, dest);
        }

        private List<string> BuildAllCategoryNames()
        {
            var result = new List<string>(32);
            if (categories != null)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    string n = categories[i].name;
                    if (string.IsNullOrEmpty(n)) continue;
                    result.Add(n);
                }
            }
            // De-dupe, stable.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int w = 0;
            for (int i = 0; i < result.Count; i++)
            {
                if (seen.Add(result[i])) result[w++] = result[i];
            }
            if (w != result.Count) result.RemoveRange(w, result.Count - w);
            return result;
        }

        private void LoadCategoryQuickEditorDraftFromConfig()
        {
            _catQuickVisibleDraft.Clear();
            _catQuickHiddenDraft.Clear();

            if (VPBConfig.Instance == null) return;

            var allNames = BuildAllCategoryNames();

            // Hidden list.
            {
                var tokens = new List<string>(32);
                ParseNameTokens(VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "", tokens);
                for (int i = 0; i < tokens.Count; i++)
                {
                    string t = tokens[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    _catQuickHiddenDraft.Add(t);
                }
            }

            // Visible ordered list.
            {
                var tokens = new List<string>(32);
                string orderSpec = (VPBConfig.Instance.GalleryCategoryQuickOrder ?? "").Trim();
                if (!string.IsNullOrEmpty(orderSpec)) ParseNameTokens(orderSpec, tokens);
                else tokens.AddRange(s_DefaultGalleryQuickSwitchOrder);

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < tokens.Count; i++)
                {
                    string want = tokens[i];
                    if (string.IsNullOrEmpty(want)) continue;
                    for (int a = 0; a < allNames.Count; a++)
                    {
                        string actual = allNames[a];
                        if (!string.Equals(actual, want, StringComparison.OrdinalIgnoreCase)) continue;
                        if (used.Add(actual) && !_catQuickHiddenDraft.Contains(actual))
                            _catQuickVisibleDraft.Add(actual);
                        break;
                    }
                }

                for (int i = 0; i < allNames.Count; i++)
                {
                    string n = allNames[i];
                    if (used.Contains(n)) continue;
                    if (_catQuickHiddenDraft.Contains(n)) continue;
                    _catQuickVisibleDraft.Add(n);
                }
            }

            // If config hid unknown token, keep it, but do not render unless it matches real category name.
            // (Hidden spec is case-insensitive, but we only show categories that exist in VaM list.)
        }

        private void SaveCategoryQuickEditorDraftToConfig()
        {
            if (VPBConfig.Instance == null) return;

            // Order spec: only explicit visible list. Quick-switch runtime appends unspecified categories.
            string orderSpec = string.Join("\n", _catQuickVisibleDraft.ToArray());

            // Hidden spec: alphabetical for stable diffs / readability.
            var hidden = new List<string>(_catQuickHiddenDraft);
            hidden.Sort(StringComparer.OrdinalIgnoreCase);
            string hiddenSpec = string.Join("\n", hidden.ToArray());

            VPBConfig.Instance.GalleryCategoryQuickOrder = orderSpec ?? "";
            VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = hiddenSpec ?? "";
            VPBConfig.Instance.TriggerChange();
        }

        private static void DestroyAllChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null) continue;
                try { UnityEngine.Object.Destroy(ch.gameObject); } catch { }
            }
        }

        private void RebuildCategoryQuickEditorRows()
        {
            if (_catQuickEditorVisibleParent == null || _catQuickEditorHiddenParent == null) return;

            DestroyAllChildren(_catQuickEditorVisibleParent);
            DestroyAllChildren(_catQuickEditorHiddenParent);

            var allNames = BuildAllCategoryNames();
            var nameSet = new HashSet<string>(allNames, StringComparer.OrdinalIgnoreCase);

            // Visible ordered rows.
            for (int i = 0; i < _catQuickVisibleDraft.Count; i++)
            {
                string name = _catQuickVisibleDraft[i];
                if (string.IsNullOrEmpty(name) || !nameSet.Contains(name)) continue;
                int idx = i;
                CreateCategoryQuickEditorRow(_catQuickEditorVisibleParent, idx + 1, name,
                    showUp: idx > 0,
                    showDown: idx < _catQuickVisibleDraft.Count - 1,
                    onUp: () => { var tmp = _catQuickVisibleDraft[idx - 1]; _catQuickVisibleDraft[idx - 1] = _catQuickVisibleDraft[idx]; _catQuickVisibleDraft[idx] = tmp; RebuildCategoryQuickEditorRows(); },
                    onDown: () => { var tmp = _catQuickVisibleDraft[idx + 1]; _catQuickVisibleDraft[idx + 1] = _catQuickVisibleDraft[idx]; _catQuickVisibleDraft[idx] = tmp; RebuildCategoryQuickEditorRows(); },
                    onToggleHidden: () =>
                    {
                        _catQuickVisibleDraft.RemoveAt(idx);
                        _catQuickHiddenDraft.Add(name);
                        RebuildCategoryQuickEditorRows();
                    },
                    toggleHiddenLabel: "Hide");
            }

            // Hidden rows: only real categories.
            var hiddenList = new List<string>();
            foreach (var n in _catQuickHiddenDraft)
            {
                if (string.IsNullOrEmpty(n) || !nameSet.Contains(n)) continue;
                hiddenList.Add(n);
            }
            hiddenList.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < hiddenList.Count; i++)
            {
                string name = hiddenList[i];
                CreateCategoryQuickEditorRow(_catQuickEditorHiddenParent, null, name,
                    showUp: false, showDown: false,
                    onUp: null, onDown: null,
                    onToggleHidden: () =>
                    {
                        _catQuickHiddenDraft.Remove(name);
                        _catQuickVisibleDraft.Add(name);
                        RebuildCategoryQuickEditorRows();
                    },
                    toggleHiddenLabel: "Show");
            }
        }

        private void CreateCategoryQuickEditorRow(
            Transform parent,
            int? displayIndex,
            string name,
            bool showUp,
            bool showDown,
            Action onUp,
            Action onDown,
            Action onToggleHidden,
            string toggleHiddenLabel)
        {
            float s = ChromeScale;
            GalleryModalTypography rowType = new GalleryModalTypography(s);
            int font = rowType.Body;
            float rowH = 42f * s;

            GameObject row = UI.CreateChildRT(parent.gameObject, "CatQuickRow");
            UI.AddHLG(row, 6f * s, childForceExpandWidth: false);
            LayoutElement le = UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            if (displayIndex.HasValue)
            {
                Text it = UI.CreateLabel(row.gameObject, displayIndex.Value.ToString(), rowType.Body, new Color(0.75f, 0.78f, 0.82f, 1f), TextAnchor.MiddleRight, name: "Index");
                LayoutElement ile = UI.AddLE(it.gameObject, minWidth: 42f * s, preferredWidth: 42f * s);
            }

            Text t = UI.CreateLabel(row.gameObject, name ?? "", font, Color.white, TextAnchor.MiddleLeft, name: "Label");
            LayoutElement tle = UI.AddLE(t.gameObject, minWidth: 0f, flexibleWidth: 1f);

            if (showUp)
            {
                UnityAction upAct = onUp != null ? (UnityAction)(() => onUp()) : null;
                Sprite upIcon = UI.LoadIconSprite("vpb_icons/up.png", new Color(0.9f, 0.9f, 0.9f, 1f));
                GameObject up = UI.CreateSideTabSquareIconButton(row, rowH, upIcon, upAct, new Color(0.22f, 0.42f, 0.58f, 1f), 4f * s);
                if (upIcon == null) AddButtonOverlayGlyph(up, "▲", GalleryUiMetrics.GlyphFontFromControlHeight(34f, s, GalleryUiDesignTokens.FontMinRef));
                AddTooltipPlain(up, VPBTranslation.T("settings.category_quick.editor.move_up_tip", "Move up"));
            }
            if (showDown)
            {
                UnityAction dnAct = onDown != null ? (UnityAction)(() => onDown()) : null;
                Sprite dnIcon = UI.LoadIconSprite("vpb_icons/down.png", new Color(0.9f, 0.9f, 0.9f, 1f));
                GameObject dn = UI.CreateSideTabSquareIconButton(row, rowH, dnIcon, dnAct, new Color(0.22f, 0.42f, 0.58f, 1f), 4f * s);
                if (dnIcon == null) AddButtonOverlayGlyph(dn, "▼", GalleryUiMetrics.GlyphFontFromControlHeight(34f, s, GalleryUiDesignTokens.FontMinRef));
                AddTooltipPlain(dn, VPBTranslation.T("settings.category_quick.editor.move_down_tip", "Move down"));
            }

            // Toggle hidden/show uses text button (icon set not guaranteed).
            GameObject tog = new GameObject("ToggleHidden");
            tog.transform.SetParent(row.transform, false);
            Image bg = AddCategoryQuickRoundedBg(tog, new Color(0.44f, 0.36f, 0.20f, 1f));
            Button b = tog.AddComponent<Button>();
            b.targetGraphic = bg;
            UI.ConfigButtonFlat(b);
            if (onToggleHidden != null) b.onClick.AddListener(() => onToggleHidden());
            LayoutElement ble = UI.AddLE(tog, minWidth: 84f * s, minHeight: rowH, preferredWidth: 84f * s, preferredHeight: rowH);

            UI.CreateLabel(tog, toggleHiddenLabel ?? "", rowType.Body, Color.white, TextAnchor.MiddleCenter);
        }

        private static void AddButtonOverlayGlyph(GameObject btnGo, string glyph, int fontSize)
        {
            if (btnGo == null) return;
            UI.CreateLabel(btnGo, glyph ?? "", fontSize, Color.white, TextAnchor.MiddleCenter, name: "Glyph");
        }

        private static GameObject CreateHeaderButton(Transform parent, float width, float height, string label, int fontSize, Color bg, UnityAction onClick)
        {
            GameObject go = new GameObject("Button_" + (label ?? ""));
            go.transform.SetParent(parent, false);
            Image img = AddCategoryQuickRoundedBg(go, bg);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            UI.ConfigButtonFlat(b);
            if (onClick != null) b.onClick.AddListener(onClick);
            go.AddComponent<UIHoverBorder>();

            LayoutElement le = UI.AddLE(go, minWidth: width, minHeight: height, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);

            UI.CreateLabel(go, label ?? "", fontSize, Color.white, TextAnchor.MiddleCenter);

            return go;
        }

        private void ShowCategoryQuickEditor()
        {
            if (backgroundBoxGO == null) return;
            if (_catQuickEditorRoot != null) return;

            LoadCategoryQuickEditorDraftFromConfig();

            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;

            _catQuickEditorRoot = UI.CreateChildRT(backgroundBoxGO, "CategoryQuickEditorRoot", AnchorPresets.stretchAll);

            Image dim = UI.AddImage(_catQuickEditorRoot, new Color(0f, 0f, 0f, 0.72f));
            Button dimBtn = _catQuickEditorRoot.AddComponent<Button>();
            UI.ConfigButtonFlat(dimBtn);
            dimBtn.onClick.AddListener(() => HideCategoryQuickEditor());

            GameObject panel = UI.CreateChildRT(_catQuickEditorRoot, "Panel", AnchorPresets.middleCenter, new Vector2(860f * s, 720f * s));
            Image pbg = AddCategoryQuickRoundedBg(panel, new Color(0.06f, 0.06f, 0.08f, 1f));

            UI.AddVLG(panel, 10f * s, UI.Pad(14f, 14f, 14f, 14f, s));

            // Header row.
            GameObject header = UI.CreateChildRT(panel, "HeaderRow");
            UI.AddHLG(header, 8f * s, childForceExpandWidth: false);
            LayoutElement hle = UI.AddLE(header, minHeight: 54f * s, preferredHeight: 54f * s);

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(header.transform, false);
            Text title = titleGo.AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(title, headerFont);
            title.color = Color.white;
            title.alignment = TextAnchor.MiddleLeft;
            title.text = VPBTranslation.T("settings.category_quick.editor.title", "Edit header category dropdown");
            try { VPBUiFont.ApplyTo(title); } catch { }
            LayoutElement tle = UI.AddLE(titleGo, minWidth: 0f, flexibleWidth: 1f);

            GameObject resetBtn = CreateHeaderButton(header.transform, 160f * s, 44f * s, VPBTranslation.T("settings.category_quick.editor.reset", "Reset"), bodyFont, UI.ChromePanel, () =>
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryCategoryQuickOrder = "";
                VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = "";
                LoadCategoryQuickEditorDraftFromConfig();
                RebuildCategoryQuickEditorRows();
            });
            AddTooltipPlain(resetBtn, VPBTranslation.T("settings.category_quick.editor.reset_tip", "Reset to built-in defaults and clear hidden list (does not save until Save)."));

            GameObject saveBtn = CreateHeaderButton(header.transform, 120f * s, 44f * s, VPBTranslation.T("settings.save", "Save"), bodyFont, new Color(0.22f, 0.42f, 0.58f, 1f), () =>
            {
                SaveCategoryQuickEditorDraftToConfig();
                HideCategoryQuickEditor();
                try { if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true); } catch { }
            });

            GameObject closeBtn = CreateHeaderButton(header.transform, 120f * s, 44f * s, VPBTranslation.T("settings.cancel", "Cancel"), bodyFont, new Color(0.44f, 0.36f, 0.20f, 1f), () =>
            {
                HideCategoryQuickEditor();
            });

            // Body: two sections inside scroll.
            GameObject scrollGO = UI.CreateVScrollableContent(panel, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0f, 300f * s, Vector2.zero, 10f * s, 3f * s, false);
            LayoutElement scLe = UI.AddLE(scrollGO, minHeight: 420f * s, flexibleHeight: 1f);
            Transform vp = scrollGO.transform.Find("Viewport");
            Transform content = vp != null ? vp.Find("Content") : null;
            if (content == null) return;

            GameObject visHeader = new GameObject("VisibleHeader");
            visHeader.transform.SetParent(content, false);
            Text vh = visHeader.AddComponent<Text>();
            vh.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(vh, type.Body);
            vh.color = new Color(0.85f, 0.9f, 1f, 1f);
            vh.alignment = TextAnchor.MiddleLeft;
            vh.text = VPBTranslation.T("settings.category_quick.editor.visible_header", "Shown in header menu (ordered)");
            try { VPBUiFont.ApplyTo(vh); } catch { }
            LayoutElement vhLe = UI.AddLE(visHeader, minHeight: 34f * s);

            GameObject visList = UI.CreateChildRT(content.gameObject, "VisibleList");
            UI.AddVLG(visList, 6f * s);
            _catQuickEditorVisibleParent = visList.transform;

            GameObject hidHeader = new GameObject("HiddenHeader");
            hidHeader.transform.SetParent(content, false);
            Text hhT = hidHeader.AddComponent<Text>();
            hhT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(hhT, type.Body);
            hhT.color = new Color(1f, 0.88f, 0.82f, 1f);
            hhT.alignment = TextAnchor.MiddleLeft;
            hhT.text = VPBTranslation.T("settings.category_quick.editor.hidden_header", "Hidden from header menu");
            try { VPBUiFont.ApplyTo(hhT); } catch { }
            LayoutElement hhLe = UI.AddLE(hidHeader, minHeight: 34f * s);

            GameObject hidList = UI.CreateChildRT(content.gameObject, "HiddenList");
            UI.AddVLG(hidList, 6f * s);
            _catQuickEditorHiddenParent = hidList.transform;

            RebuildCategoryQuickEditorRows();
        }
    }
}

