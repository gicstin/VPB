using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Transform importSidebarTypeRadioContainer;
        private Transform importSidebarOptionsPanelHost;
        private readonly Dictionary<VpbResourceType, GameObject> importSidebarTypeRadioButtons
            = new Dictionary<VpbResourceType, GameObject>();
        private Button importSidebarApplyButton;
        private Text importSidebarApplyButtonLabel;

        // Plugin picker: a caption + a pooled list of checkbox rows, rebuilt from the source atom's plugins.
        private GameObject importSidebarPluginChecklistRoot;
        private const int ImportSidebarMaxPluginRows = 24;
        private readonly List<GameObject> importSidebarPluginRowPool = new List<GameObject>(ImportSidebarMaxPluginRows);
        // Enumerated once per source/target change; checkbox clicks re-skin from this, never re-parse the atom.
        private List<ImportPluginEntry> importSidebarPluginEntries = new List<ImportPluginEntry>();

        private sealed class ImportPluginEntry
        {
            public string Key;      // plugin#N
            public string Name;     // parsed from the plugin url
            public string Label;    // author's pluginLabel, or "" when none was set
            public bool OnTarget;   // an identical plugin url is already on the target atom
        }

        // Appearance conditional rows (visibility driven by the suppress-clothing toggle).
        private GameObject importSidebarOnlySuppressRealRow;
        private GameObject importSidebarImportCUARow;

        // Re-reads each option toggle's checkbox text on demand so external changes (toolbox Suppress-scale) sync in.
        private readonly List<System.Action> importSidebarOptionToggleRefreshers = new List<System.Action>();

        private static readonly VpbResourceType[] ImportSidebarTypeOrder = new VpbResourceType[]
        {
            VpbResourceType.Appearance,
            VpbResourceType.Clothing,
            VpbResourceType.Hair,
            VpbResourceType.Pose,
            VpbResourceType.Skin,
            VpbResourceType.Morphs,
            VpbResourceType.BreastPhysics,
            VpbResourceType.Glute,
            VpbResourceType.Plugins,
            VpbResourceType.General,
        };

        // Adds the type-radio grid + per-type option-panel host into the single body-scroll content (Apply is a
        // separate pinned button). Host height is set per active type in OnImportSidebarTypeChosen.
        private void BuildImportSidebarOptionsRows(Transform content)
        {
            if (content == null) return;

            BuildImportSidebarTypeRadio(content);
            BuildImportSidebarOptionsPanelHost(content);

            RefreshTypeRadioVisibility();
            // Restore the persisted last-used type (LoadImportSidebarPrefs ran before the panels were built).
            OnImportSidebarTypeChosen(importSidebarPresetType);
        }

        private void BuildImportSidebarTypeRadio(Transform parent)
        {
            GameObject grid = new GameObject("TypeRadio");
            grid.transform.SetParent(parent, false);

            RectTransform rt = grid.AddComponent<RectTransform>();
            importSidebarTypeRadioContainer = rt;
            // The panel is a constant 220px wide (root width is not scaled), so the 2 cells must fit a fixed content
            // width (220 - ~10px scrollbar - spacing) / 2; scaling cell WIDTH by s overflows. Only the height scales.
            const float typeRadioCellW = (ImportSidebarBaseWidth - 10f - 2f) / 2f;  // ~104
            const int typeRadioRows = 5;  // 10 types / 2 columns
            LayoutElement le = grid.AddComponent<LayoutElement>();
            le.preferredHeight = typeRadioRows * 26f + (typeRadioRows - 1) * 2f;
            le.flexibleWidth = 1f;

            GridLayoutGroup g = grid.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(typeRadioCellW, 26f);
            g.spacing = new Vector2(2f, 2f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;

            LayoutElement leCaptured = le;
            GridLayoutGroup gCaptured = g;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = (typeRadioRows * 26f + (typeRadioRows - 1) * 2f) * s;
                if (gCaptured != null)
                {
                    gCaptured.cellSize = new Vector2(typeRadioCellW, 26f * s);  // width fixed (panel width is fixed)
                    gCaptured.spacing = new Vector2(2f * s, 2f * s);
                }
            });

            for (int i = 0; i < ImportSidebarTypeOrder.Length; i++)
            {
                VpbResourceType t = ImportSidebarTypeOrder[i];
                GameObject row = new GameObject("Type_" + t);
                row.transform.SetParent(grid.transform, false);
                Image rb = row.AddComponent<Image>();
                // Inactive cell uses ColorInactiveRow (matches Creator/Category tab rows);
                // active cell is set to ColorCategory in OnImportSidebarTypeChosen.
                rb.color = ColorInactiveRow;
                Button b = row.AddComponent<Button>();
                b.targetGraphic = rb;
                UI.NeutralizeSelectableColorTint(b);

                Text typeLabel = CreateImportSidebarLabel(row.transform, ShortNameForType(t), ImportSidebarBaseFontSize);
                typeLabel.alignment = TextAnchor.MiddleCenter;

                Text typeLabelCaptured = typeLabel;
                innerPaneScaleActions.Add(s => ApplyScaledFont(typeLabelCaptured, ImportSidebarBaseFontSize, s));

                VpbResourceType captured = t;
                b.onClick.AddListener(() => OnImportSidebarTypeChosen(captured));

                importSidebarTypeRadioButtons[t] = row;
            }
        }

        private static string ShortNameForType(VpbResourceType t)
        {
            switch (t)
            {
                case VpbResourceType.Appearance:    return "Appear.";
                case VpbResourceType.Clothing:      return "Clothing";
                case VpbResourceType.Hair:          return "Hair";
                case VpbResourceType.Pose:          return "Pose";
                case VpbResourceType.Skin:          return "Skin";
                case VpbResourceType.Morphs:        return "Morphs";
                case VpbResourceType.BreastPhysics: return "Breast";
                case VpbResourceType.Glute:         return "Glute";
                case VpbResourceType.Plugins:       return "Plugins";
                case VpbResourceType.General:       return "General";
                default: return t.ToString();
            }
        }

        private void RefreshTypeRadioVisibility()
        {
            bool show = currentCategoryTitle == "Scenes";
            if (importSidebarTypeRadioContainer != null)
                importSidebarTypeRadioContainer.gameObject.SetActive(show);
        }

        private void BuildImportSidebarOptionsPanelHost(Transform parent)
        {
            GameObject host = new GameObject("PanelHost");
            host.transform.SetParent(parent, false);

            RectTransform rt = host.AddComponent<RectTransform>();
            importSidebarOptionsPanelHost = rt;
            // Fixed (scaled) preferred height inside the body scroll, sized for ~6 toggle rows. Not flexibleHeight:
            // the active panel must sit directly under the type radio, and the body scroll absorbs any overflow.
            const float optionsHostRows = 6f;
            LayoutElement le = host.AddComponent<LayoutElement>();
            le.preferredHeight = optionsHostRows * ImportSidebarBaseRowHeight;
            le.flexibleWidth = 1f;

            LayoutElement leCaptured = le;
            innerPaneScaleActions.Add(s => { if (leCaptured != null) leCaptured.preferredHeight = optionsHostRows * ImportSidebarBaseRowHeight * s; });

            foreach (VpbResourceType t in ImportSidebarTypeOrder)
            {
                GameObject panel = BuildOptionPanelFor(t, host.transform);
                importSidebarOptionPanels[t] = panel;
                panel.SetActive(false);
            }
        }

        private GameObject BuildOptionPanelFor(VpbResourceType t, Transform parent)
        {
            GameObject panel = new GameObject("Panel_" + t);
            panel.transform.SetParent(parent, false);

            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;

            VerticalLayoutGroup vlgCaptured = vlg;
            innerPaneScaleActions.Add(s => { if (vlgCaptured != null) vlgCaptured.spacing = 2f * s; });

            switch (t)
            {
                case VpbResourceType.Appearance:
                    AddOptionToggle(panel.transform, "Disable scale change",
                        () => importSidebarSuppressScale, v => importSidebarSuppressScale = v, null);
                    AddOptionToggle(panel.transform, "Disable clothing load",
                        () => importSidebarSuppressClothingLoad,
                        v => { importSidebarSuppressClothingLoad = v; RefreshAppearanceConditionalRows(); }, null);
                    importSidebarOnlySuppressRealRow = AddOptionToggle(panel.transform, "  Only disable real clothing",
                        () => importSidebarOnlySuppressRealClothing, v => importSidebarOnlySuppressRealClothing = v, null);
                    importSidebarImportCUARow = AddOptionToggle(panel.transform, "Import atom CUAs",
                        () => importSidebarImportLinkedCUAs, v => importSidebarImportLinkedCUAs = v, null);
                    AddOptionToggle(panel.transform, "Delete current atom CUAs",
                        () => importSidebarDeleteTargetCUAs, v => importSidebarDeleteTargetCUAs = v,
                        new Color(0.91f, 0.53f, 0.53f, 1f));
                    RefreshAppearanceConditionalRows();
                    break;

                case VpbResourceType.Clothing:
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarMergeClothingOrHair, v => importSidebarMergeClothingOrHair = v, null);
                    AddOptionToggle(panel.transform, "Only replace \"real\" clothing",
                        () => importSidebarOnlyReplaceRealClothing, v => importSidebarOnlyReplaceRealClothing = v, null);
                    break;

                case VpbResourceType.Hair:
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarMergeClothingOrHair, v => importSidebarMergeClothingOrHair = v, null);
                    break;

                case VpbResourceType.Pose:
                    AddOptionToggle(panel.transform, "Disable morph load",
                        () => importSidebarSubToggles.SuppressMorphLoad,
                        v => importSidebarSubToggles.SuppressMorphLoad = v, null);
                    AddOptionToggle(panel.transform, "Disable root-node load",
                        () => importSidebarSubToggles.SuppressRootNodeLoad,
                        v => importSidebarSubToggles.SuppressRootNodeLoad = v, null);
                    break;

                case VpbResourceType.Morphs:
                    AddOptionToggle(panel.transform, "Include Appearance morphs",
                        () => importSidebarSubToggles.IncludeAppearanceMorphs,
                        v => importSidebarSubToggles.IncludeAppearanceMorphs = v, null);
                    AddOptionToggle(panel.transform, "Include Physical/Pose morphs",
                        () => importSidebarSubToggles.IncludePhysicalPoseMorphs,
                        v => importSidebarSubToggles.IncludePhysicalPoseMorphs = v, null);
                    break;

                case VpbResourceType.Plugins:
                    AddOptionToggle(panel.transform, "Pick plugins to import",
                        () => importSidebarPluginsMergeSingle,
                        v => { importSidebarPluginsMergeSingle = v; RefreshPluginChecklist(); },
                        null);
                    BuildImportSidebarPluginChecklist(panel.transform);
                    break;

                case VpbResourceType.General:
                    AddOptionToggle(panel.transform, "Include Physical",
                        () => importSidebarSubToggles.IncludePhysical,
                        v => importSidebarSubToggles.IncludePhysical = v, null);
                    AddOptionToggle(panel.transform, "Include Pose",
                        () => importSidebarSubToggles.IncludePose,
                        v => importSidebarSubToggles.IncludePose = v, null);
                    AddOptionToggle(panel.transform, "Include Appearance",
                        () => importSidebarSubToggles.IncludeAppearance,
                        v => importSidebarSubToggles.IncludeAppearance = v, null);
                    GameObject mocapRow = AddOptionToggle(panel.transform, "Include Mocap",
                        () => importSidebarSubToggles.IncludeMocap,
                        v => importSidebarSubToggles.IncludeMocap = v, null);
                    Button mocapBtn = mocapRow.GetComponent<Button>();
                    if (mocapBtn != null) mocapBtn.interactable = false;
                    break;

                case VpbResourceType.Skin:
                case VpbResourceType.BreastPhysics:
                case VpbResourceType.Glute:
                    break;
            }
            return panel;
        }

        private GameObject AddOptionToggle(Transform parent, string label, System.Func<bool> get, System.Action<bool> set, Color? labelColor)
        {
            GameObject row = new GameObject("Toggle_" + label);
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight;
            le.flexibleWidth = 1f;

            Image bg = row.AddComponent<Image>();
            // Same neutral row tone as Creator/Category list rows; checked-state is shown
            // by the "[x] " prefix and the bg toggling to the active accent.
            bg.color = ColorInactiveRow;

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);

            // labelColor stays an override for the destructive "Delete target linked CUAs" row,
            // which must visibly stand out from neutral toggles.
            Color tc = labelColor ?? UI.PopupText;
            Text t = AddSimpleLabelText(row.transform, "", ImportSidebarBaseFontSize, tc);
            t.text = (get() ? "[x] " : "[ ] ") + label;

            btn.onClick.AddListener(() =>
            {
                bool nv = !get();
                set(nv);
                t.text = (nv ? "[x] " : "[ ] ") + label;
                SaveImportSidebarPrefs();
            });

            importSidebarOptionToggleRefreshers.Add(() =>
            {
                if (t != null) t.text = (get() ? "[x] " : "[ ] ") + label;
            });

            LayoutElement leCaptured = le;
            Text tCaptured = t;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(tCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        private Text AddSimpleLabelText(Transform parent, string label, int size, Color color)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);

            Text t = go.AddComponent<Text>();
            t.color = color;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleLeft;
            t.text = label;
            VPBUiFont.ApplyTo(t);
            t.raycastTarget = false;
            return t;
        }

        private void BuildImportSidebarPluginChecklist(Transform parent)
        {
            // Scroll view fills the plugin panel below the "Pick plugins" toggle (flexibleHeight in the panel's VLG);
            // the row list scrolls when it overflows, leaving the Apply button fixed at the section bottom.
            importSidebarPluginChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = importSidebarPluginChecklistRoot.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.flexibleWidth = 1f;

            Transform content = importSidebarPluginChecklistRoot.GetComponent<ScrollRect>().content.transform;

            Text caption = AddSimpleLabelText(content,
                "Plugins in source (check to import)", ImportSidebarBaseFontSize, UI.PopupMutedText);
            LayoutElement capLe = caption.gameObject.AddComponent<LayoutElement>();
            capLe.preferredHeight = ImportSidebarBaseRowHeight;
            capLe.flexibleWidth = 1f;
            Text capCaptured = caption;
            LayoutElement capLeCaptured = capLe;
            innerPaneScaleActions.Add(s => {
                if (capLeCaptured != null) capLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(capCaptured, ImportSidebarBaseFontSize, s);
            });

            for (int i = 0; i < ImportSidebarMaxPluginRows; i++)
            {
                GameObject row = CreateImportSidebarPluginRow(content, i);
                importSidebarPluginRowPool.Add(row);
                row.SetActive(false);
            }
            importSidebarPluginChecklistRoot.SetActive(false);
        }

        private GameObject CreateImportSidebarPluginRow(Transform parent, int index)
        {
            GameObject row = new GameObject("PluginRow_" + index);
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight;
            le.flexibleWidth = 1f;

            Image bg = row.AddComponent<Image>();
            bg.color = ColorInactiveRow;

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);

            Text label = CreateImportSidebarLabel(row.transform, "", ImportSidebarBaseFontSize);

            LayoutElement leCaptured = le;
            Text txtCaptured = label;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        // Rebuilds the checkbox list from the selected source atom's plugins. Visible only for Plugins + gate on.
        // Switching source atom resets the checks to "all" (sig change); within the same atom, checks are preserved.
        private void RefreshPluginChecklist()
        {
            bool show = importSidebarPresetType == VpbResourceType.Plugins && importSidebarPluginsMergeSingle;
            if (importSidebarPluginChecklistRoot != null) importSidebarPluginChecklistRoot.SetActive(show);
            if (!show || importSidebarPluginRowPool.Count == 0) return;

            importSidebarPluginEntries = BuildSourcePluginEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarPluginSelectionSig)
            {
                importSidebarSelectedPluginKeys.Clear();
                foreach (ImportPluginEntry e in importSidebarPluginEntries) importSidebarSelectedPluginKeys.Add(e.Key);
                importSidebarPluginSelectionSig = sig;
            }

            RenderPluginChecklistRows();
        }

        // Skins the pooled rows from the already-enumerated entries; no atom re-parse (the toggle path uses this).
        private void RenderPluginChecklistRows()
        {
            for (int i = 0; i < importSidebarPluginRowPool.Count; i++)
            {
                GameObject row = importSidebarPluginRowPool[i];
                if (i < importSidebarPluginEntries.Count) { ConfigurePluginRow(row, importSidebarPluginEntries[i]); row.SetActive(true); }
                else row.SetActive(false);
            }
        }

        private void ConfigurePluginRow(GameObject row, ImportPluginEntry e)
        {
            bool selected = importSidebarSelectedPluginKeys.Contains(e.Key);
            string num = e.Key.StartsWith("plugin#", StringComparison.Ordinal) ? e.Key.Substring(7) : e.Key;
            string text = (selected ? "[x] #" : "[ ] #") + num + "  " + e.Name;
            if (!string.IsNullOrEmpty(e.Label)) text += " (" + e.Label + ")";
            if (e.OnTarget) text += "  (on target)";

            Text t = row.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
            Image bg = row.GetComponent<Image>();
            if (bg != null) bg.color = selected ? ColorCategory : ColorInactiveRow;

            Button btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                string key = e.Key;
                btn.onClick.AddListener(() => TogglePluginSelected(key));
            }
        }

        private void TogglePluginSelected(string key)
        {
            if (importSidebarSelectedPluginKeys.Contains(key)) importSidebarSelectedPluginKeys.Remove(key);
            else importSidebarSelectedPluginKeys.Add(key);
            RenderPluginChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private List<ImportPluginEntry> BuildSourcePluginEntries()
        {
            var result = new List<ImportPluginEntry>();
            if (importSidebarSourceScene == null) return result;
            JSONClass preset = BuildPresetJSONForCurrentSelection();
            JSONArray storables = (preset != null && preset["storables"] != null) ? preset["storables"].AsArray : null;
            if (storables == null) return result;

            JSONClass pluginsDict = null;
            var labels = new Dictionary<string, string>();
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"] != null ? s["id"].Value : "";
                if (string.Equals(id, "PluginManager", StringComparison.Ordinal))
                {
                    pluginsDict = s["plugins"] != null ? s["plugins"].AsObject : null;
                }
                else if (id.StartsWith("plugin#", StringComparison.Ordinal))
                {
                    int us = id.IndexOf('_');
                    if (us > 0)
                    {
                        string lbl = s["pluginLabel"] != null ? s["pluginLabel"].Value : "";
                        if (!string.IsNullOrEmpty(lbl)) labels[id.Substring(0, us)] = lbl;
                    }
                }
            }
            if (pluginsDict == null) return result;

            HashSet<string> targetUrls = GetTargetPluginUrls();
            foreach (string key in pluginsDict.Keys)
            {
                string url = pluginsDict[key] != null ? pluginsDict[key].Value : "";
                result.Add(new ImportPluginEntry
                {
                    Key = key,
                    Name = ParsePluginName(url),
                    Label = labels.ContainsKey(key) ? labels[key] : "",
                    OnTarget = !string.IsNullOrEmpty(url) && targetUrls.Contains(url.Trim())
                });
            }
            // Matching plugins first, then by slot number.
            result.Sort((a, b) =>
            {
                if (a.OnTarget != b.OnTarget) return a.OnTarget ? -1 : 1;
                return PluginKeyNumber(a.Key).CompareTo(PluginKeyNumber(b.Key));
            });
            return result;
        }

        private HashSet<string> GetTargetPluginUrls()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (importSidebarTargetAtom == null) return set;
            try
            {
                JSONStorable pm = importSidebarTargetAtom.GetStorableByID("PluginManager");
                if (pm != null)
                {
                    JSONClass j = pm.GetJSON();
                    JSONClass plugins = (j != null && j["plugins"] != null) ? j["plugins"].AsObject : null;
                    if (plugins != null)
                        foreach (string k in plugins.Keys)
                        {
                            string u = plugins[k] != null ? plugins[k].Value : "";
                            if (!string.IsNullOrEmpty(u)) set.Add(u.Trim());
                        }
                }
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB import] target plugin scan failed: " + ex.Message); }
            return set;
        }

        private static int PluginKeyNumber(string key)
        {
            int h = key != null ? key.IndexOf('#') : -1;
            int n;
            return (h >= 0 && int.TryParse(key.Substring(h + 1), out n)) ? n : int.MaxValue;
        }

        private static string ParsePluginName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "(plugin)";
            string u = url.Replace('\\', '/');
            int slash = u.LastIndexOf('/');
            string f = slash >= 0 ? u.Substring(slash + 1) : u;
            int dot = f.LastIndexOf('.');
            if (dot > 0) f = f.Substring(0, dot);
            return string.IsNullOrEmpty(f) ? "(plugin)" : f;
        }

        // "only suppress real" is meaningful only while clothing is locked (suppress-clothing ON). "import linked CUAs"
        // applies as a separate additive clothing merge after the appearance load, so it is valid in either mode.
        private void RefreshAppearanceConditionalRows()
        {
            if (importSidebarOnlySuppressRealRow != null)
                importSidebarOnlySuppressRealRow.SetActive(importSidebarSuppressClothingLoad);
            if (importSidebarImportCUARow != null)
                importSidebarImportCUARow.SetActive(true);
        }

        private void RefreshImportSidebarOptionToggles()
        {
            foreach (System.Action refresh in importSidebarOptionToggleRefreshers)
            {
                try { if (refresh != null) refresh(); } catch { }
            }
        }

        private void BuildImportSidebarApplyButton(Transform parent)
        {
            GameObject btn = new GameObject("Load");
            btn.transform.SetParent(parent, false);

            // Pinned to the root bottom (outside the body scroll) so it never shrinks or scrolls out of view.
            // sizeDelta.y is set by ApplyImportSidebarBaseRect; here just establish the bottom-stretch anchor.
            RectTransform rt = btn.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, ImportSidebarBaseApplyHeight);

            Image bg = btn.AddComponent<Image>();
            // Accent-blue: the Apply button is the single terminal action of the whole sidebar,
            // so it gets the most-saturated treatment to draw the eye.
            bg.color = new Color(0.16f, 0.36f, 0.56f, 1f);

            Button b = btn.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(OnImportSidebarApplyClicked);
            UI.NeutralizeSelectableColorTint(b);

            importSidebarApplyButton = b;
            importSidebarApplyButtonLabel = AddSimpleLabelText(btn.transform, "Apply", 18, UI.PopupText);
            importSidebarApplyButtonLabel.alignment = TextAnchor.MiddleCenter;

            Text labelCaptured = importSidebarApplyButtonLabel;
            innerPaneScaleActions.Add(s => ApplyScaledFont(labelCaptured, 18, s));
        }

        private void OnImportSidebarTypeChosen(VpbResourceType t)
        {
            foreach (var kv in importSidebarOptionPanels)
                kv.Value.SetActive(kv.Key == t);
            importSidebarPresetType = t;
            foreach (var kv in importSidebarTypeRadioButtons)
            {
                Image img = kv.Value.GetComponent<Image>();
                // Active = ColorCategory (red), inactive = ColorInactiveRow (grey). Same exact
                // pair Creator/Category tab rows use for their active/inactive states.
                if (img != null)
                    img.color = kv.Key == t ? ColorCategory : ColorInactiveRow;
            }
            RefreshPluginChecklist();
            RefreshApplyButtonEnabled();
            // Swapping the active panel changes the scroll content's total height; force the VLG/CSF to recompute.
            RebuildImportSidebarContent();
            // Guard: skip the build-time call (importSidebarBuilt set after BuildImportSidebar); persist user picks only.
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        partial void RefreshApplyButtonEnabled()
        {
            // Gate on the picker ids, not loadedSceneJSON: a cache-hit click leaves loadedSceneJSON null
            // yet still has person ids, so requiring a selected atom must hold on both paths.
            bool needSourceAtom = importSidebarSourcePersonIds.Count > 0;
            bool sourceOk = !needSourceAtom || !string.IsNullOrEmpty(importSidebarSourceAtomId);
            bool targetOk = importSidebarTargetAtom != null;
            bool sceneOk = importSidebarSourceScene != null;

            if (importSidebarApplyButton != null)
                importSidebarApplyButton.interactable = sourceOk && targetOk && sceneOk;
        }

        private void OnImportSidebarApplyClicked()
        {
            if (importSidebarTargetAtom == null)
            {
                LogUtil.LogWarning("[VPB import] No target atom selected.");
                return;
            }
            if (importSidebarSourceScene == null)
            {
                LogUtil.LogWarning("[VPB import] No source scene loaded.");
                return;
            }

            // CUAs import as native CustomUnityAsset atoms (after the appearance apply), independent of clothing mode.
            bool importCUAs = importSidebarPresetType == VpbResourceType.Appearance && importSidebarImportLinkedCUAs;

            JSONClass presetJSON = BuildPresetJSONForCurrentSelection();
            if (presetJSON == null)
            {
                LogUtil.LogWarning("[VPB import] Could not build preset JSON.");
                return;
            }

            string storableOverride = ResolveOptionalStorableOverrideForCurrentType();

            // Appearance: suppress-clothing maps to Keep (locks ClothingPresets so source clothing is skipped),
            // else Replace. Clothing/Hair use the merge toggle. Other types ignore the mode.
            ClothingApplyMode mode;
            if (importSidebarPresetType == VpbResourceType.Appearance)
                mode = importSidebarSuppressClothingLoad ? ClothingApplyMode.Keep : ClothingApplyMode.Replace;
            else
                mode = importSidebarMergeClothingOrHair ? ClothingApplyMode.Merge : ClothingApplyMode.Replace;

            // LoadPreset has no subToggles param: prune opted-out sub-trees here, on the fresh deep copy
            // from BuildPresetJSONForCurrentSelection (never the cached scene JSON).
            presetJSON = VpbImportSubToggleFilter.FilterForType(presetJSON, importSidebarPresetType, importSidebarSubToggles);
            // A null here would make LoadPreset fall through to reading the whole scene file and applying every atom.
            if (presetJSON == null)
            {
                LogUtil.LogWarning("[VPB import] Sub-toggle doesn't exist; nothing to apply.");
                return;
            }

            // Plugins subset: prune to the checked plugins and force Merge so they add to the target (new UIDs)
            // without dropping its existing plugins; off = import all (the whole PluginPresets storable).
            if (importSidebarPresetType == VpbResourceType.Plugins && importSidebarPluginsMergeSingle)
            {
                JSONClass pluginSlice = VpbImport.BuildSelectedPluginsSlice(presetJSON, importSidebarSelectedPluginKeys);
                if (pluginSlice == null)
                {
                    LogUtil.LogWarning("[VPB import] No plugins selected; nothing to import.");
                    return;
                }
                presetJSON = pluginSlice;
                mode = ClothingApplyMode.Merge;
            }

            // BreastPhysics / Glute / Plugins / Skin lack a dedicated dispatch case: route them through General
            // by their storable name (General aborts loud if the storable is missing).
            VpbResourceType dispatchType = importSidebarPresetType;
            if (storableOverride != null
                && (dispatchType == VpbResourceType.BreastPhysics
                    || dispatchType == VpbResourceType.Glute
                    || dispatchType == VpbResourceType.Plugins
                    || dispatchType == VpbResourceType.Skin
                    || dispatchType == VpbResourceType.Morphs))
            {
                dispatchType = VpbResourceType.General;
            }

            string sourceHostUid = (importSidebarSourceScene is VarFileEntry sceneVar && sceneVar.Package != null)
                ? sceneVar.Package.Uid : null;

            // Rewrite SELF: refs (e.g. clothing material customTexture_*Tex) to the source package uid: untouched,
            // VaM resolves SELF: against the loaded (target) scene's package and the texture fails as "not valid".
            if (!string.IsNullOrEmpty(sourceHostUid))
                VPB.src.util.JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(presetJSON, sourceHostUid);

            // Scope dep prep to this slice (not the 190-dep whole scene). StringBuilder serializer:
            // SimpleJSON .ToString() is O(N^2) and heap-bombs a multi-MB person atom.
            string sliceJson = VPB.src.util.JsonSerializationUtil.Serialize(presetJSON, 1 << 20);
            try
            {
                SceneLoadingUtils.PrewarmAndEnsureForPresetSlice(sliceJson, sourceHostUid);
                VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("vpb_import_slice_prewarm_flush");
            }
            catch (System.Exception ex)
            {
                LogUtil.LogWarning("[VPB import] Slice dependency prep failed: " + ex.Message);
            }

            // Registration alone doesn't rebuild VaM's clothing/hair catalog, so newly-prewarmed item packages
            // would report "is missing"; rebuild the target's catalogs before apply.
            RefreshTargetClothingAndHairCatalog(importSidebarTargetAtom);

            // Only suppress real clothing: keep the target's real garments, bring the source's non-real (cosmetic)
            // clothing. Drop target old non-real -> merge source non-real -> appearance with the Keep clothing lock.
            if (dispatchType == VpbResourceType.Appearance
                && importSidebarSuppressClothingLoad
                && importSidebarOnlySuppressRealClothing)
            {
                try { ClothingLoadingUtils.RemoveClothingByWearClass(importSidebarTargetAtom, ClothingLoadingUtils.ClothingWearClass.Cosmetic); }
                catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] disable target non-real clothing failed: " + ex.Message); }

                JSONClass nonRealSlice = VpbImport.BuildNonRealClothingSlice(presetJSON);
                if (nonRealSlice != null)
                {
                    VpbImport.LoadPreset(
                        sourceEntry: importSidebarSourceScene,
                        targetAtom: importSidebarTargetAtom,
                        resourceType: VpbResourceType.Clothing,
                        clothingMode: ClothingApplyMode.Merge,
                        presetJC: nonRealSlice,
                        skipDependencyPrewarm: true);
                }
            }

            VpbImport.LoadPreset(
                sourceEntry: importSidebarSourceScene,
                targetAtom: importSidebarTargetAtom,
                resourceType: dispatchType,
                clothingMode: mode,
                presetJC: presetJSON,
                suppressRoot: importSidebarSubToggles.SuppressRootNodeLoad,
                storableNameOverride: storableOverride,
                skipDependencyPrewarm: true,
                suppressScaleChange: importSidebarSuppressScale);

            // Delete-then-import = replace: removing the target's existing CUAs before spawning the new ones means
            // delete only catches pre-existing atoms, and import + delete compose into "replace".
            if (importSidebarPresetType == VpbResourceType.Appearance && importSidebarDeleteTargetCUAs)
                DeleteTargetLinkedCUAs(importSidebarTargetAtom);

            if (importCUAs)
                StartImportLinkedCUAs(importSidebarSourceScene, importSidebarSourceAtomId, importSidebarTargetAtom, sourceHostUid);
        }

        // Reads the whole source scene (CUAs are separate atoms) and spawns each person-linked CUA as a native atom.
        private void StartImportLinkedCUAs(FileEntry source, string sourceAtomId, Atom target, string sourceHostUid)
        {
            if (source == null || string.IsNullOrEmpty(sourceAtomId) || target == null) return;
            JSONClass scene;
            try
            {
                using (FileEntryStreamReader r = source.OpenStreamReader())
                {
                    JSONNode parsed = JSON.Parse(r.ReadToEnd());
                    scene = parsed != null ? parsed.AsObject : null;
                }
            }
            catch (System.Exception ex)
            {
                LogUtil.LogWarning("[VPB][CUA] failed to read source scene for atom import: " + ex.Message);
                return;
            }
            if (scene == null) return;
            StartCoroutine(VPB.src.util.CUAAtomImporter.ImportLinkedCUAsAsAtoms(scene, sourceAtomId, target, sourceHostUid));
        }

        // Removes live CustomUnityAsset atoms whose control links (transitively through CUA chains) to the target
        // person. Collect-then-remove so we don't mutate the atom list mid-enumeration.
        private void DeleteTargetLinkedCUAs(Atom target)
        {
            if (target == null || SuperController.singleton == null) return;
            List<Atom> toRemove = new List<Atom>();
            foreach (Atom a in SuperController.singleton.GetAtoms())
            {
                if (a == null || a.type != "CustomUnityAsset") continue;
                if (CUALinksToTarget(a, target)) toRemove.Add(a);
            }
            foreach (Atom a in toRemove)
            {
                try { SuperController.singleton.RemoveAtom(a); }
                catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] delete target CUA " + a.uid + " failed: " + ex.Message); }
            }
            if (toRemove.Count > 0) LogUtil.Log("[VPB import] Removed " + toRemove.Count + " target-linked CUA(s).");
        }

        private static bool CUALinksToTarget(Atom cua, Atom target)
        {
            Atom current = cua;
            HashSet<Atom> seen = new HashSet<Atom>();
            for (int i = 0; i < 16 && current != null && seen.Add(current); i++)
            {
                Atom linked = GetControllerLinkedAtom(current);
                if (linked == null) return false;
                if (linked == target) return true;
                if (linked.type != "CustomUnityAsset") return false;  // only chain through CUA->CUA links
                current = linked;
            }
            return false;
        }

        // Rebuild the target Person's clothing + hair catalogs so items from packages the slice prewarm just
        // registered (and loose CUA-converted clothing) resolve on apply instead of reporting "is missing".
        private void RefreshTargetClothingAndHairCatalog(Atom target)
        {
            if (target == null) return;
            try
            {
                DAZClothingItemControl cc = target.GetComponentInChildren<DAZClothingItemControl>();
                if (cc != null) cc.RefreshClothingItems();
            }
            catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] RefreshClothingItems failed: " + ex.Message); }
            try
            {
                DAZHairGroupControl hc = target.GetComponentInChildren<DAZHairGroupControl>();
                if (hc != null) hc.RefreshHairItems();
            }
            catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] RefreshHairItems failed: " + ex.Message); }
        }

        private static Atom GetControllerLinkedAtom(Atom atom)
        {
            FreeControllerV3 fc = atom != null ? atom.mainController : null;
            if (fc == null || fc.linkToRB == null) return null;
            return fc.linkToRB.GetComponentInParent<Atom>();
        }

        private JSONClass BuildPresetJSONForCurrentSelection()
        {
            // A selected source atom must resolve to exactly that one atom, never the whole scene: a stale
            // cache hit or read failure here must not fall through to applying every atom to the target.
            if (!string.IsNullOrEmpty(importSidebarSourceAtomId))
            {
                // Parsed this click (cache miss): slice from the in-memory scene.
                if (importSidebarLoadedSceneJSON != null)
                    return WrapSourceAtomFromScene(importSidebarLoadedSceneJSON, importSidebarSourceAtomId);

                // Cache hit: fetch just the selected atom's JSON (sig-guarded so an edited scene reads null).
                string atomJson = VpbLocalDatabase.TryReadSceneAtomJson(importSidebarSourceScene, importSidebarSourceAtomId);
                if (!string.IsNullOrEmpty(atomJson))
                {
                    JSONClass cached = JSON.Parse(atomJson).AsObject;
                    if (cached != null) return VpbImport.WrapAtomNodeAsPreset(cached);
                }

                // Stale/missing cache: re-parse the scene file and extract the one selected atom.
                try
                {
                    using (FileEntryStreamReader r = importSidebarSourceScene.OpenStreamReader())
                    {
                        JSONClass scene = JSON.Parse(r.ReadToEnd()).AsObject;
                        return WrapSourceAtomFromScene(scene, importSidebarSourceAtomId);
                    }
                }
                catch (System.Exception ex)
                {
                    LogUtil.LogWarning("[VPB import] Failed to re-read source scene for selected atom: " + ex.Message);
                    return null;
                }
            }

            // No selected source atom (non-person preset file): apply the file as-is.
            try
            {
                using (FileEntryStreamReader r = importSidebarSourceScene.OpenStreamReader())
                {
                    JSONNode n = JSON.Parse(r.ReadToEnd());
                    return n != null ? n.AsObject : null;
                }
            }
            catch (System.Exception ex)
            {
                LogUtil.LogWarning("[VPB import] Failed to read preset file: " + ex.Message);
                return null;
            }
        }

        private JSONClass WrapSourceAtomFromScene(JSONClass scene, string atomId)
        {
            if (scene == null) return null;
            JSONArray atoms = scene["atoms"] != null ? scene["atoms"].AsArray : null;
            if (atoms == null) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                // Match the picker's id derivation so the "Person_"+i fallback resolves too.
                string pid = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value)) ? a["id"].Value : ("Person_" + i);
                if (pid != atomId) continue;
                // Deep-copy so the filter / WrapAtomNodeAsPreset don't mutate the cached scene. StringBuilder
                // serializer: SimpleJSON .ToString() is O(N^2) and heap-bombs a multi-MB atom.
                JSONClass fresh = JSON.Parse(VPB.src.util.JsonSerializationUtil.Serialize(a, 1 << 20)).AsObject;
                return VpbImport.WrapAtomNodeAsPreset(fresh);
            }
            return null;
        }

        private string ResolveOptionalStorableOverrideForCurrentType()
        {
            switch (importSidebarPresetType)
            {
                case VpbResourceType.BreastPhysics: return "FemaleBreastPhysicsPresets";
                case VpbResourceType.Glute:         return "FemaleGlutePhysicsPresets";
                case VpbResourceType.Plugins:       return "PluginPresets";
                case VpbResourceType.Skin:          return "SkinPresets";
                case VpbResourceType.Morphs:        return "MorphPresets";
                default: return null;
            }
        }
    }
}
