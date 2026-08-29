using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void AppendNetRuleSettings(List<InternalSettingDefinition> defs)
        {
            AddNetSessionPanelRow(defs);
            AddNetRuleWindowRow(defs);
        }

        public static void RefreshNetRuleSettingsRows()
        {
            RefreshNetSessionChrome();
        }

        public static void RefreshNetSessionChrome()
        {
            if (Gallery.singleton == null) return;
            List<GalleryPanel> panels = Gallery.singleton.Panels;
            if (panels == null) return;

            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null) continue;
                try
                {
                    p.InvalidateInternalSettingsDefsCache();
                    p.RefreshInternalSettingsListRows(true);
                    p.UpdateNetSessionToggleButton();
                }
                catch { }
            }
        }

        private void UpdateNetSessionToggleButton()
        {
            if (footerNetSessionBtnImage == null && footerNetSessionBtnText == null) return;
            bool wanted = false;
            bool live = false;
            try
            {
                wanted = VpbNetSessionUi.IsWanted;
                live = VpbNetPresence.IsActive || VpbNetPresence.Wanted;
            }
            catch { }

            if (footerNetSessionBtnImage != null)
            {
                footerNetSessionBtnImage.color = (wanted || live)
                    ? GalleryUiColorTokens.FacetMultiplayerOn
                    : GalleryUiColorTokens.FacetMultiplayer;
            }
            if (footerNetSessionBtnText != null)
            {
                footerNetSessionBtnText.gameObject.SetActive(true);
                footerNetSessionBtnText.text = live && !wanted
                    ? VPBTranslation.T("gallery.footer.session_in", "In")
                    : VPBTranslation.T("gallery.footer.session_abbrev", "Play");
            }
        }

        private void AddNetSessionPanelRow(List<InternalSettingDefinition> defs)
        {
            defs.Add(new InternalSettingDefinition
            {
                Key = "net_rules.session_panel",
                GroupKey = "net_rules",
                Label = VPBTranslation.T("settings.net_rules.session_panel", "Play with a friend"),
                Tooltip = VPBTranslation.T("settings.tip.net_rules.session_panel",
                    "Opens Play with a friend. Steam is the default: I have a code, or I'll make a room. A live session collapses to a bar instead of closing. Leave ends the session."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbNetSessionUi.IsWanted
                    ? VPBTranslation.T("settings.net_rules.session_panel_close", "Close")
                    : VPBTranslation.T("settings.net_rules.session_panel_open", "Open"),
                OnAction = () =>
                {
                    VpbNetSessionUi.Toggle();
                    InvalidateInternalSettingsDefsCache();
                    RefreshInternalSettingsListRows(true);
                }
            });
        }

        private void AddNetRuleWindowRow(List<InternalSettingDefinition> defs)
        {
            defs.Add(new InternalSettingDefinition
            {
                Key = "net_rules.window",
                GroupKey = "net_rules",
                Label = VPBTranslation.T("settings.net_rules.window", "Session rules"),
                Tooltip = VPBTranslation.T("settings.tip.net_rules.window",
                    "What the other person may do to you and to your scene, and what they have allowed you. Seven questions, three answers each: blocked, ask, allowed. Opens inside Play with a friend — also the Rules button in that title bar."),
                SearchText = VpbNetRuleCatalog.SearchBlob(),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VpbNetRulesUi.IsOpen
                    ? VPBTranslation.T("settings.net_rules.window_close", "Close")
                    : VPBTranslation.T("settings.net_rules.window_open", "Open"),
                OnAction = VpbNetRulesUi.Toggle
            });
        }
    }
}
