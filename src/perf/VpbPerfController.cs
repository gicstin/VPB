using System;
using System.Collections;
using System.Collections.Generic;
using MeshVR;
using UnityEngine;

namespace VPB
{
    /// <summary>Discrete hair density/multiplier + mirror texture steps; last step ≈ VaM defaults.</summary>
    public static class VpbPerfController
    {
        struct PerfStep
        {
            public float CurveDensity;
            public float HairMultiplier;
            public string MirrorTexSize;
        }

        struct AppliedValues
        {
            public float CurveDensity;
            public float HairMultiplier;
            public string MirrorTexSize;
        }

        struct VaMQualityStep
        {
            public float RenderScale;
            public int MsaaLevel;
            public int PixelLightCount;
            public UserPreferences.ShaderLOD ShaderLod;
            public int SmoothPasses;
            public bool MirrorReflections;
            public bool RealtimeReflectionProbes;
            public bool SoftPhysics;
            public UserPreferences.GlowEffectsLevel GlowEffects;
        }

        // Step 0 = low, 5 = mid, 9 = high (VaM UltraLow … Max).
        static readonly string[] s_VaMQualityPresetByStep =
        {
            "UltraLow", "UltraLow", "Low", "Low", "Low", "Mid", "Mid", "High", "Ultra", "Max",
        };

        static readonly VaMQualityStep s_QualityUltraLow = new VaMQualityStep { RenderScale = 1f, MsaaLevel = 0, PixelLightCount = 0, ShaderLod = UserPreferences.ShaderLOD.Low, SmoothPasses = 1, MirrorReflections = false, RealtimeReflectionProbes = false, SoftPhysics = false, GlowEffects = UserPreferences.GlowEffectsLevel.Off };
        static readonly VaMQualityStep s_QualityLow = new VaMQualityStep { RenderScale = 1f, MsaaLevel = 2, PixelLightCount = 1, ShaderLod = UserPreferences.ShaderLOD.Low, SmoothPasses = 1, MirrorReflections = false, RealtimeReflectionProbes = false, SoftPhysics = false, GlowEffects = UserPreferences.GlowEffectsLevel.Off };
        static readonly VaMQualityStep s_QualityMid = new VaMQualityStep { RenderScale = 1f, MsaaLevel = 4, PixelLightCount = 2, ShaderLod = UserPreferences.ShaderLOD.High, SmoothPasses = 1, MirrorReflections = false, RealtimeReflectionProbes = false, SoftPhysics = true, GlowEffects = UserPreferences.GlowEffectsLevel.Low };
        static readonly VaMQualityStep s_QualityHigh = new VaMQualityStep { RenderScale = 1f, MsaaLevel = 4, PixelLightCount = 2, ShaderLod = UserPreferences.ShaderLOD.High, SmoothPasses = 2, MirrorReflections = true, RealtimeReflectionProbes = true, SoftPhysics = true, GlowEffects = UserPreferences.GlowEffectsLevel.Low };
        static readonly VaMQualityStep s_QualityUltra = new VaMQualityStep { RenderScale = 1.5f, MsaaLevel = 2, PixelLightCount = 3, ShaderLod = UserPreferences.ShaderLOD.High, SmoothPasses = 3, MirrorReflections = true, RealtimeReflectionProbes = true, SoftPhysics = true, GlowEffects = UserPreferences.GlowEffectsLevel.High };
        static readonly VaMQualityStep s_QualityMax = new VaMQualityStep { RenderScale = 2f, MsaaLevel = 2, PixelLightCount = 4, ShaderLod = UserPreferences.ShaderLOD.High, SmoothPasses = 4, MirrorReflections = true, RealtimeReflectionProbes = true, SoftPhysics = true, GlowEffects = UserPreferences.GlowEffectsLevel.High };

        // Steps 0–9: 0 lowest hair/mirror (GiveMeFPS “all FPS”), 5 mid, 9 highest.
        static readonly PerfStep[] s_Steps =
        {
            new PerfStep { CurveDensity = 10f, HairMultiplier = 2f, MirrorTexSize = "512" },
            new PerfStep { CurveDensity = 12f, HairMultiplier = 4f, MirrorTexSize = "512" },
            new PerfStep { CurveDensity = 14f, HairMultiplier = 6f, MirrorTexSize = "512" },
            new PerfStep { CurveDensity = 16f, HairMultiplier = 8f, MirrorTexSize = "1024" },
            new PerfStep { CurveDensity = 18f, HairMultiplier = 10f, MirrorTexSize = "1024" },
            new PerfStep { CurveDensity = 20f, HairMultiplier = 12f, MirrorTexSize = "1024" },
            new PerfStep { CurveDensity = 24f, HairMultiplier = 16f, MirrorTexSize = "1024" },
            new PerfStep { CurveDensity = 28f, HairMultiplier = 22f, MirrorTexSize = "2048" },
            new PerfStep { CurveDensity = 30f, HairMultiplier = 28f, MirrorTexSize = "2048" },
            new PerfStep { CurveDensity = 32f, HairMultiplier = 32f, MirrorTexSize = "2048" },
        };

        public const int PerfStepScaleVersion = 3;

        public static int StepCount { get { return s_Steps != null ? s_Steps.Length : 0; } }
        public static bool Enabled { get; private set; }
        public static int StepIndex { get; private set; }

        static readonly VpbPerfSnapshot s_Snapshot = new VpbPerfSnapshot();
        static readonly Dictionary<string, Coroutine> s_HairApplyByAtomUid =
            new Dictionary<string, Coroutine>(StringComparer.Ordinal);
        static bool s_Initialized;
        static bool s_GiveMeFpsWarned;
        static bool s_RecaptureSnapshotBeforeApply;
        /// <summary>Block applies while scene loads (native values must load first).</summary>
        static bool s_SuppressApply;
        /// <summary>Config wants perf on; waiting for scene load before capture + Enabled=true.</summary>
        static bool s_AutoEnableAfterSceneLoad;
        static bool s_BaselineCaptured;
        static Coroutine s_ApplyCo;

        /// <summary>True while perf step is actively applied (false during scene native load).</summary>
        public static bool IsAutoEnablePending { get { return s_AutoEnableAfterSceneLoad; } }

        /// <summary>User wants perf on (saved cfg), including after restart before apply.</summary>
        public static bool IsPerfModeWanted
        {
            get
            {
                if (Enabled || s_AutoEnableAfterSceneLoad) return true;
                try
                {
                    var cfg = VPBConfig.Instance;
                    return cfg != null && cfg.PerfModeEnabled;
                }
                catch { return false; }
            }
        }

        public static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                var sc = SuperController.singleton;
                if (sc != null)
                    sc.onAtomAddedHandlers += OnAtomAdded;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] Initialize handlers: " + ex.Message);
            }

            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null) return;
                StepIndex = VPBConfig.ClampPerfStepIndex(cfg.PerfStepIndex);
                Enabled = false;
                s_AutoEnableAfterSceneLoad = cfg.PerfModeEnabled;
                StopPendingApply();
                RefreshAllGalleryFooters();
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (!s_Initialized) return;
            s_Initialized = false;
            try
            {
                var sc = SuperController.singleton;
                if (sc != null)
                    sc.onAtomAddedHandlers -= OnAtomAdded;
            }
            catch { }
            StopAllHairApplyCoroutines();
            try { SetEnabled(false, false, false); } catch { }
        }

        static void StopAllHairApplyCoroutines()
        {
            if (VamHookPlugin.singleton == null)
            {
                s_HairApplyByAtomUid.Clear();
                return;
            }
            foreach (Coroutine co in s_HairApplyByAtomUid.Values)
            {
                if (co == null) continue;
                try { VamHookPlugin.singleton.StopCoroutine(co); } catch { }
            }
            s_HairApplyByAtomUid.Clear();
        }

        static void StopPendingApply()
        {
            StopAllHairApplyCoroutines();
            if (s_ApplyCo != null && VamHookPlugin.singleton != null)
            {
                try { VamHookPlugin.singleton.StopCoroutine(s_ApplyCo); } catch { }
            }
            s_ApplyCo = null;
        }

        /// <summary>Cancel post-load auto-enable (footer Off while pending). Keeps scene at native values.</summary>
        public static void CancelAutoEnableAfterSceneLoad(bool persist, bool showStatus)
        {
            if (!s_AutoEnableAfterSceneLoad) return;
            StopPendingApply();
            s_AutoEnableAfterSceneLoad = false;
            Enabled = false;
            if (persist) PersistSettings();
            if (showStatus) NotifyStatus();
            RefreshAllGalleryFooters();
        }

        public static void SetEnabled(bool enabled, bool persist, bool showStatus)
        {
            if (!enabled)
            {
                StopPendingApply();
                s_AutoEnableAfterSceneLoad = false;
                s_SuppressApply = true;
                try
                {
                    if (s_BaselineCaptured || s_Snapshot.Captured)
                        RestoreSnapshot();
                }
                finally
                {
                    s_BaselineCaptured = false;
                    s_SuppressApply = false;
                    Enabled = false;
                }
            }
            else if (Enabled)
            {
                RefreshAllGalleryFooters();
                if (persist) PersistSettings();
                if (showStatus) NotifyStatus();
                return;
            }
            else
            {
                s_AutoEnableAfterSceneLoad = false;
                StopPendingApply();
                Enabled = true;
                WarnGiveMeFpsConflictOnce();
                s_RecaptureSnapshotBeforeApply = true;
                ScheduleApply(showStatus);
            }

            if (persist) PersistSettings();
            if (showStatus) NotifyStatus();
            RefreshAllGalleryFooters();
        }

        public static void SetStepIndex(int index, bool persist, bool applyNow, bool showStatus)
        {
            int clamped = VPBConfig.ClampPerfStepIndex(index);
            if (clamped == StepIndex)
            {
                RefreshAllGalleryFooters();
                return;
            }
            StepIndex = clamped;
            if (persist) PersistSettings();
            if (applyNow && Enabled)
                ScheduleApply(false);
            if (showStatus) NotifyStatus();
            RefreshAllGalleryFooters();
        }

        public static void StepBy(int delta, bool persist, bool showStatus)
        {
            if (delta == 0) return;
            int next = StepIndex + delta;
            if (next < 0 || next >= StepCount) return;
            SetStepIndex(next, persist, true, showStatus);
        }

        public static bool CanStepDown()
        {
            return StepIndex > 0;
        }

        public static bool CanStepUp()
        {
            return StepIndex < StepCount - 1;
        }

        public static void RefreshAllGalleryFooters()
        {
            try
            {
                if (Gallery.singleton == null || Gallery.singleton.Panels == null) return;
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                {
                    if (panel != null) panel.RefreshFooterPerfChrome();
                }
            }
            catch { }
            try { VamHookPlugin.singleton?.RefreshQuickMenuPerfModeSlots(); } catch { }
        }

        public static Color GetToggleBackdropColor()
        {
            if (Enabled) return GalleryUiColorTokens.ActiveOn;
            if (IsPerfModeWanted) return new Color(0.18f, 0.22f, 0.20f, 0.65f);
            return new Color(0.12f, 0.12f, 0.14f, 0.55f);
        }

        public static Color GetToggleTextColor()
        {
            if (Enabled) return new Color(0.45f, 0.95f, 0.50f, 1f);
            if (IsPerfModeWanted) return new Color(0.55f, 0.85f, 0.58f, 1f);
            return Color.white;
        }

        public static Color GetStepButtonColor(bool canUse)
        {
            if (!IsPerfModeWanted || !canUse) return new Color(0.12f, 0.12f, 0.14f, 0.35f);
            if (!Enabled) return new Color(0.18f, 0.22f, 0.20f, 0.65f);
            return GalleryUiColorTokens.ChromeIconWell;
        }

        public static string GetToggleLabel()
        {
            if (!IsPerfModeWanted)
                return VPBTranslation.T("gallery.perf.off", "Off");
            return StepIndex.ToString();
        }

        public static int StepIndexMax()
        {
            return Math.Max(0, StepCount - 1);
        }

        public static string GetTooltip()
        {
            string baseTip = VPBTranslation.T("gallery.perf.tooltip.toggle",
                "Scenes load at native quality first. When On, your quality level applies after load; Off restores this scene's saved values.");
            if (s_AutoEnableAfterSceneLoad && !Enabled)
                return baseTip + " " + VPBTranslation.T("gallery.perf.tooltip.pending",
                    "(Waiting for scene load — click Off to keep native quality.)");
            return baseTip;
        }

        public static string GetStepTooltip()
        {
            return string.Format(
                VPBTranslation.T("gallery.perf.tooltip.step",
                    "Quality level {0} of {1} (0 = lowest, 5 = balanced, 9 = highest). Choose targets in Settings → Quality."),
                StepIndex,
                StepIndexMax());
        }

        /// <summary>Settings toggles changed — re-apply if perf On.</summary>
        public static void OnApplyTargetsChanged()
        {
            if (!Enabled) return;
            s_RecaptureSnapshotBeforeApply = true;
            ScheduleApply(false);
        }

        /// <summary>Full scene load started — keep perf off until <see cref="OnSceneLoadComplete"/>.</summary>
        public static void OnSceneLoadStarting(string saveName, bool loadMerge)
        {
            if (loadMerge) return;
            try
            {
                StopPendingApply();
                Enabled = false;
                s_SuppressApply = false;
                ResetSnapshot();
                s_BaselineCaptured = false;

                var cfg = VPBConfig.Instance;
                if (cfg != null)
                    StepIndex = VPBConfig.ClampPerfStepIndex(cfg.PerfStepIndex);

                s_AutoEnableAfterSceneLoad = cfg != null && cfg.PerfModeEnabled;
                LogUtil.Log("[VPB.Perf] scene load started autoEnable=" + (s_AutoEnableAfterSceneLoad ? "1" : "0")
                    + " save=" + (saveName ?? ""));
                RefreshAllGalleryFooters();
            }
            catch { }
        }

        /// <summary>VPB SCENE_LOAD_TOTAL finished — hair/atoms settled; safe to capture native baseline.</summary>
        public static void OnSceneLoadComplete()
        {
            try
            {
                if (!s_AutoEnableAfterSceneLoad)
                    return;
                LogUtil.Log("[VPB.Perf] scene load complete — scheduling auto-enable");
                SchedulePostSceneLoadAutoEnable(false);
            }
            catch { }
        }

        internal static void OnHairLoadCompleted(DAZHairGroup hair)
        {
            if (!Enabled || s_SuppressApply || s_AutoEnableAfterSceneLoad) return;
            try
            {
                if (hair == null || hair.characterSelector == null) return;
                Atom atom = hair.characterSelector.containingAtom;
                if (atom != null && atom.type == "Person")
                    SchedulePersonHairApply(atom);
            }
            catch { }
        }

        static void SchedulePostSceneLoadAutoEnable(bool showStatus)
        {
            StopPendingApply();
            if (VamHookPlugin.singleton == null)
            {
                if (!s_AutoEnableAfterSceneLoad) return;
                CaptureSnapshotForCurrentScene(resetFirst: true);
                s_BaselineCaptured = s_Snapshot.Captured;
                Enabled = true;
                WarnGiveMeFpsConflictOnce();
                ApplyStepImmediate();
                s_AutoEnableAfterSceneLoad = false;
                if (showStatus) NotifyStatus();
                RefreshAllGalleryFooters();
                return;
            }
            if (s_ApplyCo != null)
            {
                try { VamHookPlugin.singleton.StopCoroutine(s_ApplyCo); } catch { }
                s_ApplyCo = null;
            }
            s_ApplyCo = VamHookPlugin.singleton.StartCoroutine(PostSceneLoadAutoEnableCo(showStatus));
        }

        static IEnumerator PostSceneLoadAutoEnableCo(bool showStatus)
        {
            Enabled = false;
            s_SuppressApply = true;
            try
            {
                // SCENE_LOAD_TOTAL already finished; only wait for per-hair sim controls.
                yield return WaitForNativePersonStateReady();
                yield return new WaitForSeconds(0.35f);

                if (!s_AutoEnableAfterSceneLoad)
                    yield break;

                CaptureSnapshotForCurrentScene(resetFirst: true);
                s_BaselineCaptured = s_Snapshot.Captured;

                Enabled = true;
                s_AutoEnableAfterSceneLoad = false;
                s_SuppressApply = false;
                WarnGiveMeFpsConflictOnce();
                ApplyStepImmediate();
                ScheduleAllPersonHairApply();
                PersistSettings();
                if (showStatus) NotifyStatus();
            }
            finally
            {
                s_SuppressApply = false;
                s_ApplyCo = null;
                RefreshAllGalleryFooters();
            }
        }

        static void ScheduleAllPersonHairApply()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return;
                foreach (Atom atom in sc.GetAtoms())
                {
                    if (atom != null && atom.type == "Person")
                        SchedulePersonHairApply(atom);
                }
            }
            catch { }
        }

        static void OnAtomAdded(Atom atom)
        {
            try
            {
                if (atom == null || !Enabled || s_SuppressApply) return;
                var cfg = VPBConfig.Instance;
                if (cfg != null && !cfg.PerfApplyHair && !cfg.PerfApplyMirrors) return;
                if (cfg == null || cfg.PerfApplyHair)
                    SchedulePersonHairApply(atom);
                else if (cfg.PerfApplyMirrors)
                {
                    AppliedValues v = GetValuesForStep(StepIndex);
                    SetMirrorTextureSize(atom, v.MirrorTexSize);
                }
            }
            catch { }
        }

        public static void SchedulePersonHairApply(Atom atom)
        {
            if (!Enabled || s_SuppressApply || s_AutoEnableAfterSceneLoad || atom == null || atom.type != "Person") return;
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg != null && !cfg.PerfApplyHair) return;
            }
            catch { return; }
            try
            {
                if (VamHookPlugin.singleton == null)
                {
                    ApplyPersonHair(atom, GetValuesForStep(StepIndex));
                    return;
                }

                string uid = atom.uid;
                if (string.IsNullOrEmpty(uid)) return;

                if (s_HairApplyByAtomUid.TryGetValue(uid, out Coroutine existing) && existing != null)
                {
                    try { VamHookPlugin.singleton.StopCoroutine(existing); } catch { }
                }

                Coroutine co = VamHookPlugin.singleton.StartCoroutine(ApplyPersonHairWhenReadyCo(atom));
                s_HairApplyByAtomUid[uid] = co;
            }
            catch { }
        }

        static void ScheduleApply(bool showStatus)
        {
            if (!Enabled) return;
            if (VamHookPlugin.singleton == null)
            {
                if (s_RecaptureSnapshotBeforeApply)
                {
                    CaptureSnapshotForCurrentScene(resetFirst: true);
                    s_BaselineCaptured = s_Snapshot.Captured;
                    s_RecaptureSnapshotBeforeApply = false;
                }
                ApplyStepImmediate();
                if (showStatus) NotifyStatus();
                return;
            }
            if (s_ApplyCo != null)
            {
                try { VamHookPlugin.singleton.StopCoroutine(s_ApplyCo); } catch { }
                s_ApplyCo = null;
            }
            s_ApplyCo = VamHookPlugin.singleton.StartCoroutine(ApplyWhenReadyCo(showStatus));
        }

        static IEnumerator ApplyWhenReadyCo(bool showStatus)
        {
            try
            {
                yield return WaitForSceneReady();
                if (!Enabled)
                    yield break;

                if (s_RecaptureSnapshotBeforeApply)
                {
                    CaptureSnapshotForCurrentScene(resetFirst: true);
                    s_BaselineCaptured = s_Snapshot.Captured;
                    s_RecaptureSnapshotBeforeApply = false;
                }
                ApplyStepImmediate();
                if (showStatus) NotifyStatus();
            }
            finally
            {
                s_ApplyCo = null;
            }
        }

        /// <summary>After scene load, wait until person hair/mirror storables reflect scene JSON.</summary>
        static IEnumerator WaitForNativePersonStateReady()
        {
            for (int i = 0; i < 24; i++)
            {
                if (AllTrackedPersonsNativeReady())
                    yield break;
                yield return new WaitForSeconds(0.25f);
            }
            yield return null;
        }

        static bool AllTrackedPersonsNativeReady()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return true;
                foreach (Atom atom in sc.GetAtoms())
                {
                    if (atom == null || atom.type != "Person") continue;
                    if (!PersonHairControlsReady(atom))
                        return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        static IEnumerator ApplyPersonHairWhenReadyCo(Atom atom)
        {
            string uid = atom != null ? atom.uid : null;
            yield return null;

            AppliedValues v = GetValuesForStep(StepIndex);
            for (int i = 0; i < 24; i++)
            {
                if (!Enabled || s_SuppressApply || s_AutoEnableAfterSceneLoad || atom == null) yield break;
                ApplyPersonHair(atom, v);
                if (PersonHairControlsReady(atom))
                    break;
                yield return new WaitForSeconds(0.25f);
            }

            if (!string.IsNullOrEmpty(uid))
                s_HairApplyByAtomUid.Remove(uid);
        }

        static IEnumerator ApplyAtomWhenReady(Atom atom)
        {
            AppliedValues v = GetValuesForStep(StepIndex);
            for (int i = 0; i < 12; i++)
            {
                if (!Enabled || atom == null) yield break;
                if (!SuperController.singleton.isLoading && !SuperController.singleton.freezeAnimation)
                {
                    var cfg = VPBConfig.Instance;
                    if (atom.type == "Person" && (cfg == null || cfg.PerfApplyHair))
                        SchedulePersonHairApply(atom);
                    if (cfg == null || cfg.PerfApplyMirrors)
                        SetMirrorTextureSize(atom, v.MirrorTexSize);
                    yield break;
                }
                yield return new WaitForSeconds(0.35f);
            }
        }

        static bool PersonHairControlsReady(Atom atom)
        {
            if (atom == null) return true;
            try
            {
                var geometry = atom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (geometry == null || geometry.hairItems == null) return true;

                DAZHairGroup[] items = geometry.hairItems;
                for (int i = 0; i < items.Length; i++)
                {
                    DAZHairGroup item = items[i];
                    if (item == null || !item.active) continue;
                    if (!item.ready) return false;
                    if (item.GetComponentInChildren<HairSimControl>(true) == null)
                        return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        static IEnumerator WaitForSceneReady()
        {
            var sc = SuperController.singleton;
            if (sc == null) yield break;
            int frames = 0;
            while (sc.isLoading && frames < 600)
            {
                frames++;
                yield return null;
            }
            frames = 0;
            while (sc.freezeAnimation && frames < 120)
            {
                frames++;
                yield return null;
            }
            yield return null;
        }

        static AppliedValues GetValuesForStep(int index)
        {
            int i = VPBConfig.ClampPerfStepIndex(index);
            var s = s_Steps[i];
            return new AppliedValues
            {
                CurveDensity = s.CurveDensity,
                HairMultiplier = s.HairMultiplier,
                MirrorTexSize = s.MirrorTexSize,
            };
        }

        static void ApplyStepImmediate()
        {
            if (!Enabled || s_SuppressApply) return;
            AppliedValues v = GetValuesForStep(StepIndex);
            try
            {
                ApplyGlobalForStep(StepIndex);
                var cfg = VPBConfig.Instance;
                bool applyHair = cfg == null || cfg.PerfApplyHair;
                bool applyMirrors = cfg == null || cfg.PerfApplyMirrors;
                foreach (Atom atom in SuperController.singleton.GetAtoms())
                {
                    if (atom == null) continue;
                    if (atom.type == "Person" && applyHair)
                        ApplyPersonHair(atom, v);
                    if (applyMirrors)
                        SetMirrorTextureSize(atom, v.MirrorTexSize);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] apply: " + ex.Message);
            }
        }

        static bool AnyGlobalApplyTarget(VPBConfig cfg)
        {
            if (cfg == null) return false;
            return cfg.PerfApplyRenderScale
                || cfg.PerfApplyMsaa
                || cfg.PerfApplyPixelLightCount
                || cfg.PerfApplySmoothPasses
                || cfg.PerfApplyMirrorReflections
                || cfg.PerfApplyRealtimeReflectionProbes
                || cfg.PerfApplySoftPhysics
                || cfg.PerfApplyGlowEffects;
        }

        static VaMQualityStep GetVaMQualityForStep(int stepIndex)
        {
            int i = VPBConfig.ClampPerfStepIndex(stepIndex);
            string preset = s_VaMQualityPresetByStep[i];
            if (string.Equals(preset, "UltraLow", StringComparison.OrdinalIgnoreCase)) return s_QualityUltraLow;
            if (string.Equals(preset, "Low", StringComparison.OrdinalIgnoreCase)) return s_QualityLow;
            if (string.Equals(preset, "Mid", StringComparison.OrdinalIgnoreCase)) return s_QualityMid;
            if (string.Equals(preset, "High", StringComparison.OrdinalIgnoreCase)) return s_QualityHigh;
            if (string.Equals(preset, "Ultra", StringComparison.OrdinalIgnoreCase)) return s_QualityUltra;
            return s_QualityMax;
        }

        static void ApplyGlobalForStep(int stepIndex)
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null || !AnyGlobalApplyTarget(cfg)) return;
            UserPreferences up = UserPreferences.singleton;
            if (up == null) return;

            int i = VPBConfig.ClampPerfStepIndex(stepIndex);
            VaMQualityStep q = GetVaMQualityForStep(i);
            if (cfg.PerfApplyRenderScale) up.renderScale = q.RenderScale;
            if (cfg.PerfApplyMsaa) up.msaaLevel = q.MsaaLevel;
            if (cfg.PerfApplyPixelLightCount) up.pixelLightCount = q.PixelLightCount;
            if (cfg.PerfApplySmoothPasses) up.smoothPasses = q.SmoothPasses;
            if (cfg.PerfApplyMirrorReflections) up.mirrorReflections = q.MirrorReflections;
            if (cfg.PerfApplyRealtimeReflectionProbes) up.realtimeReflectionProbes = q.RealtimeReflectionProbes;
            if (cfg.PerfApplySoftPhysics) up.softPhysics = q.SoftPhysics;
            if (cfg.PerfApplyGlowEffects) up.glowEffects = q.GlowEffects;
        }

        static void ApplyPersonHair(Atom atom, AppliedValues v)
        {
            if (!Enabled || s_SuppressApply || s_AutoEnableAfterSceneLoad || atom == null || atom.type != "Person") return;
            try
            {
                foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>(true))
                {
                    HairSimControl hairControl = hairGroup != null ? hairGroup.GetComponentInChildren<HairSimControl>(true) : null;
                    if (hairControl == null) continue;
                    WriteFloatParam(hairControl, "curveDensity", v.CurveDensity);
                    WriteFloatParam(hairControl, "hairMultiplier", v.HairMultiplier);
                }
            }
            catch { }
        }

        static void ApplyMirrorTexture(Atom atom, string textureSize)
        {
            if (!Enabled) return;
            SetMirrorTextureSize(atom, textureSize);
        }

        static void SetMirrorTextureSize(Atom atom, string textureSize)
        {
            if (atom == null || string.IsNullOrEmpty(textureSize)) return;
            try
            {
                JSONStorable mirror = atom.GetStorableByID("MirrorRender");
                if (mirror == null) return;
                var ts = mirror.GetStringChooserJSONParam("textureSize");
                if (ts != null) ts.val = textureSize;
            }
            catch { }
        }

        static void ResetSnapshot()
        {
            s_Snapshot.Captured = false;
            s_Snapshot.Persons.Clear();
            s_Snapshot.Global.Captured = false;
        }

        /// <summary>In-memory baseline for current scene only (restore on Off). Not saved into scene files.</summary>
        static void CaptureSnapshotForCurrentScene(bool resetFirst)
        {
            if (resetFirst)
                ResetSnapshot();
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg != null && AnyGlobalApplyTarget(cfg))
                    CaptureGlobalSnapshot();

                bool capturePerson = cfg == null || cfg.PerfApplyHair || cfg.PerfApplyMirrors;
                if (capturePerson)
                {
                    foreach (Atom atom in SuperController.singleton.GetAtoms())
                    {
                        if (atom == null || atom.type != "Person") continue;
                        s_Snapshot.Persons.Add(CapturePerson(atom, cfg));
                    }
                }
                s_Snapshot.Captured = s_Snapshot.Persons.Count > 0 || s_Snapshot.Global.Captured;
                if (s_Snapshot.Captured)
                {
                    string sample = GetBaselineHairSample();
                    LogUtil.Log("[VPB.Perf] baseline captured persons=" + s_Snapshot.Persons.Count
                        + " hair=" + CountHairSnapshots() + " global=" + (s_Snapshot.Global.Captured ? "1" : "0")
                        + (string.IsNullOrEmpty(sample) ? "" : " sample=" + sample));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] CaptureSnapshot: " + ex.Message);
            }
        }

        static int CountHairSnapshots()
        {
            int n = 0;
            foreach (VpbPersonPerfSnapshot p in s_Snapshot.Persons)
                if (p != null && p.Hair != null) n += p.Hair.Count;
            return n;
        }

        static string GetBaselineHairSample()
        {
            foreach (VpbPersonPerfSnapshot p in s_Snapshot.Persons)
            {
                if (p == null || p.Hair == null || p.Hair.Count == 0) continue;
                VpbHairPerfSnapshot h = p.Hair[0];
                if (h == null) continue;
                return h.CurveDensity.ToString("0.##") + "x" + h.HairMultiplier.ToString("0.##");
            }
            return null;
        }

        static void CaptureGlobalSnapshot()
        {
            UserPreferences up = UserPreferences.singleton;
            if (up == null) return;
            var g = s_Snapshot.Global;
            g.RenderScale = up.renderScale;
            g.MsaaLevel = up.msaaLevel;
            g.PixelLightCount = up.pixelLightCount;
            g.ShaderLod = (int)up.shaderLOD;
            g.SmoothPasses = up.smoothPasses;
            g.MirrorReflections = up.mirrorReflections;
            g.RealtimeReflectionProbes = up.realtimeReflectionProbes;
            g.SoftPhysics = up.softPhysics;
            g.GlowEffects = (int)up.glowEffects;
            g.Captured = true;
        }

        static VpbPersonPerfSnapshot CapturePerson(Atom atom, VPBConfig cfg)
        {
            var snap = new VpbPersonPerfSnapshot { AtomUid = atom.uid };
            bool captureHair = cfg == null || cfg.PerfApplyHair;
            bool captureMirrors = cfg == null || cfg.PerfApplyMirrors;

            if (captureHair)
            {
                try
                {
                    foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>(true))
                    {
                        HairSimControl hc = hairGroup != null ? hairGroup.GetComponentInChildren<HairSimControl>(true) : null;
                        if (hc == null) continue;
                        var h = new VpbHairPerfSnapshot
                        {
                            HairItemUid = hairGroup.uid,
                            ControlUid = hc.name,
                        };
                        h.CurveDensity = ReadFloat(hc, "curveDensity", h.CurveDensity);
                        h.HairMultiplier = ReadFloat(hc, "hairMultiplier", h.HairMultiplier);
                        snap.Hair.Add(h);
                    }
                }
                catch { }
            }

            if (captureMirrors)
            {
                try
                {
                    JSONStorable mirror = atom.GetStorableByID("MirrorRender");
                    if (mirror != null)
                    {
                        snap.HasMirrorRender = true;
                        var ts = mirror.GetStringChooserJSONParam("textureSize");
                        if (ts != null) snap.MirrorTextureSize = ts.val;
                    }
                }
                catch { }
            }

            return snap;
        }

        static float ReadFloat(JSONStorable storable, string param, float fallback)
        {
            try
            {
                var f = storable.GetFloatJSONParam(param);
                if (f != null) return f.val;
            }
            catch { }
            return fallback;
        }

        static void RestoreSnapshot()
        {
            if (!s_Snapshot.Captured) return;
            int restoredHair = 0;
            try
            {
                if (s_Snapshot.Global.Captured)
                    RestoreGlobalSnapshot();
                foreach (VpbPersonPerfSnapshot ps in s_Snapshot.Persons)
                    restoredHair += RestorePerson(ps);
                LogUtil.Log("[VPB.Perf] restore hairWrites=" + restoredHair);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Perf] RestoreSnapshot: " + ex.Message);
            }
            finally
            {
                s_Snapshot.Captured = false;
                s_Snapshot.Persons.Clear();
                s_Snapshot.Global.Captured = false;
                s_BaselineCaptured = false;
            }
        }

        static void RestoreGlobalSnapshot()
        {
            UserPreferences up = UserPreferences.singleton;
            var g = s_Snapshot.Global;
            if (up == null || !g.Captured) return;
            try
            {
                up.renderScale = g.RenderScale;
                up.msaaLevel = g.MsaaLevel;
                up.pixelLightCount = g.PixelLightCount;
                up.shaderLOD = (UserPreferences.ShaderLOD)g.ShaderLod;
                up.smoothPasses = g.SmoothPasses;
                up.mirrorReflections = g.MirrorReflections;
                up.realtimeReflectionProbes = g.RealtimeReflectionProbes;
                up.softPhysics = g.SoftPhysics;
                up.glowEffects = (UserPreferences.GlowEffectsLevel)g.GlowEffects;
            }
            catch { }
        }

        static int RestorePerson(VpbPersonPerfSnapshot ps)
        {
            int writes = 0;
            if (ps == null || string.IsNullOrEmpty(ps.AtomUid)) return writes;
            Atom atom = SuperController.singleton.GetAtomByUid(ps.AtomUid);
            if (atom == null) return writes;

            try
            {
                foreach (VpbHairPerfSnapshot h in ps.Hair)
                {
                    HairSimControl hc = FindHairControl(atom, h);
                    if (hc == null) continue;
                    WriteFloatParam(hc, "curveDensity", h.CurveDensity);
                    WriteFloatParam(hc, "hairMultiplier", h.HairMultiplier);
                    writes++;
                }
            }
            catch { }

            if (ps.HasMirrorRender && !string.IsNullOrEmpty(ps.MirrorTextureSize))
                SetMirrorTextureSize(atom, ps.MirrorTextureSize);
            return writes;
        }

        static HairSimControl FindHairControl(Atom atom, VpbHairPerfSnapshot h)
        {
            if (atom == null || h == null) return null;
            foreach (DAZHairGroup hairGroup in atom.GetComponentsInChildren<DAZHairGroup>(true))
            {
                if (hairGroup == null) continue;
                if (!string.IsNullOrEmpty(h.HairItemUid)
                    && !string.Equals(hairGroup.uid, h.HairItemUid, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrEmpty(h.HairItemUid)
                    && !string.IsNullOrEmpty(h.ControlUid))
                {
                    HairSimControl byName = hairGroup.GetComponentInChildren<HairSimControl>(true);
                    if (byName == null || byName.name != h.ControlUid) continue;
                }
                HairSimControl hc = hairGroup.GetComponentInChildren<HairSimControl>(true);
                if (hc != null) return hc;
            }
            return null;
        }

        static void WriteFloatParam(JSONStorable storable, string param, float value)
        {
            if (storable == null) return;
            try
            {
                var f = storable.GetFloatJSONParam(param);
                if (f != null) f.val = value;
            }
            catch { }
            try { storable.SetFloatParamValue(param, value); } catch { }
        }

        static void PersistSettings()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null) return;
                cfg.PerfModeEnabled = Enabled || s_AutoEnableAfterSceneLoad;
                cfg.PerfStepIndex = StepIndex;
                int max = Math.Max(0, StepCount - 1);
                cfg.PerfBlend = max > 0 ? (float)StepIndex / (float)max : 0f;
                cfg.Save(false, false);
            }
            catch { }
        }

        static void NotifyStatus()
        {
            try
            {
                string msg = Enabled
                    ? string.Format(
                        VPBTranslation.T("gallery.perf.status_on", "Quality level {0} of {1}"),
                        StepIndex,
                        StepIndexMax())
                    : VPBTranslation.T("gallery.perf.status_off", "Quality off");
                if (Gallery.singleton == null || Gallery.singleton.Panels == null) return;
                foreach (GalleryPanel panel in Gallery.singleton.Panels)
                {
                    if (panel == null) continue;
                    panel.ShowTemporaryStatus(msg, 2f);
                    break;
                }
            }
            catch { }
        }

        static void WarnGiveMeFpsConflictOnce()
        {
            if (s_GiveMeFpsWarned) return;
            try
            {
                var inst = Settings.Instance;
                if (inst == null || inst.PerfDetectGiveMeFpsConflict == null || !inst.PerfDetectGiveMeFpsConflict.Value)
                    return;
            }
            catch { return; }

            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return;
                MVRPluginManager pm = sc.GetComponentInChildren<MVRPluginManager>();
                if (pm == null) return;
                foreach (MVRScript script in pm.GetComponentsInChildren<MVRScript>(true))
                {
                    if (script == null || script.name == null) continue;
                    if (script.name.IndexOf("GiveMeFPS", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    s_GiveMeFpsWarned = true;
                    LogUtil.LogWarning("[VPB.Perf] GiveMeFPS detected — may fight hair/mirror tuning.");
                    return;
                }
            }
            catch { }
        }
    }
}
