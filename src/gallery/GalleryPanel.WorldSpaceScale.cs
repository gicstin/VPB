using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Last player-UI root we parented to (detect HUD rebuild / null).</summary>
        private Transform _worldSpaceUiParent;

        /// <summary>
        /// WorldSpace gallery: parent under same unscaled HUD space as assignable buttons / native mainHUD
        /// so VaM worldScale does not change pane size.
        /// </summary>
        private void ApplyWorldSpaceCanvasScale()
        {
            if (canvas == null || isFixedLocally) return;
            if (canvas.renderMode != RenderMode.WorldSpace) return;

            Transform root = VpbWorldSpaceUiScale.GetPlayerUiRoot();
            if (root != null && (canvas.transform.parent != root || _worldSpaceUiParent != root))
            {
                VpbWorldSpaceUiScale.AttachToPlayerUiSpace(canvas.transform);
                _worldSpaceUiParent = root;
                return;
            }

            VpbWorldSpaceUiScale.ApplyMetersPerPixelLocalScale(canvas.transform);
            _worldSpaceUiParent = canvas.transform.parent;
        }

        /// <summary>
        /// Re-attach if HUD root appeared late, or localScale was overwritten.
        /// Cheap: parent/scale compares only.
        /// </summary>
        private void SyncWorldSpaceCanvasScaleIfWorldScaleChanged()
        {
            if (canvas == null || isFixedLocally) return;
            if (canvas.renderMode != RenderMode.WorldSpace) return;

            Transform root = VpbWorldSpaceUiScale.GetPlayerUiRoot();
            Transform tf = canvas.transform;
            float s = VpbWorldSpaceUiScale.MetersPerUiPixel;
            Vector3 ls = tf.localScale;
            bool scaleWrong = Mathf.Abs(ls.x - s) > 1e-7f || Mathf.Abs(ls.y - s) > 1e-7f || Mathf.Abs(ls.z - s) > 1e-7f;
            bool parentWrong = root != null && tf.parent != root;

            if (!parentWrong && !scaleWrong && _worldSpaceUiParent == tf.parent)
                return;

            ApplyWorldSpaceCanvasScale();
        }

        private void ResetWorldSpaceCanvasScaleSync()
        {
            _worldSpaceUiParent = null;
        }

        /// <summary>
        /// Desktop HostScale tracks VaM monitorUIScale. Cheap compare; ApplyInnerPaneScale only on change.
        /// </summary>
        private void SyncHostUiScaleIfChanged()
        {
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.IsVR) return;
            }
            catch { return; }

            GalleryUiMetrics m = UiMetrics;
            float host = m.HostScale;
            if (!float.IsNaN(_lastAppliedHostScale) && Mathf.Abs(_lastAppliedHostScale - host) < 0.001f)
                return;

            try { ApplyInnerPaneScale(); } catch { }
        }

        /// <summary>Fixed overlay: leave HUD attach so ScreenSpaceOverlay is not under WorldSpace parent.</summary>
        private void DetachWorldSpaceCanvasFromPlayerUi()
        {
            if (canvas == null) return;
            VpbWorldSpaceUiScale.DetachToSceneRoot(canvas.transform);
            _worldSpaceUiParent = null;
        }
    }
}
