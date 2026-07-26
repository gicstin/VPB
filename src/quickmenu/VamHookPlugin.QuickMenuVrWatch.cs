using UnityEngine;

namespace VPB
{
    public enum QuickMenuVrWatchMode
    {
        Off,
        LeftOnly,
        RightOnly,
        OppositeToMenu,
        SameAsMenu,
    }

    public partial class VamHookPlugin
    {
        // Grid center (canvas-space) that places the 4x4 block on the canvas origin (palm center).
        // Derived from QuickMenuApplyGridLayoutFromAnchor: slot0Center = (C.x - 28, C.y - 2),
        // block center = slot0Center + (1.5*cell, -1.5*cell). Solve block center == (0,0).
        private static readonly Vector2 QuickMenuWatchCenter =
            new Vector2(-1.5f * QuickMenuGridCell + 28f, 1.5f * QuickMenuGridCell + 2f);

        // The VAM HUD needs a moment to slide to the controller that opened the menu; sampling hand
        // proximity on the open frame reads the stale position. Wait this long before latching.
        private const float QuickMenuWatchLatchDelaySec = 0.25f;

        private bool m_QmWatchActive;
        private Transform m_QmWatchHand;
        // Hand the watch is locked to while the menu stays open (OppositeToMenu mode).
        private Transform m_QmWatchLatchedHand;
        private bool m_QmWatchMenuWasVisible;
        private bool m_QmWatchLatchPending;
        private float m_QmWatchOpenTime;

        // True while the watch drives the grid center (consumed by QuickMenuUpdateGridLayoutLive).
        internal bool QuickMenuWatchActive { get { return m_QmWatchActive; } }
        internal Vector2 QuickMenuWatchGridCenter { get { return QuickMenuWatchCenter; } }

        // Called every frame from Update (VR only does work). While the VAM menu is open, moves the
        // quick-menu canvas onto a controller (per QuickMenuVrWatchMode); restores it to the menu HUD
        // otherwise. The hand is latched a moment after the menu opens so it never switches mid-session.
        private void QuickMenuUpdateVrWatch()
        {
            if (m_QuickMenuCanvas == null) return;

            bool vr = QuickMenuIsVrActive();
            bool menuVis = QuickMenuIsVamMenuVisible();
            QuickMenuVrWatchMode mode = QuickMenuGetWatchMode();

            if (menuVis && !m_QmWatchMenuWasVisible)
            {
                // Rising edge: arm a deferred latch so the HUD can settle onto the opening hand.
                m_QmWatchLatchPending = true;
                m_QmWatchOpenTime = Time.unscaledTime;
                m_QmWatchLatchedHand = null;
            }
            m_QmWatchMenuWasVisible = menuVis;

            // Master visibility + "only with menu" gate. When the watch may show without the menu,
            // we don't require menuVis; OppositeToMenu still needs the menu open to pick a hand.
            bool watchEnabled = QuickMenuGetWatchVisible();
            bool onlyWithMenu = QuickMenuGetWatchOnlyWithMenu();
            bool gateOpen = menuVis || !onlyWithMenu;

            Transform watchHand = null;
            try
            {
                if (vr && watchEnabled && mode != QuickMenuVrWatchMode.Off && gateOpen)
                {
                    var sc = SuperController.singleton;
                    Transform left = sc != null ? sc.touchObjectLeft : null;
                    Transform right = sc != null ? sc.touchObjectRight : null;
                    Transform hud = sc != null ? sc.mainHUD : null;

                    if (left != null && right != null)
                    {
                        switch (mode)
                        {
                            case QuickMenuVrWatchMode.LeftOnly:
                                watchHand = left;
                                break;
                            case QuickMenuVrWatchMode.RightOnly:
                                watchHand = right;
                                break;
                            case QuickMenuVrWatchMode.OppositeToMenu:
                            case QuickMenuVrWatchMode.SameAsMenu:
                                if (m_QmWatchLatchedHand == null && hud != null &&
                                    (!m_QmWatchLatchPending || (Time.unscaledTime - m_QmWatchOpenTime) >= QuickMenuWatchLatchDelaySec))
                                {
                                    // Latch once: menu hand = controller closest to the HUD.
                                    float dl = (left.position - hud.position).sqrMagnitude;
                                    float dr = (right.position - hud.position).sqrMagnitude;
                                    Transform menuHand = (dl < dr) ? left : right;
                                    // SameAsMenu rides the menu hand; OppositeToMenu rides the other.
                                    m_QmWatchLatchedHand = (mode == QuickMenuVrWatchMode.SameAsMenu)
                                        ? menuHand
                                        : ((menuHand == left) ? right : left);
                                    m_QmWatchLatchPending = false;
                                }
                                watchHand = m_QmWatchLatchedHand; // null while still pending (brief)
                                break;
                        }
                    }
                }
            }
            catch { watchHand = null; }

            if (watchHand != null)
            {
                if (m_QuickMenuCanvas.transform.parent != watchHand)
                {
                    m_QuickMenuCanvas.transform.SetParent(watchHand, false);
                    m_QmWatchHand = watchHand;
                }

                QuickMenuApplyWatchPose();

                if (!m_QmWatchActive)
                {
                    m_QmWatchActive = true;
                    QuickMenuInvalidateGridLayout();
                }

                if (!m_QuickMenuCanvas.gameObject.activeSelf)
                    m_QuickMenuCanvas.gameObject.SetActive(true);
            }
            else if (m_QmWatchActive)
            {
                // Restore to the VAM menu HUD so it auto hides/shows with the menu again.
                var hud = SuperController.singleton != null ? SuperController.singleton.mainHUD : null;
                if (hud != null && m_QuickMenuCanvas.transform.parent != hud)
                    m_QuickMenuCanvas.transform.SetParent(hud, false);

                const float s = 0.001f;
                m_QuickMenuCanvas.transform.localScale = new Vector3(s, s, s);
                m_QuickMenuCanvas.transform.localPosition = Vector3.zero;
                m_QuickMenuCanvas.transform.localEulerAngles = new Vector3(32f, 180f, 0f);

                m_QmWatchActive = false;
                m_QmWatchHand = null;
                QuickMenuInvalidateGridLayout();
            }

            // Drop the latch once the menu closes so the next open re-detects the menu hand.
            if (!menuVis)
            {
                m_QmWatchLatchedHand = null;
                m_QmWatchLatchPending = false;
            }
        }

        private static QuickMenuVrWatchMode QuickMenuGetWatchMode()
        {
            try
            {
                var c = VPBConfig.Instance;
                if (c != null) return QuickMenuParseWatchMode(c.QuickMenuVrWatchMode);
            }
            catch { }
            return QuickMenuVrWatchMode.OppositeToMenu;
        }

        private static QuickMenuVrWatchMode QuickMenuParseWatchMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return QuickMenuVrWatchMode.OppositeToMenu;
            switch (s)
            {
                case "Off": return QuickMenuVrWatchMode.Off;
                case "Left only": return QuickMenuVrWatchMode.LeftOnly;
                case "Right only": return QuickMenuVrWatchMode.RightOnly;
                case "Same hand": return QuickMenuVrWatchMode.SameAsMenu;
                default: return QuickMenuVrWatchMode.OppositeToMenu;
            }
        }

        // Flip the watch onto the opposite hand. Resolves the hand currently showing the watch (when
        // visible) and forces the explicit mode (Left only / Right only) for the other hand, so a press
        // always produces a visible switch even from the Off / Opposite-to-menu modes.
        private void QuickMenuSwitchWatchHand()
        {
            var c = VPBConfig.Instance;
            if (c == null) return;

            bool currentlyLeft;
            var sc = SuperController.singleton;
            if (m_QmWatchHand != null && sc != null && m_QmWatchHand == sc.touchObjectLeft)
                currentlyLeft = true;
            else if (m_QmWatchHand != null && sc != null && m_QmWatchHand == sc.touchObjectRight)
                currentlyLeft = false;
            else
                currentlyLeft = c.QuickMenuVrWatchMode == "Left only";

            c.QuickMenuVrWatchMode = currentlyLeft ? "Right only" : "Left only";
            try { c.TriggerChange(); } catch { }
        }

        private static bool QuickMenuGetWatchVisible()
        {
            try { var c = VPBConfig.Instance; if (c != null) return c.QuickMenuVrWatchVisible; }
            catch { }
            return true;
        }

        private static bool QuickMenuGetWatchOnlyWithMenu()
        {
            try { var c = VPBConfig.Instance; if (c != null) return c.QuickMenuVrWatchOnlyWithMenu; }
            catch { }
            return true;
        }

        private void QuickMenuApplyWatchPose()
        {
            Vector3 offset = new Vector3(0f, 0.05f, 0.04f);
            Vector3 euler = new Vector3(0f, 0f, 0f);
            float scale = 0.0005f;
            bool faceUser = true;
            float towardUser = 0.12f;
            try
            {
                var c = VPBConfig.Instance;
                if (c != null)
                {
                    offset = c.QuickMenuVrWatchOffset;
                    scale = c.QuickMenuVrWatchScale;
                    faceUser = c.QuickMenuVrWatchFaceUser;
                    towardUser = c.QuickMenuVrWatchTowardUserDist;
                }
            }
            catch { }

            var t = m_QuickMenuCanvas.transform;
            t.localPosition = offset;
            t.localScale = new Vector3(scale, scale, scale);

            Transform cam = QuickMenuGetPlayerCamera();

            // Pull the panel from the controller toward the eye: closer to the user and off the
            // controller's forward pointer ray (so the same hand's laser can't hit it).
            if (cam != null && towardUser != 0f)
            {
                Vector3 worldPos = t.position;
                Vector3 toCam = cam.position - worldPos;
                float d = toCam.magnitude;
                if (d > 0.0001f)
                {
                    worldPos += (toCam / d) * Mathf.Min(towardUser, d * 0.9f);
                    t.position = worldPos;
                }
            }

            if (faceUser && cam != null)
            {
                // Face the eye directly: canvas normal points along (panel -> away from eye), so the
                // readable side faces the eye regardless of HMD rotation or hand angle.
                Vector3 dir = t.position - cam.position;
                if (dir.sqrMagnitude > 1e-6f)
                    t.rotation = Quaternion.LookRotation(dir, cam.up);
            }
            else
            {
                t.localEulerAngles = euler;
            }
        }

        private static Transform QuickMenuGetPlayerCamera()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null) return sc.centerCameraTarget.transform;
            }
            catch { }
            return null;
        }

        // Force QuickMenuUpdateGridLayoutLive to re-apply on its next call (resets the throttle cache).
        private void QuickMenuInvalidateGridLayout()
        {
            m_QmLastAnchorCenter = new Vector2(float.NaN, float.NaN);
        }

        private static bool QuickMenuIsVamMenuVisible()
        {
            var sc = SuperController.singleton;
            return sc != null && sc.mainHUD != null && sc.mainHUD.gameObject != null &&
                   sc.mainHUD.gameObject.activeInHierarchy;
        }
    }
}
