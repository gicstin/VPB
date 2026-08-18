using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal static int CurrentLayoutPresetMode()
        {
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { }
            return vr ? (int)LayoutPresetMode.VR : (int)LayoutPresetMode.Desktop;
        }

        /// <summary>
        /// Snapshot of the live arrangement, stamped with the running interaction mode.
        /// This pane is captured first so apply can reconcile against the coroutine host.
        /// </summary>
        internal GalleryLayoutPreset CaptureCurrentLayout(string name)
        {
            var preset = new GalleryLayoutPreset();
            preset.Name = name ?? "";
            preset.Mode = CurrentLayoutPresetMode();
            preset.UpdatedUtc = DateTime.UtcNow.ToBinary();
            preset.Global = CaptureLayoutGlobalState();

            LayoutPaneState own = CapturePaneState();
            if (own != null) preset.Panes.Add(own);

            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return preset;

            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null || p == this) continue;
                LayoutPaneState s = null;
                try { s = p.CapturePaneState(); }
                catch (Exception ex) { LogUtil.LogError("[VPB][Layout] capture pane: " + ex.Message); }
                if (s != null) preset.Panes.Add(s);
            }
            return preset;
        }

        private static LayoutGlobalState CaptureLayoutGlobalState()
        {
            var g = new LayoutGlobalState();
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return g;

            g.GalleryLayoutMode = cfg.GalleryLayoutMode;
            g.InnerPaneScale = cfg.CurrentInnerPaneScale;

            g.DetailStripExpanded = cfg.GalleryDetailStripExpanded;
            g.DetailStripSideInfo = cfg.GalleryDetailStripSideInfoEnabled;
            g.DetailStripThumbOnRight = cfg.GalleryDetailStripThumbOnRight;
            g.DetailStripHeightRef = cfg.GalleryDetailStripHeightRef;

            g.OnlyWhenVamMenuVisible = cfg.GalleryOnlyWhenVamMenuVisible;

            g.DesktopFixedMode = cfg.DesktopFixedMode;
            g.DefaultDockSide = VPBConfig.NormalizeDesktopFixedDockSide(cfg.DesktopFixedDefaultDockSide);
            g.EnforceDockSide = cfg.DesktopFixedEnforceDockSide;
            g.EnforcedDockSide = VPBConfig.NormalizeDesktopFixedDockSide(cfg.DesktopFixedEnforcedDockSide);
            g.AutoHideSeconds = cfg.DesktopFixedAutoHideSeconds;
            g.DockLeft.CaptureFrom(cfg.DockLeft);
            g.DockTop.CaptureFrom(cfg.DockTop);
            g.DockRight.CaptureFrom(cfg.DockRight);

            g.AnchorToVamMenu = cfg.GalleryAnchorToVamMenu;
            g.AnchorOffset = cfg.GalleryAnchorOffset;
            g.VrMenuAnchorTiltDeg = cfg.GalleryVrMenuAnchorTiltDeg;
            g.AnchorYieldsToVamPanels = cfg.AnchorYieldsToVamPanels;

            g.FollowEyeHeight = cfg.FollowEyeHeight;
            g.FollowDistance = cfg.FollowDistance;
            g.FollowAngle = cfg.FollowAngle;
            return g;
        }

        internal LayoutPaneState CapturePaneState()
        {
            var p = new LayoutPaneState();

            p.DockSlot = isFixedLocally ? (int)EffectiveDockSide : (int)GalleryDockSide.None;
            p.Collapsed = isCollapsed;

            CapturePaneWorldPose(out p.LocalPos, out p.LocalRot);

            RectTransform bgRT = GetBackgroundRT();
            if (bgRT != null && !isFixedLocally) p.SizeRef = bgRT.sizeDelta;

            VPBConfig cfg = VPBConfig.Instance;
            p.AnchoredToVamMenu = cfg != null && cfg.GalleryAnchorToVamMenu;
            p.FollowUser = GetFollowMode();

            p.CategoryTitle = GetTitle() ?? "";
            p.CategoryPath = currentPath ?? "";
            p.CategoryExtension = currentExtension ?? "";

            p.LeftContent = ContentTypeToPresetInt(leftActiveContent);
            p.RightContent = ContentTypeToPresetInt(rightActiveContent);

            p.ImportOpen = importSidebarOpenIntent;
            p.ImportOnLeft = importSidebarOnLeft;
            p.ImportFloating = importSidebarDetached;
            p.GridColumnCount = gridColumnCount;

            try { CaptureFloatStates(p.Floats); }
            catch (Exception ex) { LogUtil.LogError("[VPB][Layout] capture floats: " + ex.Message); }

            try { p.Filter = CaptureQuickFilterState(""); }
            catch { p.Filter = null; }

            return p;
        }

        /// <summary>
        /// Pose in the player-UI root frame (VaM's mainHUDAttachPoint), which rides the player rig —
        /// a world pose would be meaningless after a teleport or a world-scale change.
        /// </summary>
        private void CapturePaneWorldPose(out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero;
            localRot = Quaternion.identity;
            if (canvas == null) return;

            Transform t = canvas.transform;
            Transform root = null;
            try { root = VpbWorldSpaceUiScale.GetPlayerUiRoot(); } catch { root = null; }

            if (root == null)
            {
                localPos = t.position;
                localRot = t.rotation;
                return;
            }

            if (t.parent == root)
            {
                localPos = t.localPosition;
                localRot = t.localRotation;
                return;
            }

            localPos = root.InverseTransformPoint(t.position);
            localRot = Quaternion.Inverse(root.rotation) * t.rotation;
        }

        /// <summary>Human-readable default name derived from the arrangement itself.</summary>
        internal static string BuildSuggestedLayoutName(GalleryLayoutPreset preset)
        {
            if (preset == null) return "Layout";

            var sb = new StringBuilder(64);
            int paneCount = preset.Panes != null ? preset.Panes.Count : 0;
            if (paneCount > 1)
            {
                sb.Append(paneCount);
                sb.Append(" panes");
            }
            else if (paneCount == 1)
            {
                LayoutPaneState p = preset.Panes[0];
                GalleryDockSide side = (GalleryDockSide)p.DockSlot;
                if (side == GalleryDockSide.None)
                    sb.Append("Floating");
                else
                {
                    sb.Append("Docked ");
                    sb.Append(GalleryDockLayout.ToConfigString(side).ToLowerInvariant());
                }
            }
            else
            {
                sb.Append("Layout");
            }

            if (paneCount > 0)
            {
                string cat = preset.Panes[0] != null ? preset.Panes[0].CategoryTitle : null;
                if (!string.IsNullOrEmpty(cat))
                {
                    sb.Append(" · ");
                    sb.Append(cat);
                }
            }

            return sb.ToString();
        }

        internal static string EnsureUniqueLayoutName(string baseName, HashSet<string> existingNames)
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Layout";
            if (existingNames == null || !existingNames.Contains(baseName)) return baseName;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!existingNames.Contains(candidate)) return candidate;
            }
            return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
