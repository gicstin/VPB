using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SimpleJSON;

namespace VPB
{
    public static class UI
    {
        private static float _lastLoadSceneStartTime = -9999f;

        // Universal gallery chrome colors.
        public static readonly Color PopupBackdrop = new Color(0.12f, 0.12f, 0.14f, 0.72f);
        public static readonly Color PopupRowBackdrop = new Color(0.18f, 0.18f, 0.20f, 1f);
        public static readonly Color PopupRowActiveBackdrop = new Color(0.28f, 0.30f, 0.34f, 1f);
        public static readonly Color PopupText = Color.white;
        public static readonly Color PopupMutedText = new Color(0.65f, 0.65f, 0.68f, 1f);
        public static readonly Color TextPrimary = new Color(0.92f, 0.92f, 0.92f, 1f);
        public static readonly Color TextMuted = new Color(0.72f, 0.72f, 0.75f, 1f);
        public static readonly Color TextDim = new Color(0.55f, 0.55f, 0.58f, 1f);
        public static readonly Color InputFieldTextColor = Color.white;
        public static readonly Color InputFieldPlaceholderColor = new Color(0.5f, 0.5f, 0.52f, 1f);
        public static readonly Color InputFieldBg = new Color(0.10f, 0.10f, 0.12f, 1f);
        public static readonly Color TextShadowColor = new Color(0f, 0f, 0f, 0.75f);

        // Neutral chrome fills (formerly written inline as raw new Color(...) dozens of times).
        public static readonly Color ChromeDarker = new Color(0.1f, 0.1f, 0.1f, 1f);
        public static readonly Color ChromeDark = new Color(0.15f, 0.15f, 0.15f, 1f);
        public static readonly Color ChromePanel = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color ChromeMid = new Color(0.3f, 0.3f, 0.3f, 1f);
        // Interactive accents: blue = active/selected state, green = on/confirm, red = off/clear/destructive.
        public static readonly Color AccentBlue = new Color(0.15f, 0.45f, 0.6f, 1f);
        public static readonly Color AccentGreen = new Color(0.2f, 0.6f, 0.2f, 1f);
        public static readonly Color AccentRed = new Color(0.6f, 0.2f, 0.2f, 1f);

        /// <summary>Background of centered modal panels (formerly inline new Color(0.06,0.06,0.08,1)).</summary>
        public static readonly Color ModalPanel = new Color(0.06f, 0.06f, 0.08f, 1f);
        // Standard gallery Button ColorBlock tints (formerly inlined per button). White normalColor keeps the
        // RoundedRect fill unchanged; hover brightens, press darkens, disabled dims + fades.
        public static readonly Color ButtonHighlight = new Color(1.2f, 1.2f, 1.2f, 1f);
        public static readonly Color ButtonPressed = new Color(0.8f, 0.8f, 0.8f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        /// <summary>White with the given alpha — for hover/separator/overlay tints (replaces inline new Color(1,1,1,a)).</summary>
        public static Color White(float alpha) => new Color(1f, 1f, 1f, alpha);
        /// <summary>Black with the given alpha — for scrims/shadows (replaces inline new Color(0,0,0,a)).</summary>
        public static Color Black(float alpha) => new Color(0f, 0f, 0f, alpha);

        /// <summary>
        /// Kills Unity <see cref="Selectable"/> ColorTint hover/press (the gray “fill” on neutral buttons).
        /// Keeps <see cref="ColorBlock.disabledColor"/> so disabled chrome still dims.
        /// </summary>
        public static void NeutralizeSelectableColorTint(Selectable sel)
        {
            if (sel == null) return;
            try
            {
                ColorBlock cb = sel.colors;
                Color dis = cb.disabledColor;
                cb.normalColor = Color.white;
                cb.highlightedColor = Color.white;
                cb.pressedColor = Color.white;
                // Older Unity ColorBlock has no selectedColor (VaM stack).
                cb.disabledColor = dis;
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0f;
                sel.colors = cb;
                sel.transition = Selectable.Transition.None;
                sel.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            catch { }
        }

        private static bool IsFilterChipDismissButton(Transform t)
        {
            if (t == null) return false;
            if (!string.Equals(t.name, "Dismiss", StringComparison.Ordinal)) return false;
            Transform p = t.parent;
            return p != null && p.name.StartsWith("FilterChip", StringComparison.Ordinal);
        }

        private static bool IsUnderImportSidebarScrollViewport(Transform t)
        {
            while (t != null)
            {
                if (t.name == "Content" && t.parent != null && t.parent.name == "Viewport")
                {
                    Transform walk = t.parent.parent;
                    while (walk != null)
                    {
                        if (walk.name == "VPB_ImportSidebar") return true;
                        walk = walk.parent;
                    }
                    return false;
                }
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Gallery pane: no ColorTint fill on any <see cref="Selectable"/>; buttons get
        /// <see cref="UIHoverBorder"/>. Run once at init and continuously via <see cref="GalleryPaneChromeEnforcer"/>
        /// so tabs/redraws cannot restore default hover fill.
        /// </summary>
        public static void ApplyGalleryPaneHoverPolicy(GameObject root)
        {
            if (root == null) return;
            try
            {
                Color border = new Color(1f, 1f, 0f, 1f);
                try { if (VPBConfig.Instance != null) border = VPBConfig.Instance.GetGalleryGridBorderColor(); } catch { }

                var sels = root.GetComponentsInChildren<Selectable>(true);
                for (int i = 0; i < sels.Length; i++)
                {
                    var s = sels[i];
                    if (s == null) continue;
                    NeutralizeSelectableColorTint(s);
                    if (s is Button)
                    {
                        if (IsFilterChipDismissButton(s.transform)) continue;
                        var hb = s.GetComponent<UIHoverBorder>();
                        if (hb == null) hb = s.gameObject.AddComponent<UIHoverBorder>();
                        // Global default for buttons that don't override (file rows override per-row already)
                        hb.hoverColor = border;
                        if (IsUnderImportSidebarScrollViewport(s.transform))
                            hb.inward = true;
                        hb.ApplyBorderSettings();
                    }
                }

                // Also apply color to non-Button hover borders (resize handles, input fields, etc).
                var hbs = root.GetComponentsInChildren<UIHoverBorder>(true);
                for (int i = 0; i < hbs.Length; i++)
                {
                    var hb = hbs[i];
                    if (hb == null) continue;
                    hb.hoverColor = border;
                    if (IsUnderImportSidebarScrollViewport(hb.transform))
                        hb.inward = true;
                    hb.ApplyBorderSettings();
                }
            }
            catch { }
        }

        /// <summary>Obsolete name — use <see cref="ApplyGalleryPaneHoverPolicy"/>.</summary>
        public static void EnforceBorderHoverForAllButtons(GameObject root)
        {
            ApplyGalleryPaneHoverPolicy(root);
        }
        
        private static List<string> BuildSceneLoadUidAllowList(FileEntry entry, List<string> movedUids)
        {
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void addUidFromPath(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return;
                string p = raw.Replace('\\', '/').Trim();
                if (p.Length == 0) return;
                int sep = p.IndexOf(":/", StringComparison.Ordinal);
                if (sep <= 0) return;
                // Ignore Windows drive paths like C:/...
                if (sep == 1 && char.IsLetter(p[0])) return;
                string uid = p.Substring(0, sep);
                if (!string.IsNullOrEmpty(uid)) needed.Add(uid);
            }

            try
            {
                foreach (var uid in SceneLoadingUtils.CollectReferencedPackageUids(entry))
                {
                    if (!string.IsNullOrEmpty(uid)) needed.Add(uid);
                }
            }
            catch { }

            if (movedUids != null)
            {
                for (int i = 0; i < movedUids.Count; i++)
                {
                    string uid = movedUids[i];
                    if (!string.IsNullOrEmpty(uid)) needed.Add(uid);
                }
            }

            // History rows can be lazy/deferred and dependency parsing may fail before package resolution.
            // Always include the host package UID from entry identifiers as fallback.
            try
            {
                if (entry != null)
                {
                    addUidFromPath(entry.Uid);
                    addUidFromPath(entry.Path);
                }
            }
            catch { }

            return needed.ToList();
        }

        private static List<string> ApplyTemporarySceneLoadWhitelist(FileEntry entry, List<string> movedUids)
        {
            try
            {
                if (!ScanWhitelistManager.Instance.IsEnabled) return null;

                List<string> needed = BuildSceneLoadUidAllowList(entry, movedUids);
                if (needed == null || needed.Count == 0) return null;

                List<string> added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(needed);
                if (added != null && added.Count > 0)
                {
                    LogUtil.Log("[VPB ScanWhitelist] Temporary scene-load allow-list: +" + string.Join(", ", added.ToArray()));
                    try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
                }
                return added;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB ScanWhitelist] Temporary allow-list apply failed: " + ex.Message);
                return null;
            }
        }

        private static void RemoveTemporarySceneLoadWhitelist(List<string> temporaryUids)
        {
            if (temporaryUids == null || temporaryUids.Count == 0) return;
            try
            {
                ScanWhitelistManager.Instance.RemoveTemporaryUidOverrides(temporaryUids);
                LogUtil.Log("[VPB ScanWhitelist] Temporary scene-load allow-list removed: -" + string.Join(", ", temporaryUids.ToArray()));
                try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB ScanWhitelist] Temporary allow-list removal failed: " + ex.Message);
            }
        }

        private static void TryRefreshEntryDisplayPathAfterVarMoves(FileEntry entry)
        {
            if (entry == null) return;
            try
            {
                if (entry is VarFileEntry vfe)
                {
                    vfe.TryRefreshPathsFromLivePackage();
                }
                else if (entry is PackageListEntry ple)
                {
                    ple.RefreshPathsFromPackage();
                }
                else if (entry is SystemFileEntry sfe)
                {
                    if (sfe.isVar && sfe.package != null)
                        sfe.RefreshVarDisplayPathFromPackage();
                }
            }
            catch { }
        }

        private sealed class SceneLoadCleanupState
        {
            public List<string> TemporaryUidOverrides;
            public float SuppressionStartRealtime;
            public int SceneLoadTotalSerialAtStart;
            private bool _done;

            public bool TryMarkDone()
            {
                if (_done) return false;
                _done = true;
                return true;
            }
        }

        private static void FinalizeSceneLoadCleanup(SceneLoadCleanupState state, string reason, bool asWarning = false)
        {
            if (state == null || !state.TryMarkDone()) return;

            float waited = Time.realtimeSinceStartup - state.SuppressionStartRealtime;
            string msg = $"[VPB] DisableSuppressionAfterSceneLoad: {reason} after {waited:0.00}s, disabling suppression";
            if (asWarning) LogUtil.LogWarning(msg);
            else LogUtil.Log(msg);

            RemoveTemporarySceneLoadWhitelist(state.TemporaryUidOverrides);
            Gallery.SuppressAutoRefresh(false);
        }

        private static IEnumerator DisableSuppressionAfterSceneLoad(SceneLoadCleanupState cleanupState)
        {
            LogUtil.Log("[VPB] DisableSuppressionAfterSceneLoad: Waiting for scene to finish loading...");
            int startSerial = cleanupState != null ? cleanupState.SceneLoadTotalSerialAtStart : LogUtil.GetSceneLoadTotalSerial();
            float timeout = 60f; // Max 60 seconds
            float elapsed = 0f;
            bool completedBySceneTotal = false;

            while (elapsed < timeout)
            {
                if (LogUtil.GetSceneLoadTotalSerial() != startSerial)
                {
                    completedBySceneTotal = true;
                    break;
                }
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (completedBySceneTotal)
            {
                yield return null; // allow one frame for end-of-load side effects
                FinalizeSceneLoadCleanup(cleanupState, "scene total ended");
                yield break;
            }

            // Fallback for edge cases where scene-total auto-end is not reached in time.
            if (LogUtil.IsSceneLoading())
                FinalizeSceneLoadCleanup(cleanupState, "scene-load-total signal timeout reached (cleanup fallback)", true);
            else
                FinalizeSceneLoadCleanup(cleanupState, "scene loading flag cleared (fallback)");
        }

        public static IEnumerator DisableSuppressionAfterDelay(float delay)
        {
            LogUtil.Log($"[VPB] DisableSuppressionAfterDelay: Waiting {delay}s before disabling suppression...");
            yield return new WaitForSeconds(delay);
            LogUtil.Log("[VPB] DisableSuppressionAfterDelay: Delay complete, disabling suppression");
            Gallery.SuppressAutoRefresh(false);
        }

        public static bool EnsureInstalled(FileEntry entry)
        {
            return EnsureInstalled(entry, null);
        }

        public static bool EnsureInstalled(FileEntry entry, List<string> outMovedPackageUids)
        {
            if (entry == null) return false;
            try
            {
                return SceneLoadingUtils.EnsureInstalled(entry, outMovedPackageUids);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public static void LoadSceneFile(FileEntry entry)
        {
            LoadSceneFile(entry, null);
        }

        public static void LoadSceneFile(FileEntry entry, GalleryPanel panel)
        {
            if (entry == null) return;

            // Guard against duplicate triggers in the same click/frame burst.
            if (!TryBeginSceneLoadThrottle())
            {
                LogUtil.LogWarning("[VPB] UI.LoadSceneFile ignored (throttled)");
                return;
            }

            // History: record only this scene entry (not EnsureInstalled / dependency work below).
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(entry), "scene"); } catch { }

            if (Messager.singleton == null)
            {
                LogUtil.LogWarning("[VPB] Messager.singleton is null, cannot start scene load coroutine");
                return;
            }

            Messager.singleton.StartCoroutine(LoadSceneFileRoutine(entry, panel));
        }

        private static string SceneLoadBannerName(FileEntry entry)
        {
            try
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Name))
                    return entry.Name;
            }
            catch { }
            return "scene";
        }

        private static void EndSceneLoadBanner()
        {
            try { VpbProgressService.EndSceneLoad(); } catch { }
        }

        private static bool TryBeginSceneLoadThrottle()
        {
            float now = Time.unscaledTime;
            if (now - _lastLoadSceneStartTime < 0.75f)
                return false;
            _lastLoadSceneStartTime = now;
            return true;
        }

        private sealed class SceneLoadPrepOutcome
        {
            public bool Success;
            public bool DepsChanged;
            public SceneLoadingUtils.EnsureInstalledResult EnsureResult;
            public List<string> MovedUids;
            public string NormalizedPath;
            public SceneLoadCleanupState CleanupState;
        }

        /// <summary>
        /// Shared ensure / whitelist / refresh / rewrite path for gallery scene load and merge.
        /// </summary>
        private static IEnumerator PrepareSceneEntryCoroutine(
            FileEntry entry,
            GalleryPanel panel,
            string path,
            bool suppressGalleryRefresh,
            RefreshScope depsChangedRefreshScope,
            string refreshReasonInstalled,
            string refreshReasonAllowlist,
            SceneLoadPrepOutcome outcome)
        {
            outcome.Success = false;
            outcome.DepsChanged = false;
            outcome.EnsureResult = default(SceneLoadingUtils.EnsureInstalledResult);
            outcome.MovedUids = null;
            outcome.NormalizedPath = null;
            outcome.CleanupState = null;

            if (suppressGalleryRefresh)
            {
                Gallery.SuppressAutoRefresh(true);
                outcome.CleanupState = new SceneLoadCleanupState
                {
                    SuppressionStartRealtime = Time.realtimeSinceStartup,
                    SceneLoadTotalSerialAtStart = LogUtil.GetSceneLoadTotalSerial()
                };
            }

            try { VpbProgressService.ReportSceneLoadPrepPhase("Checking dependencies"); } catch { }
            yield return null;

            outcome.MovedUids = new List<string>(32);
            bool ensureDone = false;
            yield return SceneLoadingUtils.EnsureInstalledDetailedCoroutine(entry, outcome.MovedUids, r =>
            {
                outcome.EnsureResult = r;
                ensureDone = true;
            });
            if (!ensureDone)
                outcome.EnsureResult = default(SceneLoadingUtils.EnsureInstalledResult);

            outcome.DepsChanged = outcome.EnsureResult.DepsChanged;
            yield return null;

            List<string> temporaryUidOverrides = ApplyTemporarySceneLoadWhitelist(entry, outcome.MovedUids);
            if (outcome.CleanupState != null)
                outcome.CleanupState.TemporaryUidOverrides = temporaryUidOverrides;
            bool hasTemporaryAllowList = temporaryUidOverrides != null && temporaryUidOverrides.Count > 0;
            bool packageStateChanged = outcome.DepsChanged || hasTemporaryAllowList;

            LogUtil.Log("[VPB] UI.EnsureInstalled (with dependency scan) depsChanged:" + outcome.DepsChanged
                + " missing:" + outcome.EnsureResult.MissingCount + "/" + outcome.EnsureResult.ReferencedCount
                + " whitelistChanged:" + hasTemporaryAllowList
                + " packageStateChanged:" + packageStateChanged);
            if (outcome.EnsureResult.IsDegraded)
                LogUtil.LogWarning("[VPB] Scene load will continue in DEGRADED mode: missing "
                    + outcome.EnsureResult.MissingCount + "/" + outcome.EnsureResult.ReferencedCount + " referenced package(s)");
            else if (!outcome.DepsChanged)
                LogUtil.Log("[VPB] UI.EnsureInstalled: no package moves detected.");

            if (packageStateChanged)
            {
                if (outcome.DepsChanged) LogUtil.Log("[VPB] Refreshing FileManagers...");
                else LogUtil.Log("[VPB] Refreshing VaM FileManager for temporary scene-load allow-list...");

                try { VpbProgressService.ReportSceneLoadPrepPhase("Refreshing package catalog"); } catch { }
                yield return null;

                RefreshScope refreshScope;
                List<string> refreshUids = outcome.MovedUids;
                if (refreshUids == null || refreshUids.Count == 0)
                {
                    string hostUid = null;
                    try
                    {
                        if (entry is VarFileEntry vfe)
                            hostUid = vfe.GetRowPackageUid();
                        else if (entry is PackageListEntry ple && ple.Package != null)
                            hostUid = ple.Package.Uid;
                        else if (entry is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
                            hostUid = sfe.package.Uid;
                    }
                    catch { hostUid = null; }

                    if (!string.IsNullOrEmpty(hostUid))
                        refreshUids = new List<string>(1) { hostUid };
                }

                if (outcome.DepsChanged)
                {
                    refreshScope = (refreshUids != null && refreshUids.Count > 0)
                        ? depsChangedRefreshScope
                        : RefreshScope.Both;
                }
                else
                {
                    refreshScope = RefreshScope.NativeOnly;
                }

                string refreshReason = outcome.DepsChanged ? refreshReasonInstalled : refreshReasonAllowlist;
                string refreshError = null;
                IEnumerator refreshCo = FileManagerBridge.RefreshForSceneLoadCoroutine(
                    refreshReason,
                    refreshScope,
                    refreshUids);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = refreshCo.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        refreshError = ex.Message;
                        break;
                    }

                    if (!hasNext)
                        break;

                    yield return refreshCo.Current;
                }

                if (refreshError != null)
                {
                    LogUtil.LogError("[VPB] EnsureInstalled or FileManager refresh error: " + refreshError);
                    if (outcome.CleanupState != null)
                        FinalizeSceneLoadCleanup(outcome.CleanupState, "install/refresh error");
                    yield break;
                }

                yield return null;

                TryRefreshEntryDisplayPathAfterVarMoves(entry);
                try { if (panel != null) panel.SetHoverPath(entry); } catch { }
            }

            try { VpbProgressService.ReportSceneLoadPrepPhase("Preparing scene file"); } catch { }
            yield return null;

            string normalizedPath = UI.NormalizePath(path);
            try
            {
                if (SceneLoadingUtils.TryPrepareLocalSceneForLoad(entry, out string rewritten))
                {
                    normalizedPath = UI.NormalizePath(rewritten);
                    LogUtil.Log("[VPB] Using rewritten scene: " + normalizedPath);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Scene rewrite skipped due to error: " + ex.Message);
            }

            LogUtil.Log("[VPB] Normalized path: " + normalizedPath);
            outcome.NormalizedPath = normalizedPath;
            outcome.Success = true;
        }

        private static void ScheduleSceneLoadBannerFallback()
        {
            if (Messager.singleton == null) return;
            try
            {
                Messager.singleton.StartCoroutine(SceneLoadBannerFallbackRoutine(LogUtil.GetSceneLoadTotalSerial()));
            }
            catch { }
        }

        /// <summary>
        /// Clears scene banner when merge loads skip WorldUI.Activate / EndSceneLoadTotal.
        /// </summary>
        private static IEnumerator SceneLoadBannerFallbackRoutine(int serialAtStart, float timeoutSec = 180f)
        {
            yield return null;
            yield return null;

            float elapsed = 0f;
            int quietFrames = 0;
            while (elapsed < timeoutSec)
            {
                try
                {
                    if (!VpbProgressService.IsSceneLoadBannerActive)
                        yield break;
                    if (LogUtil.GetSceneLoadTotalSerial() != serialAtStart)
                        yield break;
                }
                catch { yield break; }

                bool busy = false;
                try { busy = LogUtil.IsSceneLoading(); } catch { busy = false; }
                if (!busy)
                {
                    quietFrames++;
                    if (quietFrames >= 8)
                    {
                        EndSceneLoadBanner();
                        yield break;
                    }
                }
                else
                {
                    quietFrames = 0;
                }

                yield return new WaitForSeconds(0.12f);
                elapsed += 0.12f;
            }

            EndSceneLoadBanner();
        }

        private static IEnumerator InvokeSceneLoadCoroutine(
            string normalizedPath,
            bool merge,
            SceneLoadCleanupState cleanupState,
            bool collapseGalleryPanels,
            bool deferOneFrameBeforeLoad)
        {
            if (deferOneFrameBeforeLoad)
                yield return null;

            SuperController sc = SuperController.singleton;
            if (sc == null)
            {
                LogUtil.LogError("[VPB] SuperController.singleton is null!");
                if (cleanupState != null)
                    FinalizeSceneLoadCleanup(cleanupState, "supercontroller unavailable");
                EndSceneLoadBanner();
                yield break;
            }

            if (cleanupState != null && Messager.singleton != null)
                Messager.singleton.StartCoroutine(DisableSuppressionAfterSceneLoad(cleanupState));
            if (collapseGalleryPanels)
            {
                try { Gallery.CollapsePanelsOnSceneLaunch(); } catch { }
            }

            try { VpbProgressService.HandoffSceneLoadNative(merge); } catch { }
            ScheduleSceneLoadBannerFallback();
            yield return null;

            int serialBefore = LogUtil.GetSceneLoadTotalSerial();
            LogUtil.Log("[VPB] Calling scene load: " + normalizedPath + (merge ? " (merge)" : ""));
            bool ok = false;
            if (merge)
                ok = SceneLoadingUtils.LoadScene(normalizedPath, true);
            else
                sc.Load(normalizedPath);

            if (merge && !ok)
            {
                LogUtil.LogError("[VPB] Scene merge load returned false");
                if (cleanupState != null)
                    FinalizeSceneLoadCleanup(cleanupState, "merge load failed");
                EndSceneLoadBanner();
                yield break;
            }

            if (!merge && LogUtil.GetSceneLoadTotalSerial() == serialBefore && !LogUtil.IsSceneLoading())
            {
                yield return null;
                if (LogUtil.GetSceneLoadTotalSerial() == serialBefore && !LogUtil.IsSceneLoading())
                {
                    LogUtil.LogWarning("[VPB] sc.Load did not start scene load — clearing banner");
                    EndSceneLoadBanner();
                }
            }
        }

        public static void MergeSceneFile(FileEntry entry, string path, GalleryPanel panel, bool atPlayer, UIDraggableItem dragger = null)
        {
            if (entry == null && string.IsNullOrEmpty(path)) return;
            if (!TryBeginSceneLoadThrottle())
            {
                LogUtil.LogWarning("[VPB] UI.MergeSceneFile ignored (throttled)");
                return;
            }
            if (Messager.singleton == null)
            {
                LogUtil.LogWarning("[VPB] Messager.singleton is null, cannot start merge scene coroutine");
                return;
            }
            Messager.singleton.StartCoroutine(MergeSceneFileRoutine(entry, path, panel, atPlayer, dragger));
        }

        private static IEnumerator MergeSceneFileRoutine(FileEntry entry, string path, GalleryPanel panel, bool atPlayer, UIDraggableItem dragger)
        {
            if (entry == null && !string.IsNullOrEmpty(path))
            {
                try { entry = FileManager.GetFileEntry(path); } catch { entry = null; }
            }
            if (entry == null)
            {
                LogUtil.LogError("[VPB] MergeSceneFile: no FileEntry for " + path);
                yield break;
            }

            LogUtil.Log("[VPB] MergeSceneFile started: " + path + " (atPlayer: " + atPlayer + ")");
            try
            {
                if (!LogUtil.IsSceneClickActive())
                    LogUtil.BeginSceneClick(path);
            }
            catch { }

            try { VpbProgressService.BeginSceneLoadPrep(SceneLoadBannerName(entry), "Merging"); } catch { }
            yield return null;

            var prep = new SceneLoadPrepOutcome();
            yield return PrepareSceneEntryCoroutine(
                entry,
                panel,
                path,
                suppressGalleryRefresh: false,
                depsChangedRefreshScope: RefreshScope.Both,
                refreshReasonInstalled: "dragdrop_merge_scene",
                refreshReasonAllowlist: "dragdrop_merge_scene",
                prep);

            if (!prep.Success || string.IsNullOrEmpty(prep.NormalizedPath))
            {
                EndSceneLoadBanner();
                yield break;
            }

            HashSet<string> atomsBefore = null;
            if (atPlayer)
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    atomsBefore = new HashSet<string>();
                    foreach (Atom a in sc.GetAtoms())
                    {
                        if (a != null) atomsBefore.Add(a.uid);
                    }
                }
            }

            yield return InvokeSceneLoadCoroutine(
                prep.NormalizedPath,
                merge: true,
                cleanupState: null,
                collapseGalleryPanels: false,
                deferOneFrameBeforeLoad: prep.DepsChanged);

            if (atPlayer && atomsBefore != null && dragger != null)
            {
                if (panel != null)
                    panel.StartCoroutine(dragger.RunTeleportMergedAtomsToPlayer(atomsBefore));
                else
                    dragger.StartCoroutine(dragger.RunTeleportMergedAtomsToPlayer(atomsBefore));
            }
        }

        private static IEnumerator LoadSceneFileRoutine(FileEntry entry, GalleryPanel panel)
        {
            string path = entry.Uid;
            LogUtil.Log("[VPB] UI.LoadSceneFile started for: " + path);

            try
            {
                if (!LogUtil.IsSceneClickActive())
                    LogUtil.BeginSceneClick(path);
            }
            catch { }

            try { VpbProgressService.BeginSceneLoadPrep(SceneLoadBannerName(entry)); } catch { }
            yield return null;

            var prep = new SceneLoadPrepOutcome();
            yield return PrepareSceneEntryCoroutine(
                entry,
                panel,
                path,
                suppressGalleryRefresh: true,
                depsChangedRefreshScope: RefreshScope.InstallOnly,
                refreshReasonInstalled: "gallery_ensure_installed",
                refreshReasonAllowlist: "gallery_scene_allowlist",
                prep);

            if (!prep.Success || string.IsNullOrEmpty(prep.NormalizedPath))
            {
                EndSceneLoadBanner();
                yield break;
            }

            yield return InvokeSceneLoadCoroutine(
                prep.NormalizedPath,
                merge: false,
                cleanupState: prep.CleanupState,
                collapseGalleryPanels: true,
                deferOneFrameBeforeLoad: prep.DepsChanged);
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            try
            {
                // FileManager.NormalizePath is more reliable in this codebase
                return FileManager.NormalizePath(path);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] FileManager.NormalizePath error: {ex.Message}");
            }
                
            string normalizedPath = path.Replace('\\', '/');
            try
            {
                string currentDir = Directory.GetCurrentDirectory().Replace('\\', '/');
                
                if (normalizedPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = normalizedPath.Substring(currentDir.Length);
                    if (normalizedPath.StartsWith("/")) normalizedPath = normalizedPath.Substring(1);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.NormalizePath fallback error: {ex.Message}");
            }
            return normalizedPath;
        }

        /// <summary>
        /// True for VaM package paths (creator.pkg.version:/internal), false for Windows drive paths (C:/...) and http(s) URLs.
        /// </summary>
        private static bool LooksLikeVarPackagePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            int i = p.IndexOf(":/", StringComparison.Ordinal);
            if (i < 0) return false;
            // Windows: "C:/Users/..." has ':' at index 1 after normalizing slashes
            if (i == 1 && p.Length > 2 && char.IsLetter(p[0])) return false;
            return true;
        }

        /// <summary>
        /// Use instead of raw <c>path.Contains(":")</c> so Windows drives and URLs are not mistaken for VAR references.
        /// </summary>
        public static bool IsLikelyVarPackageReference(string path)
        {
            return LooksLikeVarPackagePath(path);
        }

        /// <summary>
        /// Whether <paramref name="entry"/> refers to the same file as <paramref name="path"/> (any of path / Uid / normalized forms).
        /// </summary>
        private static bool FileEntryMatchesPathForJsonLoad(FileEntry entry, string path)
        {
            if (entry == null || string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            string uid = entry.Uid?.Replace('\\', '/');
            string ep = entry.Path?.Replace('\\', '/');
            if (string.Equals(uid, p, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(ep, p, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string norm = FileManager.NormalizePath(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(norm))
                {
                    if (string.Equals(uid, norm, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(ep, norm, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        public static JSONNode LoadJSONWithFallback(string path, FileEntry entry = null)
        {
            if (string.IsNullOrEmpty(path)) return null;

            JSONNode root = null;
            try
            {
                if (SuperController.singleton != null)
                    root = SuperController.singleton.LoadJSON(path);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] SuperController.LoadJSON threw for {path}: {ex.Message}");
                root = null;
            }

            if (root != null) return root;

            LogUtil.LogWarning($"[VPB] LoadJSONWithFallback: primary load failed for {path}, trying VPB stream/file fallback...");
            string content = null;
            FileEntry readEntry = null;

            try
            {
                // Selected VarFileEntry row: read this file from the .var directly. Do not require the
                // virtual path string to match entry.Path/Uid (spacing/slashes often differ from rebuilt paths).
                if (entry is VarFileEntry directVfe)
                {
                    try
                    {
                        using (var reader = directVfe.OpenStreamReader())
                        {
                            if (reader != null)
                            {
                                string directContent = reader.ReadToEnd();
                                if (!string.IsNullOrEmpty(directContent))
                                {
                                    content = directContent;
                                    readEntry = directVfe;
                                }
                            }
                        }
                    }
                    catch (Exception exDirect)
                    {
                        LogUtil.LogVerboseUi($"[VPB] LoadJSONWithFallback: direct VarFileEntry read skipped: {exDirect.Message}");
                    }
                }

                if (string.IsNullOrEmpty(content))
                {
                    if (entry != null && FileEntryMatchesPathForJsonLoad(entry, path))
                        readEntry = entry;
                    else
                    {
                        VarFileEntry vfe = FileManager.GetVarFileEntry(path);
                        if (vfe == null)
                        {
                            try
                            {
                                string norm = FileManager.NormalizePath(path);
                                if (!string.IsNullOrEmpty(norm))
                                    vfe = FileManager.GetVarFileEntry(norm);
                            }
                            catch { }
                        }
                        readEntry = vfe;
                    }

                    if (readEntry != null && string.IsNullOrEmpty(content))
                    {
                        using (var reader = readEntry.OpenStreamReader())
                        {
                            if (reader != null)
                                content = reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning($"[VPB] LoadJSONWithFallback stream read failed for {path}: {ex.Message}");
            }

            // Loose file on disk (not a package-internal path; exclude Windows drive letters)
            if (string.IsNullOrEmpty(content))
            {
                string check = path.Replace('\\', '/');
                if (!LooksLikeVarPackagePath(check))
                {
                    try
                    {
                        if (File.Exists(path))
                            content = File.ReadAllText(path);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[VPB] LoadJSONWithFallback file read failed for {path}: {ex.Message}");
                    }
                }
            }

            if (string.IsNullOrEmpty(content)) return null;

            VarFileEntry varForSelf = readEntry as VarFileEntry;
            if (varForSelf?.Package != null)
            {
                string packageUid = varForSelf.Package.Uid;
                content = content.Replace("SELF:/", packageUid + ":/");
                content = content.Replace("SELF:\\", packageUid + ":/");
            }

            try
            {
                return JSON.Parse(content);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] LoadJSONWithFallback: JSON parse failed for {path}: {ex.Message}");
                return null;
            }
        }

        public static GameObject CreateVScrollableContent(GameObject parentGO, Color backgroundColor, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float scrollBarWidth = 15f, float spacing = 0f, bool addBottomFlexSpacer = true)
        {
            GameObject scrollableContentGO = AddChildGOImage(parentGO, backgroundColor, anchorPreset, horizontalSize, verticalSize, anchoredPositionOffset);

            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollableContentGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = new Vector2(-scrollBarWidth, 0);
            viewportRT.anchoredPosition = new Vector2(-scrollBarWidth * 0.5f, 0);
            viewportGO.AddComponent<RectMask2D>();

            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = AddVLG(contentGO, spacing: spacing);

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (addBottomFlexSpacer)
            {
                // Main grid only: lets short lists fill the viewport. Sub-tab lists stay tight to the last row.
                GameObject spacer = new GameObject("BottomSpacer");
                spacer.transform.SetParent(contentGO.transform, false);
                LayoutElement le = AddLE(spacer, preferredHeight: 0, flexibleHeight: 10000);
            }

            GameObject scrollbarGO = CreateScrollBar(scrollableContentGO, scrollBarWidth, verticalSize, Scrollbar.Direction.BottomToTop);
            Scrollbar scrollbar = scrollbarGO.GetComponent<Scrollbar>();

            ScrollRect scrollRect = scrollableContentGO.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            // IMPORTANT: Do NOT assign scrollRect.verticalScrollbar directly, as it triggers Unity's 
            // internal auto-sizing which causes 1px flickering with large content heights.
            // We use ScrollbarSync instead to handle synchronization manually.
            scrollRect.verticalScrollbar = null; 
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = scrollRect;
            sync.scrollbar = scrollbar;
            sync.minSizePixels = 30f;

            return scrollableContentGO;
        }

        private static Image AddScrollbarGraphic(GameObject go, Color color)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.cornerRadiusFraction = ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        public static GameObject CreateScrollBar(GameObject parentGO, float width, float height, Scrollbar.Direction direction)
        {
            GameObject scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(parentGO.transform, false);
            RectTransform rt = scrollbarGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);

            Image bg = AddScrollbarGraphic(scrollbarGO, new Color(0.2f, 0.2f, 0.2f, 0.5f));

            Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.direction = direction;
            scrollbar.interactable = true;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.transition = Selectable.Transition.None;

            // Ensure the scrollbar is not blocked by a parent CanvasGroup
            CanvasGroup cg = scrollbarGO.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;
            cg.interactable = true;

            GameObject slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(scrollbarGO.transform, false);
            RectTransform slidingRT = slidingArea.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRT = handle.AddComponent<RectTransform>();
            handleRT.sizeDelta = Vector2.zero;
            Image handleImg = AddScrollbarGraphic(handle, new Color(0.6f, 0.6f, 0.6f, 1f));

            scrollbar.handleRect = handleRT;
            scrollbar.targetGraphic = handleImg;

            // Add BoxCollider to ensure reliable hit detection in 3D space
            var bc = scrollbarGO.AddComponent<BoxCollider>();
            bc.size = new Vector3(width, height > 0 ? height : 800f, 1f);
            bc.center = new Vector3(-width / 2, 0, 0); // Pivot is (1, 0.5)
            // UI collider must not participate in physics collisions with scene atoms.
            bc.isTrigger = true;

            return scrollbarGO;
        }

        public static float ResolveGalleryElementCornerRadiusFraction()
        {
            try
            {
                if (VPBConfig.Instance != null)
                    return VPBConfig.Instance.EffectiveGalleryElementCornerRadiusFraction();
            }
            catch { }
            return GalleryUiDesignTokens.ButtonCornerRadiusFraction;
        }

        /// <summary>Rounded fill for gallery buttons, rows, and input chrome — uses live corner-radius setting.</summary>
        public static Image AddGalleryElementRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private static bool IsLiveSceneUiComponent(Component c)
        {
            if (c == null || c.gameObject == null) return false;
            HideFlags hf = c.hideFlags;
            if ((hf & HideFlags.NotEditable) != 0) return false;
            if ((hf & HideFlags.HideAndDontSave) != 0) return false;
            return true;
        }

        /// <summary>Re-applies the configured corner radius to every live <see cref="RoundedRect"/> / <see cref="RoundedRectOutline"/>.</summary>
        public static void ApplyGalleryElementCornerRadiusGlobally()
        {
            float frac = ResolveGalleryElementCornerRadiusFraction();
            try
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(RoundedRect));
                for (int i = 0; i < all.Length; i++)
                {
                    RoundedRect rr = all[i] as RoundedRect;
                    if (rr == null || !IsLiveSceneUiComponent(rr)) continue;
                    rr.cornerRadiusFraction = frac;
                }
            }
            catch
            {
                try
                {
                    RoundedRect[] fills = UnityEngine.Object.FindObjectsOfType<RoundedRect>();
                    for (int i = 0; i < fills.Length; i++)
                    {
                        if (fills[i] != null) fills[i].cornerRadiusFraction = frac;
                    }
                }
                catch { }
            }
            try
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(RoundedRectOutline));
                for (int i = 0; i < all.Length; i++)
                {
                    RoundedRectOutline outline = all[i] as RoundedRectOutline;
                    if (outline == null || !IsLiveSceneUiComponent(outline)) continue;
                    outline.cornerRadiusFraction = frac;
                }
            }
            catch
            {
                try
                {
                    RoundedRectOutline[] outlines = UnityEngine.Object.FindObjectsOfType<RoundedRectOutline>();
                    for (int i = 0; i < outlines.Length; i++)
                    {
                        if (outlines[i] != null) outlines[i].cornerRadiusFraction = frac;
                    }
                }
                catch { }
            }
            try
            {
                UnityEngine.Object[] borders = Resources.FindObjectsOfTypeAll(typeof(UIHoverBorder));
                for (int i = 0; i < borders.Length; i++)
                {
                    UIHoverBorder hb = borders[i] as UIHoverBorder;
                    if (hb == null || !IsLiveSceneUiComponent(hb)) continue;
                    try { hb.ApplyBorderSettings(); } catch { }
                }
            }
            catch { }
            try { VamHookPlugin.singleton?.SyncQuickMenuElementCornerRadiusLive(); } catch { }
            try
            {
                Gallery g = Gallery.singleton;
                if (g != null && g.Panels != null)
                {
                    for (int i = 0; i < g.Panels.Count; i++)
                    {
                        GalleryPanel p = g.Panels[i];
                        if (p != null) p.SyncLiveElementCornerRadiusChrome();
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Creates a child GameObject with a RectTransform anchored/sized from an <see cref="AnchorPresets"/> preset.
        /// The single primitive behind the image/label/row factories — folds the repeated
        /// new GameObject + SetParent + AddComponent&lt;RectTransform&gt; + GetAnchorMin/Max/Pivot boilerplate.
        /// </summary>
        public static GameObject CreateChildRT(GameObject parentGO, string name, int anchorPreset = AnchorPresets.stretchAll, Vector2 size = default(Vector2), Vector2 anchoredPosition = default(Vector2))
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parentGO.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return go;
        }

        public static GameObject AddChildGOImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, bool rounded = false)
        {
            // RectTransform is pre-added by CreateChildRT; AddComponent<Image> reuses it (Graphic requires RectTransform).
            // RoundedRect is an Image subclass; with cornerRadius 0 it renders an identical quad,
            // so callers/lookups via GetComponent<Image>() are unaffected until a radius is set.
            GameObject go = CreateChildRT(parentGO, "Image", anchorPreset, new Vector2(horizontalSize, verticalSize), anchoredPositionOffset);
            Image img = rounded ? go.AddComponent<RoundedRect>() : go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = true;

            if (rounded)
            {
                RoundedRect rr = img as RoundedRect;
                if (rr != null)
                    rr.cornerRadiusFraction = ResolveGalleryElementCornerRadiusFraction();
            }

            return go;
        }

        public static GameObject AddChildGOChamferedImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float chamferSize = 20f)
        {
            GameObject go = CreateChildRT(parentGO, "ChamferedImage", anchorPreset, new Vector2(horizontalSize, verticalSize), anchoredPositionOffset);
            ChamferedRect img = go.AddComponent<ChamferedRect>();
            img.color = color;
            img.chamferSize = chamferSize;

            return go;
        }

        /// <summary>Adds an Image to an existing GameObject, setting color + raycastTarget — folds the pervasive
        /// AddComponent&lt;Image&gt;(); img.color=..; img.raycastTarget=..; pattern. Unity's default raycastTarget is
        /// true, so color-only sites (no raycastTarget line) fold safely with the default.</summary>
        public static Image AddImage(GameObject go, Color color, bool raycastTarget = true)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        /// <summary>Scaled <see cref="RectOffset"/> — folds the pervasive new RectOffset(RoundToInt(x*s), ...) pattern.</summary>
        public static RectOffset Pad(float left, float right, float top, float bottom, float scale = 1f)
        {
            return new RectOffset(
                Mathf.RoundToInt(left * scale),
                Mathf.RoundToInt(right * scale),
                Mathf.RoundToInt(top * scale),
                Mathf.RoundToInt(bottom * scale));
        }

        /// <summary>Adds a <see cref="VerticalLayoutGroup"/>. Defaults match the common gallery list column.</summary>
        public static VerticalLayoutGroup AddVLG(GameObject go, float spacing = 0f, RectOffset padding = null, TextAnchor childAlignment = TextAnchor.UpperLeft, bool childControlWidth = true, bool childControlHeight = true, bool childForceExpandWidth = true, bool childForceExpandHeight = false)
        {
            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            if (padding != null) vlg.padding = padding;
            vlg.childAlignment = childAlignment;
            vlg.childControlWidth = childControlWidth;
            vlg.childControlHeight = childControlHeight;
            vlg.childForceExpandWidth = childForceExpandWidth;
            vlg.childForceExpandHeight = childForceExpandHeight;
            return vlg;
        }

        /// <summary>Adds a <see cref="HorizontalLayoutGroup"/>. Defaults match the common gallery row.</summary>
        public static HorizontalLayoutGroup AddHLG(GameObject go, float spacing = 0f, RectOffset padding = null, TextAnchor childAlignment = TextAnchor.MiddleLeft, bool childControlWidth = true, bool childControlHeight = true, bool childForceExpandWidth = true, bool childForceExpandHeight = false)
        {
            HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            if (padding != null) hlg.padding = padding;
            hlg.childAlignment = childAlignment;
            hlg.childControlWidth = childControlWidth;
            hlg.childControlHeight = childControlHeight;
            hlg.childForceExpandWidth = childForceExpandWidth;
            hlg.childForceExpandHeight = childForceExpandHeight;
            return hlg;
        }

        /// <summary>
        /// Adds a <see cref="LayoutElement"/>. Each dimension defaults to -1 (Unity's "ignore this constraint"
        /// sentinel), so omitting an argument leaves that field unset exactly like a hand-rolled AddComponent.
        /// </summary>
        public static LayoutElement AddLE(GameObject go, float minWidth = -1f, float minHeight = -1f, float preferredWidth = -1f, float preferredHeight = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = minWidth;
            le.minHeight = minHeight;
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            le.flexibleHeight = flexibleHeight;
            return le;
        }

        /// <summary>
        /// Creates a gallery text label. Bakes in the Arial builtin font, non-bold style, and VPBUiFont hook.
        /// Optional-parameter DEFAULTS mirror Unity's own <see cref="Text"/> defaults (Wrap/Truncate/UpperLeft,
        /// raycast+richtext on) so omitting an argument reproduces a hand-rolled AddComponent&lt;Text&gt; site exactly.
        /// Returns the <see cref="Text"/>; use <c>.rectTransform</c>/<c>.gameObject</c> for further layout tweaks.
        /// </summary>
        public static Text CreateLabel(GameObject parentGO, string text, int fontSize, Color? color = null,
            TextAnchor alignment = TextAnchor.UpperLeft,
            HorizontalWrapMode horizontalWrap = HorizontalWrapMode.Wrap,
            VerticalWrapMode verticalWrap = VerticalWrapMode.Truncate,
            bool raycastTarget = true, bool richText = true,
            int anchorPreset = AnchorPresets.stretchAll, Vector2 size = default(Vector2), Vector2 anchoredPosition = default(Vector2),
            string name = "Text")
        {
            GameObject go = CreateChildRT(parentGO, name, anchorPreset, size, anchoredPosition);
            Text t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Normal;
            t.color = color ?? Color.white;
            t.alignment = alignment;
            t.horizontalOverflow = horizontalWrap;
            t.verticalOverflow = verticalWrap;
            t.raycastTarget = raycastTarget;
            t.supportRichText = richText;
            t.text = text ?? "";
            try { VPBUiFont.ApplyTo(t); } catch { }
            return t;
        }

        /// <summary>
        /// Turns off Unity's <see cref="Selectable"/> transition + keyboard/gamepad navigation on a button
        /// (the pair written inline at dozens of sites). Optionally applies the standard gallery ColorBlock
        /// (white normal, brighter hover, darker press, dimmed disabled) used by rounded chrome buttons.
        /// </summary>
        public static void ConfigButtonFlat(Button btn, bool applyColors = false)
        {
            if (btn == null) return;
            if (applyColors)
            {
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = ButtonHighlight;
                cb.pressedColor = ButtonPressed;
                cb.disabledColor = ButtonDisabled;
                btn.colors = cb;
            }
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        /// <summary>
        /// Creates a stretch-all click-to-dismiss dim layer (black at <paramref name="dimAlpha"/>, transition +
        /// navigation off). Returns the dim GameObject. Used standalone for scrim/blocker overlays and as the
        /// base of <see cref="CreateModalChrome"/>.
        /// </summary>
        public static GameObject CreateDimBlocker(GameObject parentGO, string name, UnityAction onDismiss, float dimAlpha = GalleryUiDesignTokens.ModalDimAlpha)
        {
            GameObject dim = CreateChildRT(parentGO, name, AnchorPresets.stretchAll);
            Image dimImg = AddImage(dim, Black(dimAlpha));
            Button dimBtn = dim.AddComponent<Button>();
            ConfigButtonFlat(dimBtn);
            if (onDismiss != null) dimBtn.onClick.AddListener(onDismiss);
            return dim;
        }

        /// <summary>
        /// Builds the standard full-screen modal scaffold: a stretch-all root, a click-to-dismiss dim layer
        /// (black at <paramref name="dimAlpha"/>, transition/navigation off), and a centered panel of the given
        /// size + background. Returns the root; the panel is returned via <paramref name="panelGO"/> for the
        /// caller to attach its own layout group / click blocker / content.
        /// </summary>
        public static GameObject CreateModalChrome(GameObject parentGO, string name, float panelWidth, float panelHeight, Color panelBg, UnityAction onDismiss, out GameObject panelGO, float dimAlpha = GalleryUiDesignTokens.ModalDimAlpha)
        {
            GameObject root = CreateChildRT(parentGO, name, AnchorPresets.stretchAll);

            CreateDimBlocker(root, "Dim", onDismiss, dimAlpha);

            panelGO = CreateChildRT(root, "Panel", AnchorPresets.middleCenter, new Vector2(panelWidth, panelHeight));
            Image pbg = AddImage(panelGO, panelBg);

            return root;
        }

        public static GameObject CreateUIButton(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction onClick)
        {
            // Rounded background. Fraction-of-size radius is scale-resistant (re-derived from the live
            // rect on every resize) and uniform across every gallery button.
            GameObject buttonGO = AddChildGOImage(parentGO, ChromePanel, anchorPreset, width, height, new Vector2(xOffset, yOffset), rounded: true);
            buttonGO.name = "Button_" + label;
            RoundedRect bgRounded = buttonGO.GetComponent<RoundedRect>();
            if (bgRounded != null) bgRounded.cornerRadiusFraction = ResolveGalleryElementCornerRadiusFraction();
            Button btn = buttonGO.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Standard gallery button ColorBlock + no transition/navigation (white normalColor keeps the
            // RoundedRect fill; hover brightens, press darkens, disabled dims).
            ConfigButtonFlat(btn, applyColors: true);

            CreateLabel(buttonGO, label, fontSize, TextPrimary, TextAnchor.MiddleCenter, name: "Text");

            // Add Hover Border
            buttonGO.AddComponent<UIHoverBorder>();

            return buttonGO;
        }

        /// <summary>
        /// Square trailing control for optional actions on gallery side-tab rows (rename today; other categories later).
        /// Uses a fixed <paramref name="edgeLengthPx"/> for both axes so layout groups cannot collapse one dimension.
        /// Pair <paramref name="edgeLengthPx"/> with the same value used for the row’s tab height (e.g. 35 × InnerPaneScale).
        /// </summary>
        public static GameObject CreateSideTabSquareIconButton(GameObject rowParent, float edgeLengthPx, Sprite icon, UnityAction onClick, Color backdrop, float iconPadding)
        {
            GameObject go = new GameObject("SideTabSquareIcon");
            go.transform.SetParent(rowParent.transform, false);
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = backdrop;
            rr.raycastTarget = true;
            rr.cornerRadiusFraction = ResolveGalleryElementCornerRadiusFraction();
            Image img = rr;
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = cb;
            ConfigButtonFlat(btn);
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            go.AddComponent<UIHoverBorder>();

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(edgeLengthPx, edgeLengthPx);

            LayoutElement le = AddLE(go, minWidth: edgeLengthPx, minHeight: edgeLengthPx, preferredWidth: edgeLengthPx, preferredHeight: edgeLengthPx, flexibleWidth: 0f, flexibleHeight: 0f);

            // HorizontalLayoutGroup row height can exceed edgeLengthPx; match width to height so icon stays square.
            AspectRatioFitter arf = go.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            arf.aspectRatio = 1f;

            if (icon != null)
                AddIconToButton(go, icon, iconPadding, backdrop);

            return go;
        }

        private static readonly Dictionary<string, Sprite> _iconSpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // Read-only counter for VpbPerfTelemetry.
        public static int IconSpriteCacheCount { get { return _iconSpriteCache != null ? _iconSpriteCache.Count : 0; } }

        /// <summary>
        /// Pre-loads all PNGs from the vpb_icons/ directory into the sprite cache.
        /// Call this at plugin startup (e.g. from a coroutine in Start) so the cache is warm
        /// before the user first opens the gallery panel.
        /// </summary>
        public static System.Collections.IEnumerator PrewarmIconCacheCoroutine()
        {
            string iconsDir = Path.Combine(BepInEx.Paths.PluginPath, "vpb_icons");
            if (!Directory.Exists(iconsDir)) yield break;

            string[] pngFiles;
            try { pngFiles = Directory.GetFiles(iconsDir, "*.png"); }
            catch { yield break; }

            Color stdColor = new Color(0.78f, 0.78f, 0.78f, 1f);

            foreach (string fullPath in pngFiles)
            {
                try
                {
                    string relPath = "vpb_icons/" + Path.GetFileName(fullPath);
                    // Load uncolored and standard-colored variants
                    LoadIconSprite(relPath);
                    LoadIconSprite(relPath, stdColor);
                }
                catch { }
                yield return null; // Spread across frames to avoid stutter
            }
        }

        public static Sprite LoadIconSprite(string relativePathFromPluginsDir, Color? recolorTo = null)
        {
            try
            {
                string cacheKey = recolorTo.HasValue
                    ? relativePathFromPluginsDir + "|" + recolorTo.Value.r.ToString("F3") + "," + recolorTo.Value.g.ToString("F3") + "," + recolorTo.Value.b.ToString("F3")
                    : relativePathFromPluginsDir;

                if (_iconSpriteCache.TryGetValue(cacheKey, out Sprite cached) && cached != null)
                    return cached;

                string fullPath = Path.Combine(BepInEx.Paths.PluginPath, relativePathFromPluginsDir);
                if (!File.Exists(fullPath)) return null;
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                if (recolorTo.HasValue)
                {
                    Color c = recolorTo.Value;
                    Color[] pixels = tex.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        if (pixels[i].a > 0.05f)
                            pixels[i] = new Color(c.r, c.g, c.b, pixels[i].a);
                    tex.SetPixels(pixels);
                    tex.Apply();
                }
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _iconSpriteCache[cacheKey] = sprite;
                return sprite;
            }
            catch { return null; }
        }

        /// <summary>Standard backdrop applied to every icon button.</summary>
        public static readonly Color IconButtonBackdrop = new Color(0.25f, 0.25f, 0.25f, 1f);

        /// <summary>Recolor passed to <see cref="LoadIconSprite"/> for gallery left/right rail icon PNGs (glyph pixels only).</summary>
        public static readonly Color SideRailIconGlyphTint = Color.white;

        /// <summary>Neutral glyph tint for top/bottom bar icon PNGs (glyph pixels only).</summary>
        public static readonly Color BarIconGlyphTint = Color.white;

        /// <summary>
        /// Adds an icon Image child to <paramref name="buttonGO"/>, hides its text label, and sets
        /// the button's background to <paramref name="backdropOverride"/> (or <see cref="IconButtonBackdrop"/>
        /// when null). Pass an override only for buttons that have a meaningful accent colour (e.g. Hub).
        /// </summary>
        public static void AddIconToButton(GameObject buttonGO, Sprite icon, float padding = 4f, Color? backdropOverride = null)
        {
            // Apply unified backdrop (or explicit override for special-case buttons)
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? IconButtonBackdrop;

            // Hide text — icon replaces it; text remains as fallback when icon is absent
            Text t = buttonGO.GetComponentInChildren<Text>(true);
            if (t != null) t.gameObject.SetActive(false);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = AddImage(iconGO, Color.white);
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(-padding * 2, -padding * 2);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Updates or creates the Icon child from <paramref name="relativePathFromPluginsDir"/> using bar glyph tint.</summary>
        public static void RegisterIconButtonPath(GameObject buttonGO, string relativePathFromPluginsDir, float padding = 4f, Color? backdropOverride = null)
        {
            ApplyBarIconFromPath(buttonGO, relativePathFromPluginsDir, padding, backdropOverride);
        }

        public static bool ApplyBarIconFromPath(GameObject buttonGO, string relativePathFromPluginsDir, float padding = 4f, Color? backdropOverride = null)
        {
            if (buttonGO == null || string.IsNullOrEmpty(relativePathFromPluginsDir)) return false;
            Sprite s = LoadIconSprite(relativePathFromPluginsDir, BarIconGlyphTint);
            if (s == null) return false;
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? IconButtonBackdrop;
            Transform iconTr = buttonGO.transform.Find("Icon");
            if (iconTr != null)
            {
                Image img = iconTr.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = s;
                    img.color = Color.white;
                    return true;
                }
            }
            AddIconToButton(buttonGO, s, padding, backdropOverride);
            return true;
        }

        /// <summary>Like <see cref="ApplyBarIconFromPath"/> but uses <see cref="SideRailIconGlyphTint"/>.</summary>
        public static bool ApplySideRailIconFromPath(GameObject buttonGO, string relativePathFromPluginsDir, float padding = 4f, Color? backdropOverride = null)
        {
            if (buttonGO == null || string.IsNullOrEmpty(relativePathFromPluginsDir)) return false;
            Sprite s = LoadIconSprite(relativePathFromPluginsDir, SideRailIconGlyphTint);
            if (s == null) return false;
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? IconButtonBackdrop;
            Transform iconTr = buttonGO.transform.Find("Icon");
            if (iconTr != null)
            {
                Image img = iconTr.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = s;
                    img.color = Color.white;
                    return true;
                }
            }
            AddIconToButton(buttonGO, s, padding, backdropOverride);
            return true;
        }

        /// <summary>Swaps a live button icon after theme change (tint is baked into <paramref name="sprite"/> pixels).</summary>
        public static void SetButtonIconGlyph(Image iconImage, Sprite sprite)
        {
            if (iconImage == null || sprite == null) return;
            iconImage.sprite = sprite;
            iconImage.color = Color.white;
        }

        public static GameObject CreateUIToggle(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = AddGalleryElementRoundedBg(boxGO, Color.white);
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = AddGalleryElementRoundedBg(innerGO, Color.black, raycastTarget: false);

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); 
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); 
            Image checkImg = AddGalleryElementRoundedBg(checkGO, Color.white, raycastTarget: false);
            toggle.graphic = checkImg;

            Text t = CreateLabel(toggleGO, label, fontSize, Color.white, TextAnchor.MiddleLeft, name: "Label");
            RectTransform labelRT = t.GetComponent<RectTransform>();
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateToggle(GameObject parentGO, string label, float width, float height, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = AddGalleryElementRoundedBg(boxGO, Color.white);
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = AddGalleryElementRoundedBg(innerGO, Color.black, raycastTarget: false);

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); // Parent to inner or box, doesn't matter much if positioned correctly
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); // Slightly smaller to leave a hint of border or full size? Let's use 14 to leave black gap, or 16 for solid. User said "white is selected". Solid white looks best.
            // Actually if I make it 16, it covers the black inner completely, merging with white outer.
            checkRT.sizeDelta = new Vector2(16, 16); 
            Image checkImg = AddGalleryElementRoundedBg(checkGO, Color.white, raycastTarget: false);
            toggle.graphic = checkImg;

            Text t = CreateLabel(toggleGO, label, GalleryUiDesignTokens.FontBodyRef, Color.white, TextAnchor.MiddleLeft, name: "Label");
            RectTransform labelRT = t.GetComponent<RectTransform>();
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateDropdown(GameObject parentGO, string label, float width, float height, List<string> options, int currentIdx, UnityAction<int> onValueChanged)
        {
            GameObject container = AddChildGOImage(parentGO, new Color(0,0,0,0), AnchorPresets.middleCenter, width, height, Vector2.zero);
            
            GameObject btnGO = CreateUIButton(container, width, height, label + ": " + (options.Count > currentIdx ? options[currentIdx] : ""), 14, 0, 0, AnchorPresets.middleCenter, null);
            Button btn = btnGO.GetComponent<Button>();
            Text t = btnGO.GetComponentInChildren<Text>();
            
            // Use a local variable to capture index if possible, but UnityAction works with captured vars
            // We need a wrapper class to hold state if we want it to persist, but for now closure is fine
            int idx = currentIdx;
            
            btn.onClick.AddListener(() => {
                idx = (idx + 1) % options.Count;
                t.text = label + ": " + options[idx];
                onValueChanged(idx);
            });
            
            return container;
        }

        public static GameObject CreateTextInput(GameObject parentGO, float width, float height, string defaultText, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<string> onEndEdit)
        {
            GameObject inputGO = AddChildGOImage(parentGO, InputFieldBg, anchorPreset, width, height, new Vector2(xOffset, yOffset), rounded: true);
            inputGO.name = "TextInput";
            
            InputField inputField = inputGO.AddComponent<InputField>();
            
            Text t = CreateLabel(inputGO, "", fontSize, InputFieldTextColor, TextAnchor.MiddleLeft, richText: false, name: "Text");
            RectTransform textRT = t.GetComponent<RectTransform>();
            textRT.sizeDelta = new Vector2(-10, -10);
            textRT.anchoredPosition = new Vector2(5, 0);

            inputField.textComponent = t;
            
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(inputGO.transform, false);
            Text p = placeholderGO.AddComponent<Text>();
            p.text = defaultText;
            p.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            p.fontSize = fontSize;
            p.color = InputFieldPlaceholderColor;
            p.alignment = TextAnchor.MiddleLeft;
            p.fontStyle = FontStyle.Italic;
            
            RectTransform placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = new Vector2(-10, -10);
            placeholderRT.anchoredPosition = new Vector2(5, 0);
            
            inputField.placeholder = p;

            // Standard editor shortcut: Ctrl+Backspace deletes previous word.
            inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(inputField);
            
            if (onEndEdit != null) inputField.onEndEdit.AddListener(onEndEdit);
            
            return inputGO;
        }
    }
}
