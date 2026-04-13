using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public class SettingsPanel
    {
        public GameObject settingsPaneGO;
        private GalleryPanel parentPanel;
        private GameObject backgroundBoxGO;
        private RectTransform settingsPaneRT;
        private GameObject settingsScrollContent;

        private const float SettingsPaneDockOffsetX = 180f;
        
        private bool isSettingsOpen = false;
        private bool settingsOnRight = true;
        
        // Pending settings state
        private bool pendingEnableButtonGaps;
        private bool backupEnableButtonGaps;
        
        private string pendingShowSideButtons;
        private string backupShowSideButtons;

        private string pendingFollowAngle;
        private string backupFollowAngle;

        private string pendingFollowDistance;
        private string backupFollowDistance;

        private string pendingFollowEyeHeight;
        private string backupFollowEyeHeight;

        private float pendingReorientStartAngle;
        private float backupReorientStartAngle;

        private float pendingMovementThreshold;
        private float backupMovementThreshold;

        private float pendingBringToFrontDistance;
        private float backupBringToFrontDistance;

        private bool pendingEnableGalleryFade;
        private bool backupEnableGalleryFade;

        private bool pendingEnableGalleryTranslucency;
        private bool backupEnableGalleryTranslucency;

        private bool pendingGalleryManualRefreshOnly;
        private bool backupGalleryManualRefreshOnly;

        private float pendingGalleryOpacity;
        private float backupGalleryOpacity;

        private float pendingSideButtonScale;
        private float backupSideButtonScale;

        private float pendingInnerPaneScale;
        private float backupInnerPaneScale;

        private bool pendingDragDropReplaceMode;
        private bool backupDragDropReplaceMode;

        private string pendingAppearanceClothingApplyMode;
        private string backupAppearanceClothingApplyMode;

        private bool pendingEnableDragDrop;
        private bool backupEnableDragDrop;

        private bool pendingRequireDragHoldBeforeMove;
        private bool backupRequireDragHoldBeforeMove;

        private float pendingDragHoldThreshold;
        private float backupDragHoldThreshold;
        
        private bool pendingIsDevMode;
        private bool backupIsDevMode;

        private bool pendingEnableAutoFixedGallery;
        private bool backupEnableAutoFixedGallery;

        private string pendingInitialGalleryCategory;
        private string backupInitialGalleryCategory;

        private string pendingGalleryDefaultLeftSidePanel;
        private string backupGalleryDefaultLeftSidePanel;

        private string pendingGalleryDefaultRightSidePanel;
        private string backupGalleryDefaultRightSidePanel;

        private HashSet<string> pendingHiddenCategories;
        private HashSet<string> backupHiddenCategories;

        private bool pendingPluginGalleryGridThumbnails;
        private bool backupPluginGalleryGridThumbnails;

        private bool pendingGalleryListNamesLegacyFileName;
        private bool backupGalleryListNamesLegacyFileName;

        private string pendingGalleryHoverPreviewMode;
        private string backupGalleryHoverPreviewMode;

        private float pendingGalleryListHoverPreviewSize;
        private float backupGalleryListHoverPreviewSize;

        private float pendingGalleryListHoverPreviewOffsetX;
        private float backupGalleryListHoverPreviewOffsetX;

        private float pendingGalleryListHoverPreviewOffsetY;
        private float backupGalleryListHoverPreviewOffsetY;

        private GameObject tooltipGO;
        private Text tooltipText;
        private Text settingsTitleText;
        private Text settingsCancelBtnText;
        private Text settingsSaveBtnText;

        private bool _settingsScrollUiBuilt;
        private bool _settingsScrollUiBuiltForFixed;
        private bool _settingsScrollUiBuiltForDevSection;
        private readonly List<System.Action> _settingsScrollSync = new List<System.Action>();
        /// <summary>Refreshes hold-duration slider/input when drag or "require hold" toggles change.</summary>
        private System.Action _syncDragHoldDelayRowInteractable;

        private sealed class RefBool { public bool Value; }
        private sealed class RefString { public string Value; }

        /// <summary>Set to false to stop writing [VPB][SettingsPerf] lines to the BepInEx log.</summary>
        private static bool LogSettingsOpenCloseTiming = true;

        private static void SettingsPerfLog(string message)
        {
            if (!LogSettingsOpenCloseTiming) return;
            LogUtil.Log("[VPB][SettingsPerf] " + message);
        }

        public SettingsPanel(GalleryPanel parentPanel, GameObject backgroundBoxGO)
        {
            this.parentPanel = parentPanel;
            this.backgroundBoxGO = backgroundBoxGO;
        }

        public void Toggle(bool onRight)
        {
            if (isSettingsOpen && settingsOnRight == onRight)
            {
                Close();
            }
            else
            {
                Open(onRight);
            }
        }

        public void Open(bool onRight)
        {
            bool logOpen = LogSettingsOpenCloseTiming;
            Stopwatch swOpen = new Stopwatch();
            if (logOpen) swOpen.Start();
            long mark = 0;
            if (logOpen) mark = swOpen.ElapsedMilliseconds;

            if (settingsPaneGO == null)
            {
                CreatePane();
                if (logOpen)
                {
                    SettingsPerfLog($"Open.CreatePane {swOpen.ElapsedMilliseconds - mark}ms");
                    mark = swOpen.ElapsedMilliseconds;
                }
            }

            isSettingsOpen = true;
            settingsOnRight = onRight;
            settingsPaneGO.SetActive(true);
            // Local layout only: do not call VPBConfig.TriggerChange() here — it runs GalleryPanel's
            // ConfigChanged handlers (UpdateLayout → ForceRebuildLayoutImmediate on the whole gallery)
            // even though no config value changed yet. Side tab *button lists* are not rebuilt on
            // ConfigChanged (only scroll chrome/layout); full lists refresh via explicit UpdateTabs().
            if (settingsPaneRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPaneRT);
            if (logOpen)
            {
                SettingsPerfLog($"Open.SetActiveAndLocalLayout {swOpen.ElapsedMilliseconds - mark}ms");
                mark = swOpen.ElapsedMilliseconds;
            }

            // Initialize pending settings from current config
            pendingEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            backupEnableButtonGaps = VPBConfig.Instance.EnableButtonGaps;
            
            pendingShowSideButtons = VPBConfig.Instance.ShowSideButtons;
            backupShowSideButtons = VPBConfig.Instance.ShowSideButtons;

            pendingFollowAngle = VPBConfig.Instance.FollowAngle;
            backupFollowAngle = VPBConfig.Instance.FollowAngle;

            pendingFollowDistance = VPBConfig.Instance._followDistance;
            backupFollowDistance = VPBConfig.Instance._followDistance;

            pendingFollowEyeHeight = VPBConfig.Instance._followEyeHeight;
            backupFollowEyeHeight = VPBConfig.Instance._followEyeHeight;

            pendingReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;
            backupReorientStartAngle = VPBConfig.Instance.ReorientStartAngle;

            pendingMovementThreshold = VPBConfig.Instance.MovementThreshold;
            backupMovementThreshold = VPBConfig.Instance.MovementThreshold;

            pendingBringToFrontDistance = VPBConfig.Instance.BringToFrontDistance;
            backupBringToFrontDistance = VPBConfig.Instance.BringToFrontDistance;

            pendingEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;
            backupEnableGalleryFade = VPBConfig.Instance.EnableGalleryFade;

            pendingEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;
            backupEnableGalleryTranslucency = VPBConfig.Instance.EnableGalleryTranslucency;

            pendingGalleryManualRefreshOnly = VPBConfig.Instance.GalleryManualRefreshOnly;
            backupGalleryManualRefreshOnly = VPBConfig.Instance.GalleryManualRefreshOnly;

            pendingGalleryOpacity = VPBConfig.Instance.GalleryOpacity;
            backupGalleryOpacity = VPBConfig.Instance.GalleryOpacity;

            pendingSideButtonScale = VPBConfig.Instance.SideButtonScale;
            backupSideButtonScale = VPBConfig.Instance.SideButtonScale;

            pendingInnerPaneScale = VPBConfig.Instance.InnerPaneScale;
            backupInnerPaneScale = VPBConfig.Instance.InnerPaneScale;

            pendingDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;
            backupDragDropReplaceMode = VPBConfig.Instance.DragDropReplaceMode;

            pendingAppearanceClothingApplyMode = NormalizeSettingsAppearanceClothingMode(VPBConfig.Instance.AppearanceClothingApplyMode);
            backupAppearanceClothingApplyMode = pendingAppearanceClothingApplyMode;

            pendingEnableDragDrop = VPBConfig.Instance.EnableDragDrop;
            backupEnableDragDrop = VPBConfig.Instance.EnableDragDrop;

            pendingRequireDragHoldBeforeMove = VPBConfig.Instance.RequireDragHoldBeforeMove;
            backupRequireDragHoldBeforeMove = VPBConfig.Instance.RequireDragHoldBeforeMove;

            pendingDragHoldThreshold = VPBConfig.Instance.DragHoldThreshold;
            backupDragHoldThreshold = VPBConfig.Instance.DragHoldThreshold;

            pendingIsDevMode = VPBConfig.Instance.IsDevMode;
            backupIsDevMode = VPBConfig.Instance.IsDevMode;

            pendingEnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery;
            backupEnableAutoFixedGallery = VPBConfig.Instance.EnableAutoFixedGallery;

            pendingInitialGalleryCategory = VPBConfig.NormalizeInitialGalleryCategory(VPBConfig.Instance.InitialGalleryCategory);
            backupInitialGalleryCategory = pendingInitialGalleryCategory;

            pendingGalleryDefaultLeftSidePanel = VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultLeftSidePanel);
            backupGalleryDefaultLeftSidePanel = pendingGalleryDefaultLeftSidePanel;
            pendingGalleryDefaultRightSidePanel = VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultRightSidePanel);
            backupGalleryDefaultRightSidePanel = pendingGalleryDefaultRightSidePanel;

            pendingHiddenCategories = new HashSet<string>(VPBConfig.Instance.HiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            backupHiddenCategories  = new HashSet<string>(pendingHiddenCategories, StringComparer.OrdinalIgnoreCase);

            pendingPluginGalleryGridThumbnails = VPBConfig.Instance.PluginGalleryGridThumbnails;
            backupPluginGalleryGridThumbnails = VPBConfig.Instance.PluginGalleryGridThumbnails;

            pendingGalleryListNamesLegacyFileName = VPBConfig.Instance.GalleryListNamesLegacyFileName;
            backupGalleryListNamesLegacyFileName = VPBConfig.Instance.GalleryListNamesLegacyFileName;

            pendingGalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(VPBConfig.Instance.GalleryHoverPreviewMode);
            backupGalleryHoverPreviewMode = pendingGalleryHoverPreviewMode;

            pendingGalleryListHoverPreviewSize = VPBConfig.Instance.GalleryListHoverPreviewSize;
            backupGalleryListHoverPreviewSize = VPBConfig.Instance.GalleryListHoverPreviewSize;

            pendingGalleryListHoverPreviewOffsetX = VPBConfig.Instance.GalleryListHoverPreviewOffsetX;
            backupGalleryListHoverPreviewOffsetX = VPBConfig.Instance.GalleryListHoverPreviewOffsetX;

            pendingGalleryListHoverPreviewOffsetY = VPBConfig.Instance.GalleryListHoverPreviewOffsetY;
            backupGalleryListHoverPreviewOffsetY = VPBConfig.Instance.GalleryListHoverPreviewOffsetY;

            if (logOpen)
            {
                SettingsPerfLog($"Open.copyPendingFromConfig {swOpen.ElapsedMilliseconds - mark}ms");
                mark = swOpen.ElapsedMilliseconds;
            }

            RectTransform rt = settingsPaneRT;
            if (onRight)
            {
                rt.anchorMin = new Vector2(1, 0.5f);
                rt.anchorMax = new Vector2(1, 0.5f);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(SettingsPaneDockOffsetX, 0); 
            }
            else
            {
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(1, 0.5f);
                rt.anchoredPosition = new Vector2(-SettingsPaneDockOffsetX, 0); 
            }

            if (logOpen)
            {
                SettingsPerfLog($"Open.dockLayout {swOpen.ElapsedMilliseconds - mark}ms");
                mark = swOpen.ElapsedMilliseconds;
            }

            bool fixedNow = parentPanel != null && parentPanel.isFixedLocally;
            bool devNow = VPBConfig.Instance.IsDevMode;
            bool needRebuild = !_settingsScrollUiBuilt
                || _settingsScrollUiBuiltForFixed != fixedNow
                || _settingsScrollUiBuiltForDevSection != devNow;
            if (logOpen)
                SettingsPerfLog($"Open.scroll path={(needRebuild ? "rebuild" : "sync")} fixed={fixedNow} devSection={devNow}");

            if (needRebuild)
            {
                RebuildSettingsScrollContent();
                if (logOpen)
                {
                    SettingsPerfLog($"Open.RebuildSettingsScrollContent {swOpen.ElapsedMilliseconds - mark}ms");
                    mark = swOpen.ElapsedMilliseconds;
                }
                _settingsScrollUiBuilt = true;
                _settingsScrollUiBuiltForFixed = fixedNow;
                _settingsScrollUiBuiltForDevSection = devNow;
                ApplySettingsFonts();
                if (logOpen)
                {
                    SettingsPerfLog($"Open.ApplySettingsFonts {swOpen.ElapsedMilliseconds - mark}ms");
                    mark = swOpen.ElapsedMilliseconds;
                }
            }
            else
            {
                SyncSettingsScrollContent();
                if (logOpen)
                {
                    SettingsPerfLog($"Open.SyncSettingsScrollContent(wall, includes inner log) {swOpen.ElapsedMilliseconds - mark}ms");
                    mark = swOpen.ElapsedMilliseconds;
                }
            }

            if (logOpen)
                SettingsPerfLog($"Open.TOTAL {swOpen.ElapsedMilliseconds}ms");
        }

        public void RefreshLocalizedUi()
        {
            if (settingsScrollContent == null) return;
            if (settingsTitleText != null) settingsTitleText.text = VPBTranslation.T("settings.title", "Settings");
            if (settingsCancelBtnText != null) settingsCancelBtnText.text = VPBTranslation.T("settings.cancel", "Cancel");
            if (settingsSaveBtnText != null) settingsSaveBtnText.text = VPBTranslation.T("settings.save", "Save");
            _settingsScrollUiBuilt = false;
            RebuildSettingsScrollContent();
            _settingsScrollUiBuilt = true;
            _settingsScrollUiBuiltForFixed = parentPanel != null && parentPanel.isFixedLocally;
            _settingsScrollUiBuiltForDevSection = VPBConfig.Instance.IsDevMode;
            ApplySettingsFonts();
        }

        private void ApplySettingsFonts()
        {
            if (settingsPaneGO == null) return;
            foreach (Text tx in settingsPaneGO.GetComponentsInChildren<Text>(true))
                VPBUiFont.ApplyTo(tx);
        }

        private static bool HiddenCategorySetsEqual(HashSet<string> a, HashSet<string> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            foreach (var x in a)
            {
                if (!b.Contains(x)) return false;
            }
            return true;
        }

        /// <summary>True when nothing was changed since this settings session opened (live preview already matches backup).</summary>
        private bool PendingMatchesBackup()
        {
            if (pendingEnableButtonGaps != backupEnableButtonGaps) return false;
            if (!string.Equals(pendingShowSideButtons ?? "", backupShowSideButtons ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(pendingFollowAngle ?? "", backupFollowAngle ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(pendingFollowDistance ?? "", backupFollowDistance ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(pendingFollowEyeHeight ?? "", backupFollowEyeHeight ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Mathf.Approximately(pendingReorientStartAngle, backupReorientStartAngle)) return false;
            if (!Mathf.Approximately(pendingMovementThreshold, backupMovementThreshold)) return false;
            if (!Mathf.Approximately(pendingBringToFrontDistance, backupBringToFrontDistance)) return false;
            if (pendingEnableGalleryFade != backupEnableGalleryFade) return false;
            if (pendingEnableGalleryTranslucency != backupEnableGalleryTranslucency) return false;
            if (pendingGalleryManualRefreshOnly != backupGalleryManualRefreshOnly) return false;
            if (!Mathf.Approximately(pendingGalleryOpacity, backupGalleryOpacity)) return false;
            if (!Mathf.Approximately(pendingSideButtonScale, backupSideButtonScale)) return false;
            if (!Mathf.Approximately(pendingInnerPaneScale, backupInnerPaneScale)) return false;
            if (pendingDragDropReplaceMode != backupDragDropReplaceMode) return false;
            if (!string.Equals(NormalizeSettingsAppearanceClothingMode(pendingAppearanceClothingApplyMode), NormalizeSettingsAppearanceClothingMode(backupAppearanceClothingApplyMode), StringComparison.OrdinalIgnoreCase)) return false;
            if (pendingEnableDragDrop != backupEnableDragDrop) return false;
            if (pendingRequireDragHoldBeforeMove != backupRequireDragHoldBeforeMove) return false;
            if (!Mathf.Approximately(pendingDragHoldThreshold, backupDragHoldThreshold)) return false;
            if (pendingIsDevMode != backupIsDevMode) return false;
            if (pendingEnableAutoFixedGallery != backupEnableAutoFixedGallery) return false;
            if (!string.Equals(pendingInitialGalleryCategory ?? "", backupInitialGalleryCategory ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(pendingGalleryDefaultLeftSidePanel ?? "", backupGalleryDefaultLeftSidePanel ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(pendingGalleryDefaultRightSidePanel ?? "", backupGalleryDefaultRightSidePanel ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!HiddenCategorySetsEqual(pendingHiddenCategories, backupHiddenCategories)) return false;
            if (pendingPluginGalleryGridThumbnails != backupPluginGalleryGridThumbnails) return false;
            if (pendingGalleryListNamesLegacyFileName != backupGalleryListNamesLegacyFileName) return false;
            if (!string.Equals(pendingGalleryHoverPreviewMode ?? "", backupGalleryHoverPreviewMode ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Mathf.Approximately(pendingGalleryListHoverPreviewSize, backupGalleryListHoverPreviewSize)) return false;
            if (!Mathf.Approximately(pendingGalleryListHoverPreviewOffsetX, backupGalleryListHoverPreviewOffsetX)) return false;
            if (!Mathf.Approximately(pendingGalleryListHoverPreviewOffsetY, backupGalleryListHoverPreviewOffsetY)) return false;
            return true;
        }

        /// <summary>After persisting settings, run ConfigChanged only if something changed that did not already fire it during live preview.</summary>
        private bool SaveNeedsDeferredConfigNotify()
        {
            if (PendingMatchesBackup()) return false;
            if (pendingIsDevMode != backupIsDevMode) return true;
            if (pendingEnableDragDrop != backupEnableDragDrop) return true;
            if (pendingRequireDragHoldBeforeMove != backupRequireDragHoldBeforeMove) return true;
            if (!Mathf.Approximately(pendingDragHoldThreshold, backupDragHoldThreshold)) return true;
            if (!Mathf.Approximately(pendingBringToFrontDistance, backupBringToFrontDistance)) return true;
            return false;
        }

        public void Close()
        {
            bool logClose = LogSettingsOpenCloseTiming;
            Stopwatch swClose = new Stopwatch();
            if (logClose) swClose.Start();
            long mark = 0;
            if (logClose) mark = swClose.ElapsedMilliseconds;

            isSettingsOpen = false;
            if (settingsPaneGO != null) settingsPaneGO.SetActive(false);
            if (tooltipGO != null) tooltipGO.SetActive(false);

            if (logClose)
            {
                SettingsPerfLog($"Close.SetActiveOff {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
            }

            if (PendingMatchesBackup())
            {
                if (logClose)
                    SettingsPerfLog($"Close.TOTAL {swClose.ElapsedMilliseconds}ms (no changes; skipped revert & TriggerChange)");
                return;
            }

            // Revert live changes from memory backup
            VPBConfig.Instance.EnableButtonGaps = backupEnableButtonGaps;
            VPBConfig.Instance.ShowSideButtons = backupShowSideButtons;
            VPBConfig.Instance.FollowAngle = backupFollowAngle;
            VPBConfig.Instance._followDistance = backupFollowDistance;
            VPBConfig.Instance.FollowEyeHeight = backupFollowEyeHeight;
            VPBConfig.Instance.ReorientStartAngle = backupReorientStartAngle;
            VPBConfig.Instance.MovementThreshold = backupMovementThreshold;
            VPBConfig.Instance.BringToFrontDistance = backupBringToFrontDistance;
            VPBConfig.Instance.EnableGalleryFade = backupEnableGalleryFade;
            VPBConfig.Instance.EnableGalleryTranslucency = backupEnableGalleryTranslucency;
            VPBConfig.Instance.GalleryManualRefreshOnly = backupGalleryManualRefreshOnly;
            VPBConfig.Instance.GalleryOpacity = backupGalleryOpacity;
            VPBConfig.Instance.SideButtonScale = backupSideButtonScale;
            VPBConfig.Instance.InnerPaneScale = backupInnerPaneScale;
            // ApplySideButtonScale / ApplyInnerPaneScale run from ConfigChanged after TriggerChange below.
            VPBConfig.Instance.DragDropReplaceMode = backupDragDropReplaceMode;
            VPBConfig.Instance.AppearanceClothingApplyMode = backupAppearanceClothingApplyMode;
            VPBConfig.Instance.EnableDragDrop = backupEnableDragDrop;
            VPBConfig.Instance.RequireDragHoldBeforeMove = backupRequireDragHoldBeforeMove;
            VPBConfig.Instance.DragHoldThreshold = backupDragHoldThreshold;
            VPBConfig.Instance.IsDevMode = backupIsDevMode;
            VPBConfig.Instance.EnableAutoFixedGallery = backupEnableAutoFixedGallery;
            VPBConfig.Instance.InitialGalleryCategory = backupInitialGalleryCategory;
            VPBConfig.Instance.GalleryDefaultLeftSidePanel = backupGalleryDefaultLeftSidePanel;
            VPBConfig.Instance.GalleryDefaultRightSidePanel = backupGalleryDefaultRightSidePanel;
            VPBConfig.Instance.HiddenCategories = new HashSet<string>(backupHiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            pendingHiddenCategories = new HashSet<string>(backupHiddenCategories ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            VPBConfig.Instance.PluginGalleryGridThumbnails = backupPluginGalleryGridThumbnails;
            pendingPluginGalleryGridThumbnails = backupPluginGalleryGridThumbnails;
            bool galleryListLegacyWasPending = pendingGalleryListNamesLegacyFileName != backupGalleryListNamesLegacyFileName;
            VPBConfig.Instance.GalleryListNamesLegacyFileName = backupGalleryListNamesLegacyFileName;
            pendingGalleryListNamesLegacyFileName = backupGalleryListNamesLegacyFileName;
            VPBConfig.Instance.GalleryHoverPreviewMode = backupGalleryHoverPreviewMode;
            pendingGalleryHoverPreviewMode = backupGalleryHoverPreviewMode;
            VPBConfig.Instance.GalleryListHoverPreviewSize = backupGalleryListHoverPreviewSize;
            pendingGalleryListHoverPreviewSize = backupGalleryListHoverPreviewSize;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetX = backupGalleryListHoverPreviewOffsetX;
            pendingGalleryListHoverPreviewOffsetX = backupGalleryListHoverPreviewOffsetX;
            VPBConfig.Instance.GalleryListHoverPreviewOffsetY = backupGalleryListHoverPreviewOffsetY;
            pendingGalleryListHoverPreviewOffsetY = backupGalleryListHoverPreviewOffsetY;

            VPBConfig.Instance.GalleryListHoverPreviewSize = backupGalleryListHoverPreviewSize;
            pendingGalleryListHoverPreviewSize = backupGalleryListHoverPreviewSize;

            if (logClose)
            {
                SettingsPerfLog($"Close.revertConfigFields {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
            }

            VPBConfig.Instance.TriggerChange();
            if (logClose)
            {
                SettingsPerfLog($"Close.TriggerChange {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
            }

            if (parentPanel != null && galleryListLegacyWasPending) parentPanel.RefreshFiles(true);
            if (logClose)
            {
                SettingsPerfLog($"Close.RefreshFiles(conditional) {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
            }

            if (parentPanel != null) parentPanel.RefreshAppearanceClothingSideButton();
            if (logClose)
            {
                SettingsPerfLog($"Close.RefreshAppearanceClothingSideButton {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
            }

            if (parentPanel != null) parentPanel.ApplySidePanelDefaultsFromConfig();
            if (logClose)
            {
                SettingsPerfLog($"Close.ApplySidePanelDefaultsFromConfig {swClose.ElapsedMilliseconds - mark}ms");
                mark = swClose.ElapsedMilliseconds;
                SettingsPerfLog($"Close.TOTAL {swClose.ElapsedMilliseconds}ms");
            }
        }

        private void CreatePane()
        {
            settingsPaneGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0.15f, 0.15f, 0.15f, 0.95f), AnchorPresets.middleRight, 500, 750, new Vector2(SettingsPaneDockOffsetX, 0));
            settingsPaneRT = settingsPaneGO.GetComponent<RectTransform>();
            
            // Header
            GameObject header = new GameObject("SettingsHeader");
            header.transform.SetParent(settingsPaneGO.transform, false);
            Text t = header.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            settingsTitleText = t;
            t.text = VPBTranslation.T("settings.title", "Settings");
            t.fontSize = 28;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform hRT = header.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(1, 1);
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.sizeDelta = new Vector2(0, 60);
            hRT.anchoredPosition = new Vector2(0, -10);

            // Scrollable Area
            GameObject scrollable = UI.CreateVScrollableContent(settingsPaneGO, new Color(0, 0, 0, 0.2f), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            RectTransform sRT = scrollable.GetComponent<RectTransform>();
            sRT.offsetMin = new Vector2(10, 80); 
            sRT.offsetMax = new Vector2(-10, -70); 
            
            settingsScrollContent = scrollable.GetComponent<ScrollRect>().content.gameObject;
            VerticalLayoutGroup vlg = settingsScrollContent.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 10;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = settingsScrollContent.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Footer Buttons
            float footerY = 10;
            float btnW = 180;
            float btnH = 50;
            
            GameObject cancelBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, VPBTranslation.T("settings.cancel", "Cancel"), 24, -120, footerY, AnchorPresets.bottomMiddle, Close);
            cancelBtn.GetComponent<Image>().color = new Color(0.6f, 0.25f, 0.25f, 1f);
            settingsCancelBtnText = cancelBtn.GetComponentInChildren<Text>();
            settingsCancelBtnText.color = Color.white;
            
            GameObject saveBtn = UI.CreateUIButton(settingsPaneGO, btnW, btnH, VPBTranslation.T("settings.save", "Save"), 24, 120, footerY, AnchorPresets.bottomMiddle, () => {
                if (!PendingMatchesBackup())
                {
                    VPBConfig.Instance.EnableButtonGaps = pendingEnableButtonGaps;
                    VPBConfig.Instance.ShowSideButtons = pendingShowSideButtons;
                    VPBConfig.Instance.FollowAngle = pendingFollowAngle;
                    VPBConfig.Instance._followDistance = pendingFollowDistance;
                    VPBConfig.Instance._followEyeHeight = pendingFollowEyeHeight;
                    VPBConfig.Instance.ReorientStartAngle = pendingReorientStartAngle;
                    VPBConfig.Instance.MovementThreshold = pendingMovementThreshold;
                    VPBConfig.Instance.BringToFrontDistance = pendingBringToFrontDistance;
                    VPBConfig.Instance.EnableGalleryFade = pendingEnableGalleryFade;
                    VPBConfig.Instance.EnableGalleryTranslucency = pendingEnableGalleryTranslucency;
                    VPBConfig.Instance.GalleryManualRefreshOnly = pendingGalleryManualRefreshOnly;
                    VPBConfig.Instance.GalleryOpacity = pendingGalleryOpacity;
                    VPBConfig.Instance.SideButtonScale = pendingSideButtonScale;
                    VPBConfig.Instance.InnerPaneScale = pendingInnerPaneScale;
                    VPBConfig.Instance.DragDropReplaceMode = pendingDragDropReplaceMode;
                    VPBConfig.Instance.AppearanceClothingApplyMode = pendingAppearanceClothingApplyMode;
                    VPBConfig.Instance.EnableDragDrop = pendingEnableDragDrop;
                    VPBConfig.Instance.RequireDragHoldBeforeMove = pendingRequireDragHoldBeforeMove;
                    VPBConfig.Instance.DragHoldThreshold = pendingDragHoldThreshold;
                    VPBConfig.Instance.IsDevMode = pendingIsDevMode;
                    VPBConfig.Instance.EnableAutoFixedGallery = pendingEnableAutoFixedGallery;
                    VPBConfig.Instance.InitialGalleryCategory = pendingInitialGalleryCategory;
                    VPBConfig.Instance.GalleryDefaultLeftSidePanel = pendingGalleryDefaultLeftSidePanel;
                    VPBConfig.Instance.GalleryDefaultRightSidePanel = pendingGalleryDefaultRightSidePanel;
                    backupGalleryDefaultLeftSidePanel = pendingGalleryDefaultLeftSidePanel;
                    backupGalleryDefaultRightSidePanel = pendingGalleryDefaultRightSidePanel;
                    VPBConfig.Instance.PluginGalleryGridThumbnails = pendingPluginGalleryGridThumbnails;
                    backupPluginGalleryGridThumbnails = pendingPluginGalleryGridThumbnails;
                    VPBConfig.Instance.GalleryListNamesLegacyFileName = pendingGalleryListNamesLegacyFileName;
                    backupGalleryListNamesLegacyFileName = pendingGalleryListNamesLegacyFileName;
                    VPBConfig.Instance.GalleryHoverPreviewMode = pendingGalleryHoverPreviewMode;
                    backupGalleryHoverPreviewMode = pendingGalleryHoverPreviewMode;
                    VPBConfig.Instance.GalleryListHoverPreviewSize = pendingGalleryListHoverPreviewSize;
                    backupGalleryListHoverPreviewSize = pendingGalleryListHoverPreviewSize;
                    VPBConfig.Instance.GalleryListHoverPreviewOffsetX = pendingGalleryListHoverPreviewOffsetX;
                    backupGalleryListHoverPreviewOffsetX = pendingGalleryListHoverPreviewOffsetX;
                    VPBConfig.Instance.GalleryListHoverPreviewOffsetY = pendingGalleryListHoverPreviewOffsetY;
                    backupGalleryListHoverPreviewOffsetY = pendingGalleryListHoverPreviewOffsetY;
                    // Avoid ConfigChanged from Save: live preview already ran it for most controls (expensive full gallery layout).
                    VPBConfig.Instance.Save(false);
                    if (SaveNeedsDeferredConfigNotify())
                        VPBConfig.Instance.TriggerChange();
                    if (parentPanel != null) parentPanel.ApplySidePanelDefaultsFromConfig();
                }

                isSettingsOpen = false;
                if (settingsPaneGO != null) settingsPaneGO.SetActive(false);
                if (tooltipGO != null) tooltipGO.SetActive(false);
            });
            saveBtn.GetComponent<Image>().color = new Color(0.25f, 0.6f, 0.25f, 1f);
            settingsSaveBtnText = saveBtn.GetComponentInChildren<Text>();
            settingsSaveBtnText.color = Color.white;
            saveBtn.AddComponent<UIHoverBorder>();

            // Close button (X) in top right
            GameObject xBtn = UI.CreateUIButton(settingsPaneGO, 40, 40, "X", 24, 0, 0, AnchorPresets.topRight, Close);
            xBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            xBtn.GetComponentInChildren<Text>().color = Color.white;
            xBtn.AddComponent<UIHoverBorder>();

            // Tooltip (Initially hidden)
            tooltipGO = UI.AddChildGOImage(settingsPaneGO, new Color(0, 0, 0, 0.9f), AnchorPresets.bottomMiddle, 480, 100, new Vector2(0, -60));
            tooltipGO.SetActive(false);
            GameObject tTextGO = new GameObject("TooltipText");
            tTextGO.transform.SetParent(tooltipGO.transform, false);
            tooltipText = tTextGO.AddComponent<Text>();
            tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tooltipText.fontSize = 20;
            tooltipText.color = Color.white;
            tooltipText.alignment = TextAnchor.MiddleCenter;
            RectTransform ttRT = tooltipText.GetComponent<RectTransform>();
            ttRT.anchorMin = Vector2.zero; ttRT.anchorMax = Vector2.one;
            ttRT.sizeDelta = new Vector2(-20, -20);
        }

        private void SyncSettingsScrollContent()
        {
            if (!LogSettingsOpenCloseTiming)
            {
                for (int i = 0; i < _settingsScrollSync.Count; i++)
                {
                    try { _settingsScrollSync[i](); }
                    catch { }
                }
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < _settingsScrollSync.Count; i++)
            {
                try { _settingsScrollSync[i](); }
                catch { }
            }
            SettingsPerfLog($"SyncSettingsScrollContent rows={_settingsScrollSync.Count} {sw.ElapsedMilliseconds}ms");
        }

        private void RebuildSettingsScrollContent()
        {
            bool logRebuild = LogSettingsOpenCloseTiming;
            Stopwatch sw = new Stopwatch();
            if (logRebuild) sw.Start();
            long mark = 0;
            if (logRebuild) mark = sw.ElapsedMilliseconds;

            _settingsScrollSync.Clear();
            _syncDragHoldDelayRowInteractable = null;
            foreach (Transform child in settingsScrollContent.transform) GameObject.Destroy(child.gameObject);

            if (logRebuild)
            {
                SettingsPerfLog($"Rebuild.DestroyScrollChildren {sw.ElapsedMilliseconds - mark}ms (Unity Destroy is deferred; real GC/layout cost may land on a later frame)");
                mark = sw.ElapsedMilliseconds;
            }

            // CATEGORY: Visuals
            CreateHeader(VPBTranslation.T("settings.header.visuals", "Visuals"));

            // Enable Gallery Fade
            CreateToggleSetting(VPBTranslation.T("settings.side_button_fade", "Side Button Fade"), pendingEnableGalleryFade, (val) => {
                pendingEnableGalleryFade = val;
                VPBConfig.Instance.EnableGalleryFade = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.side_button_fade", "Fades out side buttons when not hovering over them."), () => pendingEnableGalleryFade);

            // Gallery Translucency
            CreateToggleSetting(VPBTranslation.T("settings.gallery_translucency", "Gallery Translucency"), pendingEnableGalleryTranslucency, (val) => {
                pendingEnableGalleryTranslucency = val;
                VPBConfig.Instance.EnableGalleryTranslucency = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_translucency", "Makes the entire gallery pane translucent."), () => pendingEnableGalleryTranslucency);

            CreateToggleSetting(VPBTranslation.T("settings.gallery_manual_refresh_only", "Manual gallery refresh only"), pendingGalleryManualRefreshOnly, (val) => {
                pendingGalleryManualRefreshOnly = val;
                VPBConfig.Instance.GalleryManualRefreshOnly = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_manual_refresh_only", "When enabled, package scans do not update the file grid until you press Refresh in the gallery. Reduces scroll jumps and load when the package index changes often."), () => pendingGalleryManualRefreshOnly);

            CreateSliderSetting(VPBTranslation.T("settings.gallery_opacity", "Gallery Opacity"), pendingGalleryOpacity, 0.1f, 1.0f, (val) => {
                pendingGalleryOpacity = val;
                VPBConfig.Instance.GalleryOpacity = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.gallery_opacity", "The opacity of the gallery pane when translucency is enabled. 0.1 = 10% visible, 1.0 = Opaque."), () => pendingGalleryOpacity);

            CreateSliderSetting(VPBTranslation.T("settings.side_button_scale", "Side Button Scale"), pendingSideButtonScale, 0.5f, 2.0f, (val) => {
                pendingSideButtonScale = val;
                VPBConfig.Instance.SideButtonScale = val;
                if (parentPanel != null) parentPanel.ApplySideButtonScale();
            }, VPBTranslation.T("settings.tip.side_button_scale", "Scales the size of the side buttons. 1.0 = default size."), () => pendingSideButtonScale);

            CreateSliderSetting(VPBTranslation.T("settings.inner_pane_scale", "Inner Pane Scale"), pendingInnerPaneScale, 0.5f, 2.0f, (val) => {
                pendingInnerPaneScale = val;
                VPBConfig.Instance.InnerPaneScale = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.inner_pane_scale", "Scales all UI elements inside the gallery pane. 1.0 = default size."), () => pendingInnerPaneScale);

            // Side Button Gaps
            CreateToggleSetting(VPBTranslation.T("settings.side_button_gaps", "Side Button Gaps"), pendingEnableButtonGaps, (val) => {
                pendingEnableButtonGaps = val;
                VPBConfig.Instance.EnableButtonGaps = val;
                VPBConfig.Instance.TriggerChange(); 
            }, VPBTranslation.T("settings.tip.side_button_gaps", "Adds small gaps between groups of side buttons for better visual separation."), () => pendingEnableButtonGaps);
            
            bool isFixed = parentPanel != null && parentPanel.isFixedLocally;

            if (!isFixed)
            {
                // Show Side Buttons
                string[] sideButtonOptions = { "Both", "Left", "Right" };
                string[] sideButtonLabels = {
                    VPBTranslation.T("settings.side_both", "Both Sides"),
                    VPBTranslation.T("settings.side_left", "Left Side"),
                    VPBTranslation.T("settings.side_right", "Right Side")
                };
                CreateCycleSetting(VPBTranslation.T("settings.show_side_buttons", "Show Side Buttons"), pendingShowSideButtons, sideButtonOptions, sideButtonLabels, (val) => {
                    pendingShowSideButtons = val;
                    VPBConfig.Instance.ShowSideButtons = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.show_side_buttons", "Choose which sides of the gallery show the action buttons."), () => pendingShowSideButtons);
            }

            // CATEGORY: Interaction
            if (!isFixed)
            {
                // CATEGORY: Follow Mode
                CreateHeader(VPBTranslation.T("settings.header.follow_mode", "Follow Mode"));

                string[] followOptions = { "Off", "Desktop", "VR", "Both" };
                string[] followLabels = {
                    VPBTranslation.T("settings.follow.off", "Off"),
                    VPBTranslation.T("settings.follow.desktop", "Desktop"),
                    VPBTranslation.T("settings.follow.vr", "VR"),
                    VPBTranslation.T("settings.follow.both", "Both")
                };

                // Follow Angle
                CreateCycleSetting(VPBTranslation.T("settings.follow_angle", "Follow Angle"), pendingFollowAngle, followOptions, followLabels, (val) => {
                    pendingFollowAngle = val;
                    VPBConfig.Instance.FollowAngle = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_angle", "When enabled, the panel will rotate to face the user. 'Both' = both VR and Desktop."), () => pendingFollowAngle);

                // Follow Eye Height
                CreateCycleSetting(VPBTranslation.T("settings.follow_eye_height", "Follow Eye Height"), pendingFollowEyeHeight, followOptions, followLabels, (val) => {
                    pendingFollowEyeHeight = val;
                    VPBConfig.Instance.FollowEyeHeight = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_eye_height", "When enabled, the panel will stay at eye level. 'Both' = both VR and Desktop."), () => pendingFollowEyeHeight);

                // Follow Distance (ON/OFF)
                CreateCycleSetting(VPBTranslation.T("settings.follow_distance", "Follow Distance"), pendingFollowDistance, followOptions, followLabels, (val) => {
                    pendingFollowDistance = val;
                    VPBConfig.Instance.FollowDistance = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.follow_distance", "When enabled, the panel will maintain its distance from the user. 'Both' = both VR and Desktop."), () => pendingFollowDistance);

                // Reorient Start Angle
                CreateSliderSetting(VPBTranslation.T("settings.reorient_angle", "Reorient Angle"), pendingReorientStartAngle, 5f, 90f, (val) => {
                    pendingReorientStartAngle = val;
                    VPBConfig.Instance.ReorientStartAngle = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.reorient_angle", "The angle difference required before the panel starts rotating to face you. Higher values reduce frequent rotations."), () => pendingReorientStartAngle);

                // Movement Threshold
                CreateSliderSetting(VPBTranslation.T("settings.move_threshold", "Move Threshold"), pendingMovementThreshold, 0.01f, 1.0f, (val) => {
                    pendingMovementThreshold = val;
                    VPBConfig.Instance.MovementThreshold = val;
                    VPBConfig.Instance.TriggerChange();
                }, VPBTranslation.T("settings.tip.move_threshold", "The distance you must move before the panel updates its position. Higher values provide more stable 'discrete' updates."), () => pendingMovementThreshold);

                // Bring to Front Distance
                CreateSliderSetting(VPBTranslation.T("settings.bring_front_dist", "Bring Front Dist"), pendingBringToFrontDistance, 0.5f, 2.5f, (val) => {
                    pendingBringToFrontDistance = val;
                    VPBConfig.Instance.BringToFrontDistance = val;
                }, VPBTranslation.T("settings.tip.bring_front_dist", "The distance (in meters) from your view where panels will appear when using 'Bring to Front'."), () => pendingBringToFrontDistance);
            }

            // CATEGORY: Interaction
            CreateHeader(VPBTranslation.T("settings.header.interaction", "Interaction"));
            CreateToggleSetting(VPBTranslation.T("settings.enable_drag_drop", "Enable Drag & Drop"), pendingEnableDragDrop, (val) => {
                pendingEnableDragDrop = val;
                VPBConfig.Instance.EnableDragDrop = val;
                _syncDragHoldDelayRowInteractable?.Invoke();
            }, VPBTranslation.T("settings.tip.enable_drag_drop", "Off by default. Turn on to drag items from the gallery onto atoms or the scene. When off, apply by click only; no drag context menu."), () => pendingEnableDragDrop);

            CreateToggleSetting(VPBTranslation.T("settings.require_drag_hold", "Require hold before drag"), pendingRequireDragHoldBeforeMove, (val) => {
                pendingRequireDragHoldBeforeMove = val;
                VPBConfig.Instance.RequireDragHoldBeforeMove = val;
                _syncDragHoldDelayRowInteractable?.Invoke();
            }, VPBTranslation.T("settings.tip.require_drag_hold", "Off = classic behavior: no hold delay (same as before the hold feature). On = use the duration below so the pointer must stay still before a drag starts (helps VR jitter and mis-clicks)."), () => pendingRequireDragHoldBeforeMove);

            CreateSliderSetting(VPBTranslation.T("settings.drag_hold_threshold", "Hold duration (s)"), pendingDragHoldThreshold, 0f, 3f, (val) => {
                pendingDragHoldThreshold = val;
                VPBConfig.Instance.DragHoldThreshold = val;
            }, VPBTranslation.T("settings.tip.drag_hold_threshold", "Only when drag-and-drop and Require hold before drag are both on. How long to hold still before the drag starts. Use 0 for immediate drag once hold mode is on, or increase for shaky pointers."), () => pendingDragHoldThreshold, () => pendingEnableDragDrop && pendingRequireDragHoldBeforeMove);

            string[] appearanceClothingOptions = { "replace", "keep", "clothingonly" };
            string[] appearanceClothingLabels = {
                VPBTranslation.T("settings.appearance.preset_outfit", "Preset outfit"),
                VPBTranslation.T("settings.appearance.keep_body", "Keep body clothes"),
                VPBTranslation.T("settings.appearance.clothes_only", "Clothes only")
            };
            CreateCycleSetting(VPBTranslation.T("settings.appearance_clothing", "Appearance clothing"), pendingAppearanceClothingApplyMode, appearanceClothingOptions, appearanceClothingLabels, (val) => {
                pendingAppearanceClothingApplyMode = val;
                VPBConfig.Instance.AppearanceClothingApplyMode = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.RefreshAppearanceClothingSideButton();
            }, VPBTranslation.T("settings.tip.appearance_clothing", "Preset outfit: full appearance. Keep body clothes: face/body/hair from preset, keep your garments. Clothes only: keep current person; apply only garment clothing from the preset (not hair or makeup-type items)."), () => pendingAppearanceClothingApplyMode);

            // CATEGORY: Desktop
            CreateHeader(VPBTranslation.T("settings.header.desktop", "Desktop"));
            CreateToggleSetting(VPBTranslation.T("settings.startup_fixed_gallery", "Startup Gallery (Fixed)"), pendingEnableAutoFixedGallery, (val) => {
                pendingEnableAutoFixedGallery = val;
                VPBConfig.Instance.EnableAutoFixedGallery = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.startup_fixed_gallery", "When enabled, a pinned (Fixed) gallery pane with Autohide enabled will be automatically created on the right side of the screen when the plugin starts."), () => pendingEnableAutoFixedGallery);

            string[] initialGalleryOptions = { "Scenes", "Clothing", "Hair", "Pose", "Appearance", "Plugins", "LastUsed" };
            string[] initialGalleryLabels = {
                VPBTranslation.T("settings.initial_gallery.scenes", "Scenes"),
                VPBTranslation.T("settings.initial_gallery.clothing", "Clothing"),
                VPBTranslation.T("settings.initial_gallery.hair", "Hair"),
                VPBTranslation.T("settings.initial_gallery.pose", "Pose"),
                VPBTranslation.T("settings.initial_gallery.appearance", "Appearance"),
                VPBTranslation.T("settings.initial_gallery.plugins", "Plugins"),
                VPBTranslation.T("settings.initial_gallery.last_used", "Last used")
            };
            CreateCycleSetting(VPBTranslation.T("settings.initial_gallery_category", "Gallery opens on"), pendingInitialGalleryCategory, initialGalleryOptions, initialGalleryLabels, (val) => {
                pendingInitialGalleryCategory = val;
                VPBConfig.Instance.InitialGalleryCategory = val;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.initial_gallery_category", "Which category is shown when the gallery first opens this session or when a new pane is created. Default is Scenes. Last used restores the tab saved when you last left the gallery."), () => pendingInitialGalleryCategory);

            CreateSettingsSectionSeparator();
            CreateHeader(VPBTranslation.T("settings.header.gallery_side_lists", "Gallery side lists"));
            string[] sidePanelOptions = { "None", "Category", "Creator" };
            string[] sidePanelLabels = {
                VPBTranslation.T("settings.gallery_side.none", "None"),
                VPBTranslation.T("settings.gallery_side.category", "Category"),
                VPBTranslation.T("settings.gallery_side.creator", "Creator")
            };
            CreateCycleSetting(VPBTranslation.T("settings.gallery_default_left_panel", "Left side list (default)"), pendingGalleryDefaultLeftSidePanel, sidePanelOptions, sidePanelLabels, (val) => {
                pendingGalleryDefaultLeftSidePanel = val;
                VPBConfig.Instance.GalleryDefaultLeftSidePanel = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.ApplySidePanelDefaultsFromConfig();
            }, VPBTranslation.T("settings.tip.gallery_default_left_panel", "Which filter list is open on the left when a gallery pane is created. None leaves that side closed. If left and right are the same, only the left opens. Floating mode: if both are None, Category opens on the right (same as before)."), () => pendingGalleryDefaultLeftSidePanel);

            CreateCycleSetting(VPBTranslation.T("settings.gallery_default_right_panel", "Right side list (default)"), pendingGalleryDefaultRightSidePanel, sidePanelOptions, sidePanelLabels, (val) => {
                pendingGalleryDefaultRightSidePanel = val;
                VPBConfig.Instance.GalleryDefaultRightSidePanel = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.ApplySidePanelDefaultsFromConfig();
            }, VPBTranslation.T("settings.tip.gallery_default_right_panel", "Which filter list is open on the right when a gallery pane is created. None leaves that side closed unless floating mode applies the Category-on-right fallback when both sides are None."), () => pendingGalleryDefaultRightSidePanel);
            CreateSettingsSectionSeparator();

            CreateToggleSetting(VPBTranslation.T("settings.plugin_gallery_grid_thumbnails", "Plugin thumbnails in grid"), pendingPluginGalleryGridThumbnails, (val) => {
                pendingPluginGalleryGridThumbnails = val;
                VPBConfig.Instance.PluginGalleryGridThumbnails = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.RefreshFiles(true);
            }, VPBTranslation.T("settings.tip.plugin_gallery_grid_thumbnails", "When on, .cs/.cslist/.dll under Custom/Scripts use the same sister-image rule as other files: MyPlugin.jpg or MyPlugin.png next to MyPlugin.cs shows in the grid. When off, the grid stays blank for plugins; select an item and expand the info strip to see a preview if a sister image exists."), () => pendingPluginGalleryGridThumbnails);

            CreateToggleSetting(VPBTranslation.T("settings.gallery_list_legacy_names", "Legacy gallery list names"), pendingGalleryListNamesLegacyFileName, (val) => {
                pendingGalleryListNamesLegacyFileName = val;
                VPBConfig.Instance.GalleryListNamesLegacyFileName = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) parentPanel.RefreshFiles(true);
            }, VPBTranslation.T("settings.tip.gallery_list_legacy_names", "When off (default), list layout shows the package id (Creator.Package.Version, without .var) for items inside packages. When on, list rows use the file or item name like before."), () => pendingGalleryListNamesLegacyFileName);

            CreateSettingsSectionSeparator();
            CreateHeader(VPBTranslation.T("settings.header.hover_preview", "Hover preview"));

            // Hover Preview (Grid/List/Both/Off)
            string[] hoverPreviewModeOptions = { "Off", "List", "Grid", "Both" };
            string[] hoverPreviewModeLabels = {
                VPBTranslation.T("settings.hover_preview.off", "OFF"),
                VPBTranslation.T("settings.hover_preview.list", "List"),
                VPBTranslation.T("settings.hover_preview.grid", "Grid"),
                VPBTranslation.T("settings.hover_preview.both", "Both"),
            };
            CreateCycleSetting(VPBTranslation.T("settings.hover_preview_mode", "Hover preview"), pendingGalleryHoverPreviewMode, hoverPreviewModeOptions, hoverPreviewModeLabels, (val) => {
                pendingGalleryHoverPreviewMode = VPBConfig.NormalizeHoverPreviewMode(val);
                VPBConfig.Instance.GalleryHoverPreviewMode = pendingGalleryHoverPreviewMode;
                VPBConfig.Instance.TriggerChange();
            }, VPBTranslation.T("settings.tip.hover_preview_mode", "Show a larger square image preview when hovering thumbnails. Choose where it applies: List, Grid, Both, or OFF."), () => pendingGalleryHoverPreviewMode);

            CreateSliderSetting(VPBTranslation.T("settings.hover_preview_size", "Hover preview size"), pendingGalleryListHoverPreviewSize, 200f, 600f, (val) => {
                pendingGalleryListHoverPreviewSize = val;
                VPBConfig.Instance.GalleryListHoverPreviewSize = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) { try { parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } catch { } }
            }, VPBTranslation.T("settings.tip.hover_preview_size", "Size (in pixels) of the square hover preview."), () => pendingGalleryListHoverPreviewSize,
                onLiveDrag: (v) => { try { if (parentPanel != null) { VPBConfig.Instance.GalleryListHoverPreviewSize = v; parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } } catch { } });

            CreateSliderSetting(VPBTranslation.T("settings.hover_preview_offset_x", "Hover preview X offset"), pendingGalleryListHoverPreviewOffsetX, -2000f, 2000f, (val) => {
                pendingGalleryListHoverPreviewOffsetX = val;
                VPBConfig.Instance.GalleryListHoverPreviewOffsetX = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) { try { parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } catch { } }
            }, VPBTranslation.T("settings.tip.hover_preview_offset_x", "Move the hover preview left/right (pixels) relative to the default docked bottom-left position."), () => pendingGalleryListHoverPreviewOffsetX, allowNegative: true,
                onLiveDrag: (v) => { try { if (parentPanel != null) { VPBConfig.Instance.GalleryListHoverPreviewOffsetX = v; parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } } catch { } });

            CreateSliderSetting(VPBTranslation.T("settings.hover_preview_offset_y", "Hover preview Y offset"), pendingGalleryListHoverPreviewOffsetY, -2000f, 2000f, (val) => {
                pendingGalleryListHoverPreviewOffsetY = val;
                VPBConfig.Instance.GalleryListHoverPreviewOffsetY = val;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) { try { parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } catch { } }
            }, VPBTranslation.T("settings.tip.hover_preview_offset_y", "Move the hover preview up/down (pixels) relative to the default docked bottom-left position."), () => pendingGalleryListHoverPreviewOffsetY, allowNegative: true,
                onLiveDrag: (v) => { try { if (parentPanel != null) { VPBConfig.Instance.GalleryListHoverPreviewOffsetY = v; parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } } catch { } });

            CreateActionButtonSetting(VPBTranslation.T("settings.hover_preview_position", "Hover preview position"), VPBTranslation.T("settings.reset", "Reset"), () => {
                pendingGalleryListHoverPreviewOffsetX = 0f;
                pendingGalleryListHoverPreviewOffsetY = 0f;
                VPBConfig.Instance.GalleryListHoverPreviewOffsetX = 0f;
                VPBConfig.Instance.GalleryListHoverPreviewOffsetY = 0f;
                VPBConfig.Instance.TriggerChange();
                if (parentPanel != null) { try { parentPanel.SetHoverPreviewDummyActive(true); parentPanel.RefreshHoverPreviewLayoutImmediate(); } catch { } }
                SyncSettingsNow();
            }, VPBTranslation.T("settings.tip.hover_preview_position", "Reset the hover preview position offsets back to the docked default."));

            CreateSettingsSectionSeparator();

            if (isFixed)
            {
                // Bring to Front Distance (for Fixed Mode too, as requested)
                CreateSliderSetting(VPBTranslation.T("settings.bring_front_dist", "Bring Front Dist"), pendingBringToFrontDistance, 0.5f, 2.5f, (val) => {
                    pendingBringToFrontDistance = val;
                    VPBConfig.Instance.BringToFrontDistance = val;
                }, VPBTranslation.T("settings.tip.bring_front_dist", "The distance (in meters) from your view where panels will appear when using 'Bring to Front'."), () => pendingBringToFrontDistance);
            }

            // CATEGORY: Gallery Categories
            CreateHeader(VPBTranslation.T("settings.header.gallery_categories", "Gallery Categories"));
            var knownHideable = new[] {
                "Person", "Person BreastPhysics", "Person General",
                "Person GlutePhysics", "Person Morphs", "Person Textures", "SubScenes", "Plugins"
            };
            foreach (var catName in knownHideable)
            {
                string cn = catName;
                bool isHidden = pendingHiddenCategories != null && pendingHiddenCategories.Contains(cn);
                CreateToggleSetting(VPBTranslation.T("settings.hide_category." + cn.Replace(" ", "_").ToLowerInvariant(), "Hide: " + cn), isHidden, (val) => {
                    if (val) pendingHiddenCategories.Add(cn);
                    else     pendingHiddenCategories.Remove(cn);
                    VPBConfig.Instance.HiddenCategories = new HashSet<string>(pendingHiddenCategories, StringComparer.OrdinalIgnoreCase);
                    VPBConfig.Instance.TriggerChange();
                    if (Gallery.singleton != null)
                    {
                        foreach (var p in Gallery.singleton.Panels)
                        {
                            try { p.UpdateTabs(); } catch { }
                        }
                    }
                    else if (parentPanel != null) parentPanel.UpdateTabs();
                }, VPBTranslation.T("settings.tip.hide_category", "Hide this category from the Categories tab list. The category is still accessible via search."), () => pendingHiddenCategories != null && pendingHiddenCategories.Contains(cn));
            }

            if (VPBConfig.Instance.IsDevMode)
            {
                CreateHeader(VPBTranslation.T("settings.header.developer", "Developer"));
                CreateToggleSetting(VPBTranslation.T("settings.developer_mode", "Developer Mode"), pendingIsDevMode, (val) => {
                    pendingIsDevMode = val;
                }, VPBTranslation.T("settings.tip.developer_mode", "Enables developer-only features and debug tools. Requires restart to fully hide/show some elements."), () => pendingIsDevMode);
            }

            if (logRebuild)
            {
                SettingsPerfLog($"Rebuild.createAllRows {sw.ElapsedMilliseconds - mark}ms");
                SettingsPerfLog($"Rebuild.TOTAL {sw.ElapsedMilliseconds}ms");
            }
        }

        private static string NormalizeSettingsAppearanceClothingMode(string m)
        {
            if (string.IsNullOrEmpty(m)) return "replace";
            string t = m.Trim().ToLowerInvariant();
            if (t == "keep") return "keep";
            if (t == "clothingonly") return "clothingonly";
            return "replace";
        }

        private void CreateHeader(string title)
        {
            GameObject container = new GameObject("Header_" + title);
            container.transform.SetParent(settingsScrollContent.transform, false);
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 40; le.preferredHeight = 40;
            le.flexibleWidth = 1;

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(container.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = title.ToUpper();
            t.fontSize = 20;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 0);
            rt.offsetMax = new Vector2(-10, 0);

            // Add a small underline
            GameObject line = new GameObject("Underline");
            line.transform.SetParent(container.transform, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f);
            RectTransform lrt = line.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 0);
            lrt.sizeDelta = new Vector2(-20, 2);
            lrt.anchoredPosition = new Vector2(0, 2);
        }

        private void CreateSettingsSectionSeparator()
        {
            GameObject row = new GameObject("SectionSeparator");
            row.transform.SetParent(settingsScrollContent.transform, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 16;
            le.preferredHeight = 16;
            le.flexibleWidth = 1;

            GameObject line = new GameObject("Line");
            line.transform.SetParent(row.transform, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.32f, 0.32f, 0.32f, 1f);
            RectTransform lrt = line.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8, 7);
            lrt.offsetMax = new Vector2(-8, -7);
        }

        private void AddTooltipIcon(GameObject container, string tooltip)
        {
            GameObject iconGO = new GameObject("TooltipIcon");
            iconGO.transform.SetParent(container.transform, false);
            Image img = iconGO.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(5, -20);
            rt.sizeDelta = new Vector2(30, 30);

            GameObject textGO = new GameObject("i");
            textGO.transform.SetParent(iconGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = "i";
            t.fontSize = 20;
            t.fontStyle = FontStyle.Italic;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform tRT = textGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.sizeDelta = Vector2.zero;

            UIHoverDelegate hd = iconGO.AddComponent<UIHoverDelegate>();
            hd.OnHoverChange = (isHovering) => {
                if (isHovering)
                {
                    if (tooltipText != null) tooltipText.text = tooltip;
                    if (tooltipGO != null) tooltipGO.SetActive(true);
                }
                else
                {
                    if (tooltipGO != null) tooltipGO.SetActive(false);
                }
            };
        }

        private void SyncSettingsNow()
        {
            if (_settingsScrollSync == null) return;
            for (int i = 0; i < _settingsScrollSync.Count; i++)
            {
                try { _settingsScrollSync[i](); } catch { }
            }
        }

        private void CreateSliderSetting(string label, float currentVal, float min, float max, Action<float> onChange, string tooltip, Func<float> syncGetPending, Func<bool> enabledWhen = null, bool allowNegative = false, Action<float> onLiveDrag = null)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 100; le.preferredHeight = 100;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            // Row 1: Label and Numeric Entry
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.5f);
            labelRT.anchorMax = new Vector2(0.6f, 1f);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            // Numeric Entry (InputField)
            GameObject inputGO = new GameObject("NumericInput");
            inputGO.transform.SetParent(container.transform, false);
            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            inputBg.raycastTarget = true;
            InputField inputField = inputGO.AddComponent<InputField>();
            inputField.targetGraphic = inputBg;
            RectTransform inputRT = inputGO.GetComponent<RectTransform>();
            inputRT.anchorMin = new Vector2(0.7f, 0.6f);
            inputRT.anchorMax = new Vector2(0.95f, 0.9f);
            inputRT.sizeDelta = Vector2.zero;
            inputGO.AddComponent<UIHoverBorder>();

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text inputText = textGO.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            inputText.fontSize = 20; inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleCenter;
            inputText.raycastTarget = false;
            RectTransform inputTextRT = textGO.GetComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero; inputTextRT.anchorMax = Vector2.one;
            inputTextRT.sizeDelta = Vector2.zero;
            inputField.textComponent = inputText;
            inputField.text = currentVal.ToString("F1");
            inputField.contentType = allowNegative ? InputField.ContentType.Standard : InputField.ContentType.DecimalNumber;
            if (allowNegative)
            {
                inputField.onValidateInput = (string text, int charIndex, char addedChar) =>
                {
                    if (char.IsDigit(addedChar)) return addedChar;
                    if (addedChar == '.')
                    {
                        // One decimal separator max
                        if (text != null && text.IndexOf('.') >= 0) return '\0';
                        return addedChar;
                    }
                    if (addedChar == '-')
                    {
                        // Only allow leading '-' and only once
                        if (charIndex != 0) return '\0';
                        if (text != null && text.IndexOf('-') >= 0) return '\0';
                        return addedChar;
                    }
                    return '\0';
                };
            }

            // Row 2: Slider
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            Slider slider = sliderGO.AddComponent<Slider>();
            sliderGO.AddComponent<UIHoverBorder>();
            RectTransform sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.05f, 0.1f);
            sliderRT.anchorMax = new Vector2(0.95f, 0.4f);
            sliderRT.sizeDelta = Vector2.zero;

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            bgImg.raycastTarget = true;
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.4f);
            bgRT.anchorMax = new Vector2(1, 0.6f);
            bgRT.sizeDelta = Vector2.zero;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.4f);
            fillAreaRT.anchorMax = new Vector2(1, 0.6f);
            fillAreaRT.sizeDelta = Vector2.zero;

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.5f, 0.8f);
            fillImg.raycastTarget = false;
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.sizeDelta = Vector2.zero;
            slider.fillRect = fillRT;

            // Handle Area
            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = new Vector2(0, 0);
            handleAreaRT.anchorMax = new Vector2(1, 1);
            handleAreaRT.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            handleImg.raycastTarget = true;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0, 0);
            handleRT.anchorMax = new Vector2(0, 1);
            handleRT.sizeDelta = new Vector2(30, 0);
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.minValue = min;
            slider.maxValue = max;
            slider.value = currentVal;

            UIScrollWheelHandler swh = inputGO.AddComponent<UIScrollWheelHandler>();
            swh.Sensitivity = 1.0f;
            swh.OnScrollValue = (delta) => {
                float step = 0.1f * Mathf.Sign(delta);
                float newVal = Mathf.Clamp(slider.value + step, min, max);
                slider.value = newVal;
                inputField.text = newVal.ToString("F1");
                onChange(newVal);
            };

            // Synchronization
            slider.onValueChanged.AddListener((val) => {
                inputField.text = val.ToString("F1");
                try { onLiveDrag?.Invoke(val); } catch { }
                // We don't call onChange here to avoid loops during drag
            });

            // Add EventTrigger for PointerUp to trigger onChange only on release
            EventTrigger trigger = sliderGO.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerUp;
            entry.callback.AddListener((data) => {
                onChange(slider.value);
            });
            trigger.triggers.Add(entry);

            inputField.onEndEdit.AddListener((val) => {
                if (float.TryParse(val, out float res))
                {
                    res = Mathf.Clamp(res, min, max);
                    slider.value = res;
                    inputField.text = res.ToString("F1");
                    onChange(res);
                }
                else
                {
                    inputField.text = slider.value.ToString("F1");
                }
            });

            if (syncGetPending != null)
            {
                _settingsScrollSync.Add(() =>
                {
                    float v = syncGetPending();
                    v = Mathf.Clamp(v, min, max);
                    slider.value = v;
                    inputField.text = v.ToString("F1");
                });
            }

            if (enabledWhen != null)
            {
                System.Action applyRowInteractable = () =>
                {
                    bool en = enabledWhen();
                    slider.interactable = en;
                    inputField.interactable = en;
                };
                applyRowInteractable();
                _settingsScrollSync.Add(applyRowInteractable);
                _syncDragHoldDelayRowInteractable = applyRowInteractable;
            }
        }

        private void CreateActionButtonSetting(string label, string buttonText, Action onClick, string tooltip)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 60; le.preferredHeight = 60;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.55f, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            float btnW = 150;
            float btnH = 45;
            float btnX = 300;
            GameObject btn = UI.CreateUIButton(container, btnW, btnH, buttonText ?? "Reset", 18, btnX, 0, AnchorPresets.middleLeft, () => { try { onClick?.Invoke(); } catch { } });
            btn.AddComponent<UIHoverBorder>();
            Text bt = btn.GetComponentInChildren<Text>();
            if (bt != null) bt.color = Color.white;
            var bi = btn.GetComponent<Image>();
            if (bi != null) bi.color = new Color(0.35f, 0.35f, 0.35f, 1f);
        }

        private void CreateCycleSetting(string label, string currentVal, string[] options, string[] labels, Action<string> onCycle, string tooltip, Func<string> syncGetPending)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 60; le.preferredHeight = 60;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.4f, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            float btnW = 150;
            float btnH = 45;
            float btnX = 300;

            var cur = new RefString { Value = currentVal };
            int initIdx = Array.IndexOf(options, cur.Value);
            if (initIdx < 0) initIdx = 0;
            cur.Value = options[initIdx];

            GameObject cycleBtn = UI.CreateUIButton(container, btnW, btnH, labels[initIdx], 18, btnX, 0, AnchorPresets.middleLeft, null);
            cycleBtn.AddComponent<UIHoverBorder>();
            Text cycleTxt = cycleBtn.GetComponentInChildren<Text>();
            cycleTxt.color = Color.white;
            cycleBtn.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.8f, 1f);

            cycleBtn.GetComponent<Button>().onClick.AddListener(() => {
                int index = Array.IndexOf(options, cur.Value);
                if (index < 0) index = 0;
                index = (index + 1) % options.Length;
                cur.Value = options[index];
                cycleTxt.text = labels[index];
                onCycle(cur.Value);
            });

            if (syncGetPending != null)
            {
                _settingsScrollSync.Add(() =>
                {
                    string v = syncGetPending() ?? "";
                    int idx = Array.IndexOf(options, v);
                    if (idx < 0) idx = 0;
                    cur.Value = options[idx];
                    cycleTxt.text = labels[idx];
                });
            }
        }

        private void CreateToggleSetting(string label, bool currentVal, Action<bool> onToggle, string tooltip, Func<bool> syncGetPending)
        {
            GameObject container = new GameObject("Setting_" + label);
            container.transform.SetParent(settingsScrollContent.transform, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            LayoutElement le = container.AddComponent<LayoutElement>();
            le.minHeight = 60; le.preferredHeight = 60;
            le.flexibleWidth = 1;

            AddTooltipIcon(container, tooltip);

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            Text t = labelGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = label; t.fontSize = 22; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.4f, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(40, 0);
            labelRT.sizeDelta = Vector2.zero;

            float btnW = 70;
            float btnH = 45;
            float btnX = 300;

            var cur = new RefBool { Value = currentVal };

            GameObject offBtn = UI.CreateUIButton(container, btnW, btnH, VPBTranslation.T("settings.toggle.off", "OFF"), 18, btnX, 0, AnchorPresets.middleLeft, null);
            GameObject onBtn = UI.CreateUIButton(container, btnW, btnH, VPBTranslation.T("settings.toggle.on", "ON"), 18, btnX + btnW + 5, 0, AnchorPresets.middleLeft, null);
            
            offBtn.AddComponent<UIHoverBorder>();
            onBtn.AddComponent<UIHoverBorder>();
            
            Image offImg = offBtn.GetComponent<Image>();
            Image onImg = onBtn.GetComponent<Image>();
            Text offTxt = offBtn.GetComponentInChildren<Text>();
            Text onTxt = onBtn.GetComponentInChildren<Text>();

            Action updateColors = () => {
                offImg.color = cur.Value ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.6f, 0.2f, 0.2f);
                offTxt.color = Color.white;
                onImg.color = cur.Value ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.2f, 0.2f, 0.2f);
                onTxt.color = Color.white;
            };
            updateColors();

            offBtn.GetComponent<Button>().onClick.AddListener(() => {
                if (cur.Value) {
                    cur.Value = false;
                    updateColors();
                    onToggle(false);
                }
            });

            onBtn.GetComponent<Button>().onClick.AddListener(() => {
                if (!cur.Value) {
                    cur.Value = true;
                    updateColors();
                    onToggle(true);
                }
            });

            if (syncGetPending != null)
            {
                _settingsScrollSync.Add(() =>
                {
                    cur.Value = syncGetPending();
                    updateColors();
                });
            }
        }

    }
}
