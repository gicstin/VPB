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

        // Universal gallery chrome — aliases <see cref="GalleryUiColorTokens"/> (single source).
        public static readonly Color PopupBackdrop = GalleryUiColorTokens.PopupSurface;
        public static readonly Color PopupRowBackdrop = GalleryUiColorTokens.PopupRowIdle;
        public static readonly Color PopupRowActiveBackdrop = GalleryUiColorTokens.PopupRowActive;
        public static readonly Color PopupText = GalleryUiColorTokens.TextOnAccent;
        public static readonly Color PopupMutedText = GalleryUiColorTokens.TextMuted;
        public static readonly Color TextPrimary = GalleryUiColorTokens.TextPrimary;
        public static readonly Color TextMuted = GalleryUiColorTokens.TextMuted;
        public static readonly Color TextDim = GalleryUiColorTokens.TextDim;
        public static readonly Color InputFieldTextColor = GalleryUiColorTokens.TextOnAccent;
        public static readonly Color InputFieldPlaceholderColor = GalleryUiColorTokens.TextPlaceholder;
        public static readonly Color InputFieldBg = GalleryUiColorTokens.SurfaceDarker;
        public static readonly Color TextShadowColor = GalleryUiColorTokens.TextShadow;

        // Neutral chrome fills (formerly written inline as raw new Color(...) dozens of times).
        public static readonly Color ChromeDarker = GalleryUiColorTokens.SurfaceDarker;
        public static readonly Color ChromeDark = GalleryUiColorTokens.SurfaceDark;
        public static readonly Color ChromePanel = GalleryUiColorTokens.SurfacePanel;
        public static readonly Color ChromeMid = GalleryUiColorTokens.SurfaceMid;
        // Interactive accents: selected = muted cool-grey, green = confirm CTA, red = destructive.
        public static readonly Color AccentBlue = GalleryUiColorTokens.AccentSelected;
        public static readonly Color AccentGreen = GalleryUiColorTokens.AccentConfirm;
        public static readonly Color AccentRed = GalleryUiColorTokens.AccentDanger;

        /// <summary>Background of centered modal panels (formerly inline new Color(0.06,0.06,0.08,1)).</summary>
        public static readonly Color ModalPanel = GalleryUiColorTokens.ModalSurface;
        // Standard gallery Button ColorBlock tints (formerly inlined per button). White normalColor keeps the
        // RoundedRect fill unchanged; hover brightens, press darkens, disabled dims + fades.
        public static readonly Color ButtonHighlight = GalleryUiColorTokens.ButtonHighlight;
        public static readonly Color ButtonPressed = GalleryUiColorTokens.ButtonPressed;
        public static readonly Color ButtonDisabled = GalleryUiColorTokens.ButtonDisabled;

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
        /// <see cref="UIHoverBorder"/>. Run once at init and on a throttle via <see cref="GalleryPaneChromeEnforcer"/>
        /// so tabs/redraws cannot restore default hover fill.
        /// </summary>
        public static void ApplyGalleryPaneHoverPolicy(GameObject root)
        {
            ApplyHoverPolicyCore(root, forceInward: false);
        }

        /// <summary>
        /// Modeless float roots live on canvas (outside <see cref="GalleryPaneChromeEnforcer"/>).
        /// Same neutralize + border policy, but always inward — float title/footer/panel use
        /// <see cref="RectMask2D"/> and outward rims clip (invisible hover).
        /// Cold/warm after build or list rebuild — not per-frame.
        /// </summary>
        public static void ApplyFloatRootHoverPolicy(GameObject root)
        {
            ApplyHoverPolicyCore(root, forceInward: true);
        }

        private static void ApplyHoverPolicyCore(GameObject root, bool forceInward)
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
                        bool added = hb == null;
                        if (added) hb = s.gameObject.AddComponent<UIHoverBorder>();
                        // Only stamp default border color on newly added borders — never overwrite
                        // side-rail selected tints / custom hover colors (that caused a 0.5s pulse).
                        bool wantInward = forceInward || IsUnderImportSidebarScrollViewport(s.transform);
                        ApplyHoverBorderPolicyIfChanged(hb, border, wantInward, assignDefaultColor: added);
                        EnableChromeIdleRim(hb);
                    }
                }

                // Non-Button hover borders (resize handles, input fields): inward only — do not clobber color.
                var hbs = root.GetComponentsInChildren<UIHoverBorder>(true);
                for (int i = 0; i < hbs.Length; i++)
                {
                    var hb = hbs[i];
                    if (hb == null) continue;
                    bool wantInward = forceInward || IsUnderImportSidebarScrollViewport(hb.transform);
                    ApplyHoverBorderPolicyIfChanged(hb, border, wantInward, assignDefaultColor: false);
                }
            }
            catch { }
        }

        /// <summary>
        /// Ensure inward + optional default color. Never rewrite an existing custom <see cref="UIHoverBorder.hoverColor"/>.
        /// </summary>
        private static void ApplyHoverBorderPolicyIfChanged(UIHoverBorder hb, Color border, bool wantInward, bool assignDefaultColor)
        {
            if (hb == null) return;
            bool needApply = false;
            if (assignDefaultColor)
            {
                hb.hoverColor = border;
                needApply = true;
            }
            if (wantInward && !hb.inward)
            {
                hb.inward = true;
                needApply = true;
            }
            if (needApply) hb.ApplyBorderSettings();
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

            // Expand transitive meta.json deps from the SQLite index so scan-whitelist temp allow
            // covers packages the scene JSON never names (same closure PrewarmOnDemand uses).
            if (needed.Count > 0)
            {
                try
                {
                    var hosts = new List<string>(needed);
                    var sqlDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < hosts.Count; i++)
                    {
                        sqlDeps.Clear();
                        if (!VpbLocalDatabase.TryReadRecursiveDependencyUids(hosts[i], sqlDeps)) continue;
                        foreach (string dep in sqlDeps)
                        {
                            if (!string.IsNullOrEmpty(dep)) needed.Add(dep);
                        }
                    }
                }
                catch { }
            }

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

            // Drain pending catalog refresh while scene temp allow-list is still active so native
            // Refresh does not drop just-loaded packages in the same window we remove overrides.
            try
            {
                if (VamOnDemandLoader.HasPendingCoalescedVamRefresh())
                    VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("scene_load_cleanup_drain");
            }
            catch { }

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

            // History: SuperController.LoadInternal records scene use (covers VPB + VAM Browser + Scene Loader).

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
            try { VpbProgressService.ClearBlocking(); } catch { }
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

            // Gallery scene load previously skipped Prewarm (drag/VDS/import call it). With scan
            // whitelist on, register host+transitive deps into VaM before the native catalog refresh
            // so FileExists/GetVarFileEntry miss hooks are not the only path for meta-only deps.
            // Queue coalesced catalog refresh only when this coroutine will not run an explicit
            // bridge refresh; FinalizeSceneLoadCleanup drains any pending coalesced refresh.
            if (ScanWhitelistManager.Instance.IsEnabled)
            {
                try
                {
                    SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(
                        entry, path, queueCoalescedRefresh: !packageStateChanged);
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB OnDemand] Scene-load prewarm failed: " + ex.Message);
                }
            }

            // Tell LoadInternal funnel gallery already prepped this path — skip duplicate native prep.
            try { SceneLoadingUtils.NoteGallerySceneLoadPrep(path); } catch { }

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
        /// Clears scene banner + OS heartbeat when merge loads skip WorldUI.Activate / EndSceneLoadTotal.
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
                    bool banner = VpbProgressService.IsSceneLoadBannerActive;
                    bool blocking = VpbProgressService.IsBlocking;
                    // Only idle-exit when both gone (serial bump clears via EndSceneLoadTotal).
                    if (!banner && !blocking)
                        yield break;
                    if (LogUtil.GetSceneLoadTotalSerial() != serialAtStart)
                    {
                        EndSceneLoadBanner();
                        yield break;
                    }
                }
                catch
                {
                    EndSceneLoadBanner();
                    yield break;
                }

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
            try
            {
                VpbProgressService.EnterBlocking(
                    merge ? "Merging scene" : "Loading scene",
                    merge ? "VaM may freeze — merging…" : "VaM may freeze — restoring…");
            }
            catch { }
            ScheduleSceneLoadBannerFallback();
            // One frame so busy chrome + strip paint before sync Load stalls main thread.
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

        /// <summary>
        /// How scene atoms are added into the live scene from the floating context menu.
        /// </summary>
        public enum SceneAddMode
        {
            /// <summary>VaM LoadMerge of the whole scene (persons + everything). Heavy; may blank view.</summary>
            FullMerge = 0,
            /// <summary>Spawn non-Person atoms via SceneAtomImporter (preferred). Falls back to filtered LoadMerge.</summary>
            NonPersons = 1,
            /// <summary>Filtered LoadMerge excluding Person-like atoms (includes free-standing CUAs).</summary>
            NonPersonsMergeLoad = 2,
            /// <summary>Filtered LoadMerge of Person-like atoms only (unique ids).</summary>
            PersonsOnly = 3
        }

        public static void MergeSceneFile(FileEntry entry, string path, GalleryPanel panel, bool atPlayer, UIDraggableItem dragger = null)
        {
            MergeSceneFile(entry, path, panel, SceneAddMode.FullMerge, atPlayer, dragger);
        }

        public static void MergeSceneFile(
            FileEntry entry,
            string path,
            GalleryPanel panel,
            SceneAddMode mode,
            bool atPlayer,
            UIDraggableItem dragger = null)
        {
            if (entry == null && string.IsNullOrEmpty(path)) return;
            if (!TryBeginSceneLoadThrottle())
            {
                LogUtil.LogWarning("[VPB] UI.MergeSceneFile ignored (throttled)");
                StatusBrief(panel, VPBTranslation.T("ctx.merge.throttled", "Merge ignored (too soon)."));
                return;
            }
            if (Messager.singleton == null)
            {
                LogUtil.LogWarning("[VPB] Messager.singleton is null, cannot start merge scene coroutine");
                StatusBrief(panel, VPBTranslation.T("ctx.merge.no_messager", "Cannot merge — messager unavailable."));
                return;
            }
            Messager.singleton.StartCoroutine(MergeSceneFileRoutine(entry, path, panel, mode, atPlayer, dragger));
        }

        private static void StatusBrief(GalleryPanel panel, string msg)
        {
            if (panel == null || string.IsNullOrEmpty(msg)) return;
            try { panel.ShowTemporaryStatus(msg, 2.5f); } catch { }
        }

        private static string ResolveSceneHostUid(FileEntry entry)
        {
            if (entry == null) return null;
            try
            {
                if (entry is VarFileEntry vfe)
                {
                    string uid = vfe.GetRowPackageUid();
                    if (!string.IsNullOrEmpty(uid)) return uid;
                    if (vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                        return vfe.Package.Uid;
                }
            }
            catch { }
            return null;
        }

        private static string FindFirstPersonAtomId(JSONClass scene)
        {
            if (scene == null) return null;
            JSONArray atoms = scene["atoms"] != null ? scene["atoms"].AsArray : null;
            if (atoms == null) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i] != null ? atoms[i].AsObject : null;
                if (a == null) continue;
                string type = a["type"] != null ? a["type"].Value : null;
                if (!SceneUtils.IsPersonLikeAtomType(type)) continue;
                if (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                    return a["id"].Value;
            }
            return null;
        }

        private static IEnumerator MergeSceneFileRoutine(
            FileEntry entry,
            string path,
            GalleryPanel panel,
            SceneAddMode mode,
            bool atPlayer,
            UIDraggableItem dragger)
        {
            if (entry == null && !string.IsNullOrEmpty(path))
            {
                try { entry = FileManager.GetFileEntry(path); } catch { entry = null; }
            }
            if (entry == null)
            {
                LogUtil.LogError("[VPB] MergeSceneFile: no FileEntry for " + path);
                StatusBrief(panel, VPBTranslation.T("ctx.merge.no_entry", "Merge failed — file not found."));
                yield break;
            }

            LogUtil.Log("[VPB] MergeSceneFile started: " + path
                + " mode=" + mode + " atPlayer=" + atPlayer);
            try
            {
                if (!LogUtil.IsSceneClickActive())
                    LogUtil.BeginSceneClick(path);
            }
            catch { }

            string bannerPhase = mode == SceneAddMode.FullMerge ? "Merging" : "Adding";
            try { VpbProgressService.BeginSceneLoadPrep(SceneLoadBannerName(entry), bannerPhase); } catch { }
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
                StatusBrief(panel, VPBTranslation.T("ctx.merge.prep_failed", "Merge failed — prep unsuccessful."));
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
                        if (a != null && !string.IsNullOrEmpty(a.uid))
                            atomsBefore.Add(a.uid);
                    }
                }
            }

            Atom placeTarget = null;
            if (panel != null)
            {
                try
                {
                    Atom t = panel.SelectedTargetAtom;
                    if (SceneUtils.IsPersonLikeAtom(t)) placeTarget = t;
                }
                catch { }
            }

            if (mode == SceneAddMode.NonPersons)
            {
                yield return AddNonPersonAtomsRoutine(
                    entry, prep.NormalizedPath, panel, placeTarget, atPlayer, atomsBefore, dragger);
                yield break;
            }

            string loadPath = prep.NormalizedPath;
            if (mode == SceneAddMode.NonPersonsMergeLoad || mode == SceneAddMode.PersonsOnly)
            {
                bool personsOnly = mode == SceneAddMode.PersonsOnly;
                try
                {
                    VpbProgressService.ReportSceneLoadPrepPhase(
                        personsOnly ? "Filtering to persons" : "Filtering persons out");
                }
                catch { }
                yield return null;

                string filtered = SceneLoadingUtils.CreateFilteredSceneJSON(
                    prep.NormalizedPath,
                    entry,
                    atom =>
                    {
                        if (atom == null || atom["type"] == null) return false;
                        bool isPerson = SceneUtils.IsPersonLikeAtomType(atom["type"].Value);
                        return personsOnly ? isPerson : !isPerson;
                    },
                    ensureUniqueIds: true);

                if (string.IsNullOrEmpty(filtered))
                {
                    EndSceneLoadBanner();
                    StatusBrief(panel, personsOnly
                        ? VPBTranslation.T("ctx.merge.no_persons", "No person atoms to add.")
                        : VPBTranslation.T("ctx.merge.no_nonpersons", "No non-person atoms to add."));
                    yield break;
                }
                loadPath = NormalizePath(filtered);
            }

            yield return InvokeSceneLoadCoroutine(
                loadPath,
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

            if (mode == SceneAddMode.FullMerge)
                StatusBrief(panel, VPBTranslation.T("ctx.merge.full_done", "Scene merge started."));
            else if (mode == SceneAddMode.PersonsOnly)
                StatusBrief(panel, VPBTranslation.T("ctx.merge.persons_done", "Person merge started."));
            else
                StatusBrief(panel, VPBTranslation.T("ctx.merge.nonpersons_done", "Non-person merge started."));
        }

        /// <summary>
        /// Preferred add path: spawn non-Person atoms without VaM LoadMerge overlay.
        /// Falls back to filtered LoadMerge when importer finds nothing but JSON has atoms
        /// (e.g. only free-standing CUAs with no person target).
        /// </summary>
        private static IEnumerator AddNonPersonAtomsRoutine(
            FileEntry entry,
            string normalizedPath,
            GalleryPanel panel,
            Atom placeTarget,
            bool atPlayer,
            HashSet<string> atomsBefore,
            UIDraggableItem dragger)
        {
            try { VpbProgressService.ReportSceneLoadPrepPhase("Reading scene atoms"); } catch { }
            yield return null;

            JSONNode root = null;
            try { root = LoadJSONWithFallback(normalizedPath, entry); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] AddNonPersonAtoms: JSON load failed: " + ex.Message);
            }
            yield return null;

            JSONClass scene = root != null ? root.AsObject : null;
            if (scene == null)
            {
                EndSceneLoadBanner();
                StatusBrief(panel, VPBTranslation.T("ctx.merge.read_failed", "Could not read scene JSON."));
                yield break;
            }

            JSONArray atoms = scene["atoms"] != null ? scene["atoms"].AsArray : null;
            if (atoms == null || atoms.Count == 0)
            {
                EndSceneLoadBanner();
                StatusBrief(panel, VPBTranslation.T("ctx.merge.empty_scene", "Scene has no atoms."));
                yield break;
            }

            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            int nonPersonCount = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i] != null ? atoms[i].AsObject : null;
                if (a == null) continue;
                string type = a["type"] != null ? a["type"].Value : string.Empty;
                if (SceneUtils.IsPersonLikeAtomType(type)) continue;
                nonPersonCount++;
                string id = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                    ? a["id"].Value
                    : (type + "_" + i);
                selectedIds.Add(id);
            }

            if (selectedIds.Count == 0)
            {
                EndSceneLoadBanner();
                StatusBrief(panel, VPBTranslation.T(
                    "ctx.merge.no_nonpersons",
                    "No non-person atoms to add."));
                yield break;
            }

            string hostUid = ResolveSceneHostUid(entry);
            string sourcePersonId = FindFirstPersonAtomId(scene);
            bool relative = placeTarget != null;

            if (atomsBefore == null && atPlayer)
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    atomsBefore = new HashSet<string>();
                    foreach (Atom a in sc.GetAtoms())
                    {
                        if (a != null && !string.IsNullOrEmpty(a.uid))
                            atomsBefore.Add(a.uid);
                    }
                }
            }

            try { VpbProgressService.ReportSceneLoadPrepPhase("Spawning atoms"); } catch { }
            yield return null;

            int beforeCount = 0;
            try
            {
                SuperController scBefore = SuperController.singleton;
                if (scBefore != null)
                {
                    foreach (Atom _ in scBefore.GetAtoms())
                        beforeCount++;
                }
            }
            catch { }

            yield return global::VPB.src.util.SceneAtomImporter.ImportSelectedAtoms(
                scene,
                sourcePersonId,
                placeTarget,
                hostUid,
                selectedIds,
                relative,
                skipExistingInScene: true);

            int afterCount = beforeCount;
            try
            {
                SuperController scAfter = SuperController.singleton;
                if (scAfter != null)
                {
                    afterCount = 0;
                    foreach (Atom _ in scAfter.GetAtoms())
                        afterCount++;
                }
            }
            catch { }

            int spawned = afterCount - beforeCount;
            if (spawned <= 0 && nonPersonCount > 0)
            {
                // Importer skipped CUAs / nothing new — fall back to filtered LoadMerge.
                LogUtil.Log("[VPB] AddNonPersonAtoms: importer spawned 0; falling back to filtered LoadMerge");
                string filtered = SceneLoadingUtils.CreateFilteredSceneJSON(
                    normalizedPath,
                    entry,
                    atom => atom != null
                        && atom["type"] != null
                        && !SceneUtils.IsPersonLikeAtomType(atom["type"].Value),
                    ensureUniqueIds: true);
                if (string.IsNullOrEmpty(filtered))
                {
                    EndSceneLoadBanner();
                    StatusBrief(panel, VPBTranslation.T(
                        "ctx.merge.nothing_new",
                        "Nothing new to add (duplicates skipped)."));
                    yield break;
                }

                yield return InvokeSceneLoadCoroutine(
                    NormalizePath(filtered),
                    merge: true,
                    cleanupState: null,
                    collapseGalleryPanels: false,
                    deferOneFrameBeforeLoad: true);
            }
            else
            {
                EndSceneLoadBanner();
            }

            if (atPlayer && atomsBefore != null && dragger != null)
            {
                if (panel != null)
                    panel.StartCoroutine(dragger.RunTeleportMergedAtomsToPlayer(atomsBefore));
                else
                    dragger.StartCoroutine(dragger.RunTeleportMergedAtomsToPlayer(atomsBefore));
            }

            if (spawned > 0)
            {
                StatusBrief(panel, string.Format(
                    VPBTranslation.T("ctx.merge.added_n", "Added {0} atom(s)."),
                    spawned));
            }
            else
            {
                StatusBrief(panel, VPBTranslation.T(
                    "ctx.merge.nonpersons_done",
                    "Non-person merge started."));
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
                List<RoundedRect> fills = RoundedRect.Live;
                for (int i = 0; i < fills.Count; i++)
                {
                    RoundedRect rr = fills[i];
                    if (rr == null || rr.excludeFromGlobalRadiusSync || !IsLiveSceneUiComponent(rr)) continue;
                    rr.cornerRadiusFraction = frac;
                }
            }
            catch { }
            try
            {
                List<RoundedRectOutline> outlines = RoundedRectOutline.Live;
                for (int i = 0; i < outlines.Count; i++)
                {
                    RoundedRectOutline outline = outlines[i];
                    if (outline == null || !IsLiveSceneUiComponent(outline)) continue;
                    outline.cornerRadiusFraction = frac;
                }
            }
            catch { }
            try
            {
                List<UIHoverBorder> borders = UIHoverBorder.Live;
                for (int i = 0; i < borders.Count; i++)
                {
                    UIHoverBorder hb = borders[i];
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

        public static float Gap(float designPx, float scale = 1f)
        {
            if (scale <= 0f) scale = 1f;
            return designPx * scale;
        }

        public static float GapHair(float scale = 1f) => Gap(GalleryUiDesignTokens.HairGapRef, scale);
        public static float GapTight(float scale = 1f) => Gap(GalleryUiDesignTokens.TightGapRef, scale);
        public static float GapControl(float scale = 1f) => Gap(GalleryUiDesignTokens.ControlGapRef, scale);
        public static float GapGroup(float scale = 1f) => Gap(GalleryUiDesignTokens.GroupGapRef, scale);
        public static float GapRegion(float scale = 1f) => Gap(GalleryUiDesignTokens.RegionGapRef, scale);
        public static float GapSection(float scale = 1f) => Gap(GalleryUiDesignTokens.SectionGapRef, scale);

        public static RectOffset PadUniform(float all, float scale = 1f) => Pad(all, all, all, all, scale);
        public static RectOffset PadHV(float h, float v, float scale = 1f) => Pad(h, h, v, v, scale);
        public static RectOffset PadHair(float scale = 1f) => PadUniform(GalleryUiDesignTokens.HairGapRef, scale);
        public static RectOffset PadTight(float scale = 1f) => PadUniform(GalleryUiDesignTokens.TightGapRef, scale);
        public static RectOffset PadControl(float scale = 1f) => PadUniform(GalleryUiDesignTokens.ControlGapRef, scale);
        public static RectOffset PadGroup(float scale = 1f) => PadUniform(GalleryUiDesignTokens.GroupGapRef, scale);
        public static RectOffset PadDialog(float scale = 1f) => PadUniform(GalleryUiDesignTokens.DialogPadRef, scale);
        public static RectOffset PadSection(float scale = 1f) => PadUniform(GalleryUiDesignTokens.SectionGapRef, scale);
        /// <summary>Float footer / packed chrome: band L/R, tight T/B.</summary>
        public static RectOffset PadFloatFooter(float scale = 1f)
            => PadHV(GalleryUiDesignTokens.FloatChromePadHRef, GalleryUiDesignTokens.FloatChromePadVRef, scale);
        /// <summary>Popup / dropdown shell — same as band (Gestalt: menus match chrome).</summary>
        public static RectOffset PadPopup(float scale = 1f)
            => PadUniform(GalleryUiDesignTokens.PopupMenuPaddingRef, scale);

        public static RectOffset BandPad(float scale = 1f)
        {
            return Pad(GalleryUiDesignTokens.BandPadHRef, GalleryUiDesignTokens.BandPadHRef,
                       GalleryUiDesignTokens.BandPadVRef, GalleryUiDesignTokens.BandPadVRef, scale);
        }

        public static RectOffset RowPad(float scale = 1f)
        {
            return Pad(GalleryUiDesignTokens.BandPadHRef, GalleryUiDesignTokens.BandPadHRef, 0f, 0f, scale);
        }

        public static RectOffset ScrollEndsPad(float scale = 1f)
        {
            return Pad(0f, 0f, GalleryUiDesignTokens.BandPadVRef, GalleryUiDesignTokens.BandPadVRef, scale);
        }

        public static void ApplyBandInset(RectTransform rt, float scale, bool horizontal = true, bool vertical = true)
        {
            if (rt == null) return;
            if (scale <= 0f) scale = 1f;
            float padH = GalleryUiDesignTokens.BandPadHRef * scale;
            float padV = GalleryUiDesignTokens.BandPadVRef * scale;
            Vector2 min = rt.offsetMin;
            Vector2 max = rt.offsetMax;
            if (horizontal) { min.x = padH; max.x = -padH; }
            if (vertical) { min.y = padV; max.y = -padV; }
            rt.offsetMin = min;
            rt.offsetMax = max;
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
        /// Modal/header title: <see cref="CreateLabel"/> + <see cref="GalleryUiMetrics.ApplyEmphasisTitle"/>.
        /// </summary>
        public static Text CreateEmphasisTitleLabel(GameObject parentGO, string text, int fontSize, Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft, string name = "Title")
        {
            Text t = CreateLabel(parentGO, text, fontSize, color ?? Color.white, alignment, name: name);
            GalleryUiMetrics.ApplyEmphasisTitle(t, fontSize);
            return t;
        }

        /// <summary>
        /// Destroys all children of <paramref name="parent"/> (reverse order).
        /// </summary>
        public static void DestroyAllChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null) continue;
                try { UnityEngine.Object.Destroy(ch.gameObject); } catch { }
            }
        }

        /// <summary>
        /// Layout-group single-line input: rounded bg, padded TextArea, placeholder + text labels.
        /// </summary>
        public static InputField CreateChromeLayoutInputField(
            Transform parent,
            int fontSize,
            float height,
            float flexibleWidth,
            float padX,
            float padY,
            Color bg,
            Color placeholderColor,
            string placeholderText = null,
            string name = "Input")
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AddGalleryElementRoundedBg(go, bg);
            AddLE(go, minHeight: height, preferredHeight: height, flexibleWidth: flexibleWidth);

            GameObject ta = new GameObject("TextArea");
            ta.transform.SetParent(go.transform, false);
            RectTransform taRt = ta.AddComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(padX, padY);
            taRt.offsetMax = new Vector2(-padX, -padY);

            Text phT = CreateLabel(ta, placeholderText ?? "", fontSize, placeholderColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false, name: "Placeholder");
            Text tcT = CreateLabel(ta, "", fontSize, Color.white,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false, name: "Text");

            InputField input = go.AddComponent<InputField>();
            input.textComponent = tcT;
            input.placeholder = phT;
            input.lineType = InputField.LineType.SingleLine;
            try { NeutralizeSelectableColorTint(input); } catch { }
            UIHoverBorder hb = go.AddComponent<UIHoverBorder>();
            try
            {
                hb.inward = true;
                EnableChromeIdleRim(hb);
            }
            catch { }
            return input;
        }

        /// <summary>
        /// Magnifying-glass inside a chrome input (idempotent). Sets left TextArea inset;
        /// caller keeps right inset for clear (X) if present.
        /// </summary>
        public static void LayoutChromeSearchIcon(GameObject inputGO, float scale = 1f)
        {
            if (inputGO == null) return;
            float s = scale <= 0f ? 1f : scale;

            Transform iconTr = inputGO.transform.Find("SearchIcon");
            if (iconTr == null)
            {
                Sprite spr = null;
                try { spr = LoadIconSprite("search", GalleryUiColorTokens.SearchIconTint); }
                catch { spr = null; }
                if (spr != null)
                {
                    GameObject iconGO = new GameObject("SearchIcon");
                    iconGO.transform.SetParent(inputGO.transform, false);
                    Image iconImg = AddImage(iconGO, GalleryUiColorTokens.SearchIconTint);
                    if (iconImg != null)
                    {
                        UI.SetIconSprite(iconImg, spr);
                        iconImg.raycastTarget = false;
                        iconImg.preserveAspect = true;
                    }
                    iconTr = iconGO.transform;
                }
            }

            if (iconTr != null)
            {
                RectTransform iconRT = iconTr as RectTransform;
                if (iconRT != null)
                {
                    float iconSz = GalleryUiDesignTokens.SearchIconSizeRef * s;
                    iconRT.anchorMin = new Vector2(0f, 0.5f);
                    iconRT.anchorMax = new Vector2(0f, 0.5f);
                    iconRT.pivot = new Vector2(0f, 0.5f);
                    iconRT.anchoredPosition = new Vector2(GalleryUiDesignTokens.SearchIconLeftPadRef * s, 0f);
                    iconRT.sizeDelta = new Vector2(iconSz, iconSz);
                }
            }

            Transform textArea = inputGO.transform.Find("TextArea");
            if (textArea == null) return;
            RectTransform taRt = textArea as RectTransform;
            if (taRt == null) return;
            float left = GalleryUiDesignTokens.SearchTextLeftInsetRef * s;
            taRt.offsetMin = new Vector2(left, taRt.offsetMin.y);
        }

        /// <summary>
        /// Full-footer drag hit behind chrome buttons (same job as title-bar drag).
        /// Disables footer tint raycasts; stretch Graphic + ignoreLayout so HLG/VLG does not crush it.
        /// Caller AddComponent panel-drag on returned GO (init-time only).
        /// </summary>
        public static GameObject CreateFloatFooterDragArea(GameObject footer)
        {
            if (footer == null) return null;
            Image footerBg = footer.GetComponent<Image>();
            if (footerBg != null) footerBg.raycastTarget = false;

            GameObject footerDragArea = AddChildGOImage(
                footer, new Color(0f, 0f, 0f, 0.01f),
                AnchorPresets.stretchAll, 0f, 0f, Vector2.zero);
            footerDragArea.name = "FooterDragArea";
            footerDragArea.transform.SetAsFirstSibling();
            Image footerDragImg = footerDragArea.GetComponent<Image>();
            if (footerDragImg != null) footerDragImg.raycastTarget = true;
            LayoutElement footerDragLe = footerDragArea.GetComponent<LayoutElement>();
            if (footerDragLe == null) footerDragLe = footerDragArea.AddComponent<LayoutElement>();
            footerDragLe.ignoreLayout = true;
            return footerDragArea;
        }

        /// <summary>
        /// Flexible footer spacer needs a Graphic to receive drags (empty RT does not).
        /// Caller AddComponent panel-drag after this (init-time only).
        /// </summary>
        public static Image EnsureFloatFooterSpacerDragHit(GameObject spacer)
        {
            if (spacer == null) return null;
            Image spacerImg = spacer.GetComponent<Image>();
            if (spacerImg == null) spacerImg = spacer.AddComponent<Image>();
            spacerImg.color = new Color(0f, 0f, 0f, 0.01f);
            spacerImg.raycastTarget = true;
            return spacerImg;
        }

        /// <summary>
        /// Hover rim for float chrome that is not a <see cref="Button"/> (resize grip, etc.).
        /// <see cref="ApplyGalleryPaneHoverPolicy"/> only auto-adds borders on Buttons.
        /// Inward by default so footer/title <see cref="RectMask2D"/> does not clip the rim.
        /// </summary>
        public static UIHoverBorder EnsureFloatChromeHoverBorder(GameObject go, bool inward = true)
        {
            if (go == null) return null;
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb == null) hb = go.AddComponent<UIHoverBorder>();
            if (hb.inward != inward)
                hb.inward = inward;
            EnableChromeIdleRim(hb);
            try { hb.ApplyBorderSettings(); } catch { }
            return hb;
        }

        /// <summary>Idle + selected chrome rims. Hover rim always stays. Default on.</summary>
        public static bool ChromeButtonRimsEnabled()
        {
            try
            {
                if (VPBConfig.Instance == null) return true;
                return VPBConfig.Instance.EnableGalleryButtonChromeRims;
            }
            catch { return true; }
        }

        /// <summary>
        /// Show/hide idle + selected chrome rims on live <see cref="UIHoverBorder"/> (not grid thumbs).
        /// </summary>
        public static void ApplyGalleryButtonChromeRimsGlobally()
        {
            try
            {
                List<UIHoverBorder> borders = UIHoverBorder.Live;
                for (int i = 0; i < borders.Count; i++)
                {
                    UIHoverBorder hb = borders[i];
                    if (hb == null || hb.hoverBorderGO != null || !IsLiveSceneUiComponent(hb)) continue;
                    try { hb.SyncIndicatorVisibility(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Faint idle rim on muted chrome buttons so they still look clickable (Norman signifier).
        /// Skip grid thumbs that use <see cref="UIHoverBorder.hoverBorderGO"/>.
        /// </summary>
        public static void EnableChromeIdleRim(UIHoverBorder hb)
        {
            if (hb == null || hb.hoverBorderGO != null) return;
            bool was = hb.showIdleRim;
            hb.showIdleRim = true;
            hb.idleRimColor = GalleryUiColorTokens.RimIdle;
            try
            {
                if (!was) hb.ApplyBorderSettings();
                else hb.SyncIndicatorVisibility();
            }
            catch { }
        }

        /// <summary>
        /// Persistent selected rim (muted cool), distinct from yellow hover. Fill stays the caller's job.
        /// Hidden when <see cref="ChromeButtonRimsEnabled"/> is off.
        /// </summary>
        public static void SetControlSelectedRim(GameObject go, bool selected)
        {
            if (go == null) return;
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb == null) return;
            hb.selectedRimColor = GalleryUiColorTokens.RimSelected;
            if (hb.isSelected == selected)
            {
                hb.SyncIndicatorVisibility();
                return;
            }
            hb.isSelected = selected;
            hb.SyncIndicatorVisibility();
        }

        /// <summary>
        /// Non-interactive window-type glyph for float title bars (after grip, before title).
        /// Host includes trailing gap so label is not stuck to icon.
        /// </summary>
        public static GameObject CreateFloatTitleWindowIcon(GameObject titleBar, string iconRelativePath, float size)
        {
            if (titleBar == null || string.IsNullOrEmpty(iconRelativePath) || size <= 0f) return null;
            float gap = size * (GalleryUiDesignTokens.FloatTitleWindowIconGapRef
                / GalleryUiDesignTokens.FloatTitleWindowIconSizeRef);

            GameObject host = new GameObject("WindowIcon");
            host.transform.SetParent(titleBar.transform, false);
            AddHLG(
                host, spacing: 0f, padding: Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            AddLE(host,
                minWidth: size + gap, preferredWidth: size + gap,
                minHeight: size, preferredHeight: size,
                flexibleWidth: 0f, flexibleHeight: 0f);

            GameObject icon = new GameObject("Icon");
            icon.transform.SetParent(host.transform, false);
            Image img = AddImage(icon, GalleryUiColorTokens.TitleWindowIconTint);
            if (img != null)
            {
                img.raycastTarget = false;
                img.preserveAspect = true;
                try
                {
                    Sprite spr = LoadIconSprite(iconRelativePath, GalleryUiColorTokens.TitleWindowIconTint);
                    if (spr != null) UI.SetIconSprite(img, spr);
                }
                catch { }
            }
            AddLE(icon,
                minWidth: size, preferredWidth: size,
                minHeight: size, preferredHeight: size,
                flexibleWidth: 0f, flexibleHeight: 0f);

            GameObject spacer = new GameObject("Gap");
            spacer.transform.SetParent(host.transform, false);
            AddLE(spacer,
                minWidth: gap, preferredWidth: gap,
                minHeight: 1f, preferredHeight: 1f,
                flexibleWidth: 0f, flexibleHeight: 0f);

            return host;
        }

        /// <summary>
        /// Shared float title HLG pad/spacing + grip column width (tight icon inset).
        /// Call at create and on ChromeScale rescale.
        /// </summary>
        public static void ApplyFloatTitleBarMetrics(HorizontalLayoutGroup hlg, GameObject grip, float scale)
        {
            float s = scale > 0f ? scale : 1f;
            if (hlg != null)
            {
                hlg.spacing = GalleryUiDesignTokens.FloatTitleBarSpacingRef * s;
                hlg.padding = Pad(
                    GalleryUiDesignTokens.FloatTitleBarPadHRef,
                    GalleryUiDesignTokens.FloatTitleBarPadHRef,
                    GalleryUiDesignTokens.FloatTitleBarPadVRef,
                    GalleryUiDesignTokens.FloatTitleBarPadVRef, s);
            }
            if (grip != null)
            {
                float gw = GalleryUiDesignTokens.FloatTitleGripWidthRef * s;
                LayoutElement le = grip.GetComponent<LayoutElement>();
                if (le == null) le = grip.AddComponent<LayoutElement>();
                le.minWidth = gw;
                le.preferredWidth = gw;
                le.flexibleWidth = 0f;
            }
        }

        /// <summary>
        /// Shared float chrome square icon button (collapse / close / footer tools).
        /// Cold/warm create only — not per-frame. net35-safe.
        /// </summary>
        public static GameObject CreateFloatChromeIconButton(
            Transform parent, float size, string iconPath, Color backdrop, UnityAction onClick)
        {
            if (parent == null || size <= 0f) return null;
            GameObject go = CreateUIButton(
                parent.gameObject, size, size, " ", 14, 0, 0, AnchorPresets.middleCenter, onClick);
            if (go == null) return null;
            StyleFloatChromeIconButton(go, size, iconPath, backdrop);
            return go;
        }

        /// <summary>
        /// Style existing square chrome button (CreateUIButton → this). Same pad/tint as
        /// <see cref="CreateFloatChromeIconButton"/>. Inward hover rim — title/footer
        /// <see cref="RectMask2D"/> clips outward rims.
        /// </summary>
        public static void StyleFloatChromeIconButton(
            GameObject go, float size, string iconPath, Color? backdropOverride = null)
        {
            if (go == null || size <= 0f) return;
            Color backdrop = backdropOverride.HasValue
                ? backdropOverride.Value
                : GalleryUiColorTokens.ChromeIconWell;
            Image img = go.GetComponent<Image>();
            if (img != null) img.color = backdrop;
            Button btn = go.GetComponent<Button>();
            if (btn != null) btn.transition = Selectable.Transition.None;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            // Keep hover under float title/footer masks (never strip on rescale).
            EnsureFloatChromeHoverBorder(go, inward: true);
            float pad = GalleryUiDesignTokens.FloatChromeIconPadRef;
            if (string.IsNullOrEmpty(iconPath)) return;
            try
            {
                Sprite spr = LoadIconSprite(iconPath, BarIconGlyphTint);
                if (spr == null) return;
                Transform existing = go.transform.Find("Icon");
                if (existing != null)
                {
                    Image iconImg = existing.GetComponent<Image>();
                    if (iconImg != null)
                    {
                        UI.SetIconSprite(iconImg, spr);
                        RectTransform irt = existing as RectTransform;
                        if (irt != null)
                            SizeButtonIcon(irt, go.GetComponent<RectTransform>(), pad);
                    }
                }
                else
                    AddIconToButton(go, spr, pad, backdrop);
            }
            catch { }
        }

        /// <summary>Rescale float chrome icon button + Icon child pad (ChromeScale adapt).</summary>
        public static void ScaleFloatChromeIconButton(GameObject go, float size, float scale = 1f)
        {
            if (go == null || size <= 0f) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(size, size);
            Transform iconTr = go.transform.Find("Icon");
            if (iconTr != null)
            {
                RectTransform irt = iconTr as RectTransform;
                if (irt != null)
                {
                    float pad = GalleryUiDesignTokens.FloatChromeIconPadRef * (scale > 0f ? scale : 1f);
                    SizeButtonIcon(irt, rt, pad);
                }
            }
            EnsureFloatChromeHoverBorder(go, inward: true);
        }

        /// <summary>
        /// Tree-row expand affordance: <c>chevron-right</c> collapsed / <c>chevron-down</c> open.
        /// Warm bind path — uses icon sprite cache; no new Icon GO after first apply.
        /// </summary>
        /// <param name="transparentWhenEmpty">
        /// Plugins leaf rows: clear well. Strip Keep empty categories: opaque placeholder well.
        /// </param>
        public static void ApplyTreeRowExpandIcon(
            GameObject expandBtn, bool canExpand, bool expanded, float scale,
            bool transparentWhenEmpty = true)
        {
            if (expandBtn == null) return;
            float s = scale > 0f ? scale : 1f;
            Image expandBg = expandBtn.GetComponent<Image>();
            if (expandBg != null)
            {
                if (canExpand)
                    expandBg.color = GalleryUiColorTokens.TreeExpandWell;
                else
                    expandBg.color = transparentWhenEmpty
                        ? GalleryUiColorTokens.TreeExpandWellEmptyClear
                        : GalleryUiColorTokens.TreeExpandWellEmpty;
            }

            Text expandGlyph = expandBtn.GetComponentInChildren<Text>(true);
            Transform iconTr = expandBtn.transform.Find("Icon");
            Image iconImg = iconTr != null ? iconTr.GetComponent<Image>() : null;

            if (!canExpand)
            {
                if (expandGlyph != null)
                {
                    expandGlyph.text = "";
                    expandGlyph.gameObject.SetActive(false);
                }
                if (iconImg != null) iconImg.gameObject.SetActive(false);
                return;
            }

            string path = expanded
                ? "chevron-down"
                : "chevron-right";
            Sprite spr = null;
            try { spr = LoadIconSprite(path, GalleryUiColorTokens.TreeExpandIconTint); }
            catch { spr = null; }

            float pad = GalleryUiDesignTokens.TreeRowExpandIconPadRef * s;
            if (spr != null)
            {
                if (iconImg != null)
                {
                    UI.SetIconSprite(iconImg, spr);
                    iconImg.gameObject.SetActive(true);
                    RectTransform irt = iconTr as RectTransform;
                    if (irt != null)
                        irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                }
                else
                {
                    AddIconToButton(expandBtn, spr, pad, expandBg != null ? expandBg.color : GalleryUiColorTokens.TreeExpandWell);
                    iconTr = expandBtn.transform.Find("Icon");
                    iconImg = iconTr != null ? iconTr.GetComponent<Image>() : null;
                    if (iconImg != null) iconImg.gameObject.SetActive(true);
                }
                if (expandGlyph != null)
                {
                    expandGlyph.text = "";
                    expandGlyph.gameObject.SetActive(false);
                }
                return;
            }

            // Fallback glyphs when icon load fails (cold / missing asset).
            if (iconImg != null) iconImg.gameObject.SetActive(false);
            if (expandGlyph != null)
            {
                expandGlyph.gameObject.SetActive(true);
                expandGlyph.text = expanded ? "\u25BC" : "\u25B6";
                expandGlyph.alignment = TextAnchor.MiddleCenter;
                expandGlyph.raycastTarget = false;
                expandGlyph.color = GalleryUiColorTokens.TreeExpandIconTint;
            }
        }

        /// <summary>Rescale <c>WindowIcon</c> host + glyph + trailing gap under a float title bar.</summary>
        public static void LayoutFloatTitleWindowIcon(GameObject titleBar, float size)
        {
            if (titleBar == null || size <= 0f) return;
            Transform hostTr = titleBar.transform.Find("WindowIcon");
            if (hostTr == null) return;
            float gap = size * (GalleryUiDesignTokens.FloatTitleWindowIconGapRef
                / GalleryUiDesignTokens.FloatTitleWindowIconSizeRef);

            LayoutElement hostLe = hostTr.GetComponent<LayoutElement>();
            if (hostLe != null)
            {
                hostLe.minWidth = size + gap;
                hostLe.preferredWidth = size + gap;
                hostLe.minHeight = size;
                hostLe.preferredHeight = size;
            }

            Transform iconTr = hostTr.Find("Icon");
            if (iconTr != null)
            {
                LayoutElement iconLe = iconTr.GetComponent<LayoutElement>();
                if (iconLe != null)
                {
                    iconLe.minWidth = size;
                    iconLe.preferredWidth = size;
                    iconLe.minHeight = size;
                    iconLe.preferredHeight = size;
                }
            }

            Transform gapTr = hostTr.Find("Gap");
            if (gapTr != null)
            {
                LayoutElement gapLe = gapTr.GetComponent<LayoutElement>();
                if (gapLe != null)
                {
                    gapLe.minWidth = gap;
                    gapLe.preferredWidth = gap;
                }
            }
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

        /// <summary>
        /// Stretch-all popup root with near-transparent child backdrop for click-outside dismiss.
        /// </summary>
        public static GameObject CreatePopupMenuRoot(GameObject parentGO, string name, UnityAction onClose)
        {
            GameObject root = CreateChildRT(parentGO, name, AnchorPresets.stretchAll);
            GameObject backdropGO = CreateChildRT(root, "Backdrop", AnchorPresets.stretchAll);
            AddImage(backdropGO, Black(0.001f));
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            if (onClose != null) backdropBtn.onClick.AddListener(onClose);
            return root;
        }

        /// <summary>
        /// Standard dropdown panel: PopupBackdrop fill, VLG, vertical ContentSizeFitter.
        /// </summary>
        public static GameObject CreatePopupMenuPanel(
            GameObject rootGO,
            string panelName,
            int anchorPreset,
            Vector2 size,
            Vector2 anchoredPosition,
            TextAnchor childAlignment = TextAnchor.UpperCenter,
            Action<VerticalLayoutGroup> configureVlg = null)
        {
            GameObject panelGO = CreateChildRT(rootGO, panelName, anchorPreset, size, anchoredPosition);
            AddImage(panelGO, PopupBackdrop);
            VerticalLayoutGroup vlg = AddVLG(
                panelGO,
                GalleryUiDesignTokens.PopupMenuRowSpacingRef,
                PadPopup(),
                childAlignment);
            configureVlg?.Invoke(vlg);
            ContentSizeFitter csf = panelGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return panelGO;
        }

        /// <summary>
        /// Sort/language-style popup row: left-aligned label, active/inactive chrome.
        /// </summary>
        public static GameObject AddPopupMenuRow(
            GameObject panelGO,
            float width,
            float height,
            string label,
            int fontSize,
            bool isActive,
            UnityAction onClick,
            float preferredHeight)
        {
            GameObject row = CreateUIButton(panelGO, width, height, label, fontSize, 0, 0, AnchorPresets.middleCenter, onClick);
            Image rowImg = row.GetComponent<Image>();
            if (rowImg != null) rowImg.color = isActive ? PopupRowActiveBackdrop : PopupRowBackdrop;

            Text rowT = row.GetComponentInChildren<Text>();
            if (rowT != null)
            {
                rowT.color = isActive ? PopupText : PopupMutedText;
                rowT.fontStyle = FontStyle.Normal;
                rowT.alignment = TextAnchor.MiddleLeft;
                VPBUiFont.ApplyTo(rowT);
                ApplyPopupMenuRowTextPadding(rowT, 1f);
            }

            AddLE(row, preferredHeight: preferredHeight, flexibleWidth: 1f);
            return row;
        }

        /// <summary>
        /// Stretch-width popup row (overflow/save menus): left-aligned label with inner text pad.
        /// Optional leading <paramref name="icon"/> (does not hide label).
        /// </summary>
        public static GameObject AddStretchPopupMenuRow(
            Transform panel,
            string label,
            UnityAction onClick,
            bool isActive = false,
            bool enabled = true,
            float rowHeight = 0f,
            Sprite icon = null)
        {
            if (panel == null || onClick == null) return null;
            if (rowHeight <= 0f) rowHeight = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            int fontSize = GalleryUiDesignTokens.PopupMenuOverflowFontRef;
            GameObject row = CreateUIButton(panel.gameObject, 0f, rowHeight, label, fontSize, 0f, 0f, AnchorPresets.stretchAll, onClick);
            if (row == null) return null;

            Image img = row.GetComponent<Image>();
            if (img != null) img.color = enabled
                ? (isActive ? PopupRowActiveBackdrop : PopupRowBackdrop)
                : new Color(0.2f, 0.2f, 0.2f, 0.7f);

            Button btn = row.GetComponent<Button>();
            if (btn != null) btn.interactable = enabled;

            float leftExtraRef = 0f;
            if (icon != null)
            {
                float iconSz = GalleryUiDesignTokens.PopupMenuRowIconSizeRef;
                float pad = GalleryUiDesignTokens.PopupMenuRowTextPadXRef;
                GameObject iconGO = new GameObject("RowIcon");
                iconGO.transform.SetParent(row.transform, false);
                Image iconImg = AddImage(iconGO, Color.white);
                UI.SetIconSprite(iconImg, icon);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(iconSz, iconSz);
                irt.anchoredPosition = new Vector2(pad, 0f);
                leftExtraRef = iconSz + GalleryUiDesignTokens.PopupMenuRowIconGapRef;
            }

            Text t = row.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.gameObject.SetActive(true);
                t.alignment = TextAnchor.MiddleLeft;
                t.fontStyle = FontStyle.Normal;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.color = enabled
                    ? (isActive ? PopupText : PopupMutedText)
                    : new Color(1f, 1f, 1f, 0.5f);
                try { VPBUiFont.ApplyTo(t); } catch { }
                ApplyPopupMenuRowTextPadding(t, 1f, leftExtraRef);
            }

            AddLE(row, preferredHeight: rowHeight, flexibleWidth: 1f);
            return row;
        }

        /// <summary>Inset popup-row label from left/right edges (scale with chrome). <paramref name="leftExtraRef"/> is unscaled (icon slot).</summary>
        public static void ApplyPopupMenuRowTextPadding(Text t, float scale, float leftExtraRef = 0f)
        {
            if (t == null) return;
            RectTransform rt = t.rectTransform;
            if (rt == null) return;
            if (scale <= 0f) scale = 1f;
            float pad = GalleryUiDesignTokens.PopupMenuRowTextPadXRef * scale;
            float left = pad + leftExtraRef * scale;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-pad, 0f);
        }

        /// <summary>Keep a popup panel's X inside its stretch overlay so edge anchors do not clip.</summary>
        public static void ClampPopupMenuPanelX(RectTransform panelRT, RectTransform overlayRT, float pad)
        {
            if (panelRT == null || overlayRT == null) return;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT); } catch { }
            float panelW = panelRT.rect.width;
            if (panelW < 1f) panelW = panelRT.sizeDelta.x;
            if (panelW < 1f) return;
            if (pad < 0f) pad = 0f;

            Rect o = overlayRT.rect;
            float pivotX = panelRT.pivot.x;
            float minX = o.xMin + pad + panelW * pivotX;
            float maxX = o.xMax - pad - panelW * (1f - pivotX);
            Vector2 pos = panelRT.anchoredPosition;
            if (maxX < minX)
                pos.x = (o.xMin + o.xMax) * 0.5f;
            else
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
            panelRT.anchoredPosition = pos;
        }

        /// <summary>
        /// Keep panel Y inside overlay. <paramref name="bottomFloorLocalY"/> is min allowed Y for panel bottom
        /// (e.g. top of tooltip/info bar) in overlay local space; null = overlay bottom + pad.
        /// </summary>
        public static void ClampPopupMenuPanelY(RectTransform panelRT, RectTransform overlayRT, float pad, float? bottomFloorLocalY = null)
        {
            if (panelRT == null || overlayRT == null) return;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT); } catch { }
            float panelH = panelRT.rect.height;
            if (panelH < 1f) panelH = panelRT.sizeDelta.y;
            if (panelH < 1f) return;
            if (pad < 0f) pad = 0f;

            Rect o = overlayRT.rect;
            float pivotY = panelRT.pivot.y;
            float floor = bottomFloorLocalY.HasValue ? bottomFloorLocalY.Value : (o.yMin + pad);
            float minY = floor + panelH * pivotY;
            float maxY = o.yMax - pad - panelH * (1f - pivotY);
            Vector2 pos = panelRT.anchoredPosition;
            if (maxY < minY)
                pos.y = minY; // prefer clearing bottom chrome when space is tight
            else
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
            panelRT.anchoredPosition = pos;
        }

        public static Sprite GetButtonIconSprite(GameObject buttonGO)
        {
            if (buttonGO == null) return null;
            Transform iconTr = buttonGO.transform.Find("Icon");
            if (iconTr == null) return null;
            Image img = iconTr.GetComponent<Image>();
            return img != null ? img.sprite : null;
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
            UIHoverBorder chromeHb = buttonGO.AddComponent<UIHoverBorder>();
            EnableChromeIdleRim(chromeHb);

            return buttonGO;
        }

        /// <summary>
        /// Layout-group chrome button: rounded fill, flat Button, hover border, fixed or flexible width.
        /// Used by modal headers/footers (scan whitelist, bench, quick-menu pos, category quick editor, etc.).
        /// Pass <paramref name="width"/> &lt;= 0 for flexibleWidth=1 (shares row with siblings).
        /// </summary>
        public static GameObject CreateChromeLayoutButton(Transform parent, float width, float height, string label, int fontSize, Color bg, UnityAction onClick)
        {
            GameObject go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            Image img = AddGalleryElementRoundedBg(go, bg);
            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            UIHoverBorder hb = go.AddComponent<UIHoverBorder>();
            try
            {
                hb.inward = true;
                EnableChromeIdleRim(hb);
                hb.ApplyBorderSettings();
            }
            catch { }

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                le.minWidth = le.preferredWidth = width;
                le.flexibleWidth = 0f;
            }
            else
            {
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
            }
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;

            CreateLabel(go, label ?? "", fontSize, Color.white, TextAnchor.MiddleCenter, name: "Text");
            return go;
        }

        /// <summary>
        /// Alternating stripe list row with trailing remove chrome button (scan whitelist / bench lists).
        /// Returns the remove button so callers can attach tooltips.
        /// </summary>
        public static GameObject CreateRemovableStripeRow(
            Transform parent,
            string label,
            int fontSize,
            float rowH,
            float removeW,
            float removeHeightInset,
            float spacing,
            RectOffset padding,
            bool altStripe,
            string removeLabel,
            UnityAction onRemove,
            bool flexibleRowWidth = false)
        {
            GameObject row = new GameObject("Row");
            row.transform.SetParent(parent, false);
            Image rowBg = AddGalleryElementRoundedBg(row, altStripe ? new Color(0.11f, 0.11f, 0.14f, 1f) : new Color(0.09f, 0.09f, 0.11f, 1f));
            rowBg.raycastTarget = true;
            AddHLG(row, spacing: spacing, padding: padding, childForceExpandWidth: false);
            if (flexibleRowWidth)
                AddLE(row, minWidth: 0f, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
            else
                AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text lt = CreateLabel(row, label ?? "", fontSize, new Color(0.92f, 0.92f, 0.94f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Label");
            AddLE(lt.gameObject, minWidth: 0f, flexibleWidth: 1f);

            return CreateChromeLayoutButton(row.transform, removeW, rowH - removeHeightInset, removeLabel, fontSize, new Color(0.52f, 0.28f, 0.28f, 1f), onRemove);
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
            UIHoverBorder iconHb = go.AddComponent<UIHoverBorder>();
            EnableChromeIdleRim(iconHb);

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

        private const int IconSpriteCacheLimit = 4096;
        private static readonly Dictionary<int, Color> _iconSpriteTint = new Dictionary<int, Color>();

        public static System.Collections.IEnumerator PrewarmIconCacheCoroutine()
        {
            yield return null;
            try { GalleryIconAtlas.EnsureLoaded(); }
            catch { }
            yield return null;
        }

        public static Color IconTintOf(Sprite sprite)
        {
            if (sprite == null) return Color.white;
            Color tint;
            if (_iconSpriteTint.TryGetValue(sprite.GetInstanceID(), out tint)) return tint;
            return Color.white;
        }

        public static void SetIconSprite(Image img, Sprite sprite)
        {
            if (img == null) return;
            img.sprite = sprite;
            img.color = IconTintOf(sprite);
        }

        public static void ClearIconSpriteCache()
        {
            Texture2D atlas = GalleryIconAtlas.Texture;
            foreach (KeyValuePair<string, Sprite> kv in _iconSpriteCache)
            {
                Sprite s = kv.Value;
                if (s == null) continue;
                Texture2D owned = s.texture;
                UnityEngine.Object.Destroy(s);
                if (owned != null && owned != atlas) UnityEngine.Object.Destroy(owned);
            }
            _iconSpriteCache.Clear();
            _iconSpriteTint.Clear();
            GalleryIconAtlas.Destroy();
        }

        /// <summary>Loads a Tabler source id (e.g. <c>shirt-off</c>, <c>filled/star</c>).</summary>
        public static Sprite LoadIconSprite(string iconRole, Color? recolorTo = null)
        {
            try
            {
                string role = GalleryIconAtlas.ToAtlasKey(iconRole);
                if (string.IsNullOrEmpty(role)) return null;

                Color tint = recolorTo.HasValue ? recolorTo.Value : Color.white;
                string cacheKey = recolorTo.HasValue
                    ? role + "|" + tint.r.ToString("F3") + "," + tint.g.ToString("F3") + "," + tint.b.ToString("F3")
                    : role;

                if (_iconSpriteCache.TryGetValue(cacheKey, out Sprite cached) && cached != null)
                    return cached;

                if (_iconSpriteCache.Count >= IconSpriteCacheLimit)
                {
                    _iconSpriteCache.Clear();
                    _iconSpriteTint.Clear();
                }

                Color spriteTint = Color.white;
                Sprite sprite = CreateLooseIconSprite(GalleryIconAtlas.OverridePathFor(role), recolorTo);
                if (sprite == null)
                {
                    sprite = CreateAtlasIconSprite(role);
                    if (sprite != null) spriteTint = tint;
                }
                if (sprite == null) return null;

                _iconSpriteCache[cacheKey] = sprite;
                _iconSpriteTint[sprite.GetInstanceID()] = spriteTint;
                return sprite;
            }
            catch { return null; }
        }

        private static Sprite CreateAtlasIconSprite(string iconRole)
        {
            if (!GalleryIconAtlas.EnsureLoaded()) return null;
            Rect rect;
            if (!GalleryIconAtlas.TryGetRect(iconRole, out rect)) return null;
            return Sprite.Create(GalleryIconAtlas.Texture, rect, new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateLooseIconSprite(string relativePathFromPluginsDir, Color? recolorTo)
        {
            if (string.IsNullOrEmpty(relativePathFromPluginsDir)) return null;
            string fullPath = Path.Combine(BepInEx.Paths.PluginPath, relativePathFromPluginsDir);
            if (!File.Exists(fullPath)) return null;
            byte[] bytes = File.ReadAllBytes(fullPath);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            if (recolorTo.HasValue)
            {
                Color c = recolorTo.Value;
                Color32[] pixels = tex.GetPixels32();
                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 12)
                    {
                        pixels[i].r = r;
                        pixels[i].g = g;
                        pixels[i].b = b;
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
            }
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Raised icon well (resize handles, perf-on fill). Idle bar/toolbox chips use ChromeIconWell.</summary>
        public static readonly Color IconButtonBackdrop = GalleryUiColorTokens.SurfaceIconBtn;

        /// <summary>Recolor passed to <see cref="LoadIconSprite"/> for gallery left/right rail icons (glyph pixels only).</summary>
        public static readonly Color SideRailIconGlyphTint = Color.white;

        /// <summary>Neutral glyph tint for top/bottom bar icons (glyph pixels only).</summary>
        public static readonly Color BarIconGlyphTint = Color.white;

        /// <summary>
        /// Adds an icon Image child to <paramref name="buttonGO"/>, hides its text label, and sets
        /// the button's background to <paramref name="backdropOverride"/> (or
        /// <see cref="GalleryUiColorTokens.ChromeIconWell"/> when null). Pass an override for accents
        /// (confirm / destroy / armed) or a raised well (<see cref="IconButtonBackdrop"/>).
        /// </summary>
        public static void AddIconToButton(GameObject buttonGO, Sprite icon, float padding = 4f, Color? backdropOverride = null)
        {
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? GalleryUiColorTokens.ChromeIconWell;

            // Hide text — icon replaces it; text remains as fallback when icon is absent
            Text t = buttonGO.GetComponentInChildren<Text>(true);
            if (t != null) t.gameObject.SetActive(false);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = AddImage(iconGO, Color.white);
            UI.SetIconSprite(img, icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            SizeButtonIcon(rt, buttonGO.GetComponent<RectTransform>(), padding);
        }

        /// <summary>Stretch-inset to the painted rect. Do not bake sizeDelta — buttons start at Unity 100² / LE / VR world.</summary>
        internal static void SizeButtonIcon(RectTransform iconRT, RectTransform buttonRT, float padding)
        {
            if (iconRT == null) return;
            if (padding < 0f) padding = 0f;
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.sizeDelta = new Vector2(-padding * 2f, -padding * 2f);
            iconRT.anchoredPosition = Vector2.zero;
        }

        /// <summary>Updates or creates the Icon child from atlas role <paramref name="iconRole"/> using bar glyph tint.</summary>
        public static void RegisterIconButtonPath(GameObject buttonGO, string iconRole, float padding = 4f, Color? backdropOverride = null)
        {
            ApplyBarIconFromPath(buttonGO, iconRole, padding, backdropOverride);
        }

        public static bool ApplyBarIconFromPath(GameObject buttonGO, string iconRole, float padding = 4f, Color? backdropOverride = null)
        {
            if (buttonGO == null || string.IsNullOrEmpty(iconRole)) return false;
            Sprite s = LoadIconSprite(iconRole, BarIconGlyphTint);
            if (s == null) return false;
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? GalleryUiColorTokens.ChromeIconWell;
            Transform iconTr = buttonGO.transform.Find("Icon");
            if (iconTr != null)
            {
                Image img = iconTr.GetComponent<Image>();
                if (img != null)
                {
                    UI.SetIconSprite(img, s);
                    SizeButtonIcon(iconTr as RectTransform, buttonGO.GetComponent<RectTransform>(), padding);
                    return true;
                }
            }
            AddIconToButton(buttonGO, s, padding, backdropOverride);
            return true;
        }

        /// <summary>Like <see cref="ApplyBarIconFromPath"/> but uses <see cref="SideRailIconGlyphTint"/>.</summary>
        public static bool ApplySideRailIconFromPath(GameObject buttonGO, string iconRole, float padding = 4f, Color? backdropOverride = null)
        {
            if (buttonGO == null || string.IsNullOrEmpty(iconRole)) return false;
            Sprite s = LoadIconSprite(iconRole, SideRailIconGlyphTint);
            if (s == null) return false;
            Image btnImg = buttonGO.GetComponent<Image>();
            if (btnImg != null) btnImg.color = backdropOverride ?? GalleryUiColorTokens.ChromeIconWell;
            Transform iconTr = buttonGO.transform.Find("Icon");
            if (iconTr != null)
            {
                Image img = iconTr.GetComponent<Image>();
                if (img != null)
                {
                    UI.SetIconSprite(img, s);
                    SizeButtonIcon(iconTr as RectTransform, buttonGO.GetComponent<RectTransform>(), padding);
                    return true;
                }
            }
            AddIconToButton(buttonGO, s, padding, backdropOverride);
            return true;
        }

        /// <summary>Swaps a live button icon after theme change.</summary>
        public static void SetButtonIconGlyph(Image iconImage, Sprite sprite)
        {
            if (iconImage == null || sprite == null) return;
            UI.SetIconSprite(iconImage, sprite);
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
