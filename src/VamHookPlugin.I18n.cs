using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private bool m_ShowLangWindow;
        private Rect m_LangWindowRect = new Rect(300, 150, 220, 100);

        // UIDynamicButton refs for Quick Menu buttons (Unity UI, need explicit refresh on locale change)
        private UIDynamicButton m_CreateGalleryButton;
        private UIDynamicButton m_BringFrontButton;
        private UIDynamicButton m_CloseAllButton;

        private void SubscribeLocaleChanged()
        {
            try { VPBTranslation.LocaleChanged -= OnImGuiLocaleChanged; } catch { }
            VPBTranslation.LocaleChanged += OnImGuiLocaleChanged;
        }

        private void UnsubscribeLocaleChanged()
        {
            try { VPBTranslation.LocaleChanged -= OnImGuiLocaleChanged; } catch { }
        }

        private void OnImGuiLocaleChanged()
        {
            try { RefreshQuickMenuButtonLabels(); } catch { }
        }

        internal void RefreshQuickMenuButtonLabels()
        {
            if (m_CreateGalleryButton != null)
                m_CreateGalleryButton.label = VPBTranslation.T("hook.qmbutton.create_gallery", "Create Gallery");
            if (m_BringFrontButton != null)
                m_BringFrontButton.label = VPBTranslation.T("hook.qmbutton.bring_front", "Bring Front");
            if (m_CloseAllButton != null)
                m_CloseAllButton.label = VPBTranslation.T("hook.qmbutton.close_all", "Close All");

            // Show/Hide includes the live panel count — rebuild via helper
            RefreshShowHideButtonLabel();
        }

        internal void RefreshShowHideButtonLabel()
        {
            if (m_ShowHideButton == null) return;
            int count = (Gallery.singleton != null) ? Gallery.singleton.PanelCount : 0;
            string baseLabel = VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide");
            m_ShowHideButton.label = count > 0 ? baseLabel + " (" + count + ")" : baseLabel;
        }

        private static string GetImGuiLocaleShortCode(string localeId)
        {
            if (string.IsNullOrEmpty(localeId)) return "EN";
            switch (localeId.ToLowerInvariant())
            {
                case "en":    return "EN";
                case "zh_cn": return "\u4e2d\u6587";
                case "zh_tw": return "\u7e41\u4e2d";
                case "ja":    return "\u65e5\u672c";
                case "ko":    return "\ud55c\uad6d";
                default:      return localeId.Length > 4
                                  ? localeId.Substring(0, 4).ToUpperInvariant()
                                  : localeId.ToUpperInvariant();
            }
        }

        private void DrawLangWindow(int windowId)
        {
            EnsureStyles();
            GUILayout.BeginVertical(m_StylePanel);

            GUILayout.BeginHorizontal();
            GUILayout.Label(VPBTranslation.T("hook.lang.title", "Language"), m_StyleSubHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", m_StyleButtonSmall, GUILayout.Width(26), GUILayout.Height(22)))
            {
                m_ShowLangWindow = false;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            List<string> locales = VPBTranslation.GetAvailableLocaleIds();
            string current = VPBTranslation.CurrentLocale;

            foreach (string loc in locales)
            {
                string id = loc;
                bool isCurrent = string.Equals(id, current, System.StringComparison.OrdinalIgnoreCase);
                string label = (isCurrent ? "\u2713 " : "  ") + VPBTranslation.GetLocaleDisplayName(id);
                GUIStyle btnStyle = isCurrent ? m_StyleButtonPrimary : m_StyleButton;
                if (GUILayout.Button(label, btnStyle, GUILayout.Height(24)))
                {
                    VPBTranslation.SetLocale(id, saveConfig: true);
                    m_ShowLangWindow = false;
                }
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
