using System;
using System.Collections.Generic;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public static class VpbPassthrough
    {
        public struct KeyPreset
        {
            public string Name;
            public byte R;
            public byte G;
            public byte B;
        }

        public const string PresetCustom = "Custom";

        public static readonly KeyPreset[] Presets =
        {
            new KeyPreset { Name = "Recommended", R = 0,   G = 30,  B = 60  },
            new KeyPreset { Name = "Blue",        R = 0,   G = 71,  B = 187 },
            new KeyPreset { Name = "Green",       R = 0,   G = 153, B = 51  },
            new KeyPreset { Name = "Black",       R = 0,   G = 0,   B = 0   },
            new KeyPreset { Name = "White",       R = 255, G = 255, B = 255 },
        };

        public const string HideNothing = "Nothing";
        public const string HideEnvironment = "Environment";
        public const string HideAllButPeople = "People only";

        public const int VdSimilarity = 5;
        public const int VdSmoothness = 0;

        private const float ReconcileIntervalSeconds = 0.5f;
        private const float ConfigSaveDebounceSeconds = 1.5f;

        private struct CameraState
        {
            public Camera Cam;
            public CameraClearFlags ClearFlags;
            public Color Background;
            public bool AllowHdr;
            public bool IsDesktopMirror;
        }

        private static readonly HashSet<string> EffectTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "PostProcessingBehaviour",
            "Bloom", "BloomAndFlares", "BloomOptimized",
            "DepthOfField", "DepthOfFieldDeprecated",
            "SunShafts", "Tonemapping",
            "ColorCorrectionCurves", "ColorCorrectionLookup", "ColorCorrectionRamp",
            "GlobalFog",
            "ScreenSpaceAmbientOcclusion", "ScreenSpaceAmbientObscurance",
            "VignetteAndChromaticAberration",
            "NoiseAndGrain", "NoiseAndScratches",
            "Antialiasing", "CameraMotionBlur", "MotionBlur",
            "Blur", "BlurOptimized", "TiltShift",
            "EdgeDetection", "Fisheye", "Grayscale", "SepiaTone",
            "ContrastEnhance", "ContrastStretch", "CreaseShading",
            "ScreenOverlay", "Twirl", "Vortex",
            "AmplifyOcclusionEffect", "AmplifyColorEffect", "SSAOPro",
        };

        private static readonly HashSet<string> ProtectedAtomTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CoreControl", "WindowCamera", "PlayerNavigationPanel", "SubScene",
        };

        private static readonly List<CameraState> s_cameras = new List<CameraState>(8);
        private static readonly List<Behaviour> s_disabledEffects = new List<Behaviour>(16);
        private static readonly List<Atom> s_hiddenAtoms = new List<Atom>(32);
        private static readonly List<Camera> s_cameraScratch = new List<Camera>(8);
        private static readonly List<MonoBehaviour> s_componentScratch = new List<MonoBehaviour>(32);
        private static Camera[] s_allCameras = new Camera[16];

        private static bool s_active;
        private static bool s_dirty;
        private static float s_nextReconcile;
        private static bool s_configDirty;
        private static float s_configSaveAt;
        private static bool s_navRepairDone;
        private static string s_lastLoggedHideMode;
        private static int s_lastLoggedHideCount = -1;

        private static bool s_prefsCaptured;
        private static UserPreferences.GlowEffectsLevel s_glowPrev;
        private static int s_msaaPrev;
        private static bool s_glowForced;
        private static bool s_msaaForced;

        public static bool IsActive { get { return s_active; } }

        public static int DisabledEffectCount { get { return s_disabledEffects.Count; } }

        public static int HiddenAtomCount { get { return s_hiddenAtoms.Count; } }

        public static int PatchedCameraCount { get { return s_cameras.Count; } }

        public static void NotifySettingsChanged()
        {
            s_dirty = true;
            s_nextReconcile = 0f;
            s_configDirty = true;
            s_configSaveAt = Time.unscaledTime + ConfigSaveDebounceSeconds;
        }

        public static void ScheduleConfigSave()
        {
            s_configDirty = true;
            s_configSaveAt = Time.unscaledTime + ConfigSaveDebounceSeconds;
        }

        public static void Shutdown()
        {
            FlushConfigSave(true);
            try { VpbPassthroughLights.FlushToDisk(); }
            catch { }
            if (s_active)
            {
                Restore();
                return;
            }
            try { VpbPassthroughLights.Restore(); }
            catch { }
        }

        private static void FlushConfigSave()
        {
            FlushConfigSave(false);
        }

        private static void FlushConfigSave(bool force)
        {
            if (!s_configDirty) return;
            if (!force && Time.unscaledTime < s_configSaveAt) return;
            s_configDirty = false;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg != null) cfg.Save(false);
            }
            catch { }
        }

        public static void OnSceneLoadComplete()
        {
            if (!s_active) return;
            for (int i = s_disabledEffects.Count - 1; i >= 0; i--)
                if (s_disabledEffects[i] == null) s_disabledEffects.RemoveAt(i);
            for (int i = s_hiddenAtoms.Count - 1; i >= 0; i--)
                if (s_hiddenAtoms[i] == null) s_hiddenAtoms.RemoveAt(i);
            try { VpbPassthroughLights.OnSceneLoadComplete(); }
            catch { }
            s_dirty = true;
            s_nextReconcile = 0f;
        }

        public static void Tick()
        {
            FlushConfigSave();
            RepairGrabNavigationOnce();

            VPBConfig cfg = VPBConfig.Instance;
            bool want = cfg != null && cfg.PassthroughEnabled && IsVrActive();
            if (!want && !s_active) return;

            if (!want)
            {
                Restore();
                return;
            }

            if (!s_active)
            {
                Apply();
                return;
            }

            try { VpbPassthroughLights.TickFrame(); }
            catch { }

            float now = Time.unscaledTime;
            if (!s_dirty && now < s_nextReconcile) return;
            s_dirty = false;
            s_nextReconcile = now + ReconcileIntervalSeconds;
            Reconcile();
        }

        private static void RepairGrabNavigationOnce()
        {
            if (s_navRepairDone) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            s_navRepairDone = true;
            if (cfg.PassthroughNavGrabRepaired) return;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null)
                {
                    s_navRepairDone = false;
                    return;
                }
                if (sc.disableGrabNavigation) sc.disableGrabNavigation = false;
            }
            catch { }
            cfg.PassthroughNavGrabRepaired = true;
            ScheduleConfigSave();
        }

        public static bool IsVrActive()
        {
            try { return XrUtils.IsVrActive(); }
            catch { return false; }
        }

        public static int ToByte(float channel)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f), 0, 255);
        }

        public static float FromByte(float value)
        {
            return Mathf.Clamp01(Mathf.Clamp(value, 0f, 255f) / 255f);
        }

        public static string PresetNameFor(int r, int g, int b)
        {
            if (VPBConfig.Instance != null && VPBConfig.Instance.PassthroughKeyCustom)
                return PresetCustom;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i].R == r && Presets[i].G == g && Presets[i].B == b)
                    return Presets[i].Name;
            }
            return PresetCustom;
        }

        public static bool TryGetPreset(string name, out KeyPreset preset)
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                if (string.Equals(Presets[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    preset = Presets[i];
                    return true;
                }
            }
            preset = default(KeyPreset);
            return false;
        }

        private static void Apply()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            CollectCameras(s_cameraScratch);
            if (s_cameraScratch.Count == 0) return;

            s_cameras.Clear();
            for (int i = 0; i < s_cameraScratch.Count; i++)
                CaptureCamera(s_cameraScratch[i]);

            if (s_cameras.Count == 0) return;

            if (!s_prefsCaptured)
            {
                try
                {
                    UserPreferences prefs = UserPreferences.singleton;
                    if (prefs != null)
                    {
                        s_glowPrev = prefs.glowEffects;
                        s_msaaPrev = prefs.msaaLevel;
                        s_prefsCaptured = true;
                    }
                }
                catch { }
            }

            s_active = true;
            s_dirty = false;
            s_nextReconcile = Time.unscaledTime + ReconcileIntervalSeconds;
            Reconcile();
        }

        private static void Reconcile()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            Color key = cfg.GetPassthroughKeyColor();
            bool exact = cfg.PassthroughExactColor;

            CollectCameras(s_cameraScratch);
            for (int i = 0; i < s_cameraScratch.Count; i++)
            {
                if (IndexOfCamera(s_cameraScratch[i]) < 0)
                    CaptureCamera(s_cameraScratch[i]);
            }

            for (int i = s_cameras.Count - 1; i >= 0; i--)
            {
                CameraState st = s_cameras[i];
                if (st.Cam == null)
                {
                    s_cameras.RemoveAt(i);
                    continue;
                }
                try
                {
                    st.Cam.clearFlags = CameraClearFlags.SolidColor;
                    st.Cam.backgroundColor = st.IsDesktopMirror ? Color.black : key;
                    st.Cam.allowHDR = exact ? false : st.AllowHdr;
                }
                catch { }
            }

            ApplyPrefs(exact, cfg.PassthroughHardEdges);
            ApplyEffects(cfg.PassthroughCleanKey);
            ApplyHideScene(VPBConfig.NormalizePassthroughHideScene(cfg.PassthroughHideScene));
            try { VpbPassthroughLights.Apply(); }
            catch { }
        }

        private static void Restore()
        {
            for (int i = 0; i < s_cameras.Count; i++)
            {
                CameraState st = s_cameras[i];
                if (st.Cam == null) continue;
                try
                {
                    st.Cam.clearFlags = st.ClearFlags;
                    st.Cam.backgroundColor = st.Background;
                    st.Cam.allowHDR = st.AllowHdr;
                }
                catch { }
            }
            s_cameras.Clear();

            for (int i = 0; i < s_disabledEffects.Count; i++)
            {
                Behaviour b = s_disabledEffects[i];
                if (b == null) continue;
                try { b.enabled = true; }
                catch { }
            }
            s_disabledEffects.Clear();

            try
            {
                UserPreferences prefs = UserPreferences.singleton;
                if (prefs != null)
                {
                    if (s_glowForced) prefs.glowEffects = s_glowPrev;
                    if (s_msaaForced) prefs.msaaLevel = s_msaaPrev;
                }
            }
            catch { }
            s_glowForced = false;
            s_msaaForced = false;
            s_prefsCaptured = false;

            try { VpbPassthroughLights.Restore(); }
            catch { }

            UnhideAll();
            s_active = false;
            s_dirty = false;
        }

        private static void CollectCameras(List<Camera> into)
        {
            into.Clear();
            try
            {
                int count = Camera.allCamerasCount;
                if (count > s_allCameras.Length)
                    s_allCameras = new Camera[Mathf.NextPowerOfTwo(count)];
                int n = Camera.GetAllCameras(s_allCameras);
                for (int i = 0; i < n; i++)
                {
                    Camera cam = s_allCameras[i];
                    if (cam == null) continue;
                    if (cam.targetTexture != null) continue;
                    CameraClearFlags flags = cam.clearFlags;
                    if (flags != CameraClearFlags.Skybox && flags != CameraClearFlags.SolidColor) continue;
                    AddCamera(into, cam);
                }
            }
            catch { }

            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    AddCamera(into, sc.OVRCenterCamera);
                    AddCamera(into, sc.ViveCenterCamera);
                    AddCamera(into, sc.MonitorCenterCamera);
                }
            }
            catch { }
            try
            {
                CameraTarget ct = CameraTarget.centerTarget;
                if (ct != null) AddCamera(into, ct.targetCamera);
            }
            catch { }
        }

        private static void AddCamera(List<Camera> into, Camera cam)
        {
            if (cam == null) return;
            try { if (cam.targetTexture != null) return; }
            catch { return; }
            for (int i = 0; i < into.Count; i++)
                if (ReferenceEquals(into[i], cam)) return;
            into.Add(cam);
        }

        private static int IndexOfCamera(Camera cam)
        {
            if (cam == null) return -1;
            for (int i = 0; i < s_cameras.Count; i++)
                if (ReferenceEquals(s_cameras[i].Cam, cam)) return i;
            return -1;
        }

        private static void CaptureCamera(Camera cam)
        {
            if (cam == null) return;
            try
            {
                s_cameras.Add(new CameraState
                {
                    Cam = cam,
                    ClearFlags = cam.clearFlags,
                    Background = cam.backgroundColor,
                    AllowHdr = cam.allowHDR,
                    IsDesktopMirror = IsDesktopMirrorCamera(cam),
                });
            }
            catch { }
        }

        private static bool IsDesktopMirrorCamera(Camera cam)
        {
            if (cam == null) return false;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && ReferenceEquals(cam, sc.MonitorCenterCamera)) return true;
            }
            catch { }
            try { return cam.stereoTargetEye == StereoTargetEyeMask.None; }
            catch { return false; }
        }

        private static void ApplyPrefs(bool exact, bool hardEdges)
        {
            if (!s_prefsCaptured) return;
            try
            {
                UserPreferences prefs = UserPreferences.singleton;
                if (prefs == null) return;

                if (exact)
                {
                    if (prefs.glowEffects != UserPreferences.GlowEffectsLevel.Off)
                        prefs.glowEffects = UserPreferences.GlowEffectsLevel.Off;
                    s_glowForced = true;
                }
                else if (s_glowForced)
                {
                    prefs.glowEffects = s_glowPrev;
                    s_glowForced = false;
                }

                if (hardEdges)
                {
                    if (prefs.msaaLevel != 0) prefs.msaaLevel = 0;
                    s_msaaForced = true;
                }
                else if (s_msaaForced)
                {
                    prefs.msaaLevel = s_msaaPrev;
                    s_msaaForced = false;
                }
            }
            catch { }
        }

        private static void ApplyEffects(bool clean)
        {
            if (!clean)
            {
                for (int i = 0; i < s_disabledEffects.Count; i++)
                {
                    Behaviour b = s_disabledEffects[i];
                    if (b == null) continue;
                    try { b.enabled = true; }
                    catch { }
                }
                s_disabledEffects.Clear();
                return;
            }

            for (int i = 0; i < s_cameras.Count; i++)
            {
                Camera cam = s_cameras[i].Cam;
                if (cam == null) continue;
                try { cam.GetComponents(s_componentScratch); }
                catch { continue; }

                for (int j = 0; j < s_componentScratch.Count; j++)
                {
                    MonoBehaviour mb = s_componentScratch[j];
                    if (mb == null || !mb.enabled) continue;
                    if (!EffectTypeNames.Contains(mb.GetType().Name)) continue;
                    if (IndexOfEffect(mb) >= 0) continue;
                    try
                    {
                        mb.enabled = false;
                        s_disabledEffects.Add(mb);
                    }
                    catch { }
                }
                s_componentScratch.Clear();
            }
        }

        private static int IndexOfEffect(Behaviour b)
        {
            for (int i = 0; i < s_disabledEffects.Count; i++)
                if (ReferenceEquals(s_disabledEffects[i], b)) return i;
            return -1;
        }

        private static void ApplyHideScene(string mode)
        {
            for (int i = s_hiddenAtoms.Count - 1; i >= 0; i--)
            {
                Atom a = s_hiddenAtoms[i];
                if (a == null)
                {
                    s_hiddenAtoms.RemoveAt(i);
                    continue;
                }
                if (ShouldHide(a, mode))
                {
                    SetAtomRenderCutOut(a, true);
                    continue;
                }
                SetAtomRenderCutOut(a, false);
                s_hiddenAtoms.RemoveAt(i);
            }

            if (string.IsNullOrEmpty(mode) || string.Equals(mode, HideNothing, StringComparison.OrdinalIgnoreCase))
            {
                LogHideIfChanged(HideNothing);
                return;
            }

            List<Atom> atoms;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                atoms = sc.GetAtoms();
            }
            catch { return; }
            if (atoms == null) return;

            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                if (!ShouldHide(a, mode)) continue;
                if (IndexOfHidden(a) >= 0)
                {
                    SetAtomRenderCutOut(a, true);
                    continue;
                }
                bool alreadyCut;
                try { alreadyCut = a.tempDisableRender; }
                catch { continue; }
                if (alreadyCut) continue;
                SetAtomRenderCutOut(a, true);
                s_hiddenAtoms.Add(a);
            }
            LogHideIfChanged(mode);
        }

        private static int IndexOfHidden(Atom a)
        {
            if (a == null) return -1;
            for (int i = 0; i < s_hiddenAtoms.Count; i++)
                if (ReferenceEquals(s_hiddenAtoms[i], a)) return i;
            return -1;
        }

        private static void SetAtomRenderCutOut(Atom a, bool cut)
        {
            if (a == null) return;
            try
            {
                if (a.tempDisableRender != cut)
                    a.tempDisableRender = cut;
            }
            catch { }
            if (cut) return;
            try
            {
                if (a.tempHidden)
                    a.tempHidden = false;
            }
            catch { }
        }

        private static void LogHideIfChanged(string mode)
        {
            int n = s_hiddenAtoms.Count;
            string used = mode ?? "";
            if (n == s_lastLoggedHideCount && string.Equals(used, s_lastLoggedHideMode, StringComparison.Ordinal))
                return;
            bool wasUnset = s_lastLoggedHideCount < 0;
            s_lastLoggedHideCount = n;
            s_lastLoggedHideMode = used;
            if (wasUnset && n == 0) return;
            LogUtil.Log("[VPB] Passthrough hide " + used + " atoms=" + n);
        }

        private static void UnhideAll()
        {
            for (int i = 0; i < s_hiddenAtoms.Count; i++)
            {
                Atom a = s_hiddenAtoms[i];
                if (a == null) continue;
                SetAtomRenderCutOut(a, false);
            }
            s_hiddenAtoms.Clear();
            LogHideIfChanged(HideNothing);
        }

        private static bool ShouldHide(Atom a, string mode)
        {
            if (a == null) return false;
            try { if (VpbPassthroughLights.IsOwnedAtom(a)) return false; }
            catch { }

            string type;
            string category;
            try
            {
                type = a.type ?? "";
                category = a.category ?? "";
            }
            catch { return false; }

            return ShouldHideType(type, category, mode);
        }

        internal static bool ShouldHideType(string type, string category, string mode)
        {
            if (string.IsNullOrEmpty(mode) || string.Equals(mode, HideNothing, StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrEmpty(type)) type = "";
            if (string.IsNullOrEmpty(category)) category = "";

            if (ProtectedAtomTypes.Contains(type)) return false;
            if (SceneUtils.IsLightAtomType(type)) return false;
            if (category.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (string.Equals(type, "Empty", StringComparison.OrdinalIgnoreCase)) return false;

            if (string.Equals(mode, HideAllButPeople, StringComparison.OrdinalIgnoreCase))
                return !SceneUtils.IsPersonLikeAtomType(type);

            if (SceneUtils.IsPersonLikeAtomType(type)) return false;
            return IsRoomGeometry(type, category);
        }

        private static bool IsRoomGeometry(string type, string category)
        {
            if (!string.IsNullOrEmpty(category))
            {
                if (category.IndexOf("environ", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (category.IndexOf("furniture", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            if (string.IsNullOrEmpty(type)) return false;
            if (SceneUtils.IsCreatorStripAlwaysDropAtomType(type)) return true;
            CreatorStripKeepKind kind = SceneUtils.ClassifyCreatorStripKeepKind(type);
            return kind == CreatorStripKeepKind.Assets
                || kind == CreatorStripKeepKind.Props
                || kind == CreatorStripKeepKind.Other
                || kind == CreatorStripKeepKind.UI;
        }
    }
}
