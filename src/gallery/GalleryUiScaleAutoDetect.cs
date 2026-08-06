using UnityEngine;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Cold-path gallery UI scale seeding for new installs.
    /// Pane stays near 1.0 — HostScale already multiplies VaM <c>monitorUIScale</c> (see <see cref="GalleryUiMetrics"/>).
    /// </summary>
    internal static class GalleryUiScaleAutoDetect
    {
        /// <summary>Bump when seed formula changes; re-applies only if saved pane still matches prior formula.</summary>
        public const int SeedRevision = 2;

        /// <summary>1080p reference height for mild ConstantPixelSize nudge.</summary>
        public const float ReferenceScreenHeight = 1080f;

        /// <summary>Comfortable desktop pane. HostScale carries VaM monitor UI size separately.</summary>
        public const float DesktopPaneDefault = 1.0f;

        /// <summary>Comfortable VR pane default.</summary>
        public const float VrPaneDefault = 1.0f;

        public const float DesktopPaneMin = 0.85f;
        public const float DesktopPaneMax = 1.15f;

        /// <summary>
        /// Reads VaM Monitor UI Scale. Default 1 when SuperController / prefs not ready.
        /// </summary>
        public static float ReadMonitorUiScale()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    float v = sc.monitorUIScale;
                    if (!float.IsNaN(v) && !float.IsInfinity(v) && v > 0.01f)
                        return v;
                }
            }
            catch { }
            return GalleryUiDesignTokens.VamMonitorUiScaleDesignBaseline;
        }

        /// <summary>
        /// Desktop pane recommendation. Centered on 1.0 with a mild height nudge only.
        /// Does not bake monitorUIScale (HostScale applies that live).
        /// </summary>
        public static float RecommendDesktopPaneScale(int screenHeight)
        {
            int h = screenHeight;
            if (h <= 0) h = Mathf.RoundToInt(ReferenceScreenHeight);
            float t = (float)h / ReferenceScreenHeight;
            // ±~0.12 across common resolutions — avoid stacking on HostScale into ~1.5–2.0 look.
            float pane = DesktopPaneDefault + 0.12f * (t - 1f);
            pane = Mathf.Clamp(pane, DesktopPaneMin, DesktopPaneMax);
            pane = Mathf.Round(pane * 20f) / 20f; // 0.05 steps
            return VPBConfig.ClampUiScalePublic(pane);
        }

        /// <summary>Revision-1 formula (aggressive height scale). Used only to detect untouched v1 seeds.</summary>
        public static float RecommendDesktopPaneScaleRevision1(int screenHeight)
        {
            int h = screenHeight;
            if (h <= 0) h = Mathf.RoundToInt(ReferenceScreenHeight);
            float pane = 0.90f * ((float)h / ReferenceScreenHeight);
            pane = Mathf.Clamp(pane, 0.70f, 1.40f);
            pane = Mathf.Round(pane * 20f) / 20f;
            return VPBConfig.ClampUiScalePublic(pane);
        }

        public static float RecommendVrPaneScale()
        {
            return VPBConfig.ClampUiScalePublic(VrPaneDefault);
        }

        /// <summary>
        /// Apply recommended pane scales into config. Returns false when Screen.height not ready yet.
        /// </summary>
        public static bool TryApplyRecommendedPaneScales(VPBConfig cfg)
        {
            if (cfg == null) return false;

            int h = 0;
            try { h = Screen.height; } catch { h = 0; }
            if (h <= 0) return false;

            float desk = RecommendDesktopPaneScale(h);
            float vr = RecommendVrPaneScale();
            cfg.InnerPaneScaleDesktop = desk;
            cfg.InnerPaneScaleVR = vr;
            cfg.SideButtonScale = cfg.IsVR ? vr : desk;

            try
            {
                VPBLogger.Config.LogInfo(
                    "Gallery UI scale auto-seed | rev=" + SeedRevision
                    + " | screenH=" + h
                    + " | desktop=" + desk.ToString("0.00")
                    + " | vr=" + vr.ToString("0.00")
                    + " | monitorUIScale=" + ReadMonitorUiScale().ToString("0.00")
                    + " | vrActive=" + (XrUtils.IsVrActive() ? "1" : "0"));
            }
            catch { }

            return true;
        }

        /// <summary>
        /// True when saved desktop pane still matches revision-1 auto-seed (user did not customize).
        /// </summary>
        public static bool LooksLikeUntouchedRevision1Seed(float desktopPane, int screenHeight)
        {
            float old = RecommendDesktopPaneScaleRevision1(screenHeight);
            return Mathf.Abs(desktopPane - old) < 0.06f;
        }
    }
}
