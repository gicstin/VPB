using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private int _layoutModeWatchLast = -1;
        private int _layoutModeSuggestDeclines;
        private bool _layoutStartupApplied;

        private const int LayoutModeSuggestMaxDeclines = 3;

        private void TickLayoutModeWatch()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            // One pane owns this, or every pane would race to apply the same startup layout.
            if (!string.Equals(PanelId, PrimaryPanelId, StringComparison.Ordinal)) return;

            int mode = CurrentLayoutPresetMode();
            if (_layoutModeWatchLast < 0) _layoutModeWatchLast = mode;

            if (!_layoutStartupApplied)
            {
                if (!hasLoadedContent) return;
                if (cfg.IsLoadingScene) return;
                TryApplyStartupLayoutPreset(mode);
                return;
            }

            if (s_sessionArrangementDirty && !s_sessionArrangementRestoring && !s_layoutApplyRunning
                && !AnyPaneResizing())
                WriteSessionArrangementSnapshot();

            if (mode == _layoutModeWatchLast) return;
            _layoutModeWatchLast = mode;

            if (!cfg.LayoutPresetSuggestOnModeSwitch) return;
            if (_layoutModeSuggestDeclines >= LayoutModeSuggestMaxDeclines) return;
            if (s_layoutApplyRunning) return;

            GalleryLayoutPreset target = ResolveStartupLayoutPreset(mode);
            if (target == null) return;

            _layoutModeSuggestDeclines++;
            ShowLayoutSuggestBar(target);
        }

        private static GalleryLayoutPreset ResolveStartupLayoutPreset(int mode)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return null;

            int id = mode == (int)LayoutPresetMode.VR
                ? cfg.LayoutPresetStartupIdVR
                : cfg.LayoutPresetStartupIdDesktop;
            if (id <= 0) return null;

            GalleryLayoutPreset e = GalleryLayoutPresetStore.FindById(id);
            if (e == null || e.Mode != mode) return null;
            return e;
        }

        private void TryApplyStartupLayoutPreset(int mode)
        {
            if (_layoutStartupApplied) return;
            _layoutStartupApplied = true;

            GalleryLayoutPreset target = ResolveStartupLayoutPreset(mode);
            if (target != null)
            {
                try { ApplyNamedLayoutPreset(target); }
                catch (Exception ex) { LogUtil.LogError("[VPB][Layout] startup apply: " + ex.Message); }
                return;
            }

            if (TryRestoreSessionArrangement(mode)) return;
            TryRestoreWantedDockArrangement(mode);
        }

        private static bool s_sessionArrangementDirty;
        private static bool s_sessionArrangementRestoring;

        internal static void ClearSessionArrangementRestoring()
        {
            s_sessionArrangementRestoring = false;
        }

        internal static void ResetLayoutApplyLatches()
        {
            s_sessionArrangementRestoring = false;
            s_layoutApplyRunning = false;
        }

        internal static void MarkSessionArrangementDirty()
        {
            MarkSessionArrangementDirty(true);
        }

        internal static void MarkSessionArrangementDirty(bool writeNow)
        {
            if (s_sessionArrangementRestoring || s_layoutApplyRunning)
            {
                s_sessionArrangementDirty = true;
                return;
            }
            s_sessionArrangementDirty = true;
            if (writeNow) SaveSessionArrangementSnapshotNow();
        }

        private static bool AnyPaneResizing()
        {
            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return false;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p != null && p.isResizing) return true;
            }
            return false;
        }

        private static bool AllPanesBuilt()
        {
            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return false;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null || p.canvas == null) return false;
            }
            return true;
        }

        internal static void SaveSessionArrangementSnapshotNow()
        {
            if (s_sessionArrangementRestoring || s_layoutApplyRunning) return;
            GalleryPanel owner = FindSnapshotOwnerPanel();
            if (owner == null) return;
            owner.WriteSessionArrangementSnapshot();
        }

        private static GalleryPanel FindSnapshotOwnerPanel()
        {
            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null || panels.Count == 0) return null;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p != null && string.Equals(p.PanelId, PrimaryPanelId, StringComparison.Ordinal)) return p;
            }
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null) return panels[i];
            }
            return null;
        }

        private void WriteSessionArrangementSnapshot()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (Gallery.singleton == null || Gallery.singleton.PanelCount == 0) return;
            if (!hasLoadedContent) return;
            if (!AllPanesBuilt()) return;

            try
            {
                GalleryLayoutPreset snap = CaptureCurrentLayout("");
                if (snap.Panes == null || snap.Panes.Count < Gallery.singleton.PanelCount)
                {
                    LogUtil.LogWarning("[VPB][Layout] arrangement not saved: captured "
                        + (snap.Panes != null ? snap.Panes.Count : 0) + " of "
                        + Gallery.singleton.PanelCount + " panes");
                    return;
                }

                int docked = CountDockedPanes(snap);
                int wanted = GalleryDockLayout.WantedSideCount();
                if (docked < wanted)
                {
                    LogUtil.LogWarning("[VPB][Layout] arrangement not saved: " + docked
                        + " docked panes for " + wanted + " docked edges — a pane went away without being undocked");
                    return;
                }

                snap.RestoreFilters = false;
                string json = snap.ToJsonString();
                if (snap.IsVrPreset) cfg.LastLayoutSnapshotVR = json;
                else cfg.LastLayoutSnapshotDesktop = json;
                cfg.Save(false, true);
                s_sessionArrangementDirty = false;
            }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] session snapshot: " + ex.Message); }
        }

        private static bool SnapshotSettingsOpen(GalleryLayoutPreset preset)
        {
            if (preset == null || preset.Panes == null) return false;
            for (int i = 0; i < preset.Panes.Count; i++)
            {
                LayoutPaneState pane = preset.Panes[i];
                if (pane == null || pane.Floats == null) continue;
                for (int f = 0; f < pane.Floats.Count; f++)
                {
                    LayoutFloatState fl = pane.Floats[f];
                    if (fl == null || fl.Kind != (int)LayoutFloatKind.Settings) continue;
                    return fl.Open;
                }
            }
            return false;
        }

        private static int CountDockedPanes(GalleryLayoutPreset preset)
        {
            if (preset == null || preset.Panes == null) return 0;
            int n = 0;
            for (int i = 0; i < preset.Panes.Count; i++)
            {
                LayoutPaneState p = preset.Panes[i];
                if (p != null && p.DockSlot != (int)GalleryDockSide.None) n++;
            }
            return n;
        }

        private bool TryRestoreSessionArrangement(int mode)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return false;

            string json = mode == (int)LayoutPresetMode.VR
                ? cfg.LastLayoutSnapshotVR
                : cfg.LastLayoutSnapshotDesktop;
            if (string.IsNullOrEmpty(json)) return false;

            GalleryLayoutPreset snap = GalleryLayoutPreset.FromJsonString(json);
            if (snap == null || snap.Mode != mode) return false;
            if (snap.Panes == null || snap.Panes.Count == 0) return false;

            snap.Name = "";
            snap.RestoreFilters = false;

            LogUtil.Log("[VPB][Layout] restoring arrangement: panes=" + snap.Panes.Count
                + " docked=" + CountDockedPanes(snap) + " wantedEdges=" + GalleryDockLayout.WantedSideCount()
                + " settings=" + (SnapshotSettingsOpen(snap) ? "open" : "closed"));

            s_sessionArrangementRestoring = true;
            bool started = false;
            try { started = ApplyLayoutPreset(snap, false); }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] session restore: " + ex.Message); }
            if (!started)
            {
                s_sessionArrangementRestoring = false;
                return false;
            }

            StartCoroutine(TopUpWantedDocksAfterRestoreCo(mode));
            return true;
        }

        private void TryRestoreWantedDockArrangement(int mode)
        {
            if (mode == (int)LayoutPresetMode.VR) return;
            if (Gallery.singleton == null) return;
            if (s_layoutApplyRunning) return;
            if (GalleryDockLayout.FirstUnclaimedWantedSide() == GalleryDockSide.None) return;
            s_sessionArrangementDirty = false;
            s_sessionArrangementRestoring = true;
            StartCoroutine(RestoreWantedDockArrangementCo());
        }

        private IEnumerator TopUpWantedDocksAfterRestoreCo(int mode)
        {
            for (float waited = 0f; waited < TopUpWatchSeconds; waited += TopUpPollSeconds)
            {
                yield return s_topUpPollWait;
                if (this == null || canvas == null) yield break;

                if (s_layoutApplyRunning || s_sessionArrangementRestoring) continue;
                if (VPBConfig.Instance != null && VPBConfig.Instance.IsLoadingScene) continue;

                GalleryDockSide missing = GalleryDockLayout.FirstUnclaimedWantedSide();
                if (missing == GalleryDockSide.None) yield break;

                LogUtil.LogWarning("[VPB][Layout] arrangement short after restore: "
                    + GalleryDockLayout.ToConfigString(missing) + " dock had no pane — rebuilding it");
                TryRestoreWantedDockArrangement(mode);
            }
        }

        private const float TopUpWatchSeconds = 20f;
        private const float TopUpPollSeconds = 1f;
        private static readonly WaitForSeconds s_topUpPollWait = new WaitForSeconds(TopUpPollSeconds);

        private IEnumerator RestoreWantedDockArrangementCo()
        {
            bool alive = true;
            for (int edge = 0; alive && edge < 3; edge++)
            {
                GalleryDockSide side = GalleryDockLayout.FirstUnclaimedWantedSide();
                if (side == GalleryDockSide.None) break;

                int before = Gallery.singleton != null ? Gallery.singleton.PanelCount : 0;
                try { if (Gallery.singleton != null) Gallery.singleton.CreatePane(null, true); }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB][Layout] restore dock pane: " + ex.Message);
                    break;
                }

                yield return null;
                alive = this != null && canvas != null;
                if (!alive) break;

                int after = Gallery.singleton != null ? Gallery.singleton.PanelCount : 0;
                if (after <= before) break;

                GalleryPanel added = Gallery.singleton.Panels[after - 1];
                if (added == null) break;

                try { added.DockPaneToSide(side); }
                catch (Exception ex) { LogUtil.LogError("[VPB][Layout] restore dock side: " + ex.Message); }

                yield return null;
                alive = this != null && canvas != null;
            }

            s_sessionArrangementRestoring = false;
            MarkSessionArrangementDirty();
        }

        private void ShowLayoutSuggestBar(GalleryLayoutPreset target)
        {
            if (target == null) return;
            _layoutSuggestTarget = target;

            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.layout_preset.suggest",
                    "{0} mode — apply layout \"{1}\"? (Alt+L to open layouts)"),
                target.IsVrPreset ? "VR" : "Desktop",
                target.Name ?? ""), 5f);
        }

        private GalleryLayoutPreset _layoutSuggestTarget;

        internal bool HasSuggestedLayoutPreset()
        {
            return _layoutSuggestTarget != null;
        }

        internal bool ApplySuggestedLayoutPreset()
        {
            GalleryLayoutPreset t = _layoutSuggestTarget;
            _layoutSuggestTarget = null;
            if (t == null) return false;
            return ApplyNamedLayoutPreset(t);
        }
    }
}
