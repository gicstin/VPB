using System;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private void ResetQuickMenuPositionDefaults()
        {
            // "0,0" is the baseline anchor (see QuickMenuAnchorBaseline in VamHookPlugin).
            m_QuickMenuPosCreateX = 0f;
            m_QuickMenuPosCreateY = 0f;
            m_QuickMenuPosCreateXText = ((int)m_QuickMenuPosCreateX).ToString();
            m_QuickMenuPosCreateYText = ((int)m_QuickMenuPosCreateY).ToString();

            m_QuickMenuPosCreateXVR = 0f;
            m_QuickMenuPosCreateYVR = 0f;
            m_QuickMenuPosCreateXVRText = ((int)m_QuickMenuPosCreateXVR).ToString();
            m_QuickMenuPosCreateYVRText = ((int)m_QuickMenuPosCreateYVR).ToString();

            m_QuickMenuPosUseSameCreateInVR = true;

            ApplyQuickMenuPositionPreview();
        }

        private void OpenQuickMenuPositionWindow()
        {
            bool isVR = XrUtils.IsVrActive();
            if (isVR)
                return;

            Vector2 createPos = Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
            Vector2 showHidePos = Settings.Instance.QuickMenuShowHidePosDesktop.Value;
            Vector2 createPosVR = Settings.Instance.QuickMenuCreateGalleryPosVR.Value;
            Vector2 showHidePosVR = Settings.Instance.QuickMenuShowHidePosVR.Value;

            m_QuickMenuPosOriginalCreate = createPos;
            m_QuickMenuPosOriginalShowHide = showHidePos;

            // UI shows X/Y relative to baseline.
            m_QuickMenuPosCreateX = createPos.x - QuickMenuAnchorBaseline.x;
            m_QuickMenuPosCreateY = createPos.y - QuickMenuAnchorBaseline.y;
            m_QuickMenuPosShowHideX = showHidePos.x;
            m_QuickMenuPosShowHideY = showHidePos.y;
            m_QuickMenuPosCreateXVR = createPosVR.x - QuickMenuAnchorBaseline.x;
            m_QuickMenuPosCreateYVR = createPosVR.y - QuickMenuAnchorBaseline.y;
            m_QuickMenuPosShowHideXVR = showHidePosVR.x;
            m_QuickMenuPosShowHideYVR = showHidePosVR.y;

            m_QuickMenuPosCreateXText = ((int)m_QuickMenuPosCreateX).ToString();
            m_QuickMenuPosCreateYText = ((int)m_QuickMenuPosCreateY).ToString();
            m_QuickMenuPosShowHideXText = ((int)m_QuickMenuPosShowHideX).ToString();
            m_QuickMenuPosShowHideYText = ((int)m_QuickMenuPosShowHideY).ToString();
            m_QuickMenuPosCreateXVRText = ((int)m_QuickMenuPosCreateXVR).ToString();
            m_QuickMenuPosCreateYVRText = ((int)m_QuickMenuPosCreateYVR).ToString();
            m_QuickMenuPosShowHideXVRText = ((int)m_QuickMenuPosShowHideXVR).ToString();
            m_QuickMenuPosShowHideYVRText = ((int)m_QuickMenuPosShowHideYVR).ToString();

            m_QuickMenuPosUseSameCreateInVR = Settings.Instance.QuickMenuCreateGalleryUseSameInVR != null && Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value;
            m_QuickMenuPosUseSameShowHideInVR = Settings.Instance.QuickMenuShowHideUseSameInVR != null && Settings.Instance.QuickMenuShowHideUseSameInVR.Value;

            m_ShowQuickMenuPosWindow = true;
        }

        private void ApplyQuickMenuPositionPreview()
        {
            if (!m_ShowQuickMenuPosWindow)
                return;

            // Grid-based quick menu: treat CreateGallery position as the anchor for the entire grid.
            // Do not move individual buttons here; that would override the grid layout and "rearrange" it.
            try { QuickMenuApplyGridLayoutFromAnchor(QuickMenuAnchorBaseline + new Vector2(m_QuickMenuPosCreateX, m_QuickMenuPosCreateY)); } catch { }
        }

        private void DrawQuickMenuPosRow(string label, string controlNamePrefix, ref float x, ref string xText, ref float y, ref string yText, float xMin, float xMax, float yMin, float yMax)
        {
            GUILayout.BeginVertical(m_StyleSection);
            GUILayout.Label(label, m_StyleSubHeader);

            void ApplyWheelNudgeIfHovered(Rect r, ref float v, float min, float max)
            {
                if (Event.current == null)
                    return;
                if (Event.current.type != EventType.ScrollWheel)
                    return;
                if (!r.Contains(Event.current.mousePosition))
                    return;

                float delta = Event.current.delta.y;
                if (Math.Abs(delta) < 0.001f)
                    return;

                float step = delta > 0f ? -1f : 1f;
                v = Mathf.Clamp(Mathf.Round(v + step), min, max);
                Event.current.Use();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(16));
            float prevX = x;
            var xSliderRect = GUILayoutUtility.GetRect(0f, 18f, GUI.skin.horizontalSlider, GUILayout.ExpandWidth(true));
            x = GUI.HorizontalSlider(xSliderRect, x, xMin, xMax);
            x = Mathf.Clamp(Mathf.Round(x), xMin, xMax);
            ApplyWheelNudgeIfHovered(xSliderRect, ref x, xMin, xMax);

            GUILayout.Space(8);

            string xControl = controlNamePrefix + "_XText";
            bool xFocused = GUI.GetNameOfFocusedControl() == xControl;
            var xTextRect = GUILayoutUtility.GetRect(80f, 20f, GUI.skin.textField, GUILayout.Width(80));
            GUI.SetNextControlName(xControl);
            string newXText = GUI.TextField(xTextRect, xText ?? "");
            if (newXText != xText)
                xText = newXText;
            if (xFocused)
            {
                float parsed;
                if (float.TryParse(xText ?? "", out parsed))
                    x = Mathf.Clamp(Mathf.Round(parsed), xMin, xMax);
            }
            if (Math.Abs(prevX - x) > 0.0001f && !xFocused)
            {
                xText = ((int)x).ToString();
            }
            ApplyWheelNudgeIfHovered(xTextRect, ref x, xMin, xMax);
            if (!xFocused)
            {
                xText = ((int)x).ToString();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Y", GUILayout.Width(16));
            float prevY = y;
            var ySliderRect = GUILayoutUtility.GetRect(0f, 18f, GUI.skin.horizontalSlider, GUILayout.ExpandWidth(true));
            y = GUI.HorizontalSlider(ySliderRect, y, yMin, yMax);
            y = Mathf.Clamp(Mathf.Round(y), yMin, yMax);
            ApplyWheelNudgeIfHovered(ySliderRect, ref y, yMin, yMax);

            GUILayout.Space(8);

            string yControl = controlNamePrefix + "_YText";
            bool yFocused = GUI.GetNameOfFocusedControl() == yControl;
            var yTextRect = GUILayoutUtility.GetRect(80f, 20f, GUI.skin.textField, GUILayout.Width(80));
            GUI.SetNextControlName(yControl);
            string newYText = GUI.TextField(yTextRect, yText ?? "");
            if (newYText != yText)
                yText = newYText;
            if (yFocused)
            {
                float parsed;
                if (float.TryParse(yText ?? "", out parsed))
                    y = Mathf.Clamp(Mathf.Round(parsed), yMin, yMax);
            }
            if (Math.Abs(prevY - y) > 0.0001f && !yFocused)
            {
                yText = ((int)y).ToString();
            }
            ApplyWheelNudgeIfHovered(yTextRect, ref y, yMin, yMax);
            if (!yFocused)
            {
                yText = ((int)y).ToString();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void CloseQuickMenuPositionWindow(bool save)
        {
            if (save)
            {
                var newCreate = QuickMenuAnchorBaseline + new Vector2(m_QuickMenuPosCreateX, m_QuickMenuPosCreateY);

                var newCreateVR = m_QuickMenuPosUseSameCreateInVR
                    ? newCreate
                    : (QuickMenuAnchorBaseline + new Vector2(m_QuickMenuPosCreateXVR, m_QuickMenuPosCreateYVR));

                Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value = newCreate;
                Settings.Instance.QuickMenuCreateGalleryPosVR.Value = newCreateVR;

                if (Settings.Instance.QuickMenuCreateGalleryUseSameInVR != null)
                    Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value = m_QuickMenuPosUseSameCreateInVR;
                try { this.Config.Save(); } catch { }
            }
            else
            {
                // Restore preview to the original anchor (grid moves as a unit).
                try { QuickMenuApplyGridLayoutFromAnchor(m_QuickMenuPosOriginalCreate); } catch { }
            }

            m_ShowQuickMenuPosWindow = false;
        }

        private void DrawQuickMenuPosWindow(int windowId)
        {
            EnsureStyles();

            const float xMin = -1000f;
            const float xMax = 2000f;
            const float yMin = -500f;
            const float yMax = 1500f;

            GUILayout.BeginVertical(m_StylePanel);

            GUILayout.BeginHorizontal();
            GUILayout.Label(VPBTranslation.T("hook.qmpos.title", "Quick Menu Positions (Desktop)"), m_StyleHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", m_StyleButtonSmall, GUILayout.Width(30)))
            {
                CloseQuickMenuPositionWindow(false);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            float createX = m_QuickMenuPosCreateX;
            string createXText = m_QuickMenuPosCreateXText;
            float createY = m_QuickMenuPosCreateY;
            string createYText = m_QuickMenuPosCreateYText;
            DrawQuickMenuPosRow(VPBTranslation.T("hook.qmpos.create_gallery", "Create Gallery"), "QmCreate", ref createX, ref createXText, ref createY, ref createYText, xMin, xMax, yMin, yMax);
            m_QuickMenuPosCreateX = createX;
            m_QuickMenuPosCreateXText = createXText;
            m_QuickMenuPosCreateY = createY;
            m_QuickMenuPosCreateYText = createYText;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_QuickMenuPosUseSameCreateInVR ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_QuickMenuPosUseSameCreateInVR = !m_QuickMenuPosUseSameCreateInVR;
            }
            GUILayout.Label(VPBTranslation.T("hook.qmpos.same_vr", "Use same position in VR mode"));
            GUILayout.EndHorizontal();

            if (!m_QuickMenuPosUseSameCreateInVR)
            {
                GUILayout.Space(4);
                float createXVR = m_QuickMenuPosCreateXVR;
                string createXVRText = m_QuickMenuPosCreateXVRText;
                float createYVR = m_QuickMenuPosCreateYVR;
                string createYVRText = m_QuickMenuPosCreateYVRText;
                DrawQuickMenuPosRow(VPBTranslation.T("hook.qmpos.create_gallery_vr", "Create Gallery (VR)"), "QmCreateVR", ref createXVR, ref createXVRText, ref createYVR, ref createYVRText, xMin, xMax, yMin, yMax);
                m_QuickMenuPosCreateXVR = createXVR;
                m_QuickMenuPosCreateXVRText = createXVRText;
                m_QuickMenuPosCreateYVR = createYVR;
                m_QuickMenuPosCreateYVRText = createYVRText;
            }

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(VPBTranslation.T("hook.cancel", "Cancel"), m_StyleButton, GUILayout.Height(26)))
            {
                CloseQuickMenuPositionWindow(false);
            }
            if (GUILayout.Button(VPBTranslation.T("hook.defaults", "Defaults"), m_StyleButton, GUILayout.Height(26)))
            {
                ResetQuickMenuPositionDefaults();
            }
            if (GUILayout.Button(VPBTranslation.T("hook.save", "Save"), m_StyleButtonPrimary, GUILayout.Height(26)))
            {
                CloseQuickMenuPositionWindow(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
