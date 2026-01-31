using System;
using UnityEngine;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private void OpenSettings()
        {
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
                    m_SettingsError = "Duplicate hotkeys are not allowed.";
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

                    if (changed)
                    {
                        VPBConfig.Instance.Save();
                        VPBConfig.Instance.TriggerChange();
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
                m_SettingsError = "Invalid setting. Example hotkey: Ctrl+Shift+V";
            }
        }

        private void CloseSettings()
        {
            m_ShowSettings = false;
            m_SettingsError = null;
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
            GUILayout.Label("Settings", m_StyleHeader);
            GUILayout.Space(6);

            GUILayout.Label("Hook Settings", m_StyleHeader);
            GUILayout.Space(4);

            m_SettingsUiKeyDraft = DrawHotkeyField("Show/Hide VPB", "UIKeyField", m_SettingsUiKeyDraft ?? "", buttonHeight);
            m_SettingsHubKeyDraft = DrawHotkeyField("Open Hub Browser", "HubKeyField", m_SettingsHubKeyDraft ?? "", buttonHeight);
            m_SettingsClearConsoleKeyDraft = DrawHotkeyField("Clear Console", "ClearConsoleKeyField", m_SettingsClearConsoleKeyDraft ?? "", buttonHeight);

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Visibility", GUILayout.Width(100));
            float visibilityValue = 1.0f - m_SettingsUiTransparencyValueDraft;
            visibilityValue = GUILayout.HorizontalSlider(visibilityValue, 0.0f, 1.0f);
            m_SettingsUiTransparencyValueDraft = 1.0f - visibilityValue;
            GUILayout.Space(10);
            GUILayout.Label((visibilityValue * 100).ToString("F0") + "%", GUILayout.Width(35));
            GUILayout.EndHorizontal();
            GUILayout.Label("Adjust visibility when idle (100% = Opaque, 0% = Invisible).");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsPluginsAlwaysEnabledDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsPluginsAlwaysEnabledDraft = !m_SettingsPluginsAlwaysEnabledDraft;
            }
            GUILayout.Label("Plugins always enabled");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(28f), GUILayout.Height(buttonHeight)))
            {
                ToggleInfoCard(ref m_ShowPluginsAlwaysEnabledInfo);
            }
            GUILayout.EndHorizontal();

            DrawInfoCard(ref m_ShowPluginsAlwaysEnabledInfo, "Plugins always enabled", () =>
            {
                GUILayout.Space(4);
                GUILayout.Label("When this is ON, plugins are treated as always enabled.", m_StyleInfoCardText);
                GUILayout.Space(2);
                GUILayout.Label("Tip: Leave this OFF if you want VaM to respect per-package/per-scene plugin enable state.", m_StyleInfoCardText);
            });

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsLoadDependenciesWithPackageDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsLoadDependenciesWithPackageDraft = !m_SettingsLoadDependenciesWithPackageDraft;
            }
            GUILayout.Label("Load dependencies when loading a package");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsForceLatestDependenciesDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsForceLatestDependenciesDraft = !m_SettingsForceLatestDependenciesDraft;
            }
            GUILayout.Label("Force latest dependency versions");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Whitelist", m_StyleButtonSmall, GUILayout.Width(110f), GUILayout.Height(buttonHeight)))
            {
                OpenDependencyWhitelistUGUI();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.Space(10);

            GUILayout.Label("Gallery Pane Settings", m_StyleHeader);
            GUILayout.Space(4);

            m_SettingsGalleryKeyDraft = DrawHotkeyField("Show/Hide Panes", "GalleryKeyField", m_SettingsGalleryKeyDraft ?? "", buttonHeight);
            m_SettingsCreateGalleryKeyDraft = DrawHotkeyField("Create Gallery Pane", "CreateGalleryKeyField", m_SettingsCreateGalleryKeyDraft ?? "", buttonHeight);

            GUILayout.Space(6);

            if (GUILayout.Button("Adjust Position", m_StyleButton, GUILayout.Height(buttonHeight)))
            {
                OpenQuickMenuPositionWindow();
            }

            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(m_SettingsError))
            {
                GUILayout.Space(4);
                GUILayout.Label(m_SettingsError, m_StyleInfoCardText);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", m_StyleButton, GUILayout.Height(buttonHeight)))
            {
                CloseSettings();
            }
            if (GUILayout.Button("Save", m_StyleButtonPrimary, GUILayout.Height(buttonHeight)))
            {
                SaveSettings();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
