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
                        else
                        {
                            LogUtil.Log("[VPB Scale] core suppress=OFF preset scale will load");
                        }

                        KeepGarmentAppearanceState keepGarmentSnapshot = null;
                        KeepBodyAppearanceState keepBodySnapshot = null;
                        JSONClass preLoadLockStore = null;
                        bool preLoadLoadPresetOnSelect = false;
                        string preLoadPresetName = "";

                        if (clothingMode == ClothingApplyMode.Keep)
                        {
                            keepGarmentSnapshot = BuildKeepGarmentAppearanceState(targetAtom);
                        }
                        else if (clothingMode == ClothingApplyMode.ClothingOnly)
                        {
                            keepBodySnapshot = BuildKeepBodyAppearanceState(targetAtom);
                        }

                        // Lock state lives in the AppearancePresets storable's "storable" JSON child.
                        // Clone it now and re-merge after the apply, because PresetManager overwrites it.
                        try
                        {
                            JSONStorable appearanceStorable = targetAtom.GetStorableByID("AppearancePresets");
                            if (appearanceStorable != null)
                            {
                                JSONClass appearanceJson = appearanceStorable.GetJSON();
                                if (appearanceJson != null && appearanceJson["storable"] != null)
                                {
                                    preLoadLockStore = CloneJsonClassStatic(appearanceJson["storable"].AsObject);
                                }

                                JSONStorableBool lpos = appearanceStorable.GetBoolJSONParam("loadPresetOnSelect");
                                if (lpos != null) preLoadLoadPresetOnSelect = lpos.val;

                                JSONStorableString ps = appearanceStorable.GetStringJSONParam("presetName");
                                if (ps != null) preLoadPresetName = ps.val;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogWarning($"VpbImport: Failed to capture lock store / preset params: {ex.Message}");
                        }

                        string storableName = "AppearancePresets";

                        JSONStorable presetStorable = targetAtom.GetStorableByID(storableName);
                        if (presetStorable == null)
                        {
                            LogUtil.LogWarning($"VpbImport: AppearancePresets storable not found on target atom; aborting.");
                            return;
                        }

                        MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                        if (presetManager == null)
                        {
                            LogUtil.LogWarning($"VpbImport: PresetManager not found in AppearancePresets storable; aborting.");
                            return;
                        }

                        bool mergeLoad = false;
                        if (clothingMode == ClothingApplyMode.Merge)
                        {
                            mergeLoad = true;
                        }

                        if (clothingMode == ClothingApplyMode.Replace)
                        {
                            try
                            {
                                JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                                if (geometry != null)
                                {
                                    List<string> boolNames = geometry.GetBoolParamNames();
                                    if (boolNames != null)
                                    {
                                        foreach (string boolName in boolNames)
                                        {
                                            if (boolName.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                            {
                                                JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                                                if (b != null) b.val = false;
                                            }
                                            else if (boolName.StartsWith("hair", StringComparison.OrdinalIgnoreCase))
                                            {
                                                JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                                                if (b != null) b.val = false;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Replace mode pre-cleanup failed: {ex.Message}");
                            }
                        }
                        else if (clothingMode == ClothingApplyMode.ClothingOnly)
                        {
                            try
                            {
                                JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                                if (geometry != null)
                                {
                                    List<string> boolNames = geometry.GetBoolParamNames();
                                    if (boolNames != null)
                                    {
                                        foreach (string boolName in boolNames)
                                        {
                                            if (boolName.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                            {
                                                string uid = boolName.Substring("clothing:".Length);
                                                if (!IsCosmeticClothingUidHeuristicStatic(uid))
                                                {
                                                    JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                                                    if (b != null) b.val = false;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: ClothingOnly mode pre-cleanup failed: {ex.Message}");
                            }
                        }

                        JSONArray storables = preset["storables"] != null ? preset["storables"].AsArray : null;
                        if (storables != null)
                        {
                            preset["setUnlistedParamsToDefault"].AsBool =
                                clothingMode != ClothingApplyMode.ClothingOnly;
                        }

                        string sourcePath = sourceEntry != null ? sourceEntry.Uid : "";

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
                                    LogUtil.Log($"[VpbImport] Appearance preset applied via bridge (mergeLoad={mergeLoad}).");
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

                        if (keepGarmentSnapshot != null)
                        {
                            try
                            {
                                ApplyKeepGarmentAppearanceRestore(targetAtom, keepGarmentSnapshot, "sync");
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Sync post-load garment restore failed: {ex.Message}");
                            }
                        }

                        if (keepBodySnapshot != null)
                        {
                            try
                            {
                                JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                                if (geometry != null)
                                {
                                    keepBodySnapshot.ClothingBoolsFromPreset = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                                    List<string> boolNames = geometry.GetBoolParamNames();
                                    if (boolNames != null)
                                    {
                                        foreach (string boolName in boolNames)
                                        {
                                            if (boolName.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                            {
                                                JSONStorableBool b = geometry.GetBoolJSONParam(boolName);
                                                if (b != null) keepBodySnapshot.ClothingBoolsFromPreset[boolName] = b.val;
                                            }
                                        }
                                    }
                                }

                                ApplyClothingOnlyBodyRestore(targetAtom, keepBodySnapshot, "post-load");
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Sync post-load body restore failed: {ex.Message}");
                            }
                        }

                        if (preLoadLockStore != null)
                        {
                            try
                            {
                                JSONStorable appearanceStorable = targetAtom.GetStorableByID("AppearancePresets");
                                if (appearanceStorable != null)
                                {
                                    JSONClass appearanceJson = appearanceStorable.GetJSON();
                                    if (appearanceJson != null)
                                    {
                                        appearanceJson["storable"] = CloneJsonClassStatic(preLoadLockStore);
                                        appearanceStorable.RestoreFromJSON(appearanceJson);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Failed to restore lock store: {ex.Message}");
                            }
                        }

                        try
                        {
                            JSONStorable appearanceStorable = targetAtom.GetStorableByID("AppearancePresets");
                            if (appearanceStorable != null)
                            {
                                JSONStorableBool lpos = appearanceStorable.GetBoolJSONParam("loadPresetOnSelect");
                                if (lpos != null) lpos.val = preLoadLoadPresetOnSelect;

                                JSONStorableString ps = appearanceStorable.GetStringJSONParam("presetName");
                                if (ps != null) ps.val = preLoadPresetName;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogWarning($"VpbImport: Failed to restore preset params: {ex.Message}");
                        }

                        if (keepGarmentSnapshot != null)
                        {
                            try
                            {
                                SuperController.singleton.StartCoroutine(
                                    DelayedKeepGarmentAppearanceRestoreAtom(targetAtom.uid, keepGarmentSnapshot));
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Failed to schedule delayed garment restore: {ex.Message}");
                            }
                        }

                        if (clothingMode == ClothingApplyMode.ClothingOnly && keepBodySnapshot != null)
                        {
                            try
                            {
                                SuperController.singleton.StartCoroutine(
                                    DelayedClothingOnlyBodyRestoreAtom(targetAtom.uid, keepBodySnapshot));
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Failed to schedule delayed body restore: {ex.Message}");
                            }
                        }

                        if (clothingMode == ClothingApplyMode.ClothingOnly && storables != null)
                        {
                            try
                            {
                                bool hasPlugins = false;
                                foreach (JSONNode node in storables)
                                {
                                    JSONClass storable = node as JSONClass;
                                    if (storable != null && storable["id"] != null)
                                    {
                                        string id = storable["id"].Value;
                                        if (id == "PluginManager")
                                        {
                                            hasPlugins = true;
                                            break;
                                        }
                                    }
                                }

                                if (hasPlugins)
                                {
                                    JSONClass pluginsOnlyPreset = new JSONClass();
                                    JSONArray pluginsOnlyStorables = new JSONArray();
                                    foreach (JSONNode node in storables)
                                    {
                                        JSONClass storable = node as JSONClass;
                                        if (storable != null && storable["id"] != null)
                                        {
                                            string id = storable["id"].Value;
                                            if (id == "PluginManager")
                                            {
                                                pluginsOnlyStorables.Add(storable);
                                                break;
                                            }
                                        }
                                    }
                                    pluginsOnlyPreset["storables"] = pluginsOnlyStorables;

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
                                            new object[] { pluginsOnlyPreset, false },
                                            out bridgeError,
                                            PluginSignatureBridge.DefaultFakeAssemblyName,
                                            PluginSignatureBridge.DefaultFakePluginHash);

                                        if (bridgeSuccess)
                                        {
                                            LogUtil.Log($"[VpbImport] Plugins-only sub-preset applied.");
                                        }
                                        else
                                        {
                                            LogUtil.LogWarning($"VpbImport: Plugins-only sub-preset invoke failed: {(bridgeError != null ? bridgeError.Message : "unknown error")}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Plugins-only sub-preset handling failed: {ex.Message}");
                            }
                        }

                        if (targetAtom.type == "Person")
                        {
                            try
                            {
                                SceneLoadingUtils.SchedulePostPersonApplyFixup(targetAtom);
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogWarning($"VpbImport: Post-apply fixup failed: {ex.Message}");
                            }
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

        #region Slice C helpers — state structs and appearance restore functions
        /// <summary>Snapshot for "keep garments" appearance apply: body clothes only; hair/makeup load from preset.</summary>
        internal sealed class KeepGarmentAppearanceState
        {
            public Dictionary<string, bool> GarmentGeometryBools;
            public List<KeyValuePair<string, JSONClass>> GarmentClothingItemJsons;
            /// <summary>ClothingPresets storable: which sub-preset / variant is active per item.</summary>
            public JSONClass ClothingPresetsJson;
        }

        /// <summary>Snapshot for "clothes only" appearance apply: captures body/morphs/hair to restore after applying the full preset (clothing comes from preset).</summary>
        internal sealed class KeepBodyAppearanceState
        {
            /// <summary>geometry storable JSON with <c>clothing:</c> keys stripped — only body/morph/hair params kept.</summary>
            public JSONClass BodyGeometryJson;
            /// <summary>HairPresets storable JSON to restore original hair after the full preset is applied.</summary>
            public JSONClass HairPresetsJson;
            /// <summary>
            /// Clothing bool state captured from the atom's geometry immediately after the full preset is applied.
            /// Merged back into <see cref="BodyGeometryJson"/> during the body restore so that VaM's
            /// <c>setMissingToDefault=true</c> does not reset the newly-applied clothing to off.
            /// </summary>
            public Dictionary<string, bool> ClothingBoolsFromPreset = null;
        }

        internal static KeepGarmentAppearanceState BuildKeepGarmentAppearanceState(Atom atom)
        {
            var state = new KeepGarmentAppearanceState
            {
                GarmentGeometryBools = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                GarmentClothingItemJsons = new List<KeyValuePair<string, JSONClass>>()
            };
            if (atom == null) return state;

            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry != null)
            {
                List<string> names = geometry.GetBoolParamNames();
                if (names != null)
                {
                    foreach (string key in names)
                    {
                        if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                        string uid = key.Substring("clothing:".Length);
                        if (!IsGarmentClothingUidStatic(uid)) continue;
                        JSONStorableBool b = geometry.GetBoolJSONParam(key);
                        if (b != null) state.GarmentGeometryBools[key] = b.val;
                    }
                }
            }

            try
            {
                foreach (string sid in atom.GetStorableIDs())
                {
                    if (!IsClothingItemStorableIdStatic(sid)) continue;
                    JSONStorable st = atom.GetStorableByID(sid);
                    if (st == null) continue;
                    JSONClass jc = null;
                    try { jc = st.GetJSON(); } catch { continue; }
                    string url = ExtractClothingUrlFromStorableJsonStatic(jc);
                    if (string.IsNullOrEmpty(url) || !IsGarmentClothingUidStatic(url)) continue;
                    bool on = false;
                    if (geometry != null)
                    {
                        foreach (var kv in state.GarmentGeometryBools)
                        {
                            if (!kv.Value) continue;
                            string gUid = kv.Key.Substring("clothing:".Length);
                            if (ClothingGeometryUrlsLikelyMatchStatic(url, gUid)) { on = true; break; }
                        }
                    }
                    if (!on) continue;
                    state.GarmentClothingItemJsons.Add(new KeyValuePair<string, JSONClass>(sid, CloneJsonClassStatic(jc)));
                }
            }
            catch { }

            try
            {
                JSONStorable cps = atom.GetStorableByID("ClothingPresets");
                if (cps != null) state.ClothingPresetsJson = CloneJsonClassStatic(cps.GetJSON());
            }
            catch { }

            return state;
        }

        internal static void ApplyKeepGarmentAppearanceRestore(Atom atom, KeepGarmentAppearanceState state, string phase)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.name; } catch { }
            if (state.GarmentGeometryBools != null && state.GarmentGeometryBools.Count > 0)
            {
                JSONStorable geo = atom.GetStorableByID("geometry");
                if (geo == null)
                {
                    LogUtil.LogWarning($"[DragDropDebug][KeepGarment] {phase}: geometry storable missing atom={atomLabel}");
                }
                else
                {
                    int gOk = 0, gMiss = 0;
                    foreach (var kv in state.GarmentGeometryBools)
                    {
                        JSONStorableBool b = geo.GetBoolJSONParam(kv.Key);
                        if (b != null) { b.val = kv.Value; gOk++; }
                        else
                        {
                            gMiss++;
                            LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: geometry param missing key={kv.Key}");
                        }
                    }
                    LogUtil.Log($"[DragDropDebug][KeepGarment] {phase}: garmentGeometryBools applied={gOk} missingParam={gMiss} atom={atomLabel}");
                }
            }

            RestoreKeepGarmentClothingPresetsAndItemsStatic(atom, state, phase);
        }

        internal static IEnumerator DelayedKeepGarmentAppearanceRestoreAtom(string atomUid, KeepGarmentAppearanceState state)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(atomUid) || state == null) yield break;
            try
            {
                Atom a = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
                if (a != null)
                {
                    LogUtil.Log("[DragDropDebug][KeepGarment] delayed: second pass (3 frames after appearance load)");
                    RestoreKeepGarmentClothingPresetsAndItemsStatic(a, state, "delayed");
                }
                else
                {
                    LogUtil.LogWarning("[DragDropDebug][KeepGarment] delayed: atom not found uid=" + atomUid);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug][KeepGarment] DelayedKeepGarmentAppearanceRestore: " + ex.Message);
            }
        }

        internal static KeepBodyAppearanceState BuildKeepBodyAppearanceState(Atom atom)
        {
            if (atom == null) return null;
            var state = new KeepBodyAppearanceState();

            JSONStorable geom = null;
            try { geom = atom.GetStorableByID("geometry"); } catch { }
            if (geom != null)
            {
                JSONClass fullGeom = null;
                try { fullGeom = geom.GetJSON()?.AsObject; } catch { }
                if (fullGeom != null)
                {
                    JSONClass bodyGeom = CloneJsonClassStatic(fullGeom);
                    if (bodyGeom != null)
                    {
                        var keysToRemove = new List<string>();
                        foreach (string key in bodyGeom.Keys)
                        {
                            if (key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                keysToRemove.Add(key);
                        }
                        foreach (string k in keysToRemove)
                            try { bodyGeom.Remove(k); } catch { }
                        state.BodyGeometryJson = bodyGeom;
                    }
                }
            }

            JSONStorable hairPresets = null;
            try { hairPresets = atom.GetStorableByID("HairPresets"); } catch { }
            if (hairPresets != null)
                try { state.HairPresetsJson = hairPresets.GetJSON()?.AsObject; } catch { }

            string atomLabel = "<atom>";
            try { atomLabel = atom.uid; } catch { }
            int nBodyKeys = 0;
            try { nBodyKeys = state.BodyGeometryJson != null ? state.BodyGeometryJson.Keys.Count() : 0; } catch { }
            LogUtil.Log($"[DragDropDebug][ClothingOnly] Body snapshot built: atom={atomLabel} bodyGeomKeys={nBodyKeys} hairPresets={(state.HairPresetsJson != null ? "yes" : "no")}");
            return state;
        }

        internal static void ApplyClothingOnlyBodyRestore(Atom atom, KeepBodyAppearanceState state, string phase)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.uid; } catch { }

            JSONStorable geom = null;
            if (state.BodyGeometryJson != null)
            {
                try { geom = atom.GetStorableByID("geometry"); } catch { }
                if (geom != null)
                {
                    string origChar = null;
                    string currentChar = null;
                    try { origChar = state.BodyGeometryJson["character"]?.Value; } catch { }
                    try
                    {
                        JSONNode gj = geom.GetJSON();
                        if (gj != null) currentChar = gj["character"]?.Value;
                    }
                    catch { }
                    bool charWillChange = !string.IsNullOrEmpty(origChar) && !string.IsNullOrEmpty(currentChar) && origChar != currentChar;
                    LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: char orig={origChar ?? "null"} current={currentChar ?? "null"} willChange={charWillChange}");

                    try
                    {
                        geom.RestoreFromJSON(state.BodyGeometryJson, true, true, null, true);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: geometry restore failed atom={atomLabel} — {ex.Message}");
                    }

                    int cleared = 0;
                    try
                    {
                        foreach (string bname in geom.GetBoolParamNames())
                        {
                            if (!bname.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                            JSONStorableBool jsb = null;
                            try { jsb = geom.GetBoolJSONParam(bname); } catch { }
                            if (jsb != null && jsb.val) { jsb.val = false; cleared++; }
                        }
                    }
                    catch { }

                    int reapplied = 0;
                    int skipped = 0;
                    int skippedCosmetic = 0;
                    if (state.ClothingBoolsFromPreset != null && state.ClothingBoolsFromPreset.Count > 0)
                    {
                        foreach (var kv in state.ClothingBoolsFromPreset)
                        {
                            if (!kv.Value) continue;
                            string uid = kv.Key.Length > 9 ? kv.Key.Substring(9) : kv.Key;
                            if (IsCosmeticClothingUidHeuristicStatic(uid)) { skippedCosmetic++; continue; }
                            try
                            {
                                JSONStorableBool jsb = geom.GetBoolJSONParam(kv.Key);
                                if (jsb != null) { jsb.val = true; reapplied++; }
                                else skipped++;
                            }
                            catch { skipped++; }
                        }
                    }

                    int postRestoreActive = 0;
                    try
                    {
                        foreach (string bn in geom.GetBoolParamNames())
                        {
                            if (!bn.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                            JSONStorableBool jsb2 = null;
                            try { jsb2 = geom.GetBoolJSONParam(bn); } catch { }
                            if (jsb2 != null && jsb2.val) postRestoreActive++;
                        }
                    }
                    catch { }
                    LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: body geometry restored, cleared={cleared} reapplied={reapplied} skippedCosmetic={skippedCosmetic} skipped={skipped} activeAfterRestore={postRestoreActive} atom={atomLabel}");
                }
                else
                {
                    LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: geometry storable not found atom={atomLabel}");
                }
            }

            if (state.HairPresetsJson != null)
            {
                JSONStorable hairPresets = null;
                try { hairPresets = atom.GetStorableByID("HairPresets"); } catch { }
                if (hairPresets != null)
                {
                    try
                    {
                        hairPresets.RestoreFromJSON(state.HairPresetsJson);
                        LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: HairPresets restored atom={atomLabel}");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: HairPresets restore failed atom={atomLabel} — {ex.Message}");
                    }
                }
            }
        }

        internal static IEnumerator DelayedClothingOnlyBodyRestoreAtom(string atomUid, KeepBodyAppearanceState state)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(atomUid) || state == null) yield break;
            try
            {
                Atom a = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
                if (a != null)
                {
                    LogUtil.Log("[DragDropDebug][ClothingOnly] delayed: second body-restore pass (3 frames after preset load)");
                    ApplyClothingOnlyBodyRestore(a, state, "delayed");
                }
                else
                {
                    LogUtil.LogWarning("[DragDropDebug][ClothingOnly] delayed: atom not found uid=" + atomUid);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug][ClothingOnly] DelayedClothingOnlyBodyRestore: " + ex.Message);
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

        private static bool ClothingGeometryUrlsLikelyMatchStatic(string storableUrl, string geometryUid)
        {
            if (string.IsNullOrEmpty(storableUrl) || string.IsNullOrEmpty(geometryUid)) return false;
            if (string.Equals(storableUrl, geometryUid, StringComparison.OrdinalIgnoreCase)) return true;
            if (storableUrl.IndexOf(geometryUid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (geometryUid.IndexOf(storableUrl, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsGarmentClothingUidStatic(string itemUid)
        {
            if (string.IsNullOrEmpty(itemUid)) return false;
            if (IsCosmeticClothingUidHeuristicStatic(itemUid)) return false;
            try
            {
                VarFileEntry e = FileManager.GetVarFileEntry(itemUid);
                if (e != null)
                {
                    HashSet<string> regions = GetClothingRegionsStatic(e);
                    if (regions != null && regions.Count > 0)
                    {
                        foreach (string r in regions)
                        {
                            if (GarmentClothingRegionTagsStatic.Contains(r)) return true;
                        }
                        return false;
                    }
                }
            }
            catch { }
            if (IsClothingAssetPathInUidStatic(itemUid)) return true;
            return IsGarmentClothingUidHeuristicPositiveStatic(itemUid);
        }

        private static bool IsCosmeticClothingUidHeuristicStatic(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            string s = uid.ToLowerInvariant();
            for (int i = 0; i < S_cosmeticKeywordsStatic.Length; i++)
            {
                if (s.Contains(S_cosmeticKeywordsStatic[i])) return true;
            }
            int colonIdx = s.IndexOf(':');
            string packagePart = colonIdx >= 0 ? s.Substring(0, colonIdx) : s;
            string[] segs = packagePart.Split(new char[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segs.Length; i++)
            {
                string seg = segs[i];
                if (seg == "eye" || seg == "eyes") return true;
                if (seg.StartsWith("eye") && seg != "eyelet") return true;
            }
            return false;
        }

        private static bool IsGarmentClothingUidHeuristicPositiveStatic(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            string s = uid.ToLowerInvariant();
            for (int i = 0; i < S_garmentKeywordsStatic.Length; i++)
            {
                if (s.Contains(S_garmentKeywordsStatic[i])) return true;
            }
            return false;
        }

        private static bool IsClothingAssetPathInUidStatic(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            int colon = uid.IndexOf(':');
            string pathPart = colon >= 0 ? uid.Substring(colon + 1) : uid;
            if (pathPart.IndexOf("/custom/clothing/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (pathPart.IndexOf("\\custom\\clothing\\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static HashSet<string> GetClothingRegionsStatic(VarFileEntry e)
        {
            if (e == null) return null;
            try
            {
                using (var sr = e.OpenStreamReader())
                {
                    if (sr == null) return null;
                    var json = sr.ReadToEnd();
                    var jc = JSON.Parse(json) as JSONClass;
                    if (jc != null && jc["metadata"] != null)
                    {
                        var meta = jc["metadata"].AsObject;
                        if (meta != null && meta["tags"] != null)
                        {
                            var arr = meta["tags"].AsArray;
                            var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (arr != null)
                            {
                                foreach (JSONNode node in arr)
                                {
                                    var tag = node.Value;
                                    if (!string.IsNullOrEmpty(tag)) regions.Add(tag);
                                }
                            }
                            return regions;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void RestoreKeepGarmentClothingPresetsAndItemsStatic(Atom atom, KeepGarmentAppearanceState state, string phase)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.name; } catch { }
            if (state.ClothingPresetsJson != null)
            {
                try
                {
                    JSONStorable cps = atom.GetStorableByID("ClothingPresets");
                    if (cps != null)
                    {
                        cps.RestoreFromJSON(CloneJsonClassStatic(state.ClothingPresetsJson));
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: ClothingPresets restore: {ex.Message}");
                }
            }
            if (state.GarmentClothingItemJsons != null && state.GarmentClothingItemJsons.Count > 0)
            {
                int ok = 0, missingStorable = 0, fail = 0;
                foreach (var kv in state.GarmentClothingItemJsons)
                {
                    JSONStorable cs = null;
                    try { cs = atom.GetStorableByID(kv.Key); } catch { }
                    if (cs != null) { try { cs.RestoreFromJSON(kv.Value); ok++; } catch { fail++; } }
                    else { missingStorable++; }
                }
                LogUtil.Log($"[DragDropDebug][KeepGarment] {phase}: clothingItems restored={ok} missingStorableId={missingStorable} failed={fail} atom={atomLabel}");
            }

            LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: atom clothingItem ids now: " + CollectClothingItemStorableIdsStatic(atom));
        }

        private static string CollectClothingItemStorableIdsStatic(Atom atom)
        {
            if (atom == null) return "<no atom>";
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (string sid in atom.GetStorableIDs())
                {
                    if (!IsClothingItemStorableIdStatic(sid)) continue;
                    sb.Append(sid);
                    sb.Append(" ");
                }
            }
            catch { }
            return sb.Length == 0 ? "<none>" : sb.ToString();
        }

        private static readonly HashSet<string> GarmentClothingRegionTagsStatic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "torso", "hip", "arms", "hands", "legs", "feet", "neck", "full body"
        };

        private static readonly string[] S_cosmeticKeywordsStatic =
        {
            "lash", "lashes", "makeup", "shadow", "liner", "eyeliner", "eyeshadow", "eyesdw", "brow",
            "gloss", "lipstick", "lip_", "lips", "nail", "toenail", "piercing", "earring",
            "reflection", "eye_reflection", "eyes_reflection", "tears", "tear",
            "teeth", "head flower", "glasses", "tattoo", "decal", "lash_female", "lash_male",
            "wetline", "wet_line", "eyelid", "cornea", "sclera", "contact", "overlay",
            "iris_tex", "eyeball", "pupil"
        };

        private static readonly string[] S_garmentKeywordsStatic =
        {
            "pant", "skirt", "short", "shirt", "top", "bra", "dress", "suit", "jacket", "coat",
            "sock", "shoe", "boot", "heel", "glove", "underwear", "thong", "brief", "jean",
            "bodysuit", "sweater", "stocking", "lingerie", "bikini", "bottom", "corset", "panty",
            "winter_clothes", "costume_o", "jean_short", "sandals", "uggs", "baggy",
            "outfit", "apron", "vest", "legging", "hoodie", "costume", "uniform", "robe", "cloak",
            "tunic", "kimono", "yukata", "leotard", "catsuit", "jumpsuit", "overalls", "sarong",
            "tights", "cardigan", "blazer", "necker", "scarf", "belt", "harness", "swimwear",
            "swimsuit", "idol", "nighty", "nightie", "pajama", "pyjama"
        };
        #endregion

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
