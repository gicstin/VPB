using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
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
            try { QuickMenuRefreshAssignFloatLocalizedChrome(); } catch { }
            try { QuickMenuInvalidateWatchStrings(); } catch { }
        }

        internal void RefreshQuickMenuButtonLabels()
        {
            if (m_CreateGalleryButton != null)
                m_CreateGalleryButton.label = VPBTranslation.T("hook.qmbutton.create_gallery", "Create Gallery");
            if (m_BringFrontButton != null)
                m_BringFrontButton.label = VPBTranslation.T("hook.qmbutton.bring_front", "Bring Front");
            if (m_CloseAllButton != null)
                m_CloseAllButton.label = VPBTranslation.T("hook.qmbutton.close_all", "Close All");

            RefreshShowHideButtonLabel();
        }

        internal void RefreshShowHideButtonLabel()
        {
            if (m_ShowHideButton == null) return;
            int count = (Gallery.singleton != null) ? Gallery.singleton.PanelCount : 0;
            string baseLabel = VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide");
            m_ShowHideButton.label = count > 0 ? baseLabel + " (" + count + ")" : baseLabel;
        }
    }
}
