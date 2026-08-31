using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Mode-conditioned task chrome: one primary + legal peers per panel state.
    /// Warm/cold only — int cache key, no per-frame alloc, no LINQ.
    /// Banners name the mode (ModeAmbient); this file makes chrome obey it.
    /// </summary>
    public partial class GalleryPanel
    {
        private enum TaskChromeState : byte
        {
            Browse = 0,
            Selection = 1,
            ArmedApply = 2,
            StickyCreator = 3,
            StickyRemove = 4,
            StickyTryOn = 5,
            StickyCleanup = 6,
            StickyImport = 7,
            /// <summary>Legacy — Settings float is modeless; ResolveTaskChromeState never returns this.</summary>
            Settings = 9
        }

        private int _taskChromeAppliedKey = int.MinValue;
        private TaskChromeState _taskChromeState = TaskChromeState.Browse;

        // Soft-disable alpha for illegal sticky peers (recognition, not vanish of exit affordance).
        private const float TaskChromePeerDisabledAlpha = 0.32f;

        private TaskChromeState ResolveTaskChromeState()
        {
            // Settings float is modeless — never owns gallery task chrome (Galitz: float ≠ mode).
            StickyToolMode tool = GetActiveStickyToolMode();
            switch (tool)
            {
                case StickyToolMode.Creator: return TaskChromeState.StickyCreator;
                case StickyToolMode.Remove: return TaskChromeState.StickyRemove;
                case StickyToolMode.TryOn: return TaskChromeState.StickyTryOn;
                case StickyToolMode.Cleanup: return TaskChromeState.StickyCleanup;
                case StickyToolMode.Import: return TaskChromeState.StickyImport;
            }

            if (holdToLaunchEnabled || ItemApplyMode == ApplyMode.SingleClick)
                return TaskChromeState.ArmedApply;

            if (selectedFiles != null && selectedFiles.Count > 0)
                return TaskChromeState.Selection;

            return TaskChromeState.Browse;
        }

        private int BuildTaskChromeCacheKey(TaskChromeState state)
        {
            // Pack: state | selBit | stripBit | hold | oneClick | sticky enum
            int key = (int)state & 0xFF;
            if (selectedFiles != null && selectedFiles.Count > 0) key |= 1 << 8;
            if (DetailStripIsExpanded()) key |= 1 << 9;
            if (holdToLaunchEnabled) key |= 1 << 10;
            if (ItemApplyMode == ApplyMode.SingleClick) key |= 1 << 11;
            key |= ((int)GetActiveStickyToolMode() & 0xF) << 12;
            if (cleanupModeActive) key |= 1 << 16;
            if (creatorModeActive) key |= 1 << 17;
            if (_removeModeActive) key |= 1 << 18;
            if (_tryOnActive) key |= 1 << 19;
            return key;
        }

        /// <summary>
        /// Apply rail / apply-hold / detail policies for current task state.
        /// Call from ModeAmbient funnel and after selection/tbox rebuilds.
        /// </summary>
        private void RefreshTaskChrome(bool force)
        {
            TaskChromeState state = ResolveTaskChromeState();
            int key = BuildTaskChromeCacheKey(state);
            if (!force && key == _taskChromeAppliedKey)
                return;
            _taskChromeAppliedKey = key;
            _taskChromeState = state;

            try { ApplyTaskChromeRailPolicy(state); } catch { }
            try { ApplyTaskChromeApplyHoldPolicy(state); } catch { }
            try { ApplyTaskChromeDetailStripPolicy(state); } catch { }
        }

        /// <summary>Force next RefreshTaskChrome to re-apply (mode flag flipped mid-frame).</summary>
        private void InvalidateTaskChrome()
        {
            _taskChromeAppliedKey = int.MinValue;
        }

        private static bool TaskChromeIsSticky(TaskChromeState state)
        {
            return state == TaskChromeState.StickyCreator
                || state == TaskChromeState.StickyRemove
                || state == TaskChromeState.StickyTryOn
                || state == TaskChromeState.StickyCleanup
                || state == TaskChromeState.StickyImport;
        }

        /// <summary>Hide / soft-disable 1-Click + Hold while sticky owns input.</summary>
        private bool TaskChromeSuppressArmedApplyChrome(TaskChromeState state)
        {
            return TaskChromeIsSticky(state);
        }

        /// <summary>Detail strip Load/Hub/Delete — off when sticky rewrites grid semantics.</summary>
        private static bool TaskChromeAllowDetailStripTools(TaskChromeState state)
        {
            return state == TaskChromeState.Browse
                || state == TaskChromeState.Selection
                || state == TaskChromeState.ArmedApply
                || state == TaskChromeState.StickyCreator
                || state == TaskChromeState.StickyImport;
        }

        /// <summary>
        /// Selection + expanded strip: strip owns Load (one launch primary).
        /// Delete stays on toolbox too (duplicate ok — Fitts reach from bar).
        /// Collapsed strip: toolbox owns Load.
        /// </summary>
        private bool TaskChromeStripOwnsSelectionPrimary()
        {
            return _taskChromeState == TaskChromeState.Selection
                && DetailStripIsExpanded()
                && selectedFiles != null
                && selectedFiles.Count > 0;
        }

        private void ApplyTaskChromeRailPolicy(TaskChromeState state)
        {
            bool armed = state == TaskChromeState.ArmedApply;
            bool sticky = TaskChromeIsSticky(state);

            // Competing sticky enters blocked while another sticky or armed-apply owns chrome.
            // Settings float is modeless — never soft-disables sticky enter rails.
            bool allowCreatorEnter = state == TaskChromeState.StickyCreator
                || (!sticky && !armed);
            bool allowRemoveEnter = state == TaskChromeState.StickyRemove
                || (!sticky && !armed);

            // Own-mode rail stays interactive (toggle/exit). Foreign sticky + armed → soft-disable.
            SetTaskChromePeerEnabled(leftRemoveModeSideBtn, allowRemoveEnter || state == TaskChromeState.StickyRemove);
            SetTaskChromePeerEnabled(rightRemoveModeSideBtn, allowRemoveEnter || state == TaskChromeState.StickyRemove);

            // Import float is modeless; docked Import sticky soft-disables foreign enters only.
            bool allowImportEnter = state == TaskChromeState.StickyImport
                || (!sticky && !armed);
            SetTaskChromePeerEnabled(leftSceneImportSideBtn, allowImportEnter || state == TaskChromeState.StickyImport);
            SetTaskChromePeerEnabled(rightSceneImportSideBtn, allowImportEnter || state == TaskChromeState.StickyImport);

            try { RefreshSceneImportSideButtonVisibility(); } catch { }
        }

        private void ApplyTaskChromeApplyHoldPolicy(TaskChromeState state)
        {
            bool suppress = TaskChromeSuppressArmedApplyChrome(state);
            // When armed, keep toggles live so user can clear / Esc path stays Fitts-easy.
            bool enableApplyToggle = !suppress && !holdToLaunchEnabled;
            bool enableHoldToggle = !suppress;

            SetTaskChromePeerEnabled(footerApplyModeBtn, enableApplyToggle);
            SetTaskChromePeerEnabled(footerHoldToLaunchToggleBtn, enableHoldToggle);

            // Restyle apply buttons when allowed (UpdateApplyModeButtonState owns colors).
            if (!suppress)
            {
                try { UpdateApplyModeButtonState(); } catch { }
                try { UpdateHoldToLaunchToggleUI(); } catch { }
            }
            else
            {
                // Dim while sticky — avoid amber "armed" look fighting sticky banner.
                DimApplyModeButtonVisual(footerApplyModeBtnImage);
                if (footerHoldToLaunchToggleBtnImage != null)
                    footerHoldToLaunchToggleBtnImage.color = new Color(0f, 0f, 0f, 0.18f);
            }
        }

        private static void DimApplyModeButtonVisual(Image img)
        {
            if (img == null) return;
            img.color = new Color(0.22f, 0.22f, 0.22f, 0.85f);
        }

        private void ApplyTaskChromeDetailStripPolicy(TaskChromeState state)
        {
            // Expand affordance off when sticky owns input (strip tools gated in SetToolLinksEnabled).
            if (!TaskChromeAllowDetailStripTools(state)
                && _detailStripExpandBtnGO != null
                && _detailStripExpandBtnGO.activeSelf)
            {
                _detailStripExpandBtnGO.SetActive(false);
            }
        }

        /// <summary>
        /// Gate for DetailStripSetToolLinksEnabled — sticky destroy/session modes kill strip actions.
        /// </summary>
        private bool TaskChromeShouldEnableDetailStripTools(bool wantEnabled)
        {
            if (!wantEnabled) return false;
            return TaskChromeAllowDetailStripTools(ResolveTaskChromeState());
        }

        /// <summary>
        /// After normal tbox show/enable for browse/selection: demote peers so one primary remains.
        /// </summary>
        private void TaskChromeApplyTboxSelectionPrimary(
            System.Action<GameObject, bool> show)
        {
            if (show == null) return;

            // Expanded detail strip owns Load — hide toolbox Load duplicate (von Restorff).
            // Keep Delete on toolbox (users expect bar action even with strip open).
            if (TaskChromeStripOwnsSelectionPrimary())
            {
                show(tboxLoadBtn, false);
            }
        }

        /// <summary>
        /// Mass-hide browse toolbox peers for sticky tools that rewrite click semantics.
        /// Mirrors Cleanup/Settings early-out pattern. Returns true if caller should return.
        /// Creator/Import demotion runs in TaskChromeApplyTboxPostPass (after normal show).
        /// </summary>
        private bool TaskChromeTryApplyStickyTbox(
            System.Action<GameObject, bool> show)
        {
            if (show == null) return false;

            TaskChromeState state = ResolveTaskChromeState();
            _taskChromeState = state;

            if (state != TaskChromeState.StickyRemove
                && state != TaskChromeState.StickyTryOn)
                return false;

            // Point/pick/session owns input — kill browse action peers.
            show(tboxSettingsCancelBtn, false);
            show(tboxSettingsSaveBtn, false);
            show(tboxCleanupBtn, false);
            show(tboxCleanupApplyBtn, false);
            show(tboxCleanupClearBtn, false);
            show(tboxCleanupAddExcludeBtn, false);
            show(tboxCleanupRemoveExcludeBtn, false);
            show(tboxCreatorModeBtn, false);
            show(tboxCreatorStripSceneBtn, false);
            show(tboxCreatorCompressCacheBtn, false);
            show(tboxAutoInstallBtn, false);
            show(tboxDisableAutoInstallBtn, false);
            show(tboxHideBtn, false);
            show(tboxUnhideBtn, false);
            show(tboxScanWhitelistTemporaryBtn, false);
            show(tboxLoadBtn, false);
            show(tboxLoadRandomBtn, false);
            show(tboxUnloadBtn, false);
            show(tboxLoadDepsBtn, false);
            show(tboxCacheTexturesBtn, false);
            show(tboxThumbDebugBtn, false);
            show(tboxOpenHubBtn, false);
            show(tboxCopyPkgNamesBtn, false);
            show(tboxOverwriteSceneBtn, false);
            show(tboxSuppressScaleBtn, false);
            show(tboxReplaceBtn, false);
            show(tboxDeleteBtn, false);
            show(tboxRemoveHistoryBtn, false);
            show(tboxSelectAllBtn, false);
            show(tboxClearSelectionBtn, true);
            show(_detailStripExpandBtnGO, false);

            for (int i = 0; i < tboxPersonAtomBtns.Count; i++)
                show(tboxPersonAtomBtns[i], false);
            try { CloseTboxTargetMenu(); } catch { }

            SetTboxButtonEnabledVisual(tboxClearSelectionBtn, selectedFiles != null && selectedFiles.Count > 0);

            try { RefreshSceneImportSideButtonVisibility(); } catch { }
            try { UpdateSideButtonPositions(); } catch { }
            try { RefreshTboxGridRateControlState(); } catch { }
            RefreshTboxFlexButtonLayout();
            try { RefreshTaskChrome(force: true); } catch { }
            return true;
        }

        /// <summary>
        /// After normal tbox show/enable: demote Creator/Import peers + selection dual-primary.
        /// Must run last so earlier show() cannot resurrect illegal peers.
        /// </summary>
        private void TaskChromeApplyTboxPostPass(System.Action<GameObject, bool> show)
        {
            if (show == null) return;

            TaskChromeState state = ResolveTaskChromeState();
            _taskChromeState = state;

            if (state == TaskChromeState.StickyCreator)
            {
                // Scene Tools: Strip is primary; demote bulk browse peers.
                show(tboxCleanupBtn, false);
                show(tboxLoadBtn, false);
                show(tboxLoadRandomBtn, false);
                show(tboxUnloadBtn, false);
                show(tboxLoadDepsBtn, false);
                show(tboxOpenHubBtn, false);
                show(tboxOverwriteSceneBtn, false);
                show(tboxSuppressScaleBtn, false);
                show(tboxReplaceBtn, false);
                show(tboxDeleteBtn, false);
                show(tboxHideBtn, false);
                show(tboxUnhideBtn, false);
                show(tboxAutoInstallBtn, false);
                show(tboxDisableAutoInstallBtn, false);
                show(tboxScanWhitelistTemporaryBtn, false);
                show(tboxCreatorStripSceneBtn, true);
                show(tboxCreatorCompressCacheBtn, true);
                show(tboxCreatorModeBtn, true);
                show(_detailStripExpandBtnGO, false);
            }
            else if (state == TaskChromeState.StickyImport)
            {
                show(tboxCleanupBtn, false);
                show(tboxCreatorStripSceneBtn, false);
                show(tboxCreatorCompressCacheBtn, false);
                show(tboxLoadBtn, false);
                show(tboxLoadRandomBtn, false);
                show(tboxUnloadBtn, false);
                show(tboxDeleteBtn, false);
                show(tboxOverwriteSceneBtn, false);
                show(tboxReplaceBtn, false);
                show(_detailStripExpandBtnGO, false);
            }
            else if (state == TaskChromeState.ArmedApply)
            {
                // Armed apply: grid click is primary — hide competing sticky enter from tbox.
                show(tboxCleanupBtn, false);
                show(tboxCreatorStripSceneBtn, false);
                show(tboxCreatorCompressCacheBtn, false);
            }
            else if (state == TaskChromeState.Browse)
            {
                // Idle browse: quiet toolbox until selection (Context Bar / Hick).
                TaskChromeApplyTboxBrowseQuiet(show);
            }
            else
            {
                TaskChromeApplyTboxSelectionPrimary(show);
            }

            try { RefreshTaskChrome(force: false); } catch { }
        }

        /// <summary>
        /// No selection: keep find/browse peers; hide selection-bound bulk actions.
        /// </summary>
        private void TaskChromeApplyTboxBrowseQuiet(System.Action<GameObject, bool> show)
        {
            if (show == null) return;

            show(tboxHideBtn, false);
            show(tboxUnhideBtn, false);
            show(tboxAutoInstallBtn, false);
            show(tboxDisableAutoInstallBtn, false);
            show(tboxScanWhitelistTemporaryBtn, false);
            show(tboxLoadBtn, false);
            show(tboxUnloadBtn, false);
            show(tboxLoadDepsBtn, false);
            show(tboxCacheTexturesBtn, false);
            show(tboxThumbDebugBtn, false);
            show(tboxOpenHubBtn, false);
            show(tboxCopyPkgNamesBtn, false);
            show(tboxDeleteBtn, false);
            show(tboxOverwriteSceneBtn, false);
            show(tboxSuppressScaleBtn, false);
            show(tboxReplaceBtn, false);
            show(tboxClearSelectionBtn, false);
            show(tboxRemoveHistoryBtn, false);
            show(_detailStripExpandBtnGO, false);

            // Keep: Cleanup, SelectAll, LoadRandom (browse without selection), person targets.
            show(tboxCleanupBtn, true);
            show(tboxSelectAllBtn, true);
            show(tboxLoadRandomBtn, true);
            show(tboxCreatorModeBtn, true);
        }

        private static void SetTaskChromePeerEnabled(GameObject go, bool enabled)
        {
            if (go == null) return;
            try
            {
                Button btn = go.GetComponent<Button>();
                if (btn != null) btn.interactable = enabled;

                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = enabled ? 1f : TaskChromePeerDisabledAlpha;
                cg.blocksRaycasts = enabled;
                cg.interactable = enabled;
            }
            catch { }
        }

        /// <summary>Char for tbox conditional cache — sticky + armed bits.</summary>
        private void AppendTaskChromeTboxCacheKey(System.Text.StringBuilder sb)
        {
            if (sb == null) return;
            sb.Append(_removeModeActive ? 'R' : 'r');
            sb.Append(_tryOnActive ? 'Y' : 'y');
            sb.Append(IsImportStickyToolActive() ? 'I' : 'i');
            sb.Append(holdToLaunchEnabled ? 'L' : 'l');
            sb.Append(ItemApplyMode == ApplyMode.SingleClick ? '1' : '2');
        }
    }
}
