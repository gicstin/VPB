using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Shows VaM's native scene loading HUD before <c>SuperController.Load</c> returns into
    /// <c>LoadCo</c>, so the VPB top banner can hand off without a blank gap.
    /// </summary>
    internal static class SceneLoadNativeUiBridge
    {
        internal static void ShowForSceneLoad(bool merge, string statusText = null)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            try { sc.DeactivateWorldUI(); } catch { }

            if (!merge)
            {
                bool hideHud = true;
                try
                {
                    uFileBrowser.FileBrowser fb = sc.fileBrowserUI;
                    if (fb != null && !fb.IsHidden() && fb.keepOpen)
                        hideHud = false;
                }
                catch { }

                if (hideHud)
                {
                    try { sc.HideMainHUD(); } catch { }
                }

                try { HUDAnchor.SetAnchorsToReference(); } catch { }
            }

            try
            {
                if (UserPreferences.singleton != null)
                    UserPreferences.singleton.pauseGlow = true;
            }
            catch { }

            if (sc.loadingUI != null)
            {
                if (!merge)
                {
                    sc.loadingUI.gameObject.SetActive(true);
                    if (sc.loadingUIAlt != null && !sc.MainHUDAnchoredOnMonitor)
                        sc.loadingUIAlt.gameObject.SetActive(true);
                }

                if (sc.loadingGeometry != null)
                    sc.loadingGeometry.gameObject.SetActive(true);
            }

            ResetProgressSliders(sc);
            UpdateLoadingStatus(sc, statusText, merge);
        }

        static void ResetProgressSliders(SuperController sc)
        {
            try
            {
                if (sc.loadingProgressSlider != null)
                {
                    sc.loadingProgressSlider.minValue = 0f;
                    sc.loadingProgressSlider.maxValue = 1f;
                    sc.loadingProgressSlider.value = 0f;
                }
                if (sc.loadingProgressSliderAlt != null)
                {
                    sc.loadingProgressSliderAlt.minValue = 0f;
                    sc.loadingProgressSliderAlt.maxValue = 1f;
                    sc.loadingProgressSliderAlt.value = 0f;
                }
            }
            catch { }
        }

        static void UpdateLoadingStatus(SuperController sc, string statusText, bool merge)
        {
            if (string.IsNullOrEmpty(statusText))
                statusText = merge ? "Merging scene..." : "Loading scene...";

            try
            {
                if (sc.loadingTextStatus != null)
                    sc.loadingTextStatus.text = statusText;
                if (sc.loadingTextStatusAlt != null)
                    sc.loadingTextStatusAlt.text = statusText;
            }
            catch { }
        }
    }
}
