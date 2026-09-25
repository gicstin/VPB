using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject importSidebarRoot;
        private bool importSidebarActive;
        private bool importSidebarOpenIntent;
        private bool importSidebarOpenIntentLoaded;
        private bool importSidebarBuilt;
        private bool importSidebarOnLeft;
        /// <summary>One-shot side lock when applying GalleryDefault*SidePanel = Import from config.</summary>
        private bool? importSidebarForceOnLeft;

        private FileEntry importSidebarSourceScene;
        private string importSidebarSourceAtomId;
        private Atom importSidebarTargetAtom;
        private VpbResourceType importSidebarPresetType = VpbResourceType.Appearance;
        private readonly HashSet<VpbResourceType> importSidebarMultiSelectedTypes = new HashSet<VpbResourceType>();
        private bool importSidebarMultiSelectTypes = false;
        private UnityEngine.UI.Image importSidebarMultiToggleBg;
        private UnityEngine.UI.Text importSidebarMultiToggleLabel;

        private readonly Dictionary<VpbResourceType, GameObject> importSidebarOptionPanels
            = new Dictionary<VpbResourceType, GameObject>();

        private SubToggleOptions importSidebarSubToggles = SubToggleOptions.AllOn();
        private bool importSidebarMergeClothingOrHair;
        private bool importSidebarSuppressScale
        {
            get { return VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange; }
            set
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.SuppressAppearanceScaleChange = value;
                // Disk only — do not ConfigChanged (side-rail gap recompute).
                try { VPBConfig.Instance.Save(false); } catch { }
                RefreshSuppressScaleBtnVisual();
            }
        }
        private bool importSidebarSuppressClothingLoad;
        private bool importSidebarOnlySuppressRealClothing = true;
        private bool importSidebarOnlyReplaceRealClothing = true;
        private bool importSidebarImportLinkedCUAs;
        private bool importSidebarPickCUAs;
        private bool importSidebarCUARelativeToPerson = true;
        // CUAs: merge on = append (keep prior VPB imports); off = replace them before import.
        private bool importSidebarCuaMergeLoad;
        private bool importSidebarDeleteTargetCUAs;
        private bool importSidebarPickSceneAtoms;
        private bool importSidebarSceneAtomSkipDuplicates = true;
        private bool importSidebarSceneAtomRelativeToPerson = true;
        private bool importSidebarAlwaysShowRemapPrompt;
        private string importSidebarSceneAtomSearchFilter = string.Empty;
        // Plugins: when the gate is on, import only the checked subset.
        private bool importSidebarPickPlugins;
        private bool importSidebarMigratePluginUIDs;
        private bool importSidebarClearExistingPlugins;
        private readonly HashSet<string> importSidebarSelectedPluginKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarPluginSelectionSig;

        // Cached source scene JSON. Filled async after open (ThreadPool parse); kept until source changes.
        private JSONClass importSidebarLoadedSceneJSON;
        private readonly List<string> importSidebarSourcePersonIds = new List<string>(4);
        private readonly List<int> importSidebarSourceGenders = new List<int>(4);
        /// <summary>Bumps on source change / cancel so stale background parses are dropped.</summary>
        private int importSidebarSceneJsonLoadGen;
        private Coroutine importSidebarSceneJsonLoadCo;
        private bool importSidebarSceneJsonLoading;
        private bool importSidebarSourcePersonsPending;

        private readonly List<Atom> importSidebarTargetCandidates = new List<Atom>(8);

        public bool IsImportSidebarActive { get { return importSidebarActive; } }

        public void ToggleImportSidebar()
        {
            if (!ImportSidebarCategoryAllowed())
            {
                if (!TryNavigateGalleryToScenes())
                {
                    try
                    {
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.import.sidebar_gated_tip",
                            "Import sidebar opens in Scenes category only"), 2f);
                    }
                    catch { }
                    return;
                }
                if (importSidebarOpenIntent)
                {
                    RefreshImportSidebarCategoryGate();
                    return;
                }
            }

            importSidebarOpenIntent = !importSidebarOpenIntent;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();
        }

        private bool TryNavigateGalleryToScenes()
        {
            if (ImportSidebarCategoryAllowed()) return true;
            if (categories == null) return false;
            for (int i = 0; i < categories.Count; i++)
            {
                Gallery.Category c = categories[i];
                if (!string.Equals(c.name, "Scenes", StringComparison.OrdinalIgnoreCase)) continue;
                try { Show(c.name, c.extension, c.path); } catch { return false; }
                return true;
            }
            return false;
        }

        public void SetImportSidebarActive(bool active)
        {
            bool wasActive = importSidebarActive;

            if (active && !importSidebarBuilt)
            {
                LoadImportSidebarPrefs();
                BuildImportSidebar();
                importSidebarBuilt = true;
                SubscribeToAtomEvents();
            }

            bool dockedStickyEnter = active && !wasActive && !importSidebarDetached;
            if (dockedStickyEnter)
            {
                // Gate before mutating side panels / cleanup.
                if (!GateStickyEnterWhileTryOn(StickyToolMode.Import))
                    return;
            }

            if (active)
            {
                // Lock the side at toggle-on time.
                if (!importSidebarDetached)
                {
                    if (importSidebarForceOnLeft.HasValue)
                    {
                        importSidebarOnLeft = importSidebarForceOnLeft.Value;
                        importSidebarForceOnLeft = null;
                    }
                    else
                        importSidebarOnLeft = leftActiveContent.HasValue && !rightActiveContent.HasValue;

                    if (importSidebarOnLeft) leftActiveContent = null;
                    else rightActiveContent = null;
                    SyncActiveContentTypeFromSidePanels();
                }
                else if (importSidebarForceOnLeft.HasValue)
                {
                    // Prefer left/right remembered for later dock; do not clear live side panes.
                    importSidebarOnLeft = importSidebarForceOnLeft.Value;
                    importSidebarForceOnLeft = null;
                }
            }

            if (active && cleanupModeActive && !importSidebarDetached)
            {
                try { ExitCleanupModeForSidePanelNavigation(); } catch { }
            }

            if (dockedStickyEnter)
            {
                try { ExitOtherStickyToolModes(StickyToolMode.Import); } catch { }
            }

            importSidebarActive = active;
            if (importSidebarRoot != null)
            {
                importSidebarRoot.SetActive(active);
            }

            if (active)
            {
                // Dock vs float host (reparent + chrome) before layout/grid inset.
                try { SyncImportSidebarHostChromeAfterActivate(); } catch { }
                float s = ChromeScale;
                ApplyImportSidebarBaseRect(s);
                // Re-ensure the scene/atom subscriptions are live (idempotent) in case they were dropped since build.
                SubscribeToAtomEvents();
                RefreshTargetCandidates();
                TryLoadSelectedSceneIntoImportSidebar();
                // The scroll content only lays out reliably once shown; force it now that the body is active.
                RebuildImportSidebarContent();
                StartCoroutine(DiagDumpImportSidebarRects());
                // If no person atoms were found yet (e.g. sidebar restored from prefs before atoms are ready).
                if (CountLivePersonAtoms() == 0)
                    StartCoroutine(DeferredTargetRefreshAfterSceneLoad());
            }

            // Header category chip stays visible; sync before layout so title-bar pin order is correct.
            try { SyncCategoryQuickSwitchChrome(); } catch { }

            try { UpdateLayout(); }
            catch (System.Exception ex) { LogUtil.LogWarning("[VPB import] UpdateLayout failed: " + ex.Message); }

            try { RefreshImportSidebarWizardHeader(); } catch { }
            UpdateImportToggleBtnVisual();
            try { RefreshModeAmbientChrome(); } catch { }
            try
            {
                ShowTemporaryStatus(
                    active
                        ? VPBTranslation.T("gallery.import.opened", "Import sidebar open.")
                        : VPBTranslation.T("gallery.import.closed", "Import sidebar closed."),
                    1.25f);
            }
            catch { }
            if (!active)
            {
                try { ResetArmedApplySemanticsIfIdle(toast: true); } catch { }
            }
        }

        public void OpenImportSidebarWith(FileEntry sourceFile, Atom targetAtom)
        {
            OpenImportSidebarWithCore(sourceFile, targetAtom, VpbResourceType.Appearance, false);
        }

        internal void OpenImportSidebarWith(FileEntry sourceFile, Atom targetAtom, VpbResourceType preferredType)
        {
            OpenImportSidebarWithCore(sourceFile, targetAtom, preferredType, true);
        }

        private void OpenImportSidebarWithCore(
            FileEntry sourceFile,
            Atom targetAtom,
            VpbResourceType preferredType,
            bool applyPreferredType)
        {
            importSidebarOpenIntent = true;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();

            if (applyPreferredType)
            {
                importSidebarPresetType = preferredType;
                importSidebarMultiSelectedTypes.Clear();
                importSidebarMultiSelectedTypes.Add(preferredType);
            }

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

            if (applyPreferredType && importSidebarBuilt && importSidebarActive)
            {
                try { OnImportSidebarTypeChosen(preferredType, false); }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB import] preferred type apply failed: " + ex.Message);
                }
            }
        }

        // The Scene Import sidebar only makes sense in the Scenes category (its source is a scene's Person atoms).
        private bool ImportSidebarCategoryAllowed()
        {
            return currentCategoryTitle == "Scenes";
        }

        /// <summary>Float outside Scenes: keep panel, freeze source scene/person picks.</summary>
        private bool ImportSidebarSourceEditsLocked()
        {
            return importSidebarDetached && !ImportSidebarCategoryAllowed();
        }

        internal void RefreshImportSidebarCategoryGate()
        {
            bool allowed = ImportSidebarCategoryAllowed();
            bool shouldBeActive = importSidebarOpenIntent && (allowed || importSidebarDetached);
            if (shouldBeActive != importSidebarActive)
                SetImportSidebarActive(shouldBeActive);
            try { SyncImportSidebarHeaderGateVisual(); } catch { }
            try { UpdateImportToggleBtnVisual(); } catch { }
            try { RefreshImportSidebarWizardHeader(); } catch { }
            try { RefreshSceneImportSideButtonVisibility(); } catch { }
        }

        /// <summary>Primary pane only: restore persisted open flag + dock side once at init (not per Show, not clones/extra panes).</summary>
        private void TryRestoreImportSidebarOpenFromGlobalPref(bool allowRestore)
        {
            if (!allowRestore || importSidebarOpenIntent || importSidebarOpenIntentLoaded) return;
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
                // Side was never persisted historically; missing key keeps prior default-right heuristic inside SetImportSidebarActive.
                if (pp != null && pp.HasKey("onLeft"))
                    importSidebarForceOnLeft = pp["onLeft"].AsBool;
                try { RefreshImportSidebarCategoryGate(); } catch { }
                // Backfill onLeft after side lock (migration + keep prefs aligned with live dock).
                try { PersistImportSidebarOpenIntent(); } catch { }
            }
        }

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
            importSidebarDetached = source.importSidebarDetached;
            importSidebarFloatCollapsed = source.importSidebarFloatCollapsed;
            importSidebarExpandHeightRef = source.importSidebarExpandHeightRef;
            importSidebarSavedFloatPosCenter = source.importSidebarSavedFloatPosCenter;
            importSidebarSavedFloatSizeRef = source.importSidebarSavedFloatSizeRef;
            importSidebarCollapsedTopLeftPos = source.importSidebarCollapsedTopLeftPos;
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

        private void TryLoadSelectedSceneIntoImportSidebar()
        {
            if (!importSidebarActive) return;
            if (!ImportSidebarCategoryAllowed()) return;
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            if (ImportSidebarMultiSelectBlocked()) return;
            FileEntry sel = selectedFiles[selectedFiles.Count - 1];
            if (sel == null || importSidebarSourceScene == sel) return;
            LoadSourceScene(sel);
        }

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
            importSidebarPickSceneAtoms           = PrefBool(p, "pickSceneAtoms", importSidebarPickSceneAtoms);
            importSidebarSceneAtomSkipDuplicates  = PrefBool(p, "sceneAtomSkipDuplicates", importSidebarSceneAtomSkipDuplicates);
            // New key: the old "remapUidsOnlyWhenConflicts" meant the inverse, so reading it would flip the intent of anyone who had set it.
            importSidebarAlwaysShowRemapPrompt    = PrefBool(p, "alwaysShowRemapPrompt", importSidebarAlwaysShowRemapPrompt);
            importSidebarCUARelativeToPerson      = PrefBool(p, "cuaRelativeToPerson", importSidebarCUARelativeToPerson);
            importSidebarCuaMergeLoad             = PrefBool(p, "cuaMergeLoad", importSidebarCuaMergeLoad);
            importSidebarDeleteTargetCUAs         = PrefBool(p, "deleteTargetCUAs", importSidebarDeleteTargetCUAs);
            importSidebarPickPlugins              = PrefBool(p, "pluginsMergeSingle", importSidebarPickPlugins);
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
            if (p.HasKey("wizardCollapsedMask"))
            {
                try { _importWizardCollapsedMask = p["wizardCollapsedMask"].AsInt; }
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
            p["pickSceneAtoms"].AsBool = importSidebarPickSceneAtoms;
            p["sceneAtomSkipDuplicates"].AsBool = importSidebarSceneAtomSkipDuplicates;
            p["alwaysShowRemapPrompt"].AsBool = importSidebarAlwaysShowRemapPrompt;
            p["cuaRelativeToPerson"].AsBool = importSidebarCUARelativeToPerson;
            p["cuaMergeLoad"].AsBool = importSidebarCuaMergeLoad;
            p["deleteTargetCUAs"].AsBool = importSidebarDeleteTargetCUAs;
            p["pluginsMergeSingle"].AsBool = importSidebarPickPlugins;
            p["migratePluginUIDs"].AsBool = importSidebarMigratePluginUIDs;
            p["clearExistingPlugins"].AsBool = importSidebarClearExistingPlugins;
            p["multiSelectTypes"].AsBool = importSidebarMultiSelectTypes;
            p["wizardCollapsedMask"].AsInt = _importWizardCollapsedMask;
            p["open"].AsBool = importSidebarOpenIntent;
            p["onLeft"].AsBool = importSidebarOnLeft;
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

        // Write open + dock side only; do not persist not-yet-loaded toggle defaults.
        private void PersistImportSidebarOpenIntent()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (cfg.ImportSidebarPrefs == null) cfg.ImportSidebarPrefs = new JSONClass();
            cfg.ImportSidebarPrefs["open"].AsBool = importSidebarOpenIntent;
            cfg.ImportSidebarPrefs["onLeft"].AsBool = importSidebarOnLeft;
            try { cfg.Save(false); } catch { }
        }

        partial void BuildImportSidebar();
        partial void SubscribeToAtomEvents();
        partial void RefreshTargetCandidates();
        partial void RefreshTargetSelectionVisual();
        partial void LoadSourceScene(FileEntry entry);
        partial void RefreshApplyButtonEnabled();
        partial void UpdateImportToggleBtnVisual();

        internal void DumpSelectedGalleryItemAtoms(string tag)
        {
            VPB.src.util.GalleryAtomListingDiagnostics.DumpGallerySelection(
                selectedFiles, currentCategoryTitle, tag);
        }
    }
}
