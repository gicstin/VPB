using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        // State
        private GameObject importSidebarRoot;
        private bool importSidebarActive;
        // User intent (open/closed), the single source of truth. Actual visibility = intent && in-Scenes,
        // so the open state survives a category round-trip and (persisted to ImportSidebarPrefs) an app restart.
        private bool importSidebarOpenIntent;
        private bool importSidebarOpenIntentLoaded;
        private bool importSidebarBuilt;
        // Which physical side of the gallery the sidebar occupies. Set on toggle ON
        // from leftActiveContent / rightActiveContent so the sidebar replaces whichever
        // Category / Creator / etc. column is currently open. Default right.
        private bool importSidebarOnLeft;
        /// <summary>One-shot side lock when applying GalleryDefault*SidePanel = Import from config.</summary>
        private bool? importSidebarForceOnLeft;

        // Current selection
        private FileEntry importSidebarSourceScene;
        private string importSidebarSourceAtomId;
        private Atom importSidebarTargetAtom;
        private VpbResourceType importSidebarPresetType = VpbResourceType.Appearance;
        // Selected resource types. Apply iterates every type in this set.
        private readonly HashSet<VpbResourceType> importSidebarMultiSelectedTypes = new HashSet<VpbResourceType>();
        // User-toggleable (VR-friendly on-screen toggle): true = chips accumulate (multi-select),
        // false = each click selects only the clicked type. Persisted in ImportSidebarPrefs.
        private bool importSidebarMultiSelectTypes = true;
        private UnityEngine.UI.Image importSidebarMultiToggleBg;
        private UnityEngine.UI.Text importSidebarMultiToggleLabel;

        // Per-type option panels keyed by VpbResourceType. Populated in Task 8 (Options.cs).
        private readonly Dictionary<VpbResourceType, GameObject> importSidebarOptionPanels
            = new Dictionary<VpbResourceType, GameObject>();

        // Option values
        private SubToggleOptions importSidebarSubToggles = SubToggleOptions.AllOn();
        private bool importSidebarMergeClothingOrHair; // false = Replace
        // Shared with the toolbox Suppress-scale button via VPBConfig so toggling either side syncs both.
        private bool importSidebarSuppressScale
        {
            get { return VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange; }
            set
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.SuppressAppearanceScaleChange = value;
                try { VPBConfig.Instance.Save(true, true); } catch { }
                RefreshSuppressScaleBtnVisual();
            }
        }
        private bool importSidebarSuppressClothingLoad;
        private bool importSidebarOnlySuppressRealClothing = true;
        private bool importSidebarOnlyReplaceRealClothing = true;
        private bool importSidebarImportLinkedCUAs;
        // CUAs: when on, the picker restricts the import to the checked subset (else all person-linked CUAs).
        private bool importSidebarPickCUAs;
        // CUAs: off-person (free-standing) props are placed relative to the target person root instead of raw
        // source world coords, so they land in the same spot relative to the person regardless of scene origin.
        private bool importSidebarCUARelativeToPerson = true;
        private bool importSidebarDeleteTargetCUAs;
        // Plugins: when the gate is on, import only the checked subset; selection is per source-atom (the sig
        // tracks scene+atom so switching source resets the checks to "all"), and is not persisted.
        private bool importSidebarPluginsMergeSingle;
        // Plugins: when on, self-referencing atom UIDs in imported plugins (e.g. trigger receiverAtom)
        // are rewritten from the source atom uid to the target atom uid. Opt-in (defaults OFF).
        private bool importSidebarMigratePluginUIDs;
        // Plugins: when off (default) imported plugins MERGE onto the target's existing plugins
        // (renumbered to append past them); when on, the target's plugins are cleared/replaced.
        private bool importSidebarClearExistingPlugins;
        private readonly HashSet<string> importSidebarSelectedPluginKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarPluginSelectionSig;

        // Cached source scene JSON. One-shot, kept until source scene changes.
        private JSONClass importSidebarLoadedSceneJSON;
        private readonly List<string> importSidebarSourcePersonIds = new List<string>(4);

        // Live target list. Refreshed on atom-add / atom-remove.
        private readonly List<Atom> importSidebarTargetCandidates = new List<Atom>(8);

        // Public API
        public bool IsImportSidebarActive { get { return importSidebarActive; } }

        public void ToggleImportSidebar()
        {
            importSidebarOpenIntent = !importSidebarOpenIntent;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();
        }

        public void SetImportSidebarActive(bool active)
        {
            if (active)
            {
                // Lock the side at toggle-on time. Config default Import uses importSidebarForceOnLeft;
                // otherwise prefer the side already showing a panel, default right when both or neither are open.
                if (importSidebarForceOnLeft.HasValue)
                {
                    importSidebarOnLeft = importSidebarForceOnLeft.Value;
                    importSidebarForceOnLeft = null;
                }
                else
                    importSidebarOnLeft = leftActiveContent.HasValue && !rightActiveContent.HasValue;

                // Act like a regular side panel: opening Import CLOSES whatever Category / Creator /
                // History column occupied the same physical side, instead of layering on top of it.
                if (importSidebarOnLeft) leftActiveContent = null;
                else rightActiveContent = null;
                SyncActiveContentTypeFromSidePanels();
            }

            if (active && !importSidebarBuilt)
            {
                LoadImportSidebarPrefs();
                BuildImportSidebar();
                importSidebarBuilt = true;
                SubscribeToAtomEvents();
            }

            if (active && cleanupModeActive)
            {
                try { ExitCleanupModeForSidePanelNavigation(); } catch { }
            }

            importSidebarActive = active;
            if (importSidebarRoot != null)
            {
                importSidebarRoot.SetActive(active);
            }

            if (active)
            {
                // Re-anchor in case the side differs from the previous open.
                float s = ChromeScale;
                ApplyImportSidebarBaseRect(s);
                // Re-ensure the scene/atom subscriptions are live (idempotent) in case they were dropped since build.
                SubscribeToAtomEvents();
                RefreshTargetCandidates();
                TryLoadSelectedSceneIntoImportSidebar();
                // The scroll content only lays out reliably once shown; force it now that the body is active.
                RebuildImportSidebarContent();
                StartCoroutine(DiagDumpImportSidebarRects());
                // If no person atoms were found yet (e.g. sidebar restored from prefs before atoms are ready),
                // start a deferred retry so the target list self-corrects without requiring a sidebar reopen.
                if (CountLivePersonAtoms() == 0)
                    StartCoroutine(DeferredTargetRefreshAfterSceneLoad());
            }

            // UpdateLayout reads importSidebarActive + importSidebarOnLeft to hide the
            // matching side's tab column and force the gallery offset, so the sidebar
            // replaces (not overlaps) the Category / Creator slot.
            try { UpdateLayout(); }
            catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] UpdateLayout failed: " + ex.Message); }

            try { RefreshImportSidebarWizardHeader(); } catch { }
            UpdateImportToggleBtnVisual();
        }

        public void OpenImportSidebarWith(FileEntry sourceFile, Atom targetAtom)
        {
            importSidebarOpenIntent = true;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();

            if (sourceFile != null)
            {
                LoadSourceScene(sourceFile);
            }
            if (targetAtom != null)
            {
                importSidebarTargetAtom = targetAtom;
                RefreshTargetSelectionVisual();
                RefreshApplyButtonEnabled();
            }
        }

        // The Scene Import sidebar only makes sense in the Scenes category (its source is a scene's Person atoms).
        private bool ImportSidebarCategoryAllowed()
        {
            return currentCategoryTitle == "Scenes";
        }

        // The ONLY caller of SetImportSidebarActive: reconciles visibility to (intent && in-Scenes) on every
        // intent change and category nav (end of Show), so leaving Scenes hides it and returning restores it.
        internal void RefreshImportSidebarCategoryGate()
        {
            bool allowed = ImportSidebarCategoryAllowed();
            bool shouldBeActive = allowed && importSidebarOpenIntent;
            if (shouldBeActive != importSidebarActive)
                SetImportSidebarActive(shouldBeActive);
            try { SyncImportSidebarHeaderGateVisual(); } catch { }
            try { UpdateImportToggleBtnVisual(); } catch { }
        }

        /// <summary>Primary pane only: restore persisted open flag once at init (not per Show, not clones/extra panes).</summary>
        private void TryRestoreImportSidebarOpenFromGlobalPref(bool allowRestore)
        {
            if (!allowRestore || importSidebarOpenIntent || importSidebarOpenIntentLoaded) return;
            // An explicitly configured default side panel (e.g. Category) wins over the transient
            // last-session open state: don't auto-reopen the import sidebar on launch just because it
            // happened to be open when the app last closed. (Config default = Import is already handled
            // by ApplySidePanelDefaultsFromConfig, which would have set importSidebarOpenIntent above.)
            if (ConfigSidePanelDefaultSuppressesImportRestore())
            {
                importSidebarOpenIntentLoaded = true;
                return;
            }
            JSONClass pp = VPBConfig.Instance != null ? VPBConfig.Instance.ImportSidebarPrefs : null;
            importSidebarOpenIntent = PrefBool(pp, "open", false);
            importSidebarOpenIntentLoaded = true;
            if (importSidebarOpenIntent)
            {
                try { RefreshImportSidebarCategoryGate(); } catch { }
            }
        }

        // True when the user has configured an explicit default side panel that is neither None nor
        // Import. Such a choice should take precedence over the persisted last-session import-open flag.
        private static bool ConfigSidePanelDefaultSuppressesImportRestore()
        {
            if (VPBConfig.Instance == null) return false;
            return IsExplicitNonImportSidePanel(VPBConfig.Instance.GalleryDefaultLeftSidePanel)
                || IsExplicitNonImportSidePanel(VPBConfig.Instance.GalleryDefaultRightSidePanel);
        }

        private static bool IsExplicitNonImportSidePanel(string raw)
        {
            string v = VPBConfig.NormalizeGallerySidePanel(raw);
            return !string.Equals(v, "None", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(v, "Import", StringComparison.OrdinalIgnoreCase);
        }

        internal void CopyImportSidebarStateFrom(GalleryPanel source)
        {
            if (source == null) return;
            importSidebarOpenIntent = source.importSidebarOpenIntent;
            importSidebarOnLeft = source.importSidebarOnLeft;
            importSidebarOpenIntentLoaded = true;
            if (importSidebarOpenIntent)
            {
                importSidebarForceOnLeft = importSidebarOnLeft;
                try { RefreshImportSidebarCategoryGate(); } catch { }
            }
            else if (importSidebarActive)
            {
                try { RefreshImportSidebarCategoryGate(); } catch { }
            }
            try { UpdateImportToggleBtnVisual(); } catch { }
        }

        // Reflect the scene already highlighted in the grid into the source list on open, so the user doesn't
        // have to re-click it. selectedFiles holds the single-click selection (set even while the sidebar is closed).
        private void TryLoadSelectedSceneIntoImportSidebar()
        {
            if (!ImportSidebarCategoryAllowed()) return;
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            if (ImportSidebarMultiSelectBlocked()) return;
            FileEntry sel = selectedFiles[selectedFiles.Count - 1];
            if (sel == null || importSidebarSourceScene == sel) return;
            LoadSourceScene(sel);
        }

        // Persist sidebar toggle state across sessions via VPBConfig.ImportSidebarPrefs (one nested JSON blob).
        // suppress-scale is NOT here: it shares VPBConfig.SuppressAppearanceScaleChange with the toolbox button.
        private void LoadImportSidebarPrefs()
        {
            JSONClass p = VPBConfig.Instance != null ? VPBConfig.Instance.ImportSidebarPrefs : null;
            if (p == null) return;
            importSidebarSuppressClothingLoad    = PrefBool(p, "suppressClothing", importSidebarSuppressClothingLoad);
            importSidebarOnlySuppressRealClothing = PrefBool(p, "onlySuppressReal", importSidebarOnlySuppressRealClothing);
            importSidebarMergeClothingOrHair      = PrefBool(p, "mergeClothingOrHair", importSidebarMergeClothingOrHair);
            importSidebarOnlyReplaceRealClothing  = PrefBool(p, "onlyReplaceReal", importSidebarOnlyReplaceRealClothing);
            importSidebarImportLinkedCUAs         = PrefBool(p, "importLinkedCUAs", importSidebarImportLinkedCUAs);
            importSidebarPickCUAs                 = PrefBool(p, "pickCUAs", importSidebarPickCUAs);
            importSidebarCUARelativeToPerson      = PrefBool(p, "cuaRelativeToPerson", importSidebarCUARelativeToPerson);
            importSidebarDeleteTargetCUAs         = PrefBool(p, "deleteTargetCUAs", importSidebarDeleteTargetCUAs);
            importSidebarPluginsMergeSingle       = PrefBool(p, "pluginsMergeSingle", importSidebarPluginsMergeSingle);
            importSidebarMigratePluginUIDs        = PrefBool(p, "migratePluginUIDs", importSidebarMigratePluginUIDs);
            importSidebarClearExistingPlugins     = PrefBool(p, "clearExistingPlugins", importSidebarClearExistingPlugins);
            importSidebarMultiSelectTypes         = PrefBool(p, "multiSelectTypes", importSidebarMultiSelectTypes);
            // Open intent is per-pane; global "open" is restored only on the primary pane at init.
            importSidebarSubToggles.IncludeAppearanceMorphs   = PrefBool(p, "incAppearanceMorphs", importSidebarSubToggles.IncludeAppearanceMorphs);
            importSidebarSubToggles.IncludePhysicalPoseMorphs = PrefBool(p, "incPhysicalPoseMorphs", importSidebarSubToggles.IncludePhysicalPoseMorphs);
            importSidebarSubToggles.SuppressMorphLoad         = PrefBool(p, "suppressMorphLoad", importSidebarSubToggles.SuppressMorphLoad);
            importSidebarSubToggles.SuppressRootNodeLoad      = PrefBool(p, "suppressRootNodeLoad", importSidebarSubToggles.SuppressRootNodeLoad);
            importSidebarSubToggles.IncludePhysical           = PrefBool(p, "incPhysical", importSidebarSubToggles.IncludePhysical);
            importSidebarSubToggles.IncludePose               = PrefBool(p, "incPose", importSidebarSubToggles.IncludePose);
            importSidebarSubToggles.IncludeAppearance         = PrefBool(p, "incAppearance", importSidebarSubToggles.IncludeAppearance);
            importSidebarSubToggles.IncludeMocap              = PrefBool(p, "incMocap", importSidebarSubToggles.IncludeMocap);
            if (p.HasKey("presetType"))
            {
                try { importSidebarPresetType = (VpbResourceType)p["presetType"].AsInt; }
                catch { }
            }
        }

        private void SaveImportSidebarPrefs()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (cfg.ImportSidebarPrefs == null) cfg.ImportSidebarPrefs = new JSONClass();
            JSONClass p = cfg.ImportSidebarPrefs;
            p["suppressClothing"].AsBool = importSidebarSuppressClothingLoad;
            p["onlySuppressReal"].AsBool = importSidebarOnlySuppressRealClothing;
            p["mergeClothingOrHair"].AsBool = importSidebarMergeClothingOrHair;
            p["onlyReplaceReal"].AsBool = importSidebarOnlyReplaceRealClothing;
            p["importLinkedCUAs"].AsBool = importSidebarImportLinkedCUAs;
            p["pickCUAs"].AsBool = importSidebarPickCUAs;
            p["cuaRelativeToPerson"].AsBool = importSidebarCUARelativeToPerson;
            p["deleteTargetCUAs"].AsBool = importSidebarDeleteTargetCUAs;
            p["pluginsMergeSingle"].AsBool = importSidebarPluginsMergeSingle;
            p["migratePluginUIDs"].AsBool = importSidebarMigratePluginUIDs;
            p["clearExistingPlugins"].AsBool = importSidebarClearExistingPlugins;
            p["multiSelectTypes"].AsBool = importSidebarMultiSelectTypes;
            p["open"].AsBool = importSidebarOpenIntent;
            p["incAppearanceMorphs"].AsBool = importSidebarSubToggles.IncludeAppearanceMorphs;
            p["incPhysicalPoseMorphs"].AsBool = importSidebarSubToggles.IncludePhysicalPoseMorphs;
            p["suppressMorphLoad"].AsBool = importSidebarSubToggles.SuppressMorphLoad;
            p["suppressRootNodeLoad"].AsBool = importSidebarSubToggles.SuppressRootNodeLoad;
            p["incPhysical"].AsBool = importSidebarSubToggles.IncludePhysical;
            p["incPose"].AsBool = importSidebarSubToggles.IncludePose;
            p["incAppearance"].AsBool = importSidebarSubToggles.IncludeAppearance;
            p["incMocap"].AsBool = importSidebarSubToggles.IncludeMocap;
            p["presetType"].AsInt = (int)importSidebarPresetType;
            try { cfg.Save(false); } catch { }
        }

        private static bool PrefBool(JSONClass p, string key, bool dflt)
        {
            return (p != null && p.HasKey(key)) ? p[key].AsBool : dflt;
        }

        // Write just the open flag (the four intent-mutating sites can fire before the sidebar is built, so
        // they must not go through SaveImportSidebarPrefs, which would persist not-yet-loaded toggle defaults).
        private void PersistImportSidebarOpenIntent()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (cfg.ImportSidebarPrefs == null) cfg.ImportSidebarPrefs = new JSONClass();
            cfg.ImportSidebarPrefs["open"].AsBool = importSidebarOpenIntent;
            try { cfg.Save(false); } catch { }
        }

        // Partial methods: implementations live in other ImportSidebar.*.cs files
        partial void BuildImportSidebar();
        partial void SubscribeToAtomEvents();
        partial void RefreshTargetCandidates();
        partial void RefreshTargetSelectionVisual();
        partial void LoadSourceScene(FileEntry entry);
        partial void RefreshApplyButtonEnabled();
        partial void UpdateImportToggleBtnVisual();
    }
}
