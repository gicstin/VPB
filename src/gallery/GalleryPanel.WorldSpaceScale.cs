using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Transform _worldSpaceUiParent;

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

        private void DetachWorldSpaceCanvasFromPlayerUi()
        {
            if (canvas == null) return;
            VpbWorldSpaceUiScale.DetachToSceneRoot(canvas.transform);
            _worldSpaceUiParent = null;
        }
    }
}
