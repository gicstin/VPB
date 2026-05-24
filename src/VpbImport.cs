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
        General
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
            bool updateLastRestoredData = true)
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
                        if (VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange)
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
                            // mergeLoad=true forces setUnlistedParamsToDefault=false in VaM, so body/morph/hair stay put.
                            try { ClearNonCosmeticClothing(targetAtom); }
                            catch (Exception ex) { LogUtil.LogWarning($"VpbImport: ClothingOnly pre-cleanup failed: {ex.Message}"); }

                            JSONClass slice = BuildClothingOnlyPresetSlice(preset);
                            if (slice == null)
                            {
                                LogUtil.LogWarning("VpbImport: ClothingOnly slice empty; source preset has no clothing storables.");
                                if (lpos != null) lpos.val = lposPre;
                                if (psName != null) psName.val = psNamePre;
                                break;
                            }

                            MaybeSetLastRestoredData(targetAtom, slice, updateLastRestoredData);

                            try
                            {
                                if (!string.IsNullOrEmpty(sourcePath))
                                    MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(sourcePath));
                                InvokeLoadPresetFromJSON(presetManager, slice, mergeLoad: true);
                            }
                            finally
                            {
                                if (!string.IsNullOrEmpty(sourcePath))
                                    MVR.FileManagement.FileManager.PopLoadDir();
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
                        if (clothingMode == ClothingApplyMode.Keep && targetAtom.type == "Person")
                        {
                            lockStore = new PresetLockStore();
                            lockStore.StorePresetLocks(targetAtom, clearAllLocks: true, lockClothingPreset: true, lockMorphPreset: false);
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

        private static JSONClass BuildClothingOnlyPresetSlice(JSONClass preset)
        {
            if (preset == null || preset["storables"] == null) return null;
            JSONArray storables = preset["storables"].AsArray;
            if (storables == null) return null;

            JSONArray filtered = new JSONArray();
            foreach (JSONNode node in storables)
            {
                JSONClass s = node as JSONClass;
                if (s == null) continue;
                string id = s["id"] != null ? s["id"].Value : "";
                if (IsClothingRelatedStorableId(id))
                {
                    filtered.Add(s);
                    continue;
                }
                if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase))
                {
                    JSONClass geomSlice = ExtractClothingKeysFromGeometry(s);
                    if (geomSlice != null) filtered.Add(geomSlice);
                }
            }
            if (filtered.Count == 0) return null;
            JSONClass slice = new JSONClass();
            slice["storables"] = filtered;
            return slice;
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

        private static JSONClass ExtractClothingKeysFromGeometry(JSONClass geometry)
        {
            if (geometry == null) return null;
            JSONClass slice = new JSONClass();
            bool any = false;
            foreach (string key in geometry.Keys)
            {
                if (key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                {
                    slice[key] = geometry[key];
                    any = true;
                }
            }
            if (!any) return null;
            slice["id"] = "geometry";
            return slice;
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

        private static void ClearNonCosmeticClothing(Atom targetAtom)
        {
            if (targetAtom == null) return;
            JSONStorable geometry = targetAtom.GetStorableByID("geometry");
            if (geometry == null) return;
            List<string> boolNames = geometry.GetBoolParamNames();
            if (boolNames == null) return;
            foreach (string boolName in boolNames)
            {
                if (!boolName.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                string uid = boolName.Substring("clothing:".Length);
                if (ClothingLoadingUtils.IsCosmeticClothingUidHeuristic(uid)) continue;
                JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                if (b != null) b.val = false;
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
