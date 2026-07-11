using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    internal enum VpbResourceType
    {
        Appearance,
        Clothing,
        Hair,
        Pose,
        ClothingItem,
        HairItem,
        Morphs,
        General,
        Skin,
        BreastPhysics,
        Glute,
        Plugins,
        CUA
    }

    internal enum ClothingApplyMode
    {
        Keep,
        Replace,
        Merge,
        ClothingOnly
    }

    internal static class VpbImport
    {
        public static void LoadPreset(
            FileEntry sourceEntry,
            Atom targetAtom,
            VpbResourceType resourceType,
            ClothingApplyMode clothingMode,
            JSONClass presetJC = null,
            bool suppressRoot = false,
            string storableNameOverride = null,
            bool skipDependencyPrewarm = false,
            bool updateLastRestoredData = true,
            bool? suppressScaleChange = null)
        {
            if (targetAtom == null)
            {
                LogUtil.LogWarning("VpbImport.LoadPreset: targetAtom is null; aborting.");
                return;
            }

            if (sourceEntry == null && presetJC == null)
            {
                LogUtil.LogWarning("VpbImport.LoadPreset: both sourceEntry and presetJC are null; aborting.");
                return;
            }

            JSONClass preset = null;
            if (presetJC != null)
            {
                preset = presetJC;
            }
            else if (sourceEntry != null)
            {
                try
                {
                    string presetJson = FileManager.ReadAllText(sourceEntry);
                    preset = JSON.Parse(presetJson) as JSONClass;
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"VpbImport.LoadPreset: failed to load preset from sourceEntry: {ex.Message}");
                    return;
                }
            }

            if (preset == null)
            {
                LogUtil.LogWarning("VpbImport.LoadPreset: preset is null after resolution; aborting.");
                return;
            }

            if (sourceEntry != null && !skipDependencyPrewarm)
            {
                try
                {
                    List<string> movedUids = null;
                    bool ensured = UI.EnsureInstalled(sourceEntry, movedUids);
                    if (ensured)
                    {
                        LogUtil.Log("[VpbImport] Dependencies ensured installed.");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"VpbImport.LoadPreset: EnsureInstalled failed: {ex.Message}");
                }

                try
                {
                    int prewarmed = SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(sourceEntry, sourceEntry.Uid);
                    LogUtil.Log($"[VpbImport] Prewarm complete: {prewarmed} packages.");
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"VpbImport.LoadPreset: PrewarmOnDemandPackagesForEntry failed: {ex.Message}");
                }
            }

            // Must run after prewarm and before LoadPresetFromJSON. VaM batches the package-index
            // refresh; if it lands after the apply, dependent storables silently drop.
            if (!skipDependencyPrewarm)
            {
                try
                {
                    VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("vpb_import_prewarm_flush");
                    LogUtil.Log("[VpbImport] Coalesced refresh flushed.");
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"VpbImport.LoadPreset: ForceRunPendingCoalescedVamRefresh failed: {ex.Message}");
                }
            }

            if (resourceType == VpbResourceType.Appearance && sourceEntry != null)
            {
                if (presetJC != null)
                    preset = CloneJsonClassStatic(preset);
                VarPresetPathFixups.Apply(preset, UI.NormalizePath(sourceEntry.Uid));
            }

            // REFACTOR-IN-PROGRESS: regions below tag which slice owns each resource type's body.
            // Slice A wired the skeleton + Appearance/Clothing dispatch. Slice C extended Appearance.
            // Slice D will fill Pose. Slice E will fill Hair / ClothingItem / HairItem. Once all
            // resource types ship, flatten the regions and drop the slice labels.
            switch (resourceType)
            {
                #region Slice A + Slice C owns: Appearance
                case VpbResourceType.Appearance:
                {
                    try
                    {
                        // Caller (e.g. import sidebar) can override the global config flag per-apply via suppressScaleChange.
                        bool doSuppressScale = suppressScaleChange ?? (VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange);
                        if (doSuppressScale)
                        {
                            bool patched = AppearancePresetSuppress.PatchScaleToTargetCurrent(preset, targetAtom);
                            LogUtil.Log($"[VPB Scale] core suppress=ON patched={patched}");
                        }

                        JSONStorable presetStorable = targetAtom.GetStorableByID("AppearancePresets");
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning("VpbImport: AppearancePresets storable not found on target atom; aborting.");
                            return;
                        }
                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning("VpbImport: PresetManager not found in AppearancePresets storable; aborting.");
                            return;
                        }

                        // Suppress loadPresetOnSelect so internal callbacks don't auto-trigger a second load.
                        JSONStorableBool lpos = presetStorable.GetBoolJSONParam("loadPresetOnSelect");
                        bool lposPre = lpos != null ? lpos.val : false;
                        JSONStorableString psName = presetStorable.GetStringJSONParam("presetName");
                        string psNamePre = psName != null ? psName.val : "";
                        if (lpos != null) lpos.val = false;

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

                        if (clothingMode == ClothingApplyMode.ClothingOnly)
                        {
                            // Outfit Only: wear exactly the preset's real garments PLUS the target's
                            // current makeup/skin-overlay (cosmetic) clothing — body, face and hair are
                            // left untouched. Loaded through the dedicated ClothingPresets PresetManager
                            // (non-merge) so clothing item materials (textures/colors) bind correctly and
                            // indices align. The target's existing garments are dropped; its cosmetics kept.
                            JSONClass keepCosmetics = null;
                            try
                            {
                                JSONArray dump = new JSONArray();
                                targetAtom.Store(dump, true, true);
                                if (dump.Count > 0) keepCosmetics = dump[0].AsObject;
                            }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: ClothingOnly current-clothing capture failed: {ex.Message}"); }

                            JSONClass slice = BuildClothingOnlyPresetSlice(preset, keepCosmetics);
                            if (slice == null)
                            {
                                LogUtil.LogWarning("VpbImport: ClothingOnly slice empty; source preset has no garment storables.");
                                if (lpos != null) lpos.val = lposPre;
                                if (psName != null) psName.val = psNamePre;
                                break;
                            }

                            try { DumpOutfitOnlyDiag(preset, keepCosmetics, slice); }
                            catch (Exception ex) { LogUtil.LogWarning($"[VPB OutfitDiag] dump failed: {ex.Message}"); }

                            MeshVR.PresetManager clothingPM = null;
                            JSONStorable clothingPresetStorable = targetAtom.GetStorableByID("ClothingPresets");
                            if (clothingPresetStorable != null)
                                clothingPM = clothingPresetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                            if (clothingPM == null)
                            {
                                LogUtil.LogWarning("VpbImport: ClothingPresets PresetManager not found; aborting ClothingOnly.");
                                if (lpos != null) lpos.val = lposPre;
                                if (psName != null) psName.val = psNamePre;
                                break;
                            }

                            PresetParamsSnapshot clothingSnap = CapturePresetParamsSnapshot(targetAtom, "ClothingPresets");
                            MaybeSetLastRestoredData(targetAtom, slice, updateLastRestoredData);

                            try
                            {
                                if (!string.IsNullOrEmpty(sourcePath))
                                    MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                                InvokeLoadPresetFromJSON(clothingPM, slice, mergeLoad: false);
                            }
                            finally
                            {
                                if (!string.IsNullOrEmpty(sourcePath))
                                    MVR.FileManagement.FileManager.PopLoadDir();
                                RestorePresetParamsSnapshot(targetAtom, clothingSnap);
                                if (lpos != null) lpos.val = lposPre;
                                if (psName != null) psName.val = psNamePre;
                            }

                            TryApplyPluginsFromSource(presetManager, preset);

                            if (targetAtom.type == "Person")
                            {
                                try { SceneLoadingUtils.SchedulePostPersonApplyFixup(targetAtom); }
                                catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Post-apply fixup failed: {ex.Message}"); }
                            }
                            break;
                        }

                        if (clothingMode == ClothingApplyMode.Replace)
                        {
                            try { ClearAllClothingHairBools(targetAtom); }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Replace pre-cleanup failed: {ex.Message}"); }
                        }

                        // Keep: lock ClothingPresets PMC so VaM's preset loader skips clothing storables during the load.
                        PresetLockStore lockStore = null;
                        List<JSONClass> keepClothingMaterialSnapshots = null;
                        if (clothingMode == ClothingApplyMode.Keep && targetAtom.type == "Person")
                        {
                            lockStore = new PresetLockStore();
                            lockStore.StorePresetLocks(targetAtom, clearAllLocks: true, lockClothingPreset: true, lockMorphPreset: false);

                            // The lock preserves which clothing items are worn, but a non-merge appearance
                            // load resets unlisted storables to default — stripping textures/colors from the
                            // kept clothing since those material storables aren't in the incoming preset.
                            // Snapshot them now and re-apply after the load (issue #43).
                            try { keepClothingMaterialSnapshots = ClothingLoadingUtils.CaptureActiveClothingStorableSnapshots(targetAtom); }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Keep clothing material snapshot failed: {ex.Message}"); }
                        }

                        bool mergeLoad = clothingMode == ClothingApplyMode.Merge;
                        MaybeSetLastRestoredData(targetAtom, preset, updateLastRestoredData);

                        try
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                            InvokeLoadPresetFromJSON(presetManager, preset, mergeLoad);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                                MVR.FileManagement.FileManager.PopLoadDir();
                            if (lpos != null) lpos.val = lposPre;
                            if (psName != null) psName.val = psNamePre;
                            if (lockStore != null) lockStore.RestorePresetLocks(targetAtom);
                        }

                        // Re-apply the kept clothing's material/customization state that the non-merge load
                        // reset to default. Clothing selection was locked and unchanged, so the same storables
                        // still exist and can be restored synchronously (issue #43).
                        if (keepClothingMaterialSnapshots != null && keepClothingMaterialSnapshots.Count > 0)
                        {
                            try { ClothingLoadingUtils.RestoreClothingStorableSnapshots(targetAtom, keepClothingMaterialSnapshots); }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Keep clothing material restore failed: {ex.Message}"); }
                        }

                        if (targetAtom.type == "Person")
                        {
                            try { SceneLoadingUtils.SchedulePostPersonApplyFixup(targetAtom); }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Post-apply fixup failed: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"VpbImport: Appearance dispatch caught exception: {ex.Message}");
                    }
                    break;
                }
                #endregion

                #region Slice A owns: Clothing
                case VpbResourceType.Clothing:
                {
                    try
                    {
                        string storableName = "ClothingPresets";

                        JSONStorable presetStorable = targetAtom.GetStorableByID(storableName);
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning($"VpbImport: ClothingPresets storable not found on target atom; aborting.");
                            return;
                        }

                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PresetManager not found in ClothingPresets storable; aborting.");
                            return;
                        }

                        bool mergeLoad = false;
                        if (clothingMode == ClothingApplyMode.Merge)
                        {
                            mergeLoad = true;
                        }

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

                        // PresetManager.LoadPresetFromJSON overwrites the storable's "storable" lock-state
                        // child plus loadPresetOnSelect/presetName. Snapshot before, re-apply after, so the
                        // user's lock state and dropdown name survive the apply.
                        PresetParamsSnapshot snap = CapturePresetParamsSnapshot(targetAtom, storableName);

                        MaybeSetLastRestoredData(targetAtom, preset, updateLastRestoredData);

                        try
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                            }

                            Exception bridgeError = null;
                            MethodInfo loadMethod = typeof(MeshVR.PresetManager).GetMethod(
                                "LoadPresetFromJSON",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(JSONClass), typeof(bool) },
                                null);

                            if (loadMethod != null)
                            {
                                bool bridgeSuccess = PluginSignatureBridge.TryInvoke(
                                    loadMethod,
                                    presetManager,
                                    new object[] { preset, mergeLoad },
                                    out bridgeError,
                                    PluginSignatureBridge.DefaultFakeAssemblyName,
                                    PluginSignatureBridge.DefaultFakePluginHash);

                                if (bridgeSuccess)
                                {
                                    LogUtil.Log($"[VpbImport] Clothing preset applied via bridge (mergeLoad={mergeLoad}).");
                                }
                                else
                                {
                                    LogUtil.LogWarning($"VpbImport: Bridge invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
                                }
                            }
                            else
                            {
                                LogUtil.LogWarning("VpbImport: LoadPresetFromJSON method not found on PresetManager.");
                            }
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PopLoadDir();
                            }
                        }

                        RestorePresetParamsSnapshot(targetAtom, snap);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"VpbImport: Clothing dispatch caught exception: {ex.Message}");
                    }
                    break;
                }
                #endregion

                #region Slice D owns: Pose
                case VpbResourceType.Pose:
                {
                    try
                    {
                        // Extract scene-atom dump if needed (step 5)
                        if (preset["atoms"] != null)
                        {
                            JSONClass extracted = ExtractAtomFromSceneHelper(preset, "Person");
                            if (extracted != null)
                            {
                                preset = extracted;
                                LogUtil.Log("[VpbImport] Pose dispatch: extracted Person atom from scene dump.");
                            }
                        }

                        // Optional suppressRoot JSON patch (step 6)
                        if (suppressRoot)
                        {
                            CleanPresetsHelper(preset);
                            LogUtil.Log("[VpbImport] Pose dispatch: suppressRoot stripping applied.");
                        }

                        // Pin the primary body controls On when the pose omits their state (see helper). Without this,
                        // merge-load leaves pre-existing Off/Comply foot/hip states → feet unpinned → toes curl.
                        EnsurePrimaryPoseControlStatesHelper(preset);

                        // Diagnostics: snapshot exactly what we hand to VaM's native pose loader so Ctrl+Shift+P can
                        // compare the source foot/toe/hand control specs against the live applied state.
                        VPB.src.util.PoseImportDiagnostics.CaptureSource(preset, targetAtom != null ? targetAtom.uid : null);

                        // Resolve target storable and PresetManager (steps 7-8)
                        string storableName = "PosePresets";

                        JSONStorable presetStorable = targetAtom.GetStorableByID(storableName);
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PosePresets storable not found on target atom; aborting.");
                            return;
                        }

                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PresetManager not found in PosePresets storable; aborting.");
                            return;
                        }

                        // mergeLoad from clothingMode: Merge -> true, all others -> false (step 9)
                        bool mergeLoad = false;
                        if (clothingMode == ClothingApplyMode.Merge)
                        {
                            mergeLoad = true;
                        }

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

                        // PresetManager.LoadPresetFromJSON overwrites the storable's "storable" lock-state
                        // child plus loadPresetOnSelect/presetName. Snapshot before, re-apply after.
                        PresetParamsSnapshot snap = CapturePresetParamsSnapshot(targetAtom, storableName);

                        MaybeSetLastRestoredData(targetAtom, preset, updateLastRestoredData);

                        try
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                            }

                            Exception bridgeError = null;
                            MethodInfo loadMethod = typeof(MeshVR.PresetManager).GetMethod(
                                "LoadPresetFromJSON",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(JSONClass), typeof(bool) },
                                null);

                            if (loadMethod != null)
                            {
                                bool bridgeSuccess = PluginSignatureBridge.TryInvoke(
                                    loadMethod,
                                    presetManager,
                                    new object[] { preset, mergeLoad },
                                    out bridgeError,
                                    PluginSignatureBridge.DefaultFakeAssemblyName,
                                    PluginSignatureBridge.DefaultFakePluginHash);

                                if (bridgeSuccess)
                                {
                                    LogUtil.Log($"[VpbImport] Pose preset applied via bridge (mergeLoad={mergeLoad}).");
                                }
                                else
                                {
                                    LogUtil.LogWarning($"VpbImport: Bridge invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
                                }
                            }
                            else
                            {
                                LogUtil.LogWarning("VpbImport: LoadPresetFromJSON method not found on PresetManager.");
                            }
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PopLoadDir();
                            }
                        }

                        RestorePresetParamsSnapshot(targetAtom, snap);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"VpbImport: Pose dispatch caught exception: {ex.Message}");
                    }
                    break;
                }
                #endregion

                #region Slice E owns: Hair
                case VpbResourceType.Hair:
                {
                    try
                    {
                        string storableName = "HairPresets";

                        JSONStorable presetStorable = targetAtom.GetStorableByID(storableName);
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning($"VpbImport: HairPresets storable not found on target atom; aborting.");
                            return;
                        }

                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PresetManager not found in HairPresets storable; aborting.");
                            return;
                        }

                        bool mergeLoad = false;
                        if (clothingMode == ClothingApplyMode.Merge)
                        {
                            mergeLoad = true;
                        }

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

                        // PresetManager.LoadPresetFromJSON overwrites the storable's "storable" lock-state
                        // child plus loadPresetOnSelect/presetName. Snapshot before, re-apply after.
                        PresetParamsSnapshot snap = CapturePresetParamsSnapshot(targetAtom, storableName);

                        MaybeSetLastRestoredData(targetAtom, preset, updateLastRestoredData);

                        try
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                            }

                            Exception bridgeError = null;
                            MethodInfo loadMethod = typeof(MeshVR.PresetManager).GetMethod(
                                "LoadPresetFromJSON",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(JSONClass), typeof(bool) },
                                null);

                            if (loadMethod != null)
                            {
                                bool bridgeSuccess = PluginSignatureBridge.TryInvoke(
                                    loadMethod,
                                    presetManager,
                                    new object[] { preset, mergeLoad },
                                    out bridgeError,
                                    PluginSignatureBridge.DefaultFakeAssemblyName,
                                    PluginSignatureBridge.DefaultFakePluginHash);

                                if (bridgeSuccess)
                                {
                                    LogUtil.Log($"[VpbImport] Hair preset applied via bridge (mergeLoad={mergeLoad}).");
                                }
                                else
                                {
                                    LogUtil.LogWarning($"VpbImport: Bridge invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
                                }
                            }
                            else
                            {
                                LogUtil.LogWarning("VpbImport: LoadPresetFromJSON method not found on PresetManager.");
                            }
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PopLoadDir();
                            }
                        }

                        RestorePresetParamsSnapshot(targetAtom, snap);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"VpbImport: Hair dispatch caught exception: {ex.Message}");
                    }
                    break;
                }
                #endregion

                #region Slice E owns: ClothingItem
                case VpbResourceType.ClothingItem:
                {
                    // Item-level paths convert .vam/.vab to preset JSON before apply; use Clothing branch instead.
                    LogUtil.LogWarning("VpbImport: ClothingItem not yet implemented");
                    return;
                }
                #endregion

                #region Slice E owns: HairItem
                case VpbResourceType.HairItem:
                {
                    // Item-level paths convert .vam/.vab to preset JSON before apply; use Hair branch instead.
                    LogUtil.LogWarning("VpbImport: HairItem not yet implemented");
                    return;
                }
                #endregion

                #region Slice E owns: Morphs
                case VpbResourceType.Morphs:
                {
                    LogUtil.LogWarning("VpbImport: Morphs not yet implemented");
                    return;
                }
                #endregion

                #region Generic: any PresetManager-backed storable by name (Skin, Morphs, Animation, BreastPhysics, Plugins, ...)
                case VpbResourceType.General:
                {
                    try
                    {
                        if (string.IsNullOrEmpty(storableNameOverride))
                        {
                            LogUtil.LogWarning("VpbImport: General dispatch requires storableNameOverride; aborting.");
                            return;
                        }

                        string storableName = storableNameOverride;

                        JSONStorable presetStorable = targetAtom.GetStorableByID(storableName);
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning($"VpbImport: '{storableName}' storable not found on target atom; aborting.");
                            return;
                        }

                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PresetManager not found in '{storableName}' storable; aborting.");
                            return;
                        }

                        bool mergeLoad = false;
                        if (clothingMode == ClothingApplyMode.Merge)
                        {
                            mergeLoad = true;
                        }

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

                        PresetParamsSnapshot snap = CapturePresetParamsSnapshot(targetAtom, storableName);

                        MaybeSetLastRestoredData(targetAtom, preset, updateLastRestoredData);

                        try
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                            }

                            Exception bridgeError = null;
                            MethodInfo loadMethod = typeof(MeshVR.PresetManager).GetMethod(
                                "LoadPresetFromJSON",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(JSONClass), typeof(bool) },
                                null);

                            if (loadMethod != null)
                            {
                                bool bridgeSuccess = PluginSignatureBridge.TryInvoke(
                                    loadMethod,
                                    presetManager,
                                    new object[] { preset, mergeLoad },
                                    out bridgeError,
                                    PluginSignatureBridge.DefaultFakeAssemblyName,
                                    PluginSignatureBridge.DefaultFakePluginHash);

                                if (bridgeSuccess)
                                {
                                    LogUtil.Log($"[VpbImport] Generic '{storableName}' preset applied via bridge (mergeLoad={mergeLoad}).");
                                }
                                else
                                {
                                    LogUtil.LogWarning($"VpbImport: Bridge invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
                                }
                            }
                            else
                            {
                                LogUtil.LogWarning("VpbImport: LoadPresetFromJSON method not found on PresetManager.");
                            }
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(sourcePath))
                            {
                                MVR.FileManagement.FileManager.PopLoadDir();
                            }
                        }

                        RestorePresetParamsSnapshot(targetAtom, snap);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"VpbImport: General dispatch caught exception: {ex.Message}");
                    }
                    break;
                }
                #endregion

                default:
                {
                    LogUtil.LogWarning($"VpbImport: Unknown resource type {resourceType}; aborting.");
                    return;
                }
            }
        }

        #region Slice C helpers: appearance preset helpers (PresetLockStore-based)
        // Bridge required: VaM's FileManagerSecure rejects the BepInEx assembly name on its call-stack check.
        private static void InvokeLoadPresetFromJSON(MeshVR.PresetManager presetManager, JSONClass preset, bool mergeLoad)
        {
            if (presetManager == null || preset == null) return;
            MethodInfo loadMethod = typeof(MeshVR.PresetManager).GetMethod(
                "LoadPresetFromJSON",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(JSONClass), typeof(bool) },
                null);
            if (loadMethod == null)
            {
                LogUtil.LogWarning("VpbImport: LoadPresetFromJSON method not found on PresetManager.");
                return;
            }
            Exception bridgeError = null;
            bool ok = PluginSignatureBridge.TryInvoke(
                loadMethod,
                presetManager,
                new object[] { preset, mergeLoad },
                out bridgeError,
                PluginSignatureBridge.DefaultFakeAssemblyName,
                PluginSignatureBridge.DefaultFakePluginHash);
            if (ok)
                LogUtil.Log($"[VpbImport] Preset applied via bridge (mergeLoad={mergeLoad}).");
            else
                LogUtil.LogWarning($"VpbImport: Bridge invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
        }

        // One worn clothing item extracted from a person JSON (appearance preset or atom.Store dump):
        // the geometry "clothing" array entry that wears it, its positional clothingItem#N material
        // storable (may be null), and its geometry "clothing:<uid>" activation bool (may be null).
        private sealed class ClothingSliceItem
        {
            public JSONClass Entry;
            public string InternalId;
            public List<JSONClass> ItemStorables;
            public string BoolKey;
            public JSONNode BoolVal;
        }

        // Extracts worn clothing of the requested wear class (cosmetic vs real garment) from a person
        // JSON. uidMaterials/seenUidMat accumulate the non-positional (uid/asset-path) clothing material
        // storables shared across sources; those only bind to items that actually end up worn.
        private static void CollectClothingSliceItems(
            JSONClass source, bool wantCosmetic,
            List<ClothingSliceItem> items, List<JSONClass> uidMaterials, HashSet<string> seenUidMat)
        {
            if (source == null || source["storables"] == null) return;
            JSONArray storables = source["storables"].AsArray;
            if (storables == null) return;

            JSONClass geometry = null;
            List<JSONClass> allStorables = new List<JSONClass>();
            // Older/alternate presets customize via positional clothingItem#N or url-keyed material
            // storables. Keep those as a fallback binding path.
            Dictionary<int, JSONClass> positional = new Dictionary<int, JSONClass>();
            Dictionary<string, JSONClass> materialByUrl = new Dictionary<string, JSONClass>(StringComparer.OrdinalIgnoreCase);
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"] != null ? s["id"].Value : "";
                if (string.IsNullOrEmpty(id)) continue;
                if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) { geometry = s; continue; }
                if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "AppearancePresets", StringComparison.OrdinalIgnoreCase)) continue;
                allStorables.Add(s);

                int idx = ParseClothingItemIndex(id);
                if (idx >= 0)
                {
                    positional[idx] = s;
                    string murl = ExtractClothingUrlFromStorableJsonStatic(s);
                    if (!string.IsNullOrEmpty(murl) && !materialByUrl.ContainsKey(murl)) materialByUrl[murl] = s;
                    continue;
                }

                // uid/asset-path clothing material storable (exclude hair).
                if (IsClothingRelatedStorableId(id) && id.IndexOf("hair", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (seenUidMat.Add(id)) uidMaterials.Add(s);
                }
            }

            if (geometry == null || geometry["clothing"] == null || geometry["clothing"].AsArray == null) return;
            JSONArray clothing = geometry["clothing"].AsArray;

            // Normalized internalIds of ALL enabled clothing items in this source, for longest-match
            // disambiguation (a shorter internalId that prefixes a longer one must not steal storables).
            List<string> allWornNorm = new List<string>();
            for (int j = 0; j < clothing.Count; j++)
            {
                JSONClass e = clothing[j].AsObject;
                if (e == null) continue;
                if (e["enabled"] != null && string.Equals(e["enabled"].Value, "false", StringComparison.OrdinalIgnoreCase)) continue;
                string euid = e["id"] != null ? e["id"].Value : "";
                string eiid = e["internalId"] != null ? e["internalId"].Value : "";
                if (string.IsNullOrEmpty(eiid)) eiid = ClothingInternalIdFromUid(euid);
                string norm = NormalizeStorablePrefix(eiid);
                if (!string.IsNullOrEmpty(norm)) allWornNorm.Add(norm);
            }

            for (int i = 0; i < clothing.Count; i++)
            {
                JSONClass entry = clothing[i].AsObject;
                if (entry == null) continue;

                // Skip items that are present-but-off.
                if (entry["enabled"] != null
                    && string.Equals(entry["enabled"].Value, "false", StringComparison.OrdinalIgnoreCase))
                    continue;

                string uid = entry["id"] != null ? entry["id"].Value : "";
                // Three-way split for Outfit Only: real garments and ACCESSORIES (glasses, hats,
                // jewelry) load from the preset; true FACE cosmetics (eye overlays, makeup, lashes,
                // decals) are kept from the target. Accessories are cosmetic-classified but part of
                // the outfit, so they ride the garment pass (wantCosmetic=false).
                bool cosmetic = ClothingLoadingUtils.ClassifyClothingWearClass(uid) == ClothingLoadingUtils.ClothingWearClass.Cosmetic
                    || ClothingLoadingUtils.IsCosmeticClothingUidHeuristic(uid);
                bool faceCosmetic = cosmetic && !ClothingLoadingUtils.IsAccessoryClothingUidHeuristic(uid);
                if (faceCosmetic != wantCosmetic) continue;

                string internalId = entry["internalId"] != null ? entry["internalId"].Value : "";
                if (string.IsNullOrEmpty(internalId)) internalId = ClothingInternalIdFromUid(uid);

                ClothingSliceItem item = new ClothingSliceItem { Entry = entry, InternalId = internalId, ItemStorables = new List<JSONClass>() };
                // Primary binding: an item's customization (textures/colors/sim/wrap) is saved as storables
                // whose id is the item's runtime storeId plus an arbitrary control suffix. The storeId is
                // the internalId with spaces/underscores stripped (local items) or preserved (packaged),
                // so match with separators removed and preserve the storables' original ids on load.
                string normInternal = NormalizeStorablePrefix(internalId);
                if (!string.IsNullOrEmpty(normInternal))
                {
                    foreach (JSONClass s in allStorables)
                    {
                        string sid = s["id"].Value;
                        if (sid.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (StorableBelongsToItem(sid, normInternal, allWornNorm)) item.ItemStorables.Add(s);
                    }
                }
                // Fallback for presets that customize via positional/url material storables.
                if (item.ItemStorables.Count == 0)
                {
                    JSONClass mat = null;
                    if (!string.IsNullOrEmpty(uid)) materialByUrl.TryGetValue(uid, out mat);
                    if (mat == null && !string.IsNullOrEmpty(internalId)) materialByUrl.TryGetValue(internalId, out mat);
                    if (mat == null) positional.TryGetValue(i, out mat);
                    if (mat != null) item.ItemStorables.Add(mat);
                }

                if (!string.IsNullOrEmpty(uid))
                {
                    string boolKey = "clothing:" + uid;
                    if (geometry[boolKey] != null) { item.BoolKey = boolKey; item.BoolVal = geometry[boolKey]; }
                }
                items.Add(item);
            }
        }

        // A clothing item's customization storable id is its runtime storeId (internalId with
        // spaces/underscores stripped) plus an arbitrary control suffix (Material.../Sim/WrapControl/
        // ItemControl/Style/Preset...). Compare separator-stripped, case-sensitive. Rejects when a
        // longer worn internalId also prefixes the storable (that item owns it instead).
        private static bool StorableBelongsToItem(string storableId, string normInternal, List<string> allWornNorm)
        {
            string normS = NormalizeStorablePrefix(storableId);
            if (normInternal.Length == 0 || normS.Length <= normInternal.Length) return false;
            if (!normS.StartsWith(normInternal, StringComparison.Ordinal)) return false;
            foreach (string other in allWornNorm)
                if (other.Length > normInternal.Length && normS.StartsWith(other, StringComparison.Ordinal)) return false;
            return true;
        }

        // Strip spaces and underscores to compare against VaM's runtime clothing storeId, which drops
        // those separators from the item internalId.
        private static string NormalizeStorablePrefix(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (c != ' ' && c != '_') sb.Append(c);
            return sb.ToString();
        }

        // Derive an item's internalId from its uid when the geometry entry omits it: the file name
        // without directory or extension. Modern presets carry internalId explicitly.
        private static string ClothingInternalIdFromUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            int slash = uid.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? uid.Substring(slash + 1) : uid;
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            return name;
        }

        // Parses the N from a "clothingItem#N" storable id (positional material storable). Returns -1 otherwise.
        private static int ParseClothingItemIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int hash = id.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase);
            if (hash < 0) return -1;
            int p = hash + "clothingItem#".Length;
            int end = p;
            while (end < id.Length && char.IsDigit(id[end])) end++;
            if (end == p) return -1;
            int n;
            return int.TryParse(id.Substring(p, end - p), out n) ? n : -1;
        }

        // Builds a ClothingPresets-shaped slice worn as EXACTLY: the target's kept cosmetics (makeup /
        // skin overlays, from keepCosmeticsSource) followed by the preset's real garments. Positional
        // clothingItem#N material storables are reindexed to the combined worn order so textures/colors
        // land on the right items. Loaded non-merge through the ClothingPresets PresetManager.
        private static JSONClass BuildClothingOnlyPresetSlice(JSONClass preset, JSONClass keepCosmeticsSource)
        {
            List<ClothingSliceItem> items = new List<ClothingSliceItem>();
            List<JSONClass> uidMaterials = new List<JSONClass>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Keep the target's current makeup/skin-overlay clothing (honors "keep face").
            if (keepCosmeticsSource != null)
                CollectClothingSliceItems(keepCosmeticsSource, true, items, uidMaterials, seen);
            // Add the preset's real garments (the outfit being applied).
            CollectClothingSliceItems(preset, false, items, uidMaterials, seen);

            if (items.Count == 0) return null;

            JSONClass geomSlice = new JSONClass();
            geomSlice["id"] = "geometry";
            JSONArray clothingArr = new JSONArray();
            JSONArray storablesOut = new JSONArray();
            storablesOut.Add(geomSlice);

            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                ClothingSliceItem it = items[i];
                clothingArr.Add(it.Entry);
                if (it.BoolKey != null) geomSlice[it.BoolKey] = it.BoolVal;
                if (it.ItemStorables == null) continue;
                foreach (JSONClass ms in it.ItemStorables)
                {
                    string mid = ms["id"] != null ? ms["id"].Value : null;
                    if (string.IsNullOrEmpty(mid) || !emitted.Add(mid)) continue;
                    storablesOut.Add(CloneJsonClassStatic(ms));
                }
            }
            geomSlice["clothing"] = clothingArr;

            foreach (JSONClass m in uidMaterials)
            {
                string mid = m["id"] != null ? m["id"].Value : null;
                if (string.IsNullOrEmpty(mid) || !emitted.Add(mid)) continue;
                storablesOut.Add(m);
            }

            JSONClass slice = new JSONClass();
            slice["storables"] = storablesOut;
            slice["setUnlistedParamsToDefault"] = "true";
            return slice;
        }

        // Temporary diagnostic for the "Outfit Only" import path. One-shot per apply (not a hot path),
        // so it always logs. Prefix "[VPB OutfitDiag]" for easy grep in BepInEx/LogOutput.log.
        // Dumps how source clothing maps to the built ClothingPresets slice so material (texture/color)
        // binding can be verified against the real JSON field names.
        private static void DumpOutfitOnlyDiag(JSONClass preset, JSONClass keepCosmetics, JSONClass slice)
        {
            LogUtil.Log("[VPB OutfitDiag] ===== Outfit Only apply diagnostic =====");
            DumpOutfitOnlySource("PRESET(garments source)", preset);
            DumpOutfitOnlySource("KEEP(target cosmetics)", keepCosmetics);
            DumpOutfitOnlySource("SLICE(built ClothingPresets)", slice);
            LogUtil.Log("[VPB OutfitDiag] ===== end =====");
        }

        private static void DumpOutfitOnlySource(string label, JSONClass source)
        {
            if (source == null) { LogUtil.Log($"[VPB OutfitDiag] {label}: <null>"); return; }
            JSONArray storables = source["storables"] != null ? source["storables"].AsArray : null;
            if (storables == null) { LogUtil.Log($"[VPB OutfitDiag] {label}: no storables"); return; }

            JSONClass geometry = null;
            int matCount = 0;
            System.Text.StringBuilder allIds = new System.Text.StringBuilder();
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"] != null ? s["id"].Value : "";
                if (allIds.Length < 1200) { if (allIds.Length > 0) allIds.Append(" | "); allIds.Append(id); }
                if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase))
                {
                    string pn = s["presetName"] != null ? s["presetName"].Value : "<none>";
                    LogUtil.Log($"[VPB OutfitDiag] {label} ClothingPresets presetName='{pn}'");
                }
                if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) { geometry = s; continue; }
                if (!IsClothingRelatedStorableId(id)) continue;
                if (id.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string url = ExtractClothingUrlFromStorableJsonStatic(s);
                // List the customization keys present (what makes RedTexture differ from base).
                System.Text.StringBuilder keys = new System.Text.StringBuilder();
                foreach (string k in s.Keys)
                {
                    if (k == "id" || k == "url") continue;
                    if (keys.Length > 0) keys.Append(",");
                    keys.Append(k);
                    if (keys.Length > 300) { keys.Append("..."); break; }
                }
                matCount++;
                LogUtil.Log($"[VPB OutfitDiag] {label} MAT id='{id}' url='{url ?? "<none>"}' keys=[{keys}]");
            }

            if (geometry != null && geometry["clothing"] != null && geometry["clothing"].AsArray != null)
            {
                JSONArray clothing = geometry["clothing"].AsArray;
                for (int i = 0; i < clothing.Count; i++)
                {
                    JSONClass e = clothing[i].AsObject;
                    if (e == null) continue;
                    string uid = e["id"] != null ? e["id"].Value : "";
                    string internalId = e["internalId"] != null ? e["internalId"].Value : "";
                    if (string.IsNullOrEmpty(internalId)) internalId = ClothingInternalIdFromUid(uid);
                    string enabled = e["enabled"] != null ? e["enabled"].Value : "";
                    int itemStorables = 0;
                    string normInternal = NormalizeStorablePrefix(internalId);
                    if (!string.IsNullOrEmpty(normInternal))
                        foreach (JSONNode node in storables)
                        {
                            JSONClass s = node as JSONClass;
                            if (s == null || s["id"] == null) continue;
                            string sid = s["id"].Value;
                            if (sid.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            if (StorableBelongsToItem(sid, normInternal, new List<string>())) itemStorables++;
                        }
                    LogUtil.Log($"[VPB OutfitDiag] {label} CLOTH[{i}] id='{uid}' internalId='{internalId}' enabled='{enabled}' itemStorables={itemStorables}");
                }
            }
            LogUtil.Log($"[VPB OutfitDiag] {label}: storables={storables.Count} clothingMaterials={matCount}");
            LogUtil.Log($"[VPB OutfitDiag] {label} ALLIDS=[{allIds}]");
        }

        // Filter a wrapped appearance slice to the source's non-real (Cosmetic) clothing only, for the
        // only-suppress-real reinject. Returns null when there is none. Fed to the Clothing dispatch.
        internal static JSONClass BuildNonRealClothingSlice(JSONClass preset)
        {
            if (preset == null || preset["storables"] == null) return null;
            JSONClass clone = CloneJsonClassStatic(preset);
            JSONArray storables = (clone != null && clone["storables"] != null) ? clone["storables"].AsArray : null;
            if (storables == null) return null;

            bool anyNonReal = false;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                if (!string.Equals(s["id"] != null ? s["id"].Value : "", "geometry", StringComparison.OrdinalIgnoreCase)) continue;

                JSONArray clothing = s["clothing"] != null ? s["clothing"].AsArray : null;
                if (clothing == null) break;

                JSONArray filtered = new JSONArray();
                foreach (JSONNode cn in clothing)
                {
                    JSONClass item = cn as JSONClass;
                    if (item == null) continue;
                    string uid = item["id"] != null ? item["id"].Value : null;
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (ClothingLoadingUtils.ClassifyClothingWearClass(uid) == ClothingLoadingUtils.ClothingWearClass.Cosmetic)
                    {
                        filtered.Add(item);
                        anyNonReal = true;
                    }
                }
                s["clothing"] = filtered;
                break;
            }
            return anyNonReal ? clone : null;
        }

        // PluginPresets slice for a chosen subset: PluginManager "plugins" pruned to selected plugin#N keys + each
        // selected plugin's "plugin#N_*" param storable kept verbatim (RestoreFromLast binds settings via the pre-merge id).
        internal static JSONClass BuildSelectedPluginsSlice(JSONClass preset, ICollection<string> selectedPluginKeys)
        {
            if (preset == null || preset["storables"] == null) return null;
            if (selectedPluginKeys == null || selectedPluginKeys.Count == 0) return null;
            JSONClass clone = CloneJsonClassStatic(preset);
            JSONArray storables = (clone != null && clone["storables"] != null) ? clone["storables"].AsArray : null;
            if (storables == null) return null;

            JSONArray kept = new JSONArray();
            bool anyPlugin = false;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"] != null ? s["id"].Value : "";
                if (string.IsNullOrEmpty(id)) continue;

                if (string.Equals(id, "PluginManager", StringComparison.Ordinal))
                {
                    JSONClass plugins = s["plugins"] != null ? s["plugins"].AsObject : null;
                    if (plugins == null) continue;
                    JSONClass filtered = new JSONClass();
                    foreach (string key in plugins.Keys)
                        if (selectedPluginKeys.Contains(key)) filtered[key] = plugins[key];
                    if (filtered.Count == 0) continue;
                    s["plugins"] = filtered;
                    kept.Add(s);
                    anyPlugin = true;
                }
                else if (IsSelectedPluginParamStorable(id, selectedPluginKeys))
                {
                    kept.Add(s);
                }
            }
            if (!anyPlugin) return null;
            clone["storables"] = kept;
            return clone;
        }

        // Merge support: remaps a plugins slice's plugin#N keys (and matching plugin#N_* param storable ids +
        // references) for a MERGE onto a target that already has plugins.
        //   - A source plugin whose URL is already on the target (<paramref name="targetUrlToExistingKey"/>)
        //     is remapped onto that EXISTING slot and dropped from the PluginManager dict, so applying the
        //     slice updates the live plugin's settings in place instead of creating a duplicate.
        //   - A source plugin with a new URL is appended: assigned the next free slot starting at
        //     <paramref name="startNumber"/> (= target's max plugin# + 1) and kept in the dict.
        // Without the remap, VaM's append renumbering would leave the param storables bound to the wrong/
        // colliding slot and the imported plugins would land without their settings.
        internal static void MergePluginSliceKeys(JSONClass slice, int startNumber, Dictionary<string, string> targetUrlToExistingKey)
        {
            if (slice == null) return;
            JSONArray storables = slice["storables"] != null ? slice["storables"].AsArray : null;
            if (storables == null) return;

            JSONClass pmStorable = null;
            JSONClass plugins = null;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                if (s["id"] != null && s["id"].Value == "PluginManager")
                {
                    pmStorable = s;
                    plugins = s["plugins"] != null ? s["plugins"].AsObject : null;
                    break;
                }
            }
            if (pmStorable == null || plugins == null || plugins.Count == 0) return;

            // Ascending slot order = stable, contiguous numbering for the appended (new) plugins.
            List<string> oldKeys = new List<string>(plugins.Keys);
            oldKeys.Sort((a, b) => PluginSlotNumber(a).CompareTo(PluginSlotNumber(b)));

            int next = startNumber < 0 ? 0 : startNumber;
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            JSONClass rebuilt = new JSONClass();   // only NEW plugins remain in the PluginManager dict
            foreach (string ok in oldKeys)
            {
                string url = plugins[ok] != null ? plugins[ok].Value : "";
                string existing = null;
                if (targetUrlToExistingKey != null && !string.IsNullOrEmpty(url))
                    targetUrlToExistingKey.TryGetValue(url.Trim(), out existing);

                if (!string.IsNullOrEmpty(existing))
                {
                    // Already on target: update-in-place. Bind params to the live slot, do not re-add the plugin.
                    map[ok] = existing;
                }
                else
                {
                    string nk = "plugin#" + next;
                    next++;
                    map[ok] = nk;
                    rebuilt[nk] = plugins[ok];
                }
            }
            // Dropping matched plugins from the dict is itself a change even when no key was renumbered.
            pmStorable["plugins"] = rebuilt;

            // Rewrite the param storable ids + any plugin#N references for keys that actually moved, via a
            // two-phase sentinel pass: old -> unique sentinel, then sentinel -> new. The sentinel can never
            // equal an old OR new plugin key, so overlapping ranges cannot cross-corrupt regardless of order.
            List<string> changed = new List<string>();
            foreach (string ok in oldKeys)
                if (map[ok] != ok) changed.Add(ok);
            if (changed.Count == 0) return;

            const string sentPrefix = "\u0000VPBPLG#";
            const string sentSuffix = "\u0000";
            for (int i = 0; i < changed.Count; i++)
                JSONExtensions.ReplacePluginKeyTokenMutable(slice, changed[i], sentPrefix + i + sentSuffix);
            for (int i = 0; i < changed.Count; i++)
                JSONExtensions.ReplacePluginKeyTokenMutable(slice, sentPrefix + i + sentSuffix, map[changed[i]]);
        }

        // Numeric slot of a "plugin#N" key, or int.MaxValue if unparseable (sorts such keys last).
        private static int PluginSlotNumber(string key)
        {
            int h = key != null ? key.IndexOf('#') : -1;
            int n;
            return (h >= 0 && int.TryParse(key.Substring(h + 1), out n)) ? n : int.MaxValue;
        }

        // A plugin's param storable id is "<plugin#N>_<ClassName>"; keep it when plugin#N is selected.
        private static bool IsSelectedPluginParamStorable(string id, ICollection<string> selectedPluginKeys)
        {
            foreach (string key in selectedPluginKeys)
                if (!string.IsNullOrEmpty(key) && id.StartsWith(key + "_", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsClothingRelatedStorableId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (id.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsClothingAssetPathInUidStatic(id)) return true;
            return false;
        }

        // Steal carries source plugins even though the slice is otherwise clothing-only.
        private static void TryApplyPluginsFromSource(MeshVR.PresetManager presetManager, JSONClass sourcePreset)
        {
            if (presetManager == null || sourcePreset == null || sourcePreset["storables"] == null) return;
            JSONArray storables = sourcePreset["storables"].AsArray;
            if (storables == null) return;
            JSONClass pluginNode = null;
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s != null && s["id"] != null && s["id"].Value == "PluginManager")
                {
                    pluginNode = s;
                    break;
                }
            }
            if (pluginNode == null) return;

            JSONClass pluginsOnly = new JSONClass();
            JSONArray pluginsArr = new JSONArray();
            pluginsArr.Add(pluginNode);
            pluginsOnly["storables"] = pluginsArr;

            try { InvokeLoadPresetFromJSON(presetManager, pluginsOnly, mergeLoad: false); }
            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: Plugins sub-preset failed: {ex.Message}"); }
        }

        private static void ClearAllClothingHairBools(Atom targetAtom)
        {
            if (targetAtom == null) return;
            JSONStorable geometry = targetAtom.GetStorableByID("geometry");
            if (geometry == null) return;
            List<string> boolNames = geometry.GetBoolParamNames();
            if (boolNames == null) return;
            foreach (string boolName in boolNames)
            {
                if (boolName.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)
                    || boolName.StartsWith("hair", StringComparison.OrdinalIgnoreCase))
                {
                    JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                    if (b != null) b.val = false;
                }
            }
        }

        private static JSONClass CloneJsonClassStatic(JSONClass jc)
        {
            if (jc == null) return null;
            try { return JSON.Parse(JsonSerializationUtil.Serialize(jc, 8192)).AsObject; }
            catch { return jc; }
        }

        private static bool IsClothingItemStorableIdStatic(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            return sid.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0
                || sid.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractClothingUrlFromStorableJsonStatic(JSONClass jc)
        {
            if (jc == null) return null;
            try
            {
                if (jc["url"] != null && !string.IsNullOrEmpty(jc["url"].Value)) return jc["url"].Value;
            }
            catch { }
            return null;
        }

        #endregion

        private static bool IsClothingAssetPathInUidStatic(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            int colon = uid.IndexOf(':');
            string pathPart = colon >= 0 ? uid.Substring(colon + 1) : uid;
            if (pathPart.IndexOf("/custom/clothing/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (pathPart.IndexOf("\\custom\\clothing\\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        #region Slice D helpers
        private static JSONClass ExtractAtomFromSceneHelper(JSONClass sceneJSON, string atomType)
        {
            if (sceneJSON == null || sceneJSON["atoms"] == null) return null;

            JSONArray atoms = sceneJSON["atoms"].AsArray;
            if (atoms == null) return null;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass atom = atoms[i].AsObject;
                if (atom == null) continue;
                if (atom["type"] != null && atom["type"].Value == atomType)
                {
                    JSONClass extracted = new JSONClass();
                    extracted["storables"] = atom["storables"];
                    if (atom["setUnlistedParamsToDefault"] != null)
                        extracted["setUnlistedParamsToDefault"] = atom["setUnlistedParamsToDefault"];
                    return extracted;
                }
            }
            return null;
        }

        private static void CleanPresetsHelper(JSONClass preset)
        {
            if (preset == null) return;

            // Strip position/rotation from control storable
            JSONArray storables = preset["storables"] != null ? preset["storables"].AsArray : null;
            if (storables != null)
            {
                for (int i = 0; i < storables.Count; i++)
                {
                    JSONClass s = storables[i].AsObject;
                    if (s == null) continue;

                    if (s["id"] != null && s["id"].Value == "control")
                    {
                        if (s.HasKey("position")) s.Remove("position");
                        if (s.HasKey("rotation")) s.Remove("rotation");
                    }

                    // Clean presets arrays in PosePresets or control
                    if ((s["id"] != null && (s["id"].Value == "PosePresets" || s["id"].Value == "control"))
                        && s["presets"] != null)
                    {
                        CleanPresetsArrayHelper(s["presets"].AsArray);
                    }
                }
            }
            else if (preset["presets"] != null)
            {
                CleanPresetsArrayHelper(preset["presets"].AsArray);
            }
        }

        // VaM's primary body controls default to positionState/rotationState = On, so a pose save OMITS those
        // keys for them (only NON-default states like the deliberately-enabled pelvis/toe controls get written).
        // On a fresh scene load that is fine, but our merge-load applies the pose onto an EXISTING person whose
        // foot controls may be Off / hip Comply — and native merge-load leaves a control's state untouched when
        // the JSON omits it. Result: feet unpinned, toes curl. This restores the fresh-load intent by injecting
        // On into exactly the primary controls when (and only when) the pose omits their state — an explicit
        // Off/Comply written by the author is present in the JSON and therefore never overridden.
        private static readonly string[] PrimaryPoseControlIds =
            { "hipControl", "chestControl", "headControl", "rHandControl", "lHandControl", "rFootControl", "lFootControl" };

        private static void EnsurePrimaryPoseControlStatesHelper(JSONClass preset)
        {
            if (preset == null) return;
            JSONArray storables = preset["storables"] != null ? preset["storables"].AsArray : null;
            if (storables == null) return;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i].AsObject;
                if (s == null || s["id"] == null) continue;
                string id = s["id"].Value;

                bool isPrimary = false;
                for (int k = 0; k < PrimaryPoseControlIds.Length; k++)
                    if (PrimaryPoseControlIds[k] == id) { isPrimary = true; break; }
                if (!isPrimary) continue;

                if (!s.HasKey("positionState")) { s["positionState"] = "On"; }
                if (!s.HasKey("rotationState")) { s["rotationState"] = "On"; }
            }
        }

        private static void CleanPresetsArrayHelper(JSONArray presets)
        {
            if (presets == null) return;
            for (int j = 0; j < presets.Count; j++)
            {
                JSONClass p = presets[j].AsObject;
                if (p != null && p["id"] != null && p["id"].Value == "control")
                {
                    if (p.HasKey("position")) p.Remove("position");
                    if (p.HasKey("rotation")) p.Remove("rotation");
                }
            }
        }
        #endregion

        #region Scene-atom helpers
        /// <summary>
        /// Wraps a single scene-atom JSON node (shape: {id, type, storables, ...}) as a preset JSON
        /// (shape: {storables, setUnlistedParamsToDefault?}) consumable by PresetManager.LoadPresetFromJSON.
        /// Used by callers that extract a Person from a scene dump and apply it as an Appearance/Clothing preset.
        /// </summary>
        internal static JSONClass WrapAtomNodeAsPreset(JSONClass atomNode)
        {
            if (atomNode == null) return null;
            JSONClass preset = new JSONClass();
            if (atomNode["storables"] != null)
            {
                preset["storables"] = atomNode["storables"];
            }
            if (atomNode["setUnlistedParamsToDefault"] != null)
            {
                preset["setUnlistedParamsToDefault"] = atomNode["setUnlistedParamsToDefault"];
            }
            return preset;
        }
        #endregion

        static void MaybeSetLastRestoredData(Atom atom, JSONClass preset, bool updateLastRestoredData)
        {
            if (!updateLastRestoredData || atom == null || preset == null) return;
            try { atom.SetLastRestoredData(preset, true, true); } catch { }
        }

        #region Slice G helpers — preset-params snapshot for non-Appearance branches
        /// <summary>
        /// Snapshot of preset-storable state that PresetManager.LoadPresetFromJSON overwrites as a side effect.
        /// The "storable" JSON child of any *Presets storable holds the PresetManager's lock state for that storable.
        /// </summary>
        internal sealed class PresetParamsSnapshot
        {
            public string StorableName;
            public JSONClass LockStore;
            public bool LoadPresetOnSelect;
            public string PresetName = "";
        }

        internal static PresetParamsSnapshot CapturePresetParamsSnapshot(Atom atom, string storableName)
        {
            var snap = new PresetParamsSnapshot { StorableName = storableName };
            if (atom == null || string.IsNullOrEmpty(storableName)) return snap;

            try
            {
                JSONStorable st = atom.GetStorableByID(storableName);
                if (st == null) return snap;

                JSONClass full = st.GetJSON();
                if (full != null && full["storable"] != null)
                {
                    snap.LockStore = CloneJsonClassStatic(full["storable"].AsObject);
                }

                JSONStorableBool lpos = st.GetBoolJSONParam("loadPresetOnSelect");
                if (lpos != null) snap.LoadPresetOnSelect = lpos.val;

                JSONStorableString ps = st.GetStringJSONParam("presetName");
                if (ps != null) snap.PresetName = ps.val;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VpbImport] CapturePresetParamsSnapshot failed for {storableName}: {ex.Message}");
            }
            return snap;
        }

        internal static void RestorePresetParamsSnapshot(Atom atom, PresetParamsSnapshot snap)
        {
            if (atom == null || snap == null || string.IsNullOrEmpty(snap.StorableName)) return;

            try
            {
                JSONStorable st = atom.GetStorableByID(snap.StorableName);
                if (st == null) return;

                if (snap.LockStore != null)
                {
                    JSONClass full = st.GetJSON();
                    if (full != null)
                    {
                        full["storable"] = CloneJsonClassStatic(snap.LockStore);
                        st.RestoreFromJSON(full);
                    }
                }

                JSONStorableBool lpos = st.GetBoolJSONParam("loadPresetOnSelect");
                if (lpos != null) lpos.val = snap.LoadPresetOnSelect;

                JSONStorableString ps = st.GetStringJSONParam("presetName");
                if (ps != null) ps.val = snap.PresetName;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VpbImport] RestorePresetParamsSnapshot failed for {snap.StorableName}: {ex.Message}");
            }
        }
        #endregion
    }
}
