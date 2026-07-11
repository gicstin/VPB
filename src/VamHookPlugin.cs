using BepInEx;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using Prime31.MessageKit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZstdNet;
using Leap.Unity;
using Leap.Unity.Infix;
using System.Text;
using VPB.src.util;
namespace VPB
{
    // Plugin metadata attribute: plugin ID, plugin name, plugin version (must be numeric)
    [BepInPlugin("VPB", "VPB", PluginVersionInfo.Version)]

    public partial class VamHookPlugin : BaseUnityPlugin // Inherits BaseUnityPlugin
    {
        private class FilteringLogHandler : ILogHandler
        {
            private readonly ILogHandler m_Inner;

            public FilteringLogHandler(ILogHandler inner)
            {
                m_Inner = inner;
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                try
                {
                    if (exception != null && ThirdPartyFixHook.ShouldSuppressCheesyFxNullReferenceLog(exception.ToString()))
                        return;
                }
                catch
                {
                }
                m_Inner.LogException(exception, context);
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                try
                {
                    if (logType == LogType.Error && !string.IsNullOrEmpty(format) && IsMissingAddonDependencyMessage(format, args))
                    {
                        return;
                    }

                    if ((logType == LogType.Error || logType == LogType.Exception || logType == LogType.Assert)
                        && !string.IsNullOrEmpty(format)
                        && ThirdPartyFixHook.ShouldSuppressCheesyFxUnityLog(FormatLogMessage(format, args), null, logType))
                    {
                        return;
                    }

                    if (logType == LogType.Log && !string.IsNullOrEmpty(format) && IsUnloadPersonMessage(format, args))
                    {
                        return;
                    }
                }
                catch
                {
                }
                m_Inner.LogFormat(logType, context, format, args);
            }

            private static string FormatLogMessage(string format, object[] args)
            {
                if (string.IsNullOrEmpty(format)) return "";
                if (args == null || args.Length == 0) return format;
                try
                {
                    return string.Format(format, args);
                }
                catch
                {
                    return format;
                }
            }

            private static bool IsUnloadPersonMessage(string format, object[] args)
            {
                string msg = format;
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        msg = string.Format(format, args);
                    }
                    catch
                    {
                        msg = format;
                    }
                }

                if (string.IsNullOrEmpty(msg)) return false;

                // VaM/Unity spam during unload; shows up as "[Info   : Unity Log] Unload Person ..." in BepInEx output.
                // The string passed through Unity's logger is typically just "Unload Person ...".
                if (msg.StartsWith("Unload Person ", StringComparison.OrdinalIgnoreCase)) return true;

                // Defensive: if the prefix is included in the formatted message for some reason.
                if (msg.IndexOf("Unload Person ", StringComparison.OrdinalIgnoreCase) >= 0
                    && msg.IndexOf("Unity Log", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                return false;
            }

            private static bool IsMissingAddonDependencyMessage(string format, object[] args)
            {
                string msg = format;
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        msg = string.Format(format, args);
                    }
                    catch
                    {
                        msg = format;
                    }
                }

                if (string.IsNullOrEmpty(msg)) return false;
                return msg.IndexOf("Missing addon package", StringComparison.OrdinalIgnoreCase) >= 0
                    && msg.IndexOf("depends on", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private KeyUtil UIKey;
        private KeyUtil GalleryKey;
        private KeyUtil CreateGalleryKey;
        private KeyUtil HubKey;
        private KeyUtil ClearConsoleKey;
        private KeyUtil BoneViewKey;
        private KeyUtil ToggleFixedGalleryKey;
        private Vector2 UIPosition;
        private bool MiniMode;



        private const int STD_OUTPUT_HANDLE = -11;

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public short wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FillConsoleOutputCharacter(IntPtr hConsoleOutput, char cCharacter, uint nLength, COORD dwWriteCoord, out uint lpNumberOfCharsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FillConsoleOutputAttribute(IntPtr hConsoleOutput, ushort wAttribute, uint nLength, COORD dwWriteCoord, out uint lpNumberOfAttrsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCursorPosition(IntPtr hConsoleOutput, COORD dwCursorPosition);

        private static void TryClearConsole()
        {
            try
            {
                IntPtr h = GetStdHandle(STD_OUTPUT_HANDLE);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    try { Console.Clear(); } catch { }
                    return;
                }

                if (!GetConsoleScreenBufferInfo(h, out CONSOLE_SCREEN_BUFFER_INFO csbi))
                {
                    try { Console.Clear(); } catch { }
                    return;
                }

                uint cellCount = (uint)(csbi.dwSize.X * csbi.dwSize.Y);
                COORD home = new COORD { X = 0, Y = 0 };
                FillConsoleOutputCharacter(h, ' ', cellCount, home, out _);
                FillConsoleOutputAttribute(h, (ushort)csbi.wAttributes, cellCount, home, out _);
                SetConsoleCursorPosition(h, home);
            }
            catch
            {
                try { Console.Clear(); } catch { }
            }
        }
        private bool m_PendingGc;
        private bool m_PendingAutoLoadRefresh;
        private bool m_PendingTargetListRefresh;
        private bool m_AtomAddedSubscribed;
        private int m_PendingVamCacheCount;
        private float m_ExpandedHeight;
        float m_UIScale = 1;
        Rect m_Rect = new Rect(0, 0, 220, 50);
        private bool m_BlockingUnderlyingUGUI;

        private const float MinUiScale = 0.6f;
        private const float MaxUiScale = 2.4f;
        private const float MiniModeHeight = 50f;

        private bool m_StylesInited;
        private Texture2D m_TexLoadingOverlay;
        private GUIStyle m_StyleHeader;

        public static VamHookPlugin singleton;
        private static bool s_FileManagerInitialRefreshCompleted;
        private static bool s_UiPendingWarnLogged;

        public static string CurrentScenePackageUid;

        private Harmony m_Harmony;

        internal Harmony StartupHarmony { get { return m_Harmony; } }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply(false, true);
            return tex;
        }

        private void EnsureStyles()
        {
            if (m_StylesInited)
                return;
            if (GUI.skin == null)
                return;

            m_TexLoadingOverlay = MakeTex(new Color(0f, 0f, 0f, 1f));
            m_StyleHeader = new GUIStyle(GUI.skin.label);
            m_StyleHeader.fontStyle = FontStyle.Bold;
            m_StyleHeader.normal.textColor = Color.white;
            m_StyleHeader.alignment = TextAnchor.MiddleLeft;
            m_StyleHeader.wordWrap = false;
            m_StylesInited = true;
        }

        public static string GetCacheDir()
        {
            // Move Zstd texture cache to a subfolder of native Textures cache
            string baseCache = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (string.IsNullOrEmpty(baseCache))
            {
                baseCache = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
            }
            string cacheDir = Path.Combine(baseCache, "Zstd");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
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

            try
            {
                var current = Debug.unityLogger.logHandler;
                if (!(current is FilteringLogHandler))
                {
                    Debug.unityLogger.logHandler = new FilteringLogHandler(current);
                }
            }
            catch
            {
            }

            // Explicitly initialize ZstdNet native library early
            try { ExternMethods.Initialize(); } catch { }

            LogUtil.ResetPluginSession();
            VPBLogger.Init();

            try
            {
                var asm = typeof(VamHookPlugin).Assembly;
                string asmPath = null;
                try { asmPath = asm != null ? asm.Location : null; } catch { }
                if (string.IsNullOrEmpty(asmPath))
                {
                    try { asmPath = this.Info != null ? this.Info.Location : null; } catch { }
                }
                if (string.IsNullOrEmpty(asmPath))
                {
                    try
                    {
                        string codeBase = asm != null ? asm.CodeBase : null;
                        if (!string.IsNullOrEmpty(codeBase))
                        {
                            asmPath = new Uri(codeBase).LocalPath;
                        }
                    }
                    catch { }
                }

                string asmVer = asm != null ? asm.GetName().Version.ToString() : "null";
                string asmTime = "null";
                try { if (!string.IsNullOrEmpty(asmPath)) asmTime = System.IO.File.GetLastWriteTime(asmPath).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                LogUtil.Log("[VPB] DLL loaded | ver=" + asmVer + " | ts=" + asmTime + " | path=" + (string.IsNullOrEmpty(asmPath) ? "null" : asmPath));
            }
            catch { }

            LogUtil.MarkPluginAwake();
            VamStartupProfiler.Milestone("plugin_awake");

            try { VPBTranslation.InitializeFromConfig(); } catch { }
            try { SubscribeLocaleChanged(); } catch { }

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
                if (Settings.Instance != null && Settings.Instance.LoadDependenciesWithPackage != null)
                    Settings.Instance.LoadDependenciesWithPackage.Value = true;
                if (Settings.Instance != null && Settings.Instance.ForceLatestDependencies != null)
                    Settings.Instance.ForceLatestDependencies.Value = true;
            }
            catch { }
            try { QuickMenuMigrateAnchorBaselineOnce(); } catch { }
            try
            {
                // Ensure dependency whitelist (Saves/PluginData/VPB/dependency_whitelist.json) is loaded early.
                var _ = DependencyWhitelistManager.Instance;
            }
            catch { }
            try { GlobalInfo.EnsurePluginDataInitialized(); } catch { }
            try { VpbBenchRunner.InitializeOnce(); } catch { }
            try
            {
                VPBConfig.ReloadFromDisk();
                var cfg = VPBConfig.Instance;
                LogUtil.Log("[VPBConfig] Awake loaded | path=" + cfg.ConfigPathForDebug + " | LastGalleryCategory=" + cfg.LastGalleryCategory + " | DragDropReplaceMode=" + cfg.DragDropReplaceMode);
            }
            catch { }

            UIKey = KeyUtil.Parse(Settings.Instance.UIKey.Value);
            GalleryKey = KeyUtil.Parse(Settings.Instance.GalleryKey.Value);
            CreateGalleryKey = KeyUtil.Parse(Settings.Instance.CreateGalleryKey.Value);
            HubKey = KeyUtil.Parse(Settings.Instance.HubKey.Value);
            ClearConsoleKey = KeyUtil.Parse(Settings.Instance.ClearConsoleKey.Value);
            BoneViewKey = KeyUtil.Parse(Settings.Instance.BoneViewKey.Value);
            ToggleFixedGalleryKey = KeyUtil.Parse(Settings.Instance.ToggleFixedGalleryKey.Value);
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
            if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
                Debug.Log("VPB hook start");

            m_Harmony = new Harmony("VPB_hook");

            try
            {
                m_Harmony.UnpatchAll("VPB_hook");
            }
            catch { }
            // Patch VaM/Harmony hook points.
            SuperControllerHook.PatchOptional(m_Harmony);
            m_Harmony.PatchAll(typeof(AtomHook));
            m_Harmony.PatchAll(typeof(HubResourcePackageHook));
            m_Harmony.PatchAll(typeof(SuperControllerHook));
            m_Harmony.PatchAll(typeof(MVRPluginManagerHook));
            VamStartupProfilerPatches.ApplySafe(m_Harmony);
            VamStartupOptimizationPatches.Apply(m_Harmony);
            m_Harmony.PatchAll(typeof(PatchAssetLoader));

            if (VPBConfig.Instance.IsDevMode)
            {
                Debug.Log("[VPB] Developer Mode is ENABLED");
            }

            GenericTextureHook.PatchAll(m_Harmony);
            DAZClothingHook.PatchAll(m_Harmony);
            DAZHairSwapHook.PatchAll(m_Harmony);
            VpbPerfHairHook.PatchAll(m_Harmony);
            ThirdPartyFixHook.PatchAll(m_Harmony);
            StateMachineDiagnostic.PatchAll(m_Harmony);

            // Zstd support is now handled by ZstdNet (auto-initialized)

            InitUpdater();
            try { VpbPerfController.Initialize(); } catch { }
        }

        // Lazily subscribe to atom-add once SuperController exists. Awake runs at BepInEx
        // load time (before SuperController.singleton is set), so subscribing there is a no-op.
        // Atom removal is already handled by the SuperController.RemoveAtom Harmony postfix.
        private void EnsureAtomAddedSubscription()
        {
            if (m_AtomAddedSubscribed) return;
            var sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                sc.onAtomAddedHandlers -= OnSceneAtomAdded;
                sc.onAtomAddedHandlers += OnSceneAtomAdded;
                m_AtomAddedSubscribed = true;
            }
            catch { }
        }

        // Coalesce same-frame atom-add bursts (e.g. importing several atoms) into a single
        // deferred refresh. A newly added Person atom is not reported as person-like by
        // GetAtoms() for a few frames, so we use the multi-frame coroutine refresh rather
        // than a single next-frame pass.
        private void OnSceneAtomAdded(Atom a)
        {
            m_PendingTargetListRefresh = true;
        }

        private bool _perfMonSilencerPatched;

        internal void EnsurePerfMonSilencerPatched()
        {
            if (_perfMonSilencerPatched || m_Harmony == null) return;
            try
            {
                PerfMonSilencer.Patch(m_Harmony);
                _perfMonSilencerPatched = true;
            }
            catch { }
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
            VpbSaveCacheSupport.RegisterPluginSaveWritePathsNoConfirm();

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            System.Threading.ThreadPool.QueueUserWorkItem((state) => {
                try {
                    int count = GetVamCacheFileCount(MVR.FileManagement.CacheManager.GetTextureCacheDir(), threshold);
                    m_PendingVamCacheCount = count;
                } catch {}
            });

            VamOnDemandLoader.SetMainThread();
            VamScanFilter.DiscoverVamInternals();
            var _ = ScanWhitelistManager.Instance; // eager init

            // Migrate legacy VPB hide markers to native VaM-compatible format
            System.Threading.ThreadPool.QueueUserWorkItem((state) => {
                try { PackageHidePrefs.MigrateAllLegacyHideMarkers(); } catch { }
            });

            AutoLoadALPackages();
            try { StartCoroutine(ApplyLateStartupProfilerPatchesCo()); } catch { }

            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.ConfigChanged += RefreshQuickMenuAssignableTransparency;
            }
            catch { }

            // Auto-create gallery pane on startup if enabled
            TryCreateAutoFixedGalleryPane();
        }

        // Start() runs once per process and does not re-run on scene reload (Hard Reset). Driven again from
        // Update() via m_AutoFixedGalleryPanePending so the fixed gallery pane is re-created after a reload.
        void TryCreateAutoFixedGalleryPane()
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.EnableAutoFixedGallery) return;
            if (!m_Inited) { Init(); m_Inited = true; }
            if (Gallery.singleton != null && Gallery.singleton.PanelCount == 0)
            {
                if (!m_GalleryCatsInited) InitGalleryCategories();
                Gallery.singleton.CreatePane();
            }
        }

        void AutoLoadALPackages()
        {
            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    if (!Directory.Exists("AllPackages")) return;

                    var alPackages = AutoLoadPackagesManager.Instance.GetAutoLoadPackages();
                    if (alPackages.Count == 0) return;

                    List<string> fileList = new List<string>();
                    FileManager.SafeGetFiles("AllPackages", "*.var", fileList);
                    string[] files = fileList.ToArray();
                    bool moved = false;

                    foreach (string file in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (alPackages.Contains(name))
                        {
                            string relativePath = file.Replace('\\', '/');
                            string targetPath = "AddonPackages" + relativePath.Substring("AllPackages".Length);

                            string targetDir = Path.GetDirectoryName(targetPath);
                            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                            if (!File.Exists(targetPath))
                            {
                                try
                                {
                                    File.Move(file, targetPath);
                                    moved = true;
                                    LogUtil.Log("[VPB] Auto-Loaded package: " + name);
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError("[VPB] Failed to auto-load " + name + ": " + ex.Message);
                                }
                            }
                        }
                    }

                    if (moved)
                    {
                        m_PendingAutoLoadRefresh = true;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Error during AutoLoadALPackages: " + ex.Message);
                }
            });
        }
        void OnDestroy()
        {
            try { VpbPerfController.Shutdown(); } catch { }
            try
            {
                var sc = SuperController.singleton;
                if (sc != null)
                    sc.onAtomAddedHandlers -= OnSceneAtomAdded;
                m_AtomAddedSubscribed = false;
            }
            catch { }
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.ConfigChanged -= RefreshQuickMenuAssignableTransparency;
            }
            catch { }
            try { UnsubscribeLocaleChanged(); } catch { }
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
            VPBLogger.Destroy();
        }
        // Called on (hard) restart as well.
        IEnumerator ApplyLateStartupProfilerPatchesCo()
        {
            yield return null;
            yield return null;
            try { VamStartupProfilerPatches.TryApplyLateOptionalPatches(m_Harmony); } catch { }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try { VamStartupProfiler.Milestone("Unity.sceneLoaded name=" + (scene != null ? scene.name : "") + " mode=" + mode); } catch { }
            LogUtil.LogWarning("OnSceneLoaded " + scene.name + " " + mode.ToString());
            if (m_Harmony != null)
            {
                ThirdPartyFixHook.PatchAll(m_Harmony);
                try { VamStartupProfilerPatches.TryApplyLateOptionalPatches(m_Harmony); } catch { }
            }
            if (mode == LoadSceneMode.Single)
            {
                m_Inited = false;
                IsFileManagerInited = false;
                m_UIInited = false;
                // Scene reload (Hard Reset) destroys VaM's FileManager and skips Start()/Awake() (plugin GameObject survives), so re-arm every cold-start one-shot below to replay the plugin session: native registry, registry-ready callback, READY latches, fixed gallery pane, return-to-scene-view, on-demand cache, vamX UI.
                s_FileManagerInitialRefreshCompleted = false;
                s_UiPendingWarnLogged = false;
                m_GalleryCatsInited = false;
                m_AutoFixedGalleryPanePending = true;
                LogUtil.ResetPluginSession();
                FileManager.ResetRegistryReadyNotifiedForSceneReload();
                SuperControllerHook.ResetReturnToSceneViewOnStartupForSceneReload();
                VamOnDemandLoader.ClearCache();
                VamStartupOptimizations.InvalidateVamXAbsentCache();
                try { VpbSaveCacheSupport.RegisterPluginSaveWritePathsNoConfirm(); } catch { }
            }
        }
        void OnEnable()
        {
            VPBLogger.Init(); // in case the plugin is ever partially reloaded for some reason
            MessageKit<string>.addObserver(MessageDef.UpdateLoading, OnProgress);
            MessageKit.addObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
            try { VpbProgressService.EnsureOverlay(); } catch { }

        }
        void OnDisable()
        {
            MessageKit<string>.removeObserver(MessageDef.UpdateLoading, OnProgress);
            MessageKit.removeObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
        }

        string m_ProgressText = "";
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
        /// <summary>True when gallery Settings is recording a plugin hotkey (block live shortcuts).</summary>
        private static bool IsGalleryPluginHotkeyCaptureActive()
        {
            try
            {
                var g = Gallery.singleton;
                var panels = g != null ? g.Panels : null;
                if (panels == null) return false;
                for (int i = 0; i < panels.Count; i++)
                {
                    var p = panels[i];
                    if (p != null && p.IsPluginHotkeyCaptureActive()) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Bare-key bindings (no Ctrl/Shift/Alt) must not fire while a text field has focus.</summary>
        private static bool ShouldSuppressBareKeyHotkey(KeyUtil ku)
        {
            if (ku == null) return false;
            if (IsGalleryPluginHotkeyCaptureActive()) return true;
            if (ku.supportKeys != null && ku.supportKeys.Count > 0) return false;
            return IsTypingInTextInput();
        }

        private static bool ShouldSuppressPluginHotkey(KeyUtil ku)
        {
            if (IsGalleryPluginHotkeyCaptureActive()) return true;
            return ShouldSuppressBareKeyHotkey(ku);
        }

        internal static bool ShouldSuppressBenchHotkey(KeyUtil ku)
        {
            return ShouldSuppressPluginHotkey(ku);
        }

        private static void DumpVisibleGalleryAtomSelection(string tag)
        {
            try
            {
                Gallery g = Gallery.singleton;
                if (g == null || g.Panels == null)
                {
                    LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] gallery not open.");
                    return;
                }
                for (int i = 0; i < g.Panels.Count; i++)
                {
                    GalleryPanel p = g.Panels[i];
                    if (p == null || !p.IsVisible) continue;
                    p.DumpSelectedGalleryItemAtoms(tag);
                    return;
                }
                LogUtil.Log("[VPB][ATOMS][dump:" + tag + "] gallery not visible.");
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB][ATOMS][dump:" + tag + "] failed: " + ex.Message);
            }
        }

        private static bool IsTypingInTextInput()
        {
            try
            {
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null
                    && es.currentSelectedGameObject.GetComponent<InputField>() != null)
                    return true;
            }
            catch { }

            // IMGUI TextField focus: not in all Unity reference assemblies; probe at runtime.
            try
            {
                var t = typeof(GUIUtility);
                const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var p = t.GetProperty("editingTextField", bf);
                if (p != null)
                {
                    if (p.GetValue(null, null) is bool pb && pb) return true;
                }
                else
                {
                    var f = t.GetField("editingTextField", bf);
                    if (f != null && f.GetValue(null) is bool fb && fb) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>Normal vertical fly speed (m/s at worldScale 1) for the E/C navigation keys.</summary>
        private const float VerticalMoveSpeed = 1.0f;
        /// <summary>Fast vertical fly speed used while Shift is held.</summary>
        private const float VerticalMoveSpeedFast = 2.0f;

        /// <summary>
        /// E = move up, C = move down. Translates the VaM navigation rig vertically so users can
        /// gain/lose altitude with the keyboard the same way WASD moves horizontally. Universal
        /// (desktop + VR), gated by <see cref="VPBConfig.VerticalMoveKeysEnabled"/> (ON by default).
        /// </summary>
        private void UpdateVerticalNavigationKeys()
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.VerticalMoveKeysEnabled) return;

            var sc = SuperController.singleton;
            if (sc == null || sc.navigationRig == null) return;

            // Don't hijack text entry (E/C are letters) or modifier combos (e.g. Ctrl+C copy).
            if (IsTypingInTextInput() || IsGalleryPluginHotkeyCaptureActive()) return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

            int dir = 0;
            if (Input.GetKey(KeyCode.E)) dir += 1;
            if (Input.GetKey(KeyCode.C)) dir -= 1;
            if (dir == 0) return;

            float scale = 1f;
            try { scale = sc.worldScale; } catch { }
            if (scale <= 0f) scale = 1f;

            bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = fast ? VerticalMoveSpeedFast : VerticalMoveSpeed;
            float delta = dir * speed * scale * Time.unscaledDeltaTime;
            sc.navigationRig.position += Vector3.up * delta;
        }

        void Update()
        {
            EnsureAtomAddedSubscription();
            VpbFrameRate.Tick();
            VpbPerfDiag.RefreshCache();
            VamStartupProfiler.RefreshCache();
            VamOnDemandLoader.DrainMainThreadQueue();
            VamOnDemandLoader.TickRefreshSimHold();
            Gallery.DrainPendingSqlIndexUpdate();
            LogUtil.DrainPostReadyQueue();
            CacheCleanupManager.CheckAutoFlush();
            UpdateUpdater();
            VpbPerfTelemetry.EmitSnapshotIfDue();
            VpbPerfDiag.EmitFrameSummaryIfDue();
            if (m_PendingGc)
            {
                // Avoid forcing unload/GC during scene load; it can interfere with VaM's load lifecycle
                // and cause visible scene/atom pops.
                if (!LogUtil.IsSceneLoading())
                {
                    m_PendingGc = false;
                    DAZMorphMgr.singleton.cache.Clear();
                    ImageLoadingMgr.singleton.ClearCache();
                    GC.Collect();
                    Resources.UnloadUnusedAssets();
                }
            }

            VdsLauncher.TryExecuteOnce();

            try { VpbBenchRunner.Tick(this, IsFileManagerInited); } catch { }

            if (m_PendingAutoLoadRefresh)
            {
                m_PendingAutoLoadRefresh = false;
                Refresh("autoload");
            }

            if (m_PendingTargetListRefresh)
            {
                m_PendingTargetListRefresh = false;
                // The coroutine waits for IsLoadingScene to clear before refreshing, so it
                // correctly handles both manual atom adds (which briefly set the loading flag
                // while the character loads) and full scene loads.
                try { SceneLoadingUtils.ScheduleGalleryTargetListRefresh(); } catch { }
            }

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

            // Fail-safe: ensure startup timer cannot run indefinitely if READY never occurs (UI init stuck).
            try
            {
                LogUtil.StartupWatchdogUpdate(IsFileManagerInited, m_UIInited);
                if (!m_UIInited && IsFileManagerInited && SuperController.singleton != null && !s_UiPendingWarnLogged)
                {
                    float waitUi = Time.realtimeSinceStartup - LogUtil.GetPluginSessionEngineStartSeconds();
                    if (waitUi > 30f)
                    {
                        s_UiPendingWarnLogged = true;
                        try
                        {
                            LogUtil.LogWarning("[VPB.Startup] UI init pending despite registry ready"
                                + " | wait_sec=" + waitUi.ToString("0")
                                + " | hubBrowse=" + (MVR.Hub.HubBrowse.singleton != null ? "1" : "0")
                                + " | fileBrowserWorldUI=" + (SuperController.singleton.fileBrowserWorldUI != null ? "1" : "0"));
                        }
                        catch { }
                    }
                }
            }
            catch { }

            UpdateUnderlyingUGUIBlockState();
            if (ClearConsoleKey != null && ClearConsoleKey.TestKeyDown() && !ShouldSuppressPluginHotkey(ClearConsoleKey))
            {
                TryClearConsole();
            }
            if (BoneViewKey != null && BoneViewKey.TestKeyDown() && !ShouldSuppressPluginHotkey(BoneViewKey))
            {
                BoneViewMode.Toggle();
            }
            if (ToggleFixedGalleryKey != null && ToggleFixedGalleryKey.TestKeyDown() && !ShouldSuppressPluginHotkey(ToggleFixedGalleryKey))
            {
                if (Gallery.singleton != null)
                {
                    var panels = Gallery.singleton.Panels;
                    if (panels != null && panels.Count > 0)
                    {
                        // Toggle the first fixed panel found
                        foreach (var panel in panels)
                        {
                            if (panel != null && panel.isFixedLocally)
                            {
                                panel.SetCollapsed(!panel.IsCollapsed);
                                break;
                            }
                        }
                    }
                }
            }
            OnDemandTextureCacheHook.Update();
            try { ImageLoadingMgr.singleton?.DrainPendingRuntimeZstdWrites(); } catch { }
            // Hotkeys
            if (m_Inited)
            {
                if (CreateGalleryKey.TestKeyDown() && !ShouldSuppressPluginHotkey(CreateGalleryKey))
                {
                    OpenCreateGallery();
                }
                if (GalleryKey.TestKeyDown() && !ShouldSuppressPluginHotkey(GalleryKey))
                    ToggleGalleryVisibility();
                else if (UIKey.TestKeyDown() && !ShouldSuppressPluginHotkey(UIKey) && !UIKey.IsSame(GalleryKey))
                    ToggleGalleryVisibility();
            }

            if (m_Inited && IsFileManagerInited)
            {
                if (HubKey.TestKeyDown() && !ShouldSuppressPluginHotkey(HubKey))
                {
                    OpenHubBrowse();
                }
            }

            try { UpdateVerticalNavigationKeys(); } catch { }

            // Ctrl+Shift+U: dump live CUA geometry (control / linked-bone / mesh bounds) for comparing a
            // normal scene load against an import. Diagnostic only.
            try
            {
                if (Input.GetKeyDown(KeyCode.U)
                    && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    && !IsTypingInTextInput())
                {
                    VPB.src.util.CUAAtomImporter.DumpLiveCUAGeometry("hotkey");
                }
            }
            catch { }

            // Ctrl+Shift+P: dump the last imported pose (source foot/toe/hand/root control specs vs live applied
            // controller states + toe/foot bone angles) to diagnose toes curling / hand drift. Diagnostic only.
            try
            {
                if (Input.GetKeyDown(KeyCode.P)
                    && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    && !IsTypingInTextInput())
                {
                    VPB.src.util.PoseImportDiagnostics.Dump("hotkey");
                }
            }
            catch { }

            // Ctrl+Shift+L: dump atoms listed in the gallery's selected scene/preset, not live scene state.
            try
            {
                if (Input.GetKeyDown(KeyCode.L)
                    && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    && !IsTypingInTextInput())
                {
                    DumpVisibleGalleryAtomSelection("hotkey");
                }
            }
            catch { }

            if (!m_Inited)
            {
                Init();
            }
            if (!m_UIInited)
            {
                if (IsFileManagerInited && SuperController.singleton != null)
                {
                    try
                    {
                        if (MVR.Hub.HubBrowse.singleton != null)
                            CreateHubBrowse();
                    }
                    catch (Exception ex)
                    {
                        try { LogUtil.LogWarning("[VPB.Startup] CreateHubBrowse skipped: " + ex.Message); } catch { }
                    }
                    try { CreateFileBrowser(); } catch { }
                    m_UIInited = true;
                    LogUtil.LogReadyOnce("UI initialized");
                }
            }

            if (m_AutoFixedGalleryPanePending && m_UIInited && Gallery.singleton != null)
            {
                m_AutoFixedGalleryPanePending = false;
                try { TryCreateAutoFixedGalleryPane(); }
                catch (Exception ex) { try { LogUtil.LogWarning("[VPB.Startup] auto gallery pane re-create skipped: " + ex.Message); } catch { } }
            }

            if (!m_QuickMenuButtonInited)
            {
                CreateQuickMenuButton();
            }
            else if ((m_CloseAllButtonGO == null || m_BringFrontButtonGO == null) && SuperController.singleton != null && SuperController.singleton.mainHUD != null)
            {
                // Handle hot-reload updates: older sessions may have created the quick menu canvas
                // before these buttons existed.
                m_QuickMenuButtonInited = false;
                CreateQuickMenuButton();
            }
            else if (m_ShowHideButtonGO != null && Gallery.singleton != null)
            {
                int count = Gallery.singleton.PanelCount;
                bool shouldShow = count > 0;
                if (m_ShowHideButtonGO.activeSelf != shouldShow) m_ShowHideButtonGO.SetActive(shouldShow);
                if (m_CloseAllButtonGO != null && m_CloseAllButtonGO.activeSelf != shouldShow) m_CloseAllButtonGO.SetActive(shouldShow);
                if (m_BringFrontButtonGO != null && m_BringFrontButtonGO.activeSelf != shouldShow) m_BringFrontButtonGO.SetActive(shouldShow);

                if (shouldShow && m_ShowHideButton != null)
                {
                    m_ShowHideButtonLastCount = count;
                    m_ShowHideButton.label = VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide") + " (" + count + ")";
                }

                // Keep icon visuals current (Show/Hide toggles eye icon based on visibility).
                try
                {
                    if (m_QuickMenuGridButtons != null)
                    {
                        for (int i = 0; i < m_QuickMenuGridButtons.Length; i++)
                        {
                            var a = QuickMenuGetSlotAction(i);
                            bool requiresGallery = a == QuickMenuAssignableAction.ShowHide ||
                                                   a == QuickMenuAssignableAction.BringFront ||
                                                   a == QuickMenuAssignableAction.CloseAll ;
                            if (requiresGallery)
                            {
                                var go = m_QuickMenuGridButtons[i];
                                if (go != null && go.activeSelf != shouldShow) go.SetActive(shouldShow);
                            }

                            if (a == QuickMenuAssignableAction.ShowHide ||
                                a == QuickMenuAssignableAction.ReplaceAddToggle ||
                                a == QuickMenuAssignableAction.AutoHideGallery ||
                                a == QuickMenuAssignableAction.ShowHiddenPackages ||
                                a == QuickMenuAssignableAction.FpsCounter ||
                                i == m_QuickMenuEditSlotIdx ||
                                i == m_QuickMenuPageToggleSlotIdx)
                                QuickMenuRefreshSlotVisual(i);
                        }
                    }
                }
                catch { }
            }

            // VR wrist watch: move the grid onto the opposite controller while the VAM menu is open.
            try { QuickMenuUpdateVrWatch(); } catch { }

            // Live preview: reposition the quick-menu grid when the anchor setting changes.
            // (Do this every frame; the helper is internally throttled.)
            try { QuickMenuUpdateGridLayoutLive(); } catch { }
        }


        bool AutoInstalled = false;
        // Once per process: install every package listed in AutoInstall.txt (AllPackages → AddonPackages).
        // Also install dependencies for local Saves/scene JSON rows flagged with VPB_LS:… keys (scene file stays put).
        // Toggling AutoInstall in the UI only updates that list; it does not move files until this runs.
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
            try
            {
                if (LocalSceneGallerySupport.InstallDependenciesForAllAutoMarkedLocalScenes())
                    flag = true;
            }
            catch { }
            if (flag)
            {
                FileManagerBridge.Refresh("autoinstall", RefreshScope.Both, init: true);
            }
        }

        bool m_Inited = false;
        bool m_UIInited = false;
        bool m_AutoFixedGalleryPanePending = false;
        bool m_QuickMenuButtonInited = false;
        void Init()
        {
            if (m_FileManager == null)
            {
                var child = Tools.AddChild(this.gameObject);
                child.name = "VarBrowser_Base";
                m_FileManager = child.AddComponent<FileManager>();
                child.AddComponent<VPB.CustomImageLoaderThreaded>();
                child.AddComponent<VPB.ImageLoadingMgr>();
                child.AddComponent<VPB.Gallery>();
                LogUtil.Log("Base components initialized on " + child.name);
                FileManager.RegisterRegistryReadyHandler(() =>
                {
                    if (!IsFileManagerInited)
                    {
                        IsFileManagerInited = true;
                        try { LogUtil.Log("[VPB.Startup] IsFileManagerInited=true (registry ready, deep scan may still run)"); } catch { }
                    }
                    TryAutoInstall();
                });
                FileManager.RegisterRefreshHandler(() =>
                {
                    LogUtil.RegisterPostReadyOnce(() => VarPackageMgr.singleton.Refresh());
                });
            }

            System.Diagnostics.Stopwatch initSw = System.Diagnostics.Stopwatch.StartNew();
            VamStartupProfiler.Milestone("vpb_init_begin");
            LogUtil.Log("VPB Init start");
            System.Diagnostics.Stopwatch cacheInitSw = System.Diagnostics.Stopwatch.StartNew();
            VarPackageMgr.singleton.Init();
            cacheInitSw.Stop();
            VamStartupProfiler.AddPhaseMs("vpb_varpackagemgr_init", cacheInitSw.Elapsed.TotalMilliseconds);
            if (!s_FileManagerInitialRefreshCompleted)
            {
                System.Diagnostics.Stopwatch refreshSw = System.Diagnostics.Stopwatch.StartNew();
                FileManagerBridge.Refresh("init", RefreshScope.Both, init: true, clean: true);
                refreshSw.Stop();
                VamStartupProfiler.AddPhaseMs("vpb_refresh_init_call", refreshSw.Elapsed.TotalMilliseconds);
                LogUtil.Log("FileManager.Refresh call took " + refreshSw.ElapsedMilliseconds + "ms");
                s_FileManagerInitialRefreshCompleted = true;
            }
            else
            {
                IsFileManagerInited = true;
            }
            initSw.Stop();
            VamStartupProfiler.AddPhaseMs("vpb_init_total", initSw.Elapsed.TotalMilliseconds);
            VamStartupProfiler.Milestone("vpb_init_end");
            LogUtil.Log("VPB Init end in " + initSw.ElapsedMilliseconds + "ms");
            m_Inited = true;
        }
        void CreateFileBrowser()
        {
            if (m_FileBrowser == null)
            {
                if (SuperController.singleton == null || SuperController.singleton.fileBrowserWorldUI == null)
                {
                    return;
                }

                var go = SuperController.singleton.fileBrowserWorldUI.gameObject;
                if (go == null)
                {
                    return;
                }

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
        Dictionary<GalleryPanel, bool> m_GalleryPanelsVisibleBeforeHub;

        void CaptureAndHideGalleryForHub()
        {
            if (m_GalleryPanelsVisibleBeforeHub != null) return;
            if (Gallery.singleton == null) return;
            if (Gallery.singleton.Panels == null) return;
            m_GalleryPanelsVisibleBeforeHub = new Dictionary<GalleryPanel, bool>();
            foreach (var p in Gallery.singleton.Panels)
            {
                if (p == null) continue;
                bool wasVisible = false;
                try { wasVisible = p.IsVisible; } catch { wasVisible = false; }
                if (!m_GalleryPanelsVisibleBeforeHub.ContainsKey(p)) m_GalleryPanelsVisibleBeforeHub.Add(p, wasVisible);
                if (wasVisible)
                {
                    try { p.Hide(); } catch { }
                }
            }
        }

        void RestoreGalleryAfterHub()
        {
            var restore = m_GalleryPanelsVisibleBeforeHub;
            m_GalleryPanelsVisibleBeforeHub = null;
            if (restore == null) return;
            foreach (var kv in restore)
            {
                var p = kv.Key;
                if (p == null) continue;
                if (!kv.Value) continue;
                try
                {
                    p.Show(p.GetTitle(), p.GetCurrentExtension(), p.GetCurrentPath());
                }
                catch { }
            }
        }

        void CreateHubBrowse()
        {
            LogUtil.LogVerboseUi("VPB CreateHubBrowse");
            var _hubSw = System.Diagnostics.Stopwatch.StartNew();
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

            var prevPreShow = m_HubBrowse.preShowCallbacks;
            m_HubBrowse.preShowCallbacks = () =>
            {
                try { prevPreShow?.Invoke(); } catch { }
                CaptureAndHideGalleryForHub();
            };

            var prevOnHide = m_HubBrowse.onHideCallbacks;
            m_HubBrowse.onHideCallbacks = () =>
            {
                RestoreGalleryAfterHub();
                try { prevOnHide?.Invoke(); } catch { }
                if (Gallery.singleton != null)
                {
                    foreach (var panel in Gallery.singleton.Panels)
                    {
                        if (panel != null && panel.IsVisible)
                        {
                            try { panel.RefreshFiles(); } catch { }
                        }
                    }
                }
            };

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
            LogUtil.Log("CreateHubBrowse took " + _hubSw.ElapsedMilliseconds + "ms");
        }

        Canvas m_QuickMenuCanvas;
        GameObject m_ShowHideButtonGO;
        UIDynamicButton m_ShowHideButton;
        int m_ShowHideButtonLastCount = -1;
        GameObject m_CreateGalleryButtonGO;
        GameObject m_CloseAllButtonGO;
        GameObject m_BringFrontButtonGO;
        RectTransform m_CreateGalleryButtonRT;
        RectTransform m_CloseAllButtonRT;
        RectTransform m_BringFrontButtonRT;
        RectTransform m_ShowHideButtonRT;
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

                bool isVR = XrUtils.IsVrActive();

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

                EnsureQuickMenuGridArrays();

                // Load quick menu icons
                Color tint = Color.white;
                m_QmIconCreate   = UI.LoadIconSprite("vpb_icons/gallery_clone.png", tint);
                m_QmIconEyeOn    = UI.LoadIconSprite("vpb_icons/eye.png", tint);
                m_QmIconEyeOff   = UI.LoadIconSprite("vpb_icons/eye_off.png", tint);
                m_QmIconBringFront = UI.LoadIconSprite("vpb_icons/focus_centered.png", tint);
                m_QmIconCloseAll = UI.LoadIconSprite("vpb_icons/close.png", tint);
                m_QmIconEditPlus = UI.LoadIconSprite("vpb_icons/settings_plus.png", tint);
                m_QmIconEditOff  = UI.LoadIconSprite("vpb_icons/settings_off.png", tint);
                m_QmIconAssignEmpty = UI.LoadIconSprite("vpb_icons/button_placeholder.png", tint);
                m_QmIconSave   = UI.LoadIconSprite("vpb_icons/gallery_save.png", tint);
                // Random icon: dice (only action using dice icons).
                m_QmIconRandom = UI.LoadIconSprite("vpb_icons/dice_1.png", tint) ?? UI.LoadIconSprite("vpb_icons/random.png", tint);
                m_QmIconHexAppearance = UI.LoadIconSprite("vpb_icons/hexagon_a.png", tint) ?? m_QmIconRandom;
                m_QmIconHexPose = UI.LoadIconSprite("vpb_icons/hexagon_p.png", tint) ?? m_QmIconRandom;
                m_QmIconHexScene = UI.LoadIconSprite("vpb_icons/hexagon_s.png", tint) ?? m_QmIconRandom;
                m_QmIconHexSkin = UI.LoadIconSprite("vpb_icons/hexagon_k.png", tint) ?? m_QmIconRandom;
                m_QmIconHexSubScene = UI.LoadIconSprite("vpb_icons/hexagon_l.png", tint) ?? m_QmIconRandom;
                m_QmIconHexHair = UI.LoadIconSprite("vpb_icons/hexagon_h.png", tint) ?? m_QmIconRandom;
                m_QmIconHexClothing = UI.LoadIconSprite("vpb_icons/hexagon_c.png", tint) ?? m_QmIconRandom;
                m_QmIconUndo   = UI.LoadIconSprite("vpb_icons/undo.png", tint);
                m_QmIconRedo   = UI.LoadIconSprite("vpb_icons/redo.png", tint);
                m_QmIconHub    = UI.LoadIconSprite("vpb_icons/hub.png", tint);
                m_QmIconCleanup = UI.LoadIconSprite("vpb_icons/cleanup.png", tint);
                m_QmIconReplace = UI.LoadIconSprite("vpb_icons/gallery_replace.png", tint);
                m_QmIconAdd     = UI.LoadIconSprite("vpb_icons/gallery_add.png", tint);
                m_QmIconTargetAtom = UI.LoadIconSprite("vpb_icons/gallery_target.png", tint);
                m_QmIconNavNext = UI.LoadIconSprite("vpb_icons/nav_next.png", tint);
                m_QmIconNavPrev = UI.LoadIconSprite("vpb_icons/nav_prev.png", tint);
                m_QmIconSwitchHand = UI.LoadIconSprite("vpb_icons/switch_horizontal.png", tint);
                m_QmIconHistory     = UI.LoadIconSprite("vpb_icons/book.png", tint);
                m_QmIconPerfModeOn  = UI.LoadIconSprite("vpb_icons/auto.png", tint);
                m_QmIconPerfModeOff = UI.LoadIconSprite("vpb_icons/auto_off.png", tint);
                m_QmIconRemoveClothing = UI.LoadIconSprite("vpb_icons/remove_clothing.png", tint);
                m_QmIconRemoveHair     = UI.LoadIconSprite("vpb_icons/remove_hair.png", tint);
                m_QmIconImportSidebar  = UI.LoadIconSprite("vpb_icons/arrow_merge.png", tint);
                m_QmIconStarOn  = UI.LoadIconSprite("vpb_icons/star.png", tint);
                m_QmIconStarOff = UI.LoadIconSprite("vpb_icons/star_off.png", tint);
                m_QmIconCategorySkin = UI.LoadIconSprite("vpb_icons/c_skin.png", tint);
                m_QmIconPerfStepUp   = UI.LoadIconSprite("vpb_icons/photo_up.png", tint);
                m_QmIconPerfStepDown = UI.LoadIconSprite("vpb_icons/photo_down.png", tint);
                m_QmIconCompressCache = GalleryPanel.LoadCompressCacheIconSprite(tint);
                m_QmIconAutoHideOff = UI.LoadIconSprite("vpb_icons/auto_hide_off.png", tint);
                m_QmIconAutoHideOn  = UI.LoadIconSprite("vpb_icons/auto_hide_on.png",  tint);
                m_QmIconShowHiddenOff = UI.LoadIconSprite("vpb_icons/show_hidden_off.png", tint);
                m_QmIconShowHiddenOn  = UI.LoadIconSprite("vpb_icons/show_hidden.png",     tint);
                m_QmIconOpenCategory = UI.LoadIconSprite("vpb_icons/gallery_category.png", tint);
                m_QmIconCategoryScenes = UI.LoadIconSprite("vpb_icons/c_scene.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategorySubScenes = UI.LoadIconSprite("vpb_icons/c_subscene.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryClothing = UI.LoadIconSprite("vpb_icons/c_clothing.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryHair = UI.LoadIconSprite("vpb_icons/c_hair.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryPose = UI.LoadIconSprite("vpb_icons/c_pose.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryAppearance = UI.LoadIconSprite("vpb_icons/c_appearance.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryPlugins = UI.LoadIconSprite("vpb_icons/c_plugins.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconCategoryAll = UI.LoadIconSprite("vpb_icons/c_all.png", tint) ?? m_QmIconOpenCategory;
                m_QmIconPages = new Sprite[]
                {
                    UI.LoadIconSprite("vpb_icons/page_0.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_1.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_2.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_3.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_4.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_5.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_6.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_7.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_8.png", tint),
                    UI.LoadIconSprite("vpb_icons/page_9.png", tint),
                };
                m_QmIconPerfLevels = new Sprite[]
                {
                    UI.LoadIconSprite("vpb_icons/level_0.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_1.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_2.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_3.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_4.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_5.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_6.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_7.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_8.png", tint),
                    UI.LoadIconSprite("vpb_icons/level_9.png", tint),
                };

                // Anchor center used for layout; positions are kept live in Update().
                Vector2 createCenter = isVR ? Settings.Instance.QuickMenuCreateGalleryPosVR.Value : Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
                Vector2 rootTopLeft = createCenter + new Vector2(-QuickMenuAnchorOldButtonW * 0.5f, QuickMenuAnchorOldButtonH * 0.5f);

                // New grid: square buttons + uniform gaps.
                float cell = QuickMenuGridCell;
                Vector2 popupOffset = new Vector2(260f, -20f);

                // Top-left slot center (slot 0). Every other slot is offset from this.
                Vector2 slot0Center = new Vector2(
                    rootTopLeft.x + (QuickMenuGridButtonSize * 0.5f),
                    rootTopLeft.y - (QuickMenuGridButtonSize * 0.5f)
                );

                // Load persisted page configs (or seed defaults on first run).
                QuickMenuEnsureDefaultsAndLoadFromConfig();

                // Tooltip UI (positioned by QuickMenuApplyGridLayoutFromAnchor / live updates)
                QuickMenuEnsureTooltipUI();
                try { QuickMenuApplyGridLayoutFromAnchor(createCenter); } catch { }

                // Core slot indices are loaded from persisted config in QuickMenuEnsureDefaultsAndLoadFromConfig().

                bool initialShouldShow = Gallery.singleton != null && Gallery.singleton.PanelCount > 0;

                for (int i = 0; i < QuickMenuGridSlotCount; i++)
                {
                    // Build a clean square button (do NOT use VaM's configurableButtonPrefab; it can enforce wide layouts).
                    GameObject go = new GameObject("VPB_QM_Slot_" + i);
                    go.transform.SetParent(m_QuickMenuCanvas.transform, false);
                    m_QuickMenuGridButtons[i] = go;

                    RectTransform rt = go.AddComponent<RectTransform>();
                    m_QuickMenuGridButtonRTs[i] = rt;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(QuickMenuGridButtonSize, QuickMenuGridButtonSize);
                    int col = i % QuickMenuGridCols;
                    int row = i / QuickMenuGridCols;
                    rt.anchoredPosition = slot0Center + new Vector2(col * cell, -row * cell);

                    Image img = go.AddComponent<Image>();
                    Color normalBackdrop = new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    img.color = normalBackdrop;
                    m_QuickMenuGridBackdropImages[i] = img;

                    Button btn = go.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    btn.navigation = new Navigation { mode = Navigation.Mode.None };
                    m_QuickMenuGridUnityButtons[i] = btn;

                    int idxCopy = i;

                    var hover = go.AddComponent<QuickMenuSquareHover>();
                    hover.target = img;
                    hover.normal = normalBackdrop;
                    hover.hover = new Color(0.35f, 0.35f, 0.35f, 0.75f);

                    var qmHb = go.AddComponent<UIHoverBorder>();
                    Color qmBorderCol = new Color(1f, 1f, 0f, 1f);
                    try { if (VPBConfig.Instance != null) qmBorderCol = VPBConfig.Instance.GetGalleryGridBorderColor(); } catch { }
                    qmHb.hoverColor = qmBorderCol;
                    qmHb.ApplyBorderSettings();

                    var tip = go.AddComponent<QuickMenuTooltipHoverHandler>();
                    tip.owner = this;
                    tip.slotIdx = idxCopy;

                    var drop = go.AddComponent<QuickMenuAssignDropTargetHandler>();
                    drop.owner = this;
                    drop.slotIdx = idxCopy;

                    QuickMenuAttachTargetAtomDragSource(go, idxCopy);

                    btn.onClick.AddListener(() =>
                    {
                        if (idxCopy == m_QuickMenuEditSlotIdx)
                        {
                            m_QuickMenuEditMode = !m_QuickMenuEditMode;
                            if (m_QuickMenuEditMode) QuickMenuShowAssignPaletteForEditMode();
                            else
                            {
                                QuickMenuHideAssignPopup();
                                QuickMenuClearTooltip(null);
                            }
                            for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
                            return;
                        }

                        if (idxCopy == m_QuickMenuPageToggleSlotIdx)
                        {
                            QuickMenuChangePage(+1);
                            for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
                            return;
                        }

                        if (m_QuickMenuEditMode)
                        {
                            // Page navigation slots stay functional in edit mode so the user
                            // can flip pages while assigning buttons on other pages.
                            var editAct = QuickMenuGetSlotAction(idxCopy);
                            if (editAct == QuickMenuAssignableAction.PageNext)
                            {
                                QuickMenuChangePage(+1);
                                for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
                                return;
                            }
                            if (editAct == QuickMenuAssignableAction.PagePrev)
                            {
                                QuickMenuChangePage(-1);
                                for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
                                return;
                            }

                            Vector2 pos = (m_QuickMenuGridButtonRTs != null && m_QuickMenuGridButtonRTs[idxCopy] != null)
                                ? m_QuickMenuGridButtonRTs[idxCopy].anchoredPosition
                                : createCenter;
                            QuickMenuShowAssignPopup(idxCopy, pos + popupOffset);
                            return;
                        }

                        var act = QuickMenuGetSlotAction(idxCopy);
                        // Save opens a submenu; remember which slot invoked it.
                        if (act == QuickMenuAssignableAction.Save) m_QuickMenuSavePopupTargetIdx = idxCopy;
                        QuickMenuExecuteAssignment(act);
                    });

                    // Right-click on current Page button goes backwards (slot is dynamic, so check at click time).
                    var rc = go.AddComponent<QuickMenuRightClickHandler>();
                    rc.onRightClick = () =>
                    {
                        if (idxCopy == m_QuickMenuPageToggleSlotIdx)
                        {
                            QuickMenuChangePage(-1);
                            for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
                            return;
                        }

                        if (m_QuickMenuEditMode) return;

                        var act = QuickMenuGetSlotAction(idxCopy);
                        QuickMenuExecuteAssignmentRightClick(act);
                    };

                    var a0 = QuickMenuGetSlotAction(i);
                    bool requiresGallery = (a0 == QuickMenuAssignableAction.ShowHide) ||
                                           (a0 == QuickMenuAssignableAction.BringFront) ||
                                           (a0 == QuickMenuAssignableAction.CloseAll) ;
                    if (requiresGallery) go.SetActive(initialShouldShow);
                }

                // Bind legacy refs to assigned slots so existing update paths continue working.
                m_CreateGalleryButtonGO = m_QuickMenuGridButtons[0];
                m_ShowHideButtonGO = m_QuickMenuGridButtons[1];
                m_BringFrontButtonGO = m_QuickMenuGridButtons[2];
                m_CloseAllButtonGO = m_QuickMenuGridButtons[3];
                m_CreateGalleryButtonRT = (m_QuickMenuGridButtonRTs != null) ? m_QuickMenuGridButtonRTs[0] : null;
                m_ShowHideButtonRT = (m_QuickMenuGridButtonRTs != null) ? m_QuickMenuGridButtonRTs[1] : null;
                m_BringFrontButtonRT = (m_QuickMenuGridButtonRTs != null) ? m_QuickMenuGridButtonRTs[2] : null;
                m_CloseAllButtonRT = (m_QuickMenuGridButtonRTs != null) ? m_QuickMenuGridButtonRTs[3] : null;
                // These were UIDynamicButton refs in the old implementation; grid is icon-only now.
                m_CreateGalleryButton = null;
                m_ShowHideButton = null;
                m_BringFrontButton = null;
                m_CloseAllButton = null;

                // Assignment popup (simple list)
                m_QuickMenuAssignPopupRoot = UI.AddChildGOImage(canvasObject, new Color(0f, 0f, 0f, 0.85f), AnchorPresets.topLeft, 260f, 260f, Vector2.zero);
                m_QuickMenuAssignPopupRoot.name = "VPB_QM_AssignPopup";
                m_QuickMenuAssignPopupRT = m_QuickMenuAssignPopupRoot.GetComponent<RectTransform>();
                if (m_QuickMenuAssignPopupRT != null)
                {
                    m_QuickMenuAssignPopupRT.anchorMin = new Vector2(0.5f, 0.5f);
                    m_QuickMenuAssignPopupRT.anchorMax = new Vector2(0.5f, 0.5f);
                    // Pivot at bottom-left so we can grow options upward from the click point.
                    m_QuickMenuAssignPopupRT.pivot = new Vector2(0f, 0f);
                }
                m_QuickMenuAssignPopupRoot.SetActive(false);

                m_QuickMenuAssignCategoryPopupRoot = UI.AddChildGOImage(canvasObject, new Color(0f, 0f, 0f, 0.9f), AnchorPresets.topLeft, 250f, 220f, Vector2.zero);
                m_QuickMenuAssignCategoryPopupRoot.name = "VPB_QM_AssignPopup_Category";
                m_QuickMenuAssignCategoryPopupRT = m_QuickMenuAssignCategoryPopupRoot.GetComponent<RectTransform>();
                if (m_QuickMenuAssignCategoryPopupRT != null)
                {
                    m_QuickMenuAssignCategoryPopupRT.anchorMin = new Vector2(0.5f, 0.5f);
                    m_QuickMenuAssignCategoryPopupRT.anchorMax = new Vector2(0.5f, 0.5f);
                    m_QuickMenuAssignCategoryPopupRT.pivot = new Vector2(0f, 0f);
                }
                var catHover = m_QuickMenuAssignCategoryPopupRoot.AddComponent<QuickMenuAssignCategoryPopupHoverHandler>();
                if (catHover != null) catHover.owner = this;
                m_QuickMenuAssignCategoryPopupRoot.SetActive(false);

                m_QuickMenuAssignRandomPopupRoot = UI.AddChildGOImage(canvasObject, new Color(0f, 0f, 0f, 0.9f), AnchorPresets.topLeft, 260f, 260f, Vector2.zero);
                m_QuickMenuAssignRandomPopupRoot.name = "VPB_QM_AssignPopup_Random";
                m_QuickMenuAssignRandomPopupRT = m_QuickMenuAssignRandomPopupRoot.GetComponent<RectTransform>();
                if (m_QuickMenuAssignRandomPopupRT != null)
                {
                    m_QuickMenuAssignRandomPopupRT.anchorMin = new Vector2(0.5f, 0.5f);
                    m_QuickMenuAssignRandomPopupRT.anchorMax = new Vector2(0.5f, 0.5f);
                    m_QuickMenuAssignRandomPopupRT.pivot = new Vector2(0f, 0f);
                }
                var rndHover = m_QuickMenuAssignRandomPopupRoot.AddComponent<QuickMenuAssignRandomPopupHoverHandler>();
                if (rndHover != null) rndHover.owner = this;
                m_QuickMenuAssignRandomPopupRoot.SetActive(false);

                QuickMenuRebuildAssignPopupButtons();

                // Initial visuals (icons / showhide state)
                for (int i = 0; i < QuickMenuGridSlotCount; i++) QuickMenuRefreshSlotVisual(i);

                m_QuickMenuButtonInited = true;
                LogUtil.LogVerboseUi("QuickMenuButton created. VR: " + isVR);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("Error creating QuickMenuButton: " + ex.ToString());
            }
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

            // Refresh AutoInstall / install tint on visible file rows only (avoid global FileManagerRefresh → gallery).
            try { m_FileBrowser.RefreshDisplayedInstallStatus(); } catch { }
        }

        public void ShowSaveFileBrowser(string title, string fileFormat, string path, string defaultFileNameNoExt, Action<string> onSelected, bool inGame = false)
        {
            if (SuperController.singleton == null) return;

            if (m_FileBrowser == null)
            {
                try { CreateFileBrowser(); }
                catch { }
            }
            if (m_FileBrowser == null) return;

            SuperController.singleton.ActivateWorldUI();
            try { m_HubBrowse?.Hide(); }
            catch { }

            m_FileBrowser.Hide();

            m_FileBrowser.SetTextEntry(true);
            m_FileBrowser.keepOpen = false;
            m_FileBrowser.hideExtension = true;
            m_FileBrowser.selectOnClick = false;
            m_FileBrowser.SetTitle("<color=green>" + title + "</color>");

            FileBrowserCallback cb = (selectedPath) =>
            {
                try { onSelected?.Invoke(selectedPath); }
                catch { }
            };

            m_FileBrowser.Show(fileFormat, path, cb, true, inGame);

            try
            {
                if (m_FileBrowser.fileEntryField != null)
                {
                    m_FileBrowser.fileEntryField.text = defaultFileNameNoExt ?? string.Empty;
                    m_FileBrowser.ActivateFileNameField();
                }
            }
            catch { }
        }
        void OnGUI()
        {
            if (!m_Inited)
            {
                GUI.Box(new Rect(0, 0, 200, 30), "VPB is waiting to start");
                return;
            }

            if (!LogUtil.IsSceneLoading() || Settings.Instance == null || Settings.Instance.ShowSceneLoadingOverlay == null || !Settings.Instance.ShowSceneLoadingOverlay.Value)
                return;

            EnsureStyles();
            var prevDepth = GUI.depth;
            var prevColor = GUI.color;
            GUI.depth = -10000;
            GUI.color = Color.white;
            var overlayRect = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(overlayRect, m_TexLoadingOverlay);

            string progress = m_ProgressText;
            if (string.IsNullOrEmpty(progress))
                progress = "Loading...";
            var labelStyle = (m_StyleHeader != null) ? m_StyleHeader : GUI.skin.label;
            var prevAlign = labelStyle.alignment;
            var prevWrap = labelStyle.wordWrap;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.wordWrap = true;
            GUI.Label(overlayRect, progress, labelStyle);
            labelStyle.alignment = prevAlign;
            labelStyle.wordWrap = prevWrap;

            GUI.color = prevColor;
            GUI.depth = prevDepth;
        }

        private void UpdateUnderlyingUGUIBlockState()
        {
            if (!m_BlockingUnderlyingUGUI)
                return;

            m_BlockingUnderlyingUGUI = false;
            try
            {
                if (Gallery.singleton == null || Gallery.singleton.Panels == null)
                    return;
                for (int i = 0; i < Gallery.singleton.Panels.Count; i++)
                {
                    var p = Gallery.singleton.Panels[i];
                    if (p == null || p.canvas == null)
                        continue;
                    var gr = p.canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                    if (gr != null)
                        gr.enabled = true;
                }
            }
            catch { }
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

            try
            {
                if (SuperController.singleton != null)
                {
                    string normalized = UI.NormalizePath(saveName);
                    SuperController.singleton.Load(normalized);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] LoadFromSceneWorldDialog failed: " + ex.Message);
            }

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
