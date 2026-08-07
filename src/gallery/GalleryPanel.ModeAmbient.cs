using System.Text;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Ambient mode strip for status bar — warm path, zero per-frame alloc when unchanged.
    /// Esc claim matches modes that Esc actually exits.
    /// </summary>
    public partial class GalleryPanel
    {
        private string _modeAmbientMsg;
        private string _modeAmbientCacheKey;
        private readonly StringBuilder _modeAmbientSb = new StringBuilder(96);

        // Status multiplex cache: avoid mode+toast concat alloc every Update frame.
        private string _statusCombinedCache;
        private string _statusCombinedModeKey;
        private string _statusCombinedToastKey;
        private Color _statusBarLastColor = Color.white;
        private static readonly Color StatusBarModeTint = new Color(0.85f, 0.92f, 1f, 1f);

        /// <summary>Sticky mode line under drag/temp status. Null when idle.</summary>
        private string ModeAmbientMsg { get { return _modeAmbientMsg; } }

        /// <summary>True when Esc exits at least one sticky tool mode (not 1-Click/Hold alone).</summary>
        private bool ModeAmbientEscExitsAny()
        {
            if (_removeModeActive) return true;
            if (creatorModeActive) return true;
            if (_tryOnActive) return true;
            if (cleanupModeActive) return true;
            if (importSidebarActive) return true;
            if (importSidebarDetached && importSidebarOpenIntent) return true;
            return false;
        }

        /// <summary>Rebuild ambient mode string when a mode enters/exits. Cold/warm — not per-frame.</summary>
        private void RefreshModeAmbientChrome()
        {
            // Key avoids rebuild + string alloc when nothing changed.
            int key = 0;
            if (_removeModeActive) key |= 1;
            if (creatorModeActive) key |= 2;
            if (_tryOnActive) key |= 4;
            if (importSidebarActive || (importSidebarDetached && importSidebarOpenIntent)) key |= 8;
            if (cleanupModeActive) key |= 16;
            if (ItemApplyMode == ApplyMode.SingleClick) key |= 32;
            if (holdToLaunchEnabled) key |= 64;
            string keyStr = key.ToString();
            if (_modeAmbientCacheKey == keyStr) return;
            _modeAmbientCacheKey = keyStr;

            // Skip sticky strip when only apply/hold chrome (avoid permanent noise).
            bool hasRealMode = (key & ~(32 | 64)) != 0;
            if (!hasRealMode)
            {
                _modeAmbientMsg = null;
                return;
            }

            _modeAmbientSb.Length = 0;
            AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_remove", "Scene Eraser"), _removeModeActive);
            AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_creator", "Scene Tools"), creatorModeActive);
            AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_tryon", "Try-On"), _tryOnActive);
            AppendModeAmbientPart(
                VPBTranslation.T("gallery.mode.strip_import", "Import"),
                importSidebarActive || (importSidebarDetached && importSidebarOpenIntent));
            AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_cleanup", "Cleanup"), cleanupModeActive);
            if (holdToLaunchEnabled)
                AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_hold", "Hold-launch"), true);
            else if (ItemApplyMode == ApplyMode.SingleClick)
                AppendModeAmbientPart(VPBTranslation.T("gallery.mode.strip_1click", "1-Click"), true);

            if (_modeAmbientSb.Length == 0)
            {
                _modeAmbientMsg = null;
                return;
            }

            // Only claim Esc when Esc ladder can exit a listed tool mode.
            if (ModeAmbientEscExitsAny())
            {
                _modeAmbientSb.Append(" · ");
                _modeAmbientSb.Append(VPBTranslation.T("gallery.mode.strip_esc", "Esc exits"));
            }

            _modeAmbientMsg = _modeAmbientSb.ToString();
        }

        private void AppendModeAmbientPart(string label, bool on)
        {
            if (!on || string.IsNullOrEmpty(label)) return;
            if (_modeAmbientSb.Length > 0) _modeAmbientSb.Append(" · ");
            _modeAmbientSb.Append(label);
        }

        /// <summary>
        /// Build status line: drag &gt; mode sticky (never blanked by toast) + optional toast.
        /// Warm path — cached concat when mode+toast both set.
        /// </summary>
        private string ResolveStatusBarText(string drag, string toast, string mode)
        {
            if (drag != null) return drag;

            if (mode != null && toast != null)
            {
                if (_statusCombinedModeKey != mode || _statusCombinedToastKey != toast)
                {
                    _statusCombinedModeKey = mode;
                    _statusCombinedToastKey = toast;
                    _statusCombinedCache = mode + " · " + toast;
                }
                return _statusCombinedCache;
            }

            _statusCombinedModeKey = null;
            _statusCombinedToastKey = null;
            _statusCombinedCache = null;

            if (toast != null) return toast;
            return mode;
        }

        private void ApplyStatusBarModeTint(bool modeActive)
        {
            if (statusBarText == null) return;
            Color want = modeActive ? StatusBarModeTint : Color.white;
            if (_statusBarLastColor.r == want.r
                && _statusBarLastColor.g == want.g
                && _statusBarLastColor.b == want.b
                && _statusBarLastColor.a == want.a)
                return;
            _statusBarLastColor = want;
            statusBarText.color = want;
        }

        /// <summary>Esc ladder: exit Scene Eraser when gallery owns focus.</summary>
        private bool TryHandleRemoveModeEsc()
        {
            if (!_removeModeActive) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            try { RemoveModeExit(); } catch { }
            return true;
        }

        /// <summary>Esc: revert + end Try-On session (safe exit, no commit).</summary>
        private bool TryHandleTryOnEsc()
        {
            if (!_tryOnActive) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            try { TryOnRevert(); } catch { }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.tryon.esc_reverted", "Try-On reverted (Esc)."),
                    1.5f);
            }
            catch { }
            return true;
        }

        /// <summary>Esc: leave Cleanup list view.</summary>
        private bool TryHandleCleanupModeEsc()
        {
            if (!cleanupModeActive) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            try { ExitCleanupModeForSidePanelNavigation(); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.cleanup.exited", "Cleanup closed."),
                    1.25f);
            }
            catch { }
            return true;
        }

        /// <summary>Esc: close docked Import sidebar (float path handled separately).</summary>
        private bool TryHandleImportSidebarDockedEsc()
        {
            if (!importSidebarActive) return false;
            if (importSidebarDetached) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            try { SetImportSidebarActive(false); } catch { }
            return true;
        }

        /// <summary>Esc: clear grid selection when nothing else claimed Esc.</summary>
        private bool TryHandleClearSelectionEsc()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            if (!IsVisible) return false;
            if (selectedFiles == null || selectedFiles.Count == 0) return false;
            try { ClearSelection(); } catch { }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.keyboard.selection_cleared", "Selection cleared."),
                    1.0f);
            }
            catch { }
            return true;
        }
    }
}
