using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Sticky tool exclusivity + ambient mode chrome.
    /// One sticky tool at a time; apply/hold always visible when active; Esc names next exit.
    /// </summary>
    public partial class GalleryPanel
    {
        private enum StickyToolMode
        {
            None = 0,
            Creator = 1,
            Remove = 2,
            TryOn = 3,
            Cleanup = 4,
            Import = 5,
            BenchPick = 6
        }

        private string _modeAmbientMsg;
        private string _modeAmbientCacheKey;
        private readonly StringBuilder _modeAmbientSb = new StringBuilder(128);

        // Status multiplex cache: avoid mode+toast concat alloc every Update frame.
        private string _statusCombinedCache;
        private string _statusCombinedModeKey;
        private string _statusCombinedToastKey;
        private Color _statusBarLastColor = Color.white;
        private static readonly Color StatusBarModeTint = GalleryUiColorTokens.ModeStatusTextTool;
        private static readonly Color StatusBarApplyTint = GalleryUiColorTokens.ModeStatusTextApply;

        private GameObject _modeSemanticsBannerGO;
        private Image _modeSemanticsBannerBg;
        private Text _modeSemanticsBannerText;
        private RectTransform _modeSemanticsBannerRT;
        private bool _modeSemanticsBannerVisible;
        private string _modeSemanticsBannerCacheKey;
        private static readonly Color ModeBannerToolBg = GalleryUiColorTokens.ModeToolBanner;

        /// <summary>Sticky mode line under drag/temp status. Null when idle.</summary>
        private string ModeAmbientMsg { get { return _modeAmbientMsg; } }

        private bool IsImportStickyToolActive()
        {
            // Docked side-column Import rewrites click/chrome (sticky).
            // Floating Import is modeless — gallery browse stays fully live.
            return ImportSidebarOccupiesSideColumn;
        }

        private StickyToolMode GetActiveStickyToolMode()
        {
            // Esc ladder order for sticky tools (nested dialogs handled earlier).
            // SubScene pick is nested under Scene Tools — owns sticky chrome as Creator.
            if (_stripKeepSubScenePickActive) return StickyToolMode.Creator;
            if (creatorModeActive) return StickyToolMode.Creator;
            if (_removeModeActive) return StickyToolMode.Remove;
            if (_tryOnActive) return StickyToolMode.TryOn;
            if (cleanupModeActive) return StickyToolMode.Cleanup;
            if (IsImportStickyToolActive()) return StickyToolMode.Import;
            if (_benchPickModeActive) return StickyToolMode.BenchPick;
            return StickyToolMode.None;
        }

        private string StickyToolDisplayName(StickyToolMode mode)
        {
            switch (mode)
            {
                case StickyToolMode.Creator:
                    if (_stripKeepSubScenePickActive)
                        return VPBTranslation.T("gallery.mode.strip_creator_subscene", "Scene Tools · SubScene pick");
                    return VPBTranslation.T("gallery.mode.strip_creator", "Scene Tools");
                case StickyToolMode.Remove:
                    return VPBTranslation.T("gallery.mode.strip_remove", "Scene Eraser");
                case StickyToolMode.TryOn:
                    return VPBTranslation.T("gallery.mode.strip_tryon", "Try-On");
                case StickyToolMode.Cleanup:
                    return VPBTranslation.T("gallery.mode.strip_cleanup", "Cleanup");
                case StickyToolMode.Import:
                    return VPBTranslation.T("gallery.mode.strip_import", "Import");
                case StickyToolMode.BenchPick:
                    return VPBTranslation.T("gallery.mode.strip_bench_pick", "Bench Pick");
                default:
                    return null;
            }
        }

        private string ApplySemanticsLabel()
        {
            if (holdToLaunchEnabled)
                return VPBTranslation.T(
                    "gallery.mode.strip_hold",
                    "Hold-launch ARMED · Esc clears · drag-drop paused");
            if (ItemApplyMode == ApplyMode.SingleClick)
                return VPBTranslation.T(
                    "gallery.mode.strip_1click",
                    "1-Click ARMED · Esc clears");
            return null;
        }

        /// <summary>
        /// Exit every sticky tool except <paramref name="keep"/>.
        /// Call at the start of each sticky-tool enter path (after Try-On gate).
        /// Try-On force-revert remains fallback; normal sticky switch uses Keep/Revert dialog.
        /// </summary>
        private void ExitOtherStickyToolModes(StickyToolMode keep)
        {
            if (keep != StickyToolMode.Creator && (creatorModeActive || creatorModeStripBusy))
            {
                try { ExitCreatorMode(force: true); } catch { }
            }
            if (keep != StickyToolMode.Remove && _removeModeActive)
            {
                try { RemoveModeExit(); } catch { }
            }
            if (keep != StickyToolMode.TryOn && _tryOnActive)
            {
                // Fallback only (Esc / force). Sticky enter uses GateStickyEnterWhileTryOn first.
                try { TryOnRevert(); } catch { }
            }
            if (keep != StickyToolMode.Cleanup && cleanupModeActive)
            {
                try { ExitCleanupModeForSidePanelNavigation(); } catch { }
            }
            if (keep != StickyToolMode.Import && IsImportStickyToolActive())
            {
                try { ForceExitImportSidebarMode(); } catch { }
            }
            if (keep != StickyToolMode.BenchPick && _benchPickModeActive)
            {
                try { BenchAbortPickMode(reopenModal: false); } catch { }
            }
            if (_stripKeepSubScenePickActive && keep != StickyToolMode.Creator)
            {
                try { StripKeepAbortSubScenePickMode(reopenStrip: false); } catch { }
            }

            // Sticky enter owns input — drop armed apply so banner/chrome do not stack modes.
            if (keep != StickyToolMode.None)
            {
                try { ForceClearArmedApplySemantics(toast: false); } catch { }
            }
        }

        /// <summary>Force Hold/1-Click off (no idle check). Warm path.</summary>
        private void ForceClearArmedApplySemantics(bool toast)
        {
            bool clearedHold = false;
            bool clearedOneClick = false;

            if (holdToLaunchEnabled)
            {
                holdToLaunchEnabled = false;
                clearedHold = true;
                try
                {
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.HoldToLaunchEnabled = false;
                        VPBConfig.Instance.Save(false);
                    }
                }
                catch { }
                try { UpdateHoldToLaunchToggleUI(); } catch { }
            }

            if (ItemApplyMode == ApplyMode.SingleClick)
            {
                ItemApplyMode = ApplyMode.DoubleClick;
                clearedOneClick = true;
                try
                {
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.ApplyMode = "DoubleClick";
                        VPBConfig.Instance.Save(false);
                    }
                }
                catch { }
                try { UpdateApplyModeButtonState(); } catch { }
            }

            if (!clearedHold && !clearedOneClick) return;
            try { RefreshModeAmbientChrome(); } catch { }
            if (toast)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.apply.sticky_exit_cleared",
                        "Apply mode reset — 2-Click (safe). Esc also clears Hold / 1-Click."),
                    1.75f);
            }
        }

        private void ForceExitImportSidebarMode()
        {
            try
            {
                importSidebarOpenIntent = false;
                importSidebarOpenIntentLoaded = true;
                PersistImportSidebarOpenIntent();
            }
            catch { }
            try
            {
                if (importSidebarActive)
                    SetImportSidebarActive(false);
            }
            catch { }
        }

        /// <summary>True when Esc exits the active sticky tool (not 1-Click/Hold alone).</summary>
        private bool ModeAmbientEscExitsAny()
        {
            return GetActiveStickyToolMode() != StickyToolMode.None;
        }

        /// <summary>True when Esc clears Hold / 1-Click apply semantics.</summary>
        private bool ApplySemanticsEscExitsAny()
        {
            if (holdToLaunchEnabled) return true;
            return ItemApplyMode == ApplyMode.SingleClick;
        }

        /// <summary>
        /// Esc: clear Hold-launch and/or 1-Click back to safe 2-Click default.
        /// Only when no sticky tool owns Esc (sticky handlers run first).
        /// </summary>
        private bool TryHandleApplySemanticsEsc()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            if (GetActiveStickyToolMode() != StickyToolMode.None) return false;
            if (!ApplySemanticsEscExitsAny()) return false;

            bool clearedHold = false;
            bool clearedOneClick = false;

            if (holdToLaunchEnabled)
            {
                try { ToggleHoldToLaunch(); } catch { holdToLaunchEnabled = false; }
                clearedHold = true;
            }

            if (ItemApplyMode == ApplyMode.SingleClick)
            {
                ItemApplyMode = ApplyMode.DoubleClick;
                try { UpdateApplyModeButtonState(); } catch { }
                clearedOneClick = true;
            }

            try { RefreshModeAmbientChrome(); } catch { }

            if (clearedHold || clearedOneClick)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.apply.esc_cleared",
                        "Apply mode reset — Esc cleared Hold / 1-Click."),
                    1.5f);
            }
            return clearedHold || clearedOneClick;
        }

        /// <summary>Rebuild ambient mode string when a mode enters/exits. Cold/warm — not per-frame.</summary>
        private void RefreshModeAmbientChrome()
        {
            StickyToolMode tool = GetActiveStickyToolMode();
            string applyLabel = ApplySemanticsLabel();
            bool hold = holdToLaunchEnabled;
            bool oneClick = !hold && ItemApplyMode == ApplyMode.SingleClick;

            // Context Bar: mode ownership toggles filter-chip bar visibility — refresh before layout.
            try { RefreshActiveFilterChips(); } catch { }

            // Key avoids rebuild + string alloc when nothing changed.
            int key = ((int)tool) & 0xFF;
            if (oneClick) key |= 1 << 8;
            if (hold) key |= 1 << 9;
            if (_stripKeepSubScenePickActive) key |= 1 << 10;
            string keyStr = key.ToString();
            if (_modeAmbientCacheKey == keyStr)
            {
                SyncModeSemanticsBanner(tool, applyLabel);
                // Banner text stable — still refresh task chrome (selection/rail peers).
                try { RefreshTaskChrome(force: false); } catch { }
                return;
            }
            _modeAmbientCacheKey = keyStr;

            _modeAmbientSb.Length = 0;
            string toolName = StickyToolDisplayName(tool);
            if (!string.IsNullOrEmpty(toolName))
                _modeAmbientSb.Append(toolName);

            if (!string.IsNullOrEmpty(applyLabel))
            {
                if (_modeAmbientSb.Length > 0) _modeAmbientSb.Append(" · ");
                _modeAmbientSb.Append(applyLabel);
            }

            if (_modeAmbientSb.Length == 0)
            {
                _modeAmbientMsg = null;
                SyncModeSemanticsBanner(StickyToolMode.None, null);
                try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
                try { RefreshTboxConditionalActionButtons(); } catch { }
                return;
            }

            // Name the Esc target — matches sticky-tool Esc ladder order.
            if (tool != StickyToolMode.None && !string.IsNullOrEmpty(toolName))
            {
                _modeAmbientSb.Append(" · ");
                _modeAmbientSb.Append(
                    string.Format(
                        VPBTranslation.T("gallery.mode.strip_esc_named", "Esc → {0}"),
                        toolName));
            }
            else if (hold || oneClick)
            {
                _modeAmbientSb.Append(" · ");
                _modeAmbientSb.Append(
                    VPBTranslation.T("gallery.mode.esc_apply", "Esc → clear apply mode"));
            }

            _modeAmbientMsg = _modeAmbientSb.ToString();
            SyncModeSemanticsBanner(tool, applyLabel);
            try { InvalidateTaskChrome(); RefreshTaskChrome(force: true); } catch { }
            // Sticky/armed change → rebuild tbox peers (cache key includes TaskChrome bits).
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        /// <summary>
        /// Build status line: drag &gt; mode sticky (never blanked by toast) + optional toast.
        /// Warm path — cached concat when mode+toast both set.
        /// When mode banner visible, banner owns mode copy — status shows toast/drag only
        /// (von Restorff: one primary mode surface).
        /// </summary>
        private string ResolveStatusBarText(string drag, string toast, string mode)
        {
            if (drag != null) return drag;

            if (_modeSemanticsBannerVisible)
                return toast;

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
            Color want = Color.white;
            if (modeActive)
            {
                StickyToolMode tool = GetActiveStickyToolMode();
                want = tool != StickyToolMode.None ? StatusBarModeTint : StatusBarApplyTint;
            }
            if (_statusBarLastColor.r == want.r
                && _statusBarLastColor.g == want.g
                && _statusBarLastColor.b == want.b
                && _statusBarLastColor.a == want.a)
                return;
            _statusBarLastColor = want;
            statusBarText.color = want;
        }

        // ── Near-grid mode banner (apply/hold + sticky tool) ───────────────

        private void CreateModeSemanticsBanner(GameObject parentGO)
        {
            if (parentGO == null || _modeSemanticsBannerGO != null) return;

            float s = ChromeScale;
            _modeSemanticsBannerGO = UI.CreateChildRT(
                parentGO,
                "ModeSemanticsBanner",
                AnchorPresets.hStretchTop,
                new Vector2(0f, GalleryUiDesignTokens.ModeSemanticsBannerHeightRef * s),
                Vector2.zero);
            _modeSemanticsBannerRT = _modeSemanticsBannerGO.GetComponent<RectTransform>();
            _modeSemanticsBannerBg = UI.AddImage(_modeSemanticsBannerGO, ModeBannerToolBg, false);
            _modeSemanticsBannerText = UI.CreateLabel(
                _modeSemanticsBannerGO,
                "",
                GalleryUiDesignTokens.FontCaptionRef,
                GalleryUiColorTokens.TextOnAccent,
                TextAnchor.MiddleCenter,
                raycastTarget: false,
                name: "Label");
            RectTransform labelRT = _modeSemanticsBannerText.GetComponent<RectTransform>();
            if (labelRT != null)
            {
                labelRT.anchorMin = Vector2.zero;
                labelRT.anchorMax = Vector2.one;
                labelRT.offsetMin = new Vector2(10f * s, 0f);
                labelRT.offsetMax = new Vector2(-10f * s, 0f);
            }
            _modeSemanticsBannerGO.SetActive(false);
            _modeSemanticsBannerVisible = false;
            SetLayerRecursive(_modeSemanticsBannerGO, parentGO.layer);
        }

        private float ModeSemanticsBannerTopInsetPx(float paneScale)
        {
            if (!_modeSemanticsBannerVisible) return 0f;
            float s = paneScale <= 0f ? 1f : paneScale;
            return GalleryUiDesignTokens.ModeSemanticsBannerHeightRef * s
                + GalleryUiDesignTokens.ModeSemanticsBannerGapRef * s;
        }

        private void SyncModeSemanticsBanner(StickyToolMode tool, string applyLabel)
        {
            // Dedicated action bars own Try-On / BenchPick copy — no duplicate top banner.
            // Hold / 1-Click stay on status line only (no permanent band).
            bool dedicatedBar = tool == StickyToolMode.TryOn || tool == StickyToolMode.BenchPick;
            bool show = tool != StickyToolMode.None && !dedicatedBar;
            string toolName = StickyToolDisplayName(tool);
            string cacheKey = ((int)tool).ToString()
                + "|" + (applyLabel ?? "")
                + "|" + (show ? "1" : "0");
            if (_modeSemanticsBannerCacheKey == cacheKey
                && _modeSemanticsBannerVisible == show)
                return;
            _modeSemanticsBannerCacheKey = cacheKey;

            if (_modeSemanticsBannerGO == null)
            {
                try
                {
                    GameObject host = backgroundBoxGO != null ? backgroundBoxGO : null;
                    if (host == null && scrollRect != null && scrollRect.viewport != null)
                        host = scrollRect.viewport.gameObject;
                    CreateModeSemanticsBanner(host);
                }
                catch { }
            }

            if (_modeSemanticsBannerGO == null) return;

            bool wasVisible = _modeSemanticsBannerVisible;
            _modeSemanticsBannerVisible = show;
            _modeSemanticsBannerGO.SetActive(show);
            if (!show)
            {
                if (wasVisible)
                {
                    try { UpdateLayout(); } catch { }
                }
                return;
            }

            var sb = new StringBuilder(160);
            if (!string.IsNullOrEmpty(toolName))
            {
                sb.Append(toolName);
                sb.Append(" — ");
                switch (tool)
                {
                    case StickyToolMode.Remove:
                        sb.Append(VPBTranslation.T(
                            "gallery.mode.banner_remove",
                            "point & click to erase in the scene"));
                        break;
                    case StickyToolMode.Creator:
                        sb.Append(VPBTranslation.T(
                            "gallery.mode.banner_creator",
                            "strip / authoring tools active"));
                        break;
                    case StickyToolMode.Cleanup:
                        sb.Append(VPBTranslation.T(
                            "gallery.mode.banner_cleanup",
                            "cleanup list view"));
                        break;
                    case StickyToolMode.Import:
                        sb.Append(VPBTranslation.T(
                            "gallery.mode.banner_import",
                            "docked scene import — click sets source"));
                        break;
                }
            }
            if (!string.IsNullOrEmpty(applyLabel))
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                if (holdToLaunchEnabled)
                {
                    sb.Append(VPBTranslation.T(
                        "gallery.mode.banner_hold",
                        "Hold-launch: hold click to apply · drag-drop paused"));
                }
                else
                {
                    sb.Append(VPBTranslation.T(
                        "gallery.mode.banner_1click",
                        "1-Click: single click applies / launches"));
                }
            }
            if (tool != StickyToolMode.None && !string.IsNullOrEmpty(toolName))
            {
                sb.Append("  ·  ");
                sb.Append(
                    string.Format(
                        VPBTranslation.T("gallery.mode.strip_esc_named", "Esc → {0}"),
                        toolName));
            }

            if (_modeSemanticsBannerText != null)
                _modeSemanticsBannerText.text = sb.ToString();
            if (_modeSemanticsBannerBg != null)
                _modeSemanticsBannerBg.color = ModeBannerToolBg;

            if (wasVisible != show)
            {
                try { UpdateLayout(); } catch { }
            }
            else
            {
                try
                {
                    ApplyModeSemanticsBannerLayout(
                        _lastBrowseGridLeftInset,
                        _lastBrowseGridRightInset,
                        ChromeScale);
                }
                catch { }
            }
        }

        private void ApplyModeSemanticsBannerLayout(float leftOffset, float rightOffset, float paneScale)
        {
            if (_modeSemanticsBannerRT == null || !_modeSemanticsBannerVisible) return;
            float s = paneScale <= 0f ? 1f : paneScale;
            float h = GalleryUiDesignTokens.ModeSemanticsBannerHeightRef * s;
            float gap = GalleryUiDesignTokens.ModeSemanticsBannerGapRef * s;
            float titleBottom = -GalleryUiDesignTokens.SideTabTopOffsetRef * s;
            float filterH = 0f;
            try { filterH = ActiveFilterChromeTopInsetPx(s); } catch { }
            float barTop = titleBottom - filterH - gap;
            float pad = FilterChipHorizontalPaddingRef * s;

            _modeSemanticsBannerRT.anchorMin = new Vector2(0f, 1f);
            _modeSemanticsBannerRT.anchorMax = new Vector2(1f, 1f);
            _modeSemanticsBannerRT.pivot = new Vector2(0.5f, 1f);
            _modeSemanticsBannerRT.offsetMin = new Vector2(leftOffset + pad, barTop - h);
            _modeSemanticsBannerRT.offsetMax = new Vector2(rightOffset - pad, barTop);

            if (_modeSemanticsBannerText != null)
                GalleryUiMetrics.ApplyFont(
                    _modeSemanticsBannerText,
                    GalleryUiDesignTokens.FontCaptionRef,
                    s,
                    GalleryUiDesignTokens.FontMinRef);

            try { _modeSemanticsBannerGO.transform.SetAsLastSibling(); } catch { }
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
            try { ForceExitImportSidebarMode(); } catch { }
            return true;
        }

        /// <summary>Esc: abort Bench Pick (matches ambient Esc → Bench Pick label).</summary>
        private bool TryHandleBenchPickModeEsc()
        {
            if (!_benchPickModeActive) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            try { BenchAbortPickMode(reopenModal: true); } catch { }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.bench.esc_aborted", "Bench Pick cancelled (Esc)."),
                    1.5f);
            }
            catch { }
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
