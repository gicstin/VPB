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
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            int font = Mathf.RoundToInt(20f * s);
            float rowH = 42f * s;

            GameObject row = new GameObject("CatQuickRow");
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft;
            h.spacing = 6f * s;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = rowH;
            le.preferredHeight = rowH;

            if (displayIndex.HasValue)
            {
                GameObject idxGo = new GameObject("Index");
                idxGo.transform.SetParent(row.transform, false);
                Text it = idxGo.AddComponent<Text>();
                it.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                it.fontSize = Mathf.RoundToInt(18f * s);
                it.color = new Color(0.75f, 0.78f, 0.82f, 1f);
                it.alignment = TextAnchor.MiddleRight;
                it.text = displayIndex.Value.ToString();
                try { VPBUiFont.ApplyTo(it); } catch { }
                LayoutElement ile = idxGo.AddComponent<LayoutElement>();
                ile.preferredWidth = 42f * s;
                ile.minWidth = 42f * s;
            }

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(row.transform, false);
            Text t = labelGo.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = font;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.text = name ?? "";
            try { VPBUiFont.ApplyTo(t); } catch { }
            LayoutElement tle = labelGo.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            tle.minWidth = 0f;

            if (showUp)
            {
                UnityAction upAct = onUp != null ? (UnityAction)(() => onUp()) : null;
                Sprite upIcon = UI.LoadIconSprite("vpb_icons/up.png", new Color(0.9f, 0.9f, 0.9f, 1f));
                GameObject up = UI.CreateSideTabSquareIconButton(row, rowH, upIcon, upAct, new Color(0.22f, 0.42f, 0.58f, 1f), 4f * s);
                if (upIcon == null) AddButtonOverlayGlyph(up, "▲", Mathf.RoundToInt(18f * s));
                AddTooltipPlain(up, VPBTranslation.T("settings.category_quick.editor.move_up_tip", "Move up"));
            }
            if (showDown)
            {
                UnityAction dnAct = onDown != null ? (UnityAction)(() => onDown()) : null;
                Sprite dnIcon = UI.LoadIconSprite("vpb_icons/down.png", new Color(0.9f, 0.9f, 0.9f, 1f));
                GameObject dn = UI.CreateSideTabSquareIconButton(row, rowH, dnIcon, dnAct, new Color(0.22f, 0.42f, 0.58f, 1f), 4f * s);
                if (dnIcon == null) AddButtonOverlayGlyph(dn, "▼", Mathf.RoundToInt(18f * s));
                AddTooltipPlain(dn, VPBTranslation.T("settings.category_quick.editor.move_down_tip", "Move down"));
            }

            // Toggle hidden/show uses text button (icon set not guaranteed).
            GameObject tog = new GameObject("ToggleHidden");
            tog.transform.SetParent(row.transform, false);
            Image bg = tog.AddComponent<Image>();
            bg.color = new Color(0.44f, 0.36f, 0.20f, 1f);
            Button b = tog.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onToggleHidden != null) b.onClick.AddListener(() => onToggleHidden());
            LayoutElement ble = tog.AddComponent<LayoutElement>();
            ble.preferredWidth = 84f * s;
            ble.minWidth = 84f * s;
            ble.preferredHeight = rowH;
            ble.minHeight = rowH;

            GameObject tt = new GameObject("Text");
            tt.transform.SetParent(tog.transform, false);
            Text bt = tt.AddComponent<Text>();
            bt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            bt.fontSize = Mathf.RoundToInt(18f * s);
            bt.color = Color.white;
            bt.alignment = TextAnchor.MiddleCenter;
            bt.text = toggleHiddenLabel ?? "";
            try { VPBUiFont.ApplyTo(bt); } catch { }
            RectTransform rt = tt.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        private static void AddButtonOverlayGlyph(GameObject btnGo, string glyph, int fontSize)
        {
            if (btnGo == null) return;
            GameObject g = new GameObject("Glyph");
            g.transform.SetParent(btnGo.transform, false);
            Text t = g.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = glyph ?? "";
            try { VPBUiFont.ApplyTo(t); } catch { }
            RectTransform rt = g.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        private static GameObject CreateHeaderButton(Transform parent, float width, float height, string label, int fontSize, Color bg, UnityAction onClick)
        {
            GameObject go = new GameObject("Button_" + (label ?? ""));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) b.onClick.AddListener(onClick);
            go.AddComponent<UIHoverBorder>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = label ?? "";
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            try { VPBUiFont.ApplyTo(t); } catch { }

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            return go;
        }

        private void ShowCategoryQuickEditor()
        {
            if (backgroundBoxGO == null) return;
            if (_catQuickEditorRoot != null) return;

            LoadCategoryQuickEditorDraftFromConfig();

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            int headerFont = Mathf.RoundToInt(26f * s);
            int bodyFont = Mathf.RoundToInt(18f * s);

            _catQuickEditorRoot = new GameObject("CategoryQuickEditorRoot");
            _catQuickEditorRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRt = _catQuickEditorRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image dim = _catQuickEditorRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;
            Button dimBtn = _catQuickEditorRoot.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            dimBtn.onClick.AddListener(() => HideCategoryQuickEditor());

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_catQuickEditorRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(860f * s, 720f * s);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.06f, 0.06f, 0.08f, 1f);

            VerticalLayoutGroup v = panel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s));
            v.spacing = 10f * s;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // Header row.
            GameObject header = new GameObject("HeaderRow");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.spacing = 8f * s;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;
            LayoutElement hle = header.AddComponent<LayoutElement>();
            hle.minHeight = 54f * s;
            hle.preferredHeight = 54f * s;

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(header.transform, false);
            Text title = titleGo.AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            title.fontSize = headerFont;
            title.color = Color.white;
            title.alignment = TextAnchor.MiddleLeft;
            title.text = VPBTranslation.T("settings.category_quick.editor.title", "Edit header category dropdown");
            try { VPBUiFont.ApplyTo(title); } catch { }
            LayoutElement tle = titleGo.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
            tle.minWidth = 0f;

            GameObject resetBtn = CreateHeaderButton(header.transform, 160f * s, 44f * s, VPBTranslation.T("settings.category_quick.editor.reset", "Reset"), bodyFont, new Color(0.2f, 0.2f, 0.2f, 1f), () =>
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
            LayoutElement scLe = scrollGO.AddComponent<LayoutElement>();
            scLe.flexibleHeight = 1f;
            scLe.minHeight = 420f * s;
            Transform vp = scrollGO.transform.Find("Viewport");
            Transform content = vp != null ? vp.Find("Content") : null;
            if (content == null) return;

            GameObject visHeader = new GameObject("VisibleHeader");
            visHeader.transform.SetParent(content, false);
            Text vh = visHeader.AddComponent<Text>();
            vh.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            vh.fontSize = Mathf.RoundToInt(22f * s);
            vh.color = new Color(0.85f, 0.9f, 1f, 1f);
            vh.alignment = TextAnchor.MiddleLeft;
            vh.text = VPBTranslation.T("settings.category_quick.editor.visible_header", "Shown in header menu (ordered)");
            try { VPBUiFont.ApplyTo(vh); } catch { }
            LayoutElement vhLe = visHeader.AddComponent<LayoutElement>();
            vhLe.minHeight = 34f * s;

            GameObject visList = new GameObject("VisibleList");
            visList.transform.SetParent(content, false);
            VerticalLayoutGroup vvg = visList.AddComponent<VerticalLayoutGroup>();
            vvg.spacing = 6f * s;
            vvg.childAlignment = TextAnchor.UpperLeft;
            vvg.childControlWidth = true;
            vvg.childControlHeight = true;
            vvg.childForceExpandWidth = true;
            vvg.childForceExpandHeight = false;
            _catQuickEditorVisibleParent = visList.transform;

            GameObject hidHeader = new GameObject("HiddenHeader");
            hidHeader.transform.SetParent(content, false);
            Text hhT = hidHeader.AddComponent<Text>();
            hhT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hhT.fontSize = Mathf.RoundToInt(22f * s);
            hhT.color = new Color(1f, 0.88f, 0.82f, 1f);
            hhT.alignment = TextAnchor.MiddleLeft;
            hhT.text = VPBTranslation.T("settings.category_quick.editor.hidden_header", "Hidden from header menu");
            try { VPBUiFont.ApplyTo(hhT); } catch { }
            LayoutElement hhLe = hidHeader.AddComponent<LayoutElement>();
            hhLe.minHeight = 34f * s;

            GameObject hidList = new GameObject("HiddenList");
            hidList.transform.SetParent(content, false);
            VerticalLayoutGroup hvg = hidList.AddComponent<VerticalLayoutGroup>();
            hvg.spacing = 6f * s;
            hvg.childAlignment = TextAnchor.UpperLeft;
            hvg.childControlWidth = true;
            hvg.childControlHeight = true;
            hvg.childForceExpandWidth = true;
            hvg.childForceExpandHeight = false;
            _catQuickEditorHiddenParent = hidList.transform;

            RebuildCategoryQuickEditorRows();
        }
    }
}

