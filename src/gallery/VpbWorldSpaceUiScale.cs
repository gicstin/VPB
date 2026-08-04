using UnityEngine;

namespace VPB
{
    /// <summary>
    /// WorldSpace UI that must ignore <see cref="SuperController.worldScale"/> — same space as native mainHUD
    /// and VPB assignable quick-menu buttons (<c>mainHUD</c> / attach point, outside <c>worldScaleTransform</c>).
    /// </summary>
    internal static class VpbWorldSpaceUiScale
    {
        /// <summary>World meters per UI pixel at design scale (VaM WorldSpace canvas convention).</summary>
        public const float MetersPerUiPixel = 0.001f;

        /// <summary>
        /// Unscaled HUD space (outside <c>worldScaleTransform</c>), same chain as assignable buttons on mainHUD.
        /// Prefer attach point so gallery is not SetActive-tied to mainHUD visibility.
        /// </summary>
        public static Transform GetPlayerUiRoot()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;

            if (sc.mainHUDAttachPoint != null)
                return sc.mainHUDAttachPoint;

            // Same parent as mainHUD if attach missing — still outside world scale.
            if (sc.mainHUD != null && sc.mainHUD.parent != null)
                return sc.mainHUD.parent;

            if (sc.mainHUD != null)
                return sc.mainHUD;

            return null;
        }

        /// <summary>
        /// Parent into player UI space and lock localScale to <see cref="MetersPerUiPixel"/>.
        /// Preserves world pose. No-op when already parented with correct scale.
        /// </summary>
        public static void AttachToPlayerUiSpace(Transform tf)
        {
            if (tf == null) return;

            Transform root = GetPlayerUiRoot();
            if (root == null)
            {
                ApplyMetersPerPixelLocalScale(tf);
                return;
            }

            bool parentOk = tf.parent == root;
            if (!parentOk)
            {
                Vector3 pos = tf.position;
                Quaternion rot = tf.rotation;
                // worldPositionStays: keep pose while leaving worldScaleTransform (if any).
                tf.SetParent(root, true);
                // Parent lossy is ~1 in HUD space; force design meters-per-pixel (may correct baked scale).
                ApplyMetersPerPixelLocalScale(tf);
                tf.position = pos;
                tf.rotation = rot;
            }
            else
            {
                ApplyMetersPerPixelLocalScale(tf);
            }

            // Match native / assignable-button UI layer when possible.
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.mainHUD != null && sc.mainHUD.gameObject != null)
                    tf.gameObject.layer = sc.mainHUD.gameObject.layer;
            }
            catch { }
        }

        /// <summary>Leave player UI space (e.g. switch to ScreenSpaceOverlay fixed dock).</summary>
        public static void DetachToSceneRoot(Transform tf)
        {
            if (tf == null) return;
            if (tf.parent == null) return;
            Vector3 pos = tf.position;
            Quaternion rot = tf.rotation;
            tf.SetParent(null, false);
            tf.position = pos;
            tf.rotation = rot;
            tf.localScale = Vector3.one;
        }

        public static void ApplyMetersPerPixelLocalScale(Transform tf)
        {
            if (tf == null) return;
            float s = MetersPerUiPixel;
            Vector3 cur = tf.localScale;
            if (Mathf.Abs(cur.x - s) < 1e-8f && Mathf.Abs(cur.y - s) < 1e-8f && Mathf.Abs(cur.z - s) < 1e-8f)
                return;
            tf.localScale = new Vector3(s, s, s);
        }

        /// <summary>
        /// Attach + meters-per-pixel scale. Pass the transform that owns world pose
        /// (gallery canvas, or context-menu root — never a child while parent is rotated).
        /// </summary>
        public static void ApplyConstantWorldScale(Transform tf)
        {
            AttachToPlayerUiSpace(tf);
        }
    }
}
