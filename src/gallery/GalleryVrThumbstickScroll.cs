using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// When a VR pointer is over a gallery pane, thumbstick forward/back scrolls the hovered
    /// <see cref="ScrollRect"/> instead of moving the navigation rig (see VAM <c>LookInputModule</c> UIScroll).
    /// </summary>
    public static class GalleryVrThumbstickScroll
    {
        private const float StickDeadzone = 0.12f;
        private const float MaxViewportHeightsPerSecond = 2.25f;

        private static int _lastTickFrame = -1;

        private static readonly MethodInfo GetFreeNavigateVectorMethod =
            typeof(SuperController).GetMethod("GetFreeNavigateVector", BindingFlags.Instance | BindingFlags.Public);

        private static readonly FieldInfo FreeMoveActionField =
            typeof(SuperController).GetField("freeMoveAction", BindingFlags.Instance | BindingFlags.Public);

        private static readonly FieldInfo FreeModeMoveActionField =
            typeof(SuperController).GetField("freeModeMoveAction", BindingFlags.Instance | BindingFlags.Public);

        public static bool ShouldSuppressFreeNavigate { get; private set; }

        public static bool IsFeatureEnabled()
        {
            try
            {
                if (VPBConfig.Instance == null) return true;
                return VPBConfig.Instance.GalleryVrThumbstickScrollEnabled;
            }
            catch { return true; }
        }

        public static void TickOncePerFrame()
        {
            if (Time.frameCount == _lastTickFrame) return;
            _lastTickFrame = Time.frameCount;
            ShouldSuppressFreeNavigate = false;

            if (!IsFeatureEnabled()) return;
            if (!XrUtils.IsVrActive()) return;
            if (Gallery.singleton == null) return;

            GalleryPanel panel = FindPointerOverPanel();
            if (panel == null) return;

            float stickForward = ReadNavigateForwardAxis();
            if (Mathf.Abs(stickForward) <= StickDeadzone) return;

            // Settings hover-preview placeholder: stick resizes instead of scrolling lists.
            try
            {
                if (panel.TryApplyVrThumbstickHoverPreviewSize(stickForward))
                {
                    ShouldSuppressFreeNavigate = true;
                    return;
                }
            }
            catch { }

            ScrollRect scroll = panel.ResolveScrollRectForVrThumbstick();
            if (scroll == null || !scroll.vertical) return;

            ApplyVerticalScroll(scroll, stickForward);
            ShouldSuppressFreeNavigate = true;
        }

        private static GalleryPanel FindPointerOverPanel()
        {
            var panels = Gallery.singleton.Panels;
            if (panels == null) return null;

            GalleryPanel best = null;
            int bestHover = -1;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null || !p.IsVisible) continue;
                if (p.canvas == null || !p.canvas.enabled) continue;
                if (!p.IsVrPointerOverGalleryPane()) continue;

                int h = p.VrPointerHoverCount;
                if (h > bestHover)
                {
                    bestHover = h;
                    best = p;
                }
            }

            if (best != null) return best;

            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null || !p.IsVisible) continue;
                if (p.canvas == null || !p.canvas.enabled) continue;
                if (p.IsVrPointerOverGalleryPane()) return p;
            }

            return null;
        }

        private static float ReadNavigateForwardAxis()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return 0f;
            try
            {
                float yMove = ReadNavigateForwardAxis(sc, FreeMoveActionField);
                float yFree = ReadNavigateForwardAxis(sc, FreeModeMoveActionField);
                return Mathf.Abs(yFree) >= Mathf.Abs(yMove) ? yFree : yMove;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>Calls <see cref="SuperController.GetFreeNavigateVector"/> via reflection (SteamVR types live outside VPB refs).</summary>
        private static float ReadNavigateForwardAxis(SuperController sc, FieldInfo moveActionField)
        {
            if (sc == null || GetFreeNavigateVectorMethod == null || moveActionField == null) return 0f;
            object moveAction = moveActionField.GetValue(sc);
            if (moveAction == null) return 0f;
            object boxed = GetFreeNavigateVectorMethod.Invoke(sc, new object[] { moveAction, false });
            if (boxed is Vector4 v) return v.y;
            return 0f;
        }

        private static void ApplyVerticalScroll(ScrollRect scroll, float stickForward)
        {
            float scrollablePx = 0f;
            try
            {
                if (scroll.content != null && scroll.viewport != null)
                    scrollablePx = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
            }
            catch { scrollablePx = 0f; }

            if (scrollablePx <= 0.5f) return;

            float viewportH = 0f;
            try { viewportH = scroll.viewport != null ? scroll.viewport.rect.height : 0f; }
            catch { viewportH = 0f; }
            if (viewportH <= 0.5f) viewportH = 800f;

            float mag = Mathf.Clamp01((Mathf.Abs(stickForward) - StickDeadzone) / Mathf.Max(0.0001f, 1f - StickDeadzone));
            float sign = Mathf.Sign(stickForward);
            float speedPx = sign * mag * MaxViewportHeightsPerSecond * viewportH;
            float deltaNorm = (speedPx * Time.unscaledDeltaTime) / scrollablePx;

            float pos = scroll.verticalNormalizedPosition;
            pos += deltaNorm;
            if (pos < 0f) pos = 0f;
            else if (pos > 1f) pos = 1f;
            scroll.verticalNormalizedPosition = pos;
        }
    }
}
