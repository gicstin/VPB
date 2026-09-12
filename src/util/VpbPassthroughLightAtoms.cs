using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    internal static class VpbPassthroughLightAtoms
    {
        private const int MaxLights = VpbPassthroughLights.MaxLights;
        internal const string UidPrefix = "VPB_RoomLight";

        private static readonly string[] Uids =
        {
            "VPB_RoomLight1", "VPB_RoomLight2", "VPB_RoomLight3", "VPB_RoomLight4",
        };

        private static readonly Atom[] s_atoms = new Atom[MaxLights];
        private static readonly FreeControllerV3[] s_controls = new FreeControllerV3[MaxLights];
        private static readonly JSONStorable[] s_lightStorables = new JSONStorable[MaxLights];
        private static readonly Light[] s_unityLights = new Light[MaxLights];
        private static readonly bool[] s_wasGrabbing = new bool[MaxLights];
        private static readonly Vector3[] s_groupOffsets = new Vector3[MaxLights];
        private static readonly HashSet<FreeControllerV3> s_placeControllers = new HashSet<FreeControllerV3>();

        private static Coroutine s_spawnCo;
        private static MonoBehaviour s_spawnHost;
        private static bool s_spawning;
        private static bool s_groupCaptured;
        private static bool s_loggedReady;
        private static bool s_hudGateInited;
        private static bool s_lastHudVisible;
        private static int s_spawnPasses;

        internal static bool IsSpawning { get { return s_spawning; } }

        internal static bool IsGrabbing
        {
            get
            {
                for (int i = 0; i < MaxLights; i++)
                {
                    FreeControllerV3 fc = s_controls[i];
                    if (fc == null) continue;
                    try { if (fc.isGrabbing) return true; }
                    catch { }
                }
                return false;
            }
        }

        internal static string Uid(int i)
        {
            if (i < 0 || i >= MaxLights) return "";
            return Uids[i];
        }

        internal static bool IsOwnedUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            for (int i = 0; i < MaxLights; i++)
            {
                if (string.Equals(uid, Uids[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static bool IsOwnedAtom(Atom atom)
        {
            if (atom == null) return false;
            try { return IsOwnedUid(atom.uid); }
            catch { return false; }
        }

        internal static bool IsOwnedController(FreeControllerV3 fc)
        {
            if (fc == null) return false;
            for (int i = 0; i < MaxLights; i++)
            {
                if (ReferenceEquals(s_controls[i], fc)) return true;
            }
            try
            {
                Atom atom = fc.containingAtom;
                return atom != null && IsOwnedUid(atom.uid);
            }
            catch { return false; }
        }

        internal static bool IsOwnLight(Light lt)
        {
            if (lt == null) return false;
            for (int i = 0; i < MaxLights; i++)
                if (ReferenceEquals(s_unityLights[i], lt)) return true;
            return false;
        }

        internal static void Ensure()
        {
            if (s_spawning) return;
            VPBConfig cfg = null;
            try { cfg = VPBConfig.Instance; }
            catch { }
            if (cfg == null || !cfg.PassthroughEnabled || !cfg.PassthroughLightsEnabled) return;
            try { if (!VpbPassthrough.IsVrActive()) return; }
            catch { return; }
            if (NeedsWork()) StartSpawn();
        }

        internal static void Tick()
        {
            TickHudUiGate();
            Transform rig = ResolveRig();
            if (rig == null) return;

            int grabAnchor = -1;
            for (int i = 0; i < MaxLights; i++)
            {
                if (!VpbPassthroughLights.IsSlotEnabled(i))
                {
                    if (s_wasGrabbing[i]) EndGrab(i, false);
                    continue;
                }

                FreeControllerV3 fc = s_controls[i];
                if (s_atoms[i] == null)
                {
                    if (s_wasGrabbing[i]) EndGrab(i, false);
                    s_controls[i] = null;
                    continue;
                }
                if (fc == null)
                {
                    Bind(i, s_atoms[i], true);
                    fc = s_controls[i];
                    if (fc == null) continue;
                }

                bool grabbing = false;
                try { grabbing = fc.isGrabbing; }
                catch { grabbing = false; }
                if (VpbPassthroughLights.HandlesVisible)
                {
                    bool selected = false;
                    try { selected = fc.selected; }
                    catch { selected = false; }
                    if (selected) VpbPassthroughLights.Select(i);
                }
                bool userDriven = grabbing;

                if (userDriven && !s_wasGrabbing[i]) BeginGrab(i, grabbing);
                if (userDriven)
                {
                    if (grabbing)
                    {
                        if (grabAnchor < 0) grabAnchor = i;
                    }
                    ReadWorldIntoSlot(i, fc, rig);
                }
                else
                {
                    if (s_wasGrabbing[i]) EndGrab(i, true);
                    WriteSlotToWorld(i, fc, rig);
                }
            }

            if (grabAnchor >= 0)
                ApplyGroupFollow(grabAnchor, VpbPassthroughLights.GetPosition(grabAnchor));
        }

        internal static void SyncParams()
        {
            bool on = VpbPassthroughLights.HandlesVisible;
            for (int i = 0; i < MaxLights; i++)
            {
                if (s_atoms[i] == null) continue;
                ApplyPlacementOn(i, on);
            }
            ForceOwnLightsOn();
        }

        internal static void ApplyPlacement()
        {
            bool on = VpbPassthroughLights.HandlesVisible;
            for (int i = 0; i < MaxLights; i++)
            {
                ApplyPlacementOn(i, on);
                if (!on && s_wasGrabbing[i]) EndGrab(i, true);
                if (!on)
                {
                    FreeControllerV3 fc = s_controls[i];
                    if (fc == null) continue;
                    try { fc.RestorePreLinkState(); }
                    catch { }
                    try { fc.isGrabbing = false; }
                    catch { }
                }
            }
            ForceOwnLightsOn();
        }

        internal static void WriteSlotsToWorld()
        {
            Transform rig = ResolveRig();
            if (rig == null) return;
            for (int i = 0; i < MaxLights; i++)
            {
                if (!VpbPassthroughLights.IsSlotEnabled(i)) continue;
                FreeControllerV3 fc = s_controls[i];
                if (fc == null) continue;
                WriteSlotToWorld(i, fc, rig);
            }
        }

        internal static void ApplyHandleIsolation(bool on)
        {
            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            if (sc == null) return;
            if (!on)
            {
                try { sc.SetOnlyShowControllers(null); }
                catch { }
                return;
            }
            s_placeControllers.Clear();
            for (int i = 0; i < MaxLights; i++)
            {
                FreeControllerV3 fc = s_controls[i];
                if (fc == null) continue;
                s_placeControllers.Add(fc);
            }
            try { sc.SetOnlyShowControllers(s_placeControllers); }
            catch { }
        }

        internal static void DismissNativeLightUi()
        {
            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            if (sc == null) return;
            try { sc.ClearSelection(); }
            catch { }
            try
            {
                if (sc.activeUI == SuperController.ActiveUI.SelectedOptions)
                    sc.activeUI = SuperController.ActiveUI.None;
            }
            catch { }
        }

        internal static void DismissNativeLightUiOnHudHide()
        {
            if (VpbPassthroughLights.HandlesVisible || IsOwnedSelection())
                DismissNativeLightUi();
        }

        internal static bool IsMainHudVisible()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null || sc.mainHUD == null) return true;
                GameObject go = sc.mainHUD.gameObject;
                return go != null && go.activeInHierarchy;
            }
            catch { return true; }
        }

        private static bool IsOwnedSelection()
        {
            for (int i = 0; i < MaxLights; i++)
            {
                FreeControllerV3 fc = s_controls[i];
                if (fc == null) continue;
                try { if (fc.selected) return true; }
                catch { }
            }
            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            if (sc == null) return false;
            try
            {
                if (IsOwnedController(sc.GetSelectedController())) return true;
            }
            catch { }
            try
            {
                if (IsOwnedAtom(sc.GetSelectedAtom())) return true;
            }
            catch { }
            return false;
        }

        private static void TickHudUiGate()
        {
            bool hud = IsMainHudVisible();
            if (!s_hudGateInited)
            {
                s_hudGateInited = true;
                s_lastHudVisible = hud;
                if (!hud) DismissNativeLightUiOnHudHide();
                return;
            }
            if (hud == s_lastHudVisible) return;
            s_lastHudVisible = hud;
            if (!hud) DismissNativeLightUiOnHudHide();
            if (VpbPassthroughLights.HandlesVisible)
                ApplyPlacement();
        }

        internal static void ApplySlotsToLive()
        {
            Transform rig = ResolveRig();
            bool on = VpbPassthroughLights.HandlesVisible;
            for (int i = 0; i < MaxLights; i++)
            {
                if (s_atoms[i] == null) continue;
                ApplyLightParams(i);
                ApplyPlacementOn(i, on);
                FreeControllerV3 fc = s_controls[i];
                if (fc != null && rig != null) WriteSlotToWorld(i, fc, rig);
            }
        }

        internal static void DestroyAll()
        {
            StopSpawn();
            try { ApplyHandleIsolation(false); }
            catch { }
            SuperController sc = null;
            try { sc = SuperController.singleton; }
            catch { }
            for (int i = 0; i < MaxLights; i++)
                Drop(i, sc, true);
            s_loggedReady = false;
            s_groupCaptured = false;
            s_hudGateInited = false;
            s_spawnPasses = 0;
        }

        internal static void OnSceneLoadComplete()
        {
            StopSpawn();
            for (int i = 0; i < MaxLights; i++)
                Drop(i, null, false);
            s_loggedReady = false;
            s_groupCaptured = false;
            s_hudGateInited = false;
            s_spawnPasses = 0;
        }

        private static bool NeedsWork()
        {
            for (int i = 0; i < MaxLights; i++)
            {
                bool want = VpbPassthroughLights.IsSlotEnabled(i);
                bool have = s_atoms[i] != null;
                if (want != have) return true;
                if (want && s_controls[i] == null) return true;
            }
            return false;
        }

        private static void StartSpawn()
        {
            if (s_spawning) return;
            MonoBehaviour host = SpawnHost();
            if (host == null) return;
            try
            {
                s_spawnHost = host;
                s_spawnPasses++;
                s_spawnCo = host.StartCoroutine(SpawnRoutine());
            }
            catch
            {
                s_spawnCo = null;
                s_spawnHost = null;
                s_spawnPasses = 0;
            }
        }

        private static void StopSpawn()
        {
            if (s_spawnCo != null && s_spawnHost != null)
            {
                try { s_spawnHost.StopCoroutine(s_spawnCo); }
                catch { }
            }
            s_spawnCo = null;
            s_spawnHost = null;
            s_spawning = false;
            s_spawnPasses = 0;
        }

        private static MonoBehaviour SpawnHost()
        {
            try
            {
                if (VamHookPlugin.singleton != null) return VamHookPlugin.singleton;
            }
            catch { }
            try { return SuperController.singleton; }
            catch { return null; }
        }

        private static IEnumerator SpawnRoutine()
        {
            s_spawning = true;
            int spawned = 0;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) yield break;

                for (int i = 0; i < MaxLights; i++)
                {
                    if (!VpbPassthroughLights.IsSlotEnabled(i)) continue;
                    if (s_atoms[i] != null) continue;
                    yield return SpawnOne(sc, i);
                    if (s_atoms[i] != null) spawned++;
                }

                for (int i = 0; i < MaxLights; i++)
                {
                    if (VpbPassthroughLights.IsSlotEnabled(i)) continue;
                    if (s_atoms[i] == null) continue;
                    Drop(i, sc, true);
                }
            }
            finally
            {
                s_spawning = false;
                s_spawnCo = null;
                s_spawnHost = null;
                if (spawned > 0 && !s_loggedReady)
                {
                    s_loggedReady = true;
                    LogUtil.Log("[VPB] Real-world lights ready count=" + VpbPassthroughLights.ActiveLightCount);
                }
                ForceOwnLightsOn();
                if (VpbPassthroughLights.HandlesVisible)
                {
                    ApplyHandleIsolation(true);
                    WriteSlotsToWorld();
                }
                if (NeedsWork() && s_spawnPasses < MaxLights)
                    StartSpawn();
                else
                    s_spawnPasses = 0;
            }
        }

        private static IEnumerator SpawnOne(SuperController sc, int i)
        {
            string uid = Uids[i];
            Atom atom = null;
            try { atom = sc.GetAtomByUid(uid); }
            catch { atom = null; }
            if (atom != null)
            {
                HoldAtom(atom);
                Bind(i, atom, true);
                yield break;
            }

            IEnumerator addIe = null;
            string spawnError = null;
            try { addIe = sc.AddAtomByType("InvisibleLight", uid, false, false, false); }
            catch (Exception ex)
            {
                spawnError = ex.Message;
            }
            if (spawnError != null)
            {
                LogUtil.LogWarning("[VPB] Real-world light spawn failed for " + uid + ": " + spawnError);
                yield break;
            }

            while (addIe != null && addIe.MoveNext())
                yield return addIe.Current;

            try { atom = sc.GetAtomByUid(uid); }
            catch { atom = null; }
            if (atom == null)
            {
                LogUtil.LogWarning("[VPB] Real-world light spawn: InvisibleLight '" + uid + "' missing after AddAtomByType.");
                yield break;
            }

            HoldAtom(atom);
            Bind(i, atom, false);
        }

        private static void Bind(int i, Atom atom, bool applyAppearance)
        {
            if (atom == null) return;
            s_atoms[i] = atom;

            DetachStolenBindings(i, atom);
            HoldAtom(atom);
            try { atom.SetOn(true); }
            catch { }
            try { atom.collisionEnabled = false; }
            catch { }
            try { atom.description = VpbPassthroughLights.SlotName(i); }
            catch { }

            FreeControllerV3 fc = null;
            try { fc = atom.mainController; }
            catch { fc = null; }
            s_controls[i] = fc;
            if (fc != null)
            {
                try { fc.interactableInPlayMode = true; }
                catch { }
                try { fc.canGrabPosition = true; }
                catch { }
                try { fc.canGrabRotation = false; }
                catch { }
                try { fc.possessable = false; }
                catch { }
                try { fc.currentPositionState = FreeControllerV3.PositionState.On; }
                catch { }
                try { fc.currentRotationState = FreeControllerV3.RotationState.Off; }
                catch { }
                try { fc.drawMeshWhenDeselected = true; }
                catch { }
            }

            JSONStorable light = null;
            try { light = atom.GetStorableByID("Light"); }
            catch { light = null; }
            s_lightStorables[i] = light;
            CacheUnityLight(i, atom);
            ApplyLightParams(i, applyAppearance);
            if (!applyAppearance)
            {
                try { CaptureAppearance(i); }
                catch { }
            }
            ApplyPlacementOn(i, VpbPassthroughLights.HandlesVisible);

            Transform rig = ResolveRig();
            if (fc != null && rig != null) WriteSlotToWorld(i, fc, rig);
        }

        private static void CacheUnityLight(int i, Atom atom)
        {
            s_unityLights[i] = null;
            if (atom == null) return;
            try
            {
                Light[] found = atom.GetComponentsInChildren<Light>(true);
                if (found == null || found.Length == 0) return;
                Light best = found[0];
                float bestRange = -1f;
                for (int k = 0; k < found.Length; k++)
                {
                    Light lt = found[k];
                    if (lt == null) continue;
                    float r = 0f;
                    try { r = lt.range; }
                    catch { continue; }
                    if (r > bestRange)
                    {
                        bestRange = r;
                        best = lt;
                    }
                }
                s_unityLights[i] = best;
            }
            catch { }
        }

        private static void ApplyLightParams(int i)
        {
            ApplyLightParams(i, true);
        }

        private static void ApplyLightParams(int i, bool applyLook)
        {
            JSONStorable light = s_lightStorables[i];
            if (light == null)
            {
                Atom atom = s_atoms[i];
                if (atom == null) return;
                try { light = atom.GetStorableByID("Light"); }
                catch { return; }
                s_lightStorables[i] = light;
                if (light == null) return;
            }

            VpbPassthroughLights.LightSlot slot = VpbPassthroughLights.GetSlot(i);
            WriteBool(light, "on", slot.Enabled);
            if (!applyLook)
            {
                WriteChooser(light, "type", "Point");
                WriteChooser(light, "shadowResolution", VpbPassthroughLights.ShadowResolutionForCurrentQuality());
                return;
            }

            WriteFloat(light, "intensity", slot.Intensity);
            WriteFloat(light, "range", slot.Range);
            try
            {
                var color = light.GetColorJSONParam("color");
                if (color != null)
                {
                    HSVColor hsv = new HSVColor();
                    hsv.H = slot.ColorH;
                    hsv.S = slot.ColorS;
                    hsv.V = slot.ColorV;
                    color.val = hsv;
                }
            }
            catch { }
        }

        internal static void CaptureAppearance()
        {
            for (int i = 0; i < MaxLights; i++)
                CaptureAppearance(i);
        }

        private static void CaptureAppearance(int i)
        {
            if (i < 0 || i >= MaxLights) return;
            if (s_atoms[i] == null) return;
            JSONStorable light = s_lightStorables[i];
            if (light == null)
            {
                try { light = s_atoms[i].GetStorableByID("Light"); }
                catch { return; }
                s_lightStorables[i] = light;
            }
            if (light == null) return;

            VpbPassthroughLights.LightSlot slot = VpbPassthroughLights.GetSlot(i);
            float intensity = slot.Intensity;
            float range = slot.Range;
            float h = slot.ColorH;
            float s = slot.ColorS;
            float v = slot.ColorV;
            try
            {
                var f = light.GetFloatJSONParam("intensity");
                if (f != null) intensity = f.val;
            }
            catch { }
            try
            {
                var f = light.GetFloatJSONParam("range");
                if (f != null) range = f.val;
            }
            catch { }
            try
            {
                var color = light.GetColorJSONParam("color");
                if (color != null)
                {
                    HSVColor hsv = color.val;
                    h = hsv.H;
                    s = hsv.S;
                    v = hsv.V;
                }
            }
            catch { }
            VpbPassthroughLights.SetAppearance(i, intensity, h, s, v, range);
        }

        private static void ApplyPlacementOn(int i, bool on)
        {
            Atom atom = s_atoms[i];
            if (atom == null) return;
            try { atom.hidden = !on; }
            catch { }
            FreeControllerV3 fc = s_controls[i];
            if (fc == null) return;
            try { fc.hidden = !on; }
            catch { }
            bool allowUi = on && IsMainHudVisible();
            try { fc.guihidden = !allowUi; }
            catch { }
            try { fc.enableSelectRoot = allowUi; }
            catch { }
            try { fc.interactableInPlayMode = on; }
            catch { }
            try { fc.drawMeshWhenDeselected = on; }
            catch { }
        }

        internal static void ForceOwnLightsOn()
        {
            for (int i = 0; i < MaxLights; i++)
            {
                if (!VpbPassthroughLights.IsSlotEnabled(i)) continue;
                Atom atom = s_atoms[i];
                if (atom == null) continue;
                try { atom.SetOn(true); }
                catch { }
                Light lt = s_unityLights[i];
                if (lt != null)
                {
                    try { lt.enabled = true; }
                    catch { }
                }
                WriteBool(s_lightStorables[i], "on", true);
            }
        }

        private static void HoldAtom(Atom atom)
        {
            if (atom == null) return;
            try { atom.isPoolable = false; }
            catch { }
        }

        private static void DetachStolenBindings(int i, Atom atom)
        {
            if (atom == null) return;
            for (int j = 0; j < MaxLights; j++)
            {
                if (j == i) continue;
                if (!ReferenceEquals(s_atoms[j], atom)) continue;
                s_atoms[j] = null;
                s_controls[j] = null;
                s_lightStorables[j] = null;
                s_unityLights[j] = null;
                s_wasGrabbing[j] = false;
                LogUtil.LogWarning("[VPB] " + VpbPassthroughLights.SlotName(j) + " lost its shell to " + VpbPassthroughLights.SlotName(i) + "; respawning.");
            }
        }

        private static void BeginGrab(int i, bool physicalGrab)
        {
            s_wasGrabbing[i] = true;
            VpbPassthroughLights.Select(i);
            if (physicalGrab) CaptureGroupOffsets(i);
            if (VPBLogger.Verbose)
                VPBLogger.Main.LogInfo("[VPB] Real-world light grab " + VpbPassthroughLights.SlotName(i), false);
        }

        private static void EndGrab(int i, bool persist)
        {
            s_wasGrabbing[i] = false;
            s_groupCaptured = false;
            if (!persist) return;
            VpbPassthroughLights.PersistNow();
            VpbPassthroughLights.RequestUiRefresh();
            if (VPBLogger.Verbose)
                VPBLogger.Main.LogInfo("[VPB] Real-world light release " + VpbPassthroughLights.SlotName(i), false);
        }

        private static void ReadWorldIntoSlot(int i, FreeControllerV3 fc, Transform rig)
        {
            Vector3 world;
            try { world = fc.transform.position; }
            catch { return; }
            Vector3 local;
            try { local = rig.InverseTransformPoint(world); }
            catch { return; }
            VpbPassthroughLights.SetPosition(i, local);
        }

        private static void WriteSlotToWorld(int i, FreeControllerV3 fc, Transform rig)
        {
            Vector3 local = VpbPassthroughLights.GetPosition(i);
            Vector3 world;
            try { world = rig.TransformPoint(local); }
            catch { return; }
            try { fc.transform.position = world; }
            catch { }
        }

        private static void CaptureGroupOffsets(int anchor)
        {
            s_groupCaptured = false;
            try
            {
                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null || !cfg.PassthroughLightsMoveAsGroup) return;
            }
            catch { return; }

            Vector3 anchorPos = VpbPassthroughLights.GetPosition(anchor);
            for (int i = 0; i < MaxLights; i++)
                s_groupOffsets[i] = VpbPassthroughLights.GetPosition(i) - anchorPos;
            s_groupCaptured = true;
        }

        private static void ApplyGroupFollow(int anchor, Vector3 anchorPos)
        {
            if (!s_groupCaptured) return;
            Transform rig = ResolveRig();
            for (int i = 0; i < MaxLights; i++)
            {
                if (i == anchor) continue;
                if (!VpbPassthroughLights.IsSlotEnabled(i)) continue;
                VpbPassthroughLights.SetPosition(i, anchorPos + s_groupOffsets[i]);
                FreeControllerV3 fc = s_controls[i];
                if (fc != null && rig != null) WriteSlotToWorld(i, fc, rig);
            }
        }

        private static void Drop(int i, SuperController sc, bool remove)
        {
            s_wasGrabbing[i] = false;
            Atom atom = s_atoms[i];
            s_atoms[i] = null;
            s_controls[i] = null;
            s_lightStorables[i] = null;
            s_unityLights[i] = null;
            if (!remove || atom == null || sc == null) return;
            try { atom.isPoolable = true; }
            catch { }
            try { sc.RemoveAtom(atom); }
            catch { }
        }

        private static Transform ResolveRig()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                return sc != null ? sc.navigationRig : null;
            }
            catch { return null; }
        }

        private static void WriteFloat(JSONStorable storable, string param, float value)
        {
            if (storable == null) return;
            try
            {
                var f = storable.GetFloatJSONParam(param);
                if (f != null) f.val = value;
            }
            catch { }
            try { storable.SetFloatParamValue(param, value); }
            catch { }
        }

        private static void WriteBool(JSONStorable storable, string param, bool value)
        {
            if (storable == null) return;
            try
            {
                var b = storable.GetBoolJSONParam(param);
                if (b != null) b.val = value;
            }
            catch { }
        }

        private static void WriteChooser(JSONStorable storable, string param, string value)
        {
            if (storable == null || string.IsNullOrEmpty(value)) return;
            try
            {
                var cho = storable.GetStringChooserJSONParam(param);
                if (cho != null) cho.val = value;
            }
            catch { }
        }
    }
}
