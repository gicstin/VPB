using System;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private void OpenSettings()
        {
            // Reset transient quick-menu hover/edit UI so stale tooltip/popup state
            // cannot leak into or out of the Settings page.
            try
            {
                m_QuickMenuEditMode = false;
                QuickMenuHideAssignPopup();
                QuickMenuClearTooltip(null);
            }
            catch { }

            if (MiniMode)
            {
                SetMiniMode(false);
            }
            m_ShowSettings = true;
            m_SettingsUiKeyDraft = (Settings.Instance != null && Settings.Instance.UIKey != null) ? Settings.Instance.UIKey.Value : "";
            m_SettingsGalleryKeyDraft = (Settings.Instance != null && Settings.Instance.GalleryKey != null) ? Settings.Instance.GalleryKey.Value : "";
            m_SettingsCreateGalleryKeyDraft = (Settings.Instance != null && Settings.Instance.CreateGalleryKey != null) ? Settings.Instance.CreateGalleryKey.Value : "";
            m_SettingsHubKeyDraft = (Settings.Instance != null && Settings.Instance.HubKey != null) ? Settings.Instance.HubKey.Value : "";
            m_SettingsClearConsoleKeyDraft = (Settings.Instance != null && Settings.Instance.ClearConsoleKey != null) ? Settings.Instance.ClearConsoleKey.Value : "";
            m_SettingsPluginsAlwaysEnabledDraft = (Settings.Instance != null && Settings.Instance.PluginsAlwaysEnabled != null) ? Settings.Instance.PluginsAlwaysEnabled.Value : false;
            m_SettingsLoadDependenciesWithPackageDraft = (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null) ? Settings.Instance.LoadDependenciesWithPackage.Value : true;
            m_SettingsForceLatestDependenciesDraft = (Settings.Instance != null && Settings.Instance.ForceLatestDependencies != null) ? Settings.Instance.ForceLatestDependencies.Value : false;
            m_SettingsEnableUiTransparencyDraft = (Settings.Instance != null && Settings.Instance.EnableUiTransparency != null) ? Settings.Instance.EnableUiTransparency.Value : true;
            m_SettingsUiTransparencyValueDraft = (Settings.Instance != null && Settings.Instance.UiTransparencyValue != null) ? Settings.Instance.UiTransparencyValue.Value : 0.5f;
            m_SettingsIsDevModeDraft = (VPBConfig.Instance != null) ? VPBConfig.Instance.IsDevMode : false;
            m_SettingsGalleryCategoryQuickOrderDraft = (VPBConfig.Instance != null) ? (VPBConfig.Instance.GalleryCategoryQuickOrder ?? "") : "";
            m_SettingsGalleryCategoryQuickSwitchHiddenDraft = (VPBConfig.Instance != null) ? (VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "") : "";
            m_SettingsError = null;
        }

        private void SaveSettings()
        {
            try
            {
                var parsed = KeyUtil.Parse(m_SettingsUiKeyDraft ?? "");
                var parsedGalleryKey = KeyUtil.Parse(m_SettingsGalleryKeyDraft ?? "");
                var parsedCreateGalleryKey = KeyUtil.Parse(m_SettingsCreateGalleryKeyDraft ?? "");
                var parsedHubKey = KeyUtil.Parse(m_SettingsHubKeyDraft ?? "");
                var parsedClearConsoleKey = KeyUtil.Parse(m_SettingsClearConsoleKeyDraft ?? "");

                if (parsed.IsSame(parsedGalleryKey) || parsed.IsSame(parsedCreateGalleryKey) || parsed.IsSame(parsedHubKey) || parsed.IsSame(parsedClearConsoleKey)
                    || parsedGalleryKey.IsSame(parsedCreateGalleryKey) || parsedGalleryKey.IsSame(parsedHubKey) || parsedGalleryKey.IsSame(parsedClearConsoleKey)
                    || parsedCreateGalleryKey.IsSame(parsedHubKey) || parsedCreateGalleryKey.IsSame(parsedClearConsoleKey)
                    || parsedHubKey.IsSame(parsedClearConsoleKey))
                {
                    m_SettingsError = VPBTranslation.T("hook.settings.error.duplicate_hotkeys", "Duplicate hotkeys are not allowed.");
                    return;
                }

                if (Settings.Instance != null && Settings.Instance.UIKey != null)
                {
                    Settings.Instance.UIKey.Value = parsed.keyPattern;
                }
                if (Settings.Instance != null && Settings.Instance.GalleryKey != null)
                {
                    Settings.Instance.GalleryKey.Value = parsedGalleryKey.keyPattern;
                }
                if (Settings.Instance != null && Settings.Instance.CreateGalleryKey != null)
                {
                    Settings.Instance.CreateGalleryKey.Value = parsedCreateGalleryKey.keyPattern;
                }
                if (Settings.Instance != null && Settings.Instance.HubKey != null)
                {
                    Settings.Instance.HubKey.Value = parsedHubKey.keyPattern;
                }
                if (Settings.Instance != null && Settings.Instance.ClearConsoleKey != null)
                {
                    Settings.Instance.ClearConsoleKey.Value = parsedClearConsoleKey.keyPattern;
                }
                if (Settings.Instance != null && Settings.Instance.PluginsAlwaysEnabled != null)
                {
                    if (Settings.Instance.PluginsAlwaysEnabled.Value != m_SettingsPluginsAlwaysEnabledDraft)
                    {
                        Settings.Instance.PluginsAlwaysEnabled.Value = m_SettingsPluginsAlwaysEnabledDraft;
                    }
                }
                if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null)
                {
                    if (Settings.Instance.LoadDependenciesWithPackage.Value != m_SettingsLoadDependenciesWithPackageDraft)
                    {
                        Settings.Instance.LoadDependenciesWithPackage.Value = m_SettingsLoadDependenciesWithPackageDraft;
                    }
                }
                if (Settings.Instance != null && Settings.Instance.ForceLatestDependencies != null)
                {
                    if (Settings.Instance.ForceLatestDependencies.Value != m_SettingsForceLatestDependenciesDraft)
                    {
                        Settings.Instance.ForceLatestDependencies.Value = m_SettingsForceLatestDependenciesDraft;
                    }
                }
                if (Settings.Instance != null && Settings.Instance.EnableUiTransparency != null)
                {
                    if (Settings.Instance.EnableUiTransparency.Value != m_SettingsEnableUiTransparencyDraft)
                    {
                        Settings.Instance.EnableUiTransparency.Value = m_SettingsEnableUiTransparencyDraft;
                    }
                }
                if (Settings.Instance != null && Settings.Instance.UiTransparencyValue != null)
                {
                    if (Math.Abs(Settings.Instance.UiTransparencyValue.Value - m_SettingsUiTransparencyValueDraft) > 0.001f)
                    {
                        Settings.Instance.UiTransparencyValue.Value = m_SettingsUiTransparencyValueDraft;
                    }
                }
                if (VPBConfig.Instance != null)
                {
                    bool changed = false;
                    if (VPBConfig.Instance.IsDevMode != m_SettingsIsDevModeDraft)
                    {
                        VPBConfig.Instance.IsDevMode = m_SettingsIsDevModeDraft;
                        changed = true;
                    }

                    string qo = m_SettingsGalleryCategoryQuickOrderDraft ?? "";
                    if (!string.Equals(VPBConfig.Instance.GalleryCategoryQuickOrder ?? "", qo, StringComparison.Ordinal))
                    {
                        VPBConfig.Instance.GalleryCategoryQuickOrder = qo;
                        changed = true;
                    }

                    string qh = m_SettingsGalleryCategoryQuickSwitchHiddenDraft ?? "";
                    if (!string.Equals(VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "", qh, StringComparison.Ordinal))
                    {
                        VPBConfig.Instance.GalleryCategoryQuickSwitchHidden = qh;
                        changed = true;
                    }

                    if (changed)
                    {
                        VPBConfig.Instance.Save(false, true);
                    }
                }
                UIKey = parsed;
                GalleryKey = parsedGalleryKey;
                CreateGalleryKey = parsedCreateGalleryKey;
                HubKey = parsedHubKey;
                ClearConsoleKey = parsedClearConsoleKey;
                this.Config.Save();
                CloseSettings();
            }
            catch
            {
                m_SettingsError = VPBTranslation.T("hook.settings.error.invalid_hotkey", "Invalid setting. Example hotkey: Ctrl+Shift+V");
            }
        }

        private void CloseSettings()
        {
            m_ShowSettings = false;
            m_SettingsError = null;

            // Clear any lingering hover tooltip/status from quick-menu state.
            try
            {
                m_QuickMenuEditMode = false;
                QuickMenuHideAssignPopup();
                QuickMenuClearTooltip(null);
            }
            catch { }
        }

        private string DrawHotkeyField(string label, string fieldName, string currentValue, float height)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));

            GUIStyle style = GUI.skin.textField;
            int id = GUIUtility.GetControlID(FocusType.Keyboard);

            Rect rect = GUILayoutUtility.GetRect(new GUIContent(currentValue), style, GUILayout.ExpandWidth(true), GUILayout.Height(height));

            Event e = Event.current;
            bool isFocused = GUIUtility.keyboardControl == id;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    GUIUtility.keyboardControl = id;
                    e.Use();
                }
            }

            if (isFocused && e.type == EventType.KeyDown)
            {
                if (e.keyCode != KeyCode.None && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Tab)
                {
                    string newKey = "";
                    if (e.control) newKey += "Ctrl+";
                    if (e.shift) newKey += "Shift+";
                    if (e.alt) newKey += "Alt+";

                    KeyCode k = e.keyCode;
                    bool isModifier = k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                                      k == KeyCode.LeftShift || k == KeyCode.RightShift ||
                                      k == KeyCode.LeftAlt || k == KeyCode.RightAlt;

                    if (!isModifier)
                    {
                        newKey += k.ToString();
                    }
                    else
                    {
                        if (newKey.EndsWith("+")) newKey = newKey.Substring(0, newKey.Length - 1);
                    }

                    currentValue = newKey;
                    e.Use();
                }
            }

            if (e.type == EventType.Repaint)
            {
                style.Draw(rect, new GUIContent(currentValue), rect.Contains(e.mousePosition), isFocused, false, isFocused);
            }

            GUILayout.EndHorizontal();
            return currentValue;
        }

        private void DrawSettingsPage(float buttonHeight)
        {
            GUILayout.BeginVertical(m_StyleSection);
            GUILayout.Label(VPBTranslation.T("hook.settings.title", "Settings"), m_StyleHeader);
            GUILayout.Space(6);

            GUILayout.Label(VPBTranslation.T("hook.settings.header.hook", "Hook Settings"), m_StyleHeader);
            GUILayout.Space(4);

            m_SettingsUiKeyDraft = DrawHotkeyField(VPBTranslation.T("hook.settings.key.ui", "Show/Hide VPB"), "UIKeyField", m_SettingsUiKeyDraft ?? "", buttonHeight);
            m_SettingsHubKeyDraft = DrawHotkeyField(VPBTranslation.T("hook.settings.key.hub", "Open Hub Browser"), "HubKeyField", m_SettingsHubKeyDraft ?? "", buttonHeight);
            m_SettingsClearConsoleKeyDraft = DrawHotkeyField(VPBTranslation.T("hook.settings.key.clear_console", "Clear Console"), "ClearConsoleKeyField", m_SettingsClearConsoleKeyDraft ?? "", buttonHeight);

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label(VPBTranslation.T("hook.settings.visibility", "Visibility"), GUILayout.Width(100));
            float visibilityValue = 1.0f - m_SettingsUiTransparencyValueDraft;
            visibilityValue = GUILayout.HorizontalSlider(visibilityValue, 0.0f, 1.0f);
            m_SettingsUiTransparencyValueDraft = 1.0f - visibilityValue;
            GUILayout.Space(10);
            GUILayout.Label((visibilityValue * 100).ToString("F0") + "%", GUILayout.Width(35));
            GUILayout.EndHorizontal();
            GUILayout.Label(VPBTranslation.T("hook.settings.visibility_hint", "Adjust visibility when idle (100% = Opaque, 0% = Invisible)."));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsPluginsAlwaysEnabledDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsPluginsAlwaysEnabledDraft = !m_SettingsPluginsAlwaysEnabledDraft;
            }
            GUILayout.Label(VPBTranslation.T("hook.settings.plugins_always_enabled", "Plugins always enabled"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(28f), GUILayout.Height(buttonHeight)))
            {
                ToggleInfoCard(ref m_ShowPluginsAlwaysEnabledInfo);
            }
            GUILayout.EndHorizontal();

            DrawInfoCard(ref m_ShowPluginsAlwaysEnabledInfo, VPBTranslation.T("hook.settings.plugins_always_enabled", "Plugins always enabled"), () =>
            {
                GUILayout.Space(4);
                GUILayout.Label(VPBTranslation.T("hook.settings.info.plugins_on_1", "When this is ON, plugins are treated as always enabled."), m_StyleInfoCardTextWrapped);
                GUILayout.Space(2);
                GUILayout.Label(VPBTranslation.T("hook.settings.info.plugins_on_2", "Tip: Leave this OFF if you want VaM to respect per-package/per-scene plugin enable state."), m_StyleInfoCardTextWrapped);
            });

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsLoadDependenciesWithPackageDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsLoadDependenciesWithPackageDraft = !m_SettingsLoadDependenciesWithPackageDraft;
            }
            GUILayout.Label(VPBTranslation.T("hook.settings.load_deps", "Load dependencies when loading a package"));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsForceLatestDependenciesDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsForceLatestDependenciesDraft = !m_SettingsForceLatestDependenciesDraft;
            }
            GUILayout.Label(VPBTranslation.T("hook.settings.force_latest", "Force latest dependency versions"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(VPBTranslation.T("hook.settings.whitelist", "Whitelist"), m_StyleButtonSmall, GUILayout.Width(110f), GUILayout.Height(buttonHeight)))
            {
                OpenDependencyWhitelistUGUI();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.Space(10);

            GUILayout.Label(VPBTranslation.T("hook.settings.header.scan_whitelist", "VaM Scan Whitelist"), m_StyleHeader);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            bool swEnabled = ScanWhitelistManager.Instance.IsEnabled;
            if (GUILayout.Button(swEnabled ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                ScanWhitelistManager.Instance.SetEnabled(!swEnabled);
                ScanWhitelistManager.Instance.Save();
            }
            GUILayout.Label(VPBTranslation.T("hook.settings.scan_whitelist.enable", "Enable VaM scan whitelist (restrict VaM startup scan to whitelisted folders)"));
            GUILayout.EndHorizontal();

            if (ScanWhitelistManager.Instance.IsEnabledButEmpty())
            {
                GUILayout.Label(VPBTranslation.T("hook.settings.scan_whitelist.empty_warning", "⚠ Warning: whitelist is enabled but empty — all packages will be excluded from VaM's scan!"), m_StyleInfoCardTextWrapped);
            }

            GUILayout.Space(4);
            if (GUILayout.Button(VPBTranslation.T("hook.settings.scan_whitelist.manage", "Manage Scan Whitelist..."), m_StyleButton, GUILayout.Height(buttonHeight)))
            {
                m_ShowScanWhitelistWindow = true;
            }

            GUILayout.Space(10);

            GUILayout.Label(VPBTranslation.T("hook.settings.header.gallery", "Gallery Pane Settings"), m_StyleHeader);
            GUILayout.Space(4);

            m_SettingsGalleryKeyDraft = DrawHotkeyField(VPBTranslation.T("hook.settings.key.gallery", "Show/Hide Panes"), "GalleryKeyField", m_SettingsGalleryKeyDraft ?? "", buttonHeight);
            m_SettingsCreateGalleryKeyDraft = DrawHotkeyField(VPBTranslation.T("hook.settings.key.create_gallery", "Create Gallery Pane"), "CreateGalleryKeyField", m_SettingsCreateGalleryKeyDraft ?? "", buttonHeight);

            GUILayout.Space(6);

            if (GUILayout.Button(VPBTranslation.T("hook.settings.adjust_position", "Adjust Position"), m_StyleButton, GUILayout.Height(buttonHeight)))
            {
                OpenQuickMenuPositionWindow();
            }

            GUILayout.Space(8);
            GUILayout.Label(VPBTranslation.T("hook.settings.gallery_quick_order", "Quick category order (number keys 1–9, 0)"), m_StyleHeader);
            GUILayout.Label(VPBTranslation.T("hook.settings.gallery_quick_order.hint", "One name per line or comma-separated. Matches gallery category names. Empty order = default (ALL VAR, Scenes, Appearance, \u2026). Use Skin \u2192 Person Skin if needed."), m_StyleInfoCardTextWrapped);
            m_SettingsGalleryCategoryQuickOrderDraft = GUILayout.TextArea(m_SettingsGalleryCategoryQuickOrderDraft ?? "", GUILayout.MinHeight(72));

            GUILayout.Space(6);
            GUILayout.Label(VPBTranslation.T("hook.settings.gallery_quick_hidden", "Hide from quick menu only"), m_StyleHeader);
            GUILayout.Label(VPBTranslation.T("hook.settings.gallery_quick_hidden.hint", "Listed categories stay in the side category list but are removed from the header quick menu and number keys."), m_StyleInfoCardTextWrapped);
            m_SettingsGalleryCategoryQuickSwitchHiddenDraft = GUILayout.TextArea(m_SettingsGalleryCategoryQuickSwitchHiddenDraft ?? "", GUILayout.MinHeight(56));

            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(m_SettingsError))
            {
                GUILayout.Space(4);
                GUILayout.Label(m_SettingsError, m_StyleInfoCardTextWrapped);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(VPBTranslation.T("hook.cancel", "Cancel"), m_StyleButton, GUILayout.Height(buttonHeight)))
            {
                CloseSettings();
            }
            if (GUILayout.Button(VPBTranslation.T("hook.save", "Save"), m_StyleButtonPrimary, GUILayout.Height(buttonHeight)))
            {
                SaveSettings();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
