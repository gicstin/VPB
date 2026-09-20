using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VPB.src.util;

namespace VPB.Outliner
{
    internal enum OutlinerTargetMode
    {
        Off = 0,
        Selected = 1,
        All = 2
    }

    internal static class OutlinerTargets
    {
        static FieldInfo s_targetsOnField;
        static bool s_probed;

        static bool s_captured;
        static bool s_baseTargetsOn;
        static bool s_baseMenuOnly;
        static bool s_forcing;
        static bool s_isolated;
        static bool s_lastKnownTargetsOn;

        static readonly HashSet<FreeControllerV3> s_isolate = new HashSet<FreeControllerV3>();
        static readonly List<FreeControllerV3> s_scratch = new List<FreeControllerV3>(64);

        internal static bool Forcing { get { return s_forcing; } }
        internal static bool Isolated { get { return s_isolated; } }
        internal static int IsolatedCount { get { return s_isolate.Count; } }

        internal static OutlinerTargetMode ModeFromConfig()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return OutlinerTargetMode.Selected;
            return Normalize(cfg.OutlinerTargetsMode);
        }

        internal static OutlinerTargetMode Normalize(int v)
        {
            if (v <= 0) return OutlinerTargetMode.Off;
            if (v >= 2) return OutlinerTargetMode.All;
            return OutlinerTargetMode.Selected;
        }

        internal static OutlinerTargetMode Next(OutlinerTargetMode m)
        {
            if (m == OutlinerTargetMode.Off) return OutlinerTargetMode.Selected;
            if (m == OutlinerTargetMode.Selected) return OutlinerTargetMode.All;
            return OutlinerTargetMode.Off;
        }

        internal static bool ReadVamTargetsOn()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            if (!s_probed)
            {
                s_probed = true;
                try
                {
                    s_targetsOnField = typeof(SuperController).GetField(
                        "targetsOnWithButton", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                catch { s_targetsOnField = null; }
            }
            if (s_targetsOnField != null)
            {
                try
                {
                    object boxed = s_targetsOnField.GetValue(sc);
                    if (boxed is bool)
                    {
                        s_lastKnownTargetsOn = (bool)boxed;
                        return s_lastKnownTargetsOn;
                    }
                }
                catch { }
            }
            try { s_lastKnownTargetsOn = sc.GetTargetShow(); }
            catch { }
            return s_lastKnownTargetsOn;
        }

        static void SetVamTargetsOn(bool on)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            if (ReadVamTargetsOn() == on) return;
            try
            {
                sc.ToggleTargetsOnWithButton();
                s_lastKnownTargetsOn = on;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] target toggle failed: " + ex.Message);
            }
        }

        static void CaptureBaseline()
        {
            if (s_captured) return;
            s_baseTargetsOn = ReadVamTargetsOn();
            s_baseMenuOnly = true;
            try
            {
                if (UserPreferences.singleton != null)
                    s_baseMenuOnly = UserPreferences.singleton.showTargetsMenuOnly;
            }
            catch { }
            s_captured = true;
        }

        internal static void Apply(OutlinerTargetMode mode, List<string> uids)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            if (mode == OutlinerTargetMode.Off)
            {
                Release();
                return;
            }
            CaptureBaseline();
            s_forcing = true;
            try
            {
                if (UserPreferences.singleton != null)
                    UserPreferences.singleton.showTargetsMenuOnly = false;
            }
            catch { }
            SetVamTargetsOn(true);
            if (mode == OutlinerTargetMode.Selected && CollectControllers(uids))
                ApplyIsolation();
            else
                ClearIsolation();
        }

        static bool CollectControllers(List<string> uids)
        {
            s_scratch.Clear();
            if (uids == null || uids.Count == 0) return false;
            bool rootOnly = VPBConfig.Instance != null && VPBConfig.Instance.OutlinerTargetsRootOnly;
            for (int i = 0; i < uids.Count; i++)
            {
                Atom atom = OutlinerEdits.GetAtom(uids[i]);
                if (atom == null) continue;
                if (rootOnly)
                {
                    FreeControllerV3 rootCtrl = OutlinerEdits.GetRootController(atom);
                    if (rootCtrl != null) s_scratch.Add(rootCtrl);
                    continue;
                }
                FreeControllerV3[] all = null;
                try { all = atom.freeControllers; } catch { all = null; }
                if (all == null)
                {
                    FreeControllerV3 fallback = OutlinerEdits.GetRootController(atom);
                    if (fallback != null) s_scratch.Add(fallback);
                    continue;
                }
                for (int c = 0; c < all.Length; c++)
                {
                    if (all[c] != null) s_scratch.Add(all[c]);
                }
            }
            return s_scratch.Count > 0;
        }

        static void ApplyIsolation()
        {
            s_isolate.Clear();
            for (int i = 0; i < s_scratch.Count; i++)
                s_isolate.Add(s_scratch[i]);
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) sc.SetOnlyShowControllers(s_isolate);
                s_isolated = true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] target isolate failed: " + ex.Message);
            }
        }

        internal static void ClearIsolation()
        {
            if (!s_isolated) return;
            s_isolated = false;
            s_isolate.Clear();
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) sc.SetOnlyShowControllers(null);
            }
            catch { }
        }

        internal static void Release()
        {
            ClearIsolation();
            if (!s_captured)
            {
                s_forcing = false;
                return;
            }
            s_captured = false;
            s_forcing = false;
            SetVamTargetsOn(s_baseTargetsOn);
            RestoreMenuOnlyPreference();
        }

        internal static void AbandonToVam()
        {
            ClearIsolation();
            s_captured = false;
            s_forcing = false;
            RestoreMenuOnlyPreference();
        }

        static void RestoreMenuOnlyPreference()
        {
            try
            {
                if (UserPreferences.singleton != null)
                    UserPreferences.singleton.showTargetsMenuOnly = s_baseMenuOnly;
            }
            catch { }
        }

        internal static void OnSceneLoading()
        {
            ClearIsolation();
        }

        internal static bool ShowHiddenAtoms()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                return sc != null && sc.showHiddenAtoms;
            }
            catch { return false; }
        }

        internal static void SetShowHiddenAtoms(bool on)
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) sc.showHiddenAtoms = on;
            }
            catch { }
        }

        internal static bool HideInactiveTargets()
        {
            try
            {
                return UserPreferences.singleton != null && UserPreferences.singleton.hideInactiveTargets;
            }
            catch { return false; }
        }

        internal static void SetHideInactiveTargets(bool on)
        {
            try
            {
                if (UserPreferences.singleton != null)
                    UserPreferences.singleton.hideInactiveTargets = on;
            }
            catch { }
        }
    }
}
