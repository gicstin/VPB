using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class LocaleSafetyTests
    {
        private readonly ITestOutputHelper _out;
        public LocaleSafetyTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private sealed class CultureSwap : IDisposable
        {
            private readonly CultureInfo _previous;

            public CultureSwap(string name)
            {
                _previous = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
            }

            public void Dispose()
            {
                Thread.CurrentThread.CurrentCulture = _previous;
            }
        }

        private static HashSet<string> NormalizedFields()
        {
            string path = Path.Combine(Path.Combine(TestEnvironment.RepoRoot, "tests"), "known-normalized-config-fields.txt");
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return set;
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t);
            }
            return set;
        }

        private static FieldInfo[] FractionalFloatFields()
        {
            HashSet<string> normalized = NormalizedFields();
            return typeof(VPBConfig).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && f.FieldType == typeof(float) && !normalized.Contains(f.Name))
                .ToArray();
        }

        private static float FractionalVariant(float v)
        {
            float candidate = v * 0.5f + 0.125f;
            if (Math.Abs(candidate - v) < 0.0005f) candidate = v + 0.375f;
            return candidate;
        }

        [Theory]
        [InlineData("en-US", "de-DE")]
        [InlineData("en-US", "fr-FR")]
        [InlineData("en-US", "ru-RU")]
        [InlineData("de-DE", "en-US")]
        public void FloatSettingsSurviveAChangeOfThreadLocaleBetweenSaveAndLoad(string saveCulture, string loadCulture)
        {
            using (var install = new TempInstall("locale-cfg"))
            {
                FieldInfo[] fields = FractionalFloatFields();
                var expected = new Dictionary<string, float>(StringComparer.Ordinal);

                using (new CultureSwap(saveCulture))
                {
                    string cfgPath = VPBConfig.Instance.ConfigPathForDebug;
                    try { if (File.Exists(cfgPath)) File.Delete(cfgPath); } catch { }
                    VPBConfig.ReloadFromDisk();
                    VPBConfig cfg = VPBConfig.Instance;
                    foreach (FieldInfo f in fields)
                    {
                        float v = FractionalVariant((float)f.GetValue(cfg));
                        f.SetValue(cfg, v);
                    }
                    cfg.Save();
                    VPBConfig.ReloadFromDisk();
                    foreach (FieldInfo f in fields)
                        expected[f.Name] = (float)f.GetValue(VPBConfig.Instance);
                }

                var drifted = new List<string>();
                using (new CultureSwap(loadCulture))
                {
                    VPBConfig.ReloadFromDisk();
                    VPBConfig reloaded = VPBConfig.Instance;
                    foreach (FieldInfo f in fields)
                    {
                        float want = expected[f.Name];
                        float got = (float)f.GetValue(reloaded);
                        if (Math.Abs(want - got) > 0.0005f)
                            drifted.Add(f.Name + "   saved=" + want.ToString("R", CultureInfo.InvariantCulture)
                                + "  loaded=" + got.ToString("R", CultureInfo.InvariantCulture));
                    }
                }

                _out.WriteLine("float settings checked: " + fields.Length);
                Assert.True(drifted.Count == 0,
                    "VPB.cfg was written under " + saveCulture + " and read back under " + loadCulture + "." + Environment.NewLine +
                    "The plugin loads its config in Awake, before VaM switches the main thread to en-US, so on a" + Environment.NewLine +
                    "German, French or Russian Windows these settings come back scaled or zeroed every launch:" + Environment.NewLine +
                    HarmonyPatchTargetTests.Bullets(drifted));
            }
        }

        [Fact]
        public void ConfigWrittenByAnOlderBuildWithDecimalCommasStillLoads()
        {
            using (var install = new TempInstall("locale-legacy"))
            {
                string cfgPath = VPBConfig.Instance.ConfigPathForDebug;
                File.WriteAllText(cfgPath,
                    "{ \"GalleryOpacity\" : \"0,625\", \"BringToFrontDistance\" : \"1,5\", " +
                    "\"HiddenCategories\" : \"Scenes,Looks\", \"LastGalleryCategory\" : \"Clothing\" }");

                using (new CultureSwap("en-US"))
                    VPBConfig.ReloadFromDisk();
                VPBConfig cfg = VPBConfig.Instance;

                Assert.True(Math.Abs(cfg.GalleryOpacity - 0.625f) < 0.0005f && Math.Abs(cfg.BringToFrontDistance - 1.5f) < 0.0005f,
                    "A VPB.cfg saved by an older build on a German or French system stores 0,625 instead of 0.625." + Environment.NewLine +
                    "Loading it must keep the setting, not read the comma as a thousands separator:" + Environment.NewLine +
                    "  GalleryOpacity=" + cfg.GalleryOpacity.ToString("R", CultureInfo.InvariantCulture) +
                    "  BringToFrontDistance=" + cfg.BringToFrontDistance.ToString("R", CultureInfo.InvariantCulture));
                Assert.True(cfg.IsHiddenCategory("Scenes") && cfg.IsHiddenCategory("Looks") && cfg.LastGalleryCategory == "Clothing",
                    "Repairing decimal commas must not touch text settings: hidden categories or the last category changed.");
            }
        }

        [Theory]
        [InlineData("en-US", "de-DE")]
        [InlineData("de-DE", "en-US")]
        [InlineData("en-US", "fr-FR")]
        public void LayoutPresetGeometrySurvivesAChangeOfThreadLocale(string saveCulture, string loadCulture)
        {
            var preset = new GalleryLayoutPreset();
            preset.Global.DockRight.Occupied = true;
            preset.Global.DockRight.WidthFree = 0.4375f;
            preset.Global.DockRight.CustomHeight = 0.8125f;
            preset.Global.InnerPaneScale = 1.25f;
            preset.Global.AutoHideSeconds = 2.5f;
            string json;
            using (new CultureSwap(saveCulture))
                json = preset.ToJsonString();

            GalleryLayoutPreset back;
            using (new CultureSwap(loadCulture))
                back = GalleryLayoutPreset.FromJsonString(json);

            bool same = back != null
                && Math.Abs(back.Global.DockRight.WidthFree - 0.4375f) < 0.0001f
                && Math.Abs(back.Global.DockRight.CustomHeight - 0.8125f) < 0.0001f
                && Math.Abs(back.Global.InnerPaneScale - 1.25f) < 0.0001f
                && Math.Abs(back.Global.AutoHideSeconds - 2.5f) < 0.0001f;
            Assert.True(same,
                "A layout preset saved under " + saveCulture + " restored the dock at the wrong size or scale under " + loadCulture + "." + Environment.NewLine +
                "Payload: " + json + Environment.NewLine +
                (back == null ? "Restored: <null>" :
                    "Restored: WidthFree=" + back.Global.DockRight.WidthFree.ToString("R", CultureInfo.InvariantCulture) +
                    " CustomHeight=" + back.Global.DockRight.CustomHeight.ToString("R", CultureInfo.InvariantCulture) +
                    " InnerPaneScale=" + back.Global.InnerPaneScale.ToString("R", CultureInfo.InvariantCulture) +
                    " AutoHideSeconds=" + back.Global.AutoHideSeconds.ToString("R", CultureInfo.InvariantCulture)));
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void OutlinerUndoRestoresTheExactTransform(string culture)
        {
            var original = new Vector3(0.123456f, -7.891011f, 12.34567f);
            string payload;
            Vector3 restored;
            bool parsed;
            using (new CultureSwap(culture))
            {
                payload = GalleryPanel.FormatOutlinerVector3(original);
                parsed = GalleryPanel.TryParseOutlinerVector3(payload, out restored);
            }

            Assert.True(parsed && restored.x == original.x && restored.y == original.y && restored.z == original.z,
                "Undoing an outliner move under " + culture + " does not put the atom back where it was." + Environment.NewLine +
                "Recorded payload: " + payload + Environment.NewLine +
                "Original:  " + original.x.ToString("R", CultureInfo.InvariantCulture) + ", " + original.y.ToString("R", CultureInfo.InvariantCulture) + ", " + original.z.ToString("R", CultureInfo.InvariantCulture) + Environment.NewLine +
                "Restored:  " + (parsed ? restored.x.ToString("R", CultureInfo.InvariantCulture) + ", " + restored.y.ToString("R", CultureInfo.InvariantCulture) + ", " + restored.z.ToString("R", CultureInfo.InvariantCulture) : "<parse failed>"));
        }

        [Theory]
        [InlineData("en-US", 0.123456f)]
        [InlineData("de-DE", 0.123456f)]
        [InlineData("fr-FR", -2.718281f)]
        [InlineData("en-US", 1234.5677f)]
        public void OutlinerUndoRestoresTheExactParameterValue(string culture, float original)
        {
            string payload;
            float restored;
            bool parsed;
            using (new CultureSwap(culture))
            {
                payload = GalleryPanel.FormatOutlinerFloat(original);
                parsed = GalleryPanel.TryParseOutlinerFloat(payload, out restored);
            }

            Assert.True(parsed && restored == original,
                "Undoing a slider or scale edit under " + culture + " restores a different value than the one it replaced." + Environment.NewLine +
                "Recorded payload: " + payload + Environment.NewLine +
                "Original: " + original.ToString("R", CultureInfo.InvariantCulture) +
                "  Restored: " + (parsed ? restored.ToString("R", CultureInfo.InvariantCulture) : "<parse failed>"));
        }

        [Theory]
        [InlineData("en-US", "1.5", 1.5f)]
        [InlineData("de-DE", "1.5", 1.5f)]
        [InlineData("de-DE", "1,5", 1.5f)]
        [InlineData("fr-FR", "0.25", 0.25f)]
        [InlineData("en-US", "-3,75", -3.75f)]
        [InlineData("en-US", " 42 ", 42f)]
        public void TypedNumbersMeanTheSameInEveryLocale(string culture, string typed, float expected)
        {
            float value;
            bool ok;
            using (new CultureSwap(culture))
                ok = VpbNumberText.TryParseFloat(typed, out value);

            Assert.True(ok && Math.Abs(value - expected) < 0.00001f,
                "Typing \"" + typed + "\" into a numeric field under " + culture + " should mean " +
                expected.ToString("R", CultureInfo.InvariantCulture) + ", got " +
                (ok ? value.ToString("R", CultureInfo.InvariantCulture) : "<rejected>") + ".");
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1,000.5")]
        [InlineData("1.2.3")]
        public void AmbiguousOrJunkNumericInputIsRejected(string typed)
        {
            float value;
            Assert.False(VpbNumberText.TryParseFloat(typed, out value),
                "\"" + typed + "\" was accepted as " + value.ToString("R", CultureInfo.InvariantCulture) +
                "; a numeric field must leave the current value alone rather than guess.");
        }
    }
}
