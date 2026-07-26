using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject _mergeOutfitModalRoot;
        private Transform _mergeOutfitListParent;
        private Text _mergeOutfitTitleText;
        private Text _mergeOutfitHintText;
        private Toggle _mergeOutfitIncludeSkinHairToggle;
        private FileEntry _mergeOutfitSourceEntry;
        private JSONClass _mergeOutfitPresetJC;
        private string _mergeOutfitTargetUid;
        private List<AppearanceOutfitPickItem> _mergeOutfitItems;
        private HashSet<string> _mergeOutfitSelectedUids;
        private bool _mergeOutfitIncludeSkinAndHair;
        private readonly Dictionary<string, Image> _mergeOutfitCategoryBtnImgs =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        // Per-category hues: button on/off + matching list row selected/idle.

        /// <summary>
        /// Per-item picker for Merge Outfit: keep body by default; merge chosen clothing onto current outfit.
        /// Optional skin/hair rows when "Include skin & hair" is checked.
        /// </summary>
        public void ShowMergeOutfitPicker(FileEntry sourceEntry, Atom targetAtom, JSONClass presetJC = null)
        {
            if (backgroundBoxGO == null || targetAtom == null) return;
            if (sourceEntry == null && presetJC == null) return;

            _mergeOutfitSourceEntry = sourceEntry;
            _mergeOutfitPresetJC = presetJC;
            _mergeOutfitTargetUid = targetAtom.uid;
            _mergeOutfitIncludeSkinAndHair = false;
            ReloadMergeOutfitItems(preserveSelection: false);

            if (_mergeOutfitItems == null || _mergeOutfitItems.Count == 0)
            {
                _mergeOutfitItems = new List<AppearanceOutfitPickItem>();
                _mergeOutfitSelectedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            HideMergeOutfitPicker();
            BuildMergeOutfitPickerModal();
        }

        private void ReloadMergeOutfitItems(bool preserveSelection)
        {
            HashSet<string> prev = null;
            if (preserveSelection && _mergeOutfitSelectedUids != null)
                prev = new HashSet<string>(_mergeOutfitSelectedUids, StringComparer.OrdinalIgnoreCase);

            List<AppearanceOutfitPickItem> items = VpbImport.ListAppearanceOutfitItems(
                _mergeOutfitSourceEntry, _mergeOutfitPresetJC, _mergeOutfitIncludeSkinAndHair);
            _mergeOutfitItems = items ?? new List<AppearanceOutfitPickItem>();
            SortMergeOutfitItems(_mergeOutfitItems);
            _mergeOutfitSelectedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _mergeOutfitItems.Count; i++)
            {
                string uid = _mergeOutfitItems[i].Uid;
                if (string.IsNullOrEmpty(uid)) continue;
                if (prev != null)
                {
                    if (prev.Contains(uid))
                        _mergeOutfitSelectedUids.Add(uid);
                }
                else if (_mergeOutfitItems[i].Kind == AppearanceOutfitPickKind.Clothing)
                {
                    _mergeOutfitSelectedUids.Add(uid);
                }
            }
        }

        // Category order (Garment → Accessory → Cosmetic → Hair → Skin), then UID.
        private static void SortMergeOutfitItems(List<AppearanceOutfitPickItem> items)
        {
            if (items == null || items.Count < 2) return;
            items.Sort(CompareMergeOutfitPickItems);
        }

        private static int CompareMergeOutfitPickItems(AppearanceOutfitPickItem a, AppearanceOutfitPickItem b)
        {
            int ra = MergeOutfitCategoryRank(a);
            int rb = MergeOutfitCategoryRank(b);
            if (ra != rb) return ra.CompareTo(rb);
            return string.Compare(a != null ? a.Uid : null, b != null ? b.Uid : null, StringComparison.OrdinalIgnoreCase);
        }

        private static int MergeOutfitCategoryRank(AppearanceOutfitPickItem item)
        {
            if (item == null) return 99;
            if (item.Kind == AppearanceOutfitPickKind.Hair) return 3;
            if (item.Kind == AppearanceOutfitPickKind.Skin) return 4;
            string c = item.CategoryLabel;
            if (string.Equals(c, "Garment", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(c, "Accessory", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(c, "Cosmetic", StringComparison.OrdinalIgnoreCase)) return 2;
            return 5;
        }

        private void HideMergeOutfitPicker()
        {
            if (_mergeOutfitModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_mergeOutfitModalRoot); } catch { }
                _mergeOutfitModalRoot = null;
            }
            _mergeOutfitListParent = null;
            _mergeOutfitTitleText = null;
            _mergeOutfitHintText = null;
            _mergeOutfitIncludeSkinHairToggle = null;
            _mergeOutfitCategoryBtnImgs.Clear();
        }

        private void BuildMergeOutfitPickerModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = Mathf.Max(font + 6f * s, 22f * s);

            GameObject panel;
            // Taller panel — more room for item rows (no redundant Close in header).
            _mergeOutfitModalRoot = UI.CreateModalChrome(
                backgroundBoxGO, "VPB_MergeOutfitModal",
                580f * s, 700f * s,
                new Color(0.07f, 0.08f, 0.10f, 1f),
                HideMergeOutfitPicker,
                out panel);

            UI.AddVLG(panel, spacing: 8f * s, padding: UI.Pad(14, 14, 14, 14, s));

            // Title only — Cancel / dim dismiss cover close.
            GameObject header = new GameObject("HeaderRow");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            hh.childForceExpandWidth = true;
            hh.childForceExpandHeight = false;
            UI.AddLE(header, minHeight: titleH, preferredHeight: titleH);

            _mergeOutfitTitleText = UI.CreateEmphasisTitleLabel(header, VPBTranslation.T("gallery.merge_outfit.title", "Merge Outfit"), font);
            GalleryUiMetrics.ApplyFont(_mergeOutfitTitleText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_mergeOutfitTitleText.gameObject, flexibleWidth: 1f, preferredHeight: titleH);

            // Hint
            _mergeOutfitHintText = UI.CreateLabel(panel,
                MergeOutfitHintText(),
                font, new Color(0.75f, 0.75f, 0.78f, 1f), TextAnchor.UpperLeft,
                HorizontalWrapMode.Wrap, name: "Hint");
            GalleryUiMetrics.ApplyFont(_mergeOutfitHintText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_mergeOutfitHintText.gameObject, minHeight: btnH, preferredHeight: btnH + 8f * s);

            // Bulk: Select All / None
            GameObject bulk = new GameObject("BulkRow");
            bulk.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup bh = UI.AddHLG(bulk, spacing: 6f * s, padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: false);
            bh.childForceExpandWidth = false;
            bh.childForceExpandHeight = false;
            UI.AddLE(bulk, minHeight: btnH, preferredHeight: btnH);
            MergeOutfitChromeButton(bulk.transform, 100f * s, btnH,
                VPBTranslation.T("gallery.merge_outfit.select_all", "Select All"), font, s,
                new Color(0.22f, 0.38f, 0.52f, 1f), MergeOutfitSelectAll);
            MergeOutfitChromeButton(bulk.transform, 100f * s, btnH,
                VPBTranslation.T("gallery.merge_outfit.select_none", "Select None"), font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f), MergeOutfitSelectNone);

            // Multi-toggle categories (each on/off independently)
            _mergeOutfitCategoryBtnImgs.Clear();
            GameObject cat = new GameObject("CategoryRow");
            cat.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup ch = UI.AddHLG(cat, spacing: 6f * s, padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: false);
            ch.childForceExpandWidth = false;
            ch.childForceExpandHeight = false;
            UI.AddLE(cat, minHeight: btnH, preferredHeight: btnH);
            MergeOutfitAddCategoryToggle(cat.transform, "Garment",
                VPBTranslation.T("gallery.merge_outfit.cat_garments", "Garments"), 88f * s, btnH, font, s);
            MergeOutfitAddCategoryToggle(cat.transform, "Accessory",
                VPBTranslation.T("gallery.merge_outfit.cat_accessories", "Accessories"), 96f * s, btnH, font, s);
            MergeOutfitAddCategoryToggle(cat.transform, "Cosmetic",
                VPBTranslation.T("gallery.merge_outfit.cat_cosmetics", "Cosmetics"), 88f * s, btnH, font, s);
            MergeOutfitAddCategoryToggle(cat.transform, "Hair",
                VPBTranslation.T("gallery.merge_outfit.cat_hair", "Hair"), 64f * s, btnH, font, s);
            MergeOutfitAddCategoryToggle(cat.transform, "Skin",
                VPBTranslation.T("gallery.merge_outfit.cat_skin", "Skin"), 64f * s, btnH, font, s);
            RefreshMergeOutfitCategoryButtons();

            // Include skin & hair toggle
            GameObject includeRow = new GameObject("IncludeSkinHairRow");
            includeRow.transform.SetParent(panel.transform, false);
            UI.AddLE(includeRow, minHeight: btnH, preferredHeight: btnH);
            GameObject toggleGo = UI.CreateUIToggle(
                includeRow, 0, btnH,
                VPBTranslation.T("gallery.merge_outfit.include_skin_hair", "Include skin & hair"),
                font, 0, 0, AnchorPresets.stretchAll,
                OnMergeOutfitIncludeSkinHairChanged);
            _mergeOutfitIncludeSkinHairToggle = toggleGo.GetComponent<Toggle>();
            if (_mergeOutfitIncludeSkinHairToggle != null)
                _mergeOutfitIncludeSkinHairToggle.isOn = _mergeOutfitIncludeSkinAndHair;
            Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
            if (toggleLabel != null)
                GalleryUiMetrics.ApplyFont(toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(toggleGo, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);

            // Scroll list
            GameObject scrollHost = new GameObject("ScrollHost");
            scrollHost.transform.SetParent(panel.transform, false);
            UI.AddLE(scrollHost, flexibleHeight: 1f, minHeight: 320f * s);
            UI.AddImage(scrollHost, new Color(0.05f, 0.055f, 0.07f, 1f));

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollHost.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f * s, 4f * s);
            vpRt.offsetMax = new Vector2(-4f * s, -4f * s);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup cv = UI.AddVLG(content, spacing: 4f * s, padding: UI.Pad(4, 4, 4, 4, s));
            cv.childForceExpandHeight = false;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _mergeOutfitListParent = content.transform;

            ScrollRect sr = scrollHost.AddComponent<ScrollRect>();
            sr.viewport = vpRt;
            sr.content = contentRt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f * s;

            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();

            // Footer
            GameObject footer = new GameObject("FooterRow");
            footer.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup fh = UI.AddHLG(footer, spacing: 10f * s, padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: true);
            fh.childForceExpandHeight = false;
            UI.AddLE(footer, minHeight: btnH, preferredHeight: btnH);

            MergeOutfitChromeButton(footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.merge_outfit.cancel", "Cancel"), font, s,
                new Color(0.35f, 0.28f, 0.22f, 1f), HideMergeOutfitPicker, flexibleWidth: true);
            MergeOutfitChromeButton(footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.merge_outfit.merge", "Merge"), font, s,
                new Color(0.22f, 0.52f, 0.30f, 1f), ConfirmMergeOutfitPicker, flexibleWidth: true);
        }

        private void MergeOutfitAddCategoryToggle(
            Transform parent, string categoryKey, string label, float width, float height, int font, float s)
        {
            string key = categoryKey;
            GameObject go = MergeOutfitChromeButton(parent, width, height, label, font, s,
                MergeOutfitCategoryButtonColor(key, on: false), () => MergeOutfitToggleCategory(key));
            Image img = go != null ? go.GetComponent<Image>() : null;
            if (img != null)
                _mergeOutfitCategoryBtnImgs[key] = img;
        }

        private static Color MergeOutfitCategoryButtonColor(string category, bool on)
        {
            Color baseCol = MergeOutfitCategoryHue(category);
            if (on)
                return Color.Lerp(baseCol, Color.white, 0.12f);
            // Dimmed same hue so off state still reads as that category.
            return new Color(baseCol.r * 0.45f, baseCol.g * 0.45f, baseCol.b * 0.45f, 1f);
        }

        private static Color MergeOutfitCategoryRowColor(string category, bool selected)
        {
            Color baseCol = MergeOutfitCategoryHue(category);
            if (selected)
                return Color.Lerp(baseCol, new Color(0.08f, 0.09f, 0.11f, 1f), 0.25f);
            return new Color(baseCol.r * 0.28f, baseCol.g * 0.28f, baseCol.b * 0.28f, 1f);
        }

        private static Color MergeOutfitCategoryHue(string category)
        {
            if (string.Equals(category, "Garment", StringComparison.OrdinalIgnoreCase))
                return new Color(0.22f, 0.42f, 0.62f, 1f);   // blue
            if (string.Equals(category, "Accessory", StringComparison.OrdinalIgnoreCase))
                return new Color(0.18f, 0.52f, 0.48f, 1f);   // teal
            if (string.Equals(category, "Cosmetic", StringComparison.OrdinalIgnoreCase))
                return new Color(0.52f, 0.28f, 0.55f, 1f);   // purple
            if (string.Equals(category, "Hair", StringComparison.OrdinalIgnoreCase))
                return new Color(0.62f, 0.42f, 0.18f, 1f);   // amber
            if (string.Equals(category, "Skin", StringComparison.OrdinalIgnoreCase))
                return new Color(0.58f, 0.32f, 0.30f, 1f);   // rose
            return new Color(0.30f, 0.32f, 0.36f, 1f);
        }

        private bool IsMergeOutfitCategoryFullySelected(string categoryLabel)
        {
            if (_mergeOutfitItems == null || _mergeOutfitSelectedUids == null) return false;
            int total = 0;
            int selected = 0;
            for (int i = 0; i < _mergeOutfitItems.Count; i++)
            {
                AppearanceOutfitPickItem it = _mergeOutfitItems[i];
                if (it == null || string.IsNullOrEmpty(it.Uid)) continue;
                if (!string.Equals(it.CategoryLabel, categoryLabel, StringComparison.OrdinalIgnoreCase)) continue;
                total++;
                if (_mergeOutfitSelectedUids.Contains(it.Uid)) selected++;
            }
            return total > 0 && selected == total;
        }

        private void RefreshMergeOutfitCategoryButtons()
        {
            if (_mergeOutfitCategoryBtnImgs.Count == 0) return;
            foreach (var kv in _mergeOutfitCategoryBtnImgs)
            {
                if (kv.Value == null) continue;
                bool on = IsMergeOutfitCategoryFullySelected(kv.Key);
                kv.Value.color = MergeOutfitCategoryButtonColor(kv.Key, on);
            }
        }

        private void MergeOutfitToggleCategory(string categoryLabel)
        {
            if (_mergeOutfitItems == null || _mergeOutfitSelectedUids == null) return;
            if (!_mergeOutfitIncludeSkinAndHair
                && (string.Equals(categoryLabel, "Hair", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryLabel, "Skin", StringComparison.OrdinalIgnoreCase)))
            {
                _mergeOutfitIncludeSkinAndHair = true;
                if (_mergeOutfitIncludeSkinHairToggle != null)
                {
                    _mergeOutfitIncludeSkinHairToggle.onValueChanged.RemoveListener(OnMergeOutfitIncludeSkinHairChanged);
                    _mergeOutfitIncludeSkinHairToggle.isOn = true;
                    _mergeOutfitIncludeSkinHairToggle.onValueChanged.AddListener(OnMergeOutfitIncludeSkinHairChanged);
                }
                ReloadMergeOutfitItems(preserveSelection: true);
                if (_mergeOutfitHintText != null)
                    _mergeOutfitHintText.text = MergeOutfitHintText();
            }

            bool turnOn = !IsMergeOutfitCategoryFullySelected(categoryLabel);
            for (int i = 0; i < _mergeOutfitItems.Count; i++)
            {
                AppearanceOutfitPickItem it = _mergeOutfitItems[i];
                if (it == null || string.IsNullOrEmpty(it.Uid)) continue;
                if (!string.Equals(it.CategoryLabel, categoryLabel, StringComparison.OrdinalIgnoreCase)) continue;
                if (turnOn) _mergeOutfitSelectedUids.Add(it.Uid);
                else _mergeOutfitSelectedUids.Remove(it.Uid);
            }
            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();
        }

        private string MergeOutfitHintText()
        {
            if (_mergeOutfitIncludeSkinAndHair)
            {
                return VPBTranslation.T("gallery.merge_outfit.hint_skin_hair",
                    "Pick clothing, hair and/or skin from the appearance to merge onto the current look.");
            }
            return VPBTranslation.T("gallery.merge_outfit.hint",
                "Keep body, skin and hair. Pick clothing from the appearance to add on top of the current outfit.");
        }

        private void OnMergeOutfitIncludeSkinHairChanged(bool on)
        {
            _mergeOutfitIncludeSkinAndHair = on;
            if (_mergeOutfitHintText != null)
                _mergeOutfitHintText.text = MergeOutfitHintText();
            ReloadMergeOutfitItems(preserveSelection: true);
            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();
        }

        private void RebuildMergeOutfitRows()
        {
            if (_mergeOutfitListParent == null) return;
            float s = ChromeScale;
            int font = new GalleryModalTypography(s).Body;
            float rowH = GalleryUiDesignTokens.ButtonSizeRef * s;

            UI.DestroyAllChildren(_mergeOutfitListParent);

            if (_mergeOutfitItems == null || _mergeOutfitItems.Count == 0)
            {
                Text empty = UI.CreateLabel(_mergeOutfitListParent.gameObject,
                    VPBTranslation.T("gallery.merge_outfit.empty",
                        "No clothing in this appearance. Enable Include skin & hair, or pick another look."),
                    font, new Color(0.7f, 0.7f, 0.72f, 1f), TextAnchor.MiddleLeft,
                    HorizontalWrapMode.Wrap, name: "Empty");
                GalleryUiMetrics.ApplyFont(empty, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                UI.AddLE(empty.gameObject, minHeight: rowH + 16f * s, preferredHeight: rowH + 24f * s, flexibleWidth: 1f);
                return;
            }

            float padL = 14f * s;
            for (int i = 0; i < _mergeOutfitItems.Count; i++)
            {
                AppearanceOutfitPickItem item = _mergeOutfitItems[i];
                bool selected = _mergeOutfitSelectedUids != null && _mergeOutfitSelectedUids.Contains(item.Uid);

                GameObject row = UI.CreateUIButton(
                    _mergeOutfitListParent.gameObject, 0, rowH,
                    "", font, 0, 0, AnchorPresets.stretchAll, null);
                row.name = "MergeOutfitRow_" + i;
                Image bg = row.GetComponent<Image>();
                if (bg != null)
                    bg.color = MergeOutfitCategoryRowColor(item.CategoryLabel, selected);
                UI.AddLE(row, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

                Text t = row.GetComponentInChildren<Text>();
                if (t != null)
                {
                    string mark = selected ? "[x] " : "[ ] ";
                    string cat = string.IsNullOrEmpty(item.CategoryLabel) ? "" : "  (" + item.CategoryLabel + ")";
                    t.text = mark + item.DisplayName + cat;
                    t.alignment = TextAnchor.MiddleLeft;
                    t.color = Color.white;
                    RectTransform trt = t.GetComponent<RectTransform>();
                    if (trt != null)
                    {
                        trt.offsetMin = new Vector2(padL, 0f);
                        trt.offsetMax = new Vector2(-8f * s, 0f);
                    }
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                }

                Button btn = row.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    string uid = item.Uid;
                    btn.onClick.AddListener(() => ToggleMergeOutfitItem(uid));
                }
            }
        }

        private void ToggleMergeOutfitItem(string uid)
        {
            if (string.IsNullOrEmpty(uid) || _mergeOutfitSelectedUids == null) return;
            if (_mergeOutfitSelectedUids.Contains(uid)) _mergeOutfitSelectedUids.Remove(uid);
            else _mergeOutfitSelectedUids.Add(uid);
            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();
        }

        private void MergeOutfitSelectAll()
        {
            if (_mergeOutfitItems == null || _mergeOutfitSelectedUids == null) return;
            _mergeOutfitSelectedUids.Clear();
            for (int i = 0; i < _mergeOutfitItems.Count; i++)
            {
                if (!string.IsNullOrEmpty(_mergeOutfitItems[i].Uid))
                    _mergeOutfitSelectedUids.Add(_mergeOutfitItems[i].Uid);
            }
            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();
        }

        private void MergeOutfitSelectNone()
        {
            if (_mergeOutfitSelectedUids == null) return;
            _mergeOutfitSelectedUids.Clear();
            RebuildMergeOutfitRows();
            RefreshMergeOutfitCategoryButtons();
        }

        private void ConfirmMergeOutfitPicker()
        {
            if ((_mergeOutfitSourceEntry == null && _mergeOutfitPresetJC == null)
                || string.IsNullOrEmpty(_mergeOutfitTargetUid))
            {
                HideMergeOutfitPicker();
                return;
            }
            if (_mergeOutfitSelectedUids == null || _mergeOutfitSelectedUids.Count == 0)
            {
                LogUtil.LogWarning("[VPB] Merge Outfit: no items selected.");
                return;
            }

            Atom target = null;
            try { target = SuperController.singleton.GetAtomByUid(_mergeOutfitTargetUid); } catch { }
            if (target == null)
            {
                LogUtil.LogError("[VPB] Merge Outfit: target atom gone.");
                HideMergeOutfitPicker();
                return;
            }

            FileEntry entry = _mergeOutfitSourceEntry;
            JSONClass presetJC = _mergeOutfitPresetJC;
            List<string> selected = new List<string>(_mergeOutfitSelectedUids);
            _mergeOutfitSourceEntry = null;
            _mergeOutfitPresetJC = null;
            _mergeOutfitItems = null;
            _mergeOutfitSelectedUids = null;
            HideMergeOutfitPicker();

            try
            {
                ClothingLoadingUtils.ClothingHairUndoState snap =
                    ClothingLoadingUtils.CaptureClothingHairUndoState(target);
                string atomUid = target.uid;
                PushUndo(() =>
                {
                    Atom a = SuperController.singleton.GetAtomByUid(atomUid);
                    if (a == null) return;
                    ClothingLoadingUtils.RestoreClothingHairUndoState(a, snap);
                    LogUtil.Log("[Gallery] Undo performed on " + atomUid + " (Merge Outfit)");
                });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Merge Outfit undo capture failed: " + ex.Message);
            }

            try
            {
                VpbImport.MergeAppearanceOutfitItems(entry, target, selected, presetJC);
                LogUtil.Log("[VPB] Merge Outfit: merged " + selected.Count + " item(s) onto " + target.uid);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Merge Outfit apply failed: " + ex.Message);
            }
        }

        private static GameObject MergeOutfitChromeButton(
            Transform parent, float width, float height, string label, int fontSize, float chromeScale, Color bg, UnityAction onClick,
            bool flexibleWidth = false)
        {
            float w = (flexibleWidth || width <= 0f) ? 0f : width;
            GameObject go = UI.CreateChromeLayoutButton(parent, w, height, label, fontSize, bg, onClick);
            Text t = go != null ? go.GetComponentInChildren<Text>() : null;
            if (t != null)
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, chromeScale, GalleryUiDesignTokens.FontMinRef);
            return go;
        }
    }
}
