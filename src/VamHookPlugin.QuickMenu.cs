using System;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private void OpenQuickMenuPositionWindow()
        {
            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
            if (isVR)
                return;

            Vector2 createPos = Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
            Vector2 showHidePos = Settings.Instance.QuickMenuShowHidePosDesktop.Value;
            Vector2 createPosVR = Settings.Instance.QuickMenuCreateGalleryPosVR.Value;
            Vector2 showHidePosVR = Settings.Instance.QuickMenuShowHidePosVR.Value;

            m_QuickMenuPosOriginalCreate = createPos;
            m_QuickMenuPosOriginalShowHide = showHidePos;

            m_QuickMenuPosCreateX = createPos.x;
            m_QuickMenuPosCreateY = createPos.y;
            m_QuickMenuPosShowHideX = showHidePos.x;
            m_QuickMenuPosShowHideY = showHidePos.y;
            m_QuickMenuPosCreateXVR = createPosVR.x;
            m_QuickMenuPosCreateYVR = createPosVR.y;
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

            if (m_CreateGalleryButtonRT != null)
            {
                m_CreateGalleryButtonRT.anchoredPosition = new Vector2(m_QuickMenuPosCreateX, m_QuickMenuPosCreateY);
            }
            if (m_ShowHideButtonRT != null)
            {
                m_ShowHideButtonRT.anchoredPosition = new Vector2(m_QuickMenuPosShowHideX, m_QuickMenuPosShowHideY);
            }
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
                var newCreate = new Vector2(m_QuickMenuPosCreateX, m_QuickMenuPosCreateY);
                var newShowHide = new Vector2(m_QuickMenuPosShowHideX, m_QuickMenuPosShowHideY);

                var newCreateVR = m_QuickMenuPosUseSameCreateInVR ? newCreate : new Vector2(m_QuickMenuPosCreateXVR, m_QuickMenuPosCreateYVR);
                var newShowHideVR = m_QuickMenuPosUseSameShowHideInVR ? newShowHide : new Vector2(m_QuickMenuPosShowHideXVR, m_QuickMenuPosShowHideYVR);

                Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value = newCreate;
                Settings.Instance.QuickMenuShowHidePosDesktop.Value = newShowHide;
                Settings.Instance.QuickMenuCreateGalleryPosVR.Value = newCreateVR;
                Settings.Instance.QuickMenuShowHidePosVR.Value = newShowHideVR;

                if (Settings.Instance.QuickMenuCreateGalleryUseSameInVR != null)
                    Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value = m_QuickMenuPosUseSameCreateInVR;
                if (Settings.Instance.QuickMenuShowHideUseSameInVR != null)
                    Settings.Instance.QuickMenuShowHideUseSameInVR.Value = m_QuickMenuPosUseSameShowHideInVR;
                try { this.Config.Save(); } catch { }
            }
            else
            {
                if (m_CreateGalleryButtonRT != null)
                    m_CreateGalleryButtonRT.anchoredPosition = m_QuickMenuPosOriginalCreate;
                if (m_ShowHideButtonRT != null)
                    m_ShowHideButtonRT.anchoredPosition = m_QuickMenuPosOriginalShowHide;
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
            GUILayout.Label("Quick Menu Positions (Desktop)", m_StyleHeader);
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
            DrawQuickMenuPosRow("Create Gallery", "QmCreate", ref createX, ref createXText, ref createY, ref createYText, xMin, xMax, yMin, yMax);
            m_QuickMenuPosCreateX = createX;
            m_QuickMenuPosCreateXText = createXText;
            m_QuickMenuPosCreateY = createY;
            m_QuickMenuPosCreateYText = createYText;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_QuickMenuPosUseSameCreateInVR ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_QuickMenuPosUseSameCreateInVR = !m_QuickMenuPosUseSameCreateInVR;
            }
            GUILayout.Label("Use same position in VR mode");
            GUILayout.EndHorizontal();

            if (!m_QuickMenuPosUseSameCreateInVR)
            {
                GUILayout.Space(4);
                float createXVR = m_QuickMenuPosCreateXVR;
                string createXVRText = m_QuickMenuPosCreateXVRText;
                float createYVR = m_QuickMenuPosCreateYVR;
                string createYVRText = m_QuickMenuPosCreateYVRText;
                DrawQuickMenuPosRow("Create Gallery (VR)", "QmCreateVR", ref createXVR, ref createXVRText, ref createYVR, ref createYVRText, xMin, xMax, yMin, yMax);
                m_QuickMenuPosCreateXVR = createXVR;
                m_QuickMenuPosCreateXVRText = createXVRText;
                m_QuickMenuPosCreateYVR = createYVR;
                m_QuickMenuPosCreateYVRText = createYVRText;
            }
            GUILayout.Space(6);
            float showHideX = m_QuickMenuPosShowHideX;
            string showHideXText = m_QuickMenuPosShowHideXText;
            float showHideY = m_QuickMenuPosShowHideY;
            string showHideYText = m_QuickMenuPosShowHideYText;
            DrawQuickMenuPosRow("Show/Hide", "QmShowHide", ref showHideX, ref showHideXText, ref showHideY, ref showHideYText, xMin, xMax, yMin, yMax);
            m_QuickMenuPosShowHideX = showHideX;
            m_QuickMenuPosShowHideXText = showHideXText;
            m_QuickMenuPosShowHideY = showHideY;
            m_QuickMenuPosShowHideYText = showHideYText;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_QuickMenuPosUseSameShowHideInVR ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_QuickMenuPosUseSameShowHideInVR = !m_QuickMenuPosUseSameShowHideInVR;
            }
            GUILayout.Label("Use same position in VR mode");
            GUILayout.EndHorizontal();

            if (!m_QuickMenuPosUseSameShowHideInVR)
            {
                GUILayout.Space(4);
                float showHideXVR = m_QuickMenuPosShowHideXVR;
                string showHideXVRText = m_QuickMenuPosShowHideXVRText;
                float showHideYVR = m_QuickMenuPosShowHideYVR;
                string showHideYVRText = m_QuickMenuPosShowHideYVRText;
                DrawQuickMenuPosRow("Show/Hide (VR)", "QmShowHideVR", ref showHideXVR, ref showHideXVRText, ref showHideYVR, ref showHideYVRText, xMin, xMax, yMin, yMax);
                m_QuickMenuPosShowHideXVR = showHideXVR;
                m_QuickMenuPosShowHideXVRText = showHideXVRText;
                m_QuickMenuPosShowHideYVR = showHideYVR;
                m_QuickMenuPosShowHideYVRText = showHideYVRText;
            }

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", m_StyleButton, GUILayout.Height(26)))
            {
                CloseQuickMenuPositionWindow(false);
            }
            if (GUILayout.Button("Save", m_StyleButtonPrimary, GUILayout.Height(26)))
            {
                CloseQuickMenuPositionWindow(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
