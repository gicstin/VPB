using System;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private int _layoutModeWatchLast = -1;
        private int _layoutModeSuggestDeclines;
        private bool _layoutStartupApplied;

        private const int LayoutModeSuggestMaxDeclines = 3;

        /// <summary>
        /// Watches for a VR/desktop switch and *offers* that mode's startup layout. Never applies it
        /// silently: relocating someone's windows without asking is the failure mode the semi-automated
        /// finding in the research warns about.
        /// </summary>
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
                // Wait for the pane to finish its first load — applying into a half-built pane
                // fights the initial category bind.
                if (!hasLoadedContent) return;
                TryApplyStartupLayoutPreset(mode);
                return;
            }

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

        /// <summary>Opt-in, per mode, off by default — a startup layout is a contract the user signed.</summary>
        private void TryApplyStartupLayoutPreset(int mode)
        {
            if (_layoutStartupApplied) return;
            _layoutStartupApplied = true;

            GalleryLayoutPreset target = ResolveStartupLayoutPreset(mode);
            if (target == null) return;
            try { ApplyNamedLayoutPreset(target); }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] startup apply: " + ex.Message); }
        }

        /// <summary>Reuses the Revert bar chrome as a one-tap suggestion: Apply, or dismiss.</summary>
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

        /// <summary>Accepts a pending mode-switch suggestion. Exposed for the palette and quick menu.</summary>
        internal bool ApplySuggestedLayoutPreset()
        {
            GalleryLayoutPreset t = _layoutSuggestTarget;
            _layoutSuggestTarget = null;
            if (t == null) return false;
            return ApplyNamedLayoutPreset(t);
        }
    }
}
