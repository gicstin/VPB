using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Coroutine _layoutApplyCo;
        private GalleryLayoutPreset _layoutUndoSnapshot;
        private static bool s_layoutApplyRunning;
        private readonly List<GalleryPanel> _layoutApplyOrder = new List<GalleryPanel>(8);

        internal static bool IsLayoutApplyRunning
        {
            get { return s_layoutApplyRunning; }
        }

        internal GalleryLayoutPreset LayoutUndoSnapshot
        {
            get { return _layoutUndoSnapshot; }
        }

        /// <summary>
        /// Starts a layout apply. Multi-frame by design: pane creation drives a grid rebuild and
        /// thumbnail scheduling, so several panes in one frame is a visible stall — and in VR a stall
        /// is a comfort event.
        /// </summary>
        internal bool ApplyLayoutPreset(GalleryLayoutPreset preset, bool takeUndoSnapshot)
        {
            if (preset == null) return false;

            if (preset.Mode != CurrentLayoutPresetMode())
            {
                ShowTemporaryStatus(preset.IsVrPreset
                    ? VPBTranslation.T("gallery.status.layout_vr_only", "That layout is VR-only.")
                    : VPBTranslation.T("gallery.status.layout_desktop_only", "That layout is desktop-only."), 2.5f);
                return false;
            }

            if (s_layoutApplyRunning) return false;
            if (LayoutApplyShouldAbort(true)) return false;

            if (takeUndoSnapshot)
            {
                try { _layoutUndoSnapshot = CaptureCurrentLayout(""); }
                catch { _layoutUndoSnapshot = null; }
            }

            if (_layoutApplyCo != null)
            {
                StopCoroutine(_layoutApplyCo);
                _layoutApplyCo = null;
            }
            _layoutApplyCo = StartCoroutine(ApplyLayoutPresetCo(preset));
            return true;
        }

        /// <summary>Conditions under which an apply must not start, or must stop mid-flight.</summary>
        private bool LayoutApplyShouldAbort(bool report)
        {
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.IsLoadingScene)
                {
                    if (report)
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.status.layout_busy", "Scene is loading — try again in a moment."), 2f);
                    return true;
                }
            }
            catch { }

            try
            {
                if (dragger != null && dragger.isDragging)
                {
                    if (report)
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.status.layout_dragging", "Finish moving the pane first."), 2f);
                    return true;
                }
            }
            catch { }

            if (Gallery.singleton == null) return true;
            return false;
        }

        private IEnumerator ApplyLayoutPresetCo(GalleryLayoutPreset preset)
        {
            s_layoutApplyRunning = true;
            try
            {
                try { ApplyLayoutGlobalState(preset); }
                catch (Exception ex) { LogUtil.LogError("[VPB][Layout] global apply: " + ex.Message); }

                yield return null;
                if (this == null || canvas == null) yield break;

                int want = preset.Panes != null ? preset.Panes.Count : 0;
                if (want <= 0) yield break;
                if (want > Gallery.MaxPanels)
                {
                    want = Gallery.MaxPanels;
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("gallery.status.layout_pane_clamp", "Layout clamped to {0} panes."),
                        want), 2.5f);
                }

                // Release every claim before re-claiming, so no transient state double-books an edge.
                if (!preset.IsVrPreset)
                {
                    try { ReleaseAllLayoutDockClaims(); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] dock release: " + ex.Message); }
                }

                BuildLayoutApplyOrder();

                // Close surplus panes from the end. Index 0 is this pane — the coroutine host must survive.
                while (_layoutApplyOrder.Count > want)
                {
                    int last = _layoutApplyOrder.Count - 1;
                    GalleryPanel victim = _layoutApplyOrder[last];
                    _layoutApplyOrder.RemoveAt(last);
                    if (victim == null || victim == this) continue;

                    try { victim.Close(); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] close pane: " + ex.Message); }

                    yield return null;
                    if (this == null || canvas == null) yield break;
                }

                // Create missing panes one per frame — each one rebuilds a grid.
                while (_layoutApplyOrder.Count < want)
                {
                    int before = Gallery.singleton != null ? Gallery.singleton.PanelCount : 0;
                    try { if (Gallery.singleton != null) Gallery.singleton.CreatePane(null, false); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] create pane: " + ex.Message); }

                    yield return null;
                    if (this == null || canvas == null) yield break;

                    int after = Gallery.singleton != null ? Gallery.singleton.PanelCount : 0;
                    if (after <= before) break;

                    GalleryPanel added = Gallery.singleton.Panels[after - 1];
                    if (added == null) break;
                    _layoutApplyOrder.Add(added);
                }

                int applied = _layoutApplyOrder.Count < want ? _layoutApplyOrder.Count : want;
                for (int i = 0; i < applied; i++)
                {
                    GalleryPanel target = _layoutApplyOrder[i];
                    LayoutPaneState state = preset.Panes[i];
                    if (target == null || state == null) continue;

                    if (LayoutApplyShouldAbort(false))
                    {
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.status.layout_aborted", "Layout apply stopped — undo is still available."), 2.5f);
                        yield break;
                    }

                    try { target.ApplyPaneDockMode(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] dock mode: " + ex.Message); }

                    // The render-mode flip resets anchors, size and parenting a frame later.
                    yield return null;
                    if (this == null || canvas == null) yield break;
                    if (target == null) continue;

                    try { target.ApplyPaneGeometry(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] geometry: " + ex.Message); }

                    yield return null;
                    if (this == null || canvas == null) yield break;
                    if (target == null) continue;

                    try { target.ApplyPaneGridColumns(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] grid columns: " + ex.Message); }

                    try { target.ApplyPaneCategory(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] category: " + ex.Message); }

                    yield return null;
                    if (this == null || canvas == null) yield break;
                    if (target == null) continue;

                    try { target.ApplyPaneRails(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] rails: " + ex.Message); }

                    // Import sidebar can be docked or floating and changes the usable width other
                    // floats are placed against, so it must settle before they are positioned.
                    yield return null;
                    if (this == null || canvas == null) yield break;
                    if (target == null) continue;

                    try { target.ApplyPaneFloats(state); }
                    catch (Exception ex) { LogUtil.LogError("[VPB][Layout] floats: " + ex.Message); }

                    if (preset.RestoreFilters && state.Filter != null)
                    {
                        yield return null;
                        if (this == null || canvas == null) yield break;
                        if (target == null) continue;

                        try { target.ApplyQuickFilterState(state.Filter, false, true); }
                        catch (Exception ex) { LogUtil.LogError("[VPB][Layout] filters: " + ex.Message); }
                    }

                    // After ApplyPaneCategory (Show clears the mode). Dock-shape presets are edges only.
                    if (!preset.DockShapeOnly)
                    {
                        try { target.ApplyPaneFloatsOnly(state); }
                        catch (Exception ex) { LogUtil.LogError("[VPB][Layout] floats-only: " + ex.Message); }
                    }
                }

                yield return null;
                if (this == null || canvas == null) yield break;

                for (int i = 0; i < _layoutApplyOrder.Count; i++)
                {
                    GalleryPanel target = _layoutApplyOrder[i];
                    if (target == null) continue;
                    try
                    {
                        target.ApplyInnerPaneScale();
                        target.UpdateLayout();
                        target.UpdateTabs();
                    }
                    catch { }
                }

                try
                {
                    if (VPBConfig.Instance != null) VPBConfig.Instance.Save(false, true);
                }
                catch { }

                if (_layoutUndoSnapshot != null) ShowLayoutRevertBar(preset.Name);
                else if (!string.IsNullOrEmpty(preset.Name))
                {
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("gallery.status.layout_applied", "Layout: {0}"), preset.Name), 2f);
                }
            }
            finally
            {
                s_layoutApplyRunning = false;
                _layoutApplyCo = null;
                _layoutApplyOrder.Clear();
            }
        }

        /// <summary>This pane first — it hosts the coroutine and must outlive the reconcile.</summary>
        private void BuildLayoutApplyOrder()
        {
            _layoutApplyOrder.Clear();
            _layoutApplyOrder.Add(this);

            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p != null && p != this) _layoutApplyOrder.Add(p);
            }
        }

        private static void ReleaseAllLayoutDockClaims()
        {
            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null) continue;
                try { p.ReleaseDockSide(); } catch { }
            }
        }

        private void ApplyLayoutGlobalState(GalleryLayoutPreset preset)
        {
            LayoutGlobalState g = preset != null ? preset.Global : null;
            VPBConfig cfg = VPBConfig.Instance;
            if (g == null || cfg == null) return;

            if (preset.DockShapeOnly)
            {
                if (!preset.IsVrPreset)
                {
                    g.DockLeft.ApplySizingTo(cfg.DockLeft);
                    g.DockTop.ApplySizingTo(cfg.DockTop);
                    g.DockRight.ApplySizingTo(cfg.DockRight);
                    GalleryDockLayout.BumpVersion();
                }
                try { cfg.TriggerChange(); } catch { }
                return;
            }

            cfg.GalleryLayoutMode = g.GalleryLayoutMode;
            layoutMode = (GalleryLayoutMode)g.GalleryLayoutMode;

            cfg.InnerPaneScale = g.InnerPaneScale;

            cfg.GalleryDetailStripExpanded = g.DetailStripExpanded;
            cfg.GalleryDetailStripSideInfoEnabled = g.DetailStripSideInfo;
            cfg.GalleryDetailStripThumbOnRight = g.DetailStripThumbOnRight;
            cfg.GalleryDetailStripHeightRef = g.DetailStripHeightRef;

            cfg.GalleryOnlyWhenVamMenuVisible = g.OnlyWhenVamMenuVisible;

            cfg.FollowEyeHeight = g.FollowEyeHeight;
            cfg.FollowDistance = g.FollowDistance;
            cfg.FollowAngle = g.FollowAngle;

            if (preset.IsVrPreset)
            {
                cfg.GalleryAnchorToVamMenu = g.AnchorToVamMenu;
                cfg.GalleryAnchorOffset = g.AnchorOffset;
                cfg.GalleryVrMenuAnchorTiltDeg = VPBConfig.ClampGalleryVrMenuAnchorTiltDeg(g.VrMenuAnchorTiltDeg);
                cfg.AnchorYieldsToVamPanels = g.AnchorYieldsToVamPanels;
            }
            else
            {
                cfg.DesktopFixedDefaultDockSide = VPBConfig.NormalizeDesktopFixedDockSide(g.DefaultDockSide);
                cfg.DesktopFixedEnforceDockSide = g.EnforceDockSide;
                cfg.DesktopFixedEnforcedDockSide = VPBConfig.NormalizeDesktopFixedDockSide(g.EnforcedDockSide);
                cfg.DesktopFixedAutoHideSeconds = g.AutoHideSeconds;

                // Sizing only — which pane owns which edge is decided by the claims below.
                g.DockLeft.ApplySizingTo(cfg.DockLeft);
                g.DockTop.ApplySizingTo(cfg.DockTop);
                g.DockRight.ApplySizingTo(cfg.DockRight);
                GalleryDockLayout.BumpVersion();
            }

            try { cfg.TriggerChange(); } catch { }
        }

        internal void ApplyPaneDockMode(LayoutPaneState pane)
        {
            GalleryDockSide want = (GalleryDockSide)pane.DockSlot;
            bool wantFixed = want != GalleryDockSide.None;

            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { }
            if (vr) wantFixed = false;

            if (!wantFixed)
            {
                if (isFixedLocally) SetFixedLocally(false);
                return;
            }

            if (!GalleryDockLayout.IsFreeFor(want, PanelId))
            {
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.layout_dock_taken", "Dock edge {0} is in use — kept floating."),
                    GalleryDockLayout.ToConfigString(want)), 2.5f);
                if (isFixedLocally) SetFixedLocally(false);
                return;
            }

            GalleryDockLayout.TryClaim(want, PanelId);
            InvalidateDockSideCache();
            isFixedLocally = true;

            VPBConfig cfg = VPBConfig.Instance;
            if (cfg != null)
            {
                cfg.DesktopFixedMode = true;
                cfg.DesktopFixedDockSide = GalleryDockLayout.ToConfigString(want);
            }

            UpdateDockAnchorButton();
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
        }

        internal void ApplyPaneGeometry(LayoutPaneState pane)
        {
            SetVamMenuAnchorOptIn(pane.AnchoredToVamMenu);

            if (isFixedLocally)
            {
                SetCollapsed(pane.Collapsed);
                ApplyDockAnchorsImmediate();
                UpdateSideButtonsVisibility();
                return;
            }

            if (isCollapsed) SetCollapsed(false);

            RectTransform bgRT = GetBackgroundRT();
            if (bgRT != null && pane.SizeRef.x > 1f && pane.SizeRef.y > 1f)
                bgRT.sizeDelta = pane.SizeRef;

            ApplyPanePose(pane);
            SetFollowMode(pane.FollowUser);
            ResetFollowOffsets();
            hasBeenPositioned = true;
        }

        /// <summary>
        /// Restores the pose inside the player-UI root frame. Assigning a world rotation would round-trip
        /// through the parent's extracted rotation, which is undefined for a scaled basis.
        /// </summary>
        private void ApplyPanePose(LayoutPaneState pane)
        {
            if (canvas == null) return;

            // A captured pose is never exactly the origin — that is inside the player's head. The
            // baselines use it as "no pose recorded", meaning place the pane in front of the user.
            if (pane.LocalPos == Vector3.zero)
            {
                RepositionInFront();
                return;
            }

            Transform t = canvas.transform;
            Transform root = null;
            try { root = VpbWorldSpaceUiScale.GetPlayerUiRoot(); } catch { root = null; }

            if (root == null)
            {
                LogUtil.LogWarning("[VPB][Layout] player UI root missing — pane placed in front of camera.");
                RepositionInFront();
                return;
            }

            if (t.parent != root)
            {
                try { VpbWorldSpaceUiScale.AttachToPlayerUiSpace(t); }
                catch { }
            }

            Vector3 pos = ClampPoseToComfortEnvelope(pane.LocalPos);

            if (t.parent == root)
            {
                t.localPosition = pos;
                t.localRotation = pane.LocalRot;
            }
            else
            {
                t.position = root.TransformPoint(pos);
                t.rotation = root.rotation * pane.LocalRot;
            }
        }

        internal const float LayoutComfortMinDistance = 0.35f;
        internal const float LayoutComfortMaxDistance = 2.5f;
        internal const float LayoutComfortMaxYawDeg = 100f;
        internal const float LayoutComfortMinHeight = -0.8f;
        internal const float LayoutComfortMaxHeight = 1.0f;

        /// <summary>
        /// Guard rail only (VR): a pose captured under a different world scale or a broken rig could land
        /// the pane behind the user or at a hostile distance. Anything inside the envelope is restored
        /// verbatim — this must never become a routine tidy-up pass.
        /// </summary>
        private static Vector3 ClampPoseToComfortEnvelope(Vector3 localPos)
        {
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { }
            if (!vr) return localPos;

            Vector3 result = localPos;
            bool clamped = false;

            float y = result.y;
            if (y < LayoutComfortMinHeight) { y = LayoutComfortMinHeight; clamped = true; }
            else if (y > LayoutComfortMaxHeight) { y = LayoutComfortMaxHeight; clamped = true; }

            Vector2 flat = new Vector2(result.x, result.z);
            float dist = flat.magnitude;
            if (dist < 0.0001f)
            {
                flat = new Vector2(0f, LayoutComfortMinDistance);
                dist = LayoutComfortMinDistance;
                clamped = true;
            }
            else if (dist < LayoutComfortMinDistance)
            {
                flat = flat * (LayoutComfortMinDistance / dist);
                clamped = true;
            }
            else if (dist > LayoutComfortMaxDistance)
            {
                flat = flat * (LayoutComfortMaxDistance / dist);
                clamped = true;
            }

            float yaw = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;
            if (yaw > LayoutComfortMaxYawDeg || yaw < -LayoutComfortMaxYawDeg)
            {
                float limited = Mathf.Clamp(yaw, -LayoutComfortMaxYawDeg, LayoutComfortMaxYawDeg) * Mathf.Deg2Rad;
                float len = flat.magnitude;
                flat = new Vector2(Mathf.Sin(limited) * len, Mathf.Cos(limited) * len);
                clamped = true;
            }

            if (!clamped) return localPos;

            LogUtil.LogWarning("[VPB][Layout] pose outside comfort envelope — clamped from " + localPos);
            return new Vector3(flat.x, y, flat.y);
        }

        internal void ApplyPaneGridColumns(LayoutPaneState pane)
        {
            if (pane == null || pane.GridColumnCount < 1) return;
            int cols = pane.GridColumnCount;
            if (cols > 12) cols = 12;
            if (gridColumnCount == cols) return;
            gridColumnCount = cols;
            RebuildGridLayout();
        }

        internal void ApplyPaneCategory(LayoutPaneState pane)
        {
            if (string.IsNullOrEmpty(pane.CategoryTitle) && string.IsNullOrEmpty(pane.CategoryPath))
                return;

            string title = pane.CategoryTitle;
            string ext = pane.CategoryExtension;
            string path = pane.CategoryPath;

            if (!LayoutCategoryStillExists(title))
            {
                Gallery.Category fallback = FirstAvailableCategory();
                if (string.IsNullOrEmpty(fallback.name))
                    return;

                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.status.layout_category_missing", "Category '{0}' is gone — opened {1}."),
                    title, fallback.name), 3f);

                title = fallback.name;
                ext = fallback.extension;
                path = fallback.path;
            }

            Show(title, ext, path);
        }

        private bool LayoutCategoryStillExists(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            if (categories == null) return false;
            for (int i = 0; i < categories.Count; i++)
            {
                if (string.Equals(categories[i].name, title, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private Gallery.Category FirstAvailableCategory()
        {
            if (categories != null && categories.Count > 0) return categories[0];
            return new Gallery.Category();
        }

        internal void ApplyPaneRails(LayoutPaneState pane)
        {
            ContentType? left = PresetIntToContentType(pane.LeftContent);
            ContentType? right = PresetIntToContentType(pane.RightContent);

            leftActiveContent = left;
            rightActiveContent = right;

            importSidebarOpenIntent = pane.ImportOpen;
            importSidebarOpenIntentLoaded = true;
            importSidebarOnLeft = pane.ImportOnLeft;
            if (pane.ImportOpen)
                importSidebarForceOnLeft = pane.ImportOnLeft;

            // Docked-vs-floating is its own axis; without this the rail reopens on the wrong surface.
            if (pane.ImportOpen && pane.ImportFloating && !importSidebarDetached)
            {
                try { ToggleFloatingImportSidebar(); } catch { }
            }

            try { RefreshImportSidebarCategoryGate(); } catch { }
            try { PersistImportSidebarOpenIntent(); } catch { }

            UpdateLayout();
            UpdateTabs();
        }

        /// <summary>Restores the arrangement captured immediately before the last apply.</summary>
        internal bool RevertLayoutToSnapshot()
        {
            GalleryLayoutPreset snap = _layoutUndoSnapshot;
            if (snap == null)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_no_undo", "No layout change to undo."), 2f);
                return false;
            }
            _layoutUndoSnapshot = null;
            GalleryLayoutPresetStore.ClearActive();
            return ApplyLayoutPreset(snap, false);
        }

        /// <summary>Implicit per-mode "last layout" — the always-available way back, independent of named presets.</summary>
        internal void SaveLastLayoutSnapshot()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            try
            {
                GalleryLayoutPreset snap = CaptureCurrentLayout("");
                string json = snap.ToJsonString();
                if (snap.IsVrPreset) cfg.LastLayoutSnapshotVR = json;
                else cfg.LastLayoutSnapshotDesktop = json;
                cfg.Save(false, true);
                ShowTemporaryStatus(VPBTranslation.T("gallery.status.layout_saved", "Layout saved"), 1.5f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] save snapshot: " + ex.Message); }
        }

        internal void ApplyLastLayoutSnapshot()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;

            string json = CurrentLayoutPresetMode() == (int)LayoutPresetMode.VR
                ? cfg.LastLayoutSnapshotVR
                : cfg.LastLayoutSnapshotDesktop;

            GalleryLayoutPreset snap = GalleryLayoutPreset.FromJsonString(json);
            if (snap == null)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.status.layout_none", "No saved layout for this mode yet."), 2f);
                return;
            }
            ApplyLayoutPreset(snap, true);
        }
    }
}
