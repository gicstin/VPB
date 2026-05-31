using System;
using UnityEngine;

namespace VPB
{
    // Toggle: Settings.LogPerfDiagnostics (BepInEx config "Logging.LogPerfDiagnostics").
    // Counter sites check the cached bool, so when off the cost is one branch per site.
    // Cache is refreshed once per VamHookPlugin.Update tick.
    static class VpbPerfDiag
    {
        public static bool CachedEnabled;

        public static long QmRefresh;
        public static long QmIconCreate;
        public static long QmIconSwap;
        public static long PointerSib;
        public static long GalUpdateFull;
        public static long SetCanvasVisibleOn;
        public static long SetCanvasVisibleOff;
        public static long MenuGateFlip;
        public static long UserTagBind;
        public static long UserTagVirtVis;
        public static long UserTagScrollCb;
        public static long UserTagPinnedRebuild;
        public static long TooltipAttach;
        // File-hook activity, for attributing a stalled frame to the on-demand path vs VaM itself.
        // fxHook = FileExists postfix calls, fxHeavy = those that ran the on-demand resolve, getVar =
        // GetVarFileEntry postfix calls, scriptCtrl = plugin creates.
        public static long FileExistsHook;
        public static long FileExistsHookHeavy;
        public static long GetVarEntryHook;
        public static long ScriptCtrlCreate;

        static long _lastQmRefresh, _lastQmIconCreate, _lastQmIconSwap, _lastPointerSib;
        static long _lastGalUpdateFull;
        static long _lastSetCanvasVisibleOn, _lastSetCanvasVisibleOff, _lastMenuGateFlip;
        static long _lastUserTagBind, _lastUserTagVirtVis, _lastUserTagScrollCb, _lastUserTagPinnedRebuild, _lastTooltipAttach;
        static long _lastFileExistsHook, _lastFileExistsHookHeavy, _lastGetVarEntryHook, _lastScriptCtrlCreate;
        static float _lastEmitRealtime;
        static float _nextEmitRealtime;

        const float IntervalSeconds = 1.0f;

        public static void RefreshCache()
        {
            try
            {
                var inst = Settings.Instance;
                CachedEnabled = inst != null && inst.LogPerfDiagnostics != null && inst.LogPerfDiagnostics.Value;
            }
            catch { CachedEnabled = false; }
        }

        public static void LogTransition(string what, string detail)
        {
            if (!CachedEnabled) return;
            try
            {
                string msg = string.IsNullOrEmpty(detail)
                    ? ("[VPB.Diag.Transition] " + what)
                    : ("[VPB.Diag.Transition] " + what + " | " + detail);
                LogUtil.Log(msg);
            }
            catch { }
        }

        public static void EmitFrameSummaryIfDue()
        {
            try
            {
                if (!CachedEnabled)
                {
                    // Keep baseline current while disabled so the first enabled tick doesn't dump a giant catch-up delta.
                    _lastQmRefresh = QmRefresh;
                    _lastQmIconCreate = QmIconCreate;
                    _lastQmIconSwap = QmIconSwap;
                    _lastPointerSib = PointerSib;
                    _lastGalUpdateFull = GalUpdateFull;
                    _lastSetCanvasVisibleOn = SetCanvasVisibleOn;
                    _lastSetCanvasVisibleOff = SetCanvasVisibleOff;
                    _lastMenuGateFlip = MenuGateFlip;
                    _lastUserTagBind = UserTagBind;
                    _lastUserTagVirtVis = UserTagVirtVis;
                    _lastUserTagScrollCb = UserTagScrollCb;
                    _lastUserTagPinnedRebuild = UserTagPinnedRebuild;
                    _lastTooltipAttach = TooltipAttach;
                    _lastFileExistsHook = FileExistsHook;
                    _lastFileExistsHookHeavy = FileExistsHookHeavy;
                    _lastGetVarEntryHook = GetVarEntryHook;
                    _lastScriptCtrlCreate = ScriptCtrlCreate;
                    _nextEmitRealtime = 0f;
                    return;
                }

                float now = Time.realtimeSinceStartup;
                if (_nextEmitRealtime <= 0f)
                {
                    _nextEmitRealtime = now + IntervalSeconds;
                    _lastEmitRealtime = now;
                    return;
                }
                if (now < _nextEmitRealtime) return;

                float dt = now - _lastEmitRealtime;
                if (dt < 0.01f) dt = 0.01f;
                _lastEmitRealtime = now;
                _nextEmitRealtime = now + IntervalSeconds;

                long qmRefresh = QmRefresh - _lastQmRefresh;
                long qmIcon = QmIconCreate - _lastQmIconCreate;
                long qmSwap = QmIconSwap - _lastQmIconSwap;
                long pointerSib = PointerSib - _lastPointerSib;
                long galFull = GalUpdateFull - _lastGalUpdateFull;
                long setOn = SetCanvasVisibleOn - _lastSetCanvasVisibleOn;
                long setOff = SetCanvasVisibleOff - _lastSetCanvasVisibleOff;
                long gateFlip = MenuGateFlip - _lastMenuGateFlip;
                long utBind = UserTagBind - _lastUserTagBind;
                long utVirtVis = UserTagVirtVis - _lastUserTagVirtVis;
                long utScrollCb = UserTagScrollCb - _lastUserTagScrollCb;
                long utPinnedRebuild = UserTagPinnedRebuild - _lastUserTagPinnedRebuild;
                long tooltipAttach = TooltipAttach - _lastTooltipAttach;
                long fxHook = FileExistsHook - _lastFileExistsHook;
                long fxHeavy = FileExistsHookHeavy - _lastFileExistsHookHeavy;
                long getVar = GetVarEntryHook - _lastGetVarEntryHook;
                long scriptCtrl = ScriptCtrlCreate - _lastScriptCtrlCreate;

                _lastQmRefresh = QmRefresh;
                _lastQmIconCreate = QmIconCreate;
                _lastQmIconSwap = QmIconSwap;
                _lastPointerSib = PointerSib;
                _lastGalUpdateFull = GalUpdateFull;
                _lastSetCanvasVisibleOn = SetCanvasVisibleOn;
                _lastSetCanvasVisibleOff = SetCanvasVisibleOff;
                _lastMenuGateFlip = MenuGateFlip;
                _lastUserTagBind = UserTagBind;
                _lastUserTagVirtVis = UserTagVirtVis;
                _lastUserTagScrollCb = UserTagScrollCb;
                _lastUserTagPinnedRebuild = UserTagPinnedRebuild;
                _lastTooltipAttach = TooltipAttach;
                _lastFileExistsHook = FileExistsHook;
                _lastFileExistsHookHeavy = FileExistsHookHeavy;
                _lastGetVarEntryHook = GetVarEntryHook;
                _lastScriptCtrlCreate = ScriptCtrlCreate;

                // Snapshot panel state. `gallSubtreeActive` counts panels whose UI subtree is currently
                // active (Phase 3); the diff vs gallVis surfaces transition windows where canvas just
                // toggled but the SetActive call hasn't propagated yet.
                int panels = 0, vis = 0, hid = 0, subtree = 0;
                try
                {
                    var g = Gallery.singleton;
                    if (g != null && g.Panels != null)
                    {
                        panels = g.Panels.Count;
                        for (int i = 0; i < g.Panels.Count; i++)
                        {
                            var p = g.Panels[i];
                            if (p == null) continue;
                            if (p.IsVisible) vis++;
                            else hid++;
                            if (p.IsSubtreeActive) subtree++;
                        }
                    }
                }
                catch { }

                float fps = VpbFrameRate.Current;
                float loopFps = 0f;
                try
                {
                    float sdt = Time.smoothDeltaTime;
                    if (sdt > 0.00001f) loopFps = 1f / sdt;
                }
                catch { }

                string msg = string.Format(
                    "[VPB.Diag] fps={0:0.0} loopFps={19:0.0} dt={1:0.00}s | panels={2} gallVis={3} gallHid={4} gallSubtreeActive={5}" +
                    " | qmRefresh={6}/s qmIconCreate={7}/s qmIconSwap={8}/s" +
                    " | galUpd={9}/s setVisOn={10}/s setVisOff={11}/s gateFlip={12}/s" +
                    " | pointerSib={13}/s" +
                    " | utBind={14}/s utVirtVis={15}/s utScrollCb={16}/s utPinnedRebuild={17}/s tooltipAttach={18}/s" +
                    " | fxHook={20} fxHeavy={21} getVar={22} scriptCtrl={23} (raw/interval)",
                    fps, dt, panels, vis, hid, subtree,
                    (long)(qmRefresh / dt), (long)(qmIcon / dt), (long)(qmSwap / dt),
                    (long)(galFull / dt),
                    (long)(setOn / dt), (long)(setOff / dt), (long)(gateFlip / dt),
                    (long)(pointerSib / dt),
                    (long)(utBind / dt), (long)(utVirtVis / dt), (long)(utScrollCb / dt),
                    (long)(utPinnedRebuild / dt), (long)(tooltipAttach / dt),
                    loopFps,
                    fxHook, fxHeavy, getVar, scriptCtrl);
                LogUtil.LogWarning(msg);
            }
            catch { }
        }
    }
}
