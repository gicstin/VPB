using BepInEx;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using Prime31.MessageKit;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace VPB
{
    // Plugin metadata attribute: plugin ID, plugin name, plugin version (must be numeric)
    [BepInPlugin("VPB", "VPB", "0.06")]

    public partial class VamHookPlugin : BaseUnityPlugin // Inherits BaseUnityPlugin
    {
        private KeyUtil UIKey;
        private KeyUtil GalleryKey;
        private KeyUtil CreateGalleryKey;
        private KeyUtil HubKey;
        private Vector2 UIPosition;
        private bool MiniMode;
        
        // Unload All Window
        private class UnloadItem
        {
            public string Uid;
            public string Path;
            public string Type;
            public bool Checked;
            public bool IsActive;
        }
        private bool m_ShowUnloadWindow = false;
        private System.Collections.Generic.List<UnloadItem> m_UnloadList = new System.Collections.Generic.List<UnloadItem>();
        private string m_UnloadFilter = "";
        private bool m_ExcludeActivePackages = true;
        private Vector2 m_UnloadScroll = Vector2.zero;
        private Rect m_UnloadWindowRect = new Rect(200, 200, 600, 500);

        // Remove Old/Damaged Window
        private class RemoveItem
        {
            public string Uid;
            public string Path;
            public string Type;
            public bool Checked;
        }
        private bool m_ShowRemoveWindow = false;
        private System.Collections.Generic.List<RemoveItem> m_RemoveList = new System.Collections.Generic.List<RemoveItem>();
        private string m_RemoveFilter = "";
        private bool m_ExcludeOld = false;
        private Vector2 m_RemoveScroll = Vector2.zero;
        private Rect m_RemoveWindowRect = new Rect(250, 250, 600, 500);

        private bool m_ShowDownscaleTexturesInfo;
        private bool m_ShowPrioritizeFaceTexturesInfo;
        private bool m_ShowPrioritizeHairTexturesInfo;
        private bool m_ShowPluginsAlwaysEnabledInfo;
        private bool m_ShowRemoveOldDamagedInfo;
        private bool m_ShowUninstallAllInfo;
        private bool m_ShowGcRefreshInfo;
        private bool m_ShowSettings;
        private string m_SettingsUiKeyDraft;
        private string m_SettingsGalleryKeyDraft;
        private string m_SettingsCreateGalleryKeyDraft;
        private string m_SettingsHubKeyDraft;
        private bool m_SettingsPrioritizeFaceTexturesDraft;
        private bool m_SettingsPrioritizeHairTexturesDraft;
        private bool m_SettingsPluginsAlwaysEnabledDraft;
        private bool m_SettingsEnableUiTransparencyDraft;
        private float m_SettingsUiTransparencyValueDraft;
        private bool m_SettingsEnableGalleryFadeDraft;
        private string m_SettingsError;
        private float m_ExpandedHeight;
        float m_UIScale = 1;
        Rect m_Rect = new Rect(0, 0, 220, 50);

        private const float MinUiScale = 0.6f;
        private const float MaxUiScale = 2.4f;
        private const float MiniModeHeight = 50f;

        private GUIStyle m_TitleTagStyle;
        private GUIStyle m_TitleBarLabelStyle;
        private GUIStyle m_DragHintStyle;
        private bool m_StylesInited;
        private Texture2D m_TexPanelBg;
        private Texture2D m_TexSectionBg;
        private Texture2D m_TexBtnBg;
        private Texture2D m_TexBtnBgHover;
        private Texture2D m_TexBtnBgActive;
        private Texture2D m_TexBtnDangerBg;
        private Texture2D m_TexBtnDangerBgHover;
        private Texture2D m_TexBtnDangerBgActive;
        private Texture2D m_TexBtnPrimaryBg;
        private Texture2D m_TexBtnPrimaryBgHover;
        private Texture2D m_TexBtnPrimaryBgActive;
        private Texture2D m_TexBtnCheckboxBg;
        private Texture2D m_TexBtnCheckboxBgHover;
        private Texture2D m_TexBtnCheckboxBgActive;
        private Texture2D m_TexWindowBorder;
        private Texture2D m_TexWindowBorderActive;
        private Texture2D m_TexInfoCardBg;
        private Texture2D m_TexFpsBadgeBg;
        private Texture2D m_TexFpsBadgeOuterBg;
        private GUIStyle m_StylePanel;
        private GUIStyle m_StyleSection;
        private GUIStyle m_StyleHeader;
        private GUIStyle m_StyleSubHeader;
        private GUIStyle m_StyleButton;
        private GUIStyle m_StyleButtonSmall;
        private GUIStyle m_StyleButtonDanger;
        private GUIStyle m_StyleButtonPrimary;
        private GUIStyle m_StyleButtonCheckbox;
        private GUIStyle m_StyleToggle;
        private GUIStyle m_StyleWindow;
        private GUIStyle m_StyleWindowBorder;
        private GUIStyle m_StyleInfoIcon;
        private GUIStyle m_StyleInfoCard;
        private GUIStyle m_StyleInfoCardTitle;
        private GUIStyle m_StyleInfoCardText;
        private GUIStyle m_StyleInfoClose;
        private GUIStyle m_StyleFpsBadge;
        private GUIStyle m_StyleFpsBadgeOuter;

        private bool m_WindowActive;
        private float m_WindowAlphaState = 1.0f; // Shared state for window transparency
        private Rect m_RealWindowRect; // Capture the fully rendered rect from Repaint for accurate hover detection

        public static VamHookPlugin singleton;

        public static string CurrentScenePackageUid;

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply(false, true);
            return tex;
        }

        private void CloseAllInfoCards()
        {
            m_ShowDownscaleTexturesInfo = false;
            m_ShowPrioritizeFaceTexturesInfo = false;
            m_ShowPrioritizeHairTexturesInfo = false;
            m_ShowPluginsAlwaysEnabledInfo = false;
            m_ShowRemoveOldDamagedInfo = false;
            m_ShowUninstallAllInfo = false;
            m_ShowGcRefreshInfo = false;
        }

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
            m_SettingsPrioritizeFaceTexturesDraft = (Settings.Instance != null && Settings.Instance.PrioritizeFaceTextures != null) ? Settings.Instance.PrioritizeFaceTextures.Value : true;
            m_SettingsPrioritizeHairTexturesDraft = (Settings.Instance != null && Settings.Instance.PrioritizeHairTextures != null) ? Settings.Instance.PrioritizeHairTextures.Value : true;
            m_SettingsPluginsAlwaysEnabledDraft = (Settings.Instance != null && Settings.Instance.PluginsAlwaysEnabled != null) ? Settings.Instance.PluginsAlwaysEnabled.Value : false;
            m_SettingsEnableUiTransparencyDraft = (Settings.Instance != null && Settings.Instance.EnableUiTransparency != null) ? Settings.Instance.EnableUiTransparency.Value : true;
            m_SettingsUiTransparencyValueDraft = (Settings.Instance != null && Settings.Instance.UiTransparencyValue != null) ? Settings.Instance.UiTransparencyValue.Value : 0.5f;
            m_SettingsEnableGalleryFadeDraft = (Settings.Instance != null && Settings.Instance.EnableGalleryFade != null) ? Settings.Instance.EnableGalleryFade.Value : true;
            m_SettingsError = null;
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

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Visibility", GUILayout.Width(100));
            // Invert the value for display: 1.0 = Opaque (0.0 Transparency), 0.0 = Invisible (1.0 Transparency)
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
            if (GUILayout.Button(m_SettingsPrioritizeFaceTexturesDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsPrioritizeFaceTexturesDraft = !m_SettingsPrioritizeFaceTexturesDraft;
            }
            GUILayout.Label("Textures: Prioritize face/makeup");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(28f), GUILayout.Height(buttonHeight)))
            {
                ToggleInfoCard(ref m_ShowPrioritizeFaceTexturesInfo);
            }
            GUILayout.EndHorizontal();

            DrawInfoCard(ref m_ShowPrioritizeFaceTexturesInfo, "Prioritize face/makeup textures", () =>
            {
                GUILayout.Space(4);
                GUILayout.Label("When this is ON, VPB tries to process face textures and face overlays (makeup/freckles/etc.) earlier during scene load.", m_StyleInfoCardText);
                GUILayout.Space(2);
                GUILayout.Label("It does this by reordering VaM's image load queue (it does not download anything extra).", m_StyleInfoCardText);
                GUILayout.Space(2);
                GUILayout.Label("Tip: Leave this ON if you want faces to resolve sooner while clothing/hair textures continue loading.", m_StyleInfoCardText);
            });

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsPrioritizeHairTexturesDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsPrioritizeHairTexturesDraft = !m_SettingsPrioritizeHairTexturesDraft;
            }
            GUILayout.Label("Textures: Prioritize hair");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(28f), GUILayout.Height(buttonHeight)))
            {
                ToggleInfoCard(ref m_ShowPrioritizeHairTexturesInfo);
            }
            GUILayout.EndHorizontal();

            DrawInfoCard(ref m_ShowPrioritizeHairTexturesInfo, "Prioritize hair textures", () =>
            {
                GUILayout.Space(4);
                GUILayout.Label("When this is ON, VPB tries to process hair earlier during scene load.", m_StyleInfoCardText);
                GUILayout.Space(2);
                GUILayout.Label("It does this by reordering VaM's image load queue (it does not download anything extra).", m_StyleInfoCardText);
            });

            GUILayout.Space(10);

            GUILayout.Label("Gallery Pane Settings", m_StyleHeader);
            GUILayout.Space(4);

            m_SettingsGalleryKeyDraft = DrawHotkeyField("Show/Hide Panes", "GalleryKeyField", m_SettingsGalleryKeyDraft ?? "", buttonHeight);
            m_SettingsCreateGalleryKeyDraft = DrawHotkeyField("Create Gallery Pane", "CreateGalleryKeyField", m_SettingsCreateGalleryKeyDraft ?? "", buttonHeight);

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_SettingsEnableGalleryFadeDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsEnableGalleryFadeDraft = !m_SettingsEnableGalleryFadeDraft;
            }
            GUILayout.Label("Enable Gallery Button Fade");
            GUILayout.EndHorizontal();

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
                try
                {
                    var parsed = KeyUtil.Parse(m_SettingsUiKeyDraft ?? "");
                    var parsedGalleryKey = KeyUtil.Parse(m_SettingsGalleryKeyDraft ?? "");
                    var parsedCreateGalleryKey = KeyUtil.Parse(m_SettingsCreateGalleryKeyDraft ?? "");
                    var parsedHubKey = KeyUtil.Parse(m_SettingsHubKeyDraft ?? "");

                    if (parsed.IsSame(parsedGalleryKey) || parsed.IsSame(parsedCreateGalleryKey) || parsed.IsSame(parsedHubKey) || parsedGalleryKey.IsSame(parsedCreateGalleryKey) || parsedGalleryKey.IsSame(parsedHubKey) || parsedCreateGalleryKey.IsSame(parsedHubKey))
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
                    if (Settings.Instance != null && Settings.Instance.PluginsAlwaysEnabled != null)
                    {
                        if (Settings.Instance.PluginsAlwaysEnabled.Value != m_SettingsPluginsAlwaysEnabledDraft)
                        {
                            Settings.Instance.PluginsAlwaysEnabled.Value = m_SettingsPluginsAlwaysEnabledDraft;
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
                    if (Settings.Instance != null && Settings.Instance.EnableGalleryFade != null)
                    {
                        if (Settings.Instance.EnableGalleryFade.Value != m_SettingsEnableGalleryFadeDraft)
                        {
                            Settings.Instance.EnableGalleryFade.Value = m_SettingsEnableGalleryFadeDraft;
                        }
                    }
                    if (Settings.Instance != null && Settings.Instance.PrioritizeFaceTextures != null)
                    {
                        if (Settings.Instance.PrioritizeFaceTextures.Value != m_SettingsPrioritizeFaceTexturesDraft)
                        {
                            Settings.Instance.PrioritizeFaceTextures.Value = m_SettingsPrioritizeFaceTexturesDraft;
                        }
                    }
                    if (Settings.Instance != null && Settings.Instance.PrioritizeHairTextures != null)
                    {
                        if (Settings.Instance.PrioritizeHairTextures.Value != m_SettingsPrioritizeHairTexturesDraft)
                        {
                            Settings.Instance.PrioritizeHairTextures.Value = m_SettingsPrioritizeHairTexturesDraft;
                        }
                    }
                    UIKey = parsed;
                    GalleryKey = parsedGalleryKey;
                    CreateGalleryKey = parsedCreateGalleryKey;
                    HubKey = parsedHubKey;
                    CloseSettings();
                }
                catch
                {
                    m_SettingsError = "Invalid setting. Example hotkey: Ctrl+Shift+V";
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }



        private void ToggleInfoCard(ref bool visible)
        {
            bool newValue = !visible;
            if (newValue)
            {
                CloseAllInfoCards();
            }
            visible = newValue;
        }

        private void DrawInfoCard(ref bool visible, string title, Action drawBody)
        {
            if (!visible)
                return;

            GUILayout.BeginVertical(m_StyleInfoCard);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, m_StyleInfoCardTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", m_StyleInfoClose, GUILayout.Width(18), GUILayout.Height(18)))
            {
                visible = false;
            }
            GUILayout.EndHorizontal();

            if (visible)
            {
                drawBody?.Invoke();
            }

            GUILayout.EndVertical();
        }

        private void DrawPhiSplitButtonsInRect(Rect r, string leftText, GUIStyle leftStyle, Action leftAction, string rightText, GUIStyle rightStyle, Action rightAction, float phi)
        {
            const float gutter = 6f;
            float usableWidth = Mathf.Max(0f, r.width - gutter);
            float leftWidth = usableWidth / (1f + phi);
            float rightWidth = Mathf.Max(0f, usableWidth - leftWidth);

            var leftRect = new Rect(r.x, r.y, leftWidth, r.height);
            var rightRect = new Rect(r.x + leftWidth + gutter, r.y, rightWidth, r.height);

            var actualLeftStyle = leftStyle ?? GUI.skin.button;
            var actualRightStyle = rightStyle ?? GUI.skin.button;

            if (GUI.Button(leftRect, leftText, actualLeftStyle))
            {
                leftAction?.Invoke();
            }
            if (GUI.Button(rightRect, rightText, actualRightStyle))
            {
                rightAction?.Invoke();
            }
        }

        private void DrawPhiSplitButtons(string leftText, GUIStyle leftStyle, Action leftAction, string rightText, GUIStyle rightStyle, Action rightAction, float phi, float height)
        {
            var r = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            DrawPhiSplitButtonsInRect(r, leftText, leftStyle, leftAction, rightText, rightStyle, rightAction, phi);
        }

        private static Texture2D MakeBorderedTex(int width, int height, Color fill, Color border, int borderPx = 1)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = x < borderPx || y < borderPx || x >= (width - borderPx) || y >= (height - borderPx);
                    tex.SetPixel(x, y, isBorder ? border : fill);
                }
            }
            tex.Apply(false, true);
            return tex;
        }

        private void EnsureStyles()
        {
            // Lazily initialize GUI styles once the Unity GUI skin is available.
            if (m_StylesInited)
                return;
            if (GUI.skin == null)
                return;

            const float windowAlpha = 0.70f;
            const float sectionAlpha = 0.62f;
            const float buttonAlpha = 0.84f;
            const float borderAlpha = 0.72f;

            var panelFill = new Color(0.11f, 0.12f, 0.14f, windowAlpha);
            var panelBorder = new Color(0.42f, 0.45f, 0.50f, 0.35f);
            var sectionFill = new Color(0.16f, 0.17f, 0.20f, sectionAlpha);
            var sectionBorder = new Color(0.38f, 0.41f, 0.46f, 0.30f);
            var btnFill = new Color(0.22f, 0.24f, 0.29f, buttonAlpha);
            var btnBorder = new Color(0.70f, 0.74f, 0.80f, 0.22f);
            var btnHoverFill = new Color(0.27f, 0.30f, 0.36f, Mathf.Clamp01(buttonAlpha + 0.06f));
            var btnActiveFill = new Color(0.12f, 0.50f, 0.85f, 0.95f);

            m_TexPanelBg = MakeBorderedTex(12, 12, panelFill, panelBorder, 1);
            m_TexSectionBg = MakeBorderedTex(12, 12, sectionFill, sectionBorder, 1);
            m_TexBtnBg = MakeBorderedTex(12, 12, btnFill, btnBorder, 1);
            m_TexBtnBgHover = MakeBorderedTex(12, 12, btnHoverFill, btnBorder, 1);
            m_TexBtnBgActive = MakeBorderedTex(12, 12, btnActiveFill, new Color(0.12f, 0.50f, 0.85f, 0.95f), 1);

            m_TexBtnDangerBg = MakeBorderedTex(12, 12, new Color(0.35f, 0.12f, 0.12f, 0.90f), new Color(1f, 1f, 1f, 0.12f), 1);
            m_TexBtnDangerBgHover = MakeBorderedTex(12, 12, new Color(0.45f, 0.15f, 0.15f, 0.92f), new Color(1f, 1f, 1f, 0.12f), 1);
            m_TexBtnDangerBgActive = MakeBorderedTex(12, 12, new Color(0.65f, 0.18f, 0.18f, 0.96f), new Color(1f, 1f, 1f, 0.14f), 1);

            m_TexBtnPrimaryBg = MakeBorderedTex(12, 12, new Color(0.10f, 0.40f, 0.70f, 0.88f), new Color(1f, 1f, 1f, 0.14f), 1);
            m_TexBtnPrimaryBgHover = MakeBorderedTex(12, 12, new Color(0.12f, 0.50f, 0.85f, 0.92f), new Color(1f, 1f, 1f, 0.16f), 1);
            m_TexBtnPrimaryBgActive = MakeTex(new Color(0.18f, 0.62f, 0.95f, 0.96f));

            m_TexBtnCheckboxBg = MakeBorderedTex(12, 12, new Color(0.22f, 0.24f, 0.29f, 0.84f), new Color(0.70f, 0.74f, 0.80f, 0.22f), 1);
            m_TexBtnCheckboxBgHover = MakeBorderedTex(12, 12, new Color(0.27f, 0.30f, 0.36f, 0.90f), new Color(0.70f, 0.74f, 0.80f, 0.22f), 1);
            m_TexBtnCheckboxBgActive = MakeBorderedTex(12, 12, new Color(0.15f, 0.50f, 0.25f, 0.95f), new Color(0.30f, 0.85f, 0.45f, 0.40f), 1);

            m_TexWindowBorder = MakeTex(new Color(0.20f, 0.22f, 0.26f, borderAlpha));
            m_TexWindowBorderActive = MakeTex(new Color(0.12f, 0.50f, 0.85f, 0.88f));
            m_TexInfoCardBg = MakeBorderedTex(12, 12, new Color(0.12f, 0.14f, 0.18f, 0.82f), new Color(0.60f, 0.75f, 0.95f, 0.12f), 1);
            m_TexFpsBadgeBg = MakeTex(new Color(0.10f, 0.11f, 0.13f, 0.90f));
            m_TexFpsBadgeOuterBg = MakeBorderedTex(12, 12, new Color(0f, 0f, 0f, 0f), new Color(0.12f, 0.50f, 0.85f, 0.92f), 2);
            var texTransparent = MakeTex(new Color(0f, 0f, 0f, 0f));

            m_StyleWindowBorder = new GUIStyle(GUI.skin.box);
            m_StyleWindowBorder.normal.background = m_TexWindowBorder;
            m_StyleWindowBorder.normal.textColor = Color.white;
            m_StyleWindowBorder.padding = new RectOffset(0, 0, 0, 0);
            m_StyleWindowBorder.margin = new RectOffset(0, 0, 0, 0);
            m_StyleWindowBorder.border = new RectOffset(1, 1, 1, 1);

            m_StyleWindow = new GUIStyle(GUI.skin.window);
            m_StyleWindow.normal.background = texTransparent;
            m_StyleWindow.hover.background = texTransparent;
            m_StyleWindow.active.background = texTransparent;
            m_StyleWindow.focused.background = texTransparent;
            m_StyleWindow.onNormal.background = texTransparent;
            m_StyleWindow.onHover.background = texTransparent;
            m_StyleWindow.onActive.background = texTransparent;
            m_StyleWindow.onFocused.background = texTransparent;
            m_StyleWindow.padding = new RectOffset(6, 6, 30, 6);
            m_StyleWindow.margin = new RectOffset(0, 0, 0, 0);
            m_StyleWindow.border = new RectOffset(0, 0, 0, 0);

            m_StylePanel = new GUIStyle(GUI.skin.box);
            m_StylePanel.normal.background = m_TexPanelBg;
            m_StylePanel.normal.textColor = Color.white;
            m_StylePanel.padding = new RectOffset(8, 8, 8, 8);
            m_StylePanel.margin = new RectOffset(4, 4, 4, 4);

            m_StyleSection = new GUIStyle(GUI.skin.box);
            m_StyleSection.normal.background = m_TexSectionBg;
            m_StyleSection.normal.textColor = Color.white;
            m_StyleSection.padding = new RectOffset(8, 8, 6, 6);
            m_StyleSection.margin = new RectOffset(0, 0, 4, 4);

            m_StyleHeader = new GUIStyle(GUI.skin.label);
            m_StyleHeader.fontStyle = FontStyle.Bold;
            m_StyleHeader.normal.textColor = Color.white;
            m_StyleHeader.alignment = TextAnchor.MiddleLeft;
            m_StyleHeader.wordWrap = false;

            m_StyleSubHeader = new GUIStyle(GUI.skin.label);
            m_StyleSubHeader.fontStyle = FontStyle.Bold;
            m_StyleSubHeader.normal.textColor = new Color(0.85f, 0.88f, 0.92f, 1f);
            m_StyleSubHeader.alignment = TextAnchor.MiddleLeft;

            m_StyleButton = new GUIStyle(GUI.skin.button);
            m_StyleButton.normal.background = m_TexBtnBg;
            m_StyleButton.hover.background = m_TexBtnBgHover;
            m_StyleButton.active.background = m_TexBtnBgActive;
            m_StyleButton.onNormal.background = m_TexBtnBgActive;
            m_StyleButton.onHover.background = m_TexBtnBgActive;
            m_StyleButton.onActive.background = m_TexBtnBgActive;
            m_StyleButton.onFocused.background = m_TexBtnBgActive;
            m_StyleButton.normal.textColor = Color.white;
            m_StyleButton.hover.textColor = Color.white;
            m_StyleButton.active.textColor = Color.white;
            m_StyleButton.onNormal.textColor = Color.white;
            m_StyleButton.onHover.textColor = Color.white;
            m_StyleButton.onActive.textColor = Color.white;
            m_StyleButton.onFocused.textColor = Color.white;
            m_StyleButton.fontStyle = FontStyle.Bold;
            m_StyleButton.padding = new RectOffset(6, 6, 4, 4);

            m_StyleButtonSmall = new GUIStyle(m_StyleButton);
            m_StyleButtonSmall.fontStyle = FontStyle.Bold;
            m_StyleButtonSmall.padding = new RectOffset(4, 4, 2, 2);

            m_StyleButtonDanger = new GUIStyle(m_StyleButton);
            m_StyleButtonDanger.normal.background = m_TexBtnDangerBg;
            m_StyleButtonDanger.hover.background = m_TexBtnDangerBgHover;
            m_StyleButtonDanger.active.background = m_TexBtnDangerBgActive;
            m_StyleButtonDanger.onNormal.background = m_TexBtnDangerBgActive;
            m_StyleButtonDanger.onHover.background = m_TexBtnDangerBgActive;
            m_StyleButtonDanger.onActive.background = m_TexBtnDangerBgActive;
            m_StyleButtonDanger.onFocused.background = m_TexBtnDangerBgActive;

            m_StyleButtonPrimary = new GUIStyle(m_StyleButton);
            m_StyleButtonPrimary.normal.background = m_TexBtnPrimaryBg;
            m_StyleButtonPrimary.hover.background = m_TexBtnPrimaryBgHover;
            m_StyleButtonPrimary.active.background = m_TexBtnPrimaryBgActive;
            m_StyleButtonPrimary.onNormal.background = m_TexBtnPrimaryBgActive;
            m_StyleButtonPrimary.onHover.background = m_TexBtnPrimaryBgActive;
            m_StyleButtonPrimary.onActive.background = m_TexBtnPrimaryBgActive;
            m_StyleButtonPrimary.onFocused.background = m_TexBtnPrimaryBgActive;

            m_StyleButtonCheckbox = new GUIStyle(m_StyleButton);
            m_StyleButtonCheckbox.normal.background = m_TexBtnCheckboxBg;
            m_StyleButtonCheckbox.hover.background = m_TexBtnCheckboxBgHover;
            m_StyleButtonCheckbox.active.background = m_TexBtnCheckboxBgHover;
            m_StyleButtonCheckbox.onNormal.background = m_TexBtnCheckboxBgActive;
            m_StyleButtonCheckbox.onHover.background = m_TexBtnCheckboxBgActive;
            m_StyleButtonCheckbox.onActive.background = m_TexBtnCheckboxBgActive;
            m_StyleButtonCheckbox.onFocused.background = m_TexBtnCheckboxBgActive;
            m_StyleButtonCheckbox.padding = new RectOffset(4, 4, 4, 4);

            m_StyleToggle = new GUIStyle(GUI.skin.toggle);
            m_StyleToggle.normal.textColor = new Color(0.92f, 0.94f, 0.96f, 1f);
            m_StyleToggle.hover.textColor = Color.white;
            m_StyleToggle.active.textColor = Color.white;
            m_StyleToggle.focused.textColor = Color.white;
            m_StyleToggle.alignment = TextAnchor.MiddleLeft;
            m_StyleToggle.wordWrap = false;
            m_StyleToggle.clipping = TextClipping.Clip;
            m_StyleToggle.padding = new RectOffset(62, 0, 4, 4);
            m_StyleToggle.margin = new RectOffset(0, 0, 0, 0);
            m_StyleToggle.contentOffset = new Vector2(0f, 0f);
            m_StyleToggle.fontSize = 14;

            m_StyleInfoIcon = new GUIStyle(GUI.skin.button);
            m_StyleInfoIcon.normal.background = texTransparent;
            m_StyleInfoIcon.hover.background = MakeBorderedTex(12, 12, new Color(0.27f, 0.30f, 0.36f, 0.70f), new Color(0.70f, 0.74f, 0.80f, 0.18f), 1);
            m_StyleInfoIcon.active.background = MakeBorderedTex(12, 12, new Color(0.12f, 0.50f, 0.85f, 0.80f), new Color(0.12f, 0.50f, 0.85f, 0.80f), 1);
            m_StyleInfoIcon.normal.textColor = new Color(0.65f, 0.85f, 1f, 1f);
            m_StyleInfoIcon.hover.textColor = Color.white;
            m_StyleInfoIcon.active.textColor = Color.white;
            m_StyleInfoIcon.fontStyle = FontStyle.Bold;
            m_StyleInfoIcon.padding = new RectOffset(0, 0, 0, 0);
            m_StyleInfoIcon.margin = new RectOffset(0, 0, 0, 0);
            m_StyleInfoIcon.alignment = TextAnchor.MiddleCenter;

            m_StyleInfoCard = new GUIStyle(GUI.skin.box);
            m_StyleInfoCard.normal.background = m_TexInfoCardBg;
            m_StyleInfoCard.normal.textColor = Color.white;
            m_StyleInfoCard.padding = new RectOffset(10, 10, 8, 10);
            m_StyleInfoCard.margin = new RectOffset(0, 0, 6, 2);

            m_StyleInfoCardTitle = new GUIStyle(GUI.skin.label);
            m_StyleInfoCardTitle.fontStyle = FontStyle.Bold;
            m_StyleInfoCardTitle.normal.textColor = Color.white;
            m_StyleInfoCardTitle.wordWrap = true;

            m_StyleInfoCardText = new GUIStyle(GUI.skin.label);
            m_StyleInfoCardText.normal.textColor = new Color(0.90f, 0.93f, 0.97f, 1f);
            m_StyleInfoCardText.wordWrap = true;

            m_StyleInfoClose = new GUIStyle(GUI.skin.button);
            m_StyleInfoClose.normal.background = texTransparent;
            m_StyleInfoClose.hover.background = texTransparent;
            m_StyleInfoClose.active.background = texTransparent;
            m_StyleInfoClose.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            m_StyleInfoClose.hover.textColor = Color.white;
            m_StyleInfoClose.active.textColor = Color.white;
            m_StyleInfoClose.fontStyle = FontStyle.Bold;
            m_StyleInfoClose.padding = new RectOffset(0, 0, 0, 0);
            m_StyleInfoClose.margin = new RectOffset(0, 0, 0, 0);
            m_StyleInfoClose.alignment = TextAnchor.MiddleCenter;

            m_StyleFpsBadge = new GUIStyle(GUI.skin.box);
            m_StyleFpsBadge.normal.background = m_TexFpsBadgeBg;
            m_StyleFpsBadge.normal.textColor = Color.white;
            m_StyleFpsBadge.fontStyle = FontStyle.Bold;
            m_StyleFpsBadge.alignment = TextAnchor.MiddleCenter;
            m_StyleFpsBadge.padding = new RectOffset(8, 8, 2, 2);
            m_StyleFpsBadge.margin = new RectOffset(0, 0, 0, 0);

            m_StyleFpsBadgeOuter = new GUIStyle(GUI.skin.box);
            m_StyleFpsBadgeOuter.normal.background = m_TexFpsBadgeOuterBg;
            m_StyleFpsBadgeOuter.normal.textColor = Color.clear;
            m_StyleFpsBadgeOuter.padding = new RectOffset(0, 0, 0, 0);
            m_StyleFpsBadgeOuter.margin = new RectOffset(0, 0, 0, 0);

            if (m_TitleTagStyle == null)
            {
                m_TitleTagStyle = new GUIStyle(GUI.skin.label);
                m_TitleTagStyle.normal.textColor = Color.white;
                m_TitleTagStyle.hover.textColor = Color.white;
                m_TitleTagStyle.active.textColor = Color.white;
                m_TitleTagStyle.focused.textColor = Color.white;
                m_TitleTagStyle.alignment = TextAnchor.MiddleLeft;
                m_TitleTagStyle.fontStyle = FontStyle.Bold;
                m_TitleTagStyle.font = GUI.skin.window.font;
                m_TitleTagStyle.fontSize = GUI.skin.window.fontSize;
                m_TitleTagStyle.wordWrap = false;
                m_TitleTagStyle.padding = new RectOffset(0, 0, 0, 0);
            }

            if (m_TitleBarLabelStyle == null)
            {
                m_TitleBarLabelStyle = new GUIStyle(GUI.skin.label);
                m_TitleBarLabelStyle.font = GUI.skin.window.font;
                m_TitleBarLabelStyle.fontSize = GUI.skin.window.fontSize;
                m_TitleBarLabelStyle.fontStyle = GUI.skin.window.fontStyle;
                m_TitleBarLabelStyle.normal.textColor = Color.white;
                m_TitleBarLabelStyle.hover.textColor = Color.white;
                m_TitleBarLabelStyle.active.textColor = Color.white;
                m_TitleBarLabelStyle.focused.textColor = Color.white;
                m_TitleBarLabelStyle.alignment = TextAnchor.MiddleLeft;
                m_TitleBarLabelStyle.wordWrap = false;
                m_TitleBarLabelStyle.clipping = TextClipping.Clip;
                m_TitleBarLabelStyle.padding = new RectOffset(0, 0, 0, 0);
            }

            if (m_DragHintStyle == null)
            {
                m_DragHintStyle = new GUIStyle(m_TitleBarLabelStyle);
                m_DragHintStyle.alignment = TextAnchor.MiddleCenter;
            }

            m_StylesInited = true;
        }

        static string cacheDir;
        public static string GetCacheDir()
        {
            if (string.IsNullOrEmpty(cacheDir))
            {
                cacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/VPB_cache";
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }
            }
            return cacheDir;
        }
        static string abCacheDir;
        public static string GetAssetBundleCacheDir()
        {
            if (string.IsNullOrEmpty(abCacheDir))
            {
                abCacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/VPB_cache/ab";
                if (!Directory.Exists(abCacheDir))
                {
                    Directory.CreateDirectory(abCacheDir);
                }
            }
            return abCacheDir;
        }
        void Awake()
        {
            singleton = this;

            // Initialize Gallery
            gameObject.AddComponent<Gallery>();

            LogUtil.SetLogSource(Logger);

            LogUtil.MarkPluginAwake();

            VdsLauncher.ParseOnce();

            try
            {
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
            }
            catch
            {
            }

            try
            {
                var appType = typeof(Application);
                var stackTraceLogTypeType = appType.Assembly.GetType("UnityEngine.StackTraceLogType");
                var setMethod = appType.GetMethod(
                    "SetStackTraceLogType",
                    new Type[] { typeof(LogType), stackTraceLogTypeType }
                );
                if (setMethod != null && stackTraceLogTypeType != null)
                {
                    var noneValue = Enum.Parse(stackTraceLogTypeType, "None");
                    setMethod.Invoke(null, new object[] { LogType.Log, noneValue });
                    setMethod.Invoke(null, new object[] { LogType.Warning, noneValue });
                    setMethod.Invoke(null, new object[] { LogType.Error, noneValue });
                    setMethod.Invoke(null, new object[] { LogType.Exception, noneValue });
                    setMethod.Invoke(null, new object[] { LogType.Assert, noneValue });
                }
            }
            catch
            {
            }

            Settings.Init(this.Config);

            try
            {
                LogUtil.ConfigureCleanLog(
                    Settings.Instance != null && Settings.Instance.CleanLogEnabled != null && Settings.Instance.CleanLogEnabled.Value,
                    Settings.Instance != null && Settings.Instance.CleanLogPath != null ? Settings.Instance.CleanLogPath.Value : null
                );
            }
            catch
            {
            }

            UIKey = KeyUtil.Parse(Settings.Instance.UIKey.Value);
            GalleryKey = KeyUtil.Parse(Settings.Instance.GalleryKey.Value);
            CreateGalleryKey = KeyUtil.Parse(Settings.Instance.CreateGalleryKey.Value);
            HubKey = KeyUtil.Parse(Settings.Instance.HubKey.Value);
            m_UIScale = Settings.Instance.UIScale.Value;
            UIPosition = Settings.Instance.UIPosition.Value;
            MiniMode = Settings.Instance.MiniMode.Value;

            m_Rect = new Rect(UIPosition.x, UIPosition.y, 220, 50);
            if (MiniMode)
            {
                m_Rect.height = MiniModeHeight;
            }
            m_ExpandedHeight = Mathf.Max(m_Rect.height, MiniModeHeight);

            this.Config.SaveOnConfigSet = true;
            Debug.Log("var browser hook start");
            var harmony = new Harmony("VPB_hook");
            // Patch VaM/Harmony hook points.
            UnityEngineHook.Init();
            SuperControllerHook.PatchOptional(harmony);
            harmony.PatchAll(typeof(AtomHook));
            harmony.PatchAll(typeof(HubResourcePackageHook));
            harmony.PatchAll(typeof(SuperControllerHook));
            harmony.PatchAll(typeof(PatchAssetLoader));
            harmony.PatchAll(typeof(UnityEngineHook));

            // Initialize Native Hooks (MinHook)
            Native.NativeHookManager.Initialize();
        }

        private void SetMiniMode(bool enabled)
        {
            if (MiniMode == enabled)
            {
                return;
            }

            // Preserve the expanded height so we can restore it when leaving mini mode.
            if (!MiniMode)
            {
                m_ExpandedHeight = Mathf.Max(m_Rect.height, MiniModeHeight);
            }

            MiniMode = enabled;
            Settings.Instance.MiniMode.Value = MiniMode;

            if (MiniMode)
            {
                m_Rect.height = MiniModeHeight;
            }
            else
            {
                // Restore previous expanded height.
                m_Rect.height = Mathf.Max(m_ExpandedHeight, MiniModeHeight);
            }

            RestrictUiRect();
        }
        void Start()
        {
            var go = new GameObject("VPB_messager");
            var messager = go.AddComponent<Messager>();
            messager.target = this.gameObject;
            go.AddComponent<CustomAssetLoader>();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (!Directory.Exists("AllPackages"))
            {
                Directory.CreateDirectory("AllPackages");
            }
            MVR.FileManagement.FileManager.RegisterInternalSecureWritePath("AllPackages");


        }
        void OnDestroy()
        {
            Settings.Instance.UIPosition.Value = new Vector2((int)m_Rect.x, (int)m_Rect.y);
            Settings.Instance.MiniMode.Value = MiniMode;

            this.Config.Save();
            
            // Cleanup QuickMenu Button
            if (SuperController.singleton.mainHUD != null)
            {
                var existing = SuperController.singleton.mainHUD.Find("VPB_QuickMenuButton_Canvas");
                if (existing != null) Destroy(existing.gameObject);
            }
            if (m_QuickMenuCanvas != null)
            {
                 Destroy(m_QuickMenuCanvas.gameObject);
            }

            // Shutdown Native Hooks
            Native.NativeHookManager.Shutdown();
        }
        // Called on (hard) restart as well.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogUtil.LogWarning("OnSceneLoaded " + scene.name + " " + mode.ToString());
            if (mode == LoadSceneMode.Single)
            {
                m_Inited = false;
                m_FileManagerInited = false;
                m_UIInited = false;
            }
        }
        void OnEnable()
        {
            MessageKit<string>.addObserver(MessageDef.UpdateLoading, OnProgress);
            MessageKit.addObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);

        }
        void OnDisable()
        {
            MessageKit<string>.removeObserver(MessageDef.UpdateLoading, OnProgress);
            MessageKit.removeObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
        }

        string m_ProgressText = "";
        float m_FpsSmoothedDelta = 0f;
        float m_FpsUpdateTimer = 0f;
        string m_FpsText = "";
        void OnProgress(string text)
        {
            m_ProgressText = text;
        }
        void OnDeactivateWorldUI()
        {
            if (m_FileBrowser != null)
            {
                m_FileBrowser.Hide();
            }
            if (m_HubBrowse != null)
            {
                m_HubBrowse.Hide();
            }
        }
        static bool m_Show = true; // Made static so it can be toggled via external message calls.
        void Update()
        {
            UnityEngineHook.Update();
            VdsLauncher.TryExecuteOnce();
            float unscaledDt = Time.unscaledDeltaTime;
            if (LogUtil.IsSceneLoadActive())
            {
                LogUtil.SceneLoadFrameTick(unscaledDt);
                LogUtil.SceneLoadUpdate();
            }
            if (LogUtil.IsSceneClickActive())
            {
                LogUtil.SceneClickUpdate();
            }

            if (!m_UIInited || !m_FileManagerInited)
            {
                m_FpsSmoothedDelta = 0f;
                m_FpsUpdateTimer = 0f;
                m_FpsText = "";
            }
            else
            {
                if (unscaledDt > 0f)
                {
                    if (m_FpsSmoothedDelta <= 0f)
                        m_FpsSmoothedDelta = unscaledDt;
                    else
                        m_FpsSmoothedDelta = Mathf.Lerp(m_FpsSmoothedDelta, unscaledDt, 0.08f);

                    m_FpsUpdateTimer += unscaledDt;
                    if (m_FpsUpdateTimer >= 1.0f)
                    {
                        m_FpsUpdateTimer = 0f;
                        float fps = 1f / Mathf.Max(0.00001f, m_FpsSmoothedDelta);
                        m_FpsText = string.Format("{0:0} FPS", fps);
                    }
                }
            }

            if (UIKey.TestKeyDown())
            {
                m_Show = !m_Show;
            }
            // Hotkeys
            if (m_Inited)
            {
                if (CreateGalleryKey.TestKeyDown())
                {
                    if (Gallery.singleton != null)
                    {
                        if (!m_GalleryCatsInited) InitGalleryCategories();
                        Gallery.singleton.CreatePane();
                    }
                }
                if (GalleryKey.TestKeyDown())
                {
                    if (Gallery.singleton != null && Gallery.singleton.IsVisible)
                        Gallery.singleton.Hide();
                    else
                        OpenGallery();
                }
            }

            if (m_Inited && m_FileManagerInited)
            {
                if (HubKey.TestKeyDown())
                {
                    OpenHubBrowse();
                }
            }

            if (!m_Inited)
            {
                Init();
                m_Inited = true;
            }
            if (!m_UIInited)
            {
                if (MVR.Hub.HubBrowse.singleton != null && m_FileManagerInited)
                {
                    CreateHubBrowse();
                    CreateFileBrowser();
                    m_UIInited = true;
                    LogUtil.LogReadyOnce("UI initialized");
                }
            }

            if (!m_QuickMenuButtonInited)
            {
                CreateQuickMenuButton();
            }
            else if (m_ShowHideButtonGO != null && Gallery.singleton != null)
            {
                int count = Gallery.singleton.PanelCount;
                bool shouldShow = count > 0;
                if (m_ShowHideButtonGO.activeSelf != shouldShow)
                {
                    m_ShowHideButtonGO.SetActive(shouldShow);
                }
                
                if (shouldShow && m_ShowHideButton != null)
                {
                    m_ShowHideButton.label = "Show/Hide (" + count + ")";
                }
            }
        }


        bool AutoInstalled = false;
        // On entering the game, process AutoInstall packages once.
        void TryAutoInstall()
        {
            if (AutoInstalled) return;
            bool flag = false;
            AutoInstalled = true;
            foreach (var item in FileEntry.AutoInstallLookup)
            {
                var pkg = FileManager.GetPackage(item);
                if (pkg != null)
                {
                    bool dirty = pkg.InstallSelf();
                    if (dirty) flag = true;
                }
            }
            if (flag)
            {
                MVR.FileManagement.FileManager.Refresh();
                VPB.FileManager.Refresh();
            }
        }

        bool m_Inited = false;
        bool m_UIInited = false;
        bool m_QuickMenuButtonInited = false;
        void Init()
        {
            m_Inited = true;

            if (m_FileManager == null)
            {
                var child = Tools.AddChild(this.gameObject);
                m_FileManager = child.AddComponent<FileManager>();
                child.AddComponent<VPB.CustomImageLoaderThreaded>();
                child.AddComponent<VPB.ImageLoadingMgr>();
                child.AddComponent<VPB.Gallery>();
                FileManager.RegisterRefreshHandler(() =>
                {
                    m_FileManagerInited = true;
                    TryAutoInstall();
                    VarPackageMgr.singleton.Refresh();
                });
            }

            VarPackageMgr.singleton.Init();
            FileManager.Refresh(true);
        }
        void CreateFileBrowser()
        {
            if (m_FileBrowser == null)
            {
                var go = SuperController.singleton.fileBrowserWorldUI.gameObject;
                GameObject newgo = Instantiate(go);
                newgo.transform.SetParent(go.transform.parent, false);
                newgo.SetActive(true);

                var browser = newgo.GetComponent<uFileBrowser.FileBrowser>();
                m_FileBrowser = newgo.AddComponent<FileBrowser>();
                m_FileBrowser.InitUI(browser);
                Component.DestroyImmediate(browser);

                PoolManager mgr = newgo.AddComponent<PoolManager>();
                mgr.root = m_FileBrowser.fileContent;
            }
        }

        bool m_FileManagerInited = false;
        HubBrowse m_HubBrowse;
        FileManager m_FileManager;
        FileBrowser m_FileBrowser;
	
        void CreateHubBrowse()
        {
            LogUtil.LogWarning("var browser CreateHubBrowse");
            if (m_HubBrowse == null)
            {

                var child = Tools.AddChild(this.gameObject);
                child.name = "VarBrowser_HubBrowse";
                m_HubBrowse = child.AddComponent<HubBrowse>();

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.itemPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceItemUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceItemUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.itemPrefab = newInst;
                }

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.resourceDetailPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceItemDetailUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceItemDetailUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.resourceDetailPrefab = newInst;
                }

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.packageDownloadPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourcePackageUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourcePackageUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.packageDownloadPrefab = newInst;
                }
                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.creatorSupportButtonPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceCreatorSupportUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceCreatorSupportUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);
                    m_HubBrowse.creatorSupportButtonPrefab = newInst;
                }
            }

            Transform tf = Tools.GetChild(SuperController.singleton.transform, "HubBrowsePanel");

            GameObject newgo = Instantiate(tf.gameObject);
            newgo.transform.SetParent(tf.parent, false);

            newgo.SetActive(true);

            m_HubBrowse.SetUI(newgo.transform);
            m_HubBrowse.InitUI();
            m_HubBrowse.HubEnabled = true;
            m_HubBrowse.WebBrowserEnabled = true;
            // Close button

            var close = Tools.GetChild(newgo.transform, "CloseButton");
            if (close != null)
            {
                var closeButton = close.GetComponent<Button>();
                //var closeButton = newgo.transform.Find("LeftBar/CloseButton").GetComponent<Button>();
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() =>
                {
                    m_HubBrowse.Hide();
                });
            }
            // Hide the built-in package manager button
            var openPackageButton = Tools.GetChild(newgo.transform, "OpenPackageManager");
            //var openPackageButton = newgo.transform.Find("LeftBar/OpenPackageManager").GetComponent<Button>();
            if (openPackageButton != null)
                openPackageButton.gameObject.SetActive(false);
            else
            {
                LogUtil.LogError("HubBrowse no OpenPackageManager");
            }
            newgo.SetActive(false);
        }

        Canvas m_QuickMenuCanvas;
        GameObject m_ShowHideButtonGO;
        UIDynamicButton m_ShowHideButton;

        void CreateQuickMenuButton()
        {
            try
            {
                if (SuperController.singleton == null || SuperController.singleton.mainHUD == null) return;
                
                var existing = SuperController.singleton.mainHUD.Find("VPB_QuickMenuButton_Canvas");
                if (existing != null) 
                {
                    // Destroy old version if found to ensure update
                    DestroyImmediate(existing.gameObject);
                }

                if (m_MVRPluginManager == null)
                {
                    var mgrGO = SuperController.singleton.transform.Find("ScenePluginManager");
                    if (mgrGO != null) m_MVRPluginManager = mgrGO.GetComponent<MVRPluginManager>();
                }

                if (m_MVRPluginManager == null) return;
                
                if (SuperController.singleton == null || SuperController.singleton.mainHUD == null) return;
                if (m_MVRPluginManager.configurableButtonPrefab == null) return;

                GameObject canvasObject = new GameObject("VPB_QuickMenuButton_Canvas");
                m_QuickMenuCanvas = canvasObject.AddComponent<Canvas>();
                if (m_QuickMenuCanvas == null) return;
                
                m_QuickMenuCanvas.renderMode = RenderMode.WorldSpace;
                m_QuickMenuCanvas.pixelPerfect = false;

                if (SuperController.singleton != null && SuperController.singleton.mainHUD != null && SuperController.singleton.mainHUD.gameObject != null)
                    canvasObject.layer = SuperController.singleton.mainHUD.gameObject.layer;
                
                if (SuperController.singleton == null || SuperController.singleton.mainHUD == null) return;    
                m_QuickMenuCanvas.transform.SetParent(SuperController.singleton.mainHUD, false);
                SuperController.singleton.AddCanvas(m_QuickMenuCanvas);

                CanvasScaler cs = canvasObject.AddComponent<CanvasScaler>();
                if (cs != null)
                {
                    cs.scaleFactor = 100.0f;
                    cs.dynamicPixelsPerUnit = 1f;
                }
                GraphicRaycaster gr = canvasObject.AddComponent<GraphicRaycaster>();

                bool isVR = false;
                try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

                float s = 0.001f;
                m_QuickMenuCanvas.transform.localScale = new Vector3(s, s, s);

                if (isVR)
                {
                    m_QuickMenuCanvas.transform.localPosition = new Vector3(0f, 0f, 0f);
                    m_QuickMenuCanvas.transform.localEulerAngles = new Vector3(32, 180, 0);
                }
                else
                {
                    // Position at Left side
                    m_QuickMenuCanvas.transform.localPosition = new Vector3(0f, -0.012f, 0f);
                    m_QuickMenuCanvas.transform.localEulerAngles = new Vector3(0, 180, 0);
                }

                // Button 1: Create Gallery (Left)
                Transform btnTr = Instantiate(m_MVRPluginManager.configurableButtonPrefab);
                if (btnTr != null && m_QuickMenuCanvas.transform != null)
                {
                    btnTr.SetParent(m_QuickMenuCanvas.transform, false);
                    
                    RectTransform rt = btnTr.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(120f, 50f);
                        rt.anchoredPosition = new Vector2(-120f, 0f);
                    }

                    UIDynamicButton uiBtn = btnTr.GetComponent<UIDynamicButton>();
                    if (uiBtn != null)
                    {
                        uiBtn.label = "Create Gallery";
                        if (uiBtn.buttonText != null) uiBtn.buttonText.fontSize = 24;
                        if (uiBtn.button != null)
                        {
                            uiBtn.button.onClick.AddListener(() => {
                                 if (Gallery.singleton != null)
                                 {
                                     if (!m_GalleryCatsInited) InitGalleryCategories();
                                     Gallery.singleton.CreatePane();
                                 }
                            });
                        }
                        
                        // Use HoverHandler for dynamic transparency
                        var hover = uiBtn.gameObject.AddComponent<ButtonHoverHandler>();
                        hover.targetButton = uiBtn;
                        uiBtn.buttonColor = new Color(1f, 1f, 1f, 0.5f);
                    }
                }

                // Button 2: Show/Hide (Right)
                Transform btnTr2 = Instantiate(m_MVRPluginManager.configurableButtonPrefab);
                if (btnTr2 != null && m_QuickMenuCanvas.transform != null)
                {
                    m_ShowHideButtonGO = btnTr2.gameObject;
                    btnTr2.SetParent(m_QuickMenuCanvas.transform, false);
                    
                    RectTransform rt = btnTr2.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(120f, 50f);
                        rt.anchoredPosition = new Vector2(120f, 0f);
                    }

                    UIDynamicButton uiBtn = btnTr2.GetComponent<UIDynamicButton>();
                    if (uiBtn != null)
                    {
                        m_ShowHideButton = uiBtn;
                        uiBtn.label = "Show/Hide";
                        if (uiBtn.buttonText != null) uiBtn.buttonText.fontSize = 24;
                        if (uiBtn.button != null)
                        {
                            uiBtn.button.onClick.AddListener(() => {
                                if (Gallery.singleton != null)
                                {
                                    if (Gallery.singleton.IsVisible)
                                        Gallery.singleton.Hide();
                                    else
                                        OpenGallery();
                                }
                            });
                        }
                        
                        // Use HoverHandler for dynamic transparency
                        var hover = uiBtn.gameObject.AddComponent<ButtonHoverHandler>();
                        hover.targetButton = uiBtn;
                        uiBtn.buttonColor = new Color(1f, 1f, 1f, 0.5f);
                    }
                    m_ShowHideButtonGO.SetActive(false);
                }
                
                m_QuickMenuButtonInited = true;
                LogUtil.Log("QuickMenuButton created. VR: " + isVR);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("Error creating QuickMenuButton: " + ex.ToString());
            }
        }

        void DragWnd(int windowsid)
        {
            EnsureStyles();
            
            // Re-apply alpha for window content
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, m_WindowAlphaState);
            GUI.contentColor = new Color(GUI.contentColor.r, GUI.contentColor.g, GUI.contentColor.b, m_WindowAlphaState);
            GUI.backgroundColor = new Color(GUI.backgroundColor.r, GUI.backgroundColor.g, GUI.backgroundColor.b, m_WindowAlphaState);

            float dragHeight = MiniMode ? 26f : 48f;

            if (m_StyleWindow != null)
            {
                m_StyleWindow.padding.left = 6;
                m_StyleWindow.padding.right = 6;
                m_StyleWindow.padding.bottom = 6;
            }
            if (m_StylePanel != null)
            {
                m_StylePanel.padding.left = 8;
                m_StylePanel.padding.right = 8;
                m_StylePanel.padding.top = 8;
                m_StylePanel.padding.bottom = 8;
                m_StylePanel.margin.left = 4;
                m_StylePanel.margin.right = 4;
                m_StylePanel.margin.top = 4;
                m_StylePanel.margin.bottom = 4;
            }

            GUI.DragWindow(new Rect(0, 0, m_Rect.width, dragHeight));

            GUILayout.BeginVertical(m_StylePanel);

            // ========== HEADER & CONTROLS ==========
            GUILayout.BeginHorizontal();
            
            // Generate alpha hex for the green color
            int alphaInt = Mathf.RoundToInt(m_WindowAlphaState * 255);
            string alphaHex = alphaInt.ToString("X2");
            // Use a less bright green (LimeGreen #32CD32 approx) instead of pure #00FF00
            GUILayout.Label(string.Format("<color=#32CD32{0}><b>{1}</b></color> {2}", alphaHex, FileManager.s_InstalledCount, m_ProgressText), m_StyleHeader);
            
            GUILayout.FlexibleSpace();
            const float buttonHeight = 22f;
            if (GUILayout.Button("+", m_StyleButtonSmall, GUILayout.Width(28), GUILayout.Height(buttonHeight)))
            {
		        m_UIScale = Mathf.Clamp(m_UIScale + 0.2f, MinUiScale, MaxUiScale);
                Settings.Instance.UIScale.Value = m_UIScale;
                RestrictUiRect();
            }
            if (GUILayout.Button("-", m_StyleButtonSmall, GUILayout.Width(28), GUILayout.Height(buttonHeight)))
            {
		        m_UIScale = Mathf.Clamp(m_UIScale - 0.2f, MinUiScale, MaxUiScale);
                Settings.Instance.UIScale.Value = m_UIScale;
                RestrictUiRect();
            }

            if (GUILayout.Button(MiniMode ? "▼" : "▲", m_StyleButtonSmall, GUILayout.Width(28), GUILayout.Height(buttonHeight)))
            {
                SetMiniMode(!MiniMode);
            }
            if (GUILayout.Button("...", m_StyleButtonSmall, GUILayout.Width(28), GUILayout.Height(buttonHeight)))
            {
                OpenSettings();
            }
            GUILayout.EndHorizontal();

            if (MiniMode)
            {
                // ========== MINI MODE: QUICK ACCESS ==========
                DrawPhiSplitButtons("Hub", m_StyleButton, OpenHubBrowse, "Gallery", m_StyleButton, OpenGallery, 1.618f, buttonHeight);

                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(3);

            if (m_ShowSettings)
            {
                DrawSettingsPage(buttonHeight);
                GUILayout.EndVertical();
                return;
            }

            if (m_FileManagerInited && m_UIInited)
            {
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                    GUI.enabled = false;

                {
                    const float infoBtnWidth = 28f;
                    const float optionIndent = 12f;

                    // ========== TEXTURE OPTIMIZATION SETTINGS ==========
                    GUILayout.BeginVertical(m_StyleSection);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Settings.Instance.ReduceTextureSize.Value ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
                    {
                        Settings.Instance.ReduceTextureSize.Value = !Settings.Instance.ReduceTextureSize.Value;
                    }
                    GUILayout.Label("Textures: Downscale");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
                    {
						ToggleInfoCard(ref m_ShowDownscaleTexturesInfo);
                    }
                    GUILayout.EndHorizontal();
					DrawInfoCard(ref m_ShowDownscaleTexturesInfo, "Downscale Textures", () =>
					{
						GUILayout.Space(4);
						GUILayout.Label("When this is ON, VPB makes big textures smaller and saves them so VaM can reuse them.", m_StyleInfoCardText);
						GUILayout.Space(2);
						GUILayout.Label("This can lower memory use and help performance. The tradeoff is textures may look a bit less sharp.", m_StyleInfoCardText);
						GUILayout.Space(6);
						GUILayout.Label("Min: Anything bigger than this gets reduced down to this size.", m_StyleInfoCardText);
						GUILayout.Label("Force all to minimum: Makes almost everything use the minimum size (stronger effect).", m_StyleInfoCardText);
						GUILayout.Space(6);
						GUILayout.Label("Notes: Smaller textures won't be made bigger. The first time can take longer while VPB builds the cache.", m_StyleInfoCardText);
					});

                    if (Settings.Instance.ReduceTextureSize.Value)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(optionIndent);
                        GUILayout.Label("Min", GUILayout.Width(30));
                        int minTextureSize = Settings.Instance.MinTextureSize.Value;
                        var style2k = (minTextureSize == 2048) ? m_StyleButtonPrimary : m_StyleButton;
                        var style4k = (minTextureSize == 4096) ? m_StyleButtonPrimary : m_StyleButton;
                        var style8k = (minTextureSize == 8192) ? m_StyleButtonPrimary : m_StyleButton;
                        if (GUILayout.Button("2K", style2k, GUILayout.Width(44), GUILayout.Height(buttonHeight)))
                        {
                            Settings.Instance.MinTextureSize.Value = 2048;
                        }
                        if (GUILayout.Button("4K", style4k, GUILayout.Width(44), GUILayout.Height(buttonHeight)))
                        {
                            Settings.Instance.MinTextureSize.Value = 4096;
                        }
                        if (GUILayout.Button("8K", style8k, GUILayout.Width(44), GUILayout.Height(buttonHeight)))
                        {
                            Settings.Instance.MinTextureSize.Value = 8192;
                        }
                        if (Settings.Instance.MaxTextureSize != null)
                        {
                            Settings.Instance.MaxTextureSize.Value = Settings.Instance.MinTextureSize.Value;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(optionIndent);
                        if (GUILayout.Button(Settings.Instance.ForceTextureToMinSize.Value ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
                        {
                            Settings.Instance.ForceTextureToMinSize.Value = !Settings.Instance.ForceTextureToMinSize.Value;
                        }
                        GUILayout.Label("Force all to minimum size");
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();

                    // ========== MAINTENANCE & CACHE TOOLS ==========
                    GUILayout.BeginVertical(m_StyleSection);
                    {
                        var fullRowRect = GUILayoutUtility.GetRect(0f, buttonHeight, GUILayout.ExpandWidth(true));
                        const float rowGutter = 6f;
                        float infoWidth = infoBtnWidth;

                        var infoRect = new Rect(fullRowRect.xMax - infoWidth, fullRowRect.y, infoWidth, fullRowRect.height);
                        var buttonsRect = new Rect(fullRowRect.x, fullRowRect.y, Mathf.Max(0f, fullRowRect.width - infoWidth - rowGutter), fullRowRect.height);

                        DrawPhiSplitButtonsInRect(
                            buttonsRect,
                            "GC",
                            m_StyleButton,
                            () =>
                            {
                                DAZMorphMgr.singleton.cache.Clear();
                                ImageLoadingMgr.singleton.ClearCache();

                                GC.Collect();
                                Resources.UnloadUnusedAssets();
                            },
                            "Refresh",
                            m_StyleButton,
                            Refresh,
                            1.618f
                        );

                        if (GUI.Button(infoRect, "i", m_StyleButtonSmall ?? GUI.skin.button))
                        {
                            ToggleInfoCard(ref m_ShowGcRefreshInfo);
                        }
                    }

                    DrawInfoCard(ref m_ShowGcRefreshInfo, "GC & Refresh", () =>
                    {
                        GUILayout.Space(4);
                        GUILayout.Label("Refresh updates the package list so VPB shows what is currently on disk (new/moved/removed files).", m_StyleInfoCardText);
                        GUILayout.Space(2);
                        GUILayout.Label("GC tries to free memory after heavy browsing by clearing caches and asking Unity/.NET to clean up.", m_StyleInfoCardText);
                    });

                    // ========== REMOVE OLD/DAMAGED ==========
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove Old/Damaged", m_StyleButton, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        OpenRemoveWindow();
                    }
                    if (GUILayout.Button("i", m_StyleButton, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
                    {
						ToggleInfoCard(ref m_ShowRemoveOldDamagedInfo);
                    }
                    GUILayout.EndHorizontal();
					DrawInfoCard(ref m_ShowRemoveOldDamagedInfo, "Remove Old/Damaged", () =>
					{
						GUILayout.Space(4);
						GUILayout.Label("Scan for invalid vars (duplicates, invalid names) and old versions.", m_StyleInfoCardText);
						GUILayout.Space(2);
						GUILayout.Label("Opens a window to review and confirm removal.", m_StyleInfoCardText);
					});

                    // ========== UNLOAD ALL PACKAGES ==========
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Unload All", m_StyleButton, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        UninstallAll();
                    }
                    if (GUILayout.Button("i", m_StyleButton, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
                    {
						ToggleInfoCard(ref m_ShowUninstallAllInfo);
                    }
                    GUILayout.EndHorizontal();
					DrawInfoCard(ref m_ShowUninstallAllInfo, "Unload All", () =>
					{
						GUILayout.Space(2);
						GUILayout.Label("Moves almost all add-on packages out of the active folder so VaM stops loading them.", m_StyleInfoCardText);
						GUILayout.Space(1);
						GUILayout.Label("Nothing is deleted. Files are just moved (AutoInstall items stay).", m_StyleInfoCardText);
					});

                    // ========== HUB BROWSE ==========
                    DrawPhiSplitButtons("Hub", m_StyleButton, OpenHubBrowse, "Gallery", m_StyleButton, OpenGallery, 1.618f, buttonHeight);



                    GUILayout.EndVertical();
                }
                GUI.enabled = true;
            }

            GUILayout.EndVertical();
        }
        void ShowFileBrowser(string title, string fileFormat, string path, bool inGame = false, bool selectOnClick = true)
        {
            SuperController.singleton.ActivateWorldUI();
            // Hide Hub Browse while the file browser is open.
            m_HubBrowse.Hide();

            m_FileBrowser.Hide();

            m_FileBrowser.SetTextEntry(false);
            m_FileBrowser.keepOpen = true;
            m_FileBrowser.hideExtension = true;
            m_FileBrowser.SetTitle("<color=green>" + title + "</color>");
            m_FileBrowser.selectOnClick = selectOnClick;

            m_FileBrowser.Show(fileFormat, path, LoadFromSceneWorldDialog, true, inGame);

            // Refresh favorite and AutoInstall state.
            MessageKit.post(MessageDef.FileManagerRefresh);
        }
        void OnGUI()
        {
            if (!m_Show)
                return;
            var pre = GUI.matrix;
            // Apply UI scaling by scaling the entire GUI matrix.
            GUI.matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, new Vector3(m_UIScale, m_UIScale, 1));

            if (m_Inited)
            {
                bool show = true;
                // Hide this window while the preview/file browser UI is open.
                if ((m_FileBrowser != null && m_FileBrowser.window.activeSelf))
                {
                    show = false;
                }
                if (show)
                {
                    RestrictUiRect();

                    EnsureStyles();
                    m_StyleWindow.padding.top = MiniMode ? 28 : 50;
                    
                    var windowRect = m_Rect;
                    windowRect.height = 0f;

                    // Draw the window itself (background + controls)
                    // We let GUILayout handle the layout, but the border is drawn manually below.
                    // IMPORTANT: We use a separate border rect to avoid double-drawing borders if the skin has them.
                    
                    var prevAlphaColor = GUI.color;
                    var prevAlphaContentColor = GUI.contentColor;
                    var prevAlphaBackgroundColor = GUI.backgroundColor;

                    // Hover check for transparency
                    // We calculate screen space coordinates to robustly detect hover regardless of GUI matrix
                    // We prefer m_RealWindowRect (from Repaint) if available, to avoid layout-phase transient sizes.
                    Rect checkRect = (m_RealWindowRect.width > 10f) ? m_RealWindowRect : m_Rect;

                    float hoverMargin = 40f; // Invisible detection border in pixels
                    Rect screenRect = new Rect(
                        (checkRect.x * m_UIScale) - hoverMargin,
                        (checkRect.y * m_UIScale) - hoverMargin,
                        (checkRect.width * m_UIScale) + (hoverMargin * 2),
                        (checkRect.height * m_UIScale) + (hoverMargin * 2)
                    );
                    
                    Vector2 screenMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                    bool isHovering = screenRect.Contains(screenMousePos);

                    float transparencyValue = (Settings.Instance != null && Settings.Instance.UiTransparencyValue != null) ? Settings.Instance.UiTransparencyValue.Value : 0.5f;

                    if (isHovering)
                    {
                        m_WindowAlphaState = 1.0f;
                    }
                    else
                    {
                        m_WindowAlphaState = 1.0f - transparencyValue;
                    }

                    GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, m_WindowAlphaState);
                    GUI.contentColor = new Color(GUI.contentColor.r, GUI.contentColor.g, GUI.contentColor.b, m_WindowAlphaState);
                    GUI.backgroundColor = new Color(GUI.backgroundColor.r, GUI.backgroundColor.g, GUI.backgroundColor.b, m_WindowAlphaState);

                    m_Rect = GUILayout.Window(0, windowRect, DragWnd, "", m_StyleWindow);

                    // Draw our custom border ON TOP or AROUND the window rect
                    var borderRect = new Rect(m_Rect.x, m_Rect.y, m_Rect.width, m_Rect.height);

                    if (Event.current.type == EventType.MouseDown)
                        m_WindowActive = borderRect.Contains(Event.current.mousePosition);

                    if (Event.current.type == EventType.Repaint)
                    {
                        m_StyleWindowBorder.normal.background = m_WindowActive ? m_TexWindowBorderActive : m_TexWindowBorder;
                        var prevDepth = GUI.depth;
                        GUI.depth = 1; 
                        // Draw just the frame/border using a style that renders only the border
                        GUI.Box(borderRect, GUIContent.none, m_StyleWindowBorder);
                        GUI.depth = prevDepth;
                    }

                    // Restore opacity so subsequent elements (FPS, sub-windows) are not affected
                    GUI.color = prevAlphaColor;
                    GUI.contentColor = prevAlphaContentColor;
                    GUI.backgroundColor = prevAlphaBackgroundColor;

                    RestrictUiRect();

                    var prevGuiColor = GUI.color;
                    var prevContentColor = GUI.contentColor;
                    var prevBackgroundColor = GUI.backgroundColor;
                    var prevEnabled = GUI.enabled;

                    const float headerInsetY = 4f;
                    const float headerHeight = 24f;
                    float headerRow1Y = m_Rect.y + headerInsetY;
                    float headerRow2Y = headerRow1Y + headerHeight;

                    const float titleRightPadding = 6f;
                    var fpsText = m_FpsText;
                    float fpsWidth = 0f;
                    if (!string.IsNullOrEmpty(fpsText) && m_StyleFpsBadge != null)
                    {
                        fpsWidth = m_StyleFpsBadge.CalcSize(new GUIContent(fpsText)).x + 8f;
                        fpsWidth = Mathf.Max(fpsWidth, 50f);
                    }

                    var rightEdge = m_Rect.xMax - titleRightPadding;
                    var fpsRect = new Rect(rightEdge - fpsWidth, headerRow1Y, fpsWidth, headerHeight);

                    var hintRect = new Rect(m_Rect.x + 6f, headerRow2Y, Mathf.Max(0f, m_Rect.width - 12f), headerHeight);

                    bool isRepaint = (Event.current.type == EventType.Repaint);

                    if (isRepaint)
                    {
                        GUI.color = new Color(1f, 1f, 1f, m_WindowAlphaState);
                        GUI.backgroundColor = new Color(1f, 1f, 1f, m_WindowAlphaState);
                        GUI.contentColor = new Color(1f, 1f, 1f, m_WindowAlphaState);
                        GUI.enabled = true;

                        var startupSeconds = LogUtil.GetStartupSecondsForDisplay();
                        var sceneClickSeconds = LogUtil.GetSceneClickSecondsForDisplay();
                        var sceneLoadSeconds = LogUtil.GetSceneLoadSecondsForDisplay();
                        string tagText;
                        if (sceneLoadSeconds.HasValue)
                        {
                            // Prefer showing load time; keep text compact to avoid truncation in small headers.
                            tagText = string.Format("vpb {0} | {1:0.0}s | {2:0.0}s", PluginVersionInfo.Version, startupSeconds, sceneLoadSeconds.Value);
                        }
                        else if (sceneClickSeconds.HasValue)
                        {
                            tagText = string.Format("vpb {0} | {1:0.0}s | {2:0.0}s", PluginVersionInfo.Version, startupSeconds, sceneClickSeconds.Value);
                        }
                        else
                        {
                            tagText = string.Format("vpb {0} | {1:0.0}s", PluginVersionInfo.Version, startupSeconds);
                        }
                        var tagContent = new GUIContent(tagText);
                        float desiredTagWidth = m_TitleTagStyle != null ? m_TitleTagStyle.CalcSize(tagContent).x : 100f;
                        float availableTagWidth = Mathf.Max(0f, m_Rect.width - 6f - titleRightPadding - fpsWidth);
                        float tagWidth = Mathf.Min(desiredTagWidth, availableTagWidth);
                        var tagRect = new Rect(m_Rect.x + 6f, headerRow1Y, tagWidth, headerHeight);
                        GUI.color = new Color(1f, 1f, 1f, m_WindowAlphaState);
                        GUI.contentColor = new Color(1f, 1f, 1f, m_WindowAlphaState);
                        if (m_TitleTagStyle != null)
                        {
                            GUI.Label(tagRect, tagText, m_TitleTagStyle);
                        }

                        if (!string.IsNullOrEmpty(fpsText) && fpsRect.width > 4f && m_StyleFpsBadgeOuter != null && m_StyleFpsBadge != null)
                        {
                            const float badgeInsetY = 2f;
                            var outerRect = new Rect(
                                fpsRect.x,
                                fpsRect.y + badgeInsetY,
                                fpsRect.width,
                                Mathf.Max(0f, fpsRect.height - (badgeInsetY * 2f))
                            );
                            var innerRect = new Rect(
                                outerRect.x + 2f,
                                outerRect.y + 1f,
                                Mathf.Max(0f, outerRect.width - 4f),
                                Mathf.Max(0f, outerRect.height - 2f)
                            );

                            GUI.Box(outerRect, GUIContent.none, m_StyleFpsBadgeOuter);
                            float fpsAlpha = Mathf.Max(m_WindowAlphaState, 0.5f); // Ensure FPS is at least 50% visible
                            
                            // To ensure visibility, we might need to boost the alpha of the contentColor specifically
                            // since GUI.color affects both background and content.
                            
                            var prevC = GUI.color;
                            var prevCC = GUI.contentColor;
                            var prevBC = GUI.backgroundColor;

                            // We want the text to be relatively opaque (0.5 to 1.0) even if global GUI.color is low.
                            // But GUI.color multiplies everything.
                            // If m_WindowAlphaState is 0.1, GUI.color is (1,1,1, 0.1).
                            // If we set GUI.color to (1,1,1, 0.5), the box background will be 0.5.
                            // The text color comes from style.normal.textColor (white) * GUI.contentColor * GUI.color.
                            
                            GUI.color = new Color(1f, 1f, 1f, fpsAlpha);
                            GUI.contentColor = Color.white; 
                            GUI.backgroundColor = Color.white;
                            
                            GUI.Box(innerRect, fpsText, m_StyleFpsBadge);
                            
                            GUI.color = prevC;
                            GUI.contentColor = prevCC;
                            GUI.backgroundColor = prevBC;
                        }

                        if (!MiniMode && m_DragHintStyle != null)
                        {
                            GUI.color = new Color(1f, 1f, 1f, m_WindowAlphaState); // Ensure text is transparent
                            double totalLoadSeconds = startupSeconds + (sceneClickSeconds.HasValue ? sceneClickSeconds.Value : 0.0);
                            var dragText = string.Format("{0:0.0}s | Dragable Area | Toggle: {1}", totalLoadSeconds, UIKey.keyPattern);
                            var drawText = dragText;
                            var maxTitleWidth = hintRect.width;

                            if (m_TitleBarLabelStyle != null)
                            {
                                var textSize = m_TitleBarLabelStyle.CalcSize(new GUIContent(drawText));
                                if (textSize.x > maxTitleWidth)
                                {
                                    const string ellipsis = "...";
                                    drawText = dragText;
                                    while (drawText.Length > 0 && m_TitleBarLabelStyle.CalcSize(new GUIContent(drawText + ellipsis)).x > maxTitleWidth)
                                    {
                                        drawText = drawText.Substring(0, drawText.Length - 1);
                                    }
                                    drawText = (drawText.Length > 0) ? (drawText + ellipsis) : ellipsis;
                                }
                            }

                            GUI.Label(hintRect, drawText, m_DragHintStyle);
                        }
                    }

                    GUI.color = prevGuiColor;
                    GUI.contentColor = prevContentColor;
                    GUI.backgroundColor = prevBackgroundColor;
                    GUI.enabled = prevEnabled;

                    if (m_ShowUnloadWindow)
                    {
                        m_UnloadWindowRect = GUILayout.Window(1, m_UnloadWindowRect, DrawUnloadWindow, "Unload Packages", m_StyleWindow);
                        GUI.BringWindowToFront(1);
                    }
                    if (m_ShowRemoveWindow)
                    {
                        m_RemoveWindowRect = GUILayout.Window(2, m_RemoveWindowRect, DrawRemoveWindow, "Remove Old/Damaged", m_StyleWindow);
                        GUI.BringWindowToFront(2);
                    }
                }
            }
            else
            {
                GUI.Box(new Rect(0, 0, 200, 30), "var browser is waiting to start");
            }

            if (Event.current.type == EventType.Repaint)
            {
                m_RealWindowRect = m_Rect;
            }

            GUI.matrix = pre;
        }

        void RestrictUiRect()
        {
            const float minX = 0f;
            const float minY = 4f;
            var maxX = Mathf.Max(minX, ((float)Screen.width / m_UIScale) - m_Rect.width);
            var maxY = Mathf.Max(minY, ((float)Screen.height / m_UIScale) - m_Rect.height);
            m_Rect.x = Mathf.Clamp(m_Rect.x, minX, maxX);
            m_Rect.y = Mathf.Clamp(m_Rect.y, minY, maxY);
        }
        // Callback invoked after clicking an item in the preview/file browser UI.
        protected void LoadFromSceneWorldDialog(string saveName)
        {
            LogUtil.LogWarning("LoadFromSceneWorldDialog " + saveName);

            MethodInfo loadInternalMethod = typeof(SuperController).GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            loadInternalMethod.Invoke(SuperController.singleton, new object[3] { saveName, false, false });

            // Hide UI while loading a scene.
            if (m_FileBrowser != null)
            {
                m_FileBrowser.Hide();
            }
            if (m_HubBrowse != null)
            {
                m_HubBrowse.Hide();
            }
        }

        MVRPluginManager m_MVRPluginManager;
        public void InitDynamicPrefab()
        {
            m_MVRPluginManager = SuperController.singleton.transform.Find("ScenePluginManager").GetComponent<MVRPluginManager>();
            //m_MVRPluginManager.configurableFilterablePopupPrefab

        }

        string DeterminePackageType(string uid)
        {
             VarPackage pkg = FileManager.GetPackage(uid);
             if (pkg == null) return "Unknown";
             
             // Check content
             if (pkg.ClothingFileEntryNames != null && pkg.ClothingFileEntryNames.Count > 0) return "Clothing";
             if (pkg.HairFileEntryNames != null && pkg.HairFileEntryNames.Count > 0) return "Hair";
             
             // Check file entries for other types
             if (pkg.FileEntries != null)
             {
                 foreach(var entry in pkg.FileEntries)
                 {
                     string f = entry.InternalPath;
                     if (f.StartsWith("Saves/scene", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "Scene";
                     if (f.StartsWith("Custom/Scripts", StringComparison.OrdinalIgnoreCase)) return "Script";
                     if (f.StartsWith("Custom/Atom/Person", StringComparison.OrdinalIgnoreCase)) return "Person";
                 }
             }
             
             return "Other";
        }


        void DrawUnloadWindow(int windowID)
        {
            // Block game input from scroll wheel immediately, before UI elements consume the event
            if (Event.current.type == EventType.ScrollWheel)
            {
                Input.ResetInputAxes();
            }

            // Focus check for text field
            if (Event.current.type == EventType.KeyDown)
            {
                if (GUI.GetNameOfFocusedControl() == "UnloadFilter")
                {
                    // Consume input to prevent hotkeys/world interaction
                    // But allow normal typing
                }
            }

            GUILayout.BeginVertical(m_StylePanel);
            
            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Unload Packages", m_StyleHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", m_StyleButtonSmall, GUILayout.Width(30)))
            {
                m_ShowUnloadWindow = false;
            }
            GUILayout.EndHorizontal();

            // Filter
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(50));
            GUI.SetNextControlName("UnloadFilter");
            m_UnloadFilter = GUILayout.TextField(m_UnloadFilter);
            if (GUILayout.Button("Clear", m_StyleButtonSmall, GUILayout.Width(50)))
            {
                m_UnloadFilter = "";
                GUI.FocusControl("");
            }
            GUILayout.EndHorizontal();
            
            m_ExcludeActivePackages = GUILayout.Toggle(m_ExcludeActivePackages, "Exclude Active Packages");
            
            GUILayout.Space(5);

            // List
            GUILayout.BeginVertical(m_StyleSection);
            
            // Table Headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(25)); // Checkbox spacer
            GUILayout.Label("Category", m_StyleInfoCardTitle, GUILayout.Width(80));
            GUILayout.Label("Package Name", m_StyleInfoCardTitle, GUILayout.Width(200));
            GUILayout.Label("Folder Path", m_StyleInfoCardTitle);
            GUILayout.EndHorizontal();

            m_UnloadScroll = GUILayout.BeginScrollView(m_UnloadScroll);
            

            for (int i = 0; i < m_UnloadList.Count; i++)
            {
                var item = m_UnloadList[i];
                if (m_ExcludeActivePackages && item.IsActive) continue;

                if (!string.IsNullOrEmpty(m_UnloadFilter) && 
                    !item.Uid.ToLower().Contains(m_UnloadFilter.ToLower()) && 
                    !item.Type.ToLower().Contains(m_UnloadFilter.ToLower()))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                item.Checked = GUILayout.Toggle(item.Checked, "", GUILayout.Width(25));
                GUILayout.Label(item.Type, GUILayout.Width(80));
                GUILayout.Label(item.Uid, GUILayout.Width(200));
                
                // Show directory only, excluding filename
                string dirDisplay = Path.GetDirectoryName(item.Path);
                GUILayout.Label(dirDisplay);
                
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(5);
            
            // Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", m_StyleButton))
            {
                foreach(var item in m_UnloadList)
                {
                    if (m_ExcludeActivePackages && item.IsActive) continue;
                    item.Checked = true;
                }
            }
            if (GUILayout.Button("Select None", m_StyleButton))
            {
                foreach(var item in m_UnloadList) item.Checked = false;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Confirm Unload Selected", m_StyleButtonPrimary))
            {
                PerformUnload();
                m_ShowUnloadWindow = false;
            }
            GUILayout.EndHorizontal();
            
            // Resize handle
            var resizeRect = new Rect(m_UnloadWindowRect.width - 30, m_UnloadWindowRect.height - 30, 30, 30);
            GUI.Box(new Rect(m_UnloadWindowRect.width - 20, m_UnloadWindowRect.height - 20, 20, 20), "◢", m_StyleInfoIcon);

            int resizeControlID = GUIUtility.GetControlID(FocusType.Passive);
            switch (Event.current.GetTypeForControl(resizeControlID))
            {
                case EventType.MouseDown:
                    if (resizeRect.Contains(Event.current.mousePosition))
                    {
                        GUIUtility.hotControl = resizeControlID;
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        GUIUtility.hotControl = 0;
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        m_UnloadWindowRect.width += Event.current.delta.x;
                        m_UnloadWindowRect.height += Event.current.delta.y;
                        m_UnloadWindowRect.width = Mathf.Max(m_UnloadWindowRect.width, 300);
                        m_UnloadWindowRect.height = Mathf.Max(m_UnloadWindowRect.height, 200);
                        Event.current.Use();
                    }
                    break;
            }

            // Consume ScrollWheel event if inside window to prevent game scrolling
            if (Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }

            GUILayout.EndVertical();
            
            // Drag window (excluding resize handle area to avoid conflict)
            if (Event.current.type != EventType.MouseDrag || !resizeRect.Contains(Event.current.mousePosition))
            {
                 GUI.DragWindow();
            }
        }

        void PerformUnload()
        {
             foreach (var item in m_UnloadList)
             {
                 if (m_ExcludeActivePackages && item.IsActive) continue;

                 if (item.Checked)
                 {
                    string targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(targetPath)) continue;
                    try {
                        File.Move(item.Path, targetPath);
                    } catch (Exception ex) {
                        LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message);
                    }
                 }
             }
             Refresh();
             RemoveEmptyFolder("AddonPackages");
        }

        void OpenRemoveWindow()
        {
            m_RemoveList.Clear();
            var items = FileManager.GetCleanupList(true);
            foreach (var item in items)
            {
                m_RemoveList.Add(new RemoveItem
                {
                    Path = item.Path,
                    Uid = item.Uid,
                    Type = item.Type,
                    Checked = true
                });
            }
            m_ShowRemoveWindow = true;
        }

        void PerformRemove()
        {
            foreach (var item in m_RemoveList)
            {
                if (item.Checked)
                {
                    FileManager.RemoveToInvalid(item.Path, item.Type);
                }
            }
            Refresh();
        }

        void DrawRemoveWindow(int windowID)
        {
            if (Event.current.type == EventType.ScrollWheel)
            {
                Input.ResetInputAxes();
            }

            if (Event.current.type == EventType.KeyDown)
            {
                if (GUI.GetNameOfFocusedControl() == "RemoveFilter")
                {
                }
            }

            GUILayout.BeginVertical(m_StylePanel);

            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Remove Old/Damaged", m_StyleHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", m_StyleButtonSmall, GUILayout.Width(30)))
            {
                m_ShowRemoveWindow = false;
            }
            GUILayout.EndHorizontal();

            // Filter
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(50));
            GUI.SetNextControlName("RemoveFilter");
            m_RemoveFilter = GUILayout.TextField(m_RemoveFilter);
            if (GUILayout.Button("Clear", m_StyleButtonSmall, GUILayout.Width(50)))
            {
                m_RemoveFilter = "";
                GUI.FocusControl("");
            }
            GUILayout.EndHorizontal();

            m_ExcludeOld = GUILayout.Toggle(m_ExcludeOld, "Exclude Old");

            GUILayout.Space(5);

            // List
            GUILayout.BeginVertical(m_StyleSection);

            // Table Headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(25));
            GUILayout.Label("Type", m_StyleInfoCardTitle, GUILayout.Width(80));
            GUILayout.Label("Package Name", m_StyleInfoCardTitle, GUILayout.Width(200));
            GUILayout.Label("Path/Info", m_StyleInfoCardTitle);
            GUILayout.EndHorizontal();

            m_RemoveScroll = GUILayout.BeginScrollView(m_RemoveScroll);


            for (int i = 0; i < m_RemoveList.Count; i++)
            {
                var item = m_RemoveList[i];
                if (m_ExcludeOld && item.Type == "OldVersion") continue;

                if (!string.IsNullOrEmpty(m_RemoveFilter) &&
                    !item.Uid.ToLower().Contains(m_RemoveFilter.ToLower()) &&
                    !item.Type.ToLower().Contains(m_RemoveFilter.ToLower()))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                item.Checked = GUILayout.Toggle(item.Checked, "", GUILayout.Width(25));
                GUILayout.Label(item.Type, GUILayout.Width(80));
                GUILayout.Label(item.Uid, GUILayout.Width(200));
                GUILayout.Label(Path.GetDirectoryName(item.Path));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(5);

            // Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", m_StyleButton))
            {
                foreach (var item in m_RemoveList)
                {
                    if (m_ExcludeOld && item.Type == "OldVersion") continue;
                    item.Checked = true;
                }
            }
            if (GUILayout.Button("Select None", m_StyleButton))
            {
                foreach (var item in m_RemoveList) item.Checked = false;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove Selected", m_StyleButtonDanger))
            {
                PerformRemove();
                m_ShowRemoveWindow = false;
            }
            GUILayout.EndHorizontal();

            // Resize handle
            var resizeRect = new Rect(m_RemoveWindowRect.width - 30, m_RemoveWindowRect.height - 30, 30, 30);
            GUI.Box(new Rect(m_RemoveWindowRect.width - 20, m_RemoveWindowRect.height - 20, 20, 20), "◢", m_StyleInfoIcon);

            int resizeControlID = GUIUtility.GetControlID(FocusType.Passive);
            switch (Event.current.GetTypeForControl(resizeControlID))
            {
                case EventType.MouseDown:
                    if (resizeRect.Contains(Event.current.mousePosition))
                    {
                        GUIUtility.hotControl = resizeControlID;
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        GUIUtility.hotControl = 0;
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        m_RemoveWindowRect.width += Event.current.delta.x;
                        m_RemoveWindowRect.height += Event.current.delta.y;
                        m_RemoveWindowRect.width = Mathf.Max(m_RemoveWindowRect.width, 300);
                        m_RemoveWindowRect.height = Mathf.Max(m_RemoveWindowRect.height, 200);
                        Event.current.Use();
                    }
                    break;
            }

            // Consume ScrollWheel event if inside window to prevent game scrolling
            if (Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }

            GUILayout.EndVertical();

            // Drag window (excluding resize handle area to avoid conflict)
            if (Event.current.type != EventType.MouseDrag || !resizeRect.Contains(Event.current.mousePosition))
            {
                GUI.DragWindow();
            }
        }
        
        public class ButtonHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public UIDynamicButton targetButton;
            private Color normalColor = new Color(1f, 1f, 1f, 0.5f);
            private Color hoverColor = new Color(1f, 1f, 1f, 0.8f);

            void Start()
            {
                if (targetButton != null)
                {
                    targetButton.buttonColor = normalColor;
                }
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (targetButton != null)
                {
                    targetButton.buttonColor = hoverColor;
                }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (targetButton != null)
                {
                    targetButton.buttonColor = normalColor;
                }
            }
        }
    }
}
