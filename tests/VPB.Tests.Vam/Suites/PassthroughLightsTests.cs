using System;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PassthroughLightsTests
    {
        public PassthroughLightsTests(VamFixture vam) { }

        [Theory]
        [InlineData(null, VpbPassthrough.HideAllButPeople)]
        [InlineData("", VpbPassthrough.HideAllButPeople)]
        [InlineData("nope", VpbPassthrough.HideAllButPeople)]
        [InlineData("Nothing", VpbPassthrough.HideNothing)]
        [InlineData("nothing", VpbPassthrough.HideNothing)]
        [InlineData("People only", VpbPassthrough.HideAllButPeople)]
        [InlineData("people only", VpbPassthrough.HideAllButPeople)]
        [InlineData("Environment", VpbPassthrough.HideEnvironment)]
        public void HideSceneUnknownValuesFallBackToPeople(string raw, string expected)
        {
            string got = VPBConfig.NormalizePassthroughHideScene(raw);
            Assert.Equal(expected, got);
        }

        [Fact]
        public void FreshConfigCutsOutWallsAndObjectsByDefault()
        {
            using (new TempInstall("pt_hide_default_people"))
            {
                Assert.True(
                    string.Equals(VPBConfig.Instance.PassthroughHideScene, VpbPassthrough.HideAllButPeople, StringComparison.Ordinal),
                    "Cut-out must start on People so walls and objects leave the key and only person atoms stay.");
            }
        }

        [Theory]
        [InlineData("CustomUnityAsset", "Misc", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Cube", "Misc", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Wall", "Furniture", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Floor", "", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Ceiling", "", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Chair", "Furniture", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Environment", "", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Image", "Misc", VpbPassthrough.HideEnvironment, true)]
        [InlineData("UIButton", "", VpbPassthrough.HideEnvironment, true)]
        [InlineData("Person", "People", VpbPassthrough.HideEnvironment, false)]
        [InlineData("InvisibleLight", "Lights", VpbPassthrough.HideEnvironment, false)]
        [InlineData("Light", "Lights", VpbPassthrough.HideEnvironment, false)]
        [InlineData("Empty", "Misc", VpbPassthrough.HideEnvironment, false)]
        [InlineData("CoreControl", "", VpbPassthrough.HideEnvironment, false)]
        [InlineData("SubScene", "", VpbPassthrough.HideEnvironment, false)]
        [InlineData("CycleForce", "", VpbPassthrough.HideEnvironment, false)]
        [InlineData("CustomUnityAsset", "Misc", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("Cube", "Misc", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("Wall", "Furniture", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("Dildo", "", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("AnimationPattern", "", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("UIButton", "", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("Poster", "", VpbPassthrough.HideAllButPeople, true)]
        [InlineData("Person", "People", VpbPassthrough.HideAllButPeople, false)]
        [InlineData("InvisiblePerson", "People", VpbPassthrough.HideAllButPeople, false)]
        [InlineData("InvisibleLight", "Lights", VpbPassthrough.HideAllButPeople, false)]
        [InlineData("CustomUnityAsset", "Misc", VpbPassthrough.HideNothing, false)]
        public void RoomsHideHidesWallsAndSetsButKeepsPeopleAndLights(string type, string category, string mode, bool hide)
        {
            bool got = VpbPassthrough.ShouldHideType(type, category, mode);
            Assert.True(got == hide, hide
                ? "Passthrough must cut '" + type + "' out of the key or walls and objects stay in the headset and block the room."
                : "Passthrough must not hide '" + type + "' or people/lights/core atoms would vanish with the room.");
        }

        [Fact]
        public void SavedLightSlotsReloadWithTheSameOnOffAndPosition()
        {
            using (new TempInstall("pt_lights_spec"))
            {
                VPBConfig.Instance.PassthroughLightCount = 0;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|1.25|2|0.5|2|3200|6;0|0|1.9|-1.4|1.4|4500|8;1|-0.4|1.5|1.1|0.8|2700|4;0|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();

                VpbPassthroughLights.LightSlot a = VpbPassthroughLights.GetSlot(0);
                Assert.True(a.Enabled, "Light 1 was on when you left; it must still be on after a reload.");
                Assert.Equal(1.25f, a.X, 3);
                Assert.Equal(2f, a.Y, 3);
                Assert.Equal(0.5f, a.Z, 3);
                Assert.Equal(2f, a.Intensity, 3);
                float h3200, s3200, v3200;
                VpbPassthroughLights.KelvinToHsv(3200f, out h3200, out s3200, out v3200);
                Assert.Equal(h3200, a.ColorH, 3);
                Assert.Equal(s3200, a.ColorS, 3);
                Assert.Equal(v3200, a.ColorV, 3);
                Assert.Equal(6f, a.Range, 3);

                VpbPassthroughLights.LightSlot b = VpbPassthroughLights.GetSlot(1);
                Assert.False(b.Enabled, "Light 2 was off when you left; it must stay off after a reload.");

                VpbPassthroughLights.LightSlot c = VpbPassthroughLights.GetSlot(2);
                Assert.True(c.Enabled, "Light 3 was on when you left; it must still be on after a reload.");
                Assert.Equal(-0.4f, c.X, 3);
                float h2700, s2700, v2700;
                VpbPassthroughLights.KelvinToHsv(2700f, out h2700, out s2700, out v2700);
                Assert.Equal(h2700, c.ColorH, 3);
                Assert.Equal(s2700, c.ColorS, 3);
                Assert.Equal(v2700, c.ColorV, 3);

                string[] written = VpbPassthroughLights.Serialize().Split(';')[0].Split('|');
                Assert.True(written.Length == 9,
                    "After a reload the saved spec must store colour as HSV, not Kelvin, or the next launch would fight VaM's light panel.");
            }
        }

        [Fact]
        public void HsvSpecKeepsNativeColourChannels()
        {
            using (new TempInstall("pt_lights_hsv"))
            {
                VPBConfig.Instance.PassthroughLightCount = 0;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|1.25|2|0.5|2|0.08|0.4|0.9|6;0|0|1.9|-1.4|1.4|0|0|1|8;1|-0.4|1.5|1.1|0.8|0.3|0.5|0.7|4;0|0|1.9|-1.4|1.4|0|0|1|8";
                VpbPassthroughLights.InvalidateSlots();

                VpbPassthroughLights.LightSlot a = VpbPassthroughLights.GetSlot(0);
                Assert.True(a.Enabled, "Light 1 was on when you left; it must still be on after a reload.");
                Assert.Equal(0.08f, a.ColorH, 3);
                Assert.Equal(0.4f, a.ColorS, 3);
                Assert.Equal(0.9f, a.ColorV, 3);
                Assert.Equal(6f, a.Range, 3);

                VpbPassthroughLights.LightSlot c = VpbPassthroughLights.GetSlot(2);
                Assert.Equal(0.3f, c.ColorH, 3);
                Assert.Equal(0.5f, c.ColorS, 3);
                Assert.Equal(0.7f, c.ColorV, 3);
            }
        }

        [Fact]
        public void LightCountAndLayoutComeBackOnTheNextRun()
        {
            using (new TempInstall("pt_lights_count"))
            {
                VPBConfig.Instance.PassthroughLightCount = 3;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|1.25|2|0.5|2|3200|6;1|0.4|1.7|-0.8|1.4|4500|8;1|-0.4|1.5|1.1|0.8|2700|4;0|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();

                Assert.Equal(3, VpbPassthroughLights.GetActiveCount());
                Assert.True(VpbPassthroughLights.GetSlot(0).Enabled, "Raising Lights to 3 must leave light 1 on after a relaunch.");
                Assert.True(VpbPassthroughLights.GetSlot(1).Enabled, "Raising Lights to 3 must leave light 2 on after a relaunch.");
                Assert.True(VpbPassthroughLights.GetSlot(2).Enabled, "Raising Lights to 3 must leave light 3 on after a relaunch.");
                Assert.False(VpbPassthroughLights.GetSlot(3).Enabled, "Light 4 was unused; it must stay off after a relaunch.");
                Assert.Equal(1.25f, VpbPassthroughLights.GetSlot(0).X, 3);
                Assert.Equal(0.4f, VpbPassthroughLights.GetSlot(1).X, 3);
                Assert.Equal(-0.4f, VpbPassthroughLights.GetSlot(2).X, 3);

                VPBConfig.Instance.PassthroughLightCount = 2;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|1.25|2|0.5|2|3200|6;1|0.4|1.7|-0.8|1.4|4500|8;0|-0.4|1.5|1.1|0.8|2700|4;0|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();
                Assert.Equal(2, VpbPassthroughLights.GetActiveCount());
                Assert.False(VpbPassthroughLights.GetSlot(2).Enabled, "Dropping to 2 lights must still be 2 after the cfg is read again.");
                Assert.Equal(-0.4f, VpbPassthroughLights.GetSlot(2).X, 3);
            }
        }

        [Fact]
        public void RaisingTheCountTurnsOnEachNewLightWithAUsableIntensity()
        {
            using (new TempInstall("pt_lights_count_intensity"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(1);
                var dark = VpbPassthroughLights.GetSlot(1);
                dark.Intensity = 0f;
                dark.Range = VpbPassthroughLights.MinRange;
                VpbPassthroughLights.SetSlot(1, dark);
                VpbPassthroughLights.SetActiveCount(3);
                Assert.True(VpbPassthroughLights.GetSlot(1).Enabled,
                    "Lights=3 must turn on light 2, or only the first light would illuminate the person.");
                Assert.True(VpbPassthroughLights.GetSlot(2).Enabled,
                    "Lights=3 must turn on light 3, or only the first light would illuminate the person.");
                Assert.True(VpbPassthroughLights.GetSlot(1).Intensity > 0.05f,
                    "A newly enabled light must actually emit. A zero leftover from when it was unused would leave that slot dark.");
                Assert.True(VpbPassthroughLights.GetSlot(2).Intensity > 0.05f,
                    "A newly enabled light must actually emit. A zero leftover from when it was unused would leave that slot dark.");
            }
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(4, 4)]
        [InlineData(9, 4)]
        [InlineData(-2, 1)]
        public void LightCountUnknownValuesFallBackInsideOneToFour(int raw, int expected)
        {
            Assert.Equal(expected, VPBConfig.NormalizePassthroughLightCount(raw));
        }

        [Theory]
        [InlineData("UltraLow", 4, "Low")]
        [InlineData("Low", 4, "Low")]
        [InlineData("Mid", 4, "Medium")]
        [InlineData("High", 1, "High")]
        [InlineData("Ultra", 1, "VeryHigh")]
        [InlineData("Max", 0, "VeryHigh")]
        [InlineData("", 0, "Low")]
        [InlineData("", 1, "Low")]
        [InlineData("Custom", 2, "Medium")]
        [InlineData("Custom", 3, "High")]
        [InlineData("Custom", 4, "VeryHigh")]
        public void ShadowResolutionFollowsVaMQuality(string quality, int pixelLights, string expected)
        {
            string got = VpbPassthroughLights.ShadowResolutionForQualityLevel(quality, pixelLights);
            Assert.True(string.Equals(got, expected, StringComparison.Ordinal),
                "A new room light's shadow quality must follow VaM's quality setting, not a hard-coded VeryHigh or Off. Quality '"
                + quality + "' pixel lights " + pixelLights + " should be " + expected + ", not " + got + ".");
        }

        [Fact]
        public void ARunawayPositionIsClampedToThePlaySpace()
        {
            using (new TempInstall("pt_lights_clamp"))
            {
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|99|-1|-99|1.4|4500|8;0|0|1.9|-1.4|1.4|4500|8;0|0|1.9|-1.4|1.4|4500|8;0|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();

                VpbPassthroughLights.LightSlot got = VpbPassthroughLights.GetSlot(0);
                Assert.Equal(VpbPassthroughLights.MaxHorizontalOffset, got.X);
                Assert.Equal(0f, got.Y);
                Assert.Equal(-VpbPassthroughLights.MaxHorizontalOffset, got.Z);
            }
        }

        [Fact]
        public void EditLightSelectionStaysInsideTheActiveLights()
        {
            using (new TempInstall("pt_lights_select"))
            {
                VPBConfig.Instance.PassthroughLightCount = 4;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|0|1.9|1.2|1.4|4500|8;1|1.2|1.9|-0.4|1.4|4500|8;1|-1.2|1.9|-0.4|1.4|4500|8;1|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.Select(99);
                Assert.Equal(3, VpbPassthroughLights.SelectedIndex);
                VpbPassthroughLights.Select(-3);
                Assert.Equal(0, VpbPassthroughLights.SelectedIndex);

                VPBConfig.Instance.PassthroughLightCount = 2;
                VPBConfig.Instance.PassthroughLightsSpec =
                    "1|0|1.9|1.2|1.4|4500|8;1|1.2|1.9|-0.4|1.4|4500|8;0|-1.2|1.9|-0.4|1.4|4500|8;0|0|1.9|-1.4|1.4|4500|8";
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.Select(3);
                Assert.Equal(1, VpbPassthroughLights.SelectedIndex);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("VPB_RoomLight1", true)]
        [InlineData("VPB_RoomLight4", true)]
        [InlineData("VPB_RoomLight5", false)]
        [InlineData("VPB_RoomLight", false)]
        [InlineData("VPB_KeyLight", false)]
        [InlineData("InvisibleLight", false)]
        [InlineData("Light", false)]
        public void OnlyTheFourReservedRoomLightUidsAreOwned(string uid, bool owned)
        {
            bool got = VpbPassthroughLights.IsOwnedUid(uid);
            Assert.True(got == owned, owned
                ? "UID '" + uid + "' is a reserved room light and must be treated as one, or it would be saved into the scene."
                : "UID '" + (uid ?? "(null)") + "' is not a room light and must not be claimed as one, or a real atom would vanish from scene saves.");
        }

        [Fact]
        public void RoomLightAtomUidsAreStableOneThroughFour()
        {
            Assert.Equal("VPB_RoomLight1", VpbPassthroughLights.AtomUid(0));
            Assert.Equal("VPB_RoomLight4", VpbPassthroughLights.AtomUid(3));
            Assert.True(string.IsNullOrEmpty(VpbPassthroughLights.AtomUid(-1)),
                "There is no room light 0; a bad index must not invent a UID that could collide with a scene atom.");
            Assert.True(string.IsNullOrEmpty(VpbPassthroughLights.AtomUid(4)),
                "There is no room light 5; a bad index must not invent a UID that could collide with a scene atom.");
        }

        [Theory]
        [InlineData(0, "Front lamp")]
        [InlineData(1, "Right lamp")]
        [InlineData(2, "Left lamp")]
        [InlineData(3, "Back lamp")]
        [InlineData(-1, "")]
        [InlineData(4, "")]
        public void RealWorldLampsAreNamedByPlaceInTheRoom(int index, string expected)
        {
            Assert.Equal(expected, VpbPassthroughLights.SlotName(index));
        }

        [Theory]
        [InlineData("Front lamp", 0)]
        [InlineData("Right lamp", 1)]
        [InlineData("Left lamp", 2)]
        [InlineData("Back lamp", 3)]
        [InlineData("Light 1", 0)]
        [InlineData("Light 4", 3)]
        [InlineData("light 2", 1)]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("nope", 0)]
        public void LampFocusNameMapsToTheSameSlot(string name, int expected)
        {
            Assert.Equal(expected, VpbPassthroughLights.SlotIndexFromName(name));
        }

        [Fact]
        public void BringHereDoesNotMoveALightThatIsOffOrTheOthers()
        {
            using (new TempInstall("pt_lights_bring_one"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(2);
                var a = VpbPassthroughLights.GetSlot(0);
                a.X = 0.4f;
                a.Y = 1.5f;
                a.Z = 0.2f;
                VpbPassthroughLights.SetSlot(0, a);
                var b = VpbPassthroughLights.GetSlot(1);
                b.X = -0.6f;
                b.Y = 1.8f;
                b.Z = 0.9f;
                VpbPassthroughLights.SetSlot(1, b);
                var off = VpbPassthroughLights.GetSlot(3);
                float ox = off.X, oy = off.Y, oz = off.Z;

                VpbPassthroughLights.BringSlotToPlayer(3);
                VpbPassthroughLights.BringSlotToPlayer(-1);
                VpbPassthroughLights.BringSlotToPlayer(99);

                VpbPassthroughLights.LightSlot stillOff = VpbPassthroughLights.GetSlot(3);
                Assert.True(Math.Abs(stillOff.X - ox) < 0.001f && Math.Abs(stillOff.Y - oy) < 0.001f && Math.Abs(stillOff.Z - oz) < 0.001f,
                    "Bring here is per light. An unused light must stay put, not jump in front of you with the others.");
                Assert.True(Math.Abs(VpbPassthroughLights.GetSlot(0).X - 0.4f) < 0.001f,
                    "Bring here on another light must not drag light 1 along with it.");
                Assert.True(Math.Abs(VpbPassthroughLights.GetSlot(1).X + 0.6f) < 0.001f && Math.Abs(VpbPassthroughLights.GetSlot(1).Z - 0.9f) < 0.001f,
                    "Bring here on another light must not drag light 2 along with it.");
            }
        }

        [Fact]
        public void ANullControllerIsNotTreatedAsARoomLight()
        {
            Assert.False(VpbPassthroughLights.IsOwnedController(null),
                "A missing controller must not be claimed as a room light or VaM would hide every handle in the scene.");
        }

        [Fact]
        public void LastLayoutSurvivesAConfigReload()
        {
            using (new TempInstall("pt_lights_last_layout"))
            {
                VPBConfig.Instance.PassthroughLightCount = 2;
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(2);
                var a = new VpbPassthroughLights.LightSlot
                {
                    Enabled = true,
                    X = 1.1f,
                    Y = 2.2f,
                    Z = -0.7f,
                    Intensity = 3.25f,
                    ColorH = 0.12f,
                    ColorS = 0.44f,
                    ColorV = 0.81f,
                    Range = 7.5f
                };
                var b = new VpbPassthroughLights.LightSlot
                {
                    Enabled = true,
                    X = -0.9f,
                    Y = 1.6f,
                    Z = 1.05f,
                    Intensity = 0.6f,
                    ColorH = 0.55f,
                    ColorS = 0.3f,
                    ColorV = 0.95f,
                    Range = 4f
                };
                VpbPassthroughLights.SetSlot(0, a);
                VpbPassthroughLights.SetSlot(1, b);
                VpbPassthroughLights.FlushToDisk();

                Assert.False(string.IsNullOrEmpty(VPBConfig.Instance.PassthroughLightsSpec),
                    "The current lights must be written to disk or the next launch would come up on the defaults.");
                Assert.Equal(2, VPBConfig.Instance.PassthroughLightCount);

                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.LightSlot gotA = VpbPassthroughLights.GetSlot(0);
                Assert.Equal(1.1f, gotA.X, 3);
                Assert.Equal(2.2f, gotA.Y, 3);
                Assert.Equal(-0.7f, gotA.Z, 3);
                Assert.Equal(3.25f, gotA.Intensity, 3);
                Assert.Equal(0.12f, gotA.ColorH, 3);
                Assert.Equal(0.44f, gotA.ColorS, 3);
                Assert.Equal(0.81f, gotA.ColorV, 3);
                Assert.Equal(7.5f, gotA.Range, 3);
                VpbPassthroughLights.LightSlot gotB = VpbPassthroughLights.GetSlot(1);
                Assert.True(gotB.Enabled, "Light 2 was on; it must still be on after a relaunch.");
                Assert.Equal(-0.9f, gotB.X, 3);
                Assert.Equal(0.55f, gotB.ColorH, 3);
            }
        }

        [Fact]
        public void SavedPresetRestoresCountPlaceLookAndFlags()
        {
            using (new TempInstall("pt_lights_preset"))
            {
                VPBConfig.Instance.PassthroughLightCount = 3;
                VPBConfig.Instance.PassthroughLightsMoveAsGroup = true;
                VPBConfig.Instance.PassthroughLightsOverrideScene = false;
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(3);
                var slot = new VpbPassthroughLights.LightSlot
                {
                    Enabled = true,
                    X = 0.8f,
                    Y = 1.7f,
                    Z = 1.1f,
                    Intensity = 2.1f,
                    ColorH = 0.33f,
                    ColorS = 0.6f,
                    ColorV = 0.7f,
                    Range = 9f
                };
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TrySavePreset("Living room"),
                    "A named layout must save so you can get that room lighting back later.");
                Assert.Equal("Living room", VpbPassthroughLights.SelectedPresetName());

                VPBConfig.Instance.PassthroughLightsMoveAsGroup = false;
                VPBConfig.Instance.PassthroughLightsOverrideScene = true;
                VpbPassthroughLights.SetActiveCount(1);
                slot.X = 0f;
                slot.Intensity = 0.1f;
                slot.ColorH = 0.01f;
                VpbPassthroughLights.SetSlot(0, slot);

                Assert.True(VpbPassthroughLights.TryLoadPreset("Living room"),
                    "Load must bring back the saved lights, not leave the mutated layout in place.");
                Assert.Equal(3, VpbPassthroughLights.GetActiveCount());
                VpbPassthroughLights.LightSlot got = VpbPassthroughLights.GetSlot(0);
                Assert.Equal(0.8f, got.X, 3);
                Assert.Equal(1.7f, got.Y, 3);
                Assert.Equal(2.1f, got.Intensity, 3);
                Assert.Equal(0.33f, got.ColorH, 3);
                Assert.Equal(0.6f, got.ColorS, 3);
                Assert.Equal(9f, got.Range, 3);
                Assert.True(VPBConfig.Instance.PassthroughLightsMoveAsGroup,
                    "Move together was on in that preset; loading it must turn it back on.");
                Assert.False(VPBConfig.Instance.PassthroughLightsOverrideScene,
                    "Replace scene lights was off in that preset; loading it must leave scene lights on.");
            }
        }

        [Fact]
        public void SavingAgainOverwritesTheSamePresetName()
        {
            using (new TempInstall("pt_lights_preset_overwrite"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(1);
                var slot = VpbPassthroughLights.GetSlot(0);
                slot.X = 1f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TrySavePreset("Corner"));
                slot.X = 2f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TrySavePreset("Corner"));
                slot.X = 0f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TryLoadPreset("Corner"));
                Assert.Equal(2f, VpbPassthroughLights.GetSlot(0).X, 3);
                Assert.Single(VpbPassthroughLights.PresetNames());
            }
        }

        [Fact]
        public void LastUsedPresetAppliesOverTheLiveLayout()
        {
            using (new TempInstall("pt_lights_last_preset"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(2);
                var slot = VpbPassthroughLights.GetSlot(0);
                slot.X = 0.8f;
                slot.Y = 1.7f;
                slot.Intensity = 2.1f;
                slot.ColorH = 0.33f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TrySavePreset("Kitchen"),
                    "A named layout must save so the next launch can put it back without pressing Load.");

                slot.X = 0f;
                slot.Intensity = 0.1f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TryApplySelectedPreset(),
                    "The last saved or loaded preset must apply on its own, or the room would come up on whatever was left in the live slots.");
                VpbPassthroughLights.LightSlot got = VpbPassthroughLights.GetSlot(0);
                Assert.Equal(0.8f, got.X, 3);
                Assert.Equal(1.7f, got.Y, 3);
                Assert.Equal(2.1f, got.Intensity, 3);
                Assert.Equal(0.33f, got.ColorH, 3);
            }
        }

        [Fact]
        public void MissingLastPresetLeavesTheLiveLightsAlone()
        {
            using (new TempInstall("pt_lights_last_preset_missing"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(1);
                var slot = VpbPassthroughLights.GetSlot(0);
                slot.X = 0.5f;
                VpbPassthroughLights.SetSlot(0, slot);
                VPBConfig.Instance.PassthroughLightPresetSelected = "NoSuch";
                Assert.False(VpbPassthroughLights.TryApplySelectedPreset(),
                    "A missing last-preset name must not wipe the lights you already placed.");
                Assert.Equal(0.5f, VpbPassthroughLights.GetSlot(0).X, 3);
            }
        }

        [Fact]
        public void LastPresetOnceDoesNotReapplyAfterTheFirstTime()
        {
            using (new TempInstall("pt_lights_last_preset_once"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(1);
                var slot = VpbPassthroughLights.GetSlot(0);
                slot.X = 0.8f;
                VpbPassthroughLights.SetSlot(0, slot);
                Assert.True(VpbPassthroughLights.TrySavePreset("Kitchen"));
                slot.X = 0f;
                VpbPassthroughLights.SetSlot(0, slot);
                VpbPassthroughLights.TryApplyLastPresetOnce();
                Assert.Equal(0.8f, VpbPassthroughLights.GetSlot(0).X, 3);
                slot.X = 0.2f;
                VpbPassthroughLights.SetSlot(0, slot);
                VpbPassthroughLights.TryApplyLastPresetOnce();
                Assert.True(Math.Abs(VpbPassthroughLights.GetSlot(0).X - 0.2f) < 0.001f,
                    "Last preset must apply once on start, not stomp lights you move afterwards.");
            }
        }

        [Fact]
        public void EmptyPresetNameBecomesLightsOne()
        {
            using (new TempInstall("pt_lights_preset_auto"))
            {
                VpbPassthroughLights.InvalidateSlots();
                Assert.True(VpbPassthroughLights.TrySavePreset(""));
                Assert.Equal("Lights 1", VpbPassthroughLights.SelectedPresetName());
            }
        }

        [Fact]
        public void DeletingAPresetRemovesItAndLeavesTheLiveLights()
        {
            using (new TempInstall("pt_lights_preset_delete"))
            {
                VpbPassthroughLights.InvalidateSlots();
                VpbPassthroughLights.SetActiveCount(2);
                Assert.True(VpbPassthroughLights.TrySavePreset("Keep"));
                Assert.True(VpbPassthroughLights.TrySavePreset("Drop"));
                Assert.True(VpbPassthroughLights.TryDeletePreset("Drop"));
                string[] names = VpbPassthroughLights.PresetNames();
                Assert.Single(names);
                Assert.Equal("Keep", names[0]);
                Assert.Equal(2, VpbPassthroughLights.GetActiveCount());
            }
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("  ", "")]
        [InlineData("(none)", "")]
        [InlineData("Living room", "Living room")]
        [InlineData("  Kitchen\nspot  ", "Kitchen spot")]
        public void PresetNamesAreTrimmedAndRefuseTheNoneLabel(string raw, string expected)
        {
            Assert.Equal(expected, VpbPassthroughLights.SanitizePresetName(raw));
        }
    }
}
