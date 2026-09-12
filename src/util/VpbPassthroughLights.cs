using System;
using System.Collections.Generic;
using System.Globalization;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public static class VpbPassthroughLights
    {
        public const int MaxLights = 4;
        public const int MaxPresets = 16;
        public const string NoPresetName = "(none)";

        public const float MinIntensity = 0f;
        public const float MaxIntensity = 8f;
        public const float MinTemperature = 1500f;
        public const float MaxTemperature = 12000f;
        public const float MinRange = 0.5f;
        public const float MaxRange = 25f;

        public const float MaxHorizontalOffset = 10f;
        public const float MaxHeight = 10f;

        public const string ShadowResLow = "Low";
        public const string ShadowResMedium = "Medium";
        public const string ShadowResHigh = "High";
        public const string ShadowResVeryHigh = "VeryHigh";

        public static readonly string[] SlotNames =
        {
            "Front lamp", "Right lamp", "Left lamp", "Back lamp",
        };

        public struct LightSlot
        {
            public bool Enabled;
            public float X;
            public float Y;
            public float Z;
            public float Intensity;
            public float ColorH;
            public float ColorS;
            public float ColorV;
            public float Range;
        }

        public const string AtomUidPrefix = VpbPassthroughLightAtoms.UidPrefix;

        private static readonly LightSlot[] s_slots = new LightSlot[MaxLights];
        private sealed class MutedSceneLight
        {
            public Atom Atom;
            public JSONStorableBool OnParam;
            public Light UnityLight;
            public bool WasOn;
        }

        private static readonly List<MutedSceneLight> s_mutedSceneLights = new List<MutedSceneLight>(32);

        private static bool s_slotsLoaded;
        private static bool s_handlesVisible;
        private static int s_selected;
        private static bool s_heldGameMode;
        private static SuperController.GameMode s_gameModeBeforePlace;
        private static string s_draftPresetName = "";
        private static bool s_lastPresetApplied;
        private static bool s_applied;
        private static bool s_sceneLightsMuted;

        internal static Action UiNeedsRefresh;

        public static bool SuppressNavigate { get { return VpbPassthroughLightAtoms.IsGrabbing; } }

        public static bool IsOwnedUid(string uid)
        {
            return VpbPassthroughLightAtoms.IsOwnedUid(uid);
        }

        public static bool IsOwnedAtom(Atom atom)
        {
            return VpbPassthroughLightAtoms.IsOwnedAtom(atom);
        }

        public static bool IsOwnedController(FreeControllerV3 fc)
        {
            return VpbPassthroughLightAtoms.IsOwnedController(fc);
        }

        public static string AtomUid(int i)
        {
            return VpbPassthroughLightAtoms.Uid(i);
        }

        public static string SlotName(int i)
        {
            if (i < 0 || i >= MaxLights) return "";
            return SlotNames[i];
        }

        public static int SlotIndexFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            for (int i = 0; i < MaxLights; i++)
            {
                if (string.Equals(name, SlotNames[i], StringComparison.OrdinalIgnoreCase))
                    return i;
                if (string.Equals(name, "Light " + (i + 1).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        public static bool HandlesVisible { get { return s_handlesVisible; } }

        public static void SetHandlesVisible(bool on)
        {
            if (on)
            {
                EnsureSlotsLoaded();
                VPBConfig cfg = null;
                try { cfg = VPBConfig.Instance; }
                catch { }
                if (cfg == null || !cfg.PassthroughEnabled || !cfg.PassthroughLightsEnabled) return;
                if (!VpbPassthrough.IsVrActive()) return;
                s_handlesVisible = true;
                EnterEditMode();
                try { VpbPassthroughLightAtoms.ApplyPlacement(); }
                catch { }
                try { VpbPassthroughLightAtoms.ApplyHandleIsolation(true); }
                catch { }
                try { VpbPassthroughLightAtoms.WriteSlotsToWorld(); }
                catch { }
                try { VpbPassthroughLightAtoms.DismissNativeLightUi(); }
                catch { }
                return;
            }

            if (!s_handlesVisible) return;
            s_handlesVisible = false;
            try { VpbPassthroughLightAtoms.ApplyPlacement(); }
            catch { }
            try { VpbPassthroughLightAtoms.ApplyHandleIsolation(false); }
            catch { }
            try { VpbPassthroughLightAtoms.DismissNativeLightUi(); }
            catch { }
            RestoreEditMode();
            PersistNow();
        }

        public static LightSlot GetSlot(int i)
        {
            EnsureSlotsLoaded();
            return (i >= 0 && i < MaxLights) ? s_slots[i] : default(LightSlot);
        }

        public static void SetSlot(int i, LightSlot slot)
        {
            EnsureSlotsLoaded();
            if (i < 0 || i >= MaxLights) return;
            bool wasEnabled = s_slots[i].Enabled;

            slot.Intensity = Mathf.Clamp(slot.Intensity, MinIntensity, MaxIntensity);
            slot.ColorH = Mathf.Clamp01(slot.ColorH);
            slot.ColorS = Mathf.Clamp01(slot.ColorS);
            slot.ColorV = Mathf.Clamp01(slot.ColorV);
            slot.Range = Mathf.Clamp(slot.Range, MinRange, MaxRange);
            slot.X = Mathf.Clamp(slot.X, -MaxHorizontalOffset, MaxHorizontalOffset);
            slot.Y = Mathf.Clamp(slot.Y, 0f, MaxHeight);
            slot.Z = Mathf.Clamp(slot.Z, -MaxHorizontalOffset, MaxHorizontalOffset);
            s_slots[i] = slot;
            try { VpbPassthroughLightAtoms.SyncParams(); }
            catch { }

            if (wasEnabled != slot.Enabled)
            {
                try { VpbPassthroughLightAtoms.Ensure(); }
                catch { }
                PersistNow();
                return;
            }
            Persist();
        }

        internal static bool IsSlotEnabled(int i)
        {
            EnsureSlotsLoaded();
            return i >= 0 && i < MaxLights && s_slots[i].Enabled;
        }

        internal static Vector3 GetPosition(int i)
        {
            EnsureSlotsLoaded();
            if (i < 0 || i >= MaxLights) return Vector3.zero;
            LightSlot s = s_slots[i];
            return new Vector3(s.X, s.Y, s.Z);
        }

        internal static void SetPosition(int i, Vector3 p)
        {
            EnsureSlotsLoaded();
            if (i < 0 || i >= MaxLights) return;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) return;
            if (float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z)) return;

            LightSlot s = s_slots[i];
            s.X = Mathf.Clamp(p.x, -MaxHorizontalOffset, MaxHorizontalOffset);
            s.Y = Mathf.Clamp(p.y, 0f, MaxHeight);
            s.Z = Mathf.Clamp(p.z, -MaxHorizontalOffset, MaxHorizontalOffset);
            s_slots[i] = s;
        }

        internal static void SetAppearance(int i, float intensity, float h, float s, float v, float range)
        {
            EnsureSlotsLoaded();
            if (i < 0 || i >= MaxLights) return;
            LightSlot slot = s_slots[i];
            slot.Intensity = Mathf.Clamp(intensity, MinIntensity, MaxIntensity);
            slot.ColorH = Mathf.Clamp01(h);
            slot.ColorS = Mathf.Clamp01(s);
            slot.ColorV = Mathf.Clamp01(v);
            slot.Range = Mathf.Clamp(range, MinRange, MaxRange);
            s_slots[i] = slot;
        }

        public static void BringSlotToPlayer(int index)
        {
            EnsureSlotsLoaded();
            if (index < 0 || index >= MaxLights) return;
            if (!s_slots[index].Enabled) return;
            Vector3 head;
            if (!TryGetPlaySpaceHeadPoint(out head)) return;
            SetPosition(index, head);
            PersistNow();
            try { VpbPassthroughLightAtoms.WriteSlotsToWorld(); }
            catch { }
        }

        public static string[] PresetNames()
        {
            List<LightPreset> list = ReadPresets();
            if (list.Count == 0) return new[] { NoPresetName };
            string[] names = new string[list.Count];
            for (int i = 0; i < list.Count; i++) names[i] = list[i].Name;
            return names;
        }

        public static string SelectedPresetName()
        {
            string selected = null;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) selected = cfg.PassthroughLightPresetSelected;
            }
            catch { }
            if (string.IsNullOrEmpty(selected)) return NoPresetName;
            List<LightPreset> list = ReadPresets();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, selected, StringComparison.OrdinalIgnoreCase))
                    return list[i].Name;
            }
            return list.Count > 0 ? list[0].Name : NoPresetName;
        }

        public static void SetSelectedPresetName(string name)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, NoPresetName, StringComparison.OrdinalIgnoreCase))
            {
                WriteSelectedName("");
                s_draftPresetName = "";
                return;
            }
            List<LightPreset> list = ReadPresets();
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                WriteSelectedName(list[i].Name);
                s_draftPresetName = list[i].Name;
                return;
            }
        }

        public static bool TryApplySelectedPreset()
        {
            string selected = null;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) selected = cfg.PassthroughLightPresetSelected;
            }
            catch { }
            if (string.IsNullOrEmpty(selected)) return false;
            if (string.Equals(selected, NoPresetName, StringComparison.OrdinalIgnoreCase)) return false;
            return TryLoadPreset(selected);
        }

        internal static void TryApplyLastPresetOnce()
        {
            if (s_lastPresetApplied) return;
            s_lastPresetApplied = true;
            string selected = null;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) selected = cfg.PassthroughLightPresetSelected;
            }
            catch { }
            if (string.IsNullOrEmpty(selected)) return;
            if (!TryLoadPreset(selected)) return;
            LogUtil.Log("[VPB] Real-world lights applied last preset " + selected);
        }

        public static string DraftPresetName()
        {
            return s_draftPresetName ?? "";
        }

        public static void SetDraftPresetName(string name)
        {
            s_draftPresetName = name ?? "";
        }

        public static bool TrySavePreset(string name)
        {
            EnsureSlotsLoaded();
            string used = SanitizePresetName(name);
            if (used.Length == 0) used = SanitizePresetName(SelectedPresetName());
            if (used.Length == 0 || string.Equals(used, NoPresetName, StringComparison.OrdinalIgnoreCase))
                used = NextAutoPresetName();
            if (used.Length == 0) return false;

            try { VpbPassthroughLightAtoms.CaptureAppearance(); }
            catch { }

            List<LightPreset> list = ReadPresets();
            int found = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Name, used, StringComparison.OrdinalIgnoreCase)) continue;
                found = i;
                break;
            }
            if (found < 0 && list.Count >= MaxPresets) return false;

            LightPreset p;
            p.Name = used;
            p.Count = GetActiveCount();
            p.Group = false;
            p.OverrideScene = true;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null)
                {
                    p.Group = cfg.PassthroughLightsMoveAsGroup;
                    p.OverrideScene = cfg.PassthroughLightsOverrideScene;
                }
            }
            catch { }
            p.Spec = Serialize();
            if (found >= 0) list[found] = p;
            else list.Add(p);
            if (!WritePresets(list)) return false;
            WriteSelectedName(used);
            s_draftPresetName = used;
            PersistNow();
            RequestUiRefresh();
            return true;
        }

        public static bool TryLoadPreset(string name)
        {
            string used = SanitizePresetName(name);
            if (used.Length == 0) used = SanitizePresetName(SelectedPresetName());
            if (used.Length == 0) return false;
            List<LightPreset> list = ReadPresets();
            int found = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Name, used, StringComparison.OrdinalIgnoreCase)) continue;
                found = i;
                break;
            }
            if (found < 0) return false;

            LightPreset p = list[found];
            EnsureSlotsLoaded();
            Deserialize(p.Spec);
            ApplyEnabledPrefix(p.Count, false);
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null)
                {
                    cfg.PassthroughLightsMoveAsGroup = p.Group;
                    cfg.PassthroughLightsOverrideScene = p.OverrideScene;
                }
            }
            catch { }
            WriteSelectedName(p.Name);
            s_draftPresetName = p.Name;
            PersistNow();
            try { VpbPassthroughLightAtoms.Ensure(); }
            catch { }
            try { VpbPassthroughLightAtoms.ApplySlotsToLive(); }
            catch { }
            try { VpbPassthrough.NotifySettingsChanged(); }
            catch { }
            RequestUiRefresh();
            return true;
        }

        public static bool TryDeletePreset(string name)
        {
            string used = SanitizePresetName(name);
            if (used.Length == 0) used = SanitizePresetName(SelectedPresetName());
            if (used.Length == 0) return false;
            List<LightPreset> list = ReadPresets();
            int found = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.Equals(list[i].Name, used, StringComparison.OrdinalIgnoreCase)) continue;
                found = i;
                break;
            }
            if (found < 0) return false;
            list.RemoveAt(found);
            if (!WritePresets(list)) return false;
            string next = list.Count > 0 ? list[0].Name : "";
            WriteSelectedName(next);
            s_draftPresetName = next;
            RequestUiRefresh();
            return true;
        }

        public static string SanitizePresetName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\n' || c == '\r' || c == '\t') c = ' ';
                if (c < 32) continue;
                sb.Append(c);
                if (sb.Length >= 40) break;
            }
            string s = sb.ToString().Trim();
            if (string.Equals(s, NoPresetName, StringComparison.OrdinalIgnoreCase)) return "";
            return s;
        }

        private struct LightPreset
        {
            public string Name;
            public int Count;
            public bool Group;
            public bool OverrideScene;
            public string Spec;
        }

        private static List<LightPreset> ReadPresets()
        {
            var list = new List<LightPreset>(MaxPresets);
            string raw = null;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) raw = cfg.PassthroughLightPresetsJson;
            }
            catch { }
            if (string.IsNullOrEmpty(raw) || raw == "[]") return list;
            JSONNode root = null;
            try { root = JSON.Parse(raw); }
            catch { return list; }
            JSONArray arr = root as JSONArray;
            if (arr == null) return list;
            for (int i = 0; i < arr.Count && list.Count < MaxPresets; i++)
            {
                JSONClass jc = arr[i] as JSONClass;
                if (jc == null) continue;
                string n = SanitizePresetName(jc["Name"] != null ? jc["Name"].Value : null);
                if (n.Length == 0) continue;
                bool dup = false;
                for (int d = 0; d < list.Count; d++)
                {
                    if (!string.Equals(list[d].Name, n, StringComparison.OrdinalIgnoreCase)) continue;
                    dup = true;
                    break;
                }
                if (dup) continue;
                LightPreset p;
                p.Name = n;
                p.Count = VPBConfig.NormalizePassthroughLightCount(jc["Count"] != null ? jc["Count"].AsInt : 1);
                p.Group = jc["Group"] != null && jc["Group"].AsBool;
                p.OverrideScene = jc["Override"] == null || jc["Override"].AsBool;
                p.Spec = jc["Spec"] != null ? (jc["Spec"].Value ?? "") : "";
                if (p.Spec.Length == 0) continue;
                list.Add(p);
            }
            return list;
        }

        private static bool WritePresets(List<LightPreset> list)
        {
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return false;
                var arr = new JSONArray();
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        JSONClass jc = new JSONClass();
                        jc["Name"] = list[i].Name;
                        jc["Count"].AsInt = list[i].Count;
                        jc["Group"].AsBool = list[i].Group;
                        jc["Override"].AsBool = list[i].OverrideScene;
                        jc["Spec"] = list[i].Spec ?? "";
                        arr.Add(jc);
                    }
                }
                cfg.PassthroughLightPresetsJson = arr.ToString();
                cfg.Save(false);
                return true;
            }
            catch { return false; }
        }

        private static void WriteSelectedName(string name)
        {
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;
                cfg.PassthroughLightPresetSelected = name ?? "";
            }
            catch { }
        }

        private static string NextAutoPresetName()
        {
            List<LightPreset> list = ReadPresets();
            for (int n = 1; n <= MaxPresets; n++)
            {
                string candidate = "Lights " + n.ToString(CultureInfo.InvariantCulture);
                bool used = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!string.Equals(list[i].Name, candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    used = true;
                    break;
                }
                if (!used) return candidate;
            }
            return "";
        }

        public static int SuppressedSceneLightCount { get { return s_mutedSceneLights.Count; } }

        public static int ActiveLightCount
        {
            get
            {
                EnsureSlotsLoaded();
                int n = 0;
                for (int i = 0; i < MaxLights; i++) if (s_slots[i].Enabled) n++;
                return n;
            }
        }

        public static int SelectedIndex
        {
            get
            {
                EnsureSlotsLoaded();
                if (s_selected < 0) return 0;
                if (s_selected >= MaxLights) return MaxLights - 1;
                return s_selected;
            }
        }

        public static void Select(int i)
        {
            EnsureSlotsLoaded();
            if (i < 0) i = 0;
            else if (i >= MaxLights) i = MaxLights - 1;
            int n = GetActiveCount();
            if (i >= n) i = n - 1;
            if (s_selected == i) return;
            s_selected = i;
            RequestUiRefresh();
        }

        public static void SelectFirstEnabled(int exceptIndex)
        {
            EnsureSlotsLoaded();
            if (s_selected != exceptIndex && s_selected >= 0 && s_selected < MaxLights && s_slots[s_selected].Enabled)
                return;
            for (int i = 0; i < MaxLights; i++)
            {
                if (i == exceptIndex) continue;
                if (!s_slots[i].Enabled) continue;
                Select(i);
                return;
            }
        }

        internal static void RequestUiRefresh()
        {
            Action cb = UiNeedsRefresh;
            if (cb != null) cb();
        }

        internal static string ShadowResolutionForQualityLevel(string quality, int pixelLightCount)
        {
            if (string.Equals(quality, "UltraLow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(quality, "Low", StringComparison.OrdinalIgnoreCase))
                return ShadowResLow;
            if (string.Equals(quality, "Mid", StringComparison.OrdinalIgnoreCase))
                return ShadowResMedium;
            if (string.Equals(quality, "High", StringComparison.OrdinalIgnoreCase))
                return ShadowResHigh;
            if (string.Equals(quality, "Ultra", StringComparison.OrdinalIgnoreCase)
                || string.Equals(quality, "Max", StringComparison.OrdinalIgnoreCase))
                return ShadowResVeryHigh;
            if (pixelLightCount <= 1) return ShadowResLow;
            if (pixelLightCount == 2) return ShadowResMedium;
            if (pixelLightCount == 3) return ShadowResHigh;
            return ShadowResVeryHigh;
        }

        internal static string ShadowResolutionForCurrentQuality()
        {
            string quality = "";
            int pixel = 2;
            try
            {
                UserPreferences p = UserPreferences.singleton;
                if (p != null)
                {
                    try { pixel = p.pixelLightCount; }
                    catch { }
                    if (QualityToggleOn(p.ultraLowQualityToggle)) quality = "UltraLow";
                    else if (QualityToggleOn(p.lowQualityToggle)) quality = "Low";
                    else if (QualityToggleOn(p.midQualityToggle)) quality = "Mid";
                    else if (QualityToggleOn(p.highQualityToggle)) quality = "High";
                    else if (QualityToggleOn(p.ultraQualityToggle)) quality = "Ultra";
                    else if (QualityToggleOn(p.maxQualityToggle)) quality = "Max";
                }
            }
            catch { }
            return ShadowResolutionForQualityLevel(quality, pixel);
        }

        private static bool QualityToggleOn(Toggle t)
        {
            if (t == null) return false;
            try { return t.isOn; }
            catch { return false; }
        }

        private static LightSlot DefaultSlot(int i)
        {
            float[] xs = { 0f, 1.2f, -1.2f, 0f };
            float[] zs = { 1.2f, -0.4f, -0.4f, -1.4f };
            float h, s, v;
            KelvinToHsv(4500f, out h, out s, out v);
            return new LightSlot
            {
                Enabled = i == 0,
                X = xs[i],
                Y = 1.9f,
                Z = zs[i],
                Intensity = 1.4f,
                ColorH = h,
                ColorS = s,
                ColorV = v,
                Range = 8f,
            };
        }

        private static void EnsureSlotsLoaded()
        {
            if (s_slotsLoaded) return;

            for (int i = 0; i < MaxLights; i++) s_slots[i] = DefaultSlot(i);

            VPBConfig cfg = null;
            try { cfg = VPBConfig.Instance; }
            catch { }
            if (cfg == null) return;

            s_slotsLoaded = true;
            try { Deserialize(cfg.PassthroughLightsSpec); }
            catch { }
            ApplyCountFromConfig(cfg);
        }

        private static void ApplyCountFromConfig(VPBConfig cfg)
        {
            int n = cfg != null ? cfg.PassthroughLightCount : 0;
            if (n >= 1)
            {
                ApplyEnabledPrefix(n, false);
                return;
            }
            n = 0;
            for (int i = 0; i < MaxLights; i++)
                if (s_slots[i].Enabled) n++;
            if (n < 1) n = 1;
            try { if (cfg != null) cfg.PassthroughLightCount = n; }
            catch { }
        }

        public static void SetActiveCount(int n)
        {
            EnsureSlotsLoaded();
            ApplyEnabledPrefix(n, true);
        }

        public static int GetActiveCount()
        {
            EnsureSlotsLoaded();
            int n = 0;
            for (int i = 0; i < MaxLights; i++)
                if (s_slots[i].Enabled) n++;
            if (n < 1) n = 1;
            if (n > MaxLights) n = MaxLights;
            return n;
        }

        private static void ApplyEnabledPrefix(int n, bool persist)
        {
            if (n < 1) n = 1;
            if (n > MaxLights) n = MaxLights;
            bool changed = false;
            for (int i = 0; i < MaxLights; i++)
            {
                bool want = i < n;
                LightSlot s = s_slots[i];
                if (s.Enabled == want) continue;
                if (want && s.Intensity < 0.05f)
                {
                    LightSlot d = DefaultSlot(i);
                    s.Intensity = d.Intensity;
                    if (s.Range < MinRange + 0.01f) s.Range = d.Range;
                }
                s.Enabled = want;
                s_slots[i] = s;
                changed = true;
            }
            if (s_selected >= n) Select(n - 1);
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) cfg.PassthroughLightCount = n;
            }
            catch { }
            if (changed)
            {
                try { VpbPassthroughLightAtoms.Ensure(); }
                catch { }
            }
            if (persist)
            {
                PersistNow();
                RequestUiRefresh();
            }
        }

        public static void FlushToDisk()
        {
            if (!s_slotsLoaded) return;
            PersistNow();
        }

        public static void InvalidateSlots()
        {
            s_slotsLoaded = false;
            s_selected = 0;
            s_lastPresetApplied = false;
            EnsureSlotsLoaded();
            try { VpbPassthroughLightAtoms.Ensure(); }
            catch { }
            try { VpbPassthroughLightAtoms.SyncParams(); }
            catch { }
        }

        private static void Persist()
        {
            if (!s_slotsLoaded) return;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;
                cfg.PassthroughLightsSpec = Serialize();
                cfg.PassthroughLightCount = GetActiveCount();
            }
            catch { }
            try { VpbPassthrough.ScheduleConfigSave(); }
            catch { }
        }

        internal static void PersistNow()
        {
            if (!s_slotsLoaded) return;
            try { VpbPassthroughLightAtoms.CaptureAppearance(); }
            catch { }
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return;
                cfg.PassthroughLightsSpec = Serialize();
                cfg.PassthroughLightCount = GetActiveCount();
                cfg.Save(false);
            }
            catch { }
        }

        public static void SyncNow()
        {
            try { VpbPassthroughLightAtoms.SyncParams(); }
            catch { }
        }

        public static string Serialize()
        {
            EnsureSlotsLoaded();
            var sb = new System.Text.StringBuilder(200);
            for (int i = 0; i < MaxLights; i++)
            {
                if (i > 0) sb.Append(';');
                LightSlot s = s_slots[i];
                sb.Append(s.Enabled ? '1' : '0').Append('|');
                sb.Append(s.X.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Y.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Z.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Intensity.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.ColorH.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.ColorS.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.ColorV.ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Range.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static void Deserialize(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return;
            string[] rows = spec.Split(';');
            for (int i = 0; i < MaxLights && i < rows.Length; i++)
            {
                string[] f = rows[i].Split('|');
                if (f.Length < 7) continue;
                LightSlot s = s_slots[i];
                s.Enabled = f[0] == "1";
                s.X = Mathf.Clamp(ParseFloat(f[1], s.X), -MaxHorizontalOffset, MaxHorizontalOffset);
                s.Y = Mathf.Clamp(ParseFloat(f[2], s.Y), 0f, MaxHeight);
                s.Z = Mathf.Clamp(ParseFloat(f[3], s.Z), -MaxHorizontalOffset, MaxHorizontalOffset);
                s.Intensity = Mathf.Clamp(ParseFloat(f[4], s.Intensity), MinIntensity, MaxIntensity);
                if (f.Length >= 9)
                {
                    s.ColorH = Mathf.Clamp01(ParseFloat(f[5], s.ColorH));
                    s.ColorS = Mathf.Clamp01(ParseFloat(f[6], s.ColorS));
                    s.ColorV = Mathf.Clamp01(ParseFloat(f[7], s.ColorV));
                    s.Range = Mathf.Clamp(ParseFloat(f[8], s.Range), MinRange, MaxRange);
                }
                else
                {
                    float kelvin = Mathf.Clamp(ParseFloat(f[5], 4500f), MinTemperature, MaxTemperature);
                    float h, sv, vv;
                    KelvinToHsv(kelvin, out h, out sv, out vv);
                    s.ColorH = h;
                    s.ColorS = sv;
                    s.ColorV = vv;
                    s.Range = Mathf.Clamp(ParseFloat(f[6], s.Range), MinRange, MaxRange);
                }
                s_slots[i] = s;
            }
        }

        private static float ParseFloat(string s, float fallback)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !float.IsNaN(v) && !float.IsInfinity(v))
                return v;
            return fallback;
        }

        public static bool TryGetPlaySpaceHeadPoint(out Vector3 local)
        {
            local = Vector3.zero;
            try
            {
                SuperController sc = SuperController.singleton;
                Transform rig = sc != null ? sc.navigationRig : null;
                if (rig == null) return false;

                Transform head = null;
                if (sc.centerCameraTarget != null) head = sc.centerCameraTarget.transform;
                if (head == null && Camera.main != null) head = Camera.main.transform;
                if (head == null) return false;

                Vector3 world = head.position + head.forward * 1.35f - head.up * 0.15f;
                local = rig.InverseTransformPoint(world);
                return true;
            }
            catch { return false; }
        }

        internal static void Apply()
        {
            EnsureSlotsLoaded();
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            if (!cfg.PassthroughLightsEnabled)
            {
                TeardownIfNeeded();
                return;
            }

            s_applied = true;
            TryApplyLastPresetOnce();

            Transform rig = null;
            try
            {
                SuperController sc = SuperController.singleton;
                rig = sc != null ? sc.navigationRig : null;
            }
            catch { }
            if (rig == null) return;

            try { VpbPassthroughLightAtoms.Ensure(); }
            catch { }
            try { VpbPassthroughLightAtoms.SyncParams(); }
            catch { }

            if (cfg.PassthroughLightsOverrideScene) SuppressSceneLights();
            else RestoreSceneLights();
        }

        internal static void TeardownIfNeeded()
        {
            if (!s_applied && !s_handlesVisible && s_mutedSceneLights.Count == 0 && !s_sceneLightsMuted)
                return;
            Restore();
        }

        internal static void Restore()
        {
            if (s_handlesVisible) SetHandlesVisible(false);
            else
            {
                RestoreEditMode();
                try { VpbPassthroughLightAtoms.ApplyHandleIsolation(false); }
                catch { }
            }
            PersistNow();
            RestoreSceneLights();
            try { VpbPassthroughLightAtoms.DestroyAll(); }
            catch { }
            s_applied = false;
        }

        private static void EnterEditMode()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                if (sc.gameMode == SuperController.GameMode.Edit) return;
                if (!s_heldGameMode)
                {
                    s_gameModeBeforePlace = sc.gameMode;
                    s_heldGameMode = true;
                }
                sc.gameMode = SuperController.GameMode.Edit;
            }
            catch { }
        }

        private static void RestoreEditMode()
        {
            if (!s_heldGameMode) return;
            s_heldGameMode = false;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                sc.gameMode = s_gameModeBeforePlace;
            }
            catch { }
        }

        internal static void TickFrame()
        {
            VPBConfig cfg = null;
            try { cfg = VPBConfig.Instance; }
            catch { }
            if (cfg == null || !cfg.PassthroughLightsEnabled)
            {
                TeardownIfNeeded();
                return;
            }

            try { VpbPassthroughLightAtoms.Tick(); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Real-world light tick failed: " + ex.Message);
                SetHandlesVisible(false);
            }
        }

        internal static void OnSceneLoadComplete()
        {
            s_mutedSceneLights.Clear();
            s_sceneLightsMuted = false;
            try { VpbPassthroughLightAtoms.OnSceneLoadComplete(); }
            catch { }
        }

        internal static void RevealSceneLightForStore(Atom atom)
        {
            if (atom == null) return;
            for (int i = 0; i < s_mutedSceneLights.Count; i++)
            {
                MutedSceneLight m = s_mutedSceneLights[i];
                if (m == null || !ReferenceEquals(m.Atom, atom)) continue;
                ApplyMute(m, m.WasOn);
            }
        }

        internal static void RepressSceneLightAfterStore(Atom atom)
        {
            if (atom == null || !s_applied) return;
            for (int i = 0; i < s_mutedSceneLights.Count; i++)
            {
                MutedSceneLight m = s_mutedSceneLights[i];
                if (m == null || !ReferenceEquals(m.Atom, atom)) continue;
                ApplyMute(m, false);
            }
        }

        private static void SuppressSceneLights()
        {
            try { VpbPassthroughLightAtoms.ForceOwnLightsOn(); }
            catch { }
            if (VpbPassthroughLightAtoms.IsSpawning) return;
            if (s_sceneLightsMuted) return;

            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            List<Atom> atoms = null;
            if (sc != null)
            {
                try { atoms = sc.GetAtoms(); }
                catch { atoms = null; }
            }
            if (atoms != null)
            {
                for (int i = 0; i < atoms.Count; i++)
                    MuteLightAtom(atoms[i]);
            }

            Light[] all;
            try { all = UnityEngine.Object.FindObjectsOfType<Light>(); }
            catch
            {
                s_sceneLightsMuted = true;
                return;
            }
            if (all == null)
            {
                s_sceneLightsMuted = true;
                return;
            }
            for (int i = 0; i < all.Length; i++)
            {
                Light lt = all[i];
                if (lt == null) continue;
                if (VpbPassthroughLightAtoms.IsOwnLight(lt)) continue;
                if (UnityLightAlreadyMuted(lt)) continue;
                Atom parent = AtomOf(lt);
                if (parent != null)
                {
                    if (VpbPassthroughLightAtoms.IsOwnedAtom(parent)) continue;
                    if (SceneUtils.IsPersonLikeAtom(parent)) continue;
                    if (SceneUtils.IsLightAtom(parent)) continue;
                }
                bool on;
                try { on = lt.enabled && lt.gameObject.activeInHierarchy; }
                catch { continue; }
                if (!on) continue;
                try
                {
                    lt.enabled = false;
                    MutedSceneLight m = new MutedSceneLight();
                    m.Atom = parent;
                    m.UnityLight = lt;
                    m.WasOn = true;
                    s_mutedSceneLights.Add(m);
                }
                catch { }
            }
            s_sceneLightsMuted = true;
        }

        private static void MuteLightAtom(Atom atom)
        {
            if (atom == null) return;
            if (VpbPassthroughLightAtoms.IsOwnedAtom(atom)) return;
            if (!SceneUtils.IsLightAtom(atom)) return;
            JSONStorable light = null;
            try { light = atom.GetStorableByID("Light"); }
            catch { return; }
            if (light == null) return;
            JSONStorableBool on = null;
            try { on = light.GetBoolJSONParam("on"); }
            catch { return; }
            if (on == null) return;
            bool wasOn;
            try { wasOn = on.val; }
            catch { return; }
            if (!wasOn) return;
            MutedSceneLight m = new MutedSceneLight();
            m.Atom = atom;
            m.OnParam = on;
            m.WasOn = true;
            try { on.val = false; }
            catch { return; }
            s_mutedSceneLights.Add(m);
        }

        private static bool UnityLightAlreadyMuted(Light lt)
        {
            for (int i = 0; i < s_mutedSceneLights.Count; i++)
            {
                MutedSceneLight m = s_mutedSceneLights[i];
                if (m == null) continue;
                if (m.UnityLight != null && ReferenceEquals(m.UnityLight, lt)) return true;
                if (m.Atom == null || lt == null) continue;
                try
                {
                    if (ReferenceEquals(AtomOf(lt), m.Atom)) return true;
                }
                catch { }
            }
            return false;
        }

        private static Atom AtomOf(Light lt)
        {
            if (lt == null) return null;
            try { return lt.GetComponentInParent<Atom>(); }
            catch { return null; }
        }

        private static void ApplyMute(MutedSceneLight m, bool on)
        {
            if (m == null) return;
            if (m.OnParam != null)
            {
                try { m.OnParam.val = on; }
                catch { }
                return;
            }
            if (m.UnityLight == null) return;
            try { m.UnityLight.enabled = on; }
            catch { }
        }

        private static void RestoreSceneLights()
        {
            for (int i = 0; i < s_mutedSceneLights.Count; i++)
                ApplyMute(s_mutedSceneLights[i], s_mutedSceneLights[i] != null && s_mutedSceneLights[i].WasOn);
            s_mutedSceneLights.Clear();
            s_sceneLightsMuted = false;
        }

        public static Color ColorFromKelvin(float kelvin)
        {
            float t = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;
            float r, g, b;

            if (t <= 66f) r = 255f;
            else r = 329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f);

            if (t <= 66f) g = 99.4708025861f * Mathf.Log(t) - 161.1195681661f;
            else g = 288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f);

            if (t >= 66f) b = 255f;
            else if (t <= 19f) b = 0f;
            else b = 138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f;

            return new Color(
                Mathf.Clamp01(r / 255f),
                Mathf.Clamp01(g / 255f),
                Mathf.Clamp01(b / 255f),
                1f);
        }

        public static void KelvinToHsv(float kelvin, out float h, out float s, out float v)
        {
            Color c = ColorFromKelvin(kelvin);
            Color.RGBToHSV(c, out h, out s, out v);
        }
    }
}
