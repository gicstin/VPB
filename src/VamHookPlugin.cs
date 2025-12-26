using BepInEx;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using Prime31.MessageKit;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace var_browser
{
    // Plugin metadata attribute: plugin ID, plugin name, plugin version (must be numeric)
    [BepInPlugin("vam_var_browser", "var_browser", "0.15")]
    public partial class VamHookPlugin : BaseUnityPlugin // Inherits BaseUnityPlugin
    {
        private KeyUtil UIKey;
        private KeyUtil CustomSceneKey;
        private KeyUtil CategorySceneKey;
        private Vector2 UIPosition;
        private bool MiniMode;
        private bool m_ShowDownscaleTexturesInfo;
        private bool m_ShowPrioritizeFaceTexturesInfo;
        private bool m_ShowRemoveInvalidVarsInfo;
        private bool m_ShowRemoveOldVersionInfo;
        private bool m_ShowUninstallAllInfo;
        private bool m_ShowGcRefreshInfo;
        private bool m_ShowSettings;
        private string m_SettingsUiKeyDraft;
        private bool m_SettingsPrioritizeFaceTexturesDraft;
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

        public static VamHookPlugin singleton;

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
            m_ShowRemoveInvalidVarsInfo = false;
            m_ShowRemoveOldVersionInfo = false;
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
            m_SettingsPrioritizeFaceTexturesDraft = (Settings.Instance != null && Settings.Instance.PrioritizeFaceTextures != null) ? Settings.Instance.PrioritizeFaceTextures.Value : true;
            m_SettingsError = null;
        }

        private void CloseSettings()
        {
            m_ShowSettings = false;
            m_SettingsError = null;
        }

        private void DrawSettingsPage(float buttonHeight)
        {
            GUILayout.BeginVertical(m_StyleSection);
            GUILayout.Label("Settings", m_StyleHeader);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Show/Hide Hotkey", GUILayout.Width(120));
            m_SettingsUiKeyDraft = GUILayout.TextField(m_SettingsUiKeyDraft ?? "", GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight));
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

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
                    if (Settings.Instance != null && Settings.Instance.UIKey != null)
                    {
                        Settings.Instance.UIKey.Value = parsed.keyPattern;
                    }
                    if (Settings.Instance != null && Settings.Instance.PrioritizeFaceTextures != null)
                    {
                        Settings.Instance.PrioritizeFaceTextures.Value = m_SettingsPrioritizeFaceTexturesDraft;
                    }
                    UIKey = parsed;
                    CloseSettings();
                }
                catch
                {
                    m_SettingsError = "Invalid hotkey. Example: Ctrl+Shift+V";
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
            m_StylePanel.margin = new RectOffset(6, 6, 6, 6);

            m_StyleSection = new GUIStyle(GUI.skin.box);
            m_StyleSection.normal.background = m_TexSectionBg;
            m_StyleSection.normal.textColor = Color.white;
            m_StyleSection.padding = new RectOffset(8, 8, 6, 6);
            m_StyleSection.margin = new RectOffset(0, 0, 6, 6);

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
            m_StyleButton.padding = new RectOffset(10, 10, 7, 7);

            m_StyleButtonSmall = new GUIStyle(m_StyleButton);
            m_StyleButtonSmall.fontStyle = FontStyle.Bold;
            m_StyleButtonSmall.padding = new RectOffset(8, 8, 4, 4);

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
            m_StyleButtonCheckbox.padding = new RectOffset(8, 8, 6, 6);

            m_StyleToggle = new GUIStyle(GUI.skin.toggle);
            m_StyleToggle.normal.textColor = new Color(0.92f, 0.94f, 0.96f, 1f);
            m_StyleToggle.hover.textColor = Color.white;
            m_StyleToggle.active.textColor = Color.white;
            m_StyleToggle.focused.textColor = Color.white;
            m_StyleToggle.alignment = TextAnchor.MiddleLeft;
            m_StyleToggle.wordWrap = false;
            m_StyleToggle.clipping = TextClipping.Clip;
            m_StyleToggle.padding = new RectOffset(62, 0, 6, 6);
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
                cacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/var_browser_cache";
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
                abCacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/var_browser_cache/ab";
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

            LogUtil.MarkPluginAwake();

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
            UIKey = KeyUtil.Parse(Settings.Instance.UIKey.Value);
            CustomSceneKey = KeyUtil.Parse(Settings.Instance.CustomSceneKey.Value);
            CategorySceneKey = KeyUtil.Parse(Settings.Instance.CategorySceneKey.Value);
            m_UIScale = Settings.Instance.UIScale.Value;
            UIPosition = Settings.Instance.UIPosition.Value;
            MiniMode = Settings.Instance.MiniMode.Value;

            m_Rect = new Rect(UIPosition.x, UIPosition.y, 220, 50);
            if (MiniMode)
            {
                m_Rect.height = MiniModeHeight;
            }
            m_ExpandedHeight = Mathf.Max(m_Rect.height, MiniModeHeight);
            ZipConstants.DefaultCodePage = Settings.Instance.CodePage.Value;


            this.Config.SaveOnConfigSet = false;
            Debug.Log("var browser hook start");
            var harmony = new Harmony("var_browser_hook");
            // Patch VaM/Harmony hook points.
            harmony.PatchAll();
            harmony.PatchAll(typeof(AtomHook));
            harmony.PatchAll(typeof(HubResourcePackageHook));
            harmony.PatchAll(typeof(SuperControllerHook));
            harmony.PatchAll(typeof(PatchAssetLoader));

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
            var go = new GameObject("var_browser_messager");
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
            float unscaledDt = Time.unscaledDeltaTime;
            if (LogUtil.IsSceneLoadActive())
            {
                LogUtil.SceneLoadFrameTick(unscaledDt);
                LogUtil.SceneLoadUpdate();
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
            if (m_Inited && m_FileManagerInited)
            {
                if (CustomSceneKey.TestKeyDown())
                {
                    // Custom entries do not require installation.
                    m_FileBrowser.onlyInstalled = false;
                    ShowFileBrowser("Custom Scene", "json", "Saves/scene", true);
                }
                if (CategorySceneKey.TestKeyDown())
                {
                    ShowFileBrowser("Category Scene", "json", "Saves/scene");
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
                var_browser.FileManager.Refresh();
            }
        }

        bool m_Inited = false;
        bool m_UIInited = false;
        void Init()
        {
            m_Inited = true;

            if (m_FileManager == null)
            {
                var child = Tools.AddChild(this.gameObject);
                m_FileManager = child.AddComponent<FileManager>();
                child.AddComponent<var_browser.CustomImageLoaderThreaded>();
                child.AddComponent<var_browser.ImageLoadingMgr>();
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
        void DragWnd(int windowsid)
        {
            EnsureStyles();

            float dragHeight = MiniMode ? 28f : 52f;

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
                m_StylePanel.margin.left = 6;
                m_StylePanel.margin.right = 6;
                m_StylePanel.margin.top = 6;
                m_StylePanel.margin.bottom = 6;
            }

            GUI.DragWindow(new Rect(0, 0, m_Rect.width, dragHeight));

            GUILayout.BeginVertical(m_StylePanel);

            // ========== HEADER & CONTROLS ==========
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<color=#00FF00><b>{0}</b></color> {1}", FileManager.s_InstalledCount, m_ProgressText), m_StyleHeader);
            GUILayout.FlexibleSpace();
            const float buttonHeight = 28f;
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

            if (GUILayout.Button(MiniMode ? "Full" : "Mini", m_StyleButtonSmall, GUILayout.Width(44), GUILayout.Height(buttonHeight)))
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
                        var style1k = m_StyleButtonDanger;
                        var style2k = (minTextureSize == 2048) ? m_StyleButtonPrimary : m_StyleButton;
                        var style4k = (minTextureSize == 4096) ? m_StyleButtonPrimary : m_StyleButton;
                        var style8k = (minTextureSize == 8192) ? m_StyleButtonPrimary : m_StyleButton;
                        if (GUILayout.Button("1K", style1k, GUILayout.Width(44), GUILayout.Height(buttonHeight)))
                        {
                            Settings.Instance.MinTextureSize.Value = 1024;
                        }
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

                    // ========== REMOVE INVALID VARS ==========
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove Invalid Vars", m_StyleButton, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        RemoveInvalidVars();
                    }
                    if (GUILayout.Button("i", m_StyleButton, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
                    {
						ToggleInfoCard(ref m_ShowRemoveInvalidVarsInfo);
                    }
                    GUILayout.EndHorizontal();
					DrawInfoCard(ref m_ShowRemoveInvalidVarsInfo, "Remove Invalid Vars", () =>
					{
						GUILayout.Space(4);
						GUILayout.Label("Cleans up the browser list so missing or broken items stop showing up.", m_StyleInfoCardText);
						GUILayout.Space(2);
						GUILayout.Label("This does NOT delete your files. It only refreshes the list.", m_StyleInfoCardText);
					});

                    // ========== REMOVE OLD VERSION ==========
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove Old Version", m_StyleButtonDanger, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        RemoveOldVersion();
                    }
                    if (GUILayout.Button("i", m_StyleButtonDanger, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
                    {
						ToggleInfoCard(ref m_ShowRemoveOldVersionInfo);
                    }
                    GUILayout.EndHorizontal();
					DrawInfoCard(ref m_ShowRemoveOldVersionInfo, "Remove Old Version", () =>
					{
						GUILayout.Space(4);
						GUILayout.Label("Helps reduce duplicates by keeping newer versions and removing older ones when possible.", m_StyleInfoCardText);
						GUILayout.Space(2);
						GUILayout.Label("It may change what VaM considers " + "installed" + " because files can be moved/updated during the cleanup.", m_StyleInfoCardText);
					});

                    // ========== UNLOAD ALL PACKAGES ==========
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Unload All", m_StyleButtonPrimary, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        UninstallAll();
                    }
                    if (GUILayout.Button("i", m_StyleButtonPrimary, GUILayout.Width(infoBtnWidth), GUILayout.Height(buttonHeight)))
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
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                {
                    show = false;
                }
                if (show)
                {
                    RestrictUiRect();

                    EnsureStyles();
                    m_StyleWindow.padding.top = MiniMode ? 30 : 54;
                    const float borderPx = 1f;

                    var windowRect = m_Rect;
                    windowRect.height = 0f;

                    m_Rect = GUILayout.Window(0, windowRect, DragWnd, "", m_StyleWindow);

                    var borderRect = new Rect(m_Rect.x - borderPx, m_Rect.y - borderPx, m_Rect.width + (borderPx * 2f), m_Rect.height + (borderPx * 2f));

                    if (Event.current.type == EventType.MouseDown)
                        m_WindowActive = borderRect.Contains(Event.current.mousePosition);

                    if (Event.current.type == EventType.Repaint)
                    {
                        m_StyleWindowBorder.normal.background = m_WindowActive ? m_TexWindowBorderActive : m_TexWindowBorder;
                        var prevDepth = GUI.depth;
                        GUI.depth = 1;
                        GUI.Box(borderRect, GUIContent.none, m_StyleWindowBorder);
                        GUI.depth = prevDepth;
                    }

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
                        fpsWidth = m_StyleFpsBadge.CalcSize(new GUIContent(fpsText)).x + 24f;
                        fpsWidth = Mathf.Max(fpsWidth, 120f);
                    }

                    var rightEdge = m_Rect.xMax - titleRightPadding;
                    var fpsRect = new Rect(rightEdge - fpsWidth, headerRow1Y, fpsWidth, headerHeight);

                    var hintRect = new Rect(m_Rect.x + 6f, headerRow2Y, Mathf.Max(0f, m_Rect.width - 12f), headerHeight);

                    bool isRepaint = (Event.current.type == EventType.Repaint);

                    if (isRepaint)
                    {
                        GUI.color = Color.white;
                        GUI.backgroundColor = Color.white;
                        GUI.contentColor = Color.white;
                        GUI.enabled = true;

                        var startupSeconds = LogUtil.GetStartupSecondsForDisplay();
                        var tagText = string.Format("VPB {0} ({1:0.0}s)", PluginVersionInfo.Version, startupSeconds);
                        var tagContent = new GUIContent(tagText);
                        float desiredTagWidth = m_TitleTagStyle != null ? m_TitleTagStyle.CalcSize(tagContent).x : 100f;
                        float availableTagWidth = Mathf.Max(0f, m_Rect.width - 6f - titleRightPadding - fpsWidth);
                        float tagWidth = Mathf.Min(desiredTagWidth, availableTagWidth);
                        var tagRect = new Rect(m_Rect.x + 6f, headerRow1Y, tagWidth, headerHeight);
                        GUI.color = new Color(1f, 1f, 1f, 1f);
                        GUI.contentColor = new Color(1f, 1f, 1f, 1f);
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
                                outerRect.x + 4f,
                                outerRect.y + 1f,
                                Mathf.Max(0f, outerRect.width - 8f),
                                Mathf.Max(0f, outerRect.height - 2f)
                            );

                            GUI.Box(outerRect, GUIContent.none, m_StyleFpsBadgeOuter);
                            GUI.Box(innerRect, fpsText, m_StyleFpsBadge);
                        }

                        if (!MiniMode && m_DragHintStyle != null)
                        {
                            var dragText = string.Format("Dragable Area | Toggle: {0}", UIKey.keyPattern);
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

                }
            }
            else
            {
                GUI.Box(new Rect(0, 0, 200, 30), "var browser is waiting to start");
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
    }
}
