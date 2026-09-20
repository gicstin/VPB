using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        GameObject _outlinerTargetsBtn;
        float _outlinerTargetsPulseUntil;
        bool _outlinerTargetsPulsing;
        int _outlinerTargetsAppliedMode = -1;
        string _outlinerTargetsAppliedPrimary = "";
        int _outlinerTargetsAppliedCount = -1;
        bool _outlinerTargetsExpectOn;
        bool _outlinerTargetsSceneWasLoading;
        readonly List<string> _outlinerTargetsUids = new List<string>(8);

        OutlinerTargetMode OutlinerTargetModeSetting()
        {
            return OutlinerTargets.ModeFromConfig();
        }

        OutlinerTargetMode OutlinerEffectiveTargetMode()
        {
            OutlinerTargetMode m = OutlinerTargetModeSetting();
            if (_outlinerTargetsPulsing && m == OutlinerTargetMode.Off)
                return OutlinerTargetMode.Selected;
            return m;
        }

        void CycleOutlinerTargetMode()
        {
            SetOutlinerTargetMode(OutlinerTargets.Next(OutlinerTargetModeSetting()), true);
        }

        void SetOutlinerTargetMode(OutlinerTargetMode mode, bool announce)
        {
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.OutlinerTargetsMode = (int)mode;
                VPBConfig.Instance.Save();
            }
            _outlinerTargetsPulsing = false;
            _outlinerTargetsPulseUntil = 0f;
            if (mode != OutlinerTargetMode.Off) OutlinerEdits.EnsureEditMode();
            ApplyOutlinerTargets(true);
            _outlinerLastFocused = true;
            if (announce)
                ShowTemporaryStatus(OutlinerTargetModeSentence(mode), 2f);
        }

        static string OutlinerTargetModeLabel(OutlinerTargetMode mode)
        {
            if (mode == OutlinerTargetMode.Off)
                return VPBTranslation.T("outliner.targets.off", "off");
            if (mode == OutlinerTargetMode.All)
                return VPBTranslation.T("outliner.targets.all", "all atoms");
            return VPBTranslation.T("outliner.targets.selected", "selection only");
        }

        static string OutlinerTargetModeSentence(OutlinerTargetMode mode)
        {
            if (mode == OutlinerTargetMode.Off)
                return VPBTranslation.T("outliner.targets.msg_off",
                    "Targets off — VaM's own target state restored.");
            if (mode == OutlinerTargetMode.All)
                return VPBTranslation.T("outliner.targets.msg_all",
                    "Targets on for every atom.");
            return VPBTranslation.T("outliner.targets.msg_selected",
                "Targets on for the selected atom only — everything else stays clear.");
        }

        void PulseOutlinerTargets()
        {
            float secs = 3f;
            if (VPBConfig.Instance != null)
                secs = VPBConfig.ClampOutlinerTargetsPulse(VPBConfig.Instance.OutlinerTargetsPulseSeconds);
            if (secs <= 0f) return;
            if (!OutlinerAutoTargetsEnabled()) return;
            _outlinerTargetsPulseUntil = Time.realtimeSinceStartup + secs;
            if (!_outlinerTargetsPulsing)
            {
                _outlinerTargetsPulsing = true;
                ApplyOutlinerTargets(true);
            }
        }

        bool OutlinerAutoTargetsEnabled()
        {
            return VPBConfig.Instance == null || VPBConfig.Instance.OutlinerAutoTargets;
        }

        void NotifyOutlinerSelectionChanged(bool fromPane)
        {
            if (fromPane && OutlinerAutoTargetsEnabled()
                && OutlinerTargetModeSetting() != OutlinerTargetMode.Off)
                OutlinerEdits.EnsureEditMode();
            ApplyOutlinerTargets(false);
        }

        void ApplyOutlinerTargets(bool force)
        {
            if (!IsSceneOutlinerOpen())
            {
                ReleaseOutlinerTargets();
                return;
            }
            OutlinerTargetMode mode = OutlinerEffectiveTargetMode();
            string primary = _outlinerSelection.PrimaryUid ?? "";
            int count = _outlinerSelection.Count;
            if (!force
                && (int)mode == _outlinerTargetsAppliedMode
                && count == _outlinerTargetsAppliedCount
                && string.Equals(primary, _outlinerTargetsAppliedPrimary, StringComparison.Ordinal))
                return;
            _outlinerTargetsAppliedMode = (int)mode;
            _outlinerTargetsAppliedCount = count;
            _outlinerTargetsAppliedPrimary = primary;
            _outlinerSelection.CopyUids(_outlinerTargetsUids);
            OutlinerTargets.Apply(mode, _outlinerTargetsUids);
            _outlinerTargetsExpectOn = mode != OutlinerTargetMode.Off;
            SyncOutlinerTargetsButton();
            RefreshOutlinerPlayBanner();
        }

        void ReleaseOutlinerTargets()
        {
            _outlinerTargetsPulsing = false;
            _outlinerTargetsPulseUntil = 0f;
            _outlinerTargetsAppliedMode = -1;
            _outlinerTargetsAppliedCount = -1;
            _outlinerTargetsAppliedPrimary = "";
            _outlinerTargetsExpectOn = false;
            OutlinerTargets.Release();
            SyncOutlinerTargetsButton();
        }

        void TickOutlinerTargets()
        {
            bool loading = false;
            try
            {
                SuperController sc = SuperController.singleton;
                loading = sc != null && sc.isLoading;
            }
            catch { }
            if (loading != _outlinerTargetsSceneWasLoading)
            {
                _outlinerTargetsSceneWasLoading = loading;
                if (loading)
                {
                    OutlinerTargets.OnSceneLoading();
                    _outlinerTargetsAppliedMode = -1;
                }
                else
                {
                    _outlinerTargetsAppliedMode = -1;
                    ApplyOutlinerTargets(true);
                }
                return;
            }
            if (_outlinerTargetsPulsing && _outlinerSpringHeldCount > 0)
            {
                ApplyOutlinerTargets(false);
                return;
            }
            if (_outlinerTargetsPulsing && Time.realtimeSinceStartup >= _outlinerTargetsPulseUntil)
            {
                _outlinerTargetsPulsing = false;
                ApplyOutlinerTargets(true);
                return;
            }
            ApplyOutlinerTargets(false);
        }

        void PollOutlinerTargetsFromVam()
        {
            if (!_outlinerTargetsExpectOn) return;
            if (OutlinerTargets.ReadVamTargetsOn()) return;
            OutlinerTargets.AbandonToVam();
            _outlinerTargetsExpectOn = false;
            _outlinerTargetsPulsing = false;
            _outlinerTargetsPulseUntil = 0f;
            _outlinerTargetsAppliedMode = (int)OutlinerTargetMode.Off;
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.OutlinerTargetsMode = (int)OutlinerTargetMode.Off;
                try { ScheduleQuickFiltersConfigSave(); } catch { }
            }
            SyncOutlinerTargetsButton();
            RefreshOutlinerPlayBanner();
        }

        void SyncOutlinerTargetsButton()
        {
            if (_outlinerTargetsBtn == null) return;
            OutlinerTargetMode mode = OutlinerTargetModeSetting();
            bool pulsing = _outlinerTargetsPulsing && mode == OutlinerTargetMode.Off;
            Color backdrop = GalleryUiColorTokens.ChromeIconWell;
            if (pulsing) backdrop = GalleryUiColorTokens.ActivePaused;
            else if (mode != OutlinerTargetMode.Off) backdrop = GalleryUiColorTokens.ActiveOn;
            string icon = mode == OutlinerTargetMode.Off
                ? (pulsing ? "target" : "target-off")
                : (mode == OutlinerTargetMode.Selected ? "target" : "crosshair");
            float size = OutlinerChromeSize(_outlinerChromeScale > 0f ? _outlinerChromeScale : 1f);
            try { UI.StyleFloatChromeIconButton(_outlinerTargetsBtn, size, icon, backdrop); }
            catch { }
            AddTooltipPlain(_outlinerTargetsBtn, OutlinerTargetsTooltip(mode));
        }

        static string OutlinerTargetsTooltip(OutlinerTargetMode mode)
        {
            return VPBTranslation.T("outliner.targets.tip", "Move targets")
                + ": " + OutlinerTargetModeLabel(mode)
                + "  —  " + VPBTranslation.T("outliner.targets.tip_next", "click for")
                + " " + OutlinerTargetModeLabel(OutlinerTargets.Next(mode))
                + VPBTranslation.T("outliner.targets.tip_key", "{hint:outliner_targets}");
        }

        string OutlinerTargetsFooterText()
        {
            OutlinerTargetMode mode = OutlinerTargetModeSetting();
            if (_outlinerTargetsPulsing && mode == OutlinerTargetMode.Off)
                return VPBTranslation.T("outliner.targets.footer_pulse", "Targets: showing what moved");
            if (mode == OutlinerTargetMode.Off) return "";
            return VPBTranslation.T("outliner.targets.footer", "Targets") + ": "
                + OutlinerTargetModeLabel(mode);
        }

        int OutlinerTargetsBannerKey()
        {
            OutlinerTargetMode mode = OutlinerTargetModeSetting();
            int k = (int)mode;
            if (_outlinerTargetsPulsing) k += 8;
            return k;
        }

        void IsolateOutlinerTargets(string atomUid)
        {
            if (string.IsNullOrEmpty(atomUid)) return;
            _outlinerSelection.SelectOnly(atomUid);
            RefreshOutlinerTitle();
            UpdateOutlinerVirtualVisible(true);
            RebuildOutlinerInspector();
            _outlinerPendingVamUid = atomUid;
            _outlinerPendingVamLookAt = false;
            SetOutlinerTargetMode(OutlinerTargetMode.Selected, false);
            ShowTemporaryStatus(VPBTranslation.T("outliner.targets.isolated",
                "Only this atom's targets are showing."), 2f);
        }
    }
}
