using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;
using VPB.src.util;

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
        private RectTransform importSidebarApplyReasonRT;
        private Text importSidebarApplyReasonLabel;
        private Image importSidebarApplyReasonBg;
        // Warm-path scratch — reuse across source-availability refreshes (no per-refresh List alloc).
        private readonly List<VpbResourceType> _importSidebarPausedScratch = new List<VpbResourceType>(8);
        private readonly System.Text.StringBuilder _importSidebarStatusSb = new System.Text.StringBuilder(96);

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
        // Two instances — Appearance panel (optional add-on) and standalone CUA type panel — share selection state.
        private sealed class ImportCUAChecklistHandles
        {
            public GameObject ChecklistRoot;
            public LayoutElement ChecklistLe;
            public GameObject BulkRow;
            public readonly List<GameObject> RowPool = new List<GameObject>(ImportSidebarMaxCUARows);
        }

        private const int ImportSidebarVisibleCUARows = 8;
        private const int ImportSidebarMaxCUARows = 24;
        private readonly ImportCUAChecklistHandles importSidebarAppearanceCUAUi = new ImportCUAChecklistHandles();
        private readonly ImportCUAChecklistHandles importSidebarCuaOnlyCUAUi = new ImportCUAChecklistHandles();
        private List<ImportCUAEntry> importSidebarCUAEntries = new List<ImportCUAEntry>();
        // Checked CUA atom ids to import; per source-atom (sig tracks scene+atom so switching source reseeds to "all").
        private readonly HashSet<string> importSidebarSelectedCUAKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarCUASelectionSig;

        private sealed class ImportCUAEntry
        {
            public string Id;            // CUA atom id
            public bool LinksToPerson;   // reaches the selected source person (tagged "on person")
        }

        // Scene atom picker: every non-Person atom in the source scene (Lights, Empty, CUA, SubScene, …).
        private const int ImportSidebarVisibleSceneAtomRows = 8;
        private const int ImportSidebarMaxSceneAtomRows = 256;
        private const int ImportSidebarSceneAtomRowPoolInitial = 32;
        private readonly ImportCUAChecklistHandles importSidebarSceneAtomUi = new ImportCUAChecklistHandles();
        private List<ImportSceneAtomEntry> importSidebarSceneAtomEntries = new List<ImportSceneAtomEntry>();
        private List<ImportSceneAtomEntry> importSidebarSceneAtomFilteredEntries = new List<ImportSceneAtomEntry>();
        private readonly HashSet<string> importSidebarSelectedSceneAtomKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarSceneAtomSelectionSig;
        private GameObject importSidebarSceneAtomSearchRow;
        private InputField importSidebarSceneAtomSearchInput;
        private GameObject importSidebarSceneAtomEmptyHintRow;
        private Text importSidebarSceneAtomEmptyHint;

        private sealed class ImportSceneAtomEntry
        {
            public string Id;
            public string Type;
            public bool LinksToPerson;
            public bool UidCollision;
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
            VpbResourceType.CUA,
            VpbResourceType.Atoms,
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
            LayoutElement bulkLe = UI.AddLE(bulkRow, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            HorizontalLayoutGroup bulkHlg = UI.AddHLG(bulkRow, spacing: ImportSidebarBaseRowSpacing, childForceExpandHeight: true);

            BuildImportSidebarBulkButton(bulkRow.transform,
                VPBTranslation.T("gallery.import.select_all_types", "Select All"),
                "gallery.import.select_all_tip", "Select every resource type",
                ImportSidebarSelectAllBg, ImportSidebarSelectAllTypes);
            BuildImportSidebarBulkButton(bulkRow.transform,
                VPBTranslation.T("gallery.import.clear_all_types", "Clear All"),
                "gallery.import.clear_all_tip", "Deselect every resource type",
                ImportSidebarClearAllBg, ImportSidebarClearAllTypes);
            GameObject multiBtn = BuildImportSidebarBulkButton(bulkRow.transform,
                "Accumulate",
                "gallery.import.multi_select_tip",
                "When on, type chips accumulate; when off, each click picks one type",
                ImportSidebarMultiToggleBg, OnImportSidebarMultiSelectClicked);
            importSidebarMultiToggleBg = multiBtn.GetComponent<Image>();
            importSidebarMultiToggleLabel = multiBtn.GetComponentInChildren<Text>();
            RefreshImportSidebarMultiToggleVisual();

            LayoutElement bulkLeC = bulkLe;
            HorizontalLayoutGroup bulkHlgCaptured = bulkHlg;
            innerPaneScaleActions.Add(s => {
                if (bulkLeC != null) bulkLeC.preferredHeight = ImportSidebarBaseRowHeight * s;
                if (bulkHlgCaptured != null) bulkHlgCaptured.spacing = ImportSidebarBaseRowSpacing * s;
            });

            GameObject grid = new GameObject("TypeRadio");
            grid.transform.SetParent(parent, false);

            RectTransform rt = grid.AddComponent<RectTransform>();
            importSidebarTypeRadioContainer = rt;
            const int typeRadioRows = 6;
            float rowH = ImportSidebarBaseRowHeight;
            float gap = ImportSidebarBaseRowSpacing;
            float contentW = ImportSidebarBaseWidth - ImportSidebarScrollBarWidthRef - ImportSidebarInnerPadHRef;
            float cellW = Mathf.Floor((contentW - gap) * 0.5f);
            LayoutElement le = UI.AddLE(grid, preferredHeight: typeRadioRows * rowH + (typeRadioRows - 1) * gap, flexibleWidth: 1f);

            GridLayoutGroup g = grid.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(cellW, rowH);
            g.spacing = new Vector2(gap, gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;

            LayoutElement leCaptured = le;
            innerPaneScaleActions.Add(s => SyncImportSidebarTypeRadioGridWidth(s));

            for (int i = 0; i < ImportSidebarTypeOrder.Length; i++)
            {
                VpbResourceType t = ImportSidebarTypeOrder[i];
                GameObject row = new GameObject("Type_" + t);
                row.transform.SetParent(grid.transform, false);
                Image rb = AddImportSidebarRoundedBg(row, ColorInactiveRow);
                Button b = row.AddComponent<Button>();
                b.targetGraphic = rb;
                UI.NeutralizeSelectableColorTint(b);
                UI.EnsureFloatChromeHoverBorder(row, inward: true);

                Text typeLabel = CreateImportSidebarLabel(row.transform, ShortNameForType(t), ImportSidebarBaseFontSize);
                typeLabel.alignment = TextAnchor.MiddleLeft;

                Text typeLabelCaptured = typeLabel;
                innerPaneScaleActions.Add(s => ApplyScaledFont(typeLabelCaptured, ImportSidebarBaseFontSize, s));

                VpbResourceType captured = t;
                b.onClick.AddListener(() => OnImportSidebarTypeChosen(captured, importSidebarMultiSelectTypes));
                AddDynamicTooltip(row, () => BuildImportTypeTooltip(captured));

                importSidebarTypeRadioButtons[t] = row;
                importSidebarTypeRadioLabels[t] = typeLabel;
            }
        }

        private GameObject BuildImportSidebarSelectClearBulkRow(
            Transform parent,
            string rowName,
            string selectLabel, string selectTipKey, string selectTipDefault, UnityEngine.Events.UnityAction onSelect,
            string clearLabel, string clearTipKey, string clearTipDefault, UnityEngine.Events.UnityAction onClear)
        {
            GameObject bulk = new GameObject(rowName);
            bulk.transform.SetParent(parent, false);
            LayoutElement bulkLe = UI.AddLE(bulk, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            UI.AddHLG(bulk, spacing: ImportSidebarBaseRowSpacing, childForceExpandHeight: true);
            BuildImportSidebarBulkButton(bulk.transform, selectLabel, selectTipKey, selectTipDefault, ImportSidebarSelectAllBg, onSelect);
            BuildImportSidebarBulkButton(bulk.transform, clearLabel, clearTipKey, clearTipDefault, ImportSidebarClearAllBg, onClear);
            LayoutElement bulkLeC = bulkLe;
            innerPaneScaleActions.Add(s => {
                if (bulkLeC != null) bulkLeC.preferredHeight = ImportSidebarBaseRowHeight * s;
            });
            bulk.SetActive(false);
            return bulk;
        }

        private void AddImportSidebarChecklistCaption(Transform content, string captionText)
        {
            Text caption = AddSimpleLabelText(content, captionText, ImportSidebarBaseFontSize, UI.PopupMutedText);
            LayoutElement capLe = UI.AddLE(caption.gameObject, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            Text capCaptured = caption;
            LayoutElement capLeCaptured = capLe;
            innerPaneScaleActions.Add(s => {
                if (capLeCaptured != null) capLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(capCaptured, ImportSidebarBaseFontSize, s);
            });
        }

        private GameObject BuildImportSidebarBulkButton(Transform parent, string label,
            string tipKey, string tipDefault, Color bgColor, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnGO = new GameObject("BulkBtn_" + label);
            btnGO.transform.SetParent(parent, false);
            LayoutElement btnLe = UI.AddLE(btnGO, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            Image bg = AddImportSidebarRoundedBg(btnGO, bgColor);
            Button btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);
            UI.EnsureFloatChromeHoverBorder(btnGO, inward: true);
            Text txt = CreateImportSidebarLabel(btnGO.transform, label, ImportSidebarBaseFontSize);
            txt.alignment = TextAnchor.MiddleCenter;
            btn.onClick.AddListener(onClick);
            AddTooltip(btnGO, tipKey, tipDefault);
            Text txtCaptured = txt;
            LayoutElement btnLeCaptured = btnLe;
            innerPaneScaleActions.Add(s => {
                ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s);
                if (btnLeCaptured != null) btnLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
            });
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
                case VpbResourceType.Appearance:    return "Appear";
                case VpbResourceType.Clothing:      return "Clothing";
                case VpbResourceType.Hair:          return "Hair";
                case VpbResourceType.Pose:          return "Pose";
                case VpbResourceType.Skin:          return "Skin";
                case VpbResourceType.Morphs:        return "Morphs";
                case VpbResourceType.BreastPhysics: return "Breast";
                case VpbResourceType.Glute:         return "Glute";
                case VpbResourceType.Plugins:       return "Plugins";
                case VpbResourceType.CUA:           return "CUA";
                case VpbResourceType.Atoms:         return "Atoms";
                case VpbResourceType.General:       return "General";
                default: return t.ToString();
            }
        }

        // True when the selected type is present on the current source (or count unknown).
        private bool IsImportTypeAvailable(VpbResourceType t)
        {
            int c;
            return !importSidebarSourceTypeCounts.TryGetValue(t, out c) || c > 0;
        }

        private bool IsImportTypePaused(VpbResourceType t)
        {
            return importSidebarMultiSelectedTypes.Contains(t) && !IsImportTypeAvailable(t);
        }

        private int CountAvailableSelectedImportTypes()
        {
            int n = 0;
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
            {
                if (IsImportTypeAvailable(t)) n++;
            }
            return n;
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
            LayoutElement le = UI.AddLE(host, flexibleWidth: 1f);
            // Stack active panels vertically; ContentSizeFitter drives the host height to match content.
            VerticalLayoutGroup hostVlg = UI.AddVLG(host, spacing: 0f);
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

            VerticalLayoutGroup vlg = UI.AddVLG(panel, spacing: ImportSidebarBaseRowSpacing);

            VerticalLayoutGroup vlgCaptured = vlg;
            innerPaneScaleActions.Add(s => { if (vlgCaptured != null) vlgCaptured.spacing = ImportSidebarBaseRowSpacing * s; });

            // Caption so stacked panels read as distinct groups (which options belong to which type).
            AddOptionGroupHeader(panel.transform, DisplayNameForType(t));

            switch (t)
            {
                case VpbResourceType.Appearance:
                    AddOptionToggle(panel.transform, "Disable scale change",
                        () => importSidebarSuppressScale, v => importSidebarSuppressScale = v, null,
                        VPBTranslation.T("gallery.import.opt.appr_scale", "Keep the target person's current height/scale instead of applying the source's scale."));
                    AddOptionToggle(panel.transform, "Disable clothing load",
                        () => importSidebarSuppressClothingLoad,
                        v => { importSidebarSuppressClothingLoad = v; RefreshAppearanceConditionalRows(); }, null,
                        VPBTranslation.T("gallery.import.opt.appr_noclothing", "Import the look but leave the target's current clothing untouched."));
                    importSidebarOnlySuppressRealRow = AddOptionToggle(panel.transform, "  Only disable real clothing",
                        () => importSidebarOnlySuppressRealClothing, v => importSidebarOnlySuppressRealClothing = v, null,
                        VPBTranslation.T("gallery.import.opt.appr_realclothing", "When skipping clothing, still import accessories/hair-like items \u2014 only real garments are skipped."));
                    importSidebarImportCUARow = AddOptionToggle(panel.transform, "Import atom CUAs",
                        () => importSidebarImportLinkedCUAs,
                        v => { importSidebarImportLinkedCUAs = v; RefreshCUAChecklist(); RefreshAppearanceConditionalRows(); }, null,
                        VPBTranslation.T("gallery.import.opt.appr_cua", "Also import Custom Unity Assets (props: horns, tails, etc.) attached to the source person."));
                    AddOptionToggle(panel.transform, "Pick CUAs to import",
                        () => importSidebarPickCUAs,
                        v => { importSidebarPickCUAs = v; RefreshCUAChecklist(); }, null,
                        VPBTranslation.T("gallery.import.opt.pick_cua", "Show a checklist to choose which CUAs to import instead of importing all of them."));
                    importSidebarCUARelativeRow = AddOptionToggle(panel.transform, "Place off-person relative to person",
                        () => importSidebarCUARelativeToPerson,
                        v => importSidebarCUARelativeToPerson = v, null,
                        VPBTranslation.T("gallery.import.opt.cua_relative", "Position CUAs that aren't parented to a bone relative to the target person rather than at their absolute scene coordinates."));
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarCuaMergeLoad, v => importSidebarCuaMergeLoad = v, null,
                        VPBTranslation.T("gallery.import.opt.cua_merge", "Add the imported CUAs alongside the target's existing ones instead of replacing them."));
                    BuildImportSidebarCUAChecklist(panel.transform, importSidebarAppearanceCUAUi);
                    AddOptionToggle(panel.transform, "Delete current atom CUAs",
                        () => importSidebarDeleteTargetCUAs, v => importSidebarDeleteTargetCUAs = v,
                        GalleryUiColorTokens.AccentDangerStrong,
                        VPBTranslation.T("gallery.import.opt.cua_delete", "Destructive: remove all CUAs currently on the target person before importing."));
                    RefreshAppearanceConditionalRows();
                    break;

                case VpbResourceType.Clothing:
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarMergeClothingOrHair, v => importSidebarMergeClothingOrHair = v, null,
                        VPBTranslation.T("gallery.import.opt.cloth_merge", "Add the source clothing on top of what the target already wears instead of replacing the whole outfit."));
                    AddOptionToggle(panel.transform, "Only replace \"real\" clothing",
                        () => importSidebarOnlyReplaceRealClothing, v => importSidebarOnlyReplaceRealClothing = v, null,
                        VPBTranslation.T("gallery.import.opt.cloth_real", "Keep accessory/non-garment items on the target; only real garments get replaced."));
                    break;

                case VpbResourceType.Hair:
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarMergeClothingOrHair, v => importSidebarMergeClothingOrHair = v, null,
                        VPBTranslation.T("gallery.import.opt.hair_merge", "Add the source hair alongside the target's existing hair instead of replacing it."));
                    break;

                case VpbResourceType.Pose:
                    AddOptionToggle(panel.transform, "Disable morph load",
                        () => importSidebarSubToggles.SuppressMorphLoad,
                        v => importSidebarSubToggles.SuppressMorphLoad = v, null,
                        VPBTranslation.T("gallery.import.opt.pose_nomorph", "Apply the pose only; don't touch the target's morphs (body/face shape)."));
                    AddOptionToggle(panel.transform, "Disable root-node load",
                        () => importSidebarSubToggles.SuppressRootNodeLoad,
                        v => importSidebarSubToggles.SuppressRootNodeLoad = v, null,
                        VPBTranslation.T("gallery.import.opt.pose_noroot", "Apply the pose in place without moving the person's root position/rotation in the scene."));
                    break;

                case VpbResourceType.Morphs:
                    AddOptionToggle(panel.transform, "Include Appearance morphs",
                        () => importSidebarSubToggles.IncludeAppearanceMorphs,
                        v => importSidebarSubToggles.IncludeAppearanceMorphs = v, null,
                        VPBTranslation.T("gallery.import.opt.morph_appr", "Import morphs that shape the look (face/body appearance)."));
                    AddOptionToggle(panel.transform, "Include Physical/Pose morphs",
                        () => importSidebarSubToggles.IncludePhysicalPoseMorphs,
                        v => importSidebarSubToggles.IncludePhysicalPoseMorphs = v, null,
                        VPBTranslation.T("gallery.import.opt.morph_phys", "Import physical/pose-driven morphs (e.g. joint corrective and physics morphs)."));
                    break;

                case VpbResourceType.Plugins:
                    AddOptionToggle(panel.transform, "Pick plugins to import",
                        () => importSidebarPickPlugins,
                        v => { importSidebarPickPlugins = v; RefreshPluginChecklist(); },
                        null,
                        VPBTranslation.T("gallery.import.opt.pick_plugins", "Show a checklist to choose which plugins to import instead of importing all of them."));
                    AddOptionToggle(panel.transform, "Migrate self-UID references",
                        () => importSidebarMigratePluginUIDs,
                        v => importSidebarMigratePluginUIDs = v,
                        null,
                        VPBTranslation.T("gallery.import.opt.plugin_uid", "Rewrite plugin settings that point at the source atom's name so they point at the target atom (fixes self-references after import)."));
                    AddOptionToggle(panel.transform, "Clear existing plugins",
                        () => importSidebarClearExistingPlugins,
                        v => importSidebarClearExistingPlugins = v,
                        null,
                        VPBTranslation.T("gallery.import.opt.plugin_clear", "Remove the target's current plugins first. Off = merge (existing plugins are kept and duplicates updated in place)."));
                    BuildImportSidebarPluginChecklist(panel.transform);
                    break;

                case VpbResourceType.CUA:
                    AddOptionToggle(panel.transform, "Merge load",
                        () => importSidebarCuaMergeLoad, v => importSidebarCuaMergeLoad = v, null,
                        VPBTranslation.T("gallery.import.opt.cua_merge", "Add the imported CUAs alongside the target's existing ones instead of replacing them."));
                    AddOptionToggle(panel.transform, "Pick CUAs to import",
                        () => importSidebarPickCUAs,
                        v => { importSidebarPickCUAs = v; RefreshCUAChecklist(); }, null,
                        VPBTranslation.T("gallery.import.opt.pick_cua", "Show a checklist to choose which CUAs to import instead of importing all of them."));
                    AddOptionToggle(panel.transform, "Place off-person relative to person",
                        () => importSidebarCUARelativeToPerson,
                        v => importSidebarCUARelativeToPerson = v, null,
                        VPBTranslation.T("gallery.import.opt.cua_relative", "Position CUAs that aren't parented to a bone relative to the target person rather than at their absolute scene coordinates."));
                    BuildImportSidebarCUAChecklist(panel.transform, importSidebarCuaOnlyCUAUi);
                    AddOptionToggle(panel.transform, "Delete current atom CUAs",
                        () => importSidebarDeleteTargetCUAs, v => importSidebarDeleteTargetCUAs = v,
                        GalleryUiColorTokens.AccentDangerStrong,
                        VPBTranslation.T("gallery.import.opt.cua_delete", "Destructive: remove all CUAs currently on the target person before importing."));
                    break;

                case VpbResourceType.Atoms:
                    AddOptionToggle(panel.transform, "Skip existing atoms",
                        () => importSidebarSceneAtomSkipDuplicates,
                        v => importSidebarSceneAtomSkipDuplicates = v, null,
                        VPBTranslation.T("gallery.import.opt.atom_skip", "Don't import a source atom if an atom with the same name already exists in the current scene (avoids duplicates)."));
                    AddOptionToggle(panel.transform, "Place relative to target person",
                        () => importSidebarSceneAtomRelativeToPerson,
                        v => importSidebarSceneAtomRelativeToPerson = v, null,
                        VPBTranslation.T("gallery.import.opt.atom_relative", "Offset imported atoms relative to the target person instead of dropping them at their absolute source coordinates."));
                    AddOptionToggle(panel.transform, "Pick atoms to import",
                        () => importSidebarPickSceneAtoms,
                        v => { importSidebarPickSceneAtoms = v; RefreshSceneAtomChecklist(); RebuildImportSidebarContent(); },
                        null,
                        VPBTranslation.T("gallery.import.opt.pick_atoms", "Show a searchable checklist to choose which scene atoms to import instead of importing all of them."));
                    AddOptionToggle(panel.transform, "Always show remap prompt",
                        () => importSidebarAlwaysShowRemapPrompt,
                        v => importSidebarAlwaysShowRemapPrompt = v, null,
                        VPBTranslation.T(
                            "gallery.import.opt.always_show_remap",
                            "Open Remap Atom UIDs on every scene atom import. Off (default) = prompt only when a referenced atom is missing live, is ambiguous, or has a plugin slot that cannot be matched."));
                    BuildImportSidebarSceneAtomSearchRow(panel.transform);
                    BuildImportSidebarSceneAtomChecklist(panel.transform);
                    break;

                case VpbResourceType.General:
                    AddOptionToggle(panel.transform, "Include Physical",
                        () => importSidebarSubToggles.IncludePhysical,
                        v => importSidebarSubToggles.IncludePhysical = v, null,
                        VPBTranslation.T("gallery.import.opt.gen_physical", "Import physical settings (physics, collision, joint parameters) from the source."));
                    AddOptionToggle(panel.transform, "Include Pose",
                        () => importSidebarSubToggles.IncludePose,
                        v => importSidebarSubToggles.IncludePose = v, null,
                        VPBTranslation.T("gallery.import.opt.gen_pose", "Import the body pose from the source."));
                    AddOptionToggle(panel.transform, "Include Appearance",
                        () => importSidebarSubToggles.IncludeAppearance,
                        v => importSidebarSubToggles.IncludeAppearance = v, null,
                        VPBTranslation.T("gallery.import.opt.gen_appr", "Import appearance (morphs, skin, hair, clothing) from the source."));
                    GameObject mocapRow = AddOptionToggle(panel.transform, "Include Mocap",
                        () => importSidebarSubToggles.IncludeMocap,
                        v => importSidebarSubToggles.IncludeMocap = v, null,
                        VPBTranslation.T("gallery.import.opt.gen_mocap", "Motion-capture animation data (not available for this import type)."));
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
            LayoutElement le = UI.AddLE(row, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            Image bg = AddImportSidebarRoundedBg(row, ImportSidebarGroupHeaderBg, raycastTarget: false);
            Text t = CreateImportSidebarLabel(row.transform, label, ImportSidebarBaseFontSize);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = GalleryUiColorTokens.TextPrimary;
            LayoutElement leC = le;
            Text tC = t;
            innerPaneScaleActions.Add(s => {
                if (leC != null) leC.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(tC, ImportSidebarBaseFontSize, s);
            });
        }

        private void AddOptionGroupNote(Transform parent, string note)
        {
            GameObject row = new GameObject("GroupNote");
            row.transform.SetParent(parent, false);
            LayoutElement le = UI.AddLE(row, preferredHeight: ImportSidebarBaseRowHeight * 0.85f, flexibleWidth: 1f);
            Text t = AddSimpleLabelText(row.transform, note, ImportSidebarBaseFontSize, GalleryUiColorTokens.TextDim);
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
                case VpbResourceType.CUA:           return "Custom Unity Assets";
                case VpbResourceType.Atoms:         return "Scene Atoms";
                case VpbResourceType.General:       return "General";
                default: return t.ToString();
            }
        }

        // One-line summary of what importing a given resource type does, shown in the status bar on hover.
        private static string DescribeImportType(VpbResourceType t)
        {
            switch (t)
            {
                case VpbResourceType.Appearance:    return VPBTranslation.T("gallery.import.type_desc.appearance", "full look \u2014 morphs, skin, hair and clothing from the source person");
                case VpbResourceType.Clothing:      return VPBTranslation.T("gallery.import.type_desc.clothing", "only the clothing items worn by the source person");
                case VpbResourceType.Hair:          return VPBTranslation.T("gallery.import.type_desc.hair", "only the hair items and styling from the source person");
                case VpbResourceType.Pose:          return VPBTranslation.T("gallery.import.type_desc.pose", "only the body pose \u2014 bone rotations and root position");
                case VpbResourceType.Skin:          return VPBTranslation.T("gallery.import.type_desc.skin", "only the skin textures and materials from the source");
                case VpbResourceType.Morphs:        return VPBTranslation.T("gallery.import.type_desc.morphs", "only the morph values \u2014 body and face shape");
                case VpbResourceType.BreastPhysics: return VPBTranslation.T("gallery.import.type_desc.breast", "only the breast physics settings");
                case VpbResourceType.Glute:         return VPBTranslation.T("gallery.import.type_desc.glute", "only the glute physics settings");
                case VpbResourceType.Plugins:       return VPBTranslation.T("gallery.import.type_desc.plugins", "the person plugins loaded in the source scene");
                case VpbResourceType.CUA:           return VPBTranslation.T("gallery.import.type_desc.cua", "Custom Unity Assets \u2014 props attached in the source scene");
                case VpbResourceType.Atoms:         return VPBTranslation.T("gallery.import.type_desc.atoms", "whole atoms copied from the source scene into the current scene");
                case VpbResourceType.General:       return VPBTranslation.T("gallery.import.type_desc.general", "general person settings from the source");
                default: return "";
            }
        }

        // Status-bar tooltip for a type radio cell: name + what it imports + whether clicking adds or removes it.
        private string BuildImportTypeTooltip(VpbResourceType t)
        {
            bool available = IsImportTypeAvailable(t);
            bool selected = importSidebarMultiSelectedTypes.Contains(t);
            if (!available)
            {
                if (selected)
                {
                    return DisplayNameForType(t) + ": " + DescribeImportType(t) + "  \u2014  "
                        + VPBTranslation.T("gallery.import.type_tip.paused",
                            "0 on this source — kept selected, skipped on Apply (click to clear)");
                }
                return DisplayNameForType(t) + ": " + DescribeImportType(t) + "  \u2014  "
                    + VPBTranslation.T("gallery.import.type_tip.unavailable",
                        "0 on this source — nothing to import");
            }
            string action = selected
                ? VPBTranslation.T("gallery.import.type_tip.on", "selected \u2014 click to remove from import")
                : VPBTranslation.T("gallery.import.type_tip.off", "click to add to import");
            return DisplayNameForType(t) + ": " + DescribeImportType(t) + "  \u2014  " + action;
        }

        // Pooled checklist rows are re-skinned per entry and their labels are visually truncated, so the tooltip
        // reads the row's live label text (full, untruncated) at hover time and drops the "[x]/[ ]" checkbox glyph.
        private static string ImportChecklistRowTooltip(Text label)
        {
            if (label == null) return null;
            string s = label.text;
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length >= 4 && (s.StartsWith("[x] ", StringComparison.Ordinal) || s.StartsWith("[ ] ", StringComparison.Ordinal)))
                s = s.Substring(4);
            return s + "  \u2014  " + VPBTranslation.T("gallery.import.pick_row_tip", "click to include/exclude from import");
        }

        // Status-bar tooltip for a source/target atom row: full (untruncated) uid + the role clicking it assigns.
        private string ImportAtomRowTooltip(Text label, bool isSource)
        {
            if (label == null) return null;
            string s = label.text;
            if (string.IsNullOrEmpty(s)) return null;
            if (isSource)
                return VPBTranslation.T("gallery.import.source_row_tip", "Source") + ": " + s
                    + "  \u2014  " + VPBTranslation.T("gallery.import.source_row_tip2", "click to import from this atom");
            if (s == "<New Person Atom>")
                return VPBTranslation.T("gallery.import.new_person_tip", "create a new Person atom and import onto it");
            return VPBTranslation.T("gallery.import.target_row_tip", "Target") + ": " + s
                + "  \u2014  " + VPBTranslation.T("gallery.import.target_row_tip2", "click to import onto this atom");
        }

        private GameObject AddOptionToggle(Transform parent, string label, System.Func<bool> get, System.Action<bool> set, Color? labelColor, string tooltip = null)
        {
            GameObject row = new GameObject("Toggle_" + label);
            row.transform.SetParent(parent, false);

            // Lock min+preferred so [ ]/[x] text swap cannot change CSF/VLG row height.
            LayoutElement le = UI.AddLE(row, minHeight: ImportSidebarBaseRowHeight, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);

            Image bg = AddImportSidebarRoundedBg(row, get() ? ImportSidebarSelectedAccent : ColorInactiveRow);

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);
            UI.EnsureFloatChromeHoverBorder(row, inward: true);

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
                if (bg != null) bg.color = nv ? ImportSidebarSelectedAccent : ColorInactiveRow;
                SaveImportSidebarPrefs();
            });

            importSidebarOptionToggleRefreshers.Add(() =>
            {
                if (t != null) t.text = (get() ? "[x] " : "[ ] ") + label;
                if (bg != null) bg.color = get() ? ImportSidebarSelectedAccent : ColorInactiveRow;
            });

            if (!string.IsNullOrEmpty(tooltip)) AddTooltipPlain(row, tooltip);

            LayoutElement leCaptured = le;
            Text tCaptured = t;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null)
                {
                    float h = ImportSidebarBaseRowHeight * s;
                    leCaptured.minHeight = h;
                    leCaptured.preferredHeight = h;
                }
                ApplyScaledFont(tCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        private Text AddSimpleLabelText(Transform parent, string label, int size, Color color)
        {
            Text t = UI.CreateLabel(parent.gameObject, label, size, color, TextAnchor.MiddleLeft, raycastTarget: false, name: "Label");
            RectTransform rt = t.GetComponent<RectTransform>();

            RectTransform rtCaptured = rt;
            Text tCaptured = t;
            int sizeCaptured = size;
            innerPaneScaleActions.Add(s =>
            {
                ApplyImportSidebarLabelInsets(rtCaptured, s);
                ApplyScaledFont(tCaptured, sizeCaptured, s);
            });
            ApplyImportSidebarLabelInsets(rt, ChromeScale);
            ApplyScaledFont(t, sizeCaptured, ChromeScale);
            return t;
        }

        private void BuildImportSidebarPluginChecklist(Transform parent)
        {
            // Select All / Clear All bulk row: a fixed peer above the scroll so the user can include/exclude every
            // source plugin in one click, then refine per-row. Its visibility tracks the checklist (gate ON).
            importSidebarPluginBulkRow = BuildImportSidebarSelectClearBulkRow(
                parent, "PluginBulkRow",
                VPBTranslation.T("gallery.import.select_all_plugins", "Select All"),
                "gallery.import.select_all_plugins_tip", "Import every plugin in the source", ImportSidebarSelectAllPlugins,
                VPBTranslation.T("gallery.import.clear_all_plugins", "Clear All"),
                "gallery.import.clear_all_plugins_tip", "Exclude every plugin from import", ImportSidebarClearAllPlugins);

            // Scroll view sits below the "Pick plugins" toggle. Its height is reserved per the live entry count
            // (UpdatePluginChecklistHeight) since the option panel is ContentSizeFitter-driven; the row list
            // scrolls when it overflows ImportSidebarVisiblePluginRows.
            importSidebarPluginChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = UI.AddLE(importSidebarPluginChecklistRoot, flexibleWidth: 1f);
            importSidebarPluginChecklistLe = scrollLe;
            innerPaneScaleActions.Add(s => UpdatePluginChecklistHeight());

            Transform content = importSidebarPluginChecklistRoot.GetComponent<ScrollRect>().content.transform;
            AddImportSidebarChecklistCaption(content, "Plugins in source (check to import)");

            for (int i = 0; i < ImportSidebarMaxPluginRows; i++)
            {
                GameObject row = CreateImportSidebarChecklistRow(content, "PluginRow_", i);
                importSidebarPluginRowPool.Add(row);
                row.SetActive(false);
            }
            importSidebarPluginChecklistRoot.SetActive(false);
        }

        // Rebuilds the checkbox list from the selected source atom's plugins. Visible only for Plugins + gate on.
        // Switching source atom resets the checks to NONE (sig change); within the same atom, checks are preserved.
        // Opting into "Pick plugins" means "I want to choose", so the list starts empty rather than mirroring the
        // gate-off "import all" behavior (an all-checked default would make the picker a no-op).
        private void RefreshPluginChecklist()
        {
            bool show = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Plugins) && importSidebarPickPlugins;
            if (importSidebarPluginChecklistRoot != null) importSidebarPluginChecklistRoot.SetActive(show);
            if (importSidebarPluginBulkRow != null) importSidebarPluginBulkRow.SetActive(show);
            if (!show || importSidebarPluginRowPool.Count == 0) return;

            importSidebarPluginEntries = BuildSourcePluginEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarPluginSelectionSig)
            {
                importSidebarSelectedPluginKeys.Clear();
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
            if (bg != null) bg.color = selected ? ImportSidebarSelectedAccent : ColorInactiveRow;

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

        private void BuildImportSidebarCUAChecklist(Transform parent, ImportCUAChecklistHandles ui)
        {
            ui.BulkRow = BuildImportSidebarSelectClearBulkRow(
                parent, "CUABulkRow",
                VPBTranslation.T("gallery.import.select_all_cuas", "Select All"),
                "gallery.import.select_all_cuas_tip", "Import every CUA in the source", ImportSidebarSelectAllCUAs,
                VPBTranslation.T("gallery.import.clear_all_cuas", "Clear All"),
                "gallery.import.clear_all_cuas_tip", "Exclude every CUA from import", ImportSidebarClearAllCUAs);

            ui.ChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = UI.AddLE(ui.ChecklistRoot, flexibleWidth: 1f);
            ui.ChecklistLe = scrollLe;
            innerPaneScaleActions.Add(s => UpdateCUAChecklistHeights());

            Transform content = ui.ChecklistRoot.GetComponent<ScrollRect>().content.transform;
            AddImportSidebarChecklistCaption(content, "CUAs in source (check to import)");

            for (int i = 0; i < ImportSidebarMaxCUARows; i++)
            {
                GameObject row = CreateImportSidebarChecklistRow(content, "CUARow_", i);
                ui.RowPool.Add(row);
                row.SetActive(false);
            }
            ui.ChecklistRoot.SetActive(false);
        }

        private GameObject CreateImportSidebarChecklistRow(Transform parent, string namePrefix, int index)
        {
            GameObject row = new GameObject(namePrefix + index);
            row.transform.SetParent(parent, false);

            LayoutElement le = UI.AddLE(row, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);

            Image bg = AddImportSidebarRoundedBg(row, ColorInactiveRow);

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);
            UI.EnsureFloatChromeHoverBorder(row, inward: true);

            Text label = CreateImportSidebarLabel(row.transform, "", ImportSidebarBaseFontSize);
            ConfigureImportSidebarChecklistLabel(label);
            Text tipLabel = label;
            AddDynamicTooltip(row, () => ImportChecklistRowTooltip(tipLabel));

            LayoutElement leCaptured = le;
            Text txtCaptured = label;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        // Rebuilds the CUA checklist from the source scene. Appearance shows picker only with import+picked gates;
        // standalone CUA type shows picker when "Pick CUAs" is on. Both lists share the same checked ids.
        private void RefreshCUAChecklist()
        {
            bool showAppearance = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance)
                && importSidebarImportLinkedCUAs && importSidebarPickCUAs;
            bool showCuaOnly = importSidebarMultiSelectedTypes.Contains(VpbResourceType.CUA)
                && importSidebarPickCUAs;
            bool needEntries = showAppearance || showCuaOnly;

            SetCUAChecklistVisible(importSidebarAppearanceCUAUi, showAppearance);
            SetCUAChecklistVisible(importSidebarCuaOnlyCUAUi, showCuaOnly);

            if (!needEntries) return;

            importSidebarCUAEntries = BuildSourceCUAEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarCUASelectionSig)
            {
                // Opting into "Pick CUAs" starts with nothing checked (see RefreshPluginChecklist rationale).
                importSidebarSelectedCUAKeys.Clear();
                importSidebarCUASelectionSig = sig;
            }

            UpdateCUAChecklistHeights();
            RenderCUAChecklistRows();
        }

        private static void SetCUAChecklistVisible(ImportCUAChecklistHandles ui, bool show)
        {
            if (ui.ChecklistRoot != null) ui.ChecklistRoot.SetActive(show);
            if (ui.BulkRow != null) ui.BulkRow.SetActive(show);
        }

        private void UpdateCUAChecklistHeights()
        {
            UpdateCUAChecklistHeight(importSidebarAppearanceCUAUi);
            UpdateCUAChecklistHeight(importSidebarCuaOnlyCUAUi);
        }

        private void UpdateCUAChecklistHeight(ImportCUAChecklistHandles ui)
        {
            if (ui.ChecklistLe == null) return;
            int visibleRows = Mathf.Min(importSidebarCUAEntries.Count, ImportSidebarVisibleCUARows);
            float s = ChromeScale;
            ui.ChecklistLe.preferredHeight = (visibleRows + 1) * (ImportSidebarBaseRowHeight + 2f) * s;
        }

        private void RenderCUAChecklistRows()
        {
            RenderCUAChecklistRows(importSidebarAppearanceCUAUi);
            RenderCUAChecklistRows(importSidebarCuaOnlyCUAUi);
        }

        private void RenderCUAChecklistRows(ImportCUAChecklistHandles ui)
        {
            for (int i = 0; i < ui.RowPool.Count; i++)
            {
                GameObject row = ui.RowPool[i];
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
            if (bg != null) bg.color = selected ? ImportSidebarSelectedAccent : ColorInactiveRow;

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

        // ---- Scene atom picker (every non-Person atom in the source scene) ----

        private void BuildImportSidebarSceneAtomSearchRow(Transform parent)
        {
            importSidebarSceneAtomSearchRow = new GameObject("SceneAtomSearchRow");
            importSidebarSceneAtomSearchRow.transform.SetParent(parent, false);
            LayoutElement searchLe = UI.AddLE(importSidebarSceneAtomSearchRow, preferredHeight: ImportSidebarBaseRowHeight * 1.05f, flexibleWidth: 1f);

            importSidebarSceneAtomSearchInput = CreateSearchInput(
                importSidebarSceneAtomSearchRow, ImportSidebarBaseWidth,
                OnImportSidebarSceneAtomSearchChanged,
                () => SetImportSidebarSceneAtomSearch(string.Empty));
            RectTransform searchRt = importSidebarSceneAtomSearchInput.GetComponent<RectTransform>();
            searchRt.anchorMin = Vector2.zero;
            searchRt.anchorMax = Vector2.one;
            searchRt.pivot = new Vector2(0.5f, 0.5f);
            searchRt.offsetMin = Vector2.zero;
            searchRt.offsetMax = Vector2.zero;
            LayoutElement inputLe = importSidebarSceneAtomSearchInput.gameObject.GetComponent<LayoutElement>();
            if (inputLe == null) inputLe = importSidebarSceneAtomSearchInput.gameObject.AddComponent<LayoutElement>();
            inputLe.flexibleWidth = 1f;
            inputLe.preferredHeight = ImportSidebarBaseRowHeight;

            LayoutElement searchLeC = searchLe;
            innerPaneScaleActions.Add(s => {
                if (searchLeC != null) searchLeC.preferredHeight = ImportSidebarBaseRowHeight * 1.05f * s;
                if (importSidebarSceneAtomSearchInput != null)
                    RescaleSearchInput(importSidebarSceneAtomSearchInput, s);
            });
            importSidebarSceneAtomSearchRow.SetActive(false);
        }

        private void OnImportSidebarSceneAtomSearchChanged(string value)
        {
            importSidebarSceneAtomSearchFilter = value ?? string.Empty;
            RefreshSceneAtomChecklistFilteredOnly();
        }

        private void SetImportSidebarSceneAtomSearch(string value)
        {
            importSidebarSceneAtomSearchFilter = value ?? string.Empty;
            if (importSidebarSceneAtomSearchInput != null)
                importSidebarSceneAtomSearchInput.text = importSidebarSceneAtomSearchFilter;
            RefreshSceneAtomChecklistFilteredOnly();
        }

        private void RefreshSceneAtomChecklistFilteredOnly()
        {
            if (!importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)) return;
            ApplySceneAtomSearchFilter();
            UpdateSceneAtomChecklistHeight();
            RenderSceneAtomChecklistRows();
            RefreshSceneAtomEmptyHint();
            RefreshApplyButtonEnabled();
            RebuildImportSidebarContent();
        }

        private void ApplySceneAtomSearchFilter()
        {
            importSidebarSceneAtomFilteredEntries.Clear();
            string needle = (importSidebarSceneAtomSearchFilter ?? string.Empty).Trim();
            bool hasNeedle = needle.Length > 0;

            foreach (ImportSceneAtomEntry e in importSidebarSceneAtomEntries)
            {
                if (hasNeedle)
                {
                    if (e.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && e.Type.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                importSidebarSceneAtomFilteredEntries.Add(e);
            }
        }

        private void RefreshSceneAtomEmptyHint()
        {
            if (importSidebarSceneAtomEmptyHintRow == null) return;
            bool showPicker = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)
                && importSidebarPickSceneAtoms;
            if (!showPicker)
            {
                importSidebarSceneAtomEmptyHintRow.SetActive(false);
                return;
            }
            bool empty = importSidebarSceneAtomFilteredEntries.Count == 0;
            importSidebarSceneAtomEmptyHintRow.SetActive(empty);
            if (empty && importSidebarSceneAtomEmptyHint != null)
            {
                importSidebarSceneAtomEmptyHint.text = importSidebarSceneAtomEntries.Count == 0
                    ? "No importable atoms in this scene."
                    : "No atoms match the current search.";
            }
        }

        private void BuildImportSidebarSceneAtomChecklist(Transform parent)
        {
            importSidebarSceneAtomUi.BulkRow = BuildImportSidebarSelectClearBulkRow(
                parent, "SceneAtomBulkRow",
                VPBTranslation.T("gallery.import.select_all_atoms", "Select All"),
                "gallery.import.select_all_atoms_tip", "Select every importable atom in the source scene", ImportSidebarSelectAllSceneAtoms,
                VPBTranslation.T("gallery.import.clear_all_atoms", "Clear All"),
                "gallery.import.clear_all_atoms_tip", "Deselect every atom", ImportSidebarClearAllSceneAtoms);

            importSidebarSceneAtomUi.ChecklistRoot = UI.CreateVScrollableContent(
                parent.gameObject, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 12f, spacing: 2f, addBottomFlexSpacer: false);
            LayoutElement scrollLe = UI.AddLE(importSidebarSceneAtomUi.ChecklistRoot, flexibleWidth: 1f);
            importSidebarSceneAtomUi.ChecklistLe = scrollLe;
            innerPaneScaleActions.Add(s => UpdateSceneAtomChecklistHeight());

            Transform content = importSidebarSceneAtomUi.ChecklistRoot.GetComponent<ScrollRect>().content.transform;
            AddImportSidebarChecklistCaption(content, "Atoms in source scene (check to import)");

            for (int i = 0; i < ImportSidebarSceneAtomRowPoolInitial; i++)
            {
                GameObject row = CreateImportSidebarChecklistRow(content, "SceneAtomRow_", i);
                importSidebarSceneAtomUi.RowPool.Add(row);
                row.SetActive(false);
            }

            GameObject emptyGO = new GameObject("SceneAtomEmptyHint");
            emptyGO.transform.SetParent(parent, false);
            importSidebarSceneAtomEmptyHintRow = emptyGO;
            LayoutElement emptyLe = UI.AddLE(emptyGO, preferredHeight: ImportSidebarBaseRowHeight * 1.2f, flexibleWidth: 1f);
            importSidebarSceneAtomEmptyHint = AddSimpleLabelText(
                emptyGO.transform,
                "No importable atoms in this scene.",
                ImportSidebarBaseFontSize,
                GalleryUiColorTokens.TextDim);
            importSidebarSceneAtomEmptyHint.fontStyle = FontStyle.Italic;
            importSidebarSceneAtomEmptyHint.alignment = TextAnchor.MiddleCenter;
            emptyGO.SetActive(false);

            importSidebarSceneAtomUi.ChecklistRoot.SetActive(false);
        }

        private void ApplySceneAtomRowScale(GameObject row, float s)
        {
            if (row == null) return;
            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = ImportSidebarBaseRowHeight * s;
            Text t = row.GetComponentInChildren<Text>();
            ApplyScaledFont(t, ImportSidebarBaseFontSize, s);
        }

        private void RefreshSceneAtomChecklist()
        {
            bool typeSelected = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms);
            bool showPicker = typeSelected && importSidebarPickSceneAtoms;

            if (importSidebarSceneAtomSearchRow != null)
                importSidebarSceneAtomSearchRow.SetActive(showPicker);
            SetCUAChecklistVisible(importSidebarSceneAtomUi, showPicker);

            // Only build/enumerate when the picker is actually shown (matches the plugin/CUA pickers, which bail
            // before parsing when their gate is off — avoids an unnecessary scene parse on the gate-off path).
            if (!showPicker) return;

            importSidebarSceneAtomEntries = BuildSourceSceneAtomEntries();

            string sig = (importSidebarSourceScene != null ? importSidebarSourceScene.Uid : "") + "|" + (importSidebarSourceAtomId ?? "");
            if (sig != importSidebarSceneAtomSelectionSig)
            {
                // Opting into "Pick atoms" starts with nothing checked (see RefreshPluginChecklist rationale).
                importSidebarSelectedSceneAtomKeys.Clear();
                importSidebarSceneAtomSelectionSig = sig;
                importSidebarSceneAtomSearchFilter = string.Empty;
                if (importSidebarSceneAtomSearchInput != null)
                    importSidebarSceneAtomSearchInput.text = string.Empty;
            }

            ApplySceneAtomSearchFilter();
            UpdateSceneAtomChecklistHeight();
            RenderSceneAtomChecklistRows();
            RefreshSceneAtomEmptyHint();
        }

        private void UpdateSceneAtomChecklistHeight()
        {
            if (importSidebarSceneAtomUi.ChecklistLe == null) return;
            int filteredCount = importSidebarSceneAtomFilteredEntries.Count;
            int visibleRows = Mathf.Clamp(filteredCount, 1, ImportSidebarVisibleSceneAtomRows);
            float s = ChromeScale;
            importSidebarSceneAtomUi.ChecklistLe.preferredHeight = (visibleRows + 1) * (ImportSidebarBaseRowHeight + 2f) * s;
        }

        private void EnsureSceneAtomRowPool(int needed)
        {
            if (needed <= 0 || importSidebarSceneAtomUi.ChecklistRoot == null) return;
            int cap = Mathf.Min(needed, ImportSidebarMaxSceneAtomRows);
            ScrollRect sr = importSidebarSceneAtomUi.ChecklistRoot.GetComponent<ScrollRect>();
            Transform content = sr != null && sr.content != null ? sr.content.transform : null;
            if (content == null) return;
            while (importSidebarSceneAtomUi.RowPool.Count < cap)
            {
                int i = importSidebarSceneAtomUi.RowPool.Count;
                GameObject row = CreateImportSidebarChecklistRow(content, "SceneAtomRow_", i);
                importSidebarSceneAtomUi.RowPool.Add(row);
                ApplySceneAtomRowScale(row, ChromeScale);
                row.SetActive(false);
            }
        }

        private void RenderSceneAtomChecklistRows()
        {
            int filteredCount = importSidebarSceneAtomFilteredEntries.Count;
            int renderCount = Mathf.Min(filteredCount, ImportSidebarMaxSceneAtomRows);
            EnsureSceneAtomRowPool(renderCount);

            for (int i = 0; i < importSidebarSceneAtomUi.RowPool.Count; i++)
            {
                GameObject row = importSidebarSceneAtomUi.RowPool[i];
                if (i < renderCount)
                {
                    ImportSceneAtomEntry e = importSidebarSceneAtomFilteredEntries[i];
                    ConfigureSceneAtomRow(row, e,
                        i == renderCount - 1 && filteredCount > renderCount
                            ? filteredCount - renderCount : 0);
                    row.SetActive(true);
                }
                else row.SetActive(false);
            }
        }

        private static string FormatSceneAtomDisplayId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            int slash = id.IndexOf('/');
            if (slash < 0) return id;
            int lastSlash = id.LastIndexOf('/');
            string leaf = id.Substring(lastSlash + 1);
            if (lastSlash <= 0) return leaf;
            string parent = id.Substring(0, lastSlash);
            return parent + " \u203a " + leaf;
        }

        private static string FormatSceneAtomRowLabel(ImportSceneAtomEntry e, bool selected, int moreHidden)
        {
            string mark = selected ? "[x] " : "[ ] ";
            string idDisplay = FormatSceneAtomDisplayId(e.Id);
            string text = mark + idDisplay;
            if (!string.IsNullOrEmpty(e.Type) && !string.Equals(e.Type, e.Id, StringComparison.Ordinal)
                && !string.Equals(e.Type, idDisplay, StringComparison.Ordinal))
                text += "  " + e.Type;
            if (e.LinksToPerson) text += "  (on person)";
            if (e.UidCollision) text += "  (exists)";
            if (moreHidden > 0) text += "  (+" + moreHidden + " more — narrow search)";
            return text;
        }

        private void ConfigureSceneAtomRow(GameObject row, ImportSceneAtomEntry e, int moreHidden)
        {
            bool selected = importSidebarSelectedSceneAtomKeys.Contains(e.Id);
            string text = FormatSceneAtomRowLabel(e, selected, moreHidden);

            Text t = row.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.text = text;
                ConfigureImportSidebarChecklistLabel(t);
            }
            ApplySceneAtomRowScale(row, ChromeScale);
            Image bg = row.GetComponent<Image>();
            if (bg != null) bg.color = selected ? ImportSidebarSelectedAccent : ColorInactiveRow;

            Button btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                string id = e.Id;
                btn.onClick.AddListener(() => ToggleSceneAtomSelected(id));
            }
        }

        private void ToggleSceneAtomSelected(string id)
        {
            if (importSidebarSelectedSceneAtomKeys.Contains(id)) importSidebarSelectedSceneAtomKeys.Remove(id);
            else importSidebarSelectedSceneAtomKeys.Add(id);
            RenderSceneAtomChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private void ImportSidebarSelectAllSceneAtoms()
        {
            importSidebarSelectedSceneAtomKeys.Clear();
            foreach (ImportSceneAtomEntry e in importSidebarSceneAtomEntries)
                importSidebarSelectedSceneAtomKeys.Add(e.Id);
            RenderSceneAtomChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private void ImportSidebarClearAllSceneAtoms()
        {
            importSidebarSelectedSceneAtomKeys.Clear();
            RenderSceneAtomChecklistRows();
            RefreshApplyButtonEnabled();
        }

        private List<ImportSceneAtomEntry> BuildSourceSceneAtomEntries()
        {
            var result = new List<ImportSceneAtomEntry>();
            JSONClass scene = EnsureLoadedSceneJSON();
            if (scene == null) return result;
            foreach (SceneAtomImporter.SceneAtomEntry e in SceneAtomImporter.EnumerateSceneAtoms(scene, importSidebarSourceAtomId))
            {
                result.Add(new ImportSceneAtomEntry
                {
                    Id = e.Id,
                    Type = e.Type,
                    LinksToPerson = e.LinksToPerson,
                    UidCollision = e.UidCollision
                });
            }
            return result;
        }

        // Enumerate every CustomUnityAsset in the source scene. Free-standing CUAs live OUTSIDE the person atom, so
        // this needs the whole scene JSON (unlike the plugin picker, which slices one person). Full scene is loaded
        // async after open; EnsureLoadedSceneJSON kicks/waits for that load and does not sync-parse on the click frame.
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

        // Returns cached source scene JSON, or starts a background load and returns null until ready.
        // Apply paths that cannot wait keep their own sync fallback (see StartImportSelectedSceneAtoms).
        private JSONClass EnsureLoadedSceneJSON()
        {
            if (importSidebarLoadedSceneJSON != null) return importSidebarLoadedSceneJSON;
            if (importSidebarSourceScene == null) return null;
            if (!importSidebarSceneJsonLoading)
                BeginImportSceneJsonLoad(importSidebarSourceScene, writePersonCache: false);
            return importSidebarLoadedSceneJSON;
        }

        /// <summary>
        /// Blocking fallback for Apply when background load has not finished. Prefer waiting via
        /// WaitForImportSourceSceneReady when a coroutine is available.
        /// </summary>
        private JSONClass EnsureLoadedSceneJSONSync()
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
                LogUtil.LogWarning("[VPB import] EnsureLoadedSceneJSONSync failed for " + importSidebarSourceScene.Uid + ": " + ex.Message);
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
            // Reason strip (above Apply) — shows why Apply is disabled.
            GameObject reasonGO = new GameObject("ApplyReason");
            reasonGO.transform.SetParent(parent, false);
            importSidebarApplyReasonRT = reasonGO.AddComponent<RectTransform>();
            importSidebarApplyReasonRT.anchorMin = new Vector2(0f, 0f);
            importSidebarApplyReasonRT.anchorMax = new Vector2(1f, 0f);
            importSidebarApplyReasonRT.pivot = new Vector2(0.5f, 0f);
            importSidebarApplyReasonBg = AddImportSidebarRoundedBg(reasonGO, ImportSidebarApplyReasonBg, raycastTarget: false);
            importSidebarApplyReasonLabel = AddSimpleLabelText(reasonGO.transform, "", ImportSidebarBaseFontSize - 1, ImportSidebarApplyReasonText);
            importSidebarApplyReasonLabel.alignment = TextAnchor.MiddleCenter;
            importSidebarApplyReasonLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            importSidebarApplyReasonLabel.verticalOverflow = VerticalWrapMode.Truncate;
            reasonGO.SetActive(false);

            Text reasonLblC = importSidebarApplyReasonLabel;
            innerPaneScaleActions.Add(s => ApplyScaledFont(reasonLblC, ImportSidebarBaseFontSize - 1, s));

            GameObject btn = new GameObject("Load");
            btn.transform.SetParent(parent, false);

            RectTransform rt = btn.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, ImportSidebarBaseApplyHeight);

            Image bg = AddImportSidebarRoundedBg(btn, ImportSidebarApplyBg);

            Button b = btn.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(OnImportSidebarApplyClicked);
            UI.NeutralizeSelectableColorTint(b);
            UI.EnsureFloatChromeHoverBorder(btn, inward: true);

            importSidebarApplyButton = b;
            importSidebarApplyButtonLabel = AddSimpleLabelText(
                btn.transform,
                VPBTranslation.T("gallery.import.apply_btn", "Import to atom"),
                ImportSidebarBaseFontSize,
                UI.PopupText);
            importSidebarApplyButtonLabel.alignment = TextAnchor.MiddleCenter;
            AddDynamicTooltip(btn, () =>
            {
                string block = GetImportSidebarApplyBlockReason();
                if (!string.IsNullOrEmpty(block))
                    return block;
                return VPBTranslation.T("gallery.import.apply_tip",
                    "Import the selected resource types from the source onto the target with the options above.")
                    + " (" + ImportSidebarSelectedTypesSummary() + " \u2192 "
                    + (importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "\u2014") + ")";
            });

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
                importSidebarMultiToggleLabel.text = importSidebarMultiSelectTypes
                    ? VPBTranslation.T("gallery.import.accumulate_on", "Accumulate: On")
                    : VPBTranslation.T("gallery.import.accumulate_off", "Accumulate: Off");
            if (importSidebarMultiToggleBg != null)
                importSidebarMultiToggleBg.color = importSidebarMultiSelectTypes
                    ? ImportSidebarMultiToggleBg : ImportSidebarMultiToggleOffBg;
        }

        private void OnImportSidebarTypeChosen(VpbResourceType t, bool additive)
        {
            bool available = IsImportTypeAvailable(t);
            bool already = importSidebarMultiSelectedTypes.Contains(t);

            if (!available)
            {
                // Paused chip: click clears intent only (cannot select empty types).
                if (already) importSidebarMultiSelectedTypes.Remove(t);
            }
            else if (additive)
            {
                if (already)
                    importSidebarMultiSelectedTypes.Remove(t);
                else
                    importSidebarMultiSelectedTypes.Add(t);
            }
            else
            {
                importSidebarMultiSelectedTypes.Clear();
                importSidebarMultiSelectedTypes.Add(t);
            }

            importSidebarPresetType = t;

            SyncImportSidebarOptionPanelsToSelection();
            RefreshTypeRadioButtonColors();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshSceneAtomChecklist();
            RefreshApplyButtonEnabled();
            try { RefreshImportSidebarWizardHeader(); } catch { }
            RebuildImportSidebarContent();
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        private void SyncImportSidebarOptionPanelsToSelection()
        {
            foreach (var kv in importSidebarOptionPanels)
            {
                // Hide options for paused (unavailable) types — nothing useful to configure.
                bool show = importSidebarMultiSelectedTypes.Contains(kv.Key) && IsImportTypeAvailable(kv.Key);
                kv.Value.SetActive(show);
            }
        }

        private void RefreshTypeRadioButtonColors()
        {
            foreach (var kv in importSidebarTypeRadioButtons)
            {
                bool available = IsImportTypeAvailable(kv.Key);
                bool active = importSidebarMultiSelectedTypes.Contains(kv.Key);
                bool paused = active && !available;

                Image img = kv.Value.GetComponent<Image>();
                if (img != null)
                {
                    if (paused) img.color = ImportSidebarPausedSelectedRow;
                    else if (!available) img.color = ImportSidebarUnavailableRow;
                    else if (active) img.color = ImportSidebarSelectedAccent;
                    else img.color = ColorInactiveRow;
                }

                Button btn = kv.Value.GetComponent<Button>();
                // Allow deselect of paused chips; block selecting empty types.
                if (btn != null) btn.interactable = available || active;

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

        // Recompute per-type counts from the selected source atom and re-skin the chips.
        // Empty types are greyed; selection INTENT is kept (paused) so Apply skips them — never silent prune.
        // Does NOT sync-parse the full scene: CUA/Atoms counts fill in when background JSON arrives;
        // person-type counts use SQLite atom JSON or an already-loaded scene slice (no deep-copy).
        private void RefreshSourceTypeAvailability()
        {
            importSidebarSourceTypeCounts.Clear();

            JSONClass scene = importSidebarLoadedSceneJSON;
            if (scene != null)
            {
                int cuaCount = 0;
                foreach (CUAAtomImporter.CuaEntry _ in
                         CUAAtomImporter.EnumerateSceneCUAs(scene, importSidebarSourceAtomId))
                    cuaCount++;
                importSidebarSourceTypeCounts[VpbResourceType.CUA] = cuaCount;

                int atomCount = 0;
                foreach (SceneAtomImporter.SceneAtomEntry _ in
                         SceneAtomImporter.EnumerateSceneAtoms(scene, importSidebarSourceAtomId))
                    atomCount++;
                importSidebarSourceTypeCounts[VpbResourceType.Atoms] = atomCount;
            }
            // else: CUA/Atoms counts stay unknown (chips remain selectable) until async JSON arrives.

            FillPersonDerivedTypeCounts();

            _importSidebarPausedScratch.Clear();
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
            {
                if (!IsImportTypeAvailable(t))
                    _importSidebarPausedScratch.Add(t);
            }

            if (_importSidebarPausedScratch.Count > 0 && importSidebarBuilt)
            {
                _importSidebarStatusSb.Length = 0;
                for (int i = 0; i < _importSidebarPausedScratch.Count; i++)
                {
                    if (i > 0) _importSidebarStatusSb.Append(", ");
                    _importSidebarStatusSb.Append(ShortNameForType(_importSidebarPausedScratch[i]));
                    _importSidebarStatusSb.Append(" (0)");
                }
                _importSidebarStatusSb.Append(VPBTranslation.T(
                    "gallery.import.type_paused_suffix",
                    " — kept selected, skipped on Apply"));
                try { ShowTemporaryStatus(_importSidebarStatusSb.ToString(), 2.5f); } catch { }
            }

            RefreshTypeRadioButtonColors();
            SyncImportSidebarOptionPanelsToSelection();
            RefreshSceneAtomChecklist();
            RefreshApplyButtonEnabled();
            try { RefreshImportSidebarWizardHeader(); } catch { }
        }

        // Clothing/Hair/Morphs/Plugins counts from the selected Person atom — prefer DB cache / in-memory
        // scene node; never trigger a full-scene parse just for chip labels.
        private void FillPersonDerivedTypeCounts()
        {
            if (string.IsNullOrEmpty(importSidebarSourceAtomId)) return;

            JSONArray storables = null;
            if (importSidebarLoadedSceneJSON != null)
            {
                JSONClass atom = FindPersonAtomNodeInLoadedScene(importSidebarSourceAtomId);
                if (atom != null && atom["storables"] != null)
                    storables = atom["storables"].AsArray;
            }

            if (storables == null && importSidebarSourceScene != null)
            {
                string atomJson = VpbLocalDatabase.TryReadSceneAtomJson(importSidebarSourceScene, importSidebarSourceAtomId);
                if (!string.IsNullOrEmpty(atomJson))
                {
                    try
                    {
                        JSONClass cached = JSON.Parse(atomJson).AsObject;
                        if (cached != null && cached["storables"] != null)
                            storables = cached["storables"].AsArray;
                    }
                    catch { }
                }
            }

            if (storables == null) return;

            int clothing = 0, hair = 0, morphs = 0, plugins = 0;
            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i] as JSONClass;
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

        private JSONClass FindPersonAtomNodeInLoadedScene(string atomId)
        {
            if (importSidebarLoadedSceneJSON == null || string.IsNullOrEmpty(atomId)) return null;
            JSONArray atoms = importSidebarLoadedSceneJSON["atoms"] != null
                ? importSidebarLoadedSceneJSON["atoms"].AsArray : null;
            if (atoms == null) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i].AsObject;
                if (a == null) continue;
                string pid = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value)) ? a["id"].Value : ("Person_" + i);
                if (pid == atomId) return a;
            }
            return null;
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
            SyncImportSidebarOptionPanelsToSelection();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshSceneAtomChecklist();
            try { RefreshImportSidebarWizardHeader(); } catch { }
            RebuildImportSidebarContent();
            RefreshApplyButtonEnabled();
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        /// <summary>Human-readable reason Apply is blocked, or null when Apply is ready.</summary>
        private string GetImportSidebarApplyBlockReason()
        {
            if (ImportSidebarMultiSelectBlocked())
                return VPBTranslation.T("gallery.import.block.multi_scene", "Select only one scene");
            if (importSidebarSourceScene == null)
                return VPBTranslation.T("gallery.import.block.no_scene", "Pick a source scene");
            bool needSourceAtom = importSidebarSourcePersonIds.Count > 0;
            if (needSourceAtom && string.IsNullOrEmpty(importSidebarSourceAtomId))
                return VPBTranslation.T("gallery.import.block.no_source_atom", "Pick a source person");
            if (ImportSidebarNeedsPersonTarget() && importSidebarTargetAtom == null)
                return VPBTranslation.T("gallery.import.block.no_target", "Pick a target person");
            if (importSidebarMultiSelectedTypes.Count == 0)
                return VPBTranslation.T("gallery.import.block.no_type", "Pick a resource type");
            if (CountAvailableSelectedImportTypes() == 0)
                return VPBTranslation.T("gallery.import.block.all_paused", "Selected types empty on source");
            if (importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)
                && IsImportTypeAvailable(VpbResourceType.Atoms))
            {
                if (importSidebarPickSceneAtoms)
                {
                    if (importSidebarSelectedSceneAtomKeys.Count == 0)
                        return VPBTranslation.T("gallery.import.block.no_atoms_picked", "Pick at least one atom");
                }
                else
                {
                    int atomCount;
                    if (!importSidebarSourceTypeCounts.TryGetValue(VpbResourceType.Atoms, out atomCount) || atomCount <= 0)
                        return VPBTranslation.T("gallery.import.block.no_atoms", "Source has no scene atoms");
                }
            }
            return null;
        }

        partial void RefreshApplyButtonEnabled()
        {
            string block = GetImportSidebarApplyBlockReason();
            bool ok = block == null;

            if (importSidebarApplyButton != null)
            {
                importSidebarApplyButton.interactable = ok;
                Image applyBg = importSidebarApplyButton.targetGraphic as Image;
                if (applyBg != null)
                    applyBg.color = ok ? ImportSidebarApplyBg : GalleryUiColorTokens.SurfaceMid;
            }

            if (importSidebarApplyReasonLabel != null && importSidebarApplyReasonRT != null)
            {
                bool showReason = !ok && !string.IsNullOrEmpty(block);
                importSidebarApplyReasonRT.gameObject.SetActive(showReason);
                if (showReason)
                    importSidebarApplyReasonLabel.text = block;
            }

            try { LayoutImportSidebarApplyBand(ChromeScale > 0f ? ChromeScale : 1f); } catch { }
            try { RefreshImportSidebarWizardHeader(); } catch { }
        }

        /// <summary>
        /// True when Apply needs a live Person target. Atoms-only without person-linked CUAs does not —
        /// relative placement is best-effort when a person is selected (absolute coords otherwise).
        /// Fixes Apply no-op on person-less scenes (UIButton import repro).
        /// </summary>
        private bool ImportSidebarNeedsPersonTarget()
        {
            if (importSidebarMultiSelectedTypes == null || importSidebarMultiSelectedTypes.Count == 0)
                return true;

            int availableCount = 0;
            bool atomsSelected = false;
            bool nonAtomsSelected = false;
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
            {
                if (!IsImportTypeAvailable(t)) continue;
                availableCount++;
                if (t == VpbResourceType.Atoms) atomsSelected = true;
                else nonAtomsSelected = true;
            }
            if (availableCount == 0) return true;

            bool onlyAtoms = atomsSelected && !nonAtomsSelected;
            if (!onlyAtoms)
                return true;

            // Person-linked CUAs in the scene-atom picker still route through CUAAtomImporter.
            if (importSidebarPickSceneAtoms && importSidebarSelectedSceneAtomKeys.Count > 0
                && importSidebarSceneAtomEntries != null)
            {
                for (int i = 0; i < importSidebarSceneAtomEntries.Count; i++)
                {
                    ImportSceneAtomEntry e = importSidebarSceneAtomEntries[i];
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    if (!importSidebarSelectedSceneAtomKeys.Contains(e.Id)) continue;
                    if (e.LinksToPerson || string.Equals(e.Type, "CustomUnityAsset", StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private void OnImportSidebarApplyClicked()
        {
            if (importSidebarSourceScene == null)
            {
                LogUtil.LogWarning("[VPB import] No source scene loaded.");
                return;
            }

            bool needTargetPerson = ImportSidebarNeedsPersonTarget();
            if (needTargetPerson && importSidebarTargetAtom == null)
            {
                LogUtil.LogWarning("[VPB import] No target atom selected.");
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.import.need_target", "Select a target Person atom first."),
                    2f);
                return;
            }

            // Snapshot the target atom BEFORE any changes so the user can undo the entire import.
            if (importSidebarTargetAtom != null)
            {
                try
                {
                    Action undoAction = CaptureAtomSnapshotAction(importSidebarTargetAtom);
                    if (undoAction != null) PushUndo(undoAction);
                }
                catch { }
            }

            string sourceHostUid = (importSidebarSourceScene is VarFileEntry sceneVar && sceneVar.Package != null)
                ? sceneVar.Package.Uid : null;

            // Apply each selected AVAILABLE type in sequence, EXCEPT Pose. Paused (0-count) types are skipped.
            // Pose deferred until other types settle (scale/morphs).
            bool hasPose = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Pose)
                && IsImportTypeAvailable(VpbResourceType.Pose);
            foreach (VpbResourceType t in importSidebarMultiSelectedTypes)
            {
                if (!IsImportTypeAvailable(t)) continue;
                if (t == VpbResourceType.Pose || t == VpbResourceType.CUA || t == VpbResourceType.Atoms) continue;
                ApplyOneTypeImport(t, sourceHostUid);
            }

            // CUA import runs from Appearance (optional add-on) or the standalone CUA type. Must run AFTER pose
            // so anchors land on the final posed skeleton. When Atoms is also selected it supersedes standalone CUA.
            bool importCUAsFromAppearance = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance)
                && IsImportTypeAvailable(VpbResourceType.Appearance)
                && importSidebarImportLinkedCUAs;
            bool importCUAsStandalone = importSidebarMultiSelectedTypes.Contains(VpbResourceType.CUA)
                && IsImportTypeAvailable(VpbResourceType.CUA)
                && !(importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)
                    && IsImportTypeAvailable(VpbResourceType.Atoms));
            bool importCUAs = importCUAsFromAppearance || importCUAsStandalone;
            bool importSceneAtoms = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)
                && IsImportTypeAvailable(VpbResourceType.Atoms);

            if (hasPose)
                StartCoroutine(ApplyDeferredPoseThenSpawnImports(sourceHostUid, importCUAs, importSceneAtoms));
            else
            {
                if (importCUAs)
                    RunCUAImportWithOptionalDelete(importCUAsStandalone);
                if (importSceneAtoms)
                    StartImportSelectedSceneAtoms(sourceHostUid);
            }
        }

        // Maps toolbox AppearanceClothingApplyMode → ClothingApplyMode for Appearance-type imports.
        // Outfit Only (clothingonly) is not an Appearance import — mapping it to ClothingOnly skipped
        // skin/hair/morphs. Keep / Merge Outfit / Full Look (replace) only.
        private ClothingApplyMode ResolveAppearanceClothingApplyModeFromConfig()
        {
            string cfg = AppearanceClothingApplyMode;
            if (string.IsNullOrEmpty(cfg)) cfg = "replace";
            if (string.Equals(cfg, "keep", StringComparison.OrdinalIgnoreCase))
                return ClothingApplyMode.Keep;
            if (string.Equals(cfg, "mergeoutfit", StringComparison.OrdinalIgnoreCase))
                return ClothingApplyMode.MergeOutfit;
            if (string.Equals(cfg, "merge", StringComparison.OrdinalIgnoreCase))
                return ClothingApplyMode.Merge;
            return ClothingApplyMode.Replace;
        }

        // Deletes target-linked CUAs when only the standalone CUA type is selected (Appearance path deletes during
        // its own apply). Then spawns the chosen source CUAs as native atoms.
        private void RunCUAImportWithOptionalDelete(bool standaloneCuaType)
        {
            bool appearanceWillDelete = importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance)
                && importSidebarDeleteTargetCUAs;
            if (standaloneCuaType && importSidebarDeleteTargetCUAs && !appearanceWillDelete)
                DeleteTargetLinkedCUAs(importSidebarTargetAtom);

            string sourceHostUid = (importSidebarSourceScene is VarFileEntry sceneVar && sceneVar.Package != null)
                ? sceneVar.Package.Uid : null;
            StartImportLinkedCUAs(importSidebarSourceScene, importSidebarSourceAtomId, importSidebarTargetAtom, sourceHostUid);
        }

        // Applies the pose once the target skeleton has settled from the appearance load (scale/morphs settle over
        // several frames), then runs CUA / scene-atom spawns so they anchor to the final pose.
        private IEnumerator ApplyDeferredPoseThenSpawnImports(string sourceHostUid, bool importCUAs, bool importSceneAtoms)
        {
            yield return VPB.src.util.CUAAtomImporter.WaitForPersonSettled(importSidebarTargetAtom);
            ApplyOneTypeImport(VpbResourceType.Pose, sourceHostUid);
            yield return VPB.src.util.CUAAtomImporter.WaitForPersonSettled(importSidebarTargetAtom);
            if (importCUAs)
            {
                bool standalone = importSidebarMultiSelectedTypes.Contains(VpbResourceType.CUA)
                    && IsImportTypeAvailable(VpbResourceType.CUA)
                    && !(importSidebarMultiSelectedTypes.Contains(VpbResourceType.Atoms)
                        && IsImportTypeAvailable(VpbResourceType.Atoms));
                RunCUAImportWithOptionalDelete(standalone);
            }
            if (importSceneAtoms)
                StartImportSelectedSceneAtoms(sourceHostUid);
        }

        private void ApplyOneTypeImport(VpbResourceType type, string sourceHostUid)
        {
            if (!IsImportTypeAvailable(type)) return;

            JSONClass presetJSON = BuildPresetJSONForCurrentSelection();
            if (presetJSON == null)
            {
                LogUtil.LogWarning("[VPB import] Could not build preset JSON for type " + type);
                return;
            }

            string storableOverride = ResolveStorableOverrideForType(type);

            // Appearance: sidebar "Disable clothing load" forces Keep. Otherwise honor the toolbox
            // Appearance clothing mode (Keep My Outfit / Full Look / Outfit Only / Merge Outfit) — same
            // contract as drag-drop. Clothing/Hair use the merge toggle. Morphs/Skin/Breast/etc. always
            // Replace — clothing merge toggle must not append morph banks (deforms person).
            ClothingApplyMode mode;
            if (type == VpbResourceType.Appearance)
            {
                mode = importSidebarSuppressClothingLoad
                    ? ClothingApplyMode.Keep
                    : ResolveAppearanceClothingApplyModeFromConfig();
            }
            else if (type == VpbResourceType.Clothing || type == VpbResourceType.Hair)
            {
                mode = importSidebarMergeClothingOrHair ? ClothingApplyMode.Merge : ClothingApplyMode.Replace;
            }
            else
            {
                mode = ClothingApplyMode.Replace;
            }

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
                if (importSidebarPickPlugins)
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

            // Appearance/Morphs: zero prior look morphs BEFORE ForceRun/Ensure RefreshPackageMorphs.
            // VaM bank rebuild snapshots non-default values and restores them after ClearPackageMorphs —
            // without this, previous looks bleed into later imports (corrupted face after many applies).
            if (type == VpbResourceType.Appearance || type == VpbResourceType.Morphs)
            {
                try
                {
                    VamOnDemandLoader.ResetAppearanceMorphValues(
                        importSidebarTargetAtom,
                        "vpb_import_pre_refresh_" + type);
                }
                catch (System.Exception ex)
                {
                    LogUtil.LogWarning("[VPB import] ResetAppearanceMorphValues failed: " + ex.Message);
                }
            }

            try
            {
                SceneLoadingUtils.PrewarmAndEnsureForPresetSlice(sliceJson, sourceHostUid);
                // Clothing/hair-only stale UIDs skip RefreshPackageMorphs inside ForceRun (Naturalis cost).
                VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("vpb_import_slice_prewarm_flush");
            }
            catch (System.Exception ex)
            {
                LogUtil.LogWarning("[VPB import] Slice dependency prep failed: " + ex.Message);
            }

            // Registration alone doesn't rebuild VaM's clothing/hair catalog, so newly-prewarmed item packages
            // would report "is missing"; rebuild the target's catalogs before apply.
            RefreshTargetClothingAndHairCatalog(importSidebarTargetAtom);

            // Morph banks are separate from clothing/hair catalogs. Clothing/hair-only ForceRun can skip
            // RefreshPackageMorphs (Naturalis cost); ingest here so new package morph UIDs resolve.
            if (type == VpbResourceType.Appearance || type == VpbResourceType.Morphs)
            {
                try
                {
                    VamOnDemandLoader.EnsurePackageMorphsIngestedIfNeeded(
                        importSidebarTargetAtom,
                        sliceJson,
                        "vpb_import_" + type);
                }
                catch (System.Exception ex)
                {
                    LogUtil.LogWarning("[VPB import] EnsurePackageMorphsIngested failed: " + ex.Message);
                }
            }

            // Only suppress real clothing: keep the target's real garments, bring the source's non-real (cosmetic)
            // clothing. Drop target old non-real -> merge source non-real -> appearance with the Keep clothing lock.
            // Nested toggle only applies when sidebar "Disable clothing load" is on (not toolbox Keep alone).
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
            JSONClass scene = null;
            if (source == importSidebarSourceScene)
            {
                scene = EnsureLoadedSceneJSON();
                if (scene == null) scene = EnsureLoadedSceneJSONSync();
            }
            if (scene == null)
            {
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
            }
            if (scene == null) return;
            // Picker on -> import exactly the checked CUAs; off -> all person-linked CUAs (legacy behavior).
            bool pickerActive = importSidebarPickCUAs
                && (importSidebarMultiSelectedTypes.Contains(VpbResourceType.CUA)
                    || (importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance) && importSidebarImportLinkedCUAs));
            HashSet<string> selectedIds = pickerActive
                ? new HashSet<string>(importSidebarSelectedCUAKeys, StringComparer.Ordinal) : null;
            // Match the plugin/scene-atom pickers: with the picker on and nothing checked, skip CUA import
            // (and say so) rather than silently importing zero.
            if (pickerActive && selectedIds.Count == 0)
            {
                LogUtil.LogWarning("[VPB][CUA] picker on but nothing checked; skipping CUA import.");
                return;
            }
            StartCoroutine(VPB.src.util.CUAAtomImporter.ImportSelectedCUAsAsAtoms(
                scene, sourceAtomId, target, sourceHostUid, selectedIds,
                importSidebarCUARelativeToPerson, replaceExisting: !importSidebarCuaMergeLoad));
        }

        // Spawns the checked non-Person atoms from the source scene (CUAs delegate to CUAAtomImporter).
        private void StartImportSelectedSceneAtoms(string sourceHostUid)
        {
            if (importSidebarSourceScene == null)
            {
                LogUtil.LogWarning("[VPB][Atoms][import] abort — sourceScene=null");
                return;
            }
            if (ImportSidebarNeedsPersonTarget() && importSidebarTargetAtom == null)
            {
                LogUtil.LogWarning("[VPB][Atoms][import] abort — person target required but missing.");
                return;
            }

            JSONClass scene = EnsureLoadedSceneJSON();
            if (scene == null)
                scene = EnsureLoadedSceneJSONSync();
            if (scene == null)
            {
                LogUtil.LogWarning("[VPB][Atoms][import] abort — scene JSON null.");
                return;
            }

            HashSet<string> selectedIds;
            if (importSidebarPickSceneAtoms)
            {
                if (importSidebarSelectedSceneAtomKeys.Count == 0)
                {
                    LogUtil.LogWarning("[VPB][Atoms][import] abort — picker on but nothing checked.");
                    return;
                }
                selectedIds = new HashSet<string>(importSidebarSelectedSceneAtomKeys, StringComparer.Ordinal);
            }
            else
            {
                selectedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SceneAtomImporter.SceneAtomEntry e in SceneAtomImporter.EnumerateSceneAtoms(scene, importSidebarSourceAtomId))
                    selectedIds.Add(e.Id);
                if (selectedIds.Count == 0)
                {
                    LogUtil.LogWarning("[VPB][Atoms][import] abort — no importable atoms in source scene.");
                    return;
                }
            }

            string preferredPersonUid = importSidebarTargetAtom != null
                ? importSidebarTargetAtom.uid
                : null;

            List<SceneAtomImporter.BrokenUidRef> broken
                = SceneAtomImporter.CollectBrokenExternalUidRefs(scene, selectedIds, preferredPersonUid);
            if (broken == null)
                broken = new List<SceneAtomImporter.BrokenUidRef>();

            // Refs that land on their own (same uid live, receivers all matched) are not a decision —
            // only prompt when something genuinely cannot be resolved. "Always show remap prompt"
            // forces the gate for users who want to inspect every import.
            List<SceneAtomImporter.BrokenUidRef> needsAttention
                = SceneAtomImporter.FilterUidRefsNeedingAttention(broken);
            if (needsAttention.Count == 0 && !importSidebarAlwaysShowRemapPrompt)
            {
                // Identity destinations need no uid map, but their auto-matched plugin slot moves do.
                Dictionary<string, Dictionary<string, string>> autoReceiverRemap
                    = SceneAtomImporter.BuildAutoReceiverRemapByUid(broken);
                int autoRecv = 0;
                foreach (KeyValuePair<string, Dictionary<string, string>> kv in autoReceiverRemap)
                    if (kv.Value != null) autoRecv += kv.Value.Count;

                LogUtil.Log("[VPB][Atoms][import] all " + broken.Count
                    + " external UID ref(s) resolve as-is — remap prompt skipped, "
                    + autoRecv + " auto receiver remap(s).");
                SetStatus(VPBTranslation.T(
                    "gallery.import.remap_uids.skipped_no_conflicts",
                    "All UID refs resolved — importing"));
                RunImportSelectedSceneAtoms(
                    scene, sourceHostUid, selectedIds, null,
                    autoReceiverRemap.Count > 0 ? autoReceiverRemap : null);
                return;
            }

            LogUtil.Log("[VPB][Atoms][import] " + needsAttention.Count + " of " + broken.Count
                + " external UID ref(s) need attention — showing Remap Atom UIDs modal"
                + (importSidebarAlwaysShowRemapPrompt ? " (always-show on)." : "."));
            // Show every ref, not just the unresolvable ones: the resolved rows arrive pre-filled and give
            // the user the context to re-point them, and the header states how many actually need a choice.
            ContinueImportSelectedSceneAtomsAfterRemap(
                scene, sourceHostUid, selectedIds, null, null, broken, null, 1);
        }

        /// <summary>Safety stop for the remap re-scan loop (co-import chains are shallow in practice).</summary>
        private const int RemapAtomUidsMaxPasses = 8;

        /// <summary>
        /// Remap modal loop: Create new co-imports donor atoms into <paramref name="selectedIds"/>;
        /// remaps accumulate. Re-scan after create in case newly added atoms expose more broken refs.
        /// Only refs the user has NOT yet been shown may reopen the modal — see <paramref name="decidedUids"/>.
        /// </summary>
        /// <param name="decidedUids">
        /// Original UIDs already presented in a previous pass. A row the user answered is settled even when
        /// the answer produced no remap entry (destination identical to the source uid, receiver-only row,
        /// or a deliberate skip) — without this the re-scan re-collects it and the modal reopens forever,
        /// which reads to the user as "Import does nothing".
        /// </param>
        /// <param name="pass">1-based modal pass, for the header cue and the loop cap.</param>
        private void ContinueImportSelectedSceneAtomsAfterRemap(
            JSONClass scene,
            string sourceHostUid,
            HashSet<string> selectedIds,
            Dictionary<string, string> remapAcc,
            Dictionary<string, Dictionary<string, string>> receiverRemapAcc,
            List<SceneAtomImporter.BrokenUidRef> broken,
            HashSet<string> decidedUids,
            int pass)
        {
            if (decidedUids == null)
                decidedUids = new HashSet<string>(StringComparer.Ordinal);

            ShowRemapAtomUidsModal(broken, pass, (remap, createNew, receiverRemap) =>
            {
                if (remap != null)
                {
                    if (remapAcc == null)
                        remapAcc = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, string> kv in remap)
                        remapAcc[kv.Key] = kv.Value;
                }

                receiverRemapAcc = MergeReceiverRemapInto(receiverRemapAcc, receiverRemap);

                if (createNew != null)
                {
                    foreach (string id in createNew)
                    {
                        if (!string.IsNullOrEmpty(id))
                            selectedIds.Add(id);
                    }
                }

                // The user just answered every row in this pass. Mark them settled before re-scanning:
                // an answer that yields no map entry (dest == source uid, receiver-only row, or a skip)
                // is still an answer, and re-presenting it is the reopen loop.
                if (broken != null)
                {
                    for (int i = 0; i < broken.Count; i++)
                    {
                        string decided = broken[i].OriginalUid;
                        if (!string.IsNullOrEmpty(decided)) decidedUids.Add(decided);
                    }
                }

                List<SceneAtomImporter.BrokenUidRef> more
                    = SceneAtomImporter.CollectBrokenExternalUidRefs(
                        scene, selectedIds,
                        importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : null);
                if (more != null && more.Count > 0)
                {
                    // Keep only refs never shown — i.e. ones the co-imported atoms just introduced.
                    var remaining = new List<SceneAtomImporter.BrokenUidRef>(more.Count);
                    for (int i = 0; i < more.Count; i++)
                    {
                        SceneAtomImporter.BrokenUidRef row = more[i];
                        if (string.IsNullOrEmpty(row.OriginalUid)) continue;
                        if (decidedUids.Contains(row.OriginalUid)) continue;
                        remaining.Add(row);
                    }
                    more = remaining;
                }

                // Same rule as the first gate: new refs that land on their own never reopen the prompt,
                // but their auto receiver remaps still have to be carried into the import.
                if (more != null && more.Count > 0 && !importSidebarAlwaysShowRemapPrompt
                    && SceneAtomImporter.FilterUidRefsNeedingAttention(more).Count == 0)
                {
                    receiverRemapAcc = MergeReceiverRemapInto(
                        receiverRemapAcc, SceneAtomImporter.BuildAutoReceiverRemapByUid(more));
                    LogUtil.Log("[VPB][Atoms][import] " + more.Count
                        + " new ref(s) from co-imported atoms resolve as-is — no further prompt.");
                    more = null;
                }

                if (more != null && more.Count > 0)
                {
                    if (pass >= RemapAtomUidsMaxPasses)
                    {
                        LogUtil.LogWarning("[VPB][Atoms][import] remap pass cap (" + RemapAtomUidsMaxPasses
                            + ") reached with " + more.Count + " new ref(s) still unshown — importing as-is.");
                        SetStatus(string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.pass_cap",
                                "Remap depth limit — importing with {0} ref(s) left as-is"),
                            more.Count));
                    }
                    else
                    {
                        LogUtil.Log("[VPB][Atoms][import] " + more.Count
                            + " new UID ref(s) from co-imported atoms — remap pass " + (pass + 1) + ".");
                        ContinueImportSelectedSceneAtomsAfterRemap(
                            scene, sourceHostUid, selectedIds, remapAcc, receiverRemapAcc,
                            more, decidedUids, pass + 1);
                        return;
                    }
                }

                SetStatus(string.Format(
                    VPBTranslation.T(
                        "gallery.import.remap_uids.importing",
                        "Importing {0} atom(s)…"),
                    selectedIds.Count));
                RunImportSelectedSceneAtoms(scene, sourceHostUid, selectedIds, remapAcc, receiverRemapAcc);
            });
        }

        /// <summary>
        /// Per-uid receiver-remap union. Returns the accumulator (created on demand) — the remap loop keeps
        /// it in a captured local, which cannot be passed by ref.
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> MergeReceiverRemapInto(
            Dictionary<string, Dictionary<string, string>> acc,
            Dictionary<string, Dictionary<string, string>> add)
        {
            if (add == null || add.Count == 0) return acc;
            if (acc == null)
                acc = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in add)
            {
                if (kv.Value == null || kv.Value.Count == 0) continue;
                Dictionary<string, string> existing;
                if (!acc.TryGetValue(kv.Key, out existing) || existing == null)
                {
                    existing = new Dictionary<string, string>(StringComparer.Ordinal);
                    acc[kv.Key] = existing;
                }
                foreach (KeyValuePair<string, string> rv in kv.Value)
                    existing[rv.Key] = rv.Value;
            }
            return acc;
        }

        private void RunImportSelectedSceneAtoms(
            JSONClass scene,
            string sourceHostUid,
            HashSet<string> selectedIds,
            Dictionary<string, string> uidRemap)
        {
            RunImportSelectedSceneAtoms(scene, sourceHostUid, selectedIds, uidRemap, null);
        }

        private void RunImportSelectedSceneAtoms(
            JSONClass scene,
            string sourceHostUid,
            HashSet<string> selectedIds,
            Dictionary<string, string> uidRemap,
            Dictionary<string, Dictionary<string, string>> receiverRemapByUid)
        {
            if (scene == null || selectedIds == null || selectedIds.Count == 0) return;

            int recvCount = 0;
            if (receiverRemapByUid != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> kv in receiverRemapByUid)
                    if (kv.Value != null) recvCount += kv.Value.Count;
            }

            LogUtil.Log("[VPB][Atoms][import] apply sidebar pick=" + importSidebarPickSceneAtoms
                + " selected=" + selectedIds.Count
                + " skipDup=" + importSidebarSceneAtomSkipDuplicates
                + " relative=" + importSidebarSceneAtomRelativeToPerson
                + " source='" + (importSidebarSourceAtomId ?? "") + "'"
                + " target='" + (importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "(none)") + "'"
                + " hostUid='" + (sourceHostUid ?? "") + "'"
                + " uidRemap=" + (uidRemap != null ? uidRemap.Count.ToString() : "0")
                + " receiverRemap=" + recvCount.ToString());

            StartCoroutine(SceneAtomImporter.ImportSelectedAtoms(
                scene, importSidebarSourceAtomId, importSidebarTargetAtom, sourceHostUid,
                selectedIds, importSidebarSceneAtomRelativeToPerson, importSidebarSceneAtomSkipDuplicates,
                uidRemap, receiverRemapByUid));
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
