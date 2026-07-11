using System;
using System.Collections;
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
        private readonly Dictionary<VpbResourceType, Text> importSidebarTypeRadioLabels
            = new Dictionary<VpbResourceType, Text>();
        // Per-type item count parsed from the selected source atom (only for introspectable types:
        // Clothing/Hair/Morphs/Plugins). Absence of a key = "always available, count unknown".
        private readonly Dictionary<VpbResourceType, int> importSidebarSourceTypeCounts
            = new Dictionary<VpbResourceType, int>();
        private Button importSidebarApplyButton;
        private Text importSidebarApplyButtonLabel;

        // Plugin picker: a caption + a pooled list of checkbox rows, rebuilt from the source atom's plugins.
        private GameObject importSidebarPluginChecklistRoot;
        // The checklist's LayoutElement: its preferredHeight is driven by the live entry count so the scroll
        // reserves space. The parent option panel uses a ContentSizeFitter (PreferredSize), under which a bare
        // flexibleHeight collapses to zero, which would hide every plugin row.
        private LayoutElement importSidebarPluginChecklistLe;
        // How many rows are visible before the checklist starts to scroll (drives the reserved height).
        private const int ImportSidebarVisiblePluginRows = 8;
        // Select All / Clear All bulk row above the checklist (visibility tracks the checklist root).
        private GameObject importSidebarPluginBulkRow;
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

        // CUA picker: same pooled-row scaffold as the plugin picker, listing every CustomUnityAsset in the source.
        private GameObject importSidebarCUAChecklistRoot;
        private LayoutElement importSidebarCUAChecklistLe;
        private const int ImportSidebarVisibleCUARows = 8;
        private GameObject importSidebarCUABulkRow;
        private const int ImportSidebarMaxCUARows = 24;
        private readonly List<GameObject> importSidebarCUARowPool = new List<GameObject>(ImportSidebarMaxCUARows);
        private List<ImportCUAEntry> importSidebarCUAEntries = new List<ImportCUAEntry>();
        // Checked CUA atom ids to import; per source-atom (sig tracks scene+atom so switching source reseeds to "all").
        private readonly HashSet<string> importSidebarSelectedCUAKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarCUASelectionSig;

        private sealed class ImportCUAEntry
        {
            public string Id;            // CUA atom id
            public bool LinksToPerson;   // reaches the selected source person (tagged "on person")
        }

        // Appearance conditional rows (visibility driven by the suppress-clothing toggle).
        private GameObject importSidebarOnlySuppressRealRow;
        private GameObject importSidebarImportCUARow;
        private GameObject importSidebarCUARelativeRow;

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
            OnImportSidebarTypeChosen(importSidebarPresetType, true);
        }

        private void BuildImportSidebarTypeRadio(Transform parent)
        {
            // Bulk-select controls: Select All / Clear All / Multi (chips accumulate when on, single-select when off).
            GameObject bulkRow = new GameObject("BulkSelectRow");
            bulkRow.transform.SetParent(parent, false);
            LayoutElement bulkLe = bulkRow.AddComponent<LayoutElement>();
            bulkLe.preferredHeight = ImportSidebarBaseRowHeight * 0.8f;
            bulkLe.flexibleWidth = 1f;
            HorizontalLayoutGroup bulkHlg = bulkRow.AddComponent<HorizontalLayoutGroup>();
            bulkHlg.childForceExpandWidth = true;
            bulkHlg.childForceExpandHeight = true;
            bulkHlg.childControlWidth = true;
            bulkHlg.childControlHeight = true;
            bulkHlg.spacing = 2f;

            BuildImportSidebarBulkButton(bulkRow.transform,
                VPBTranslation.T("gallery.import.select_all_types", "Select All"),
                "gallery.import.select_all_tip", "Select every resource type",
                ImportSidebarSelectAllBg, ImportSidebarSelectAllTypes);
            BuildImportSidebarBulkButton(bulkRow.transform,
                VPBTranslation.T("gallery.import.clear_all_types", "Clear All"),
                "gallery.import.clear_all_tip", "Deselect every resource type",
                ImportSidebarClearAllBg, ImportSidebarClearAllTypes);
            GameObject multiBtn = BuildImportSidebarBulkButton(bulkRow.transform,
                "Multi",
                "gallery.import.multi_select_tip", "When on, type chips accumulate; when off, each click picks one",
                ImportSidebarMultiToggleBg, OnImportSidebarMultiSelectClicked);
            importSidebarMultiToggleBg = multiBtn.GetComponent<Image>();
            importSidebarMultiToggleLabel = multiBtn.GetComponentInChildren<Text>();
            RefreshImportSidebarMultiToggleVisual();

            LayoutElement bulkLeC = bulkLe;
            innerPaneScaleActions.Add(s => {
                if (bulkLeC != null) bulkLeC.preferredHeight = ImportSidebarBaseRowHeight * 0.8f * s;
            });

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
                b.onClick.AddListener(() => OnImportSidebarTypeChosen(captured, importSidebarMultiSelectTypes));

                importSidebarTypeRadioButtons[t] = row;
                importSidebarTypeRadioLabels[t] = typeLabel;
            }
        }

        private GameObject BuildImportSidebarBulkButton(Transform parent, string label,
            string tipKey, string tipDefault, Color bgColor, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnGO = new GameObject("BulkBtn_" + label);
            btnGO.transform.SetParent(parent, false);
            Image bg = btnGO.AddComponent<Image>();
            bg.color = bgColor;
            Button btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);
            Text txt = CreateImportSidebarLabel(btnGO.transform, label, ImportSidebarBaseFontSize);
            txt.alignment = TextAnchor.MiddleCenter;
            btn.onClick.AddListener(onClick);
            AddTooltip(btnGO, tipKey, tipDefault);
            Text txtCaptured = txt;
            innerPaneScaleActions.Add(s => ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s));
            return btnGO;
        }

        // Short label for the current type selection: the single type's name, an "N types" count for several,
        // or a dash for none.
        private string ImportSidebarSelectedTypesSummary()
        {
            int n = importSidebarMultiSelectedTypes.Count;
            if (n == 0) return "\u2014";
            if (n == 1)
            {
                foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
                    return ShortNameForType(t);
            }
            return n + " " + VPBTranslation.T("gallery.import.wizard.multi_type_count", "types");
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
            LayoutElement le = host.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            // Stack active panels vertically; ContentSizeFitter drives the host height to match content.
            VerticalLayoutGroup hostVlg = host.AddComponent<VerticalLayoutGroup>();
            hostVlg.childForceExpandWidth = true;
            hostVlg.childForceExpandHeight = false;
            hostVlg.childControlWidth = true;
            hostVlg.childControlHeight = true;
            hostVlg.spacing = 0f;
            ContentSizeFitter hostCsf = host.AddComponent<ContentSizeFitter>();
            hostCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;

            VerticalLayoutGroup vlgCaptured = vlg;
            innerPaneScaleActions.Add(s => { if (vlgCaptured != null) vlgCaptured.spacing = 2f * s; });

            // Caption so stacked panels read as distinct groups (which options belong to which type).
            AddOptionGroupHeader(panel.transform, DisplayNameForType(t));

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
                        () => importSidebarImportLinkedCUAs,
                        v => { importSidebarImportLinkedCUAs = v; RefreshCUAChecklist(); RefreshAppearanceConditionalRows(); }, null);
                    AddOptionToggle(panel.transform, "Pick CUAs to import",
                        () => importSidebarPickCUAs,
                        v => { importSidebarPickCUAs = v; RefreshCUAChecklist(); }, null);
                    importSidebarCUARelativeRow = AddOptionToggle(panel.transform, "Place off-person relative to person",
                        () => importSidebarCUARelativeToPerson,
                        v => importSidebarCUARelativeToPerson = v, null);
                    BuildImportSidebarCUAChecklist(panel.transform);
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
                    AddOptionToggle(panel.transform, "Migrate self-UID references",
                        () => importSidebarMigratePluginUIDs,
                        v => importSidebarMigratePluginUIDs = v,
                        null);
                    AddOptionToggle(panel.transform, "Clear existing plugins",
                        () => importSidebarClearExistingPlugins,
                        v => importSidebarClearExistingPlugins = v,
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
                    AddOptionGroupNote(panel.transform, "No extra options \u2014 imported as-is.");
                    break;
            }
            return panel;
        }

        private void AddOptionGroupHeader(Transform parent, string label)
        {
            GameObject row = new GameObject("GroupHeader_" + label);
            row.transform.SetParent(parent, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight * 0.82f;
            le.flexibleWidth = 1f;
            Image bg = row.AddComponent<Image>();
            bg.color = ImportSidebarGroupHeaderBg;
            bg.raycastTarget = false;
            Text t = CreateImportSidebarLabel(row.transform, label, ImportSidebarBaseFontSize);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = new Color(0.92f, 0.95f, 1f, 1f);
            LayoutElement leC = le;
            Text tC = t;
            innerPaneScaleActions.Add(s => {
                if (leC != null) leC.preferredHeight = ImportSidebarBaseRowHeight * 0.82f * s;
                ApplyScaledFont(tC, ImportSidebarBaseFontSize, s);
            });
        }

        private void AddOptionGroupNote(Transform parent, string note)
        {
            GameObject row = new GameObject("GroupNote");
            row.transform.SetParent(parent, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight * 0.85f;
            le.flexibleWidth = 1f;
            Text t = AddSimpleLabelText(row.transform, note, ImportSidebarBaseFontSize, new Color(0.62f, 0.64f, 0.68f, 1f));
            t.fontStyle = FontStyle.Italic;
            LayoutElement leC = le;
            Text tC = t;
            innerPaneScaleActions.Add(s => {
                if (leC != null) leC.preferredHeight = ImportSidebarBaseRowHeight * 0.85f * s;
                ApplyScaledFont(tC, ImportSidebarBaseFontSize, s);
            });
        }

        private static string DisplayNameForType(VpbResourceType t)
        {
            switch (t)
            {
                case VpbResourceType.Appearance:    return "Appearance";
                case VpbResourceType.Clothing:      return "Clothing";
                case VpbResourceType.Hair:          return "Hair";
                case VpbResourceType.Pose:          return "Pose";
                case VpbResourceType.Skin:          return "Skin";
                case VpbResourceType.Morphs:        return "Morphs";
                case VpbResourceType.BreastPhysics: return "Breast Physics";
                case VpbResourceType.Glute:         return "Glute";
                case VpbResourceType.Plugins:       return "Plugins";
                case VpbResourceType.General:       return "General";
                default: return t.ToString();
            }
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
            // Select All / Clear All bulk row: a fixed peer above the scroll so the user can include/exclude every
            // source plugin in one click, then refine per-row. Its visibility tracks the checklist (gate ON).
            importSidebarPluginBulkRow = new GameObject("PluginBulkRow");
            importSidebarPluginBulkRow.transform.SetParent(parent, false);
            LayoutElement pluginBulkLe = importSidebarPluginBulkRow.AddComponent<LayoutElement>();
            pluginBulkLe.preferredHeight = ImportSidebarBaseRowHeight * 0.8f;
            pluginBulkLe.flexibleWidth = 1f;
            HorizontalLayoutGroup pluginBulkHlg = importSidebarPluginBulkRow.AddComponent<HorizontalLayoutGroup>();
            pluginBulkHlg.childForceExpandWidth = true;
            pluginBulkHlg.childForceExpandHeight = true;
            pluginBulkHlg.childControlWidth = true;
            pluginBulkHlg.childControlHeight = true;
            pluginBulkHlg.spacing = 2f;
            BuildImportSidebarBulkButton(importSidebarPluginBulkRow.transform,
                VPBTranslation.T("gallery.import.select_all_plugins", "Select All"),
                "gallery.import.select_all_plugins_tip", "Import every plugin in the source",
                ImportSidebarSelectAllBg, ImportSidebarSelectAllPlugins);
            BuildImportSidebarBulkButton(importSidebarPluginBulkRow.transform,
                VPBTranslation.T("gallery.import.clear_all_plugins", "Clear All"),
                "gallery.import.clear_all_plugins_tip", "Exclude every plugin from import",
                ImportSidebarClearAllBg, ImportSidebarClearAllPlugins);
            LayoutElement pluginBulkLeC = pluginBulkLe;
            innerPaneScaleActions.Add(s => {
                if (pluginBulkLeC != null) pluginBulkLeC.preferredHeight = ImportSidebarBaseRowHeight * 0.8f * s;
            });
            importSidebarPluginBulkRow.SetActive(false);

            // Scroll view sits below the "Pick plugins" toggle. Its height is reserved per the live entry count
            // (UpdatePluginChecklistHeight) since the option panel is ContentSizeFitter-driven; the row list
            // scrolls when it overflows ImportSidebarVisiblePluginRows.
            importSidebarPluginChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = importSidebarPluginChecklistRoot.AddComponent<LayoutElement>();
            scrollLe.flexibleWidth = 1f;
            importSidebarPluginChecklistLe = scrollLe;
            innerPaneScaleActions.Add(s => UpdatePluginChecklistHeight());

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
            bool show = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Plugins) && importSidebarPluginsMergeSingle;
            if (importSidebarPluginChecklistRoot != null) importSidebarPluginChecklistRoot.SetActive(show);
            if (importSidebarPluginBulkRow != null) importSidebarPluginBulkRow.SetActive(show);
            if (!show || importSidebarPluginRowPool.Count == 0) return;

            importSidebarPluginEntries = BuildSourcePluginEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarPluginSelectionSig)
            {
                importSidebarSelectedPluginKeys.Clear();
                foreach (ImportPluginEntry e in importSidebarPluginEntries) importSidebarSelectedPluginKeys.Add(e.Key);
                importSidebarPluginSelectionSig = sig;
            }

            UpdatePluginChecklistHeight();
            RenderPluginChecklistRows();
        }

        // Reserve a concrete height for the checklist scroll: the parent option panel is ContentSizeFitter-driven,
        // so a flexibleHeight-only scroll collapses to zero and hides the rows. Height = caption + up to
        // ImportSidebarVisiblePluginRows rows (longer lists scroll), matched to the current chrome scale.
        private void UpdatePluginChecklistHeight()
        {
            if (importSidebarPluginChecklistLe == null) return;
            int visibleRows = Mathf.Min(importSidebarPluginEntries.Count, ImportSidebarVisiblePluginRows);
            float s = ChromeScale;
            // +1 for the "Plugins in source" caption row; 2px inter-row spacing matches the scroll's VLG spacing.
            importSidebarPluginChecklistLe.preferredHeight = (visibleRows + 1) * (ImportSidebarBaseRowHeight + 2f) * s;
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
            if (bg != null) bg.color = selected ? ImportSidebarSelectAllBg : ColorInactiveRow;

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

        // Check every source plugin (import all). Operates on the enumerated entries so it covers rows beyond the
        // visible pool too. The selection sig is left intact so a later source-atom change still re-seeds normally.
        private void ImportSidebarSelectAllPlugins()
        {
            importSidebarSelectedPluginKeys.Clear();
            foreach (ImportPluginEntry e in importSidebarPluginEntries) importSidebarSelectedPluginKeys.Add(e.Key);
            RenderPluginChecklistRows();
            RefreshApplyButtonEnabled();
        }

        // Uncheck every source plugin (exclude all). With the gate on, an empty selection imports no plugins.
        private void ImportSidebarClearAllPlugins()
        {
            importSidebarSelectedPluginKeys.Clear();
            RenderPluginChecklistRows();
            RefreshApplyButtonEnabled();
        }

        // ---- CUA picker (mirrors the plugin picker; lists every CustomUnityAsset in the source scene) ----

        private void BuildImportSidebarCUAChecklist(Transform parent)
        {
            importSidebarCUABulkRow = new GameObject("CUABulkRow");
            importSidebarCUABulkRow.transform.SetParent(parent, false);
            LayoutElement cuaBulkLe = importSidebarCUABulkRow.AddComponent<LayoutElement>();
            cuaBulkLe.preferredHeight = ImportSidebarBaseRowHeight * 0.8f;
            cuaBulkLe.flexibleWidth = 1f;
            HorizontalLayoutGroup cuaBulkHlg = importSidebarCUABulkRow.AddComponent<HorizontalLayoutGroup>();
            cuaBulkHlg.childForceExpandWidth = true;
            cuaBulkHlg.childForceExpandHeight = true;
            cuaBulkHlg.childControlWidth = true;
            cuaBulkHlg.childControlHeight = true;
            cuaBulkHlg.spacing = 2f;
            BuildImportSidebarBulkButton(importSidebarCUABulkRow.transform,
                VPBTranslation.T("gallery.import.select_all_cuas", "Select All"),
                "gallery.import.select_all_cuas_tip", "Import every CUA in the source",
                ImportSidebarSelectAllBg, ImportSidebarSelectAllCUAs);
            BuildImportSidebarBulkButton(importSidebarCUABulkRow.transform,
                VPBTranslation.T("gallery.import.clear_all_cuas", "Clear All"),
                "gallery.import.clear_all_cuas_tip", "Exclude every CUA from import",
                ImportSidebarClearAllBg, ImportSidebarClearAllCUAs);
            LayoutElement cuaBulkLeC = cuaBulkLe;
            innerPaneScaleActions.Add(s => {
                if (cuaBulkLeC != null) cuaBulkLeC.preferredHeight = ImportSidebarBaseRowHeight * 0.8f * s;
            });
            importSidebarCUABulkRow.SetActive(false);

            importSidebarCUAChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = importSidebarCUAChecklistRoot.AddComponent<LayoutElement>();
            scrollLe.flexibleWidth = 1f;
            importSidebarCUAChecklistLe = scrollLe;
            innerPaneScaleActions.Add(s => UpdateCUAChecklistHeight());

            Transform content = importSidebarCUAChecklistRoot.GetComponent<ScrollRect>().content.transform;

            Text caption = AddSimpleLabelText(content,
                "CUAs in source (check to import)", ImportSidebarBaseFontSize, UI.PopupMutedText);
            LayoutElement capLe = caption.gameObject.AddComponent<LayoutElement>();
            capLe.preferredHeight = ImportSidebarBaseRowHeight;
            capLe.flexibleWidth = 1f;
            Text capCaptured = caption;
            LayoutElement capLeCaptured = capLe;
            innerPaneScaleActions.Add(s => {
                if (capLeCaptured != null) capLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(capCaptured, ImportSidebarBaseFontSize, s);
            });

            for (int i = 0; i < ImportSidebarMaxCUARows; i++)
            {
                GameObject row = CreateImportSidebarCUARow(content, i);
                importSidebarCUARowPool.Add(row);
                row.SetActive(false);
            }
            importSidebarCUAChecklistRoot.SetActive(false);
        }

        private GameObject CreateImportSidebarCUARow(Transform parent, int index)
        {
            GameObject row = new GameObject("CUARow_" + index);
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

        // Rebuilds the CUA checklist from the source scene. Visible only for Appearance + "Import atom CUAs" + pick gate.
        // Switching source atom (or scene) reseeds the checks to "all"; within the same source, checks are preserved.
        private void RefreshCUAChecklist()
        {
            bool show = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance)
                && importSidebarImportLinkedCUAs && importSidebarPickCUAs;
            if (importSidebarCUAChecklistRoot != null) importSidebarCUAChecklistRoot.SetActive(show);
            if (importSidebarCUABulkRow != null) importSidebarCUABulkRow.SetActive(show);
            if (!show || importSidebarCUARowPool.Count == 0) return;

            importSidebarCUAEntries = BuildSourceCUAEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarCUASelectionSig)
            {
                importSidebarSelectedCUAKeys.Clear();
                foreach (ImportCUAEntry e in importSidebarCUAEntries) importSidebarSelectedCUAKeys.Add(e.Id);
                importSidebarCUASelectionSig = sig;
            }

            UpdateCUAChecklistHeight();
            RenderCUAChecklistRows();
        }

        private void UpdateCUAChecklistHeight()
        {
            if (importSidebarCUAChecklistLe == null) return;
            int visibleRows = Mathf.Min(importSidebarCUAEntries.Count, ImportSidebarVisibleCUARows);
            float s = ChromeScale;
            importSidebarCUAChecklistLe.preferredHeight = (visibleRows + 1) * (ImportSidebarBaseRowHeight + 2f) * s;
        }

        private void RenderCUAChecklistRows()
        {
            for (int i = 0; i < importSidebarCUARowPool.Count; i++)
            {
                GameObject row = importSidebarCUARowPool[i];
                if (i < importSidebarCUAEntries.Count) { ConfigureCUARow(row, importSidebarCUAEntries[i]); row.SetActive(true); }
                else row.SetActive(false);
            }
        }

        private void ConfigureCUARow(GameObject row, ImportCUAEntry e)
        {
            bool selected = importSidebarSelectedCUAKeys.Contains(e.Id);
            string text = (selected ? "[x] " : "[ ] ") + e.Id;
            if (e.LinksToPerson) text += "  (on person)";

            Text t = row.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
            Image bg = row.GetComponent<Image>();
            if (bg != null) bg.color = selected ? ImportSidebarSelectAllBg : ColorInactiveRow;

            Button btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                string id = e.Id;
                btn.onClick.AddListener(() => ToggleCUASelected(id));
            }
        }

        private void ToggleCUASelected(string id)
        {
            if (importSidebarSelectedCUAKeys.Contains(id)) importSidebarSelectedCUAKeys.Remove(id);
            else importSidebarSelectedCUAKeys.Add(id);
            RenderCUAChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private void ImportSidebarSelectAllCUAs()
        {
            importSidebarSelectedCUAKeys.Clear();
            foreach (ImportCUAEntry e in importSidebarCUAEntries) importSidebarSelectedCUAKeys.Add(e.Id);
            RenderCUAChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private void ImportSidebarClearAllCUAs()
        {
            importSidebarSelectedCUAKeys.Clear();
            RenderCUAChecklistRows();
            RefreshApplyButtonEnabled();
        }

        // Enumerate every CustomUnityAsset in the source scene. Free-standing CUAs live OUTSIDE the person atom, so
        // this needs the whole scene JSON (unlike the plugin picker, which slices one person). On the cache-hit path
        // importSidebarLoadedSceneJSON is null, so parse the scene once here (the same accepted one-time freeze).
        private List<ImportCUAEntry> BuildSourceCUAEntries()
        {
            var result = new List<ImportCUAEntry>();
            JSONClass scene = EnsureLoadedSceneJSON();
            if (scene == null) return result;
            foreach (VPB.src.util.CUAAtomImporter.CuaEntry ce
                     in VPB.src.util.CUAAtomImporter.EnumerateSceneCUAs(scene, importSidebarSourceAtomId))
                result.Add(new ImportCUAEntry { Id = ce.Id, LinksToPerson = ce.LinksToPerson });
            return result;
        }

        // Returns the full source scene JSON, parsing + caching it once if only person-atom ids were cached.
        private JSONClass EnsureLoadedSceneJSON()
        {
            if (importSidebarLoadedSceneJSON != null) return importSidebarLoadedSceneJSON;
            if (importSidebarSourceScene == null) return null;
            try
            {
                using (FileEntryStreamReader r = importSidebarSourceScene.OpenStreamReader())
                {
                    JSONNode parsed = JSON.Parse(r.ReadToEnd());
                    importSidebarLoadedSceneJSON = parsed != null ? parsed.AsObject : null;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB import] EnsureLoadedSceneJSON failed for " + importSidebarSourceScene.Uid + ": " + ex.Message);
            }
            return importSidebarLoadedSceneJSON;
        }

        private List<ImportPluginEntry> BuildSourcePluginEntries()
        {
            var result = new List<ImportPluginEntry>();
            // Match BuildPresetJSONForCurrentSelection's data dependency: it can slice the atom straight from the
            // in-memory scene (cache-miss path) with no FileEntry. Guarding on importSidebarSourceScene alone made
            // the picker return empty (no rows) even while the "Plugins (N)" chip — which reads the same preset —
            // counted plugins. Bail only when there is genuinely no source to read.
            if (importSidebarSourceScene == null && importSidebarLoadedSceneJSON == null) return result;
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

        // Every plugin#N key in the source preset's PluginManager storable (used when "Pick plugins" is off).
        private static List<string> AllSourcePluginKeys(JSONClass preset)
        {
            var keys = new List<string>();
            JSONArray storables = (preset != null && preset["storables"] != null) ? preset["storables"].AsArray : null;
            if (storables == null) return keys;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                if (s["id"] == null || s["id"].Value != "PluginManager") continue;
                JSONClass plugins = s["plugins"] != null ? s["plugins"].AsObject : null;
                if (plugins != null)
                    foreach (string k in plugins.Keys) keys.Add(k);
                break;
            }
            return keys;
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

        // Maps each target plugin URL -> its existing plugin#N slot key, so a merging source plugin with the
        // same URL updates that live slot's settings instead of being appended as a duplicate. First slot wins
        // when the target has the same URL twice. Keyed case-insensitively to match GetTargetPluginUrls.
        private Dictionary<string, string> GetTargetPluginUrlToKey()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (importSidebarTargetAtom == null) return map;
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
                            if (string.IsNullOrEmpty(u)) continue;
                            u = u.Trim();
                            if (!map.ContainsKey(u)) map[u] = k;
                        }
                }
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB import] target plugin url->key scan failed: " + ex.Message); }
            return map;
        }

        // Count of plugin slots currently on the target's PluginManager. VaM's merge-load renumbers
        // ALL active plugins on the atom into a single contiguous 0..N-1 range (it does not preserve
        // gaps or the original slot numbers - e.g. an existing plugin#5 compacts down to plugin#1 if
        // only one other plugin precedes it), so the newly appended source plugins must start
        // numbering at this COUNT, not at (highest existing slot number + 1).
        private int GetTargetPluginCount()
        {
            if (importSidebarTargetAtom == null) return 0;
            try
            {
                JSONStorable pm = importSidebarTargetAtom.GetStorableByID("PluginManager");
                if (pm != null)
                {
                    JSONClass j = pm.GetJSON();
                    JSONClass plugins = (j != null && j["plugins"] != null) ? j["plugins"].AsObject : null;
                    if (plugins != null) return plugins.Count;
                }
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB import] target plugin count scan failed: " + ex.Message); }
            return 0;
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
            if (importSidebarCUARelativeRow != null)
                importSidebarCUARelativeRow.SetActive(importSidebarImportLinkedCUAs);
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
            importSidebarApplyButtonLabel = AddSimpleLabelText(btn.transform, "Apply", ImportSidebarBaseFontSize, UI.PopupText);
            importSidebarApplyButtonLabel.alignment = TextAnchor.MiddleCenter;

            Text labelCaptured = importSidebarApplyButtonLabel;
            innerPaneScaleActions.Add(s => ApplyScaledFont(labelCaptured, ImportSidebarBaseFontSize, s));
        }

        private void OnImportSidebarMultiSelectClicked()
        {
            importSidebarMultiSelectTypes = !importSidebarMultiSelectTypes;
            // Leaving multi-select with several types picked: collapse to the active (last-clicked) type.
            if (!importSidebarMultiSelectTypes && importSidebarMultiSelectedTypes.Count > 1)
            {
                importSidebarMultiSelectedTypes.Clear();
                if (IsImportTypeAvailable(importSidebarPresetType))
                    importSidebarMultiSelectedTypes.Add(importSidebarPresetType);
                ApplyImportSidebarTypeSelectionChange();
            }
            RefreshImportSidebarMultiToggleVisual();
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        private void RefreshImportSidebarMultiToggleVisual()
        {
            if (importSidebarMultiToggleLabel != null)
                importSidebarMultiToggleLabel.text = importSidebarMultiSelectTypes ? "Multi: On" : "Multi: Off";
            if (importSidebarMultiToggleBg != null)
                importSidebarMultiToggleBg.color = importSidebarMultiSelectTypes
                    ? ImportSidebarMultiToggleBg : ImportSidebarMultiToggleOffBg;
        }

        private void OnImportSidebarTypeChosen(VpbResourceType t, bool additive)
        {
            // Plain click selects only this type; Ctrl-click (additive) toggles it in/out for multi-select.
            if (additive)
            {
                if (importSidebarMultiSelectedTypes.Contains(t))
                    importSidebarMultiSelectedTypes.Remove(t);
                else
                    importSidebarMultiSelectedTypes.Add(t);
            }
            else
            {
                importSidebarMultiSelectedTypes.Clear();
                importSidebarMultiSelectedTypes.Add(t);
            }
            // Last-clicked type drives which option panel is highlighted/scrolled to.
            importSidebarPresetType = t;

            // Show an option panel for each selected type.
            foreach (var kv in importSidebarOptionPanels)
                kv.Value.SetActive(importSidebarMultiSelectedTypes.Contains(kv.Key));

            RefreshTypeRadioButtonColors();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshApplyButtonEnabled();
            try { RefreshImportSidebarWizardHeader(); } catch { }
            // Swapping the active panel changes the scroll content's total height; force the VLG/CSF to recompute.
            RebuildImportSidebarContent();
            // Guard: skip the build-time call (importSidebarBuilt set after BuildImportSidebar); persist user picks only.
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        private void RefreshTypeRadioButtonColors()
        {
            foreach (var kv in importSidebarTypeRadioButtons)
            {
                bool available = IsImportTypeAvailable(kv.Key);
                bool active = importSidebarMultiSelectedTypes.Contains(kv.Key);

                Image img = kv.Value.GetComponent<Image>();
                if (img != null)
                    img.color = !available ? ImportSidebarUnavailableRow
                              : (active ? ImportSidebarSelectedAccent : ColorInactiveRow);

                Button btn = kv.Value.GetComponent<Button>();
                if (btn != null) btn.interactable = available;

                Text lbl;
                if (importSidebarTypeRadioLabels.TryGetValue(kv.Key, out lbl) && lbl != null)
                {
                    lbl.text = TypeChipLabel(kv.Key);
                    lbl.color = available ? UI.TextPrimary : ImportSidebarUnavailableText;
                }
            }
        }

        // Chip caption with a count badge for introspectable types ("Plugins (2)"); bare name otherwise.
        private string TypeChipLabel(VpbResourceType t)
        {
            int c;
            if (importSidebarSourceTypeCounts.TryGetValue(t, out c))
                return ShortNameForType(t) + " (" + c + ")";
            return ShortNameForType(t);
        }

        // A type is selectable unless the source is known to carry zero of it.
        private bool IsImportTypeAvailable(VpbResourceType t)
        {
            int c;
            return !importSidebarSourceTypeCounts.TryGetValue(t, out c) || c > 0;
        }

        // Recompute per-type counts from the selected source atom and re-skin the chips. Empty types are
        // greyed/disabled and dropped from the current selection so Apply never runs a no-op type.
        private void RefreshSourceTypeAvailability()
        {
            importSidebarSourceTypeCounts.Clear();

            if (!string.IsNullOrEmpty(importSidebarSourceAtomId))
            {
                JSONClass preset = null;
                try { preset = BuildPresetJSONForCurrentSelection(); } catch { }
                JSONArray storables = (preset != null && preset["storables"] != null) ? preset["storables"].AsArray : null;
                if (storables != null)
                {
                    int clothing = 0, hair = 0, morphs = 0, plugins = 0;
                    foreach (JSONNode node in storables)
                    {
                        JSONClass s = node as JSONClass;
                        if (s == null || s["id"] == null) continue;
                        string id = s["id"].Value;
                        if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase))
                        {
                            clothing = CountEnabledArray(s["clothing"]);
                            hair = CountEnabledArray(s["hair"]);
                            morphs = (s["morphs"] != null && s["morphs"].AsArray != null) ? s["morphs"].AsArray.Count : 0;
                        }
                        else if (string.Equals(id, "PluginManager", StringComparison.Ordinal))
                        {
                            JSONClass p = s["plugins"] != null ? s["plugins"].AsObject : null;
                            plugins = p != null ? p.Count : 0;
                        }
                    }
                    importSidebarSourceTypeCounts[VpbResourceType.Clothing] = clothing;
                    importSidebarSourceTypeCounts[VpbResourceType.Hair] = hair;
                    importSidebarSourceTypeCounts[VpbResourceType.Morphs] = morphs;
                    importSidebarSourceTypeCounts[VpbResourceType.Plugins] = plugins;
                }
            }

            // Prune now-unavailable types from the selection.
            var stale = new List<VpbResourceType>();
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
                if (!IsImportTypeAvailable(t)) stale.Add(t);
            foreach (VpbResourceType t in stale) importSidebarMultiSelectedTypes.Remove(t);

            RefreshTypeRadioButtonColors();
            foreach (var kv in importSidebarOptionPanels)
                kv.Value.SetActive(importSidebarMultiSelectedTypes.Contains(kv.Key));
            RefreshApplyButtonEnabled();
        }

        private static int CountEnabledArray(JSONNode arrNode)
        {
            JSONArray arr = arrNode != null ? arrNode.AsArray : null;
            if (arr == null) return 0;
            int n = 0;
            foreach (JSONNode item in arr)
            {
                JSONClass o = item as JSONClass;
                // Items stored with enabled:"false" are inactive and would import nothing visible.
                if (o != null && o["enabled"] != null
                    && string.Equals(o["enabled"].Value, "false", StringComparison.OrdinalIgnoreCase))
                    continue;
                n++;
            }
            return n;
        }

        private void ImportSidebarSelectAllTypes()
        {
            foreach (VpbResourceType t in ImportSidebarTypeOrder)
                if (IsImportTypeAvailable(t)) importSidebarMultiSelectedTypes.Add(t);
            ApplyImportSidebarTypeSelectionChange();
        }

        private void ImportSidebarClearAllTypes()
        {
            importSidebarMultiSelectedTypes.Clear();
            ApplyImportSidebarTypeSelectionChange();
        }

        private void ApplyImportSidebarTypeSelectionChange()
        {
            RefreshTypeRadioButtonColors();
            foreach (var kv in importSidebarOptionPanels)
                kv.Value.SetActive(importSidebarMultiSelectedTypes.Contains(kv.Key));
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            try { RefreshImportSidebarWizardHeader(); } catch { }
            RebuildImportSidebarContent();
            RefreshApplyButtonEnabled();
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
            bool typeOk = importSidebarMultiSelectedTypes.Count > 0;
            bool multiBlock = ImportSidebarMultiSelectBlocked();

            if (importSidebarApplyButton != null)
                importSidebarApplyButton.interactable = !multiBlock && sourceOk && targetOk && sceneOk && typeOk;

            try { RefreshImportSidebarWizardHeader(); } catch { }
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

            // Snapshot the target atom BEFORE any changes so the user can undo the entire import.
            try
            {
                Action undoAction = CaptureAtomSnapshotAction(importSidebarTargetAtom);
                if (undoAction != null) PushUndo(undoAction);
            }
            catch { }

            string sourceHostUid = (importSidebarSourceScene is VarFileEntry sceneVar && sceneVar.Package != null)
                ? sceneVar.Package.Uid : null;

            // Apply each selected type in sequence, EXCEPT Pose. The selected-types set is unordered, so a pose
            // applied before Appearance (or before its scale/morphs settle) lands its control nodes on a
            // still-rescaling skeleton and drifts (feet/toes/hands off). Defer Pose until every other type has
            // applied and the body has settled.
            bool hasPose = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Pose);
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
            {
                if (t == VpbResourceType.Pose) continue;
                ApplyOneTypeImport(t, sourceHostUid);
            }

            // CUA import is Appearance-specific; run it when Appearance is among the selected types. It must run
            // AFTER the pose so it anchors to the final posed skeleton.
            bool importCUAs = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance)
                && importSidebarImportLinkedCUAs;

            if (hasPose)
                StartCoroutine(ApplyDeferredPoseThenCUAImport(sourceHostUid, importCUAs));
            else if (importCUAs)
                StartImportLinkedCUAs(importSidebarSourceScene, importSidebarSourceAtomId, importSidebarTargetAtom, sourceHostUid);
        }

        // Applies the pose once the target skeleton has settled from the appearance load (scale/morphs settle over
        // several frames), then runs the CUA import so it anchors to the final pose. Mirrors the settle wait the
        // CUA importer already uses for bone-based placement.
        private IEnumerator ApplyDeferredPoseThenCUAImport(string sourceHostUid, bool importCUAs)
        {
            yield return VPB.src.util.CUAAtomImporter.WaitForPersonSettled(importSidebarTargetAtom);
            ApplyOneTypeImport(VpbResourceType.Pose, sourceHostUid);
            yield return VPB.src.util.CUAAtomImporter.WaitForPersonSettled(importSidebarTargetAtom);
            if (importCUAs)
                StartImportLinkedCUAs(importSidebarSourceScene, importSidebarSourceAtomId, importSidebarTargetAtom, sourceHostUid);
        }

        private void ApplyOneTypeImport(VpbResourceType type, string sourceHostUid)
        {
            JSONClass presetJSON = BuildPresetJSONForCurrentSelection();
            if (presetJSON == null)
            {
                LogUtil.LogWarning("[VPB import] Could not build preset JSON for type " + type);
                return;
            }

            string storableOverride = ResolveStorableOverrideForType(type);

            // Appearance: suppress-clothing maps to Keep (locks ClothingPresets so source clothing is skipped),
            // else Replace. Clothing/Hair use the merge toggle. Other types ignore the mode.
            ClothingApplyMode mode;
            if (type == VpbResourceType.Appearance)
                mode = importSidebarSuppressClothingLoad ? ClothingApplyMode.Keep : ClothingApplyMode.Replace;
            else
                mode = importSidebarMergeClothingOrHair ? ClothingApplyMode.Merge : ClothingApplyMode.Replace;

            // LoadPreset has no subToggles param: prune opted-out sub-trees here, on the fresh deep copy
            // from BuildPresetJSONForCurrentSelection (never the cached scene JSON).
            presetJSON = VpbImportSubToggleFilter.FilterForType(presetJSON, type, importSidebarSubToggles);
            // A null here would make LoadPreset fall through to reading the whole scene file and applying every atom.
            if (presetJSON == null)
            {
                LogUtil.LogWarning("[VPB import] Sub-toggle filter returned null for type " + type + "; skipping.");
                return;
            }

            // Plugins: always hand the PluginPresets manager a plugins-ONLY slice (PluginManager dict + each
            // plugin's param storable) — the same shape VaM loads for a .vap plugin preset. Feeding the whole
            // person preset to the plugin preset manager imports nothing reliably. Pick-mode prunes to the
            // checked plugins; off imports every source plugin. Merge so they add without dropping target plugins.
            if (type == VpbResourceType.Plugins)
            {
                ICollection<string> keys;
                if (importSidebarPluginsMergeSingle)
                {
                    keys = importSidebarSelectedPluginKeys;
                }
                else
                {
                    keys = AllSourcePluginKeys(presetJSON);
                }
                JSONClass pluginSlice = VpbImport.BuildSelectedPluginsSlice(presetJSON, keys);
                if (pluginSlice == null)
                {
                    LogUtil.LogWarning("[VPB import] No plugins to import for type Plugins; skipping.");
                    return;
                }
                presetJSON = pluginSlice;

                // Default = MERGE: keep the target's existing plugins. A source plugin whose URL already
                // exists on the target UPDATES that live plugin's settings (its param storable is remapped
                // onto the existing slot, and it is NOT re-added) instead of creating a duplicate; only
                // genuinely new plugins are appended past the target's existing slots. VaM assigns appended
                // plugins the next slot numbers, so the slice's plugin#N keys + plugin#N_* param storables
                // must be remapped accordingly. "Clear existing plugins" = old wipe-and-replace.
                if (importSidebarClearExistingPlugins)
                {
                    mode = ClothingApplyMode.Replace;
                }
                else
                {
                    mode = ClothingApplyMode.Merge;
                    int startNumber = GetTargetPluginCount();
                    VpbImport.MergePluginSliceKeys(presetJSON, startNumber, GetTargetPluginUrlToKey());
                }

                // Issue #66: rewrite plugin self-references (e.g. trigger receiverAtom) from the source
                // atom uid to the target atom uid so they don't break when the names differ. Opt-in.
                if (importSidebarMigratePluginUIDs
                    && importSidebarTargetAtom != null
                    && !string.IsNullOrEmpty(importSidebarSourceAtomId))
                {
                    VPB.src.util.JSONExtensions.ReplaceAtomUidReferencesMutable(
                        presetJSON, importSidebarSourceAtomId, importSidebarTargetAtom.uid);
                }
            }

            // BreastPhysics / Glute / Plugins / Skin lack a dedicated dispatch case: route them through General
            // by their storable name (General aborts loud if the storable is missing).
            VpbResourceType dispatchType = type;
            if (storableOverride != null
                && (dispatchType == VpbResourceType.BreastPhysics
                    || dispatchType == VpbResourceType.Glute
                    || dispatchType == VpbResourceType.Plugins
                    || dispatchType == VpbResourceType.Skin
                    || dispatchType == VpbResourceType.Morphs))
            {
                dispatchType = VpbResourceType.General;
            }

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
            if (type == VpbResourceType.Appearance && importSidebarDeleteTargetCUAs)
                DeleteTargetLinkedCUAs(importSidebarTargetAtom);
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
            // Picker on -> import exactly the checked CUAs; off -> all person-linked CUAs (legacy behavior).
            HashSet<string> selectedIds = importSidebarPickCUAs
                ? new HashSet<string>(importSidebarSelectedCUAKeys, StringComparer.Ordinal) : null;
            StartCoroutine(VPB.src.util.CUAAtomImporter.ImportSelectedCUAsAsAtoms(
                scene, sourceAtomId, target, sourceHostUid, selectedIds,
                importSidebarCUARelativeToPerson));
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
            return ResolveStorableOverrideForType(importSidebarPresetType);
        }

        private static string ResolveStorableOverrideForType(VpbResourceType type)
        {
            switch (type)
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
