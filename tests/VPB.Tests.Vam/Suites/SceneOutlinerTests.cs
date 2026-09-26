using System;
using System.Collections.Generic;
using UnityEngine;
using VPB.Outliner;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SceneOutlinerTests
    {
        public SceneOutlinerTests(VamFixture vam) { }

        static OutlinerAtomFacts Fact(
            string uid,
            string type,
            CreatorStripKeepKind kind,
            string parent = "",
            string sub = "",
            string package = "")
        {
            var f = new OutlinerAtomFacts();
            f.Uid = uid;
            f.Type = type;
            f.DisplayName = uid;
            f.Kind = kind;
            f.ParentUid = parent ?? "";
            f.SubSceneUid = sub ?? "";
            f.PackageUid = package ?? "";
            return f;
        }

        [Fact]
        public void SharedPathPrefixDropsFromSiblingAtomLabels()
        {
            string[] shown = OutlinerFilter.DistinctPathLabels(new[]
            {
                "MPS/Lighting/Model Lights/Right Fill Light 1",
                "MPS/Lighting/Model Lights/Right Fill Light 2",
                "MPS/Lighting/Room Lights/Center Fill Light",
                "White Room Style/Backdrop Light 2"
            });
            Assert.Equal("Right Fill Light 1", shown[0]);
            Assert.Equal("Right Fill Light 2", shown[1]);
            Assert.Equal("Center Fill Light", shown[2]);
            Assert.Equal("Backdrop Light 2", shown[3]);
        }

        [Fact]
        public void CollidingLeafNamesKeepTheParentFolder()
        {
            string[] shown = OutlinerFilter.DistinctPathLabels(new[]
            {
                "MPS/Lighting/Model Lights/Fill",
                "MPS/Lighting/Room Lights/Fill"
            });
            Assert.Equal("Model Lights · Fill", shown[0]);
            Assert.Equal("Room Lights · Fill", shown[1]);
        }

        [Fact]
        public void TypeGroupAtomRowsUseCompactPathLabelsAndKeepFullUid()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("MPS/Lighting/Model Lights/Right Fill Light 1", "InvisibleLight", CreatorStripKeepKind.Lights),
                Fact("MPS/Lighting/Model Lights/Right Fill Light 2", "InvisibleLight", CreatorStripKeepKind.Lights)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            OutlinerNode a = model.Find("atom:MPS/Lighting/Model Lights/Right Fill Light 1");
            OutlinerNode b = model.Find("atom:MPS/Lighting/Model Lights/Right Fill Light 2");
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal("Right Fill Light 1", a.Label);
            Assert.Equal("Right Fill Light 2", b.Label);
            Assert.Equal("MPS/Lighting/Model Lights/Right Fill Light 1", a.AtomUid);
        }

        [Fact]
        public void TypeGroupNodeCarriesTheKindForItsIcon()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            OutlinerNode group = model.Find("group:" + ((int)CreatorStripKeepKind.Lights).ToString());
            Assert.NotNull(group);
            Assert.Equal(CreatorStripKeepKind.Lights, group.AtomKind);
        }

        [Fact]
        public void TypeGroupsKeepPeopleAndLightsInDisplayOrder()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights),
                Fact("PersonA", "Person", CreatorStripKeepKind.Persons),
                Fact("Cube", "Cube", CreatorStripKeepKind.Props)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            OutlinerNode scene = model.Get(model.RootIndex);
            Assert.Equal(3, scene.ChildIndices.Count);
            Assert.Equal("Persons", model.Get(scene.ChildIndices[0]).Label);
            Assert.Equal("Lights", model.Get(scene.ChildIndices[1]).Label);
            Assert.Equal("Props", model.Get(scene.ChildIndices[2]).Label);
            var vis = new List<int>();
            var depths = new List<int>();
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            model.CollectVisible(expanded, vis, depths);
            Assert.Equal(3, vis.Count);
            Assert.Equal("Persons", model.Get(vis[0]).Label);
            Assert.Equal(0, depths[0]);
            for (int i = 0; i < vis.Count; i++)
            {
                OutlinerNode n = model.Get(vis[i]);
                Assert.NotEqual(OutlinerNodeKind.Scene, n.Kind);
            }
        }

        [Fact]
        public void HierarchyNestsChildUnderParentAtom()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Parent", "Empty", CreatorStripKeepKind.Props),
                Fact("Child", "Cube", CreatorStripKeepKind.Props, parent: "Parent")
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.Hierarchy, "", CreatorStripKeepKind.None);
            OutlinerNode parent = model.Find("atom:Parent");
            Assert.NotNull(parent);
            bool found = false;
            for (int i = 0; i < parent.ChildIndices.Count; i++)
            {
                OutlinerNode c = model.Get(parent.ChildIndices[i]);
                if (c != null && string.Equals(c.AtomUid, "Child", StringComparison.Ordinal))
                    found = true;
            }
            Assert.True(found, "Parenting in the outliner must follow VaM parentAtom so nested props stay nested.");
        }

        [Fact]
        public void FlatListSortsByDisplayName()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("zeta", "Cube", CreatorStripKeepKind.Props),
                Fact("alpha", "Cube", CreatorStripKeepKind.Props)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.Flat, "", CreatorStripKeepKind.None);
            OutlinerNode scene = model.Get(model.RootIndex);
            Assert.Equal("alpha", model.Get(scene.ChildIndices[0]).Label);
            Assert.Equal("zeta", model.Get(scene.ChildIndices[1]).Label);
        }

        [Fact]
        public void FilterMatchesUidTypeAndPackage()
        {
            var lamp = Fact("Lamp1", "InvisibleLight", CreatorStripKeepKind.Lights, package: "Author.Lights.1");
            Assert.True(OutlinerFilter.Matches(lamp, "lamp"));
            Assert.True(OutlinerFilter.Matches(lamp, "Invisible"));
            Assert.True(OutlinerFilter.Matches(lamp, "Author.Lights"));
            Assert.False(OutlinerFilter.Matches(lamp, "Person"));
        }

        [Fact]
        public void KindMaskHidesOtherTypes()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("P", "Person", CreatorStripKeepKind.Persons),
                Fact("L", "InvisibleLight", CreatorStripKeepKind.Lights)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.Flat, "", CreatorStripKeepKind.Persons);
            Assert.Equal(1, model.AtomCount);
            Assert.NotNull(model.Find("atom:P"));
            Assert.Null(model.Find("atom:L"));
        }

        [Fact]
        public void PresentKindsOnlyIncludesBucketsThatHaveAtoms()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("P", "Person", CreatorStripKeepKind.Persons),
                Fact("Hair1", "Hair", CreatorStripKeepKind.Other)
            };
            CreatorStripKeepKind present = OutlinerModelBuilder.PresentKinds(facts);
            Assert.Equal(CreatorStripKeepKind.Persons | CreatorStripKeepKind.Other, present);
            Assert.Equal(CreatorStripKeepKind.None, present & CreatorStripKeepKind.Lights);
        }

        [Fact]
        public void PresentKindsTreatsUnclassifiedAtomsAsOther()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Mystery", "CustomThing", CreatorStripKeepKind.None)
            };
            Assert.Equal(CreatorStripKeepKind.Other, OutlinerModelBuilder.PresentKinds(facts));
        }

        [Fact]
        public void KindMaskDropsWhenThatKindLeavesTheScene()
        {
            CreatorStripKeepKind clipped = OutlinerModelBuilder.ClipKindMask(
                CreatorStripKeepKind.Persons | CreatorStripKeepKind.Lights,
                CreatorStripKeepKind.Persons);
            Assert.Equal(CreatorStripKeepKind.Persons, clipped);
            Assert.Equal(CreatorStripKeepKind.None, OutlinerModelBuilder.ClipKindMask(
                CreatorStripKeepKind.Lights, CreatorStripKeepKind.Persons));
        }

        [Fact]
        public void ExpansionStateSurvivesRebuildThatAddsAndRemovesAtoms()
        {
            var first = new List<OutlinerAtomFacts>
            {
                Fact("Keep", "Person", CreatorStripKeepKind.Persons),
                Fact("Gone", "Cube", CreatorStripKeepKind.Props)
            };
            OutlinerModelBuilder.Build(
                first, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            expanded.Add("scene");
            expanded.Add("group:" + ((int)CreatorStripKeepKind.Persons).ToString());
            expanded.Add("atom:Keep");

            var second = new List<OutlinerAtomFacts>
            {
                Fact("Keep", "Person", CreatorStripKeepKind.Persons),
                Fact("New", "InvisibleLight", CreatorStripKeepKind.Lights)
            };
            OutlinerModel b = OutlinerModelBuilder.Build(
                second, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            var vis = new List<int>();
            var depths = new List<int>();
            b.CollectVisible(expanded, vis, depths);
            bool keepVisible = false;
            for (int i = 0; i < vis.Count; i++)
            {
                OutlinerNode n = b.Get(vis[i]);
                if (n != null && n.Kind == OutlinerNodeKind.Atom && n.AtomUid == "Keep")
                    keepVisible = true;
            }
            Assert.True(keepVisible,
                "An expanded group must still show its atom after another atom is spawned or removed.");
            Assert.Null(b.Find("atom:Gone"));
            Assert.NotNull(b.Find("atom:New"));
        }

        [Fact]
        public void UndoCoalescesSameKeyInsideTheWindowAndRevertsInOrder()
        {
            var ring = new OutlinerUndo();
            ring.PushAt("a/intensity", "Intensity", "1", "2", 0f);
            ring.PushAt("a/intensity", "Intensity", "2", "3", 0.2f);
            Assert.Equal(1, ring.UndoCount);
            ring.PushAt("a/range", "Range", "4", "8", 10f);
            Assert.Equal(2, ring.UndoCount);
            OutlinerUndoRecord range = ring.Undo();
            Assert.Equal("Range", range.Label);
            OutlinerUndoRecord intensity = ring.Undo();
            Assert.Equal("1", intensity.Before);
            Assert.Equal("3", intensity.After);
            Assert.Equal("Intensity", ring.Redo().Label);
        }

        [Fact]
        public void UndoRingDropsOldestPastBound()
        {
            var ring = new OutlinerUndo();
            for (int i = 0; i < OutlinerUndo.MaxRecords + 5; i++)
                ring.PushAt("k" + i.ToString(), "L", "0", "1", i);
            Assert.Equal(OutlinerUndo.MaxRecords, ring.UndoCount);
        }

        [Fact]
        public void PinStoreRoundTripsAndIsolatesAtomTypes()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            OutlinerPins.SetPinned(map, "Person", "control/freezePhysics", true);
            OutlinerPins.SetPinned(map, "InvisibleLight", "Light/intensity", true);
            string json = OutlinerPins.Serialize(map);
            Dictionary<string, List<string>> back = OutlinerPins.Parse(json);
            Assert.True(OutlinerPins.IsPinned(back, "Person", "control/freezePhysics"));
            Assert.False(OutlinerPins.IsPinned(back, "Person", "Light/intensity"),
                "Favourites for Person must not pick up Light pins.");
            Assert.True(OutlinerPins.IsPinned(back, "InvisibleLight", "Light/intensity"));
        }

        [Fact]
        public void LookPreviewDoesNotNeedALivePersonHierarchyToRefuseEmptyInput()
        {
            var into = new List<OutlinerLookItem>();
            into.Add(new OutlinerLookItem());
            OutlinerLookPreview.Collect(null, into, 4);
            Assert.Empty(into);
            Assert.Equal("Author.Pack.1", OutlinerLookPreview.PackageUidOf("Author.Pack.1:/Custom/Clothing/x.vam"));
        }

        [Theory]
        [InlineData("Author.Pack.1:/Custom/Clothing/Female/x/shirt.vam", "Author.Pack.1:/Custom/Clothing/Female/x/shirt.jpg")]
        [InlineData("Custom/Clothing/Female/x/shirt.vam", "Custom/Clothing/Female/x/shirt.jpg")]
        [InlineData("Author.Pack.1:/Custom/Clothing/Female/x/shirt", "Author.Pack.1:/Custom/Clothing/Female/x/shirt.jpg")]
        public void WornItemThumbUsesTheSisterJpgEvenWhenUidHasNoExtension(string file, string jpg)
        {
            Assert.Equal(jpg, OutlinerLookPreview.SisterJpgPath(file));
        }

        [Fact]
        public void ClothingInsideAPackGetsAPackageQualifiedThumbPath()
        {
            string thumb = OutlinerLookPreview.ThumbPathFromParts(
                "Custom/Clothing/Female/Creator/Skirt/skirt.vam",
                "Creator.Outfit.1",
                "",
                "",
                "");
            Assert.Equal(
                "Creator.Outfit.1:/Custom/Clothing/Female/Creator/Skirt/skirt.jpg",
                thumb);
        }

        [Fact]
        public void ClothingThumbUsesContainingDirWhenUidIsOnlyAnInternalId()
        {
            string thumb = OutlinerLookPreview.ThumbPathFromParts(
                "LittleFlirtSkirt",
                "Creator.Clothes.2",
                "",
                "Custom/Clothing/Female/Creator/Flirt",
                "skirt.vam");
            Assert.Equal(
                "Creator.Clothes.2:/Custom/Clothing/Female/Creator/Flirt/skirt.jpg",
                thumb);
        }

        [Fact]
        public void BrokenNullPackagePrefixStillResolvesInsideThePack()
        {
            string thumb = OutlinerLookPreview.ThumbPathFromParts(
                ":/Custom/Clothing/Female/x/shirt.vam",
                "Creator.Pack.1",
                "",
                "",
                "");
            Assert.Equal(
                "Creator.Pack.1:/Custom/Clothing/Female/x/shirt.jpg",
                thumb);
        }

        [Fact]
        public void ParamCatalogListsFloatsAndMarksThrowingStorablesDisabled()
        {
            List<OutlinerParamDescriptor> ok = OutlinerParamCatalog.FromNameLists(
                "Light",
                new[] { "intensity" },
                new[] { "on" },
                null, null, null, null, null,
                false);
            Assert.Equal(2, ok.Count);
            Assert.Equal("on", ok[0].ParamId);
            Assert.Equal(OutlinerParamKind.Bool, ok[0].Kind);
            Assert.Equal("On", ok[0].Label);
            Assert.Equal("intensity", ok[1].ParamId);
            Assert.Equal(OutlinerParamKind.Float, ok[1].Kind);
            Assert.Equal("Intensity", ok[1].Label);

            Assert.True(OutlinerParamCatalog.IsNoiseParam("SaveToStore1"),
                "Store-slot actions must not appear as atom controls.");
            Assert.True(OutlinerParamCatalog.IsNoiseParam("lastRelativeVelocity"),
                "Readout floats must not appear as sliders.");
            Assert.False(OutlinerParamCatalog.IsNoiseParam("intensity"),
                "Light intensity is a real control and must stay listed.");

            var empty = new List<OutlinerParamDescriptor>();
            Assert.False(OutlinerParamCatalog.HasEditableParams(empty),
                "A storable with no params must not render as a name-only card.");
            var disabled = new List<OutlinerParamDescriptor>();
            var badRow = new OutlinerParamDescriptor();
            badRow.Kind = OutlinerParamKind.Disabled;
            badRow.DisabledReason = "Missing storable";
            disabled.Add(badRow);
            Assert.False(OutlinerParamCatalog.HasEditableParams(disabled),
                "A missing storable must not render as a name-only card.");
            Assert.True(OutlinerParamCatalog.HasEditableParams(ok),
                "A Light intensity row must count as an editable control.");
            Assert.True(OutlinerParamCatalog.DescribeNamed(null, "control", "freezePhysics") == null,
                "A missing storable must not walk every Person param name to draw freeze physics.");

            List<OutlinerParamDescriptor> bad = OutlinerParamCatalog.FromNameLists(
                "Broken", null, null, null, null, null, null, null, true);
            Assert.Single(bad);
            Assert.Equal(OutlinerParamKind.Disabled, bad[0].Kind);
            Assert.False(string.IsNullOrEmpty(bad[0].DisabledReason),
                "A throwing storable must become a disabled row, not a crash of the inspector.");

            Assert.False(OutlinerParamCatalog.EnumeratesFloatParams(null, "geometry"),
                "Person geometry is thousands of morph floats — listing them freezes VaM on Person select.");
            Assert.False(OutlinerParamCatalog.EnumeratesFloatParams(null, "morphs"),
                "Morph banks must not dump every slider into the outliner inspector.");
            Assert.True(OutlinerParamCatalog.EnumeratesFloatParams(null, "Light"),
                "A light intensity float is a real control and must still be listed.");
        }

        [Fact]
        public void PresetSlotActionsNeverReachTheInspector()
        {
            string[] universal =
            {
                "SaveToStore1", "SaveToStore2", "SaveToStore3",
                "RestoreAllFromStore1", "RestoreAllFromStore2", "RestoreAllFromStore3",
                "RestorePhysicsFromStore1", "RestorePhysicsFromStore2", "RestorePhysicsFromStore3",
                "RestoreAppearanceFromStore1", "RestoreAppearanceFromStore2", "RestoreAppearanceFromStore3",
                "RestoreAllFromDefaults", "RestorePhysicalFromDefaults", "RestoreAppearanceFromDefaults"
            };
            for (int i = 0; i < universal.Length; i++)
            {
                Assert.True(OutlinerParamCatalog.IsNoiseParam(universal[i]),
                    universal[i] + " is registered on every VaM storable and is not an atom control.");
            }

            List<OutlinerParamDescriptor> rescale = OutlinerParamCatalog.FromNameLists(
                "rescaleObject",
                new[] { "scale" },
                null, null, null, null, null,
                universal,
                false);
            Assert.Single(rescale);
            Assert.Equal("scale", rescale[0].ParamId);
            Assert.True(OutlinerParamCatalog.IsRedundantStorable("rescaleObject"),
                "The Transform card already owns scale — the rescaleObject card is pure duplication.");
            Assert.False(OutlinerParamCatalog.IsRedundantStorable("Light"),
                "Only scale storables are redundant; real type storables must keep their card.");
        }

        [Fact]
        public void UsefulParamsSortAboveThePowerUserSoup()
        {
            List<OutlinerParamDescriptor> control = OutlinerParamCatalog.FromNameLists(
                "control",
                new[] { "complyPositionSpring", "holdPositionSpring", "mass", "jointDriveXTarget" },
                new[] { "xPositionLock", "physicsEnabled", "on" },
                null,
                new[] { "positionState" },
                null, null,
                new[] { "Reset" },
                false);
            Assert.Equal("on", control[0].ParamId);
            Assert.Equal("physicsEnabled", control[1].ParamId);
            Assert.Equal("positionState", control[2].ParamId);
            Assert.Equal("holdPositionSpring", control[3].ParamId);
            Assert.Equal("mass", control[4].ParamId);
            Assert.Equal("Reset", control[5].ParamId);
            List<OutlinerParamDescriptor> light = OutlinerParamCatalog.FromNameLists(
                "Light",
                new[] { "intensity" },
                new[] { "on" },
                null, null, null, null,
                new[] { "ToggleOn" },
                false);
            Assert.Equal(2, light.Count);
            Assert.DoesNotContain(light, d => d.ParamId == "ToggleOn");

            List<OutlinerParamDescriptor> toggleOnly = OutlinerParamCatalog.FromNameLists(
                "Widget", null, null, null, null, null, null,
                new[] { "ToggleOn" },
                false);
            Assert.Single(toggleOnly);

            int comply = control.FindIndex(d => d.ParamId == "complyPositionSpring");
            int lockIdx = control.FindIndex(d => d.ParamId == "xPositionLock");
            Assert.True(lockIdx < comply,
                "Uncurated bools must outrank uncurated float springs in a narrow rail.");

            Assert.Equal("Hold position spring", OutlinerParamLabels.Humanize("holdPositionSpring"));
            Assert.Equal("Shadows on", OutlinerParamLabels.Humanize("shadowsOn"));
            Assert.Equal("Asset URL", OutlinerParamLabels.Humanize("assetUrl"));
            Assert.Equal("FOV", OutlinerParamLabels.Humanize("FOV"));
            Assert.Equal("Reset physics", OutlinerParamLabels.Humanize("ResetPhysics"));
        }

        [Fact]
        public void OneSettingIsOfferedInOneFormOnly()
        {
            List<OutlinerParamDescriptor> audio = OutlinerParamCatalog.FromNameLists(
                "AudioSource",
                new[] { "volume" },
                new[] { "loop", "on" },
                null, null, null, null,
                new[] { "SetLoop", "ToggleOn", "volume", "Stop" },
                false);
            Assert.DoesNotContain(audio, d => d.ParamId == "SetLoop");
            Assert.DoesNotContain(audio, d => d.ParamId == "ToggleOn");
            Assert.Single(audio.FindAll(d => d.ParamId == "volume"));
            Assert.Contains(audio, d => d.ParamId == "Stop");

            Assert.True(OutlinerParamCatalog.ActionMirrorsParam("ToggleOn", "on"));
            Assert.True(OutlinerParamCatalog.ActionMirrorsParam("SetLoopTrue", "loop"));
            Assert.False(OutlinerParamCatalog.ActionMirrorsParam("ResetPhysics", "physicsEnabled"),
                "A reset is a different act from the value it clears, so it keeps its own row.");
        }

        [Fact]
        public void InspectorHeaderTogglesAreNotRepeatedAsAtomRows()
        {
            Assert.True(OutlinerParamCatalog.IsHiddenParam("atom", "on"));
            Assert.True(OutlinerParamCatalog.IsHiddenParam("atom", "hidden"));
            Assert.True(OutlinerParamCatalog.IsHiddenParam("atom", "collisionEnabled"));
            Assert.False(OutlinerParamCatalog.IsHiddenParam("atom", "freezePhysics"),
                "Only the three facts the inspector header already owns may be suppressed.");
            Assert.False(OutlinerParamCatalog.IsHiddenParam("control", "on"),
                "The header writes atom/on, so control's own on stays visible.");

            List<OutlinerParamDescriptor> atom = OutlinerParamCatalog.FromNameLists(
                OutlinerParamCatalog.AtomStorableId,
                null,
                new[] { "on", "hidden", "collisionEnabled", "freezePhysics" },
                null, null, null, null, null,
                false);
            Assert.Single(atom);
            Assert.Equal("freezePhysics", atom[0].ParamId);
        }

        [Fact]
        public void SelectingAPersonMustNotOpenVaMPersonUI()
        {
            bool alignView;
            bool alignRotationOnly;
            bool alignUpDown;
            bool openSelectedUI;
            OutlinerEdits.MapSelectControllerArgs(false, false,
                out alignView, out alignRotationOnly, out alignUpDown, out openSelectedUI);
            Assert.False(openSelectedUI,
                "VaM's Person atom UI rebuilds every morph and clothing row; opening it from the Scene Outliner stalls the game.");
            Assert.False(alignView);

            OutlinerEdits.MapSelectControllerArgs(false, true,
                out alignView, out alignRotationOnly, out alignUpDown, out openSelectedUI);
            Assert.False(alignView);
            Assert.True(openSelectedUI,
                "Jump-to-VaM-UI is the one path that may open the Person panel.");
        }

        [Theory]
        [InlineData(320f, 320f)]
        [InlineData(200f, 280f)]
        [InlineData(900f, 560f)]
        public void RailWidthClampKeepsTheDockedPaneInTheConfiguredBand(float input, float expected)
        {
            Assert.Equal(expected, VPBConfig.ClampOutlinerWidth(input));
        }

        [Theory]
        [InlineData(320f, 720f)]
        [InlineData(980f, 980f)]
        [InlineData(2000f, 1600f)]
        public void FloatWidthClampLetsTheWindowHostTreeAndInspectorSideBySide(float input, float expected)
        {
            Assert.Equal(expected, VPBConfig.ClampOutlinerFloatWidth(input));
        }

        [Fact]
        public void AtomRowsDropTheRepeatedTypeIconSoOnlyTheGroupKeepsIt()
        {
            float indent = GalleryUiDesignTokens.Space4Ref;
            float slot = GalleryUiDesignTokens.ButtonSizeRef;
            float icon = GalleryUiDesignTokens.PopupMenuRowIconSizeRef;
            float hair = GalleryUiDesignTokens.HairGapRef;
            float tight = GalleryUiDesignTokens.TightGapRef;
            float group = OutlinerRowLayout.TextLeft(0, indent, slot, icon, hair, tight, true);
            float leaf = OutlinerRowLayout.TextLeft(0, indent, slot, icon, hair, tight, false);
            Assert.True(leaf < group,
                "Lights stays marked with the type icon; each light name must not repeat that icon.");
        }

        [Fact]
        public void ChildRowsSitToTheRightOfTheirTypeGroup()
        {
            float indent = GalleryUiDesignTokens.Space4Ref;
            float slot = GalleryUiDesignTokens.ButtonSizeRef;
            float icon = GalleryUiDesignTokens.PopupMenuRowIconSizeRef;
            float hair = GalleryUiDesignTokens.HairGapRef;
            float tight = GalleryUiDesignTokens.TightGapRef;
            float parent = OutlinerRowLayout.IconX(0, indent, slot, hair);
            float child = OutlinerRowLayout.IconX(1, indent, slot, hair);
            Assert.True(child > parent,
                "A camera under Cameras must indent past the group icon, not sit left of it.");
        }

        [Fact]
        public void TreeRowStrideLeavesAGutterSoAdjacentAtomsDoNotFuse()
        {
            float row = GalleryUiDesignTokens.ControlSlotHeightRef;
            float gap = GalleryUiDesignTokens.OutlinerTreeRowGapRef;
            float stride = OutlinerRowLayout.RowStridePx(row, gap);
            Assert.True(GalleryUiDesignTokens.OutlinerSliderHandleSizeRef >= GalleryUiDesignTokens.ButtonSizeRef,
                "Slider thumbs under 32px cannot be grabbed in the inspector.");
            Assert.True(
                GalleryUiDesignTokens.OutlinerChromeBarHeightRef == GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef,
                "Outliner title/footer must match other float chrome so the bars are one control row, not empty padding.");
            Assert.True(GalleryUiDesignTokens.OutlinerSpringTrackMinWidthRef >= GalleryUiDesignTokens.ButtonSizeRef * 3f,
                "Spring track must leave room to pull the handle both ways without a Fitts miss.");
        }

        [Fact]
        public void SpringPullAtCenterDoesNotMoveTheAtom()
        {
            float rate = OutlinerSpringAxis.PullToRate(0f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            Assert.Equal(0f, rate);
        }

        [Fact]
        public void SpringPullInsideTheHandleDeadzoneIsRest()
        {
            float rate = OutlinerSpringAxis.PullToRate(16f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            Assert.Equal(0f, rate);
        }

        [Fact]
        public void SpringPullAtTheTrackEndIsFullRate()
        {
            float pos = OutlinerSpringAxis.PullToRate(100f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            float neg = OutlinerSpringAxis.PullToRate(-100f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            Assert.True(Mathf.Abs(pos - 1f) < 0.001f,
                "Full right pull must be the fastest the spring can go.");
            Assert.True(Mathf.Abs(neg + 1f) < 0.001f,
                "Full left pull must match full right, opposite sign.");
        }

        [Fact]
        public void FartherSpringPullMovesFasterThanANearPull()
        {
            float near = OutlinerSpringAxis.PullToRate(40f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            float far = OutlinerSpringAxis.PullToRate(80f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            Assert.True(Mathf.Abs(far) > Mathf.Abs(near),
                "Dragging the transform handle farther from rest must speed the atom up, not stay linear-flat.");
        }

        [Fact]
        public void NearSpringPullStaysGentlerThanLinearSoPosingDoesNotJump()
        {
            float rate = Mathf.Abs(OutlinerSpringAxis.PullToRate(40f, 100f, 16f, OutlinerSpringAxis.ResponsePower));
            float linear = (40f - 16f) / (100f - 16f);
            Assert.True(rate < linear,
                "Small pulls must stay slower than linear or a tiny drag yanks the atom.");
        }

        [Fact]
        public void TheGentlestSpringPullStillMovesTheAtomVisibly()
        {
            float rate = OutlinerSpringAxis.PullToRate(17f, 100f, 16f, OutlinerSpringAxis.ResponsePower);
            float speed = Mathf.Abs(OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Position, rate));
            Assert.True(speed >= OutlinerSpringAxis.PositionMetersPerSecondMin * 0.99f,
                "A pull just past the deadzone must still creep, or the spring reads as dead.");
        }

        [Fact]
        public void SpringSpeedRisesWithThePullAndCapsAtTheTrackEnd()
        {
            float near = Mathf.Abs(OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Rotation, 0.25f));
            float far = Mathf.Abs(OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Rotation, 1f));
            float over = Mathf.Abs(OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Rotation, 4f));
            Assert.True(far > near, "A farther pull must turn faster.");
            Assert.True(Mathf.Abs(far - OutlinerSpringAxis.RotationDegreesPerSecondMax) < 0.001f,
                "Full pull must be the documented top speed.");
            Assert.True(Mathf.Abs(over - far) < 0.001f,
                "Pulling past the track end must not run away past the top speed.");
        }

        [Fact]
        public void SpringAtRestHoldsTheAtomStill()
        {
            Assert.Equal(0f, OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Position, 0f));
            Assert.Equal(0f, OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Scale, 0f));
        }

        [Fact]
        public void SpringSpeedKeepsTheSignOfThePull()
        {
            float left = OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Position, -0.5f);
            float right = OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Position, 0.5f);
            Assert.True(left < 0f && right > 0f, "Pulling left must move the atom left.");
            Assert.True(Mathf.Abs(left + right) < 1e-6f, "Both directions must run at the same speed.");
        }

        [Fact]
        public void ASingleStutteredFrameCannotFlingTheAtomAcrossTheScene()
        {
            float speed = OutlinerSpringAxis.SpeedFor(OutlinerSpringAxis.Kind.Position, 1f);
            float worstStep = speed * OutlinerSpringAxis.MaxFrameSeconds;
            Assert.True(worstStep <= 0.2f,
                "A hitched frame must not teleport the atom; the drive clamps its timestep.");
        }

        [Fact]
        public void PlusNudgeFromAMessyValueLandsOnTheNextStep()
        {
            float next = OutlinerSpringAxis.NudgeToStep(1.198f, 0.1f, 1f);
            Assert.True(Mathf.Abs(next - 1.2f) < 1e-5f,
                "+ from 1.198 at step 0.1 must show 1.2, not 1.298.");
        }

        [Fact]
        public void MinusNudgeFromAMessyValueLandsOnThePreviousStep()
        {
            float next = OutlinerSpringAxis.NudgeToStep(1.198f, 0.1f, -1f);
            Assert.True(Mathf.Abs(next - 1.1f) < 1e-5f,
                "− from 1.198 at step 0.1 must show 1.1, not 1.098.");
        }

        [Fact]
        public void PlusNudgeOnACleanStepAdvancesOneFullStep()
        {
            float next = OutlinerSpringAxis.NudgeToStep(1.2f, 0.1f, 1f);
            Assert.True(Mathf.Abs(next - 1.3f) < 1e-5f,
                "+ on an already-snapped 1.2 must go to 1.3, not stick.");
        }

        [Fact]
        public void AxisValueFieldStaysCompactSoTheSpringKeepsTheRow()
        {
            Assert.True(GalleryUiDesignTokens.OutlinerAxisValueWidthRef <= GalleryUiDesignTokens.ButtonSizeRef * 2f,
                "Typed value must stay a side field; the spring track owns leftover width.");
        }

        [Theory]
        [InlineData("st:Light", "InvisibleLight", "Light")]
        [InlineData("transform", "InvisibleLight", "transform")]
        [InlineData("st:rescaleObject", "InvisibleLight", "transform")]
        [InlineData("st:control", "InvisibleLight", "control")]
        [InlineData("st:plugin#0", "Person", "plugins")]
        [InlineData("st:PluginManager", "Person", "plugins")]
        public void InspectorChipsGroupTheSameKindOfControls(string cardId, string atomType, string expected)
        {
            Assert.Equal(expected, OutlinerInspectorSections.KeyForCard(cardId, atomType));
        }

        [Fact]
        public void UnknownStorablesShareOneOtherChipInsteadOfAGearPerModule()
        {
            Assert.Equal(OutlinerInspectorSections.Other, OutlinerInspectorSections.KeyForStorable("AutoCollider"));
            Assert.Equal(OutlinerInspectorSections.Other, OutlinerInspectorSections.KeyForCard("st:JawControl", "Person"));
            var keys = new List<string>();
            keys.Add(OutlinerInspectorSections.All);
            keys.Add(OutlinerInspectorSections.Transform);
            for (int i = 0; i < 40; i++)
                keys.Add("mod" + i);
            OutlinerInspectorSections.ClampVisible(keys);
            Assert.True(keys.Count <= OutlinerInspectorSections.MaxVisibleChips,
                "Inspector chips must stay a short row, not a wallpaper of identical gears.");
            Assert.Contains(OutlinerInspectorSections.Other, keys);
            Assert.Contains(OutlinerInspectorSections.Transform, keys);
        }

        [Fact]
        public void TypeGroupsExpandUnlessTheUserCollapsedThem()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights),
                Fact("PersonA", "Person", CreatorStripKeepKind.Persons)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            expanded.Add("scene");
            OutlinerModel.ExpandDefaultCategories(model, expanded, null);
            var vis = new List<int>();
            var depths = new List<int>();
            model.CollectVisible(expanded, vis, depths);
            Assert.True(vis.Count > 2,
                "Type groups must start expanded so atoms are visible without a second click.");
            string lightsId = "group:" + ((int)CreatorStripKeepKind.Lights).ToString();
            var collapsed = new HashSet<string>(StringComparer.Ordinal);
            collapsed.Add(lightsId);
            expanded.Clear();
            expanded.Add("scene");
            OutlinerModel.ExpandDefaultCategories(model, expanded, collapsed);
            vis.Clear();
            depths.Clear();
            model.CollectVisible(expanded, vis, depths);
            bool lampVisible = false;
            for (int i = 0; i < vis.Count; i++)
            {
                OutlinerNode n = model.Get(vis[i]);
                if (n != null && n.AtomUid == "Lamp") lampVisible = true;
            }
            Assert.False(lampVisible, "A category the user collapsed must stay closed.");
        }

        [Fact]
        public void LightChipShowsIntensityAndHidesTransform()
        {
            Assert.True(OutlinerInspectorSections.CardMatches("Light", "st:Light", "InvisibleLight"),
                "Light chip must keep intensity/on/range on the Light card.");
            Assert.False(OutlinerInspectorSections.CardMatches("Light", "transform", "InvisibleLight"),
                "Light chip must hide transform so the inspector is one control family.");
            Assert.True(OutlinerInspectorSections.CardMatches("", "transform", "InvisibleLight"),
                "All options must still show transform.");
        }

        [Fact]
        public void DockedSplitGivesTheInspectorTheGoldenMajorShare()
        {
            float tree = GalleryUiDesignTokens.OutlinerSplitTreeShareRef;
            float inspector = OutlinerSplitLayout.RailInspectorShare(tree);
            Assert.True(inspector > tree,
                "Docked Scene Outliner must give the editable inspector more height than the type list.");
            Assert.True(
                Mathf.Abs(inspector / tree - GalleryUiDesignTokens.GoldenRatio) < 0.01f,
                "Docked inspector/tree height must be φ (1.618) so the bottom half owns the work.");
        }

        static OutlinerAtomFacts Flagged(
            string uid, string type, CreatorStripKeepKind kind,
            bool on, bool hidden, bool collision, string package = "", string parent = "")
        {
            OutlinerAtomFacts f = Fact(uid, type, kind, parent, "", package);
            f.On = on;
            f.Hidden = hidden;
            f.Collision = collision;
            return f;
        }

        [Fact]
        public void FilterTypeTokenNarrowsToOneAtomType()
        {
            OutlinerAtomFacts lamp = Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights);
            OutlinerAtomFacts person = Fact("Person1", "Person", CreatorStripKeepKind.Persons);
            Assert.True(OutlinerFilter.Matches(lamp, "t:light"));
            Assert.False(OutlinerFilter.Matches(person, "t:light"));
            Assert.True(OutlinerFilter.Matches(person, "type:person"));
        }

        [Fact]
        public void FilterPackageTokenSeparatesPackagedFromLocal()
        {
            OutlinerAtomFacts fromPkg = Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights,
                package: "Author.Lights.3");
            OutlinerAtomFacts local = Fact("Lamp2", "InvisibleLight", CreatorStripKeepKind.Lights);
            Assert.True(OutlinerFilter.Matches(fromPkg, "pkg:Author"));
            Assert.False(OutlinerFilter.Matches(local, "pkg:Author"));
            Assert.True(OutlinerFilter.Matches(local, "pkg:none"));
            Assert.False(OutlinerFilter.Matches(fromPkg, "pkg:none"));
            Assert.True(OutlinerFilter.Matches(fromPkg, "pkg:any"));
        }

        [Fact]
        public void FilterStateTokensReadTheAtomFlags()
        {
            OutlinerAtomFacts live = Flagged("A", "Person", CreatorStripKeepKind.Persons, true, false, true);
            OutlinerAtomFacts dark = Flagged("B", "Person", CreatorStripKeepKind.Persons, false, true, false);
            Assert.True(OutlinerFilter.Matches(live, "is:on"));
            Assert.False(OutlinerFilter.Matches(dark, "is:on"));
            Assert.True(OutlinerFilter.Matches(dark, "is:off"));
            Assert.True(OutlinerFilter.Matches(dark, "is:hidden"));
            Assert.True(OutlinerFilter.Matches(live, "is:visible"));
            Assert.True(OutlinerFilter.Matches(live, "is:collision"));
            Assert.True(OutlinerFilter.Matches(dark, "is:nocollision"));
        }

        [Fact]
        public void FilterParentTokenSeparatesChildrenFromRootAtoms()
        {
            OutlinerAtomFacts child = Fact("Prop", "Cube", CreatorStripKeepKind.Props, parent: "Person1");
            OutlinerAtomFacts root = Fact("Prop2", "Cube", CreatorStripKeepKind.Props);
            Assert.True(OutlinerFilter.Matches(child, "is:parented"));
            Assert.False(OutlinerFilter.Matches(root, "is:parented"));
            Assert.True(OutlinerFilter.Matches(root, "is:root"));
        }

        [Fact]
        public void FilterTermsCombineWithAnd()
        {
            OutlinerAtomFacts lamp = Flagged("KeyLamp", "InvisibleLight", CreatorStripKeepKind.Lights,
                true, false, true, "Author.Lights.3");
            Assert.True(OutlinerFilter.Matches(lamp, "t:light is:on pkg:Author"));
            Assert.False(OutlinerFilter.Matches(lamp, "t:light is:off"));
            Assert.False(OutlinerFilter.Matches(lamp, "KeyLamp nothinglikethis"));
            Assert.True(OutlinerFilter.Matches(lamp, "KeyLamp t:light"));
        }

        [Fact]
        public void UnknownTokenStaysAPlainSearchTerm()
        {
            OutlinerAtomFacts lamp = Fact("ratio:2", "InvisibleLight", CreatorStripKeepKind.Lights);
            Assert.True(OutlinerFilter.Matches(lamp, "ratio:2"));
            Assert.False(OutlinerFilter.Matches(lamp, "ratio:9"));
        }

        [Fact]
        public void ModelCountsEverySceneAtomEvenWhenTheFilterHidesThemAll()
        {
            var facts = new List<OutlinerAtomFacts>
            {
                Fact("Lamp", "InvisibleLight", CreatorStripKeepKind.Lights),
                Fact("Person1", "Person", CreatorStripKeepKind.Persons)
            };
            OutlinerModel model = OutlinerModelBuilder.Build(
                facts, OutlinerGrouping.TypeGroups, "nothingmatchesthis", CreatorStripKeepKind.None);
            Assert.Equal(0, model.AtomCount);
            Assert.Equal(2, model.SceneAtomCount);
        }

        [Fact]
        public void RangeSelectionKeepsEveryUidAndTheLastClickIsPrimary()
        {
            var sel = new OutlinerSelection();
            sel.SelectOnly("A");
            sel.Add("B");
            sel.Add("C");
            Assert.Equal(3, sel.Count);
            Assert.True(sel.Contains("A"));
            Assert.True(sel.Contains("C"));
            Assert.Equal("C", sel.PrimaryUid);
            sel.Add("");
            Assert.Equal(3, sel.Count);
            Assert.Equal("C", sel.PrimaryUid);
        }

        [Fact]
        public void WornItemsMapToTheGeometryBoolVaMItselfDrives()
        {
            var shirt = new OutlinerLookItem();
            shirt.Group = "clothing";
            shirt.ItemUid = "Author.Outfit.1:/Custom/Clothing/Female/shirt.vam";
            Assert.True(OutlinerLookPreview.IsWearable(shirt));
            Assert.Equal("clothing:" + shirt.ItemUid, OutlinerLookPreview.GeometryParamId(shirt));
            Assert.True(OutlinerLookPreview.IsWornParam("geometry", OutlinerLookPreview.GeometryParamId(shirt)));

            var character = new OutlinerLookItem();
            character.Group = "character";
            character.Label = "Jane";
            Assert.False(OutlinerLookPreview.IsWearable(character));
            Assert.Equal("", OutlinerLookPreview.GeometryParamId(character));
            Assert.False(OutlinerLookPreview.IsWornParam("geometry", "on"));
            Assert.False(OutlinerLookPreview.IsWornParam("control", "clothing:x"));
        }

        [Fact]
        public void WornItemGroupsStaySeparateSoHairIsNotEatenByClothingCap()
        {
            var items = new List<OutlinerLookItem>();
            for (int i = 0; i < 10; i++)
            {
                var cloth = new OutlinerLookItem();
                cloth.Group = OutlinerLookPreview.ClothingGroup;
                cloth.ItemUid = "c" + i.ToString();
                items.Add(cloth);
            }
            for (int i = 0; i < 3; i++)
            {
                var hair = new OutlinerLookItem();
                hair.Group = OutlinerLookPreview.HairGroup;
                hair.ItemUid = "h" + i.ToString();
                items.Add(hair);
            }
            Assert.Equal(10, OutlinerLookPreview.CountGroup(items, OutlinerLookPreview.ClothingGroup));
            Assert.Equal(3, OutlinerLookPreview.CountGroup(items, OutlinerLookPreview.HairGroup));
            Assert.Equal(0, OutlinerLookPreview.CountGroup(items, OutlinerLookPreview.CharacterGroup));
            Assert.Equal(2, OutlinerLookPreview.OmittedAfterCap(10, 8));
            Assert.Equal(0, OutlinerLookPreview.OmittedAfterCap(8, 8));
            Assert.Equal(0, OutlinerLookPreview.OmittedAfterCap(3, 8));
        }

        [Fact]
        public void InspectorSectionOrderPutsWornItemsDirectlyAfterTransform()
        {
            var keys = new List<string>
            {
                OutlinerInspectorSections.Plugins,
                OutlinerInspectorSections.Look,
                OutlinerInspectorSections.Favourites,
                OutlinerInspectorSections.Transform,
                OutlinerInspectorSections.All
            };
            OutlinerInspectorSections.SortKeys(keys);
            Assert.Equal(OutlinerInspectorSections.All, keys[0]);
            Assert.Equal(OutlinerInspectorSections.Transform, keys[1]);
            Assert.Equal(OutlinerInspectorSections.Look, keys[2]);
            Assert.Equal(OutlinerInspectorSections.Favourites, keys[3]);
        }

        [Fact]
        public void VrTreeRowsClearTheHitFloorTheDesktopRowOnlyJustMeets()
        {
            Assert.True(
                GalleryUiDesignTokens.OutlinerVrRowHeightRef > GalleryUiDesignTokens.ControlSlotHeightRef,
                "VR outliner rows must be taller than desktop rows, not the same token.");
            Assert.True(
                GalleryUiDesignTokens.OutlinerNudgeButtonWidthRef >= GalleryUiDesignTokens.ButtonSizeRef,
                "Nudge buttons are the most-pressed control in the pane and may not sit under the 32px floor.");
        }

        [Fact]
        public void SpreadOrdersByAxisAndPlacesTheMiddleAtomsEvenly()
        {
            float[] keys = { 4f, 0f, 3f, 1f };
            int[] order = new int[keys.Length];
            Assert.True(OutlinerAlign.ComputeSpreadOrder(keys, keys.Length, order));
            Assert.Equal(new[] { 1, 3, 2, 0 }, order);
            Assert.Equal(0f, OutlinerAlign.SpreadTarget(keys, keys.Length, order, 0), 4);
            Assert.Equal(4f / 3f, OutlinerAlign.SpreadTarget(keys, keys.Length, order, 1), 4);
            Assert.Equal(8f / 3f, OutlinerAlign.SpreadTarget(keys, keys.Length, order, 2), 4);
            Assert.Equal(4f, OutlinerAlign.SpreadTarget(keys, keys.Length, order, 3), 4);
        }

        [Fact]
        public void SpreadRefusesFewerThanThreeAtomsAndFlatSelections()
        {
            float[] two = { 0f, 1f };
            Assert.False(OutlinerAlign.ComputeSpreadOrder(two, two.Length, new int[two.Length]));
            float[] flat = { 2f, 2f, 2f };
            Assert.False(OutlinerAlign.ComputeSpreadOrder(flat, flat.Length, new int[flat.Length]));
        }

        [Fact]
        public void AxisHelpersWriteOnlyTheNamedAxis()
        {
            Vector3 v = new Vector3(1f, 2f, 3f);
            Assert.Equal(2f, OutlinerAlign.Axis(v, 1));
            Assert.Equal(new Vector3(1f, 9f, 3f), OutlinerAlign.WithAxis(v, 1, 9f));
            Assert.Equal(new Vector3(1f, 2f, 3f), v);
        }

        [Fact]
        public void DefaultTreeShareStaysTheMinorGoldenSlice()
        {
            Assert.Equal(GalleryUiDesignTokens.GoldenRatioMinor, GalleryUiDesignTokens.OutlinerSplitTreeShareRef);
            Assert.Equal(0f, OutlinerSplitLayout.ClampTreeShare(0f));
            Assert.Equal(1f, OutlinerSplitLayout.ClampTreeShare(1f));
            Assert.Equal(
                GalleryUiDesignTokens.OutlinerSplitTreeShareRef,
                OutlinerSplitLayout.ClampTreeShare(GalleryUiDesignTokens.OutlinerSplitTreeShareRef));
        }

        [Fact]
        public void PersonAllowlistIncludesPluginManagerSoAddPluginIsNotBuried()
        {
            string[] allow = OutlinerParamCatalog.AllowlistForType("Person");
            Assert.NotNull(allow);
            bool found = false;
            for (int i = 0; i < allow.Length; i++)
            {
                if (string.Equals(allow[i], "PluginManager", StringComparison.Ordinal))
                    found = true;
            }
            Assert.True(found,
                "PluginManager must sit on the Person inspector allowlist so Create Plugin is one chip away.");
        }

        [Theory]
        [InlineData("PluginManager", "CreatePlugin", true)]
        [InlineData("PluginManager", "AddPlugin", true)]
        [InlineData("PluginManager", "SaveJSON", false)]
        [InlineData("geometry", "CreatePlugin", false)]
        public void CreatePluginActionIsRecognizedOnTheManagerOnly(
            string storableId, string paramId, bool expected)
        {
            Assert.Equal(expected, OutlinerPlugins.IsCreatePluginAction(storableId, paramId));
        }

        [Fact]
        public void DropHoverNamesTheSelectedAtomSoTheUserDoesNotGuess()
        {
            Assert.Equal("Add Embody to PersonX", OutlinerPlugins.DropHover("Embody", "PersonX"));
            Assert.Equal(
                "Select an atom in Scene Overview, then drop.",
                OutlinerPlugins.DropHover("Embody", ""));
        }

        [Fact]
        public void PopupWidthGrowsPastTheNarrowDefaultSoLongLabelsStayInside()
        {
            float w = OutlinerPlugins.PopupWidthRefForCharCount(37);
            Assert.True(w > GalleryUiDesignTokens.PopupMenuPanelWidthRef,
                "A Clothing/Hair menu line must not be forced into the 230px popup, which paints past the panel.");
            Assert.True(w >= GalleryUiDesignTokens.OverflowMenuPanelWidthRef,
                "Outliner menus share the overflow width floor, not the short grid popup.");
        }
        static DAZMorph PoseMorph(string name, string region, float value, bool pose = true)
        {
            var m = new DAZMorph();
            m.displayName = name;
            m.region = region;
            m.isPoseControl = pose;
            m.morphValue = value;
            return m;
        }

        [Theory]
        [InlineData("Pose Controls/Head/Expressions/AshAuryn/Sex", "Expressions")]
        [InlineData("Pose Controls/Head/Expressions", "Expressions")]
        [InlineData("Pose Controls/Hands/Left/Fingers", "Hands")]
        [InlineData("Pose Controls/Head/Eyes", "Face")]
        [InlineData("Pose Controls/Head/Mouth/Visemes", "Face")]
        [InlineData("Pose Controls/Arms/Forearm", "Arms")]
        [InlineData("Pose Controls/Legs/Feet", "Legs")]
        [InlineData("Pose Controls/Torso/Hip", "Torso")]
        [InlineData("Morph/Custom", "Other")]
        [InlineData("", "Other")]
        public void PoseMorphRegionsLandInTheBucketAUserWouldLookUnder(string region, string expected)
        {
            Assert.Equal(expected, OutlinerPoseMorphs.BucketOf(region).ToString());
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(0.005f, true)]
        [InlineData(-0.009f, true)]
        [InlineData(0.02f, false)]
        [InlineData(-0.3f, false)]
        public void NeutralToleranceIgnoresFloatDustButNotRealPoseValues(float value, bool neutral)
        {
            Assert.Equal(neutral, OutlinerPoseMorphs.IsNeutral(value));
        }

        [Fact]
        public void PoseCollectionSkipsAppearanceMorphsAndSortsStrongestFirstWithinABucket()
        {
            var morphs = new List<DAZMorph>
            {
                PoseMorph("Smile", "Pose Controls/Head/Expressions", 0.2f),
                PoseMorph("Breast Size", "Morph/Chest", 0.9f, pose: false),
                PoseMorph("Fist Left", "Pose Controls/Hands/Left/Fingers", -0.8f),
                PoseMorph("Frown", "Pose Controls/Head/Expressions", 0.7f),
                PoseMorph("Neutral Brow", "Pose Controls/Head/Brow", 0f)
            };
            var into = new List<OutlinerPoseMorphEntry>();
            var active = new int[(int)OutlinerPoseBucket.Count];
            var total = new int[(int)OutlinerPoseBucket.Count];
            int count = OutlinerPoseMorphs.CollectFrom(morphs, into, active, total);
            into.Sort((a, b) => a.Bucket != b.Bucket
                ? ((int)a.Bucket).CompareTo((int)b.Bucket)
                : Mathf.Abs(b.Value).CompareTo(Mathf.Abs(a.Value)));

            Assert.Equal(3, count);
            Assert.Equal(2, active[(int)OutlinerPoseBucket.Expressions]);
            Assert.Equal(1, active[(int)OutlinerPoseBucket.Hands]);
            Assert.Equal(0, active[(int)OutlinerPoseBucket.Face]);
            Assert.Equal(1, total[(int)OutlinerPoseBucket.Face]);
            Assert.Equal("Frown", into[0].Label);
            Assert.Equal("Smile", into[1].Label);
            Assert.Equal("Fist Left", into[2].Label);
            Assert.DoesNotContain(into, e => e.Label == "Breast Size");
        }

        [Fact]
        public void ZeroingPoseMorphsLeavesAppearanceAloneAndTheFooterUndoPutsThemBack()
        {
            var smile = PoseMorph("Smile", "Pose Controls/Head/Expressions", 0.6f);
            var fist = PoseMorph("Fist", "Pose Controls/Hands/Left/Fingers", -0.4f);
            var dust = PoseMorph("Blink", "Pose Controls/Head/Eyes", 0.004f);
            var shape = PoseMorph("Body Tone", "Morph/Full Body", 0.5f, pose: false);
            var morphs = new List<DAZMorph> { smile, fist, dust, shape };
            var snap = new OutlinerResetSnapshot();
            int driven = 0;

            int done = OutlinerPoseMorphs.ZeroFrom(morphs, OutlinerPoseMorphs.AllBuckets, snap, ref driven);

            Assert.Equal(3, done);
            Assert.Equal(0, driven);
            Assert.Equal(0f, smile.morphValue);
            Assert.Equal(0f, fist.morphValue);
            Assert.Equal(0f, dust.morphValue);
            Assert.Equal(0.5f, shape.morphValue);
            Assert.Equal(3, snap.MorphCount);
            Assert.False(snap.IsEmpty, "A zero with nothing to undo would hide the footer bar.");

            Assert.True(snap.Restore());
            Assert.Equal(0.6f, smile.morphValue);
            Assert.Equal(-0.4f, fist.morphValue);
        }

        [Fact]
        public void ZeroingOneBucketDoesNotTouchTheOthers()
        {
            var smile = PoseMorph("Smile", "Pose Controls/Head/Expressions", 0.6f);
            var fist = PoseMorph("Fist", "Pose Controls/Hands/Left/Fingers", -0.4f);
            var morphs = new List<DAZMorph> { smile, fist };
            int driven = 0;

            int done = OutlinerPoseMorphs.ZeroFrom(morphs, (int)OutlinerPoseBucket.Hands, null, ref driven);

            Assert.Equal(1, done);
            Assert.Equal(0.6f, smile.morphValue);
            Assert.Equal(0f, fist.morphValue);
        }

        [Fact]
        public void PoseSignatureChangesWhenAMorphLeavesNeutralSoTheCardRefreshesItself()
        {
            var smile = PoseMorph("Smile", "Pose Controls/Head/Expressions", 0f);
            var morphs = new List<DAZMorph> { smile };
            int activeA = 0;
            int before = OutlinerPoseMorphs.SignatureFrom(morphs, 17, ref activeA);
            smile.morphValue = 0.5f;
            int activeB = 0;
            int after = OutlinerPoseMorphs.SignatureFrom(morphs, 17, ref activeB);

            Assert.Equal(0, activeA);
            Assert.Equal(1, activeB);
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void SingleMorphUndoKeyAddressesTheGeometryFloatVaMRegisters()
        {
            Assert.Equal("Person|geometry|Smile", OutlinerPoseMorphs.UndoKey("Person", "Smile"));
            Assert.Equal("", OutlinerPoseMorphs.UndoKey("Person", "Odd|Name"));
            Assert.Equal("", OutlinerPoseMorphs.UndoKey("", "Smile"));
        }

        [Fact]
        public void PoseCardSitsBesideWornItemsInTheSectionNav()
        {
            var keys = new List<string>
            {
                OutlinerInspectorSections.Favourites,
                OutlinerInspectorSections.Pose,
                OutlinerInspectorSections.Look,
                OutlinerInspectorSections.Transform
            };
            OutlinerInspectorSections.SortKeys(keys);
            Assert.Equal(OutlinerInspectorSections.Look, keys[1]);
            Assert.Equal(OutlinerInspectorSections.Pose, keys[2]);
            Assert.Equal(OutlinerInspectorSections.Pose, OutlinerInspectorSections.KeyForCard("pose", "Person"));
            Assert.Equal("mood-neutral", OutlinerInspectorSections.Icon(OutlinerInspectorSections.Pose));
        }
    
        [Fact]
        public void GroupEyeAggregatesOnlyNonProtectedAtoms()
        {
            var a = Fact("Light1", "InvisibleLight", CreatorStripKeepKind.Lights);
            var b = Fact("Light2", "InvisibleLight", CreatorStripKeepKind.Lights);
            b.On = false;
            var c = Fact("CoreControl", "CoreControl", CreatorStripKeepKind.Lights);
            c.IsSystemProtected = true;
            OutlinerModel model = OutlinerModelBuilder.Build(
                new List<OutlinerAtomFacts> { a, b, c }, OutlinerGrouping.TypeGroups, "", CreatorStripKeepKind.None);
            OutlinerNode group = model.Find("group:" + ((int)CreatorStripKeepKind.Lights).ToString());
            Assert.NotNull(group);
            var atoms = new List<OutlinerNode>();
            OutlinerVisibility.CollectAtoms(model, group, atoms);
            int on, total;
            Assert.Equal(OutlinerEyeState.Mixed, OutlinerVisibility.Aggregate(atoms, out on, out total));
            Assert.Equal(1, on);
            Assert.Equal(2, total);
            Assert.False(OutlinerVisibility.NextOnFor(OutlinerEyeState.Mixed));
            Assert.False(OutlinerVisibility.NextOnFor(OutlinerEyeState.AllOn));
            Assert.True(OutlinerVisibility.NextOnFor(OutlinerEyeState.AllOff));
        }

        [Fact]
        public void BatchOnStatesRoundTripThroughUndoPayload()
        {
            var uids = new List<string> { "A/B", "Light 2", "x=y;z" };
            var on = new List<bool> { true, false, true };
            string payload = OutlinerVisibility.EncodeStates(uids, on);
            var u2 = new List<string>();
            var o2 = new List<bool>();
            Assert.Equal(3, OutlinerVisibility.DecodeStates(payload, u2, o2));
            Assert.Equal(uids, u2);
            Assert.Equal(on, o2);
            Assert.Equal(0, OutlinerVisibility.DecodeStates("", u2, o2));
        }
    }
}
