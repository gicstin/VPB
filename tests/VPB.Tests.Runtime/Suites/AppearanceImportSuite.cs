using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SimpleJSON;
using VPB.src.util;

namespace VPB.Tests.Runtime
{
    [VpbRuntimeSuite(Name = "AppearanceImport", Order = 90)]
    public static class AppearanceImportSuite
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SkinOnlyPreservesTheTargetsMorphsClothingAndHair()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Skin });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator MorphsOnlyPreserveTheTargetsSkinClothingAndHair()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Morphs });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator ClothingOnlyPreservesTheTargetsBodySkinAndHair()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Clothing });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator HairOnlyPreservesTheTargetsBodySkinAndClothing()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Hair });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator CombinedSkinAndHairCanBeUndoneTogether()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Skin, VpbResourceType.Hair });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator FullAppearanceCanBeUndone()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Appearance });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator LocalAppearanceImportsWithoutAPackage()
        {
            GalleryPanel panel = FindAppearancePanel();
            if (!(SourceEntry(panel) is SystemFileEntry)) RuntimeAssert.Inconclusive("Select a local appearance first.");
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Skin });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator AppearanceOnlyPackageImportsWithoutScenes()
        {
            VarFileEntry entry = SourceEntry(FindAppearancePanel()) as VarFileEntry;
            if (entry == null || entry.Package == null) RuntimeAssert.Inconclusive("Select a packaged appearance first.");
            foreach (VarFileEntry file in entry.Package.FileEntries)
                if (file.InternalPath.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
                    && file.InternalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    RuntimeAssert.Inconclusive("Select an appearance package containing no scenes.");
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Clothing, VpbResourceType.Hair });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator ExternalMorphDependenciesAreApplied()
        {
            GalleryPanel panel = FindAppearancePanel();
            JSONClass source = (JSONClass)typeof(GalleryPanel).GetMethod("GetImportSidebarPresetView", Fields).Invoke(panel, null);
            JSONClass morphs = VpbImportSource.Slice(source, VpbResourceType.Morphs);
            if (morphs == null) RuntimeAssert.Inconclusive("Select an appearance with package morph references.");
            HashSet<string> references = VarNameParser.Parse(JsonSerializationUtil.Serialize(morphs, 8192));
            VarFileEntry entry = SourceEntry(panel) as VarFileEntry;
            if (entry != null && entry.Package != null) references.Remove(entry.Package.Uid);
            if (references.Count == 0) RuntimeAssert.Inconclusive("Select an appearance referencing external morph packages.");
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Morphs });
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator ExistingScenePersonImportsRemainIsolated()
        {
            yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Skin, VpbResourceType.Morphs }, VpbImportSourceKind.Scene);
        }

        private static FileEntry SourceEntry(GalleryPanel panel)
        {
            return (FileEntry)typeof(GalleryPanel).GetField("importSidebarSourceScene", Fields).GetValue(panel);
        }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SavedPoseImportsIndependently() { yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Pose }); }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SavedBreastPhysicsImportsIndependently() { yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.BreastPhysics }); }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SavedGlutePhysicsImportsIndependently() { yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Glute }); }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SavedPluginsImportIndependently() { yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.Plugins }); }

        [VpbRuntimeTest(TimeoutSeconds = 120)]
        public static IEnumerator SavedGeneralPresetImportsIndependently() { yield return ApplyOnTemporaryPerson(new[] { VpbResourceType.General }); }

        [VpbRuntimeTest]
        public static void AppearanceSourceDisablesSceneOnlyActionsInTheOpenImporter()
        {
            GalleryPanel panel = FindAppearancePanel();
            var gate = typeof(GalleryPanel).GetMethod("ImportSidebarCategoryAllowed", Fields);
            var available = typeof(GalleryPanel).GetMethod("IsImportTypeAvailable", Fields);
            RuntimeAssert.True((bool)gate.Invoke(panel, null), "Import must remain available in the Appearance tab.");
            RuntimeAssert.False((bool)available.Invoke(panel, new object[] { VpbResourceType.CUA }), "Appearance presets cannot import scene CUAs.");
            RuntimeAssert.False((bool)available.Invoke(panel, new object[] { VpbResourceType.Atoms }), "Appearance presets cannot import scene atoms.");
        }

        [VpbRuntimeTest]
        public static void LoadingAppearanceKeepsTypeCountsUnknownAndChipsAvailable()
        {
            GalleryPanel panel = FindAppearancePanel();
            Type panelType = typeof(GalleryPanel);
            var root = panelType.GetField("importSidebarLoadedSceneJSON", Fields);
            var preset = panelType.GetField("importSidebarPresetView", Fields);
            var presetAtom = panelType.GetField("importSidebarPresetViewAtomId", Fields);
            var loading = panelType.GetField("importSidebarSceneJsonLoading", Fields);
            var refresh = panelType.GetMethod("RefreshSourceTypeAvailability", Fields);
            var available = panelType.GetMethod("IsImportTypeAvailable", Fields);
            object savedRoot = root.GetValue(panel), savedPreset = preset.GetValue(panel);
            object savedAtom = presetAtom.GetValue(panel), savedLoading = loading.GetValue(panel);
            try
            {
                root.SetValue(panel, null);
                preset.SetValue(panel, null);
                loading.SetValue(panel, true);
                refresh.Invoke(panel, null);
                var counts = (Dictionary<VpbResourceType, int>)panelType.GetField("importSidebarSourceTypeCounts", Fields).GetValue(panel);
                RuntimeAssert.True(counts.Count == 0, "Loading appearance counts must remain unknown.");
                var buttons = (Dictionary<VpbResourceType, UnityEngine.GameObject>)panelType.GetField("importSidebarTypeRadioButtons", Fields).GetValue(panel);
                foreach (var chip in buttons)
                {
                    RuntimeAssert.True((bool)available.Invoke(panel, new object[] { chip.Key }), "Loading type must remain available: " + chip.Key);
                    RuntimeAssert.True(chip.Value.activeSelf, "Loading chip must stay visible: " + chip.Key);
                    var button = chip.Value.GetComponent<UnityEngine.UI.Button>();
                    RuntimeAssert.True(button != null && button.interactable, "Loading chip must stay selectable: " + chip.Key);
                }
            }
            finally
            {
                root.SetValue(panel, savedRoot);
                preset.SetValue(panel, savedPreset);
                presetAtom.SetValue(panel, savedAtom);
                loading.SetValue(panel, savedLoading);
                refresh.Invoke(panel, null);
            }
        }

        private static GalleryPanel FindAppearancePanel(VpbImportSourceKind expected = VpbImportSourceKind.Appearance)
        {
            if (Gallery.singleton != null)
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                {
                    if (panel == null || !panel.IsImportSidebarActive) continue;
                    object kind = typeof(GalleryPanel).GetField("importSidebarSourceKind", Fields).GetValue(panel);
                    JSONClass root = typeof(GalleryPanel).GetField("importSidebarLoadedSceneJSON", Fields).GetValue(panel) as JSONClass;
                    if ((VpbImportSourceKind)kind == expected && root != null) return panel;
                }
            RuntimeAssert.Inconclusive("Open Import and select a ready source of kind " + expected + " containing the components under test.");
            return null;
        }

        private static IEnumerator ApplyOnTemporaryPerson(VpbResourceType[] types, VpbImportSourceKind kind = VpbImportSourceKind.Appearance)
        {
            GalleryPanel panel = FindAppearancePanel(kind);
            JSONClass source = (JSONClass)typeof(GalleryPanel).GetMethod("GetImportSidebarPresetView", Fields).Invoke(panel, null);
            RuntimeAssert.NotNull(source, "Choose a source Person or appearance first.");
            FileEntry entry = (FileEntry)typeof(GalleryPanel).GetField("importSidebarSourceScene", Fields).GetValue(panel);
            var slices = new List<JSONClass>();
            foreach (VpbResourceType type in types)
            {
                if (VpbImportSource.Count(source, type) == 0)
                    RuntimeAssert.Inconclusive("Selected appearance has no " + type + " data.");
                JSONClass slice = VpbImportSource.Slice(source, type);
                RuntimeAssert.NotNull(slice, "Saved component did not produce an import payload.");
                VarPresetPathFixups.Apply(slice, entry.Uid);
                slices.Add(slice);
            }
            string uid = "VPBImportTest_" + Guid.NewGuid().ToString("N");
            Atom target = null;
            try
            {
                yield return SuperController.singleton.AddAtomByType("Person", uid);
                target = SuperController.singleton.GetAtomByUid(uid);
                RuntimeAssert.NotNull(target, "Could not create isolated import target.");
                yield return CUAAtomImporter.WaitForPersonSettled(target);
                LooseVapGenderProbe.Gender sourceGender = LooseVapGenderProbe.ClassifyStorables(source);
                if (Array.IndexOf(types, VpbResourceType.Appearance) < 0
                    && sourceGender != LooseVapGenderProbe.Gender.Unknown && sourceGender != AtomGenderUtils.ClassifyForBadge(target))
                    RuntimeAssert.Inconclusive("Component tests currently require a source compatible with a new default Person.");
                Action undo = (Action)typeof(GalleryPanel).GetMethod("CaptureAtomSnapshotAction", Fields)
                    .Invoke(panel, new object[] { target });
                RuntimeAssert.NotNull(undo, "Import must capture an undo before changing the target.");
                var additionalUndo = new Dictionary<string, JSONClass>();
                foreach (VpbResourceType type in types)
                    if (type == VpbResourceType.Pose || type == VpbResourceType.BreastPhysics || type == VpbResourceType.Glute
                        || type == VpbResourceType.Plugins || type == VpbResourceType.General)
                    {
                        string id = ManagerId(type);
                        JSONClass snapshot = VpbImport.CapturePresetForUndo(target, id);
                        if (snapshot == null) RuntimeAssert.Inconclusive("Default target cannot import " + type + ".");
                        additionalUndo.Add(id, snapshot);
                    }
                string beforeAll = StableState(target, new VpbResourceType[0]);
                string beforeUnselected = StableState(target, types);
                for (int i = 0; i < types.Length; i++)
                {
                    VpbResourceType type = types[i];
                    string managerId = ManagerId(type);
                    JSONStorable control = target.GetStorableByID(managerId);
                    MeshVR.PresetManager manager = control != null ? control.GetComponentInChildren<MeshVR.PresetManager>() : null;
                    RuntimeAssert.NotNull(manager, "Target lacks its native " + managerId + " manager.");
                    JSONClass previous = manager.lastLoadedJSON;
                    VpbImport.LoadPreset(entry, target,
                        type == VpbResourceType.Appearance || type == VpbResourceType.Clothing
                            || type == VpbResourceType.Hair || type == VpbResourceType.Pose ? type : VpbResourceType.General,
                        ClothingApplyMode.Replace, slices[i], storableNameOverride: managerId, suppressScaleChange: true);
                    yield return CUAAtomImporter.WaitForPersonSettled(target);
                    RuntimeAssert.False(ReferenceEquals(previous, manager.lastLoadedJSON), "Native preset apply was never reached for " + type + ".");
                    RuntimeAssert.True(type == VpbResourceType.General
                        ? manager.filteredJSON != null && manager.filteredJSON["storables"].Count > 0
                        : VpbImportSource.Count(manager.filteredJSON, type) > 0,
                        "Native filtering discarded the selected " + type + " data.");
                }
                RuntimeAssert.Equal(beforeUnselected, StableState(target, types), "Component import changed unselected appearance data.");
                undo();
                foreach (KeyValuePair<string, JSONClass> snapshot in additionalUndo)
                    VpbImport.LoadPreset(null, target, VpbResourceType.General, ClothingApplyMode.Replace,
                        VpbImportSource.Clone(snapshot.Value), storableNameOverride: snapshot.Key,
                        skipDependencyPrewarm: true, updateLastRestoredData: false);
                yield return CUAAtomImporter.WaitForPersonSettled(target);
                foreach (KeyValuePair<string, JSONClass> snapshot in additionalUndo)
                    RuntimeAssert.Equal(JsonSerializationUtil.Serialize(snapshot.Value, 8192),
                        JsonSerializationUtil.Serialize(VpbImport.CapturePresetForUndo(target, snapshot.Key), 8192),
                        "Undo did not restore " + snapshot.Key + ".");
                RuntimeAssert.Equal(beforeAll, StableState(target, new VpbResourceType[0]), "Undo did not restore all appearance components.");
            }
            finally
            {
                if (target == null) target = SuperController.singleton.GetAtomByUid(uid);
                if (target != null) SuperController.singleton.RemoveAtom(target);
            }
        }

        private static string ManagerId(VpbResourceType type)
        {
            string id = (string)typeof(GalleryPanel).GetMethod("ResolveStorableOverrideForType", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { type });
            return id ?? type + "Presets";
        }

        private static string StableState(Atom target, VpbResourceType[] excluded)
        {
            var skip = new HashSet<VpbResourceType>(excluded);
            bool appearance = skip.Contains(VpbResourceType.Appearance) || skip.Contains(VpbResourceType.General);
            if (skip.Contains(VpbResourceType.Pose)) skip.Add(VpbResourceType.Morphs);
            if (appearance)
            {
                skip.Add(VpbResourceType.Morphs);
                skip.Add(VpbResourceType.Skin);
                skip.Add(VpbResourceType.Clothing);
                skip.Add(VpbResourceType.Hair);
            }
            JSONClass state = new JSONClass();
            JSONStorable geometry = target.GetStorableByID("geometry");
            JSONClass data = geometry != null ? geometry.GetJSON(true, true, true) : null;
            if (data != null)
            {
                if (!skip.Contains(VpbResourceType.Morphs)) state["morphs"] = data["morphs"];
                if (!skip.Contains(VpbResourceType.Clothing)) state["clothing"] = data["clothing"];
                if (!skip.Contains(VpbResourceType.Hair)) state["hair"] = data["hair"];
                if (!appearance) state["character"] = data["character"];
            }
            foreach (string id in target.GetStorableIDs())
            {
                if (id == "rescaleObject" && skip.Contains(VpbResourceType.General)) continue;
                if ((skip.Contains(VpbResourceType.Skin) || !VpbImportSource.Matches(id, VpbResourceType.Skin)) && id != "rescaleObject") continue;
                JSONStorable storable = target.GetStorableByID(id);
                if (storable != null) state[id] = storable.GetJSON(true, true, true);
            }
            return JsonSerializationUtil.Serialize(state, 8192);
        }
    }
}
