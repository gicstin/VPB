using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public enum QuickMenuVrWatchMode
    {
        LeftOnly,
        RightOnly,
        OppositeToMenu,
        SameAsMenu,
    }

    public enum QuickMenuVrWatchShowWhen
    {
        Glance,
        Menu,
        Always,
    }

    public partial class VamHookPlugin
    {
        private const float QuickMenuWatchLatchDelaySec = 0.25f;
        private const float QuickMenuWatchGlanceShowDot = 0.42f;
        private const float QuickMenuWatchGlanceHideDot = 0.22f;
        private const float QuickMenuWatchTutorialSec = 5f;
        private const float QuickMenuWatchBtn = 40f;
        private const float QuickMenuWatchGap = 10f;
        private const float QuickMenuWatchRailGap = 8f;
        private const float QuickMenuWatchChrome = 32f;
        private const float QuickMenuWatchChromeGap = 8f;
        private const int QuickMenuWatchStaticChromeCount = 4;
        private const float QuickMenuWatchBezelCornerPx = 8f;
        private const float QuickMenuWatchPad = 10f;
        private const float QuickMenuWatchStatusH = 26f;
        private const int QuickMenuWatchHudSlotCount = 4;
        private const int QuickMenuWatchAssignSlotCount = VPBConfig.QuickMenuVrWatchAssignSlotCount;
        private const int QuickMenuWatchAssignSheetCols = 6;
        private const float QuickMenuWatchAssignCell = 32f;
        private const float QuickMenuWatchAssignGap = 4f;
        private const int QuickMenuWatchStatusFont = 20;
        private const int QuickMenuWatchPageFont = 18;
        private const int QuickMenuWatchCueFont = 16;
        private const float QuickMenuWatchFontDppu = 8f;

        private static readonly QuickMenuAssignableAction[] QuickMenuWatchAssignCatalog =
        {
            QuickMenuAssignableAction.None,
            QuickMenuAssignableAction.CoreSettingsButton,
            QuickMenuAssignableAction.CorePageButton,
            QuickMenuAssignableAction.CreateGallery,
            QuickMenuAssignableAction.ShowHide,
            QuickMenuAssignableAction.BringFront,
            QuickMenuAssignableAction.CloseAll,
            QuickMenuAssignableAction.Save,
            QuickMenuAssignableAction.Undo,
            QuickMenuAssignableAction.Redo,
            QuickMenuAssignableAction.Hub,
            QuickMenuAssignableAction.Cleanup,
            QuickMenuAssignableAction.CreatorMode,
            QuickMenuAssignableAction.TargetAtom,
            QuickMenuAssignableAction.ReplaceAddToggle,
            QuickMenuAssignableAction.CompressCache,
            QuickMenuAssignableAction.AutoHideGallery,
            QuickMenuAssignableAction.ShowHiddenPackages,
            QuickMenuAssignableAction.FpsCounter,
            QuickMenuAssignableAction.History,
            QuickMenuAssignableAction.PerfMode,
            QuickMenuAssignableAction.RemoveAllClothing,
            QuickMenuAssignableAction.RemoveAllHair,
            QuickMenuAssignableAction.ToggleImportSidebar,
            QuickMenuAssignableAction.StarFilter,
            QuickMenuAssignableAction.PerfStepUp,
            QuickMenuAssignableAction.PerfStepDown,
            QuickMenuAssignableAction.Random,
            QuickMenuAssignableAction.RandomScenes,
            QuickMenuAssignableAction.RandomSubScenes,
            QuickMenuAssignableAction.RandomClothing,
            QuickMenuAssignableAction.RandomHair,
            QuickMenuAssignableAction.RandomPose,
            QuickMenuAssignableAction.RandomAppearance,
            QuickMenuAssignableAction.RandomSkin,
            QuickMenuAssignableAction.RandomSceneImport,
            QuickMenuAssignableAction.OpenCategoryScenes,
            QuickMenuAssignableAction.OpenCategorySubScenes,
            QuickMenuAssignableAction.OpenCategoryClothing,
            QuickMenuAssignableAction.OpenCategoryHair,
            QuickMenuAssignableAction.OpenCategoryPose,
            QuickMenuAssignableAction.OpenCategoryAppearance,
            QuickMenuAssignableAction.OpenCategoryPlugins,
            QuickMenuAssignableAction.OpenCategorySkin,
            QuickMenuAssignableAction.OpenCategoryAll,
        };

        private static readonly Quaternion QuickMenuWatchWristLocalRot = Quaternion.Euler(90f, 180f, 0f);
        private static readonly Color QuickMenuWatchBezelColor = new Color(0.10f, 0.10f, 0.10f, 0.92f);
        private static readonly Color QuickMenuWatchChromeIdle = new Color(0.22f, 0.22f, 0.22f, 0.95f);
        private static readonly Color QuickMenuWatchChromeIdleHover = new Color(0.34f, 0.34f, 0.34f, 0.95f);
        private static readonly Color QuickMenuWatchChromeOn = new Color(0.28f, 0.38f, 0.31f, 0.95f);
        private static readonly Color QuickMenuWatchChromeOnHover = new Color(0.36f, 0.50f, 0.40f, 0.95f);
        private Canvas m_WatchCanvas;
        private RectTransform m_WatchCanvasRT;
        private Transform m_WatchTf;
        private Image m_WatchBezel;
        private Text m_WatchStatusText;
        private Text m_WatchCueText;
        private GameObject m_WatchCueGo;
        private GameObject[] m_WatchSlotGos;
        private Image[] m_WatchSlotIcons;
        private Image[] m_WatchSlotBackdrops;
        private GameObject[] m_WatchAssignGos;
        private Image[] m_WatchAssignIcons;
        private Image[] m_WatchAssignBackdrops;
        private Image m_WatchPinBg;
        private QuickMenuSquareHover m_WatchPinHover;
        private VrWatchHoverTip m_WatchPinTip;
        private Image m_WatchEditBg;
        private QuickMenuSquareHover m_WatchEditHover;
        private VrWatchHoverTip m_WatchEditTip;
        private Text m_WatchPageText;
        private int m_WatchPageShown = -1;
        private QuickMenuAssignableAction[][] m_WatchPageAssignments;
        private int m_WatchCurrentPage;
        private bool m_WatchEditMode;
        private int m_WatchAssignTargetIdx = -1;
        private GameObject m_WatchAssignSheet;
        private RectTransform m_WatchAssignSheetRt;
        private Image m_WatchAssignSheetBg;
        private float m_WatchAssignSheetW;
        private Transform m_WatchHand;
        private Transform m_WatchLatchedHand;
        private bool m_WatchMenuWasVisible;
        private bool m_WatchLatchPending;
        private float m_WatchOpenTime;
        private bool m_WatchVisibleNow;
        private bool m_WatchGlanced;
        private bool m_WatchPinned;
        private bool m_WatchCalibrating;
        private bool m_WatchIsLeft;
        private byte m_WatchSessionHand; // 0 none, 1 left, 2 right
        private float m_WatchTutorialUntil;
        private bool m_WatchStatusNeedRebuild = true;
        private string m_WatchStatusShown = "";
        private string m_WatchHoverStatus;
        private bool m_WatchAddedToSc;
        private float m_WatchLastLocalScale = -1f;

        // Cached settings (copied on change; hot path never parses strings).
        private bool m_WatchCfgVisible = true;
        private QuickMenuVrWatchMode m_WatchCfgHand = QuickMenuVrWatchMode.OppositeToMenu;
        private QuickMenuVrWatchShowWhen m_WatchCfgShowWhen = QuickMenuVrWatchShowWhen.Glance;
        private bool m_WatchCfgFaceUser = true;
        private bool m_WatchCfgRememberHand;
        private float m_WatchCfgScaleMul = 1f;
        private float m_WatchCfgToward = 0.04f;
        private Vector3 m_WatchCfgOffset = VPBConfig.QuickMenuVrWatchOffsetDefault;
        private string m_WatchCfgHandRaw;
        private string m_WatchCfgShowRaw;
        private bool m_WatchAssignmentsLoaded;

        internal void QuickMenuDestroyWatch()
        {
            if (m_WatchCanvas != null)
            {
                try
                {
                    var sc = SuperController.singleton;
                    if (sc != null && m_WatchAddedToSc) sc.RemoveCanvas(m_WatchCanvas);
                }
                catch { }
                try { UnityEngine.Object.Destroy(m_WatchCanvas.gameObject); } catch { }
            }
            m_WatchCanvas = null;
            m_WatchCanvasRT = null;
            m_WatchTf = null;
            m_WatchBezel = null;
            m_WatchStatusText = null;
            m_WatchCueText = null;
            m_WatchCueGo = null;
            m_WatchSlotGos = null;
            m_WatchSlotIcons = null;
            m_WatchSlotBackdrops = null;
            m_WatchAssignGos = null;
            m_WatchAssignIcons = null;
            m_WatchAssignBackdrops = null;
            m_WatchPinBg = null;
            m_WatchPinHover = null;
            m_WatchPinTip = null;
            m_WatchEditBg = null;
            m_WatchEditHover = null;
            m_WatchEditTip = null;
            m_WatchPageText = null;
            m_WatchPageShown = -1;
            m_WatchAssignTargetIdx = -1;
            m_WatchAssignSheet = null;
            m_WatchAssignSheetRt = null;
            m_WatchAssignSheetBg = null;
            m_WatchHand = null;
            m_WatchLatchedHand = null;
            m_WatchVisibleNow = false;
            m_WatchAddedToSc = false;
            m_WatchLastLocalScale = -1f;
        }

        internal void QuickMenuSyncWatchCornerRadius(float frac)
        {
            QuickMenuApplyWatchBezelRadius(frac);
            if (m_WatchSlotBackdrops != null)
            {
                for (int i = 0; i < m_WatchSlotBackdrops.Length; i++)
                {
                    RoundedRect rr = m_WatchSlotBackdrops[i] as RoundedRect;
                    if (rr != null) rr.cornerRadiusFraction = frac;
                }
            }
            if (m_WatchAssignBackdrops != null)
            {
                for (int i = 0; i < m_WatchAssignBackdrops.Length; i++)
                {
                    RoundedRect rr = m_WatchAssignBackdrops[i] as RoundedRect;
                    if (rr != null) rr.cornerRadiusFraction = frac;
                }
            }
        }

        internal void QuickMenuInvalidateWatchStrings()
        {
            m_WatchStatusNeedRebuild = true;
            if (m_WatchCueText != null)
                m_WatchCueText.text = VPBTranslation.T("hook.watch.cue", "Look at inner wrist to show again. HUD grid stays.");
            if (m_WatchPinTip != null) m_WatchPinTip.tip = QuickMenuWatchPinTipText();
            if (m_WatchEditTip != null) m_WatchEditTip.tip = QuickMenuWatchEditTipText();
        }

        // Called every frame from Update. HUD grid is never reparented.
        private void QuickMenuUpdateVrWatch()
        {
            bool vr = QuickMenuIsVrActive();
            if (!vr)
            {
                if (m_WatchVisibleNow) QuickMenuSetWatchShown(false);
                return;
            }

            QuickMenuPullWatchSettings();

            if (!m_WatchCfgVisible)
            {
                if (m_WatchVisibleNow) QuickMenuSetWatchShown(false);
                return;
            }

            bool menuVis = QuickMenuIsVamMenuVisible();
            if (menuVis && !m_WatchMenuWasVisible)
            {
                m_WatchOpenTime = Time.unscaledTime;
                // Explicit Left/Right / session switch stay. Opposite/Same re-latch which hand opened menu.
                if (m_WatchSessionHand == 0 &&
                    (m_WatchCfgHand == QuickMenuVrWatchMode.OppositeToMenu ||
                     m_WatchCfgHand == QuickMenuVrWatchMode.SameAsMenu))
                {
                    m_WatchLatchPending = true;
                    m_WatchLatchedHand = null;
                }
                var cfg = VPBConfig.Instance;
                if (cfg != null && !cfg.QuickMenuVrWatchOnboardingSeen && m_WatchTutorialUntil <= 0f)
                    m_WatchTutorialUntil = Time.unscaledTime + QuickMenuWatchTutorialSec;
            }
            m_WatchMenuWasVisible = menuVis;

            Transform watchHand = QuickMenuResolveWatchHand();
            if (watchHand == null)
            {
                if (m_WatchVisibleNow) QuickMenuSetWatchShown(false);
                return;
            }

            var scHand = SuperController.singleton;
            bool isLeft = scHand != null && watchHand == scHand.touchObjectLeft;
            if (isLeft != m_WatchIsLeft)
            {
                m_WatchIsLeft = isLeft;
                m_WatchStatusNeedRebuild = true;
                QuickMenuPlaceWatchAssignSheet();
            }

            bool tutorial = m_WatchTutorialUntil > 0f && Time.unscaledTime < m_WatchTutorialUntil;
            if (m_WatchTutorialUntil > 0f && Time.unscaledTime >= m_WatchTutorialUntil)
            {
                m_WatchTutorialUntil = 0f;
                try
                {
                    var c = VPBConfig.Instance;
                    if (c != null && !c.QuickMenuVrWatchOnboardingSeen)
                    {
                        c.QuickMenuVrWatchOnboardingSeen = true;
                        c.Save(false, true);
                    }
                }
                catch { }
            }

            bool glanced = QuickMenuComputeWatchGlance(watchHand);
            if (m_WatchGlanced)
            {
                if (!glanced) m_WatchGlanced = false;
            }
            else if (glanced) m_WatchGlanced = true;

            bool wantShow;
            if (m_WatchCalibrating || tutorial || m_WatchPinned)
                wantShow = true;
            else
            {
                switch (m_WatchCfgShowWhen)
                {
                    case QuickMenuVrWatchShowWhen.Always:
                        wantShow = true;
                        break;
                    case QuickMenuVrWatchShowWhen.Menu:
                        wantShow = menuVis;
                        break;
                    default:
                        wantShow = m_WatchGlanced;
                        break;
                }
            }

            if (!wantShow)
            {
                if (m_WatchVisibleNow) QuickMenuSetWatchShown(false);
                return;
            }

            if (m_WatchCanvas == null) QuickMenuEnsureWatchCanvas();
            if (m_WatchTf == null) return;

            if (m_WatchTf.parent != watchHand)
            {
                m_WatchTf.SetParent(watchHand, false);
                m_WatchHand = watchHand;
            }

            QuickMenuApplyWatchPose();
            QuickMenuSetWatchShown(true);
            QuickMenuRefreshWatchStatus();
            QuickMenuSyncWatchPageLabel();
            QuickMenuSyncWatchAssignLiveIcons();
            QuickMenuSetWatchCueShown(tutorial);
        }

        private void QuickMenuPullWatchSettings()
        {
            var c = VPBConfig.Instance;
            if (c == null) return;

            m_WatchCfgVisible = c.QuickMenuVrWatchVisible;
            m_WatchCfgFaceUser = c.QuickMenuVrWatchFaceUser;
            m_WatchCfgRememberHand = c.QuickMenuVrWatchRememberHand;
            m_WatchCfgScaleMul = c.QuickMenuVrWatchScaleMul;
            m_WatchCfgToward = c.QuickMenuVrWatchTowardUserDist;
            m_WatchCfgOffset = c.QuickMenuVrWatchOffset;

            string handRaw = c.QuickMenuVrWatchMode;
            if (!string.Equals(handRaw, m_WatchCfgHandRaw, System.StringComparison.Ordinal))
            {
                m_WatchCfgHandRaw = handRaw;
                m_WatchCfgHand = QuickMenuParseWatchMode(handRaw);
                m_WatchSessionHand = 0;
                m_WatchLatchedHand = null;
                m_WatchLatchPending = true;
                m_WatchStatusNeedRebuild = true;
            }

            string showRaw = c.QuickMenuVrWatchShowWhen;
            if (!string.Equals(showRaw, m_WatchCfgShowRaw, System.StringComparison.Ordinal))
            {
                m_WatchCfgShowRaw = showRaw;
                m_WatchCfgShowWhen = QuickMenuParseWatchShowWhen(showRaw);
                m_WatchStatusNeedRebuild = true;
                QuickMenuRefreshWatchChromeColors();
            }

            QuickMenuEnsureWatchAssignments();
        }

        private Transform QuickMenuResolveWatchHand()
        {
            var sc = SuperController.singleton;
            if (sc == null) return null;
            Transform left = sc.touchObjectLeft;
            Transform right = sc.touchObjectRight;
            Transform hud = sc.mainHUD;

            if (left == null && right == null) return null;

            // Explicit choice never steals the other controller (even if that hand is untracked).
            if (m_WatchSessionHand == 1) return left;
            if (m_WatchSessionHand == 2) return right;
            if (m_WatchCfgHand == QuickMenuVrWatchMode.LeftOnly) return left;
            if (m_WatchCfgHand == QuickMenuVrWatchMode.RightOnly) return right;

            // Opposite/Same only: one tracked controller.
            if (left == null) return right;
            if (right == null) return left;

            switch (m_WatchCfgHand)
            {
                case QuickMenuVrWatchMode.SameAsMenu:
                case QuickMenuVrWatchMode.OppositeToMenu:
                    if (m_WatchLatchedHand == null && hud != null &&
                        (!m_WatchLatchPending || (Time.unscaledTime - m_WatchOpenTime) >= QuickMenuWatchLatchDelaySec))
                    {
                        float dl = (left.position - hud.position).sqrMagnitude;
                        float dr = (right.position - hud.position).sqrMagnitude;
                        Transform menuHand = (dl < dr) ? left : right;
                        m_WatchLatchedHand = (m_WatchCfgHand == QuickMenuVrWatchMode.SameAsMenu)
                            ? menuHand
                            : ((menuHand == left) ? right : left);
                        m_WatchLatchPending = false;
                    }
                    if (m_WatchLatchedHand != null) return m_WatchLatchedHand;
                    // No latch yet (menu never opened): opposite → left (laser typically right).
                    return (m_WatchCfgHand == QuickMenuVrWatchMode.SameAsMenu) ? right : left;
                default:
                    return left;
            }
        }

        private bool QuickMenuComputeWatchGlance(Transform hand)
        {
            Transform cam = QuickMenuGetPlayerCamera();
            if (cam == null || hand == null) return m_WatchGlanced;

            Vector3 offset = m_WatchCfgOffset;
            if (!m_WatchIsLeft) offset.x = -offset.x;
            Vector3 watchPos = hand.TransformPoint(offset);
            Vector3 away = watchPos - cam.position;
            float mag2 = away.sqrMagnitude;
            if (mag2 < 0.0064f || mag2 > 1.44f) return m_WatchGlanced; // 8cm–1.2m

            Vector3 restFwd = (hand.rotation * QuickMenuWatchWristLocalRot) * Vector3.forward;
            float dot = (away.x * restFwd.x + away.y * restFwd.y + away.z * restFwd.z) * (1f / Mathf.Sqrt(mag2));
            if (m_WatchGlanced) return dot >= QuickMenuWatchGlanceHideDot;
            return dot >= QuickMenuWatchGlanceShowDot;
        }

        private void QuickMenuSetWatchShown(bool show)
        {
            if (m_WatchVisibleNow == show && (m_WatchCanvas == null || m_WatchCanvas.gameObject.activeSelf == show))
            {
                m_WatchVisibleNow = show;
                return;
            }
            m_WatchVisibleNow = show;
            if (m_WatchCanvas != null && m_WatchCanvas.gameObject.activeSelf != show)
                m_WatchCanvas.gameObject.SetActive(show);
            if (show)
            {
                for (int i = 0; i < QuickMenuWatchHudSlotCount; i++)
                    QuickMenuSyncWatchSlot(i);
                QuickMenuSyncWatchAssignAll();
                QuickMenuSyncWatchPageLabel();
                QuickMenuRefreshWatchChromeColors();
            }
        }

        private void QuickMenuSetWatchCueShown(bool show)
        {
            if (m_WatchCueGo == null) return;
            if (m_WatchCueGo.activeSelf != show) m_WatchCueGo.SetActive(show);
        }

        private void QuickMenuEnsureWatchCanvas()
        {
            if (m_WatchCanvas != null) return;
            var sc = SuperController.singleton;
            if (sc == null || sc.mainHUD == null) return;

            QuickMenuEnsureWatchAssignments();

            float grid = QuickMenuWatchBtn * 2f + QuickMenuWatchGap;
            float chromeW = QuickMenuWatchChrome * QuickMenuWatchStaticChromeCount
                + QuickMenuWatchChromeGap * (QuickMenuWatchStaticChromeCount - 1);
            float innerW = grid > chromeW ? grid : chromeW;
            float stackH = QuickMenuWatchBtn * 4f + QuickMenuWatchGap * 3f;
            float bezelW = QuickMenuWatchPad + QuickMenuWatchBtn + QuickMenuWatchRailGap + innerW
                + QuickMenuWatchRailGap + QuickMenuWatchBtn + QuickMenuWatchPad;
            float bezelH = QuickMenuWatchPad + QuickMenuWatchStatusH + 6f + stackH + QuickMenuWatchPad;

            GameObject root = new GameObject("VPB_VrWatch_Canvas");
            if (sc.mainHUD.gameObject != null) root.layer = sc.mainHUD.gameObject.layer;

            m_WatchCanvas = root.AddComponent<Canvas>();
            m_WatchCanvas.renderMode = RenderMode.WorldSpace;
            m_WatchCanvas.pixelPerfect = false;
            m_WatchTf = root.transform;

            CanvasScaler cs = root.AddComponent<CanvasScaler>();
            if (cs != null)
            {
                cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                cs.scaleFactor = 100f;
                cs.dynamicPixelsPerUnit = QuickMenuWatchFontDppu;
            }
            root.AddComponent<GraphicRaycaster>();

            try
            {
                sc.AddCanvas(m_WatchCanvas);
                m_WatchAddedToSc = true;
            }
            catch { m_WatchAddedToSc = false; }

            m_WatchCanvasRT = root.GetComponent<RectTransform>();
            if (m_WatchCanvasRT == null) m_WatchCanvasRT = root.AddComponent<RectTransform>();
            m_WatchCanvasRT.anchorMin = new Vector2(0.5f, 0.5f);
            m_WatchCanvasRT.anchorMax = new Vector2(0.5f, 0.5f);
            m_WatchCanvasRT.pivot = new Vector2(0.5f, 0.5f);
            m_WatchCanvasRT.sizeDelta = new Vector2(bezelW, bezelH);

            GameObject bezelGo = new GameObject("Bezel");
            bezelGo.transform.SetParent(root.transform, false);
            m_WatchBezel = AddQuickMenuRoundedBg(bezelGo, QuickMenuWatchBezelColor, true);
            QuickMenuApplyWatchBezelRadius(UI.ResolveGalleryElementCornerRadiusFraction());
            RectTransform bezelRt = bezelGo.GetComponent<RectTransform>();
            bezelRt.anchorMin = Vector2.zero;
            bezelRt.anchorMax = Vector2.one;
            bezelRt.sizeDelta = Vector2.zero;
            bezelRt.anchoredPosition = Vector2.zero;
            var drag = bezelGo.AddComponent<VrWatchBezelDrag>();
            drag.owner = this;

            float yCursor = bezelH * 0.5f - QuickMenuWatchPad;
            float yStatus = yCursor - QuickMenuWatchStatusH * 0.5f;
            m_WatchStatusText = UI.CreateLabel(root, "", QuickMenuWatchStatusFont, GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.middleCenter, new Vector2(innerW, QuickMenuWatchStatusH),
                new Vector2(0f, yStatus), "Status");
            if (m_WatchStatusText != null) m_WatchStatusText.resizeTextForBestFit = false;
            yCursor -= QuickMenuWatchStatusH + 6f;

            float yStackTop = yCursor;
            float rowStep = QuickMenuWatchBtn + QuickMenuWatchGap;
            float leftX = -(innerW * 0.5f + QuickMenuWatchRailGap + QuickMenuWatchBtn * 0.5f);
            float rightX = -leftX;
            float cell = QuickMenuWatchBtn + QuickMenuWatchGap;
            float gridOriginX = -cell * 0.5f;
            float chromeOriginX = -(chromeW * 0.5f) + QuickMenuWatchChrome * 0.5f;
            float chromeStep = QuickMenuWatchChrome + QuickMenuWatchChromeGap;

            m_WatchSlotGos = new GameObject[QuickMenuWatchHudSlotCount];
            m_WatchSlotIcons = new Image[QuickMenuWatchHudSlotCount];
            m_WatchSlotBackdrops = new Image[QuickMenuWatchHudSlotCount];
            m_WatchAssignGos = new GameObject[QuickMenuWatchAssignSlotCount];
            m_WatchAssignIcons = new Image[QuickMenuWatchAssignSlotCount];
            m_WatchAssignBackdrops = new Image[QuickMenuWatchAssignSlotCount];

            for (int row = 0; row < 4; row++)
            {
                float yRow = yStackTop - QuickMenuWatchBtn * 0.5f - row * rowStep;
                QuickMenuBuildWatchAssignSlot(root, row, leftX, yRow);
                QuickMenuBuildWatchAssignSlot(root, row + 4, rightX, yRow);

                if (row == 0 || row == 1)
                {
                    int hud0 = row * 2;
                    int hud1 = hud0 + 1;
                    QuickMenuBuildWatchHudSlot(root, hud0, gridOriginX, yRow);
                    QuickMenuBuildWatchHudSlot(root, hud1, gridOriginX + cell, yRow);
                }
                else if (row == 2)
                {
                    QuickMenuBuildWatchChromeButton(root, 0, chromeOriginX, yRow, "vpb_icons/device_watch_off.png",
                        () => { try { QuickMenuSetWatchVisibleFromWatch(false); } catch { } },
                        VPBTranslation.T("hook.watch.tip.hide", "Hide watch"));
                    QuickMenuBuildWatchChromeButton(root, 1, chromeOriginX + chromeStep, yRow,
                        "vpb_icons/switch_horizontal.png",
                        () => { try { QuickMenuSwitchWatchHand(); } catch { } },
                        VPBTranslation.T("hook.watch.tip.hand", "Switch watch hand"));
                    Image pinBg = QuickMenuBuildWatchChromeButton(root, 2,
                        chromeOriginX + chromeStep * 2f, yRow,
                        "vpb_icons/gallery_fixed.png",
                        () => { try { QuickMenuToggleWatchPin(); } catch { } },
                        QuickMenuWatchPinTipText());
                    m_WatchPinBg = pinBg;
                    if (pinBg != null)
                    {
                        m_WatchPinHover = pinBg.GetComponent<QuickMenuSquareHover>();
                        m_WatchPinTip = pinBg.GetComponent<VrWatchHoverTip>();
                    }
                    Image editBg = QuickMenuBuildWatchChromeButton(root, 3,
                        chromeOriginX + chromeStep * 3f, yRow,
                        "vpb_icons/settings_plus.png",
                        () => { try { QuickMenuToggleWatchEdit(); } catch { } },
                        QuickMenuWatchEditTipText());
                    m_WatchEditBg = editBg;
                    if (editBg != null)
                    {
                        m_WatchEditHover = editBg.GetComponent<QuickMenuSquareHover>();
                        m_WatchEditTip = editBg.GetComponent<VrWatchHoverTip>();
                    }
                }
                else
                {
                    QuickMenuBuildWatchChromeButton(root, 10, chromeOriginX, yRow, "vpb_icons/nav_prev.png",
                        () => { try { QuickMenuWatchChangePage(-1); } catch { } },
                        VPBTranslation.T("hook.watch.tip.page_prev", "Previous watch page"));
                    QuickMenuBuildWatchPageLabel(root, yRow);
                    QuickMenuBuildWatchChromeButton(root, 11, chromeOriginX + chromeStep * 3f, yRow, "vpb_icons/nav_next.png",
                        () => { try { QuickMenuWatchChangePage(+1); } catch { } },
                        VPBTranslation.T("hook.watch.tip.page_next", "Next watch page"));
                }
            }

            QuickMenuBuildWatchAssignSheet(root);

            m_WatchCueGo = new GameObject("Cue");
            m_WatchCueGo.transform.SetParent(root.transform, false);
            RectTransform cueRt = m_WatchCueGo.AddComponent<RectTransform>();
            cueRt.anchorMin = new Vector2(0.5f, 0.5f);
            cueRt.anchorMax = new Vector2(0.5f, 0.5f);
            cueRt.pivot = new Vector2(0.5f, 0.5f);
            cueRt.sizeDelta = new Vector2(innerW, 40f);
            cueRt.anchoredPosition = new Vector2(0f, bezelH * 0.5f + 24f);
            m_WatchCueText = UI.CreateLabel(m_WatchCueGo,
                VPBTranslation.T("hook.watch.cue", "Look at inner wrist to show again. HUD grid stays."),
                QuickMenuWatchCueFont, GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "CueText");
            if (m_WatchCueText != null) m_WatchCueText.resizeTextForBestFit = false;
            m_WatchCueGo.SetActive(false);

            root.SetActive(false);
            m_WatchStatusNeedRebuild = true;
            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++)
                QuickMenuSyncWatchSlot(i);
            QuickMenuSyncWatchAssignAll();
            QuickMenuSyncWatchPageLabel();
            QuickMenuRefreshWatchChromeColors();
        }

        private void QuickMenuBuildWatchHudSlot(GameObject root, int hudIdx, float x, float y)
        {
            GameObject go = new GameObject("WatchSlot_" + hudIdx);
            go.transform.SetParent(root.transform, false);
            m_WatchSlotGos[hudIdx] = go;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchBtn, QuickMenuWatchBtn);
            rt.anchoredPosition = new Vector2(x, y);

            Color nb = QmBackdropAssignedOpaque;
            Image img = AddQuickMenuRoundedBg(go, nb);
            m_WatchSlotBackdrops[hudIdx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            QuickMenuAttachWatchHover(go, img, nb, QmBackdropAssignedHoverOpaque, null);

            int idxCopy = hudIdx;
            var tip = go.AddComponent<VrWatchSlotHover>();
            tip.owner = this;
            tip.hudSlotIdx = idxCopy;
            btn.onClick.AddListener(() => { try { QuickMenuOnWatchSlotClick(idxCopy); } catch { } });
        }

        private void QuickMenuBuildWatchAssignSlot(GameObject root, int slotIdx, float x, float y)
        {
            GameObject go = new GameObject("WatchAssign_" + slotIdx);
            go.transform.SetParent(root.transform, false);
            m_WatchAssignGos[slotIdx] = go;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchBtn, QuickMenuWatchBtn);
            rt.anchoredPosition = new Vector2(x, y);

            Color nb = QmBackdropEmptyOpaque;
            Image img = AddQuickMenuRoundedBg(go, nb);
            m_WatchAssignBackdrops[slotIdx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            QuickMenuAttachWatchHover(go, img, nb, QmBackdropEmptyHoverOpaque, null);

            int idxCopy = slotIdx;
            var tip = go.AddComponent<VrWatchAssignHover>();
            tip.owner = this;
            tip.slotIdx = idxCopy;
            btn.onClick.AddListener(() => { try { QuickMenuOnWatchAssignClick(idxCopy); } catch { } });
        }

        private void QuickMenuAttachWatchHover(GameObject go, Image img, Color normal, Color hover, string tip)
        {
            if (go == null || img == null) return;
            var h = go.GetComponent<QuickMenuSquareHover>();
            if (h == null) h = go.AddComponent<QuickMenuSquareHover>();
            h.target = img;
            h.normal = normal;
            h.hover = hover;
            var hb = go.GetComponent<UIHoverBorder>();
            if (hb == null) hb = go.AddComponent<UIHoverBorder>();
            hb.hoverColor = new Color(1f, 1f, 0f, 1f);
            try { if (VPBConfig.Instance != null) hb.hoverColor = VPBConfig.Instance.GetGalleryGridBorderColor(); } catch { }
            hb.ApplyBorderSettings();
            if (!string.IsNullOrEmpty(tip))
            {
                var t = go.GetComponent<VrWatchHoverTip>();
                if (t == null) t = go.AddComponent<VrWatchHoverTip>();
                t.owner = this;
                t.tip = tip;
            }
        }

        private Image QuickMenuBuildWatchChromeButton(GameObject parent, int idx, float x, float y, string iconPath, UnityEngine.Events.UnityAction onClick, string tip)
        {
            GameObject go = new GameObject("WatchChrome_" + idx);
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchChrome, QuickMenuWatchChrome);
            rt.anchoredPosition = new Vector2(x, y);
            Image img = AddQuickMenuRoundedBg(go, QuickMenuWatchChromeIdle);
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            QuickMenuAttachWatchHover(go, img, QuickMenuWatchChromeIdle, QuickMenuWatchChromeIdleHover, tip);
            Sprite spr = UI.LoadIconSprite(iconPath, Color.white);
            if (spr != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(go.transform, false);
                Image icon = iconGO.AddComponent<Image>();
                icon.sprite = spr;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.sizeDelta = new Vector2(-8f, -8f);
                irt.anchoredPosition = Vector2.zero;
            }
            return img;
        }

        private void QuickMenuApplyWatchBezelRadius(float buttonFrac)
        {
            float px = buttonFrac <= 0f ? 0f : QuickMenuWatchBezelCornerPx;
            QuickMenuApplyWatchPanelRadius(m_WatchBezel, px);
            QuickMenuApplyWatchPanelRadius(m_WatchAssignSheetBg, px);
        }

        private static void QuickMenuApplyWatchPanelRadius(Image img, float px)
        {
            if (img == null) return;
            RoundedRect rr = img as RoundedRect;
            if (rr == null) return;
            rr.cornerRadiusFraction = 0f;
            rr.cornerRadius = px;
        }

        private void QuickMenuBuildWatchPageLabel(GameObject parent, float y)
        {
            float w = QuickMenuWatchChrome * 2f + QuickMenuWatchChromeGap;
            m_WatchPageText = UI.CreateLabel(parent, "", QuickMenuWatchPageFont, GalleryUiColorTokens.TextMuted,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.middleCenter, new Vector2(w, QuickMenuWatchChrome),
                new Vector2(0f, y), "WatchPage");
            if (m_WatchPageText != null) m_WatchPageText.resizeTextForBestFit = false;
            m_WatchPageShown = -1;
        }

        internal string QuickMenuWatchHoverLabel(int hudIdx)
        {
            return QuickMenuGetActionTooltip(QuickMenuGetSlotAction(hudIdx), -1);
        }

        private void QuickMenuOnWatchSlotClick(int hudIdx)
        {
            if (m_QuickMenuEditMode) return;
            var act = QuickMenuGetSlotAction(hudIdx);
            if (act == QuickMenuAssignableAction.Save) m_QuickMenuSavePopupTargetIdx = hudIdx;
            QuickMenuExecuteAssignment(act);
        }

        private void QuickMenuOnWatchAssignClick(int slotIdx)
        {
            if (slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return;
            if (m_WatchEditMode)
            {
                if (m_WatchAssignTargetIdx == slotIdx && m_WatchAssignSheet != null && m_WatchAssignSheet.activeSelf)
                {
                    QuickMenuSetWatchAssignTarget(-1);
                    return;
                }
                QuickMenuSetWatchAssignTarget(slotIdx);
                return;
            }

            var act = QuickMenuGetWatchSlotAction(slotIdx);
            if (act == QuickMenuAssignableAction.None) return;
            if (act == QuickMenuAssignableAction.PageNext)
            {
                QuickMenuWatchChangePage(+1);
                return;
            }
            if (act == QuickMenuAssignableAction.PagePrev)
            {
                QuickMenuWatchChangePage(-1);
                return;
            }
            if (act == QuickMenuAssignableAction.SwitchWatchHand)
            {
                QuickMenuSwitchWatchHand();
                return;
            }
            if (act == QuickMenuAssignableAction.Save) m_QuickMenuSavePopupTargetIdx = -1;
            QuickMenuExecuteAssignment(act);
        }

        internal string QuickMenuWatchAssignHoverLabel(int slotIdx)
        {
            if (m_WatchEditMode)
                return VPBTranslation.T("hook.watch.tip.assign_slot", "Assign this watch slot");
            return QuickMenuGetActionTooltip(QuickMenuGetWatchSlotAction(slotIdx), -1);
        }

        private string QuickMenuWatchPinTipText()
        {
            return m_WatchPinned
                ? VPBTranslation.T("hook.watch.tip.unpin", "Unpin watch")
                : VPBTranslation.T("hook.watch.tip.pin", "Pin watch");
        }

        private string QuickMenuWatchEditTipText()
        {
            return m_WatchEditMode
                ? VPBTranslation.T("hook.watch.tip.edit_done", "Done assigning")
                : VPBTranslation.T("hook.watch.tip.edit", "Assign watch buttons");
        }

        private void QuickMenuEnsureWatchAssignments()
        {
            int pages = QuickMenuPageCount;
            int slots = QuickMenuWatchAssignSlotCount;
            if (m_WatchPageAssignments == null || m_WatchPageAssignments.Length != pages)
                m_WatchPageAssignments = new QuickMenuAssignableAction[pages][];
            for (int p = 0; p < pages; p++)
            {
                if (m_WatchPageAssignments[p] == null || m_WatchPageAssignments[p].Length != slots)
                    m_WatchPageAssignments[p] = new QuickMenuAssignableAction[slots];
            }

            if (m_WatchAssignmentsLoaded) return;
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.EnsureWatchButtonPages();
            int loadPages = Mathf.Min(cfg.QuickMenuVrWatchButtonsPages.Length, pages);
            for (int p = 0; p < loadPages; p++)
            {
                var src = cfg.QuickMenuVrWatchButtonsPages[p];
                for (int s = 0; s < slots; s++)
                {
                    string id = (src != null && s < src.Length) ? src[s] : "";
                    m_WatchPageAssignments[p][s] = QuickMenuIdToAction(id);
                }
            }
            for (int p = loadPages; p < pages; p++)
                for (int s = 0; s < slots; s++)
                    m_WatchPageAssignments[p][s] = QuickMenuAssignableAction.None;
            m_WatchCurrentPage = Mathf.Clamp(cfg.QuickMenuVrWatchCurrentPage, 0, pages - 1);
            m_WatchAssignmentsLoaded = true;
        }

        private QuickMenuAssignableAction QuickMenuGetWatchSlotAction(int slotIdx)
        {
            if (m_WatchPageAssignments == null) QuickMenuEnsureWatchAssignments();
            if (m_WatchPageAssignments == null) return QuickMenuAssignableAction.None;
            if (slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return QuickMenuAssignableAction.None;
            int p = m_WatchCurrentPage;
            if (p < 0 || p >= QuickMenuPageCount) p = 0;
            return m_WatchPageAssignments[p][slotIdx];
        }

        private void QuickMenuSetWatchSlotAction(int slotIdx, QuickMenuAssignableAction action)
        {
            if (m_WatchPageAssignments == null) QuickMenuEnsureWatchAssignments();
            if (m_WatchPageAssignments == null) return;
            if (slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return;
            if (m_WatchCurrentPage < 0) m_WatchCurrentPage = 0;
            if (m_WatchCurrentPage >= QuickMenuPageCount) m_WatchCurrentPage = 0;
            m_WatchPageAssignments[m_WatchCurrentPage][slotIdx] = action;
            QuickMenuPersistWatchAssignments();
            QuickMenuSyncWatchAssignSlot(slotIdx);
        }

        private void QuickMenuPersistWatchAssignments()
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.EnsureWatchButtonPages();
            cfg.QuickMenuVrWatchCurrentPage = Mathf.Clamp(m_WatchCurrentPage, 0, QuickMenuPageCount - 1);
            for (int p = 0; p < QuickMenuPageCount; p++)
            {
                for (int s = 0; s < QuickMenuWatchAssignSlotCount; s++)
                    cfg.QuickMenuVrWatchButtonsPages[p][s] = QuickMenuActionToId(m_WatchPageAssignments[p][s]);
            }
            try { cfg.Save(false, true); } catch { }
        }

        private void QuickMenuWatchChangePage(int delta)
        {
            QuickMenuEnsureWatchAssignments();
            int n = QuickMenuPageCount;
            int p = (m_WatchCurrentPage + delta) % n;
            if (p < 0) p += n;
            if (p == m_WatchCurrentPage) return;
            m_WatchCurrentPage = p;
            QuickMenuPersistWatchAssignments();
            QuickMenuSyncWatchAssignAll();
            m_WatchPageShown = -1;
            QuickMenuSyncWatchPageLabel();
            m_WatchStatusNeedRebuild = true;
            QuickMenuRefreshWatchStatus();
            if (m_WatchAssignTargetIdx >= 0) QuickMenuSyncWatchAssignSlot(m_WatchAssignTargetIdx);
        }

        private void QuickMenuToggleWatchEdit()
        {
            m_WatchEditMode = !m_WatchEditMode;
            if (!m_WatchEditMode) QuickMenuSetWatchAssignTarget(-1);
            else if (m_WatchAssignSheet != null && !m_WatchAssignSheet.activeSelf)
                QuickMenuSetWatchAssignTarget(m_WatchAssignTargetIdx >= 0 ? m_WatchAssignTargetIdx : 0);
            m_WatchStatusNeedRebuild = true;
            QuickMenuRefreshWatchChromeColors();
            QuickMenuSyncWatchAssignAll();
            QuickMenuRefreshWatchStatus();
        }

        private void QuickMenuSetWatchAssignTarget(int slotIdx)
        {
            int prev = m_WatchAssignTargetIdx;
            m_WatchAssignTargetIdx = slotIdx;
            if (prev >= 0) QuickMenuSyncWatchAssignSlot(prev);
            if (slotIdx >= 0) QuickMenuSyncWatchAssignSlot(slotIdx);
            if (m_WatchAssignSheet != null)
            {
                bool show = m_WatchEditMode && slotIdx >= 0;
                if (m_WatchAssignSheet.activeSelf != show) m_WatchAssignSheet.SetActive(show);
                if (show) QuickMenuPlaceWatchAssignSheet();
            }
        }

        private void QuickMenuBuildWatchAssignSheet(GameObject root)
        {
            int n = QuickMenuWatchAssignCatalog.Length;
            int cols = QuickMenuWatchAssignSheetCols;
            int rows = (n + cols - 1) / cols;
            float cell = QuickMenuWatchAssignCell + QuickMenuWatchAssignGap;
            float sheetW = cols * QuickMenuWatchAssignCell + (cols - 1) * QuickMenuWatchAssignGap + 8f;
            float sheetH = rows * QuickMenuWatchAssignCell + (rows - 1) * QuickMenuWatchAssignGap + 8f;
            m_WatchAssignSheetW = sheetW;

            GameObject sheet = new GameObject("WatchAssignSheet");
            sheet.transform.SetParent(root.transform, false);
            m_WatchAssignSheet = sheet;
            m_WatchAssignSheetRt = sheet.AddComponent<RectTransform>();
            m_WatchAssignSheetRt.anchorMin = new Vector2(0.5f, 0.5f);
            m_WatchAssignSheetRt.anchorMax = new Vector2(0.5f, 0.5f);
            m_WatchAssignSheetRt.pivot = new Vector2(0.5f, 0.5f);
            m_WatchAssignSheetRt.sizeDelta = new Vector2(sheetW, sheetH);
            m_WatchAssignSheetBg = AddQuickMenuRoundedBg(sheet, QuickMenuWatchBezelColor, true);
            QuickMenuApplyWatchBezelRadius(UI.ResolveGalleryElementCornerRadiusFraction());

            float originX = -sheetW * 0.5f + 4f + QuickMenuWatchAssignCell * 0.5f;
            float originY = sheetH * 0.5f - 4f - QuickMenuWatchAssignCell * 0.5f;
            for (int i = 0; i < n; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var act = QuickMenuWatchAssignCatalog[i];
                GameObject go = new GameObject("WatchAssignPick_" + i);
                go.transform.SetParent(sheet.transform, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(QuickMenuWatchAssignCell, QuickMenuWatchAssignCell);
                rt.anchoredPosition = new Vector2(originX + col * cell, originY - row * cell);

                Color nb = QmBackdropAssignedOpaque;
                Image img = AddQuickMenuRoundedBg(go, nb);
                Button btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.targetGraphic = img;
                string tip = QuickMenuGetActionTooltip(act, -1);
                QuickMenuAttachWatchHover(go, img, nb, QmBackdropAssignedHoverOpaque, tip);

                Sprite spr = QuickMenuGetAssignPopupIcon(act);
                if (spr != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(go.transform, false);
                    Image icon = iconGO.AddComponent<Image>();
                    icon.sprite = spr;
                    icon.color = Color.white;
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.sizeDelta = new Vector2(-6f, -6f);
                    irt.anchoredPosition = Vector2.zero;
                }

                int iCopy = i;
                btn.onClick.AddListener(() =>
                {
                    try
                    {
                        int target = m_WatchAssignTargetIdx;
                        if (target < 0 || target >= QuickMenuWatchAssignSlotCount) return;
                        QuickMenuSetWatchSlotAction(target, QuickMenuWatchAssignCatalog[iCopy]);
                    }
                    catch { }
                });
            }

            sheet.SetActive(false);
            QuickMenuPlaceWatchAssignSheet();
        }

        private void QuickMenuPlaceWatchAssignSheet()
        {
            if (m_WatchAssignSheetRt == null || m_WatchCanvasRT == null) return;
            float bezelW = m_WatchCanvasRT.sizeDelta.x;
            float side = m_WatchIsLeft ? 1f : -1f;
            m_WatchAssignSheetRt.anchoredPosition = new Vector2(
                side * (bezelW * 0.5f + 8f + m_WatchAssignSheetW * 0.5f), 0f);
        }

        private void QuickMenuSyncWatchAssignAll()
        {
            for (int i = 0; i < QuickMenuWatchAssignSlotCount; i++)
                QuickMenuSyncWatchAssignSlot(i);
        }

        private void QuickMenuSyncWatchAssignLiveIcons()
        {
            if (m_WatchAssignGos == null || !m_WatchVisibleNow) return;
            for (int i = 0; i < QuickMenuWatchAssignSlotCount; i++)
            {
                var act = QuickMenuGetWatchSlotAction(i);
                if (act == QuickMenuAssignableAction.ShowHide ||
                    act == QuickMenuAssignableAction.ReplaceAddToggle ||
                    act == QuickMenuAssignableAction.AutoHideGallery ||
                    act == QuickMenuAssignableAction.ShowHiddenPackages ||
                    act == QuickMenuAssignableAction.FpsCounter ||
                    act == QuickMenuAssignableAction.PerfMode)
                    QuickMenuSyncWatchAssignSlot(i);
            }
        }

        private void QuickMenuSyncWatchAssignSlot(int slotIdx)
        {
            if (m_WatchAssignGos == null || slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return;
            GameObject go = m_WatchAssignGos[slotIdx];
            if (go == null) return;

            var act = QuickMenuGetWatchSlotAction(slotIdx);
            Sprite icon = QuickMenuGetAssignPopupIcon(act);
            if (act == QuickMenuAssignableAction.None && m_WatchEditMode)
                icon = m_QmIconAssignEmpty ?? icon;

            Image slotIcon = m_WatchAssignIcons[slotIdx];
            if (slotIcon == null)
            {
                Transform existing = go.transform.Find("Icon");
                if (existing != null) slotIcon = existing.GetComponent<Image>();
                if (slotIcon == null && icon != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(go.transform, false);
                    slotIcon = iconGO.AddComponent<Image>();
                    slotIcon.color = Color.white;
                    slotIcon.preserveAspect = true;
                    slotIcon.raycastTarget = false;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.sizeDelta = new Vector2(-8f, -8f);
                    irt.anchoredPosition = Vector2.zero;
                }
                m_WatchAssignIcons[slotIdx] = slotIcon;
            }
            if (slotIcon != null)
            {
                if (icon == null)
                {
                    if (slotIcon.gameObject.activeSelf) slotIcon.gameObject.SetActive(false);
                }
                else
                {
                    if (!slotIcon.gameObject.activeSelf) slotIcon.gameObject.SetActive(true);
                    if (slotIcon.sprite != icon) slotIcon.sprite = icon;
                }
            }

            Image bg = m_WatchAssignBackdrops[slotIdx];
            if (bg != null)
            {
                bool selected = m_WatchEditMode && slotIdx == m_WatchAssignTargetIdx;
                bool assigned = act != QuickMenuAssignableAction.None;
                Color normal;
                Color hover;
                if (selected)
                {
                    normal = QmBackdropEditOnOpaque;
                    hover = QmBackdropEditOnHoverOpaque;
                }
                else if (assigned)
                {
                    normal = QmBackdropAssignedOpaque;
                    hover = QmBackdropAssignedHoverOpaque;
                }
                else
                {
                    normal = QmBackdropEmptyOpaque;
                    hover = QmBackdropEmptyHoverOpaque;
                }
                QuickMenuApplyBackdropColors(bg, normal, hover);
            }
        }

        private void QuickMenuSyncWatchPageLabel()
        {
            if (m_WatchPageText == null) return;
            int p = m_WatchCurrentPage;
            if (p < 0) p = 0;
            if (p == m_WatchPageShown && m_WatchPageText.text != null && m_WatchPageText.text.Length > 0) return;
            m_WatchPageShown = p;
            m_WatchPageText.text = (p + 1).ToString() + "/" + QuickMenuPageCount.ToString();
        }

        internal void QuickMenuSyncWatchSlot(int hudIdx)
        {
            if (m_WatchSlotGos == null || hudIdx < 0 || hudIdx >= QuickMenuWatchHudSlotCount) return;
            GameObject watchGo = m_WatchSlotGos[hudIdx];
            if (watchGo == null) return;

            GameObject hudGo = null;
            if (m_QuickMenuGridButtons != null && hudIdx < m_QuickMenuGridButtons.Length)
                hudGo = m_QuickMenuGridButtons[hudIdx];

            bool hudOn = hudGo != null && hudGo.activeSelf;
            if (watchGo.activeSelf != hudOn) watchGo.SetActive(hudOn);
            if (!hudOn) return;

            Sprite icon = null;
            if (hudGo != null)
            {
                Transform iconTr = hudGo.transform.Find("Icon");
                if (iconTr != null)
                {
                    Image hudIcon = iconTr.GetComponent<Image>();
                    if (hudIcon != null) icon = hudIcon.sprite;
                }
            }
            if (icon == null) icon = QuickMenuGetAssignPopupIcon(QuickMenuGetSlotAction(hudIdx));

            Image slotIcon = m_WatchSlotIcons[hudIdx];
            if (slotIcon == null)
            {
                Transform existing = watchGo.transform.Find("Icon");
                if (existing != null) slotIcon = existing.GetComponent<Image>();
                if (slotIcon == null && icon != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(watchGo.transform, false);
                    slotIcon = iconGO.AddComponent<Image>();
                    slotIcon.color = Color.white;
                    slotIcon.preserveAspect = true;
                    slotIcon.raycastTarget = false;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.sizeDelta = new Vector2(-8f, -8f);
                    irt.anchoredPosition = Vector2.zero;
                }
                m_WatchSlotIcons[hudIdx] = slotIcon;
            }
            if (slotIcon != null && slotIcon.sprite != icon) slotIcon.sprite = icon;

            bool assigned = QuickMenuGetSlotAction(hudIdx) != QuickMenuAssignableAction.None;
            Image bg = m_WatchSlotBackdrops[hudIdx];
            if (bg != null)
            {
                Color normal = assigned ? QmBackdropAssignedOpaque : QmBackdropEmptyOpaque;
                Color hover = assigned ? QmBackdropAssignedHoverOpaque : QmBackdropEmptyHoverOpaque;
                QuickMenuApplyBackdropColors(bg, normal, hover);
            }
        }

        private void QuickMenuRefreshWatchStatus()
        {
            if (m_WatchStatusText == null) return;
            if (m_WatchHoverStatus != null)
            {
                if (!string.Equals(m_WatchStatusShown, m_WatchHoverStatus, System.StringComparison.Ordinal))
                {
                    m_WatchStatusText.text = m_WatchHoverStatus;
                    m_WatchStatusShown = m_WatchHoverStatus;
                }
                return;
            }

            if (!m_WatchStatusNeedRebuild && m_WatchStatusShown.Length > 0) return;
            m_WatchStatusNeedRebuild = false;

            string hand = m_WatchIsLeft
                ? VPBTranslation.T("hook.watch.hand.left", "L")
                : VPBTranslation.T("hook.watch.hand.right", "R");
            string mode;
            if (m_WatchEditMode)
                mode = VPBTranslation.T("hook.watch.status.edit", "EDIT");
            else if (m_WatchPinned)
                mode = VPBTranslation.T("hook.watch.status.pinned", "pinned");
            else
            {
                switch (m_WatchCfgShowWhen)
                {
                    case QuickMenuVrWatchShowWhen.Always:
                        mode = VPBTranslation.T("hook.watch.status.always", "always");
                        break;
                    case QuickMenuVrWatchShowWhen.Menu:
                        mode = VPBTranslation.T("hook.watch.status.menu", "menu");
                        break;
                    default:
                        mode = VPBTranslation.T("hook.watch.status.glance", "glance");
                        break;
                }
            }
            string s = hand + " · " + mode;
            if (!string.Equals(m_WatchStatusShown, s, System.StringComparison.Ordinal))
            {
                m_WatchStatusText.text = s;
                m_WatchStatusShown = s;
            }
        }

        private void QuickMenuRefreshWatchChromeColors()
        {
            if (m_WatchPinBg != null)
            {
                bool on = m_WatchPinned || m_WatchCfgShowWhen == QuickMenuVrWatchShowWhen.Always;
                Color normal = on ? QuickMenuWatchChromeOn : QuickMenuWatchChromeIdle;
                Color hover = on ? QuickMenuWatchChromeOnHover : QuickMenuWatchChromeIdleHover;
                m_WatchPinBg.color = normal;
                if (m_WatchPinHover != null)
                {
                    m_WatchPinHover.normal = normal;
                    m_WatchPinHover.hover = hover;
                }
                if (m_WatchPinTip != null) m_WatchPinTip.tip = QuickMenuWatchPinTipText();
            }
            if (m_WatchEditBg != null)
            {
                Color normal = m_WatchEditMode ? QuickMenuWatchChromeOn : QuickMenuWatchChromeIdle;
                Color hover = m_WatchEditMode ? QuickMenuWatchChromeOnHover : QuickMenuWatchChromeIdleHover;
                m_WatchEditBg.color = normal;
                if (m_WatchEditHover != null)
                {
                    m_WatchEditHover.normal = normal;
                    m_WatchEditHover.hover = hover;
                }
                if (m_WatchEditTip != null) m_WatchEditTip.tip = QuickMenuWatchEditTipText();
            }
        }

        private void QuickMenuApplyWatchPose()
        {
            Transform t = m_WatchTf;
            if (t == null || m_WatchHand == null) return;

            Vector3 offset = m_WatchCfgOffset;
            if (!m_WatchIsLeft) offset.x = -offset.x;
            t.localPosition = offset;
            t.localRotation = QuickMenuWatchWristLocalRot;

            float lossy = m_WatchHand.lossyScale.x;
            if (lossy < 1e-5f) lossy = 1f;
            float metersPerPx = VpbWorldSpaceUiScale.MetersPerUiPixel * m_WatchCfgScaleMul;
            if (metersPerPx < 1e-5f) metersPerPx = VpbWorldSpaceUiScale.MetersPerUiPixel;
            float local = metersPerPx / lossy;
            if (Mathf.Abs(local - m_WatchLastLocalScale) > 1e-6f)
            {
                m_WatchLastLocalScale = local;
                t.localScale = new Vector3(local, local, local);
            }

            Transform cam = QuickMenuGetPlayerCamera();
            if (cam != null && m_WatchCfgToward != 0f)
            {
                Vector3 worldPos = t.position;
                Vector3 toCam = cam.position - worldPos;
                float d2 = toCam.sqrMagnitude;
                if (d2 > 1e-8f)
                {
                    float d = Mathf.Sqrt(d2);
                    worldPos += (toCam / d) * Mathf.Min(m_WatchCfgToward, d * 0.9f);
                    t.position = worldPos;
                }
            }

            if (m_WatchCfgFaceUser && cam != null)
            {
                Vector3 dir = t.position - cam.position;
                if (dir.sqrMagnitude > 1e-6f)
                    t.rotation = Quaternion.LookRotation(dir, cam.up);
            }
        }

        private static QuickMenuVrWatchMode QuickMenuParseWatchMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return QuickMenuVrWatchMode.OppositeToMenu;
            switch (s)
            {
                case "Left only": return QuickMenuVrWatchMode.LeftOnly;
                case "Right only": return QuickMenuVrWatchMode.RightOnly;
                case "Same hand": return QuickMenuVrWatchMode.SameAsMenu;
                default: return QuickMenuVrWatchMode.OppositeToMenu;
            }
        }

        private static QuickMenuVrWatchShowWhen QuickMenuParseWatchShowWhen(string s)
        {
            if (string.IsNullOrEmpty(s)) return QuickMenuVrWatchShowWhen.Glance;
            switch (s)
            {
                case "Menu": return QuickMenuVrWatchShowWhen.Menu;
                case "Always": return QuickMenuVrWatchShowWhen.Always;
                default: return QuickMenuVrWatchShowWhen.Glance;
            }
        }

        private void QuickMenuSwitchWatchHand()
        {
            var sc = SuperController.singleton;
            bool currentlyLeft;
            if (m_WatchHand != null && sc != null && m_WatchHand == sc.touchObjectLeft)
                currentlyLeft = true;
            else if (m_WatchHand != null && sc != null && m_WatchHand == sc.touchObjectRight)
                currentlyLeft = false;
            else
                currentlyLeft = m_WatchCfgHand == QuickMenuVrWatchMode.LeftOnly;

            bool wantLeft = !currentlyLeft;
            m_WatchSessionHand = wantLeft ? (byte)1 : (byte)2;
            m_WatchLatchedHand = (sc != null) ? (wantLeft ? sc.touchObjectLeft : sc.touchObjectRight) : null;
            m_WatchLatchPending = false;
            m_WatchStatusNeedRebuild = true;

            if (m_WatchCfgRememberHand)
            {
                var c = VPBConfig.Instance;
                if (c != null)
                {
                    c.QuickMenuVrWatchMode = wantLeft ? "Left only" : "Right only";
                    try { c.TriggerChange(); } catch { }
                }
            }
        }

        private void QuickMenuToggleWatchPin()
        {
            m_WatchPinned = !m_WatchPinned;
            m_WatchStatusNeedRebuild = true;
            QuickMenuRefreshWatchChromeColors();
        }

        private void QuickMenuSetWatchVisibleFromWatch(bool on)
        {
            var c = VPBConfig.Instance;
            if (c == null) return;
            c.QuickMenuVrWatchVisible = on;
            try { c.Save(); } catch { }
            try
            {
                var g = Gallery.singleton;
                if (g != null && g.Panels != null)
                {
                    for (int i = 0; i < g.Panels.Count; i++)
                    {
                        var p = g.Panels[i];
                        if (p != null) p.NotifyFooterVrWatchState();
                    }
                }
            }
            catch { }
        }

        internal void QuickMenuSetWatchHoverStatus(string msg)
        {
            m_WatchHoverStatus = msg;
            m_WatchStatusNeedRebuild = true;
        }

        internal void QuickMenuClearWatchHoverStatus(string expected)
        {
            if (expected != null && m_WatchHoverStatus != expected) return;
            m_WatchHoverStatus = null;
            m_WatchStatusNeedRebuild = true;
        }

        internal void QuickMenuBeginWatchCalibrate()
        {
            m_WatchCalibrating = true;
        }

        internal void QuickMenuDragWatchOffset(Vector3 worldDelta)
        {
            if (m_WatchHand == null) return;
            Vector3 local = m_WatchHand.InverseTransformVector(worldDelta);
            if (!m_WatchIsLeft) local.x = -local.x;
            var c = VPBConfig.Instance;
            if (c == null) return;
            Vector3 o = c.QuickMenuVrWatchOffset + local;
            o.x = Mathf.Clamp(o.x, -0.2f, 0.2f);
            o.y = Mathf.Clamp(o.y, -0.2f, 0.2f);
            o.z = Mathf.Clamp(o.z, -0.2f, 0.2f);
            c.QuickMenuVrWatchOffset = o;
            m_WatchCfgOffset = o;
        }

        internal void QuickMenuEndWatchCalibrate()
        {
            m_WatchCalibrating = false;
            try
            {
                var c = VPBConfig.Instance;
                if (c != null) c.Save(false, true);
            }
            catch { }
        }

        private static Transform QuickMenuGetPlayerCamera()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null) return sc.centerCameraTarget.transform;
            }
            catch { }
            return null;
        }

        private static bool QuickMenuIsVamMenuVisible()
        {
            var sc = SuperController.singleton;
            return sc != null && sc.mainHUD != null && sc.mainHUD.gameObject != null &&
                   sc.mainHUD.gameObject.activeInHierarchy;
        }
    }

    internal sealed class VrWatchBezelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public VamHookPlugin owner;
        private Vector3 m_LastWorld;
        private bool m_Active;
        private Camera m_Cam;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner == null || eventData == null) return;
            if (!UIFloatPanelDrag.AcceptPointerButton(eventData)) return;
            m_Cam = eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    transform as RectTransform, eventData.position, m_Cam, out m_LastWorld))
                return;
            m_Active = true;
            owner.QuickMenuBeginWatchCalibrate();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!m_Active || owner == null || eventData == null) return;
            Vector3 world;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    transform as RectTransform, eventData.position, m_Cam, out world))
                return;
            owner.QuickMenuDragWatchOffset(world - m_LastWorld);
            m_LastWorld = world;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!m_Active) return;
            m_Active = false;
            if (owner != null) owner.QuickMenuEndWatchCalibrate();
        }

        private void OnDisable()
        {
            if (m_Active && owner != null) owner.QuickMenuEndWatchCalibrate();
            m_Active = false;
        }
    }

    internal sealed class VrWatchSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public VamHookPlugin owner;
        public int hudSlotIdx;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null) return;
            string tip = owner.QuickMenuWatchHoverLabel(hudSlotIdx);
            owner.QuickMenuSetWatchHoverStatus(tip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuClearWatchHoverStatus(owner.QuickMenuWatchHoverLabel(hudSlotIdx));
        }
    }

    internal sealed class VrWatchAssignHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public VamHookPlugin owner;
        public int slotIdx;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null) return;
            string tip = owner.QuickMenuWatchAssignHoverLabel(slotIdx);
            owner.QuickMenuSetWatchHoverStatus(tip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuClearWatchHoverStatus(owner.QuickMenuWatchAssignHoverLabel(slotIdx));
        }
    }

    internal sealed class VrWatchHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public VamHookPlugin owner;
        public string tip;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null || string.IsNullOrEmpty(tip)) return;
            owner.QuickMenuSetWatchHoverStatus(tip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuClearWatchHoverStatus(tip);
        }
    }
}
