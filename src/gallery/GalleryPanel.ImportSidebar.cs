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
        // Which physical side of the gallery the sidebar occupies. Locked on toggle ON
        // (force / rail / config, else heuristic). Persisted as ImportSidebarPrefs.onLeft with open.
        // Default right when neither force nor persisted side is available.
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
        // Default single-select — multi is opt-in (less option-panel stack for novices).
        private bool importSidebarMultiSelectTypes = false;
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
                // Disk only — do not ConfigChanged (side-rail gap recompute).
                try { VPBConfig.Instance.Save(false); } catch { }
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
        // CUAs: merge on = append (keep prior VPB imports); off = replace them before import.
        private bool importSidebarCuaMergeLoad;
        private bool importSidebarDeleteTargetCUAs;
        // Scene atoms: when on, the picker restricts import to the checked subset; off imports every non-Person atom.
        // Defaults OFF to match the other pickers (Plugins/CUAs): the picker is opt-in and, once on, starts empty.
        private bool importSidebarPickSceneAtoms;
        // Scene atoms: skip import when an atom with the same source uid (or uid#N variant) is already in the scene.
        private bool importSidebarSceneAtomSkipDuplicates = true;
        // Scene atoms: off-scene props placed relative to the target person root instead of raw source world coords.
        private bool importSidebarSceneAtomRelativeToPerson = true;
        // Scene atoms: expert opt-out — when true, Remap Atom UIDs opens only if broken external refs exist.
        // Default false = always show remap gate (predictable import path).
        private bool importSidebarRemapUidsOnlyWhenConflicts;
        private string importSidebarSceneAtomSearchFilter = string.Empty;
        // Plugins: when the gate is on, import only the checked subset; selection is per source-atom (the sig
        // tracks scene+atom so switching source resets the checks to none), and is not persisted.
        private bool importSidebarPickPlugins;
        // Plugins: when on, self-referencing atom UIDs in imported plugins (e.g. trigger receiverAtom)
        // are rewritten from the source atom uid to the target atom uid. Opt-in (defaults OFF).
        private bool importSidebarMigratePluginUIDs;
        // Plugins: when off (default) imported plugins MERGE onto the target's existing plugins
        // (renumbered to append past them); when on, the target's plugins are cleared/replaced.
        private bool importSidebarClearExistingPlugins;
        private readonly HashSet<string> importSidebarSelectedPluginKeys = new HashSet<string>(StringComparer.Ordinal);
        private string importSidebarPluginSelectionSig;

        // Cached source scene JSON. Filled async after open (ThreadPool parse); kept until source changes.
        private JSONClass importSidebarLoadedSceneJSON;
        private readonly List<string> importSidebarSourcePersonIds = new List<string>(4);
        /// <summary>Bumps on source change / cancel so stale background parses are dropped.</summary>
        private int importSidebarSceneJsonLoadGen;
        private Coroutine importSidebarSceneJsonLoadCo;
        private bool importSidebarSceneJsonLoading;
        /// <summary>True while person ids still pending (cache-miss async parse).</summary>
        private bool importSidebarSourcePersonsPending;

        // Live target list. Refreshed on atom-add / atom-remove.
        private readonly List<Atom> importSidebarTargetCandidates = new List<Atom>(8);

        // Public API
        public bool IsImportSidebarActive { get { return importSidebarActive; } }

        public void ToggleImportSidebar()
        {
            // Outside Scenes: navigate back so docked Import can reopen (intent may already be on).
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

        /// <summary>Switch gallery browse to Scenes so Import source selection works.</summary>
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

            // Floating Import is modeless — skip sticky Try-On gate / exclusivity.
            // Docked Import still owns sticky enter (side column rewrites grid click).
            // Prefer post-build detach flag (config may mark float before first activate).
            bool dockedStickyEnter = active && !wasActive && !importSidebarDetached;
            if (dockedStickyEnter)
            {
                // Gate before mutating side panels / cleanup.
                if (!GateStickyEnterWhileTryOn(StickyToolMode.Import))
                    return;
            }

            if (active)
            {
                // Lock the side at toggle-on time. Config default Import uses importSidebarForceOnLeft;
                // otherwise prefer the side already showing a panel, default right when both or neither are open.
                // Floating Import does not steal a side column — skip closing Category/Creator panes.
                if (!importSidebarDetached)
                {
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

            // Header category chip stays visible; sync before layout so title-bar pin order is correct.
            try { SyncCategoryQuickSwitchChrome(); } catch { }

            // UpdateLayout reads ImportSidebarOccupiesSideColumn + importSidebarOnLeft to hide the
            // matching side's tab column and force the gallery offset, so the docked sidebar
            // replaces (not overlaps) the Category / Creator slot. Float leaves grid full-width.
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

        /// <summary>
        /// Open Scene Import focused on a resource type (e.g. Atoms from floating menu).
        /// </summary>
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

        // Reconciles visibility on intent change and category nav.
        // Docked: intent && Scenes. Float: intent alone (survives leaving Scenes; source edits stay locked).
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
                // Side was never persisted historically; missing key keeps prior default-right heuristic
                // inside SetImportSidebarActive. When present, force the last docked side (left/right).
                if (pp != null && pp.HasKey("onLeft"))
                    importSidebarForceOnLeft = pp["onLeft"].AsBool;
                try { RefreshImportSidebarCategoryGate(); } catch { }
                // Backfill onLeft after side lock (migration + keep prefs aligned with live dock).
                try { PersistImportSidebarOpenIntent(); } catch { }
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

        // Reflect gallery selection into import source. Used on open + any selection change while open
        // (click, keyboard, preview scrub commit). Cheap no-op when inactive / multi-select / same entry.
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
            importSidebarPickSceneAtoms           = PrefBool(p, "pickSceneAtoms", importSidebarPickSceneAtoms);
            importSidebarSceneAtomSkipDuplicates  = PrefBool(p, "sceneAtomSkipDuplicates", importSidebarSceneAtomSkipDuplicates);
            importSidebarRemapUidsOnlyWhenConflicts = PrefBool(p, "remapUidsOnlyWhenConflicts", importSidebarRemapUidsOnlyWhenConflicts);
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
            p["remapUidsOnlyWhenConflicts"].AsBool = importSidebarRemapUidsOnlyWhenConflicts;
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

        // Write open + dock side only (intent-mutating sites can fire before the sidebar is built, so
        // they must not go through SaveImportSidebarPrefs, which would persist not-yet-loaded toggle defaults).
        // Call after RefreshImportSidebarCategoryGate so importSidebarOnLeft already reflects the locked side.
        private void PersistImportSidebarOpenIntent()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (cfg.ImportSidebarPrefs == null) cfg.ImportSidebarPrefs = new JSONClass();
            cfg.ImportSidebarPrefs["open"].AsBool = importSidebarOpenIntent;
            cfg.ImportSidebarPrefs["onLeft"].AsBool = importSidebarOnLeft;
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

        internal void DumpSelectedGalleryItemAtoms(string tag)
        {
            VPB.src.util.GalleryAtomListingDiagnostics.DumpGallerySelection(
                selectedFiles, currentCategoryTitle, tag);
        }
    }
}
