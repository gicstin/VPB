using BepInEx;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using Prime31.MessageKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZstdNet;
namespace VPB
{
    // Plugin metadata attribute: plugin ID, plugin name, plugin version (must be numeric)
    [BepInPlugin("VPB", "VPB", PluginVersionInfo.Version)]

    public partial class VamHookPlugin : BaseUnityPlugin // Inherits BaseUnityPlugin
    {
        private KeyUtil UIKey;
        private KeyUtil GalleryKey;
        private KeyUtil CreateGalleryKey;
        private KeyUtil HubKey;
        private Vector2 UIPosition;
        private bool MiniMode;
        
        // Package Manager Window
        private class PackageManagerItem
        {
            public string Uid;
            public string Path;
            public string Type;
            public long Size;
            public DateTime LastWriteTime;
            public string AgeString;
            public int DependencyCount;
            public int LoadedDependencyCount;
            public bool Checked;
            public bool IsActive;
            public bool Locked;
            public bool IsLatest;
        }
        private bool m_ShowPackageManagerWindow = false;
        private System.Collections.Generic.List<PackageManagerItem> m_AddonList = new System.Collections.Generic.List<PackageManagerItem>();
        private System.Collections.Generic.List<PackageManagerItem> m_AllList = new System.Collections.Generic.List<PackageManagerItem>();
        private string m_PkgMgrFilter = "";
        private string m_PkgMgrFilterLower = "";
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryInclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> m_PkgMgrCategoryExclusive = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.List<string> m_PkgMgrCategories = new System.Collections.Generic.List<string>();
        private System.Collections.Generic.Dictionary<string, int> m_PkgMgrCategoryCounts = new System.Collections.Generic.Dictionary<string, int>();
        private System.Collections.Generic.HashSet<string> m_LockedPackages = new System.Collections.Generic.HashSet<string>();
        private Vector2 m_AddonScroll = Vector2.zero;
        private Vector2 m_AllScroll = Vector2.zero;
        private Vector2 m_PkgMgrCategoryScroll = Vector2.zero;
        private Rect m_PackageManagerWindowRect = new Rect(100, 100, 1000, 600);
        private string m_PkgMgrSortField = "Name";
        private bool m_PkgMgrSortAscending = true;
        private int m_AddonLastSelectedIndex = -1;
        private int m_AllLastSelectedIndex = -1;
        private bool m_PkgMgrIsDragging = false;
        private bool m_PkgMgrDragChecked = false;
        private int m_PkgMgrAddonCount = 0;
        private int m_PkgMgrAllCount = 0;
        private int m_PkgMgrDragLastIdx = -1;
        
        private System.Collections.Generic.List<int> m_AddonVisibleIndices = new System.Collections.Generic.List<int>();
        private System.Collections.Generic.List<int> m_AllVisibleIndices = new System.Collections.Generic.List<int>();
        private bool m_PkgMgrIndicesDirty = true;
        
        private void RefreshVisibleIndices()
        {
            m_AddonVisibleIndices.Clear();
            for (int i = 0; i < m_AddonList.Count; i++)
                if (IsPackageManagerItemVisible(m_AddonList[i])) m_AddonVisibleIndices.Add(i);

            m_AllVisibleIndices.Clear();
            for (int i = 0; i < m_AllList.Count; i++)
                if (IsPackageManagerItemVisible(m_AllList[i])) m_AllVisibleIndices.Add(i);

            m_PkgMgrIndicesDirty = false;
        }

        // Desktop Context Menu
        private bool m_ShowDesktopContextMenu = false;
        private Rect m_DesktopContextMenuRect = new Rect(0, 0, 250, 100);
        private PackageManagerItem m_ContextMenuTargetItem;
        private string m_PkgMgrStatusMessage = "";
        private float m_PkgMgrStatusTimer = 0f;

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
        
        private bool m_ShowPluginsAlwaysEnabledInfo;
        private bool m_ShowRemoveOldDamagedInfo;
        private bool m_ShowGcRefreshInfo;
        private bool m_PendingGc;
        private bool m_ShowSpaceSaverWindow;
        private string m_AutoOptimizeReport;
        private float m_AutoOptimizeReportTimer;
        private Rect m_SpaceSaverWindowRect = new Rect(100, 100, 650, 200);
        private bool m_DecompressConfirmRequested;
        private bool m_CompressConfirmRequested;
        private long m_CachedTexturesSize;
        private long m_CachedVpbSize;
        private int m_PendingVamCacheCount;
        private bool m_IsCountingCache;
        private float m_CacheCountUpdateTimer;
        private bool m_ShowSettings;
        private string m_SettingsUiKeyDraft;
        private string m_SettingsGalleryKeyDraft;
        private string m_SettingsCreateGalleryKeyDraft;
        private string m_SettingsHubKeyDraft;
        private bool m_SettingsPluginsAlwaysEnabledDraft;
        private bool m_SettingsLoadDependenciesWithPackageDraft;
        private bool m_SettingsIsDevModeDraft;
        private bool m_SettingsEnableUiTransparencyDraft;
        private float m_SettingsUiTransparencyValueDraft;
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
        private GUIStyle m_StyleRow;
        private GUIStyle m_StylePkgMgrRow;
        private GUIStyle m_StylePkgMgrRowCentered;
        private GUIStyle m_StylePkgMgrHeader;
        private GUIStyle m_StyleRowAlternate;
        private GUIStyle m_StyleRowHover;
        private GUIStyle m_StyleHeader;
        private GUIStyle m_StyleSubHeader;
        private GUIStyle m_StyleButton;
        private GUIStyle m_StyleButtonSmall;
        private GUIStyle m_StyleButtonDanger;
        private GUIStyle m_StyleButtonPrimary;
        private GUIStyle m_StyleButtonCheckbox;
        private GUIStyle m_StyleToggle;
        private GUIStyle m_StyleWindow;
        private GUIStyle m_StyleContextMenu;
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

        private Harmony m_Harmony;

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply(false, true);
            return tex;
        }

        private void CloseAllInfoCards()
        {
            m_ShowPluginsAlwaysEnabledInfo = false;
            m_ShowRemoveOldDamagedInfo = false;
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
            m_SettingsPluginsAlwaysEnabledDraft = (Settings.Instance != null && Settings.Instance.PluginsAlwaysEnabled != null) ? Settings.Instance.PluginsAlwaysEnabled.Value : false;
            m_SettingsLoadDependenciesWithPackageDraft = (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null) ? Settings.Instance.LoadDependenciesWithPackage.Value : true;
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
                if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null)
                {
                    if (Settings.Instance.LoadDependenciesWithPackage.Value != m_SettingsLoadDependenciesWithPackageDraft)
                    {
                        Settings.Instance.LoadDependenciesWithPackage.Value = m_SettingsLoadDependenciesWithPackageDraft;
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
            if (GUILayout.Button(m_SettingsLoadDependenciesWithPackageDraft ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
            {
                m_SettingsLoadDependenciesWithPackageDraft = !m_SettingsLoadDependenciesWithPackageDraft;
            }
            GUILayout.Label("Load dependencies when loading a package");
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

                // Scroll up (negative delta.y) should increase value.
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

            DrawQuickMenuPosRow("Create Gallery", "QmCreate", ref m_QuickMenuPosCreateX, ref m_QuickMenuPosCreateXText, ref m_QuickMenuPosCreateY, ref m_QuickMenuPosCreateYText, xMin, xMax, yMin, yMax);
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
                DrawQuickMenuPosRow("Create Gallery (VR)", "QmCreateVR", ref m_QuickMenuPosCreateXVR, ref m_QuickMenuPosCreateXVRText, ref m_QuickMenuPosCreateYVR, ref m_QuickMenuPosCreateYVRText, xMin, xMax, yMin, yMax);
            }
            GUILayout.Space(6);
            DrawQuickMenuPosRow("Show/Hide", "QmShowHide", ref m_QuickMenuPosShowHideX, ref m_QuickMenuPosShowHideXText, ref m_QuickMenuPosShowHideY, ref m_QuickMenuPosShowHideYText, xMin, xMax, yMin, yMax);
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
                DrawQuickMenuPosRow("Show/Hide (VR)", "QmShowHideVR", ref m_QuickMenuPosShowHideXVR, ref m_QuickMenuPosShowHideXVRText, ref m_QuickMenuPosShowHideYVR, ref m_QuickMenuPosShowHideYVRText, xMin, xMax, yMin, yMax);
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
            m_StyleWindow.padding = new RectOffset(0, 0, 0, 0);
            m_StyleWindow.margin = new RectOffset(0, 0, 0, 0);
            m_StyleWindow.border = new RectOffset(0, 0, 0, 0);

            m_StyleContextMenu = new GUIStyle(m_StyleWindow);
            m_StyleContextMenu.padding = new RectOffset(0, 0, 0, 0);
            m_StyleContextMenu.margin = new RectOffset(0, 0, 0, 0);

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

            m_StyleRow = new GUIStyle(GUI.skin.label);
            m_StyleRow.padding = new RectOffset(2, 2, 2, 2);
            m_StyleRow.margin = new RectOffset(0, 0, 0, 0);

            m_StylePkgMgrRow = new GUIStyle(m_StyleRow);
            m_StylePkgMgrRow.wordWrap = false;
            m_StylePkgMgrRow.clipping = TextClipping.Clip;

            m_StylePkgMgrRowCentered = new GUIStyle(m_StylePkgMgrRow);
            m_StylePkgMgrRowCentered.alignment = TextAnchor.MiddleCenter;

            m_StylePkgMgrHeader = new GUIStyle(GUI.skin.button);
            m_StylePkgMgrHeader.fontStyle = FontStyle.Bold;
            m_StylePkgMgrHeader.alignment = TextAnchor.MiddleCenter;
            m_StylePkgMgrHeader.wordWrap = false;
            m_StylePkgMgrHeader.clipping = TextClipping.Clip;

            m_StyleRowAlternate = new GUIStyle(m_StyleRow);
            m_StyleRowAlternate.normal.background = MakeTex(new Color(1, 1, 1, 0.05f));

            m_StyleRowHover = new GUIStyle(m_StyleRow);
            // Flat semi-transparent yellow for selection to ensure readability
            m_StyleRowHover.normal.background = MakeTex(new Color(1f, 0.85f, 0f, 0.35f));

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
            m_StyleInfoCardText.wordWrap = false;

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
                // Move Zstd texture cache to a subfolder of native Textures cache
                string baseCache = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                if (string.IsNullOrEmpty(baseCache))
                {
                    baseCache = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
                }
                cacheDir = Path.Combine(baseCache, "Zstd");
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
                // Keep assetbundle cache in its own isolated folder
                abCacheDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/VPB_cache/ab"));
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
            IsFileManagerInited = false;
            
            // Explicitly initialize ZstdNet native library early
            try { ExternMethods.Initialize(); } catch { }

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
            m_Harmony = new Harmony("VPB_hook");
            // Patch VaM/Harmony hook points.
            SuperControllerHook.PatchOptional(m_Harmony);
            m_Harmony.PatchAll(typeof(AtomHook));
            m_Harmony.PatchAll(typeof(HubResourcePackageHook));
            m_Harmony.PatchAll(typeof(SuperControllerHook));
            m_Harmony.PatchAll(typeof(PatchAssetLoader));

            if (VPBConfig.Instance.IsDevMode)
            {
                Debug.Log("[VPB] Developer Mode is ENABLED");
            }

            GenericTextureHook.PatchAll(m_Harmony);
            DAZClothingHook.PatchAll(m_Harmony);
            ThirdPartyFixHook.PatchAll(m_Harmony);

            // Zstd support is now handled by ZstdNet (auto-initialized)
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

            m_PendingVamCacheCount = GetVamCacheFileCount();
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
        }
        // Called on (hard) restart as well.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogUtil.LogWarning("OnSceneLoaded " + scene.name + " " + mode.ToString());
            if (m_Harmony != null)
            {
                ThirdPartyFixHook.PatchAll(m_Harmony);
            }
            if (mode == LoadSceneMode.Single)
            {
                m_Inited = false;
                IsFileManagerInited = false;
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
            if (m_PendingGc)
            {
                m_PendingGc = false;
                DAZMorphMgr.singleton.cache.Clear();
                ImageLoadingMgr.singleton.ClearCache();
                GC.Collect();
                Resources.UnloadUnusedAssets();
            }

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

            if (!m_UIInited || !IsFileManagerInited)
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


                if (m_AutoOptimizeReportTimer > 0)
                {
                    m_AutoOptimizeReportTimer -= unscaledDt;
                    if (m_AutoOptimizeReportTimer <= 0)
                    {
                        m_AutoOptimizeReport = null;
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
                    OpenCreateGallery();
                }
                if (GalleryKey.TestKeyDown())
                {
                    if (Gallery.singleton != null && Gallery.singleton.IsVisible)
                        Gallery.singleton.Hide();
                    else
                        OpenGallery();
                }
            }

            if (m_Inited && IsFileManagerInited)
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
                if (IsFileManagerInited)
                {
                    if (MVR.Hub.HubBrowse.singleton != null)
                    {
                        CreateHubBrowse();
                        CreateFileBrowser();
                        m_UIInited = true;
                        LogUtil.LogReadyOnce("UI initialized");
                    }
                    else if (VdsLauncher.IsVdsEnabled())
                    {
                        // In VDS mode, HubBrowse might not be available, but we still want to mark UI as inited
                        // to enable FPS display and other UI features.
                        CreateFileBrowser();
                        m_UIInited = true;
                        LogUtil.LogReadyOnce("UI initialized (VDS)");
                    }
                }
            }

            if (!m_QuickMenuButtonInited)
            {
                CreateQuickMenuButton();
            }
            else if (m_ShowHideButtonGO != null && Gallery.singleton != null)
            {
                if (!Settings.Instance.QuickMenuShowHideEnabled.Value)
                {
                    if (m_ShowHideButtonGO.activeSelf)
                        m_ShowHideButtonGO.SetActive(false);
                    return;
                }

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

            m_PackageManagerWindowRect = Settings.Instance.PackageManagerWindowRect.Value;
            m_PkgMgrSortField = Settings.Instance.PackageManagerSortField.Value;
            m_PkgMgrSortAscending = Settings.Instance.PackageManagerSortAscending.Value;

            if (m_FileManager == null)
            {
                var child = Tools.AddChild(this.gameObject);
                child.name = "VarBrowser_Base";
                m_FileManager = child.AddComponent<FileManager>();
                child.AddComponent<VPB.CustomImageLoaderThreaded>();
                child.AddComponent<VPB.ImageLoadingMgr>();
                child.AddComponent<VPB.Gallery>();
                LogUtil.Log("Base components initialized on " + child.name);
                FileManager.RegisterRefreshHandler(() =>
                {
                    IsFileManagerInited = true;
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

        public static bool IsFileManagerInited = false;
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
                child.AddComponent<VPB.HubImageLoaderThreaded>();
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
        GameObject m_CreateGalleryButtonGO;
        RectTransform m_CreateGalleryButtonRT;
        RectTransform m_ShowHideButtonRT;

        private bool m_ShowQuickMenuPosWindow;
        private Rect m_QuickMenuPosWindowRect = new Rect(300, 200, 520, 320);
        private Vector2 m_QuickMenuPosOriginalCreate;
        private Vector2 m_QuickMenuPosOriginalShowHide;
        private float m_QuickMenuPosCreateX;
        private float m_QuickMenuPosCreateY;
        private float m_QuickMenuPosShowHideX;
        private float m_QuickMenuPosShowHideY;
        private float m_QuickMenuPosCreateXVR;
        private float m_QuickMenuPosCreateYVR;
        private float m_QuickMenuPosShowHideXVR;
        private float m_QuickMenuPosShowHideYVR;
        private string m_QuickMenuPosCreateXText;
        private string m_QuickMenuPosCreateYText;
        private string m_QuickMenuPosShowHideXText;
        private string m_QuickMenuPosShowHideYText;
        private string m_QuickMenuPosCreateXVRText;
        private string m_QuickMenuPosCreateYVRText;
        private string m_QuickMenuPosShowHideXVRText;
        private string m_QuickMenuPosShowHideYVRText;
        private bool m_QuickMenuPosUseSameCreateInVR;
        private bool m_QuickMenuPosUseSameShowHideInVR;

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
                    m_QuickMenuCanvas.transform.localPosition = new Vector3(0f, 0f, 0f);
                    m_QuickMenuCanvas.transform.localEulerAngles = new Vector3(0, 180, 0);
                }

                // Button 1: Create Gallery (Left)
                if (Settings.Instance.QuickMenuCreateGalleryEnabled.Value)
                {
                    Transform btnTr = Instantiate(m_MVRPluginManager.configurableButtonPrefab);
                    if (btnTr != null && m_QuickMenuCanvas.transform != null)
                    {
                        m_CreateGalleryButtonGO = btnTr.gameObject;
                        btnTr.SetParent(m_QuickMenuCanvas.transform, false);
                        
                        RectTransform rt = btnTr.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            m_CreateGalleryButtonRT = rt;
                            rt.sizeDelta = new Vector2(100f, 40f);
                            rt.anchoredPosition = isVR ? Settings.Instance.QuickMenuCreateGalleryPosVR.Value : Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
                        }

                        UIDynamicButton uiBtn = btnTr.GetComponent<UIDynamicButton>();
                        if (uiBtn != null)
                        {
                            uiBtn.label = "Create Gallery";
                            if (uiBtn.buttonText != null) uiBtn.buttonText.fontSize = 24;
                            if (uiBtn.button != null)
                            {
                                uiBtn.button.onClick.AddListener(() => {
                                     OpenCreateGallery();
                                });
                            }
                            
                            // Use HoverHandler for dynamic transparency
                            var hover = uiBtn.gameObject.AddComponent<ButtonHoverHandler>();
                            hover.targetButton = uiBtn;
                            uiBtn.buttonColor = new Color(1f, 1f, 1f, 0.5f);
                        }
                    }
                }

                // Button 2: Show/Hide (Right)
                if (Settings.Instance.QuickMenuShowHideEnabled.Value)
                {
                    Transform btnTr2 = Instantiate(m_MVRPluginManager.configurableButtonPrefab);
                    if (btnTr2 != null && m_QuickMenuCanvas.transform != null)
                    {
                        m_ShowHideButtonGO = btnTr2.gameObject;
                        btnTr2.SetParent(m_QuickMenuCanvas.transform, false);
                        
                        RectTransform rt = btnTr2.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            m_ShowHideButtonRT = rt;
                            rt.sizeDelta = new Vector2(100f, 40f);
                            rt.anchoredPosition = isVR ? Settings.Instance.QuickMenuShowHidePosVR.Value : Settings.Instance.QuickMenuShowHidePosDesktop.Value;
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

            GUILayout.Space(MiniMode ? 26 : 50);
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
                if (m_ShowSettings)
                {
                    SaveSettings();
                }
                else
                {
                    OpenSettings();
                }
            }
            GUILayout.EndHorizontal();

            if (MiniMode)
            {
                // ========== MINI MODE: QUICK ACCESS ==========
                DrawPhiSplitButtons("Hub", m_StyleButton, OpenHubBrowse, "Create Gallery", m_StyleButton, OpenCreateGallery, 1.618f, buttonHeight);

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

            if (IsFileManagerInited && m_UIInited)
            {
                const float infoBtnWidth = 28f;

                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                    GUI.enabled = false;

                // Removed original Developer Tools section
                
                {
                    // ========== ZSTD CACHE SETTINGS ==========
                    GUILayout.BeginVertical(m_StyleSection);
                    
                    var stats = ImageLoadingMgr.singleton.CurrentZstdStats;
                    var btnLabel = m_PendingVamCacheCount > 0 ? string.Format("Optimize Cache ({0})", m_PendingVamCacheCount) : "Optimize Cache";
                    var btnRect = GUILayoutUtility.GetRect(new GUIContent(btnLabel), m_StyleButtonPrimary, GUILayout.Height(buttonHeight));
                    
                    if (Event.current.type == EventType.Repaint && btnRect.Contains(Event.current.mousePosition))
                    {
                        if (!m_IsCountingCache && Time.unscaledTime - m_CacheCountUpdateTimer > 2.0f)
                        {
                            m_CacheCountUpdateTimer = Time.unscaledTime;
                            m_IsCountingCache = true;
                            string path = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                            int threshold = Settings.Instance.ThumbnailThreshold.Value;
                            LogUtil.Log("Checking VAM cache count...");
                            System.Threading.ThreadPool.QueueUserWorkItem((s) => {
                                m_PendingVamCacheCount = GetVamCacheFileCount(path, threshold);
                                m_IsCountingCache = false;
                            });
                        }
                    }

                    if (GUI.Button(btnRect, btnLabel, m_StyleButtonPrimary))
                    {
                        if (Settings.Instance.AutoOptimizeCache.Value)
                        {
                            if (!stats.IsRunning)
                            {
                                ImageLoadingMgr.singleton.StartBulkZstdCompression();
                            }
                        }
                        else
                        {
                            m_ShowSpaceSaverWindow = !m_ShowSpaceSaverWindow;
                        }
                    }

                    // Show progress bar under button if running and (minimized or in auto mode)
                    if (stats.IsRunning && (!m_ShowSpaceSaverWindow || Settings.Instance.AutoOptimizeCache.Value))
                    {
                        GUILayout.Space(2);
                        float progress = stats.TotalFiles > 0 ? (float)stats.ProcessedFiles / stats.TotalFiles : 0f;
                        var progressRect = GUILayoutUtility.GetRect(0f, 4f, GUILayout.ExpandWidth(true));
                        
                        // Use solid color for progress bar to ensure visibility
                        var prevColorProgressBar = GUI.color;
                        GUI.color = new Color(0.2f, 1f, 0.2f, 0.8f); // Bright green
                        GUI.DrawTexture(new Rect(progressRect.x, progressRect.y, progressRect.width * progress, progressRect.height), Texture2D.whiteTexture);
                        GUI.color = prevColorProgressBar;
                        GUILayout.Space(2);
                    }

                    // Handle completion report for Auto Mode
                    if (stats.Completed && Settings.Instance.AutoOptimizeCache.Value)
                    {
                        stats.Completed = false;
                        if (stats.TotalOriginalSize > stats.TotalCompressedSize)
                        {
                            long diff = stats.TotalOriginalSize - stats.TotalCompressedSize;
                            m_AutoOptimizeReport = "Saved " + FormatBytes(diff);
                        }
                        else
                        {
                            m_AutoOptimizeReport = "Done!";
                        }
                        m_AutoOptimizeReportTimer = 5.0f;
                    }

                    if (!string.IsNullOrEmpty(m_AutoOptimizeReport))
                    {
                        var prevContentColor = GUI.contentColor;
                        GUI.contentColor = new Color(0.2f, 1f, 0.2f); // Green
                        GUILayout.Label(m_AutoOptimizeReport, m_StyleInfoCardText);
                        GUI.contentColor = prevContentColor;
                    }

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Settings.Instance.AutoOptimizeCache.Value ? "✓" : " ", m_StyleButtonCheckbox, GUILayout.Width(20f), GUILayout.Height(20f)))
                    {
                        Settings.Instance.AutoOptimizeCache.Value = !Settings.Instance.AutoOptimizeCache.Value;
                    }
                    GUILayout.Label("Optimize: No Confirmation", m_StyleInfoCardText);
                    GUILayout.EndHorizontal();

                    if (m_ShowSpaceSaverWindow)
                    {
                        // Window is drawn in OnGUI
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(4);
                }

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
                            () => { m_PendingGc = true; },
                            "Refresh",
                            m_StyleButton,
                            Refresh,
                            1.618f
                        );

                        if (GUI.Button(infoRect, "i", m_StyleButtonSmall ?? GUI.skin.button))
                        {
                            ToggleInfoCard(ref m_ShowGcRefreshInfo);
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

                    // ========== PACKAGE MANAGER ==========
                    if (GUILayout.Button("Package Manager", m_StyleButton, GUILayout.ExpandWidth(true), GUILayout.Height(buttonHeight)))
                    {
                        OpenPackageManagerWindow();
                    }

                    // ========== HUB BROWSE ==========
                    DrawPhiSplitButtons("Hub", m_StyleButton, OpenHubBrowse, "Create Gallery", m_StyleButton, OpenCreateGallery, 1.618f, buttonHeight);



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

                    if (m_ShowSpaceSaverWindow)
                    {
                        // Block world interaction when mouse is over the Optimize Cache window
                        var screenSpaceRect = new Rect(m_SpaceSaverWindowRect.x * m_UIScale, m_SpaceSaverWindowRect.y * m_UIScale, m_SpaceSaverWindowRect.width * m_UIScale, m_SpaceSaverWindowRect.height * m_UIScale);
                        if (screenSpaceRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                        {
                            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel)
                            {
                                Input.ResetInputAxes();
                            }
                        }
                        
                        m_SpaceSaverWindowRect = GUI.Window(999, m_SpaceSaverWindowRect, DrawSpaceSaverWindow, "", m_StyleWindow);
                    }


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
                            tagText = string.Format("VPB {0} | {1:0.0}s | {2:0.0}s", PluginVersionInfo.Version, startupSeconds, sceneLoadSeconds.Value);
                        }
                        else if (sceneClickSeconds.HasValue)
                        {
                            tagText = string.Format("VPB {0} | {1:0.0}s | {2:0.0}s", PluginVersionInfo.Version, startupSeconds, sceneClickSeconds.Value);
                        }
                        else
                        {
                            tagText = string.Format("VPB {0} | {1:0.0}s", PluginVersionInfo.Version, startupSeconds);
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

                    if (m_ShowPackageManagerWindow)
                    {
                        m_PackageManagerWindowRect = GUILayout.Window(1, m_PackageManagerWindowRect, DrawPackageManagerWindow, "", m_StyleWindow);
                        GUI.BringWindowToFront(1);
                    }
                    if (m_ShowDesktopContextMenu)
                    {
                        m_DesktopContextMenuRect = GUILayout.Window(100, m_DesktopContextMenuRect, DrawDesktopContextMenu, "", m_StyleContextMenu);
                        GUI.BringWindowToFront(100);
                    }
                    if (m_ShowRemoveWindow)
                    {
                        m_RemoveWindowRect = GUILayout.Window(2, m_RemoveWindowRect, DrawRemoveWindow, "", m_StyleWindow);
                        GUI.BringWindowToFront(2);
                    }
                    if (m_ShowQuickMenuPosWindow)
                    {
                        ApplyQuickMenuPositionPreview();
                        m_QuickMenuPosWindowRect = GUILayout.Window(3, m_QuickMenuPosWindowRect, DrawQuickMenuPosWindow, "", m_StyleWindow);
                        GUI.BringWindowToFront(3);
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
             VarPackage pkg = FileManager.GetPackage(uid, false);
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

        private string GetFirstScenePath(string uid)
        {
             VarPackage pkg = FileManager.GetPackage(uid, false);
             if (pkg == null || pkg.FileEntries == null) return null;
             
             foreach (var entry in pkg.FileEntries)
             {
                 string f = entry.InternalPath;
                 if (f.StartsWith("Saves/scene", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                 {
                     return uid + ":/" + f;
                 }
             }
             return null;
        }


        bool IsPackageManagerItemVisible(PackageManagerItem item)
        {
            // Exclusive filters (must match NONE)
            if (m_PkgMgrCategoryExclusive.Count > 0)
            {
                foreach (var filter in m_PkgMgrCategoryExclusive)
                {
                    bool itemMatches = false;
                    if (filter == "Locked (L)") itemMatches = item.Locked;
                    else if (filter == "Active") itemMatches = item.IsActive;
                    else if (filter == "Latest") itemMatches = item.IsLatest;
                    else if (filter == "Old Version") itemMatches = !item.IsLatest;
                    else itemMatches = (item.Type == filter);

                    if (itemMatches) return false;
                }
            }

            // Inclusive filters (must match at least ONE if any are set)
            if (m_PkgMgrCategoryInclusive.Count > 0)
            {
                bool match = false;
                foreach (var filter in m_PkgMgrCategoryInclusive)
                {
                    if (filter == "Locked (L)") { if (item.Locked) { match = true; break; } }
                    else if (filter == "Active") { if (item.IsActive) { match = true; break; } }
                    else if (filter == "Latest") { if (item.IsLatest) { match = true; break; } }
                    else if (filter == "Old Version") { if (!item.IsLatest) { match = true; break; } }
                    else if (item.Type == filter) { match = true; break; }
                }
                if (!match) return false;
            }

            if (!string.IsNullOrEmpty(m_PkgMgrFilter))
            {
                if (item.Uid.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0 && 
                    item.Type.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Path.IndexOf(m_PkgMgrFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        void DrawPackageManagerHeader(string label, string field, float width = -1)
        {
            string sortIndicator = "";
            if (m_PkgMgrSortField == field)
            {
                sortIndicator = m_PkgMgrSortAscending ? " ▲" : " ▼";
            }

            bool clicked;
            if (width > 0) clicked = GUILayout.Button(label + sortIndicator, m_StylePkgMgrHeader, GUILayout.Width(width));
            else clicked = GUILayout.Button(label + sortIndicator, m_StylePkgMgrHeader);

            if (clicked)
            {
                if (m_PkgMgrSortField == field) m_PkgMgrSortAscending = !m_PkgMgrSortAscending;
                else
                {
                    m_PkgMgrSortField = field;
                    m_PkgMgrSortAscending = true;
                }
                Settings.Instance.PackageManagerSortField.Value = m_PkgMgrSortField;
                Settings.Instance.PackageManagerSortAscending.Value = m_PkgMgrSortAscending;
                SortPackageManagerList();
            }
        }

        void DrawPackageManagerWindow(int windowID)
        {
            if (Event.current.type == EventType.ScrollWheel || Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) 
            {
                Input.ResetInputAxes();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.A)
            {
                if (Event.current.mousePosition.x < m_PackageManagerWindowRect.width / 2) SelectAllPackageManager(m_AddonList, true);
                else SelectAllPackageManager(m_AllList, true);
                Event.current.Use();
            }

            if (m_PkgMgrIndicesDirty) RefreshVisibleIndices();

            GUILayout.BeginVertical(m_StylePanel);
            
            // Header & Filter
            GUILayout.BeginHorizontal();
            GUILayout.Label("Package Manager", m_StyleHeader, GUILayout.ExpandWidth(false));
            GUILayout.Space(20);
            GUILayout.Label("<color=#aaaaaa>Isolate (Left Click + | Right Click -)</color>", m_StyleInfoCardText, GUILayout.ExpandWidth(false));
            GUILayout.Space(20);
            GUILayout.Label("Filter:", GUILayout.Width(40));
            GUI.SetNextControlName("PkgMgrFilter");
            string newPkgMgrFilter = GUILayout.TextField(m_PkgMgrFilter, GUILayout.ExpandWidth(true), GUILayout.MinWidth(100));
            if (newPkgMgrFilter != m_PkgMgrFilter)
            {
                m_PkgMgrFilter = newPkgMgrFilter;
                m_PkgMgrFilterLower = m_PkgMgrFilter.ToLower();
                m_PkgMgrIndicesDirty = true;
            }
            if (GUILayout.Button("Clear", m_StyleButtonSmall, GUILayout.Width(50)))
            {
                m_PkgMgrFilter = "";
                m_PkgMgrFilterLower = "";
                m_PkgMgrIndicesDirty = true;
                GUI.FocusControl("");
            }
            if (GUILayout.Button("Refresh", m_StyleButtonSmall, GUILayout.Width(60)))
            {
                Refresh();
                ScanPackageManagerPackages();
            }
            GUILayout.Space(10);
            if (GUILayout.Button("X", m_StyleButtonSmall, GUILayout.Width(30))) m_ShowPackageManagerWindow = false;
            GUILayout.EndHorizontal();

            // Category Filter
            if (m_PkgMgrCategories.Count > 0)
            {
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                float currentWidth = 0;
                float maxWidth = m_PackageManagerWindowRect.width - 40; 
                
                foreach (var cat in m_PkgMgrCategories)
                {
                    bool isInc = m_PkgMgrCategoryInclusive.Contains(cat);
                    bool isExc = m_PkgMgrCategoryExclusive.Contains(cat);
                    bool isAll = (cat == "All");
                    bool isSelected = isAll ? (m_PkgMgrCategoryInclusive.Count == 0 && m_PkgMgrCategoryExclusive.Count == 0) : (isInc || isExc);
                    
                    int count = 0;
                    m_PkgMgrCategoryCounts.TryGetValue(cat, out count);
                    
                    string prefix = isInc ? "+ " : (isExc ? "- " : "");
                    string label = string.Format("{0}{1} ({2})", prefix, cat, count);
                    GUIContent content = new GUIContent(label);
                    float btnWidth = m_StyleButtonSmall.CalcSize(content).x + 10;

                    if (currentWidth + btnWidth > maxWidth && currentWidth > 0)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        currentWidth = 0;
                    }
                    currentWidth += btnWidth;
                    
                    Rect r = GUILayoutUtility.GetRect(content, m_StyleButtonSmall, GUILayout.ExpandWidth(false));
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.button == 0) // Left click
                        {
                            if (isAll)
                            {
                                m_PkgMgrCategoryInclusive.Clear();
                                m_PkgMgrCategoryExclusive.Clear();
                            }
                            else
                            {
                                if (isInc) m_PkgMgrCategoryInclusive.Remove(cat);
                                else { m_PkgMgrCategoryInclusive.Add(cat); m_PkgMgrCategoryExclusive.Remove(cat); }
                            }
                            m_PkgMgrIndicesDirty = true;
                            Event.current.Use();
                        }
                        else if (Event.current.button == 1) // Right click
                        {
                            if (!isAll)
                            {
                                if (isExc) m_PkgMgrCategoryExclusive.Remove(cat);
                                else { m_PkgMgrCategoryExclusive.Add(cat); m_PkgMgrCategoryInclusive.Remove(cat); }
                                m_PkgMgrIndicesDirty = true;
                                Event.current.Use();
                            }
                        }
                    }
                    GUI.Toggle(r, isSelected, label, m_StyleButtonSmall);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            
            GUILayout.Space(5);

            // Tables Side-by-Side
            float sidePaneWidth = Mathf.Max(250, (m_PackageManagerWindowRect.width - 120) / 2f);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            
            int addonSelectedCount = 0;
            long addonSelectedSize = 0;
            foreach (var item in m_AddonList) if (item.Checked) { addonSelectedCount++; addonSelectedSize += item.Size; }
            int allSelectedCount = 0;
            long allSelectedSize = 0;
            foreach (var item in m_AllList) if (item.Checked) { allSelectedCount++; allSelectedSize += item.Size; }

            // LEFT: Loaded
            GUILayout.BeginVertical(GUILayout.Width(sidePaneWidth));
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Loaded ({0} | {1} vis | {2} sel | {3})", m_PkgMgrAddonCount, m_AddonVisibleIndices.Count, addonSelectedCount, FormatSize(addonSelectedSize)), m_StyleSubHeader, GUILayout.Width(sidePaneWidth - 90));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("All", m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AddonList, true);
            if (GUILayout.Button("None", m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AddonList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AddonList, ref m_AddonScroll, ref m_AddonLastSelectedIndex, sidePaneWidth);
            GUILayout.EndVertical();

            // MIDDLE: Buttons
            GUILayout.BeginVertical(GUILayout.Width(50), GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("<b>></b>", m_StyleButton, GUILayout.Height(60))) PerformMove(m_AddonList, true);
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("[ L ]", "Lock/Unlock selected packages"), m_StyleButton, GUILayout.Height(40))) ToggleLockSelection();
            GUILayout.Space(15);
            if (GUILayout.Button("<b><</b>", m_StyleButton, GUILayout.Height(60))) PerformMove(m_AllList, false);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            // RIGHT: Unloaded
            GUILayout.BeginVertical(GUILayout.Width(sidePaneWidth));
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Unloaded ({0} | {1} vis | {2} sel | {3})", m_PkgMgrAllCount, m_AllVisibleIndices.Count, allSelectedCount, FormatSize(allSelectedSize)), m_StyleSubHeader, GUILayout.Width(sidePaneWidth - 90));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("All", m_StyleButtonSmall, GUILayout.Width(40))) SelectAllPackageManager(m_AllList, true);
            if (GUILayout.Button("None", m_StyleButtonSmall, GUILayout.Width(45))) SelectAllPackageManager(m_AllList, false);
            GUILayout.EndHorizontal();
            DrawPackageManagerPane(m_AllList, ref m_AllScroll, ref m_AllLastSelectedIndex, sidePaneWidth);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // Footer / Stats
            GUILayout.BeginHorizontal(m_StyleSection);
            if (m_PkgMgrStatusTimer > Time.realtimeSinceStartup)
            {
                GUILayout.Label("<b>" + m_PkgMgrStatusMessage + "</b>", m_StyleInfoCardText, GUILayout.Height(20));
            }
            else
            {
                GUILayout.Label("Path: " + (string.IsNullOrEmpty(GUI.tooltip) ? "" : GUI.tooltip), m_StyleInfoCardText, GUILayout.Height(20));
            }
            GUILayout.EndHorizontal();

            // Resize handle
            var resizeRect = new Rect(m_PackageManagerWindowRect.width - 30, m_PackageManagerWindowRect.height - 30, 30, 30);
            GUI.Box(new Rect(m_PackageManagerWindowRect.width - 20, m_PackageManagerWindowRect.height - 20, 20, 20), "◢", m_StyleInfoIcon);
            int resizeControlID = GUIUtility.GetControlID(FocusType.Passive);
            switch (Event.current.GetTypeForControl(resizeControlID))
            {
                case EventType.MouseDown:
                    if (resizeRect.Contains(Event.current.mousePosition)) { GUIUtility.hotControl = resizeControlID; Event.current.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == resizeControlID) { 
                        GUIUtility.hotControl = 0; 
                        Settings.Instance.PackageManagerWindowRect.Value = m_PackageManagerWindowRect;
                        Event.current.Use(); 
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == resizeControlID)
                    {
                        m_PackageManagerWindowRect.width = Mathf.Max(m_PackageManagerWindowRect.width + Event.current.delta.x, 600);
                        m_PackageManagerWindowRect.height = Mathf.Max(m_PackageManagerWindowRect.height + Event.current.delta.y, 400);
                        Event.current.Use();
                    }
                    break;
            }

            GUILayout.EndVertical();
            if (Event.current.type != EventType.MouseDrag || !resizeRect.Contains(Event.current.mousePosition)) GUI.DragWindow();

            if (Event.current.type == EventType.MouseUp)
            {
                Settings.Instance.PackageManagerWindowRect.Value = m_PackageManagerWindowRect;
            }
        }

        void ToggleLockSelection()
        {
            foreach (var item in m_AddonList) 
            {
                if (item.Checked) 
                {
                    item.Locked = !item.Locked;
                    if (item.Locked) 
                    {
                        if (m_LockedPackages.Add(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]++;
                    }
                    else 
                    {
                        if (m_LockedPackages.Remove(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]--;
                    }
                }
            }
            foreach (var item in m_AllList) 
            {
                if (item.Checked) 
                {
                    item.Locked = !item.Locked;
                    if (item.Locked) 
                    {
                        if (m_LockedPackages.Add(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]++;
                    }
                    else 
                    {
                        if (m_LockedPackages.Remove(item.Uid)) m_PkgMgrCategoryCounts["Locked (L)"]--;
                    }
                }
            }
        }

        void SelectAllPackageManager(System.Collections.Generic.List<PackageManagerItem> list, bool state)
        {
            foreach (var item in list)
            {
                if (IsPackageManagerItemVisible(item))
                {
                    if (item.Locked && state && !m_PkgMgrCategoryInclusive.Contains("Locked (L)")) continue;
                    item.Checked = state;
                }
            }
        }

        void ShowPackageManagerContextMenu(PackageManagerItem item)
        {
            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            if (!isVR)
            {
                m_ContextMenuTargetItem = item;
                m_ShowDesktopContextMenu = true;
                
                // Get mouse position in GUI space (scaled)
                Vector2 mousePos = new Vector2(Input.mousePosition.x / m_UIScale, (Screen.height - Input.mousePosition.y) / m_UIScale);
                
                // Offset slightly to place the cursor tip comfortably within the clickable area.
                float offsetX = -5f;
                float offsetY = -5f; 
                
                m_DesktopContextMenuRect = new Rect(mousePos.x + offsetX, mousePos.y + offsetY, 250, 0);
                
                // Keep menu on screen
                float screenWidthScaled = Screen.width / m_UIScale;
                float screenHeightScaled = Screen.height / m_UIScale;
                if (m_DesktopContextMenuRect.xMax > screenWidthScaled) m_DesktopContextMenuRect.x = screenWidthScaled - m_DesktopContextMenuRect.width;
                // Note: Height is 0 here as GUILayout.Window will auto-expand, but we can't easily check final height before it draws.
                
                return;
            }

            System.Collections.Generic.List<ContextMenuPanel.Option> options = new System.Collections.Generic.List<ContextMenuPanel.Option>();
            int groupCount = CountGroupItems(item.Uid);
            int selectedCount = CountSelectedItems();
            string title = item.Uid;

            if (item.Checked && selectedCount > 1)
            {
                title = string.Format("{0} items selected", selectedCount);

                options.Add(new ContextMenuPanel.Option("Copy Selected Names", () => {
                    CopySelectedNames();
                }));

                options.Add(new ContextMenuPanel.Option("Copy Selected Dependencies (Deep)", () => {
                    CopySelectedDependenciesDeep();
                }));

                options.Add(new ContextMenuPanel.Option("Unselect All", () => {
                    SelectAllPackageManager(m_AddonList, false);
                    SelectAllPackageManager(m_AllList, false);
                }));

                options.Add(new ContextMenuPanel.Option("Keep Selected -> Unload Rest", () => {
                    PerformKeepSelectedUnloadRest();
                }));
            }
            else if (groupCount > 1)
            {
                title = string.Format("Group: {0} ({1} items)", FileManager.PackageIDToPackageGroupID(item.Uid), groupCount);

                options.Add(new ContextMenuPanel.Option("Select Group", () => {
                    SetGroupChecked(item.Uid, true);
                }));

                options.Add(new ContextMenuPanel.Option("Unselect Group", () => {
                    SetGroupChecked(item.Uid, false);
                }));

                options.Add(new ContextMenuPanel.Option("Copy Group Names", () => {
                    CopyGroupNames(item.Uid);
                }));

                options.Add(new ContextMenuPanel.Option("Copy Group Dependencies (Deep)", () => {
                    CopyGroupDependenciesDeep(item.Uid);
                }));

                if (selectedCount > 0)
                {
                    options.Add(new ContextMenuPanel.Option("Keep Selected -> Unload Rest", () => {
                        PerformKeepSelectedUnloadRest();
                    }));
                }
            }
            else
            {
                options.Add(new ContextMenuPanel.Option("Copy Package Name", () => {
                    GUIUtility.systemCopyBuffer = item.Uid;
                }));

                options.Add(new ContextMenuPanel.Option("Copy Dependencies (Deep)", () => {
                    VarPackage pkg = FileManager.GetPackage(item.Uid, false);
                    if (pkg != null)
                    {
                        var deps = pkg.GetDependenciesDeep(2);
                        if (deps != null && deps.Count > 0)
                        {
                            GUIUtility.systemCopyBuffer = string.Join("\n", deps.ToArray());
                            LogUtil.Log("Copied " + deps.Count + " dependencies to clipboard.");
                        }
                        else
                        {
                            LogUtil.Log("No dependencies found for " + item.Uid);
                        }
                    }
                }));

                if (item.Type == "Scene")
                {
                    options.Add(new ContextMenuPanel.Option("Launch Scene", () => {
                        string scenePath = GetFirstScenePath(item.Uid);
                        if (!string.IsNullOrEmpty(scenePath))
                        {
                            LoadFromSceneWorldDialog(scenePath);
                            m_ShowPackageManagerWindow = false;
                        }
                    }));
                }

                if (selectedCount > 0)
                {
                    options.Add(new ContextMenuPanel.Option("Keep Selected -> Unload Rest", () => {
                        PerformKeepSelectedUnloadRest();
                    }));
                }
            }

            // Default position in front of camera
            Vector3 position = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
            ContextMenuPanel.Instance.Show(position, options, title);
        }

        private int CountSelectedItems()
        {
            int count = 0;
            foreach (var item in m_AddonList) if (item.Checked) count++;
            foreach (var item in m_AllList) if (item.Checked) count++;
            return count;
        }

        private void CopySelectedNames()
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList) if (item.Checked) names.Add(item.Uid);
            foreach (var item in m_AllList) if (item.Checked) names.Add(item.Uid);
            
            if (names.Count > 0)
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", names.ToArray());
                m_PkgMgrStatusMessage = string.Format("Copied {0} selected names to clipboard.", names.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private void CopySelectedDependenciesDeep()
        {
            System.Collections.Generic.HashSet<string> allDeps = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            System.Collections.Generic.List<string> uids = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList) if (item.Checked) uids.Add(item.Uid);
            foreach (var item in m_AllList) if (item.Checked) uids.Add(item.Uid);

            foreach (var uid in uids)
            {
                var deps = FileManager.GetDependenciesDeep(uid, 2);
                foreach (var dep in deps) allDeps.Add(dep);
            }

            if (allDeps.Count > 0)
            {
                string[] depArray = new string[allDeps.Count];
                allDeps.CopyTo(depArray);
                GUIUtility.systemCopyBuffer = string.Join("\n", depArray);
                m_PkgMgrStatusMessage = string.Format("Copied {0} unique deep dependencies for selection to clipboard.", allDeps.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private int CountGroupItems(string packageId)
        {
            string groupId = FileManager.PackageIDToPackageGroupID(packageId);
            int count = 0;
            foreach (var item in m_AddonList)
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase)) count++;
            foreach (var item in m_AllList)
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }

        void DrawDesktopContextMenu(int windowID)
        {
            if (m_ContextMenuTargetItem == null) 
            {
                m_ShowDesktopContextMenu = false;
                return;
            }

            GUILayout.BeginVertical(m_StylePanel);
            
            int groupCount = CountGroupItems(m_ContextMenuTargetItem.Uid);
            int selectedCount = CountSelectedItems();
            string title = m_ContextMenuTargetItem.Uid;

            if (m_ContextMenuTargetItem.Checked && selectedCount > 1)
            {
                title = string.Format("{0} items selected", selectedCount);
            }
            else if (groupCount > 1)
            {
                title = string.Format("Group: {0} ({1} items)", FileManager.PackageIDToPackageGroupID(m_ContextMenuTargetItem.Uid), groupCount);
            }

            GUILayout.Label("<b>" + title + "</b>", m_StyleInfoCardText);
            GUILayout.Space(5);

            if (m_ContextMenuTargetItem.Checked && selectedCount > 1)
            {
                // Selection mode
                if (GUILayout.Button("Copy Selected Names", m_StyleButton))
                {
                    CopySelectedNames();
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Selected Dependencies (Deep)", m_StyleButton))
                {
                    CopySelectedDependenciesDeep();
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Unselect All", m_StyleButton))
                {
                    SelectAllPackageManager(m_AddonList, false);
                    SelectAllPackageManager(m_AllList, false);
                    m_ShowDesktopContextMenu = false;
                }

                GUILayout.Space(5);
                if (GUILayout.Button("<b>Keep Selected -> Unload Rest</b>", m_StyleButton))
                {
                    PerformKeepSelectedUnloadRest();
                    m_ShowDesktopContextMenu = false;
                }
            }
            else if (groupCount > 1)
            {
                // Group mode
                if (GUILayout.Button("Select Group", m_StyleButton))
                {
                    SetGroupChecked(m_ContextMenuTargetItem.Uid, true);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Unselect Group", m_StyleButton))
                {
                    SetGroupChecked(m_ContextMenuTargetItem.Uid, false);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Group Names", m_StyleButton))
                {
                    CopyGroupNames(m_ContextMenuTargetItem.Uid);
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Group Dependencies (Deep)", m_StyleButton))
                {
                    CopyGroupDependenciesDeep(m_ContextMenuTargetItem.Uid);
                    m_ShowDesktopContextMenu = false;
                }
            }
            else
            {
                // Single item mode
                if (GUILayout.Button("Copy Package Name", m_StyleButton))
                {
                    GUIUtility.systemCopyBuffer = m_ContextMenuTargetItem.Uid;
                    m_PkgMgrStatusMessage = "Copied name to clipboard: " + m_ContextMenuTargetItem.Uid;
                    m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                    m_ShowDesktopContextMenu = false;
                }

                if (GUILayout.Button("Copy Dependencies (Deep)", m_StyleButton))
                {
                    VarPackage pkg = FileManager.GetPackage(m_ContextMenuTargetItem.Uid, false);
                    if (pkg != null)
                    {
                        var deps = pkg.GetDependenciesDeep(2);
                        if (deps != null && deps.Count > 0)
                        {
                            GUIUtility.systemCopyBuffer = string.Join("\n", deps.ToArray());
                            m_PkgMgrStatusMessage = string.Format("Copied {0} dependencies to clipboard.", deps.Count);
                            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                            LogUtil.Log("Copied " + deps.Count + " dependencies to clipboard.");
                        }
                        else
                        {
                            m_PkgMgrStatusMessage = "No dependencies found for " + m_ContextMenuTargetItem.Uid;
                            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
                            LogUtil.Log("No dependencies found for " + m_ContextMenuTargetItem.Uid);
                        }
                    }
                    m_ShowDesktopContextMenu = false;
                }

                if (m_ContextMenuTargetItem.Type == "Scene")
                {
                    if (GUILayout.Button("<b>Launch Scene</b>", m_StyleButton))
                    {
                        string scenePath = GetFirstScenePath(m_ContextMenuTargetItem.Uid);
                        if (!string.IsNullOrEmpty(scenePath))
                        {
                            LoadFromSceneWorldDialog(scenePath);
                            m_ShowPackageManagerWindow = false;
                        }
                        m_ShowDesktopContextMenu = false;
                    }
                }

                if (selectedCount > 0)
                {
                    GUILayout.Space(5);
                    if (GUILayout.Button("<b>Keep Selected -> Unload Rest</b>", m_StyleButton))
                    {
                        PerformKeepSelectedUnloadRest();
                        m_ShowDesktopContextMenu = false;
                    }
                }
            }

            GUILayout.Space(5);
            if (GUILayout.Button("Cancel", m_StyleButtonDanger))
            {
                m_ShowDesktopContextMenu = false;
            }

            GUILayout.EndVertical();

            // Close if clicked elsewhere
            if (Event.current.type == EventType.MouseDown && !new Rect(0, 0, m_DesktopContextMenuRect.width, m_DesktopContextMenuRect.height).Contains(Event.current.mousePosition))
            {
                m_ShowDesktopContextMenu = false;
                Event.current.Use();
            }
        }

        void DrawLabelWithEllipsis(Rect rect, string text, GUIStyle style, string tooltip = "")
        {
            GUIContent content = new GUIContent(text, tooltip);
            Vector2 size = style.CalcSize(content);
            if (size.x > rect.width)
            {
                string truncatedText = text;
                int iterations = 0;
                while (truncatedText.Length > 0 && style.CalcSize(new GUIContent(truncatedText + "...")).x > rect.width && iterations < 500)
                {
                    truncatedText = truncatedText.Substring(0, truncatedText.Length - 1);
                    iterations++;
                }
                content.text = truncatedText + "...";
            }
            GUI.Label(rect, content, style);
        }

        void DrawPackageManagerPane(System.Collections.Generic.List<PackageManagerItem> list, ref Vector2 scroll, ref int lastIdx, float paneWidth)
        {
            GUILayout.BeginVertical(m_StyleSection, GUILayout.ExpandHeight(true));
            
            // Local Headers
            float nameWidth = paneWidth - 100 - 80 - 65 - 40; // Subtract other columns + buffer
            GUILayout.BeginHorizontal();
            DrawPackageManagerHeader("Category", "Category", 100);
            DrawPackageManagerHeader("Name", "Name", nameWidth);
            DrawPackageManagerHeader("Dep", "Deps", 80);
            //DrawPackageManagerHeader("Age", "Age", 60);
            DrawPackageManagerHeader("Size", "Size", 65);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            
            System.Collections.Generic.List<int> visibleIndices = (list == m_AddonList) ? m_AddonVisibleIndices : m_AllVisibleIndices;

            if (Event.current.type == EventType.MouseUp) m_PkgMgrIsDragging = false;

            float rowHeight = 24;
            int firstVisible = Mathf.Max(0, (int)(scroll.y / rowHeight));
            int lastVisible = Mathf.Min(visibleIndices.Count - 1, (int)((scroll.y + 800) / rowHeight));

            GUILayout.Space(firstVisible * rowHeight);

            for (int j = firstVisible; j <= lastVisible; j++)
            {
                int i = visibleIndices[j];
                var item = list[i];
                
                Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
                
                // Selection Logic
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 1) // Right click
                    {
                        ShowPackageManagerContextMenu(item);
                        Event.current.Use();
                    }
                    else if (Event.current.shift && lastIdx != -1)
                    {
                        int startVisible = -1;
                        for (int k = 0; k < visibleIndices.Count; k++) { if (visibleIndices[k] == lastIdx) { startVisible = k; break; } }
                        if (startVisible != -1)
                        {
                            int min = Math.Min(startVisible, j);
                            int max = Math.Max(startVisible, j);
                            bool newState = list[lastIdx].Checked;
                            for (int k = min; k <= max; k++) list[visibleIndices[k]].Checked = newState;
                        }
                        else item.Checked = !item.Checked;
                        lastIdx = i;
                    }
                    else if (Event.current.control) { item.Checked = !item.Checked; lastIdx = i; }
                    else
                    {
                        // Single selection mode: clear others in this list
                        bool wasChecked = item.Checked;
                        foreach (var it in list) it.Checked = false;
                        item.Checked = !wasChecked;
                        lastIdx = i;
                        m_PkgMgrIsDragging = true;
                        m_PkgMgrDragChecked = item.Checked;
                        m_PkgMgrDragLastIdx = j;
                    }
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDrag && m_PkgMgrIsDragging && rowRect.Contains(Event.current.mousePosition))
                {
                    if (m_PkgMgrDragLastIdx != j)
                    {
                        int min = Math.Min(m_PkgMgrDragLastIdx, j);
                        int max = Math.Max(m_PkgMgrDragLastIdx, j);
                        for (int k = min; k <= max; k++)
                        {
                            int idx = visibleIndices[k];
                            list[idx].Checked = m_PkgMgrDragChecked;
                        }
                        lastIdx = i;
                        m_PkgMgrDragLastIdx = j;
                    }
                    Event.current.Use();
                }

                if (Event.current.type == EventType.Repaint)
                {
                    if (item.Locked)
                    {
                        // Dark grey background for locked items
                        GUI.Box(rowRect, "", m_StyleButton); 
                        var prevCol = GUI.color;
                        GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                        GUI.DrawTexture(rowRect, Texture2D.whiteTexture);
                        GUI.color = prevCol;
                    }
                    else if (item.Checked) 
                    {
                        m_StyleRowHover.Draw(rowRect, false, false, false, false);
                    }
                    else
                    {
                        GUIStyle style = (j % 2 == 0) ? m_StyleRowAlternate : m_StyleRow;
                        style.Draw(rowRect, false, false, false, false);
                    }
                }

                // Content
                var prevContentColor = GUI.contentColor;
                if (item.Locked) GUI.contentColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

                float x = rowRect.x + 5;
                string statusPrefix = (item.Locked ? "L " : "") + (item.IsActive ? "A " : "") + (!item.IsLatest ? "O " : "");
                
                // Truncate Type/Category and Name with ellipsis if they don't fit
                DrawLabelWithEllipsis(new Rect(x, rowRect.y, 100, rowRect.height), item.Type, m_StylePkgMgrRow, item.Path);
                x += 105;
                DrawLabelWithEllipsis(new Rect(x, rowRect.y, rowRect.width - 275, rowRect.height), statusPrefix + item.Uid, m_StylePkgMgrRow, item.Path);

                x = rowRect.xMax - 150;
                GUI.Label(new Rect(x, rowRect.y, 80, rowRect.height), string.Format("{0} ({1})", item.DependencyCount, item.LoadedDependencyCount), m_StylePkgMgrRowCentered);
                
                x = rowRect.xMax - 70;
                GUI.Label(new Rect(x, rowRect.y, 65, rowRect.height), FormatSize(item.Size), m_StylePkgMgrRowCentered);

                GUI.contentColor = prevContentColor;
            }

            GUILayout.Space(Mathf.Max(0, (visibleIndices.Count - 1 - lastVisible) * rowHeight));

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void PerformMove(System.Collections.Generic.List<PackageManagerItem> sourceList, bool isMovingToAll)
        {
            foreach (var item in sourceList)
            {
                if (item.Locked) continue; // Skip locked items
                if (!item.Checked) continue;

                string targetPath;
                if (isMovingToAll) targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                else targetPath = "AddonPackages" + item.Path.Substring("AllPackages".Length);

                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) continue;

                try { File.Move(item.Path, targetPath); }
                catch (Exception ex) { LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
            }
            Refresh();
            RemoveEmptyFolder("AddonPackages");
            RemoveEmptyFolder("AllPackages");
            ScanPackageManagerPackages();
        }

        private void PerformKeepSelectedUnloadRest()
        {
            HashSet<string> keepUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Add explicitly selected items and their recursive dependencies
            foreach (var item in m_AddonList)
            {
                if (item.Checked)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }
            foreach (var item in m_AllList)
            {
                if (item.Checked)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }

            // 2. Add active (protected) items and their recursive dependencies
            foreach (var item in m_AddonList)
            {
                if (item.IsActive)
                {
                    ProtectPackage(item.Uid, keepUids);
                }
            }

            // 3. Unload everything in AddonPackages that is NOT in keepUids
            int moveCount = 0;
            foreach (var item in m_AddonList)
            {
                if (item.Locked) continue;
                if (keepUids.Contains(item.Uid)) continue;

                // Move to AllPackages
                string targetPath = "AllPackages" + item.Path.Substring("AddonPackages".Length);
                string dir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(targetPath)) continue;

                try 
                { 
                    File.Move(item.Path, targetPath); 
                    moveCount++;
                }
                catch (Exception ex) { LogUtil.LogError("Failed to move " + item.Path + ": " + ex.Message); }
            }

            if (moveCount > 0)
            {
                m_PkgMgrStatusMessage = string.Format("Unloaded {0} packages (kept selected + active + deps).", moveCount);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 4f;
                Refresh();
                RemoveEmptyFolder("AddonPackages");
                RemoveEmptyFolder("AllPackages");
                ScanPackageManagerPackages();
            }
            else
            {
                m_PkgMgrStatusMessage = "No additional packages to unload.";
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        public void ScanPackageManagerPackages()
        {
            System.Collections.Generic.HashSet<string> protectedPackages = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Protect currently loaded scene and its dependencies
            if (FileEntry.AutoInstallLookup != null)
            {
                foreach (var item in FileEntry.AutoInstallLookup)
                {
                    ProtectPackage(item, protectedPackages);
                    VarPackage p = FileManager.ResolveDependency(item);
                    if (p != null) ProtectPackage(p.Uid, protectedPackages);
                }
            }

            string currentPackageUid = CurrentScenePackageUid;
            if (string.IsNullOrEmpty(currentPackageUid))
            {
                currentPackageUid = FileManager.CurrentPackageUid;
            }
            ProtectPackage(currentPackageUid, protectedPackages);

            // Protect active plugins
            try
            {
                var plugins = UnityEngine.Object.FindObjectsOfType<MVRScript>();
                foreach (var p in plugins)
                {
                        var fields = p.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        foreach(var f in fields)
                        {
                            if (f.FieldType == typeof(JSONStorableUrl))
                            {
                                var jUrl = f.GetValue(p) as JSONStorableUrl;
                                if (jUrl != null && !string.IsNullOrEmpty(jUrl.val))
                                {
                                    string pkg = GetPackageFromPath(jUrl.val);
                                    if (pkg != null) ProtectPackage(pkg, protectedPackages);
                                }
                            }
                        }
                }
            }
            catch (Exception) { }

            m_PkgMgrCategories.Clear();
            m_PkgMgrCategories.Add("All");
            m_PkgMgrCategories.Add("Active");
            m_PkgMgrCategories.Add("Locked (L)");
            m_PkgMgrCategories.Add("Latest");
            m_PkgMgrCategories.Add("Old Version");

            m_PkgMgrCategoryCounts.Clear();
            m_PkgMgrCategoryCounts["All"] = 0;
            m_PkgMgrCategoryCounts["Active"] = 0;
            m_PkgMgrCategoryCounts["Locked (L)"] = 0;
            m_PkgMgrCategoryCounts["Latest"] = 0;
            m_PkgMgrCategoryCounts["Old Version"] = 0;

            System.Collections.Generic.HashSet<string> latestUids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = FileManager.GetPackageGroups();
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    if (g.NewestPackage != null) latestUids.Add(g.NewestPackage.Uid);
                }
            }

            System.Collections.Generic.HashSet<string> types = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            m_AddonList.Clear();
            ScanDirectory("AddonPackages", m_AddonList, protectedPackages, types, latestUids);
            m_PkgMgrAddonCount = m_AddonList.Count;

            m_AllList.Clear();
            ScanDirectory("AllPackages", m_AllList, protectedPackages, types, latestUids);
            m_PkgMgrAllCount = m_AllList.Count;
            
            var sortedTypes = new System.Collections.Generic.List<string>(types);
            sortedTypes.Sort();
            m_PkgMgrCategories.AddRange(sortedTypes);
            
            SortPackageManagerList();
            m_PkgMgrIndicesDirty = true;
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024f).ToString("F1") + " KB";
            if (bytes < 1073741824) return (bytes / 1048576f).ToString("F1") + " MB";
            return (bytes / 1073741824f).ToString("F2") + " GB";
        }

        private string FormatAge(DateTime dt, DateTime now)
        {
            TimeSpan span = now - dt;
            if (span.TotalDays < 0) return "Future";
            if (span.TotalDays < 1) return "Today";
            if (span.TotalDays < 7) return (int)span.TotalDays + "d";
            if (span.TotalDays < 30) return (int)(span.TotalDays / 7) + "w";
            if (span.TotalDays < 365) return (int)(span.TotalDays / 30) + "m";
            return (int)(span.TotalDays / 365) + "y";
        }

        private void ScanDirectory(string path, System.Collections.Generic.List<PackageManagerItem> list, System.Collections.Generic.HashSet<string> protectedPackages, System.Collections.Generic.HashSet<string> types, System.Collections.Generic.HashSet<string> latestUids)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            DirectoryInfo di = new DirectoryInfo(path);
            FileInfo[] files = di.GetFiles("*.var", SearchOption.AllDirectories);
            DateTime now = DateTime.Now;
            foreach (var file in files)
            {
                string itemPath = file.FullName.Replace('\\', '/');
                // Make relative to current dir if needed, but FileManager paths are usually relative or absolute
                // Here we need to match the logic of previous implementation which was relative to VaM root
                string relativePath = itemPath;
                int idx = itemPath.IndexOf(path, StringComparison.OrdinalIgnoreCase);
                if (idx != -1) relativePath = itemPath.Substring(idx);

                string name = Path.GetFileNameWithoutExtension(itemPath);
                bool isProtected = protectedPackages.Contains(name);
                string type = DeterminePackageType(name);
                bool isLocked = m_LockedPackages.Contains(name);
                bool isLatest = latestUids.Contains(name);
                DateTime lwt = file.CreationTime;

                var deepDeps = FileManager.GetDependenciesDeep(name, 2);
                int depCount = deepDeps.Count;
                int loadedDepCount = 0;
                foreach (var dep in deepDeps)
                {
                    var resolved = FileManager.ResolveDependency(dep);
                    // A dependency is considered "loaded" if it is available and not in the AllPackages (unloaded) folder
                    if (resolved != null && !resolved.Path.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) 
                    {
                        loadedDepCount++;
                    }
                }

                list.Add(new PackageManagerItem {
                    Uid = name,
                    Path = relativePath,
                    Type = type,
                    Size = file.Length,
                    LastWriteTime = lwt,
                    AgeString = FormatAge(lwt, now),
                    DependencyCount = depCount,
                    LoadedDependencyCount = loadedDepCount,
                    Checked = false,
                    IsActive = isProtected,
                    Locked = isLocked,
                    IsLatest = isLatest
                });

                if (!string.IsNullOrEmpty(type)) 
                {
                    types.Add(type);
                    if (!m_PkgMgrCategoryCounts.ContainsKey(type)) m_PkgMgrCategoryCounts[type] = 0;
                    m_PkgMgrCategoryCounts[type]++;
                }
                
                m_PkgMgrCategoryCounts["All"]++;
                if (isProtected) m_PkgMgrCategoryCounts["Active"]++;
                if (isLocked) m_PkgMgrCategoryCounts["Locked (L)"]++;
                if (isLatest) m_PkgMgrCategoryCounts["Latest"]++;
                else m_PkgMgrCategoryCounts["Old Version"]++;
            }
        }

        void SortPackageManagerList()
        {
            System.Comparison<PackageManagerItem> comp = (a, b) => {
                int result = 0;
                switch (m_PkgMgrSortField)
                {
                    case "Category": result = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase); break;
                    case "Name": result = string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase); break;
                    case "Path": result = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
                    case "Size": result = a.Size.CompareTo(b.Size); break;
                    case "Age": result = a.LastWriteTime.CompareTo(b.LastWriteTime); break;
                    case "Deps": result = a.DependencyCount.CompareTo(b.DependencyCount); break;
                }
                return m_PkgMgrSortAscending ? result : -result;
            };
            m_AddonList.Sort(comp);
            m_AllList.Sort(comp);
        }

        void OpenPackageManagerWindow()
        {
            ScanPackageManagerPackages();
            m_ShowPackageManagerWindow = true;
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

        void DrawSpaceSaverWindow(int windowID)
        {
            var prevPaddingTop = m_StyleWindow.padding.top;
            m_StyleWindow.padding.top = 40;

            GUI.DragWindow(new Rect(0, 0, 10000, 45));

            // Draw header background
            var headerBgRect = new Rect(5, 5, m_SpaceSaverWindowRect.width - 10, 35);
            GUI.Box(headerBgRect, "", m_StyleSection);

            // Draw title in the header area (the padding gap)
            GUI.Label(new Rect(15, 12, m_SpaceSaverWindowRect.width - 60, 25), "Optimize Cache (Zstd)", m_StyleHeader);

            // Block game input when interacting with the window
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel || Event.current.type == EventType.MouseMove)
            {
                Input.ResetInputAxes();
            }

            // Force fully opaque content inside the window so it doesn't look "behind glass"
            var prevColor = GUI.color;
            var prevContent = GUI.contentColor;
            GUI.color = new Color(1, 1, 1, 1);
            GUI.contentColor = new Color(1, 1, 1, 1);

            GUILayout.BeginVertical(m_StylePanel);

            GUILayout.Space(2);

            // Explanation Area
            GUILayout.BeginVertical(m_StyleSection);
            GUILayout.Label("About Cache Optimization", m_StyleHeader);
            GUILayout.Space(5);
            GUILayout.Label("This tool migrates VaM's native texture cache (.vamcache) to a highly optimized Zstandard (Zstd) format.", m_StyleInfoCardText);
            GUILayout.Label("• Reduces disk usage by up to 80% (Lossless, no quality lost).", m_StyleInfoCardText);
            GUILayout.Label("• Scenes load faster by reducing disk I/O.", m_StyleInfoCardText);
            GUILayout.Label("• VPB plugin is required for compressed cache to work.", m_StyleInfoCardText);
            GUILayout.Label("• Safely archives textures; can be reverted back at any time.", m_StyleInfoCardText);
            GUILayout.Label("• Optimization speeds up over time as the cache warms.", m_StyleInfoCardText);
            GUILayout.Label("• You can minimize this window and continue using VaM while it runs in the background.", m_StyleInfoCardText);
            GUILayout.EndVertical();

            GUILayout.Space(10);

            var stats = ImageLoadingMgr.singleton.CurrentZstdStats;

            if (stats.IsRunning)
            {
                GUILayout.BeginVertical(m_StyleSection);
                GUILayout.Label("Operation in Progress...", m_StyleHeader);
                GUILayout.Space(5);
                
                float progress = stats.TotalFiles > 0 ? (float)stats.ProcessedFiles / stats.TotalFiles : 0f;
                GUILayout.Label(string.Format("Progress: {0}/{1} files ({2:P1})", stats.ProcessedFiles, stats.TotalFiles, progress), m_StyleInfoCardText);
                
                // Progress bar
                var rect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                GUI.Box(rect, "", m_StyleButton); // Background
                var progressRect = new Rect(rect.x, rect.y, rect.width * progress, rect.height);
                
                // Use solid color for internal progress bar too
                var barPrevColor = GUI.color;
                GUI.color = new Color(0.2f, 0.6f, 1f, 0.8f); // Blue
                GUI.DrawTexture(progressRect, Texture2D.whiteTexture);
                GUI.color = barPrevColor;

                GUILayout.Space(5);
                GUILayout.Label("Current: " + stats.CurrentFile, m_StyleInfoCardText);
                
                if (stats.TotalOriginalSize > stats.TotalCompressedSize) {
                    long diff = stats.TotalOriginalSize - stats.TotalCompressedSize;
                    string label = stats.IsDecompression ? "Space Lost: " : "Space Saved: ";
                    GUILayout.Label(label + FormatBytes(diff), m_StyleInfoCardText);
                }

                GUILayout.EndVertical();

                // Buttons outside the section
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Minimize", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_ShowSpaceSaverWindow = false;
                }
                if (GUILayout.Button("Stop Operation", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    ImageLoadingMgr.singleton.CancelBulkOperation();
                }
                GUILayout.EndHorizontal();
            }
            else if (stats.Completed)
            {
                GUILayout.BeginVertical(m_StyleSection);
                GUILayout.Label("Operation Report", m_StyleHeader);
                GUILayout.Space(5);
                GUILayout.Label("Status: " + stats.CurrentFile + "!", m_StyleInfoCardText);
                GUILayout.Label("Total Files Processed: " + stats.ProcessedFiles, m_StyleInfoCardText);
                if (stats.SkippedCount > 0)
                {
                    GUILayout.Label("Thumbnails Skipped: " + stats.SkippedCount, m_StyleInfoCardText);
                }
                GUILayout.Label("Total Uncompressed Size: " + FormatBytes(stats.TotalOriginalSize), m_StyleInfoCardText);
                GUILayout.Label("Total Compressed Size: " + FormatBytes(stats.TotalCompressedSize), m_StyleInfoCardText);
                GUILayout.Label(string.Format("Time Taken: {0:0.##} seconds", stats.Duration), m_StyleInfoCardText);
                
                if (stats.TotalOriginalSize > stats.TotalCompressedSize) {
                    long diff = stats.TotalOriginalSize - stats.TotalCompressedSize;
                    double percent = stats.TotalOriginalSize > 0 ? (double)diff / stats.TotalOriginalSize : 0;
                    
                    var reportPrevContentColor = GUI.contentColor;
                    if (stats.IsDecompression)
                    {
                        GUI.contentColor = new Color(1f, 0.2f, 0.2f); // Red for space lost
                        GUILayout.Label(string.Format("Total Space Lost: {0} ({1:P1})", FormatBytes(diff), percent), m_StyleHeader);
                    }
                    else
                    {
                        GUI.contentColor = new Color(0.2f, 1f, 0.2f); // Green for savings
                        GUILayout.Label(string.Format("Total Space Saved: {0} ({1:P1})", FormatBytes(diff), percent), m_StyleHeader);
                    }
                    GUI.contentColor = reportPrevContentColor;
                }

                if (stats.FailedCount > 0)
                {
                    GUILayout.Label("Failed Files: " + stats.FailedCount, m_StyleInfoCardText);
                }
                
                if (GUILayout.Button("Close Report", m_StyleButton, GUILayout.Height(30)))
                {
                    stats.Completed = false; // Reset to show start screen next time
                }
                GUILayout.EndVertical();
            }
            else if (m_CompressConfirmRequested)
            {
                GUILayout.BeginVertical(m_StyleSection);
                GUILayout.Label("Confirm Compression", m_StyleHeader);
                GUILayout.Space(5);
                GUILayout.Label("This will migrate VaM's native texture cache to optimized Zstd format.", m_StyleInfoCardText);
                GUILayout.Space(5);
                GUILayout.Label("• Current Cache Size: " + FormatBytes(m_CachedTexturesSize), m_StyleInfoCardText);
                long estimatedSavings = (long)(m_CachedTexturesSize * 0.7f);
                long estimatedCompressed = m_CachedTexturesSize - estimatedSavings;
                GUILayout.Label("• Estimated compressed cache = " + FormatBytes(estimatedCompressed) + " (space saved: ~" + FormatBytes(estimatedSavings) + ")", m_StyleInfoCardText);
                GUILayout.Label("• Compression is lossless; no texture quality will be lost.", m_StyleInfoCardText);
                GUILayout.Label("• The operation may take several minutes depending on cache size.", m_StyleInfoCardText);
                
                GUILayout.Space(20);

                GUILayout.BeginHorizontal();

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f, 1f); // More vibrant green
                if (GUILayout.Button("Confirm Compress", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_CompressConfirmRequested = false;
                    ImageLoadingMgr.singleton.StartBulkZstdCompression();
                }
                GUI.backgroundColor = prevBg;

                GUILayout.Space(10);

                var prevBgCancel = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.4f, 0.4f, 1f); // More vibrant red
                if (GUILayout.Button("Cancel", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_CompressConfirmRequested = false;
                }
                GUI.backgroundColor = prevBgCancel;

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            else if (m_DecompressConfirmRequested)
            {
                GUILayout.BeginVertical(m_StyleSection);
                GUILayout.Label("Confirm Decompression", m_StyleHeader);
                GUILayout.Space(5);
                GUILayout.Label("This will decompress all .zvamcache files back to VaM's native format.", m_StyleInfoCardText);
                GUILayout.Space(5);
                GUILayout.Label("• Current Compressed Size: " + FormatBytes(m_CachedVpbSize), m_StyleInfoCardText);
                long estimatedDecompressed = (long)(m_CachedVpbSize * 4.6f);
                long extraSpaceNeeded = estimatedDecompressed - m_CachedVpbSize;
                GUILayout.Label("• Estimated native cache = " + FormatBytes(estimatedDecompressed) + " (extra space needed: ~" + FormatBytes(extraSpaceNeeded) + ")", m_StyleInfoCardText);
                GUILayout.Label("• This should only be used if you plan to stop using the VPB plugin.", m_StyleInfoCardText);
                GUILayout.Label("• The operation may take several minutes.", m_StyleInfoCardText);
                
                GUILayout.Space(20);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_DecompressConfirmRequested = false;
                }

                GUILayout.Space(10);

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.4f, 0.4f, 1f); // More vibrant red
                if (GUILayout.Button("Confirm Decompress", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_DecompressConfirmRequested = false;
                    ImageLoadingMgr.singleton.StartBulkZstdDecompression();
                }
                GUI.backgroundColor = prevBg;

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            else
            {
                GUILayout.BeginVertical(m_StyleSection);
                
                // Buttons layout: Compress, Decompress, Close
                GUILayout.BeginHorizontal();
                
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f, 1f); // More vibrant green
                if (GUILayout.Button("Compress", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_CachedTexturesSize = GetTexturesFolderSize();
                    m_CompressConfirmRequested = true;
                }

                GUILayout.Space(10);

                GUI.backgroundColor = new Color(0.8f, 0.4f, 0.4f, 1f); // More vibrant red
                if (GUILayout.Button("Decompress", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_CachedVpbSize = GetVpbCacheFolderSize();
                    m_DecompressConfirmRequested = true;
                }
                
                GUILayout.Space(10);
                
                GUI.backgroundColor = prevBg;
                if (GUILayout.Button("Close", m_StyleButton, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    m_ShowSpaceSaverWindow = false;
                }

                GUILayout.EndHorizontal();
                
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
            {
                var contentHeight = GUILayoutUtility.GetLastRect().yMax;
                m_SpaceSaverWindowRect.height = contentHeight + m_StyleWindow.padding.bottom;
            }

            GUI.color = prevColor;
            GUI.contentColor = prevContent;
            m_StyleWindow.padding.top = prevPaddingTop;

            // Allow dragging from the header area (top 60 pixels)
            // Calling it at the end allows it to catch clicks not consumed by buttons
            GUI.DragWindow(new Rect(0, 0, 10000, 60));
        }

        private long GetVpbCacheFolderSize()
        {
            try
            {
                string path = GetCacheDir();
                if (!Directory.Exists(path)) return 0;
                
                long size = 0;
                List<string> fileList = new List<string>();
                FileManager.SafeGetFiles(path, "*.*", fileList);
                foreach (string file in fileList)
                {
                    size += new FileInfo(file).Length;
                }
                return size;
            }
            catch { return 0; }
        }

        private int GetVamCacheFileCount(string path, int threshold)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
                
                string[] files = Directory.GetFiles(path, "*.vamcache", SearchOption.TopDirectoryOnly);
                int count = 0;
                foreach (var file in files)
                {
                    // Check native .vamcachemeta resolution (<= threshold)
                    string metaPath = file + "meta";
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = SimpleJSON.JSON.Parse(File.ReadAllText(metaPath));
                            if (meta != null)
                            {
                                int w = meta["width"].AsInt;
                                int h = meta["height"].AsInt;
                                if (meta["isThumbnail"].AsBool || (w > 0 && w <= threshold && h > 0 && h <= threshold)) continue;
                            }
                        }
                        catch { }
                    }

                    count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private int GetVamCacheFileCount()
        {
             return GetVamCacheFileCount(MVR.FileManagement.CacheManager.GetTextureCacheDir(), Settings.Instance.ThumbnailThreshold.Value);
        }


        private long GetTexturesFolderSize()
        {
            try
            {
                string path = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
                
                long size = 0;
                string[] files = Directory.GetFiles(path, "*.vamcache", SearchOption.TopDirectoryOnly);
                int threshold = Settings.Instance.ThumbnailThreshold.Value;
                foreach (var file in files)
                {
                    string metaPath = file + "meta";
                    bool isThumb = false;
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = SimpleJSON.JSON.Parse(File.ReadAllText(metaPath));
                            if (meta != null)
                            {
                                int w = meta["width"].AsInt;
                                int h = meta["height"].AsInt;
                                if (meta["isThumbnail"].AsBool || (w > 0 && w <= threshold && h > 0 && h <= threshold)) isThumb = true;
                            }
                        }
                        catch { }
                    }

                    if (!isThumb)
                    {
                        size += new FileInfo(file).Length;
                        if (File.Exists(metaPath))
                            size += new FileInfo(metaPath).Length;
                    }
                }
                return size;
            }
            catch { return 0; }
        }

        private string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
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
        
        private void SetGroupChecked(string packageId, bool newState)
        {
            string groupId = FileManager.PackageIDToPackageGroupID(packageId);
            int count = 0;
            foreach (var item in m_AddonList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Checked = newState;
                    count++;
                }
            }
            foreach (var item in m_AllList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Checked = newState;
                    count++;
                }
            }
            m_PkgMgrStatusMessage = string.Format("{0} {1} items in group {2}", newState ? "Selected" : "Unselected", count, groupId);
            m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
        }

        private void CopyGroupNames(string packageId)
        {
            string groupId = FileManager.PackageIDToPackageGroupID(packageId);
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!names.Contains(item.Uid)) names.Add(item.Uid);
                }
            }
            foreach (var item in m_AllList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!names.Contains(item.Uid)) names.Add(item.Uid);
                }
            }
            if (names.Count > 0)
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", names.ToArray());
                m_PkgMgrStatusMessage = string.Format("Copied {0} group names to clipboard.", names.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
            }
        }

        private void CopyGroupDependenciesDeep(string packageId)
        {
            string groupId = FileManager.PackageIDToPackageGroupID(packageId);
            System.Collections.Generic.HashSet<string> allDeps = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            System.Collections.Generic.List<string> uids = new System.Collections.Generic.List<string>();
            foreach (var item in m_AddonList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!uids.Contains(item.Uid)) uids.Add(item.Uid);
                }
            }
            foreach (var item in m_AllList)
            {
                if (FileManager.PackageIDToPackageGroupID(item.Uid).Equals(groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!uids.Contains(item.Uid)) uids.Add(item.Uid);
                }
            }

            foreach (var uid in uids)
            {
                var deps = FileManager.GetDependenciesDeep(uid, 2);
                foreach (var dep in deps) allDeps.Add(dep);
            }

            if (allDeps.Count > 0)
            {
                string[] depArray = new string[allDeps.Count];
                allDeps.CopyTo(depArray);
                GUIUtility.systemCopyBuffer = string.Join("\n", depArray);
                m_PkgMgrStatusMessage = string.Format("Copied {0} unique deep dependencies for group to clipboard.", allDeps.Count);
                m_PkgMgrStatusTimer = Time.realtimeSinceStartup + 3f;
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
