using UnityEngine;

namespace VPB
{
    internal static class VpbWorldSpaceUiScale
    {
        public const float MetersPerUiPixel = 0.001f;

        public static Transform GetPlayerUiRoot()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;

            if (sc.mainHUDAttachPoint != null)
                return sc.mainHUDAttachPoint;

            if (sc.mainHUD != null && sc.mainHUD.parent != null)
                return sc.mainHUD.parent;

            if (sc.mainHUD != null)
                return sc.mainHUD;

            return null;
        }

        /// <summary>Parent into player UI space and lock localScale to MetersPerUiPixel.</summary>
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
                tf.SetParent(root, true);
                ApplyMetersPerPixelLocalScale(tf);
                tf.position = pos;
                tf.rotation = rot;
            }
            else
            {
                ApplyMetersPerPixelLocalScale(tf);
            }

            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.mainHUD != null && sc.mainHUD.gameObject != null)
                    tf.gameObject.layer = sc.mainHUD.gameObject.layer;
            }
            catch { }
        }

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

        public static void ApplyConstantWorldScale(Transform tf)
        {
            AttachToPlayerUiSpace(tf);
        }
    }
}
