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
        private bool m_ShowRemoveInvalidVarsInfo;
        private bool m_ShowRemoveOldVersionInfo;
        private bool m_ShowUninstallAllInfo;
        private float m_ExpandedHeight;
        float m_UIScale = 1;
        Rect m_Rect = new Rect(0, 0, 240, 50);

        private const float MinUiScale = 0.6f;
        private const float MaxUiScale = 2.4f;
        private const float MiniModeHeight = 50f;

        private GUIStyle m_TitleTagStyle;
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
            m_TexBtnPrimaryBgActive = MakeBorderedTex(12, 12, new Color(0.18f, 0.62f, 0.95f, 0.96f), new Color(1f, 1f, 1f, 0.18f), 1);

            m_TexWindowBorder = MakeTex(new Color(0.20f, 0.22f, 0.26f, borderAlpha));
            m_TexWindowBorderActive = MakeTex(new Color(0.12f, 0.50f, 0.85f, 0.88f));
            m_TexInfoCardBg = MakeBorderedTex(12, 12, new Color(0.12f, 0.14f, 0.18f, 0.82f), new Color(0.60f, 0.75f, 0.95f, 0.12f), 1);
            m_TexFpsBadgeBg = MakeBorderedTex(12, 12, new Color(0.10f, 0.11f, 0.13f, 0.90f), new Color(0.70f, 0.74f, 0.80f, 0.32f), 1);
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
            m_StylePanel.padding = new RectOffset(10, 10, 10, 10);
            m_StylePanel.margin = new RectOffset(6, 6, 6, 6);

            m_StyleSection = new GUIStyle(GUI.skin.box);
            m_StyleSection.normal.background = m_TexSectionBg;
            m_StyleSection.normal.textColor = Color.white;
            m_StyleSection.padding = new RectOffset(10, 10, 8, 8);
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

            m_StyleToggle = new GUIStyle(GUI.skin.toggle);
            m_StyleToggle.normal.textColor = new Color(0.92f, 0.94f, 0.96f, 1f);
            m_StyleToggle.hover.textColor = Color.white;
            m_StyleToggle.active.textColor = Color.white;
            m_StyleToggle.focused.textColor = Color.white;
            m_StyleToggle.alignment = TextAnchor.MiddleLeft;
            m_StyleToggle.wordWrap = false;
            m_StyleToggle.clipping = TextClipping.Clip;
            m_StyleToggle.padding = new RectOffset(20, 0, 2, 2);
            m_StyleToggle.contentOffset = new Vector2(0f, -1f);

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

            m_Rect = new Rect(UIPosition.x, UIPosition.y, 240, 50);
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
            //harmony.PatchAll(typeof(PatchHairLODSettings));

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

            RestrcitUIRect();
        }
        void Start()
        {
            //this.gameObject.name = "var_browser";
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
            MessageKit<string>.addObserver(MessageDef.UpdateLoading, OnPrograss);
            MessageKit.addObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
            MessageKit.addObserver(MessageDef.FileManagerInit, OnFileManagerInit);

        }
        void OnDisable()
        {
            MessageKit<string>.removeObserver(MessageDef.UpdateLoading, OnPrograss);
            MessageKit.removeObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
            MessageKit.removeObserver(MessageDef.FileManagerInit, OnFileManagerInit);
        }
        void OnFileManagerInit()
        {

        }

        string prograssText = "";
        float m_FpsSmoothedDelta = 0f;
        float m_FpsUpdateTimer = 0f;
        string m_FpsText = "";
        void OnPrograss(string text)
        {
            prograssText = text;
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
            if (!m_UIInited || !m_FileManagerInited)
            {
                m_FpsSmoothedDelta = 0f;
                m_FpsUpdateTimer = 0f;
                m_FpsText = "";
            }
            else
            {
                float unscaledDt = Time.unscaledDeltaTime;
                if (unscaledDt > 0f)
                {
                    if (m_FpsSmoothedDelta <= 0f)
                        m_FpsSmoothedDelta = unscaledDt;
                    else
                        m_FpsSmoothedDelta = Mathf.Lerp(m_FpsSmoothedDelta, unscaledDt, 0.08f);

                    m_FpsUpdateTimer += unscaledDt;
                    if (m_FpsUpdateTimer >= 0.25f)
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
                //if (MVR.Hub.HubBrowse.singleton != null)
                {
                    Init();
                    m_Inited = true;
                }
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

            //CreateHubBrowse();
            //CreateFileBrowser();
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

            GUI.DragWindow(new Rect(0, 0, m_Rect.width, 28));

            GUILayout.BeginVertical(m_StylePanel);

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<color=#00FF00><b>{0}</b></color> {1}", FileManager.s_InstalledCount, prograssText), m_StyleHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", m_StyleButtonSmall, GUILayout.Width(28)))
            {
		        m_UIScale = Mathf.Clamp(m_UIScale + 0.2f, MinUiScale, MaxUiScale);
                Settings.Instance.UIScale.Value = m_UIScale;
                RestrcitUIRect();
            }
            if (GUILayout.Button("-", m_StyleButtonSmall, GUILayout.Width(28)))
            {
		        m_UIScale = Mathf.Clamp(m_UIScale - 0.2f, MinUiScale, MaxUiScale);
                Settings.Instance.UIScale.Value = m_UIScale;
                RestrcitUIRect();
            }

            if (GUILayout.Button(MiniMode ? "Full" : "Mini", m_StyleButtonSmall, GUILayout.Width(44)))
            {
                SetMiniMode(!MiniMode);
            }
            GUILayout.EndHorizontal();

            if (MiniMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1.Scene", m_StyleButton))
                {
                    // Custom entries do not require installation.
                    m_FileBrowser.onlyInstalled = false;
                    ShowFileBrowser("Custom Scene", "json", "Saves/scene", true);
                }
                if (GUILayout.Button("2.Scene", m_StyleButton))
                {
                    ShowFileBrowser("Category Scene", "json", "Saves/scene");
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);
            GUILayout.Label(string.Format("Show/Hide: {0}", UIKey.keyPattern), m_StyleSubHeader);

            if (m_FileManagerInited && m_UIInited)
            {
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                    GUI.enabled = false;

                {
                    GUILayout.BeginVertical(m_StyleSection);
                    GUILayout.Label("System", m_StyleSubHeader);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Refresh", m_StyleButton))
                    {
                        Refresh();
                    }
                    if (GUILayout.Button("GC", m_StyleButton))
                    {
                        DAZMorphMgr.singleton.cache.Clear();
                        ImageLoadingMgr.singleton.ClearCache();

                        GC.Collect();
                        Resources.UnloadUnusedAssets();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    const float infoBtnWidth = 28f;
                    const float actionRowHeight = 34f;
                    const float optionRowHeight = 28f;
                    //const float optionBtnWidth = 44f;
                    const float optionIndent = 18f;

                    Settings.Instance.ReduceTextureSize.Value = GUILayout.Toggle(Settings.Instance.ReduceTextureSize.Value, "Downscale Textures", m_StyleToggle, GUILayout.Height(optionRowHeight));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("i", m_StyleButtonSmall, GUILayout.Width(infoBtnWidth), GUILayout.Height(optionRowHeight)))
                    {
                        m_ShowDownscaleTexturesInfo = !m_ShowDownscaleTexturesInfo;
                    }
                    GUILayout.EndHorizontal();
                    if (m_ShowDownscaleTexturesInfo)
                    {
                        GUILayout.BeginVertical(m_StyleInfoCard);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Downscale Textures", m_StyleInfoCardTitle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("x", m_StyleInfoClose, GUILayout.Width(18), GUILayout.Height(18)))
                        {
                            m_ShowDownscaleTexturesInfo = false;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(4);
                        GUILayout.Label("When enabled, images loaded by VaM are resized to smaller, power-of-two textures and cached.", m_StyleInfoCardText);
                        GUILayout.Space(2);
                        GUILayout.Label("This reduces VRAM/RAM usage and can improve performance, at the cost of some texture sharpness.", m_StyleInfoCardText);
                        GUILayout.Space(6);
                        GUILayout.Label("Min Resize: textures larger than this will be scaled down (2K/4K/8K).", m_StyleInfoCardText);
                        GUILayout.Label("Force all to minimum size: clamps both width/height to the minimum (more aggressive).", m_StyleInfoCardText);
                        GUILayout.Space(6);
                        GUILayout.Label("Notes: textures smaller than the limit are not upscaled; first load may take longer while the cache is created; web-loaded textures are ignored.", m_StyleInfoCardText);
                        GUILayout.EndVertical();
                    }

                    if (Settings.Instance.ReduceTextureSize.Value)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(optionIndent);
                        GUILayout.Label("Min Resize", GUILayout.Width(80));
                        int minTextureSize = Settings.Instance.MinTextureSize.Value;
                        int selectedIndex = (minTextureSize == 8192) ? 2 : (minTextureSize == 4096) ? 1 : 0;
                        selectedIndex = GUILayout.Toolbar(
                            selectedIndex,
                            new string[] { "2K", "4K", "8K" },
                            m_StyleButton,
                            GUILayout.Height(optionRowHeight)
                        );
                        Settings.Instance.MinTextureSize.Value = (selectedIndex == 2) ? 8192 : (selectedIndex == 1) ? 4096 : 2048;
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(optionIndent);
                        Settings.Instance.ForceTextureToMinSize.Value = GUILayout.Toggle(Settings.Instance.ForceTextureToMinSize.Value, "Force all to minimum size", m_StyleToggle, GUILayout.Height(optionRowHeight));
                        GUILayout.EndHorizontal();
                    }

                    //if (GUILayout.Button("HeapDump"))
                    //{
                    //    //UnityHeapDump.Create();
                    //    new UnityHeapCrawler.HeapSnapshotCollector().Start();
                    //}
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove Invalid Vars", m_StyleButton, GUILayout.ExpandWidth(true), GUILayout.Height(actionRowHeight)))
                    {
                        RemoveInvalidVars();
                    }
                    if (GUILayout.Button("i", m_StyleButton, GUILayout.Width(infoBtnWidth), GUILayout.Height(actionRowHeight)))
                    {
                        m_ShowRemoveInvalidVarsInfo = !m_ShowRemoveInvalidVarsInfo;
                        if (m_ShowRemoveInvalidVarsInfo)
                        {
                            m_ShowRemoveOldVersionInfo = false;
                            m_ShowUninstallAllInfo = false;
                        }
                    }
                    GUILayout.EndHorizontal();
                    if (m_ShowRemoveInvalidVarsInfo)
                    {
                        GUILayout.BeginVertical(m_StyleInfoCard);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Remove Invalid Vars", m_StyleInfoCardTitle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("x", m_StyleInfoClose, GUILayout.Width(18), GUILayout.Height(18)))
                        {
                            m_ShowRemoveInvalidVarsInfo = false;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(4);
                        GUILayout.Label("This triggers a deep refresh that removes invalid/broken package entries from the browser database.", m_StyleInfoCardText);
                        GUILayout.Space(2);
                        GUILayout.Label("It does not delete your files. It just rebuilds the index so items that no longer exist (or are unreadable) stop showing up.", m_StyleInfoCardText);
                        GUILayout.EndVertical();
                    }
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove Old Version", m_StyleButtonDanger, GUILayout.ExpandWidth(true), GUILayout.Height(actionRowHeight)))
                    {
                        RemoveOldVersion();
                    }
                    if (GUILayout.Button("i", m_StyleButtonDanger, GUILayout.Width(infoBtnWidth), GUILayout.Height(actionRowHeight)))
                    {
                        m_ShowRemoveOldVersionInfo = !m_ShowRemoveOldVersionInfo;
                        if (m_ShowRemoveOldVersionInfo)
                        {
                            m_ShowRemoveInvalidVarsInfo = false;
                            m_ShowUninstallAllInfo = false;
                        }
                    }
                    GUILayout.EndHorizontal();
                    if (m_ShowRemoveOldVersionInfo)
                    {
                        GUILayout.BeginVertical(m_StyleInfoCard);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Remove Old Version", m_StyleInfoCardTitle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("x", m_StyleInfoClose, GUILayout.Width(18), GUILayout.Height(18)))
                        {
                            m_ShowRemoveOldVersionInfo = false;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(4);
                        GUILayout.Label("This triggers a refresh that also tries to remove older duplicates of packages when newer versions exist.", m_StyleInfoCardText);
                        GUILayout.Space(2);
                        GUILayout.Label("Depending on your setup, this can move packages out of AddonPackages and/or clean up package lists. It may change what is considered installed.", m_StyleInfoCardText);
                        GUILayout.EndVertical();
                    }
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Unload All", m_StyleButtonPrimary, GUILayout.ExpandWidth(true), GUILayout.Height(actionRowHeight)))
                    {
                        UninstallAll();
                    }
                    if (GUILayout.Button("i", m_StyleButtonPrimary, GUILayout.Width(infoBtnWidth), GUILayout.Height(actionRowHeight)))
                    {
                        m_ShowUninstallAllInfo = !m_ShowUninstallAllInfo;
                        if (m_ShowUninstallAllInfo)
                        {
                            m_ShowRemoveInvalidVarsInfo = false;
                            m_ShowRemoveOldVersionInfo = false;
                        }
                    }
                    GUILayout.EndHorizontal();
                    if (m_ShowUninstallAllInfo)
                    {
                        GUILayout.BeginVertical(m_StyleInfoCard);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Unload All", m_StyleInfoCardTitle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("x", m_StyleInfoClose, GUILayout.Width(18), GUILayout.Height(18)))
                        {
                            m_ShowUninstallAllInfo = false;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.Space(4);
                        GUILayout.Label("This moves all .var files from AddonPackages into AllPackages (except ones marked AutoInstall).", m_StyleInfoCardText);
                        GUILayout.Space(2);
                        GUILayout.Label("Result: those packages will no longer be treated as installed/active add-ons. Nothing is deleted; files are just moved.", m_StyleInfoCardText);
                        GUILayout.EndVertical();
                    }
                    if (GUILayout.Button("Hub Browse", m_StyleButton))
                    {
                        OpenHubBrowse();
                    }

                    GUILayout.EndVertical();
                }
                GUI.enabled = true;

                GUILayout.BeginVertical(m_StyleSection);
                if (GUILayout.Button("Gallery", m_StyleButton))
                {
                    OpenGallery();
                }
                GUILayout.EndVertical();
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
                //if (m_IsMin)
                //{
                //    m_Rect = GUILayout.Window(0, m_Rect, DragWnd, "dragable area");
                //}
                //else
                bool show = true;
                // Hide this window while the preview/file browser UI is open.
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                {
                    show = false;
                }
                if (show)
                {
                    RestrcitUIRect();

                    EnsureStyles();
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

                    RestrcitUIRect();

                    var prevGuiColor = GUI.color;
                    var prevContentColor = GUI.contentColor;
                    var prevBackgroundColor = GUI.backgroundColor;
                    var prevEnabled = GUI.enabled;

                    bool isRepaint = (Event.current.type == EventType.Repaint);

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

                    if (isRepaint)
                    {
                        GUI.color = Color.white;
                        GUI.backgroundColor = Color.white;
                        GUI.contentColor = Color.white;
                        GUI.enabled = true;

                        const float headerInsetY = 4f;
                        const float headerHeight = 24f;

                        var tagRect = new Rect(m_Rect.x + 6f, m_Rect.y + headerInsetY, 150f, headerHeight);
                        GUI.color = new Color(1f, 1f, 1f, 1f);
                        GUI.contentColor = new Color(1f, 1f, 1f, 1f);
                        var startupSeconds = LogUtil.GetStartupSecondsForDisplay();
                        GUI.Label(tagRect, string.Format("VPB {0} ({1:0.0}s)", PluginVersionInfo.Version, startupSeconds), m_TitleTagStyle);

                        var titleStyle = new GUIStyle(GUI.skin.label);
                        titleStyle.font = GUI.skin.window.font;
                        titleStyle.fontSize = GUI.skin.window.fontSize;
                        titleStyle.fontStyle = GUI.skin.window.fontStyle;
                        titleStyle.normal.textColor = Color.white;
                        titleStyle.hover.textColor = Color.white;
                        titleStyle.active.textColor = Color.white;
                        titleStyle.focused.textColor = Color.white;
                        titleStyle.alignment = TextAnchor.MiddleLeft;
                        titleStyle.wordWrap = false;
                        titleStyle.clipping = TextClipping.Clip;
                        titleStyle.padding = new RectOffset(0, 0, 0, 0);

                        const float titleRightPadding = 6f;
                        var fpsText = m_FpsText;
                        float fpsWidth = 0f;
                        if (!string.IsNullOrEmpty(fpsText))
                        {
                            fpsWidth = titleStyle.CalcSize(new GUIContent(fpsText)).x;
                        }

                        const float titleLeftGap = 4f;
                        const float titleRightGap = 6f;

                        var leftEdge = tagRect.xMax + titleLeftGap;
                        var rightEdge = m_Rect.xMax - titleRightPadding;

                        var fpsRect = new Rect(rightEdge - fpsWidth, m_Rect.y + headerInsetY, fpsWidth, headerHeight);
                        var middleRect = new Rect(leftEdge, m_Rect.y + headerInsetY, Mathf.Max(0f, (fpsRect.xMin - titleRightGap) - leftEdge), headerHeight);

                        if (!string.IsNullOrEmpty(fpsText) && fpsRect.width > 4f)
                        {
                            var fpsStyle = new GUIStyle(titleStyle);
                            fpsStyle.alignment = TextAnchor.MiddleLeft;
                            GUI.color = new Color(1f, 1f, 1f, 1f);
                            GUI.contentColor = new Color(1f, 1f, 1f, 1f);
                            GUI.Label(fpsRect, fpsText, fpsStyle);
                        }

                        if (middleRect.width > 10f)
                        {
                            var dragText = "drag area";
                            var drawText = dragText;
                            var maxTitleWidth = middleRect.width;

                            var textSize = titleStyle.CalcSize(new GUIContent(drawText));
                            if (textSize.x > maxTitleWidth)
                            {
                                const string ellipsis = "...";
                                drawText = dragText;
                                while (drawText.Length > 0 && titleStyle.CalcSize(new GUIContent(drawText + ellipsis)).x > maxTitleWidth)
                                {
                                    drawText = drawText.Substring(0, drawText.Length - 1);
                                }
                                drawText = (drawText.Length > 0) ? (drawText + ellipsis) : ellipsis;
                            }

                            var dragStyle = new GUIStyle(titleStyle);
                            dragStyle.alignment = TextAnchor.MiddleCenter;
                            GUI.color = new Color(1f, 1f, 1f, 1f);
                            GUI.contentColor = new Color(1f, 1f, 1f, 1f);
                            GUI.Label(middleRect, drawText, dragStyle);
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

        void RestrcitUIRect()
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

            //Debug.Log("FileExists " + MVR.FileManagement.FileManager.FileExists(saveName));
            //Debug.Log("onStartScene " + Traverse.Create(SuperController.singleton).Field("onStartScene").GetValue());
            //Traverse.Create(SuperController.singleton).Method("LoadInternal", 
            //    new Type[3] {typeof(string),typeof(bool),typeof(bool) }, 
            //    new object[3] { saveName,false,false });

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
