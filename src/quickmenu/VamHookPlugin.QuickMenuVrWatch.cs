using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.src.util;

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

    internal enum QuickMenuWatchFaceMode
    {
        Collapsed,
        Compact,
        Expanded,
    }

    public partial class VamHookPlugin
    {
        private const float QuickMenuWatchLatchDelaySec = 0.25f;
        private const float QuickMenuWatchGlanceShowDot = 0.42f;
        private const float QuickMenuWatchGlanceHideDot = 0.22f;
        private const float QuickMenuWatchGlanceLookShow = 0.80f;
        private const float QuickMenuWatchGlanceLookHide = 0.65f;
        private const float QuickMenuWatchGlanceMaxDistSqr = 4f;
        private const float QuickMenuWatchGlanceLookMaxDistSqr = 0.64f;
        private const float QuickMenuWatchTutorialSec = 6f;
        private const float QuickMenuWatchFadeSec = 0.12f;
        private const float QuickMenuWatchFadeScaleFrom = 0.88f;
        // Reach radii for freeze-on-approach, squared. 0.25 m/0.35 m (the old values) fire when the
        // hands are merely held together, which reads as the watch pinning itself.
        private const float QuickMenuWatchFreezeEnterSqr = 0.0196f;  // 0.14 m
        private const float QuickMenuWatchFreezeExitSqr = 0.04f;     // 0.20 m
        private const float QuickMenuWatchFreezeBreakSqr = 0.0625f;  // 0.25 m of wrist travel
        private const float QuickMenuWatchHoldConfirmSec = 0.4f;
        private const float QuickMenuWatchShoulderOutM = 0.18f;
        private const float QuickMenuWatchShoulderDownM = 0.22f;
        private const float QuickMenuWatchGripHoldSec = 0.3f;
        private const float QuickMenuWatchGripCooldownSec = 0.7f;
        private const float QuickMenuWatchGripAnalog = 0.65f;
        private const int QuickMenuWatchCanvasMaxFails = 3;
        private const float QuickMenuWatchCanvasRetrySec = 3f;
        private const float QuickMenuWatchRaycastFade = 0.5f;

        private const float QuickMenuWatchBtn = 40f;
        private const float QuickMenuWatchGap = 10f;
        private const float QuickMenuWatchRailGap = 8f;
        private const float QuickMenuWatchChrome = 32f;
        private const float QuickMenuWatchChromeSm = 28f;
        private const float QuickMenuWatchChromeGap = 8f;
        private const float QuickMenuWatchChromeGapSm = 6f;
        private const float QuickMenuWatchPagerBtn = 24f;
        private const float QuickMenuWatchPagerGap = 4f;
        private const float QuickMenuWatchDotSize = 34f;
        private const float QuickMenuWatchDotPad = 4f;
        private const float QuickMenuWatchLabelH = 16f;
        private const float QuickMenuWatchBezelCornerPx = 8f;
        private const float QuickMenuWatchPad = 10f;
        private const float QuickMenuWatchStatusH = 26f;
        private const float QuickMenuWatchStatusGap = 6f;
        private const float QuickMenuWatchCompactCols = 3f;
        private const int QuickMenuWatchCompactSlotCount = 6;
        private const int QuickMenuWatchHudSlotCount = 4;
        private const int QuickMenuWatchAssignSlotCount = VPBConfig.QuickMenuVrWatchAssignSlotCount;
        private const int QuickMenuWatchChromeCount = 6;
        private const int QuickMenuWatchAssignSheetCols = 9;
        private const float QuickMenuWatchAssignCellW = 52f;
        private const float QuickMenuWatchAssignCellH = 42f;
        private const float QuickMenuWatchAssignIcon = 26f;
        private const float QuickMenuWatchAssignGap = 4f;
        private const float QuickMenuWatchAssignHeaderH = 20f;
        private const float QuickMenuWatchAssignSectionGap = 6f;
        private const float QuickMenuWatchAssignPad = 8f;
        private const int QuickMenuWatchStatusFont = 20;
        private const int QuickMenuWatchLabelFont = 12;
        private const int QuickMenuWatchCueFont = 16;
        private const int QuickMenuWatchTipFont = 17;
        private const float QuickMenuWatchTipW = 320f;
        private const float QuickMenuWatchTipH = 62f;
        private const float QuickMenuWatchTipGap = 10f;
        private const int QuickMenuWatchSheetLabelFont = 11;
        private const int QuickMenuWatchSheetHeaderFont = 14;
        private const float QuickMenuWatchFontDppu = 8f;

        private const int QuickMenuWatchChromeCollapse = 0;
        private const int QuickMenuWatchChromeHand = 1;
        private const int QuickMenuWatchChromePin = 2;
        private const int QuickMenuWatchChromeEdit = 3;
        private const int QuickMenuWatchChromeExpand = 4;
        private const int QuickMenuWatchChromeHelp = 5;

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
            QuickMenuAssignableAction.AutoHideGallery,
            QuickMenuAssignableAction.ShowHiddenPackages,
            QuickMenuAssignableAction.FpsCounter,
            QuickMenuAssignableAction.History,
            QuickMenuAssignableAction.PerfMode,
            QuickMenuAssignableAction.ToggleImportSidebar,

            QuickMenuAssignableAction.RemoveAllClothing,
            QuickMenuAssignableAction.RemoveAllHair,
            QuickMenuAssignableAction.StarFilter,
            QuickMenuAssignableAction.CompressCache,

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

        private static readonly int[] QuickMenuWatchAssignSectionStarts = { 0, 7, 14, 21, 25, 27, 36 };

        private static readonly Quaternion QuickMenuWatchWristLocalRot = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Color QuickMenuWatchBezelColor = new Color(0.10f, 0.10f, 0.10f, 0.92f);
        private static readonly Color QuickMenuWatchChromeIdle = new Color(0.22f, 0.22f, 0.22f, 0.95f);
        private static readonly Color QuickMenuWatchChromeIdleHover = new Color(0.34f, 0.34f, 0.34f, 0.95f);
        private static readonly Color QuickMenuWatchChromeOn = new Color(0.28f, 0.38f, 0.31f, 0.95f);
        private static readonly Color QuickMenuWatchChromeOnHover = new Color(0.36f, 0.50f, 0.40f, 0.95f);
        private static readonly Color QuickMenuWatchChromeDanger = new Color(0.44f, 0.20f, 0.20f, 0.95f);
        private static readonly Color QuickMenuWatchChromeDangerHover = new Color(0.58f, 0.26f, 0.26f, 0.95f);
        private static readonly Color QuickMenuWatchSlotDisabled = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color QuickMenuWatchLabelColor = new Color(0.78f, 0.78f, 0.80f, 1f);
        private static readonly Color QuickMenuWatchEmptyIconTint = new Color(1f, 1f, 1f, 0.3f);
        private static readonly Color QuickMenuWatchCueBackdrop = new Color(0.06f, 0.06f, 0.07f, 0.94f);

        private Canvas m_WatchCanvas;
        private RectTransform m_WatchCanvasRT;
        private CanvasGroup m_WatchCanvasGroup;
        private Transform m_WatchTf;
        private Image m_WatchBezel;
        private RectTransform m_WatchBezelRt;
        private Text m_WatchStatusText;
        private RectTransform m_WatchStatusRt;
        private Text m_WatchCueText;
        private GameObject m_WatchCueGo;
        private RectTransform m_WatchCueRt;
        private GameObject m_WatchTipGo;
        private RectTransform m_WatchTipRt;
        private Text m_WatchTipText;
        private GameObject[] m_WatchSlotGos;
        private RectTransform[] m_WatchSlotRts;
        private Image[] m_WatchSlotIcons;
        private RectTransform[] m_WatchSlotIconRts;
        private Image[] m_WatchSlotBackdrops;
        private Text[] m_WatchSlotLabels;
        private GameObject[] m_WatchAssignGos;
        private RectTransform[] m_WatchAssignRts;
        private Image[] m_WatchAssignIcons;
        private RectTransform[] m_WatchAssignIconRts;
        private Image[] m_WatchAssignBackdrops;
        private Text[] m_WatchAssignLabels;
        private GameObject[] m_WatchChromeGos;
        private RectTransform[] m_WatchChromeRts;
        private Image[] m_WatchChromeBgs;
        private QuickMenuSquareHover[] m_WatchChromeHovers;
        private VrWatchHoverTip[] m_WatchChromeTips;
        private Image[] m_WatchChromeIcons;
        private Sprite m_WatchIconExpand;
        private Sprite m_WatchIconCompact;
        private Sprite m_WatchIconPinOn;
        private Sprite m_WatchIconPinOff;
        private GameObject m_WatchPagerPrevGo;
        private RectTransform m_WatchPagerPrevRt;
        private GameObject m_WatchPagerNextGo;
        private RectTransform m_WatchPagerNextRt;
        private GameObject m_WatchDotGo;
        private RectTransform m_WatchDotRt;

        private QuickMenuAssignableAction[][] m_WatchPageAssignments;
        private int m_WatchCurrentPage;
        private bool m_WatchEditMode;
        private int m_WatchAssignTargetIdx = -1;
        private GameObject m_WatchAssignSheet;
        private RectTransform m_WatchAssignSheetRt;
        private Image m_WatchAssignSheetBg;
        private Image[] m_WatchAssignPickBgs;
        private float m_WatchAssignSheetW;

        private Transform m_WatchHand;
        private Transform m_WatchOtherHand;
        private Transform m_WatchLatchedHand;
        private Transform m_WatchHandLeft;
        private Transform m_WatchHandRight;
        private bool m_WatchMenuWasVisible;
        private bool m_WatchLatchPending;
        private float m_WatchOpenTime;
        private bool m_WatchVisibleNow;
        private bool m_WatchActive;
        private float m_WatchFade;
        private bool m_WatchGlanced;
        private float m_WatchGlanceCandidateSince = -1f;
        private bool m_WatchWorldLocked;
        private bool m_WatchWorldLockBeforeEdit;
        private bool m_WatchFrozen;
        private bool m_WatchCalibrating;
        private bool m_WatchIsLeft;
        private byte m_WatchSessionHand;
        private float m_WatchTutorialUntil;
        private bool m_WatchStatusNeedRebuild = true;
        private string m_WatchStatusShown = "";
        private string m_WatchHoverStatus;
        private int m_WatchHoverToken;
        private bool m_WatchAddedToSc;
        // Set when the retina read-back caught the face inverted; negates the up hint from then on.
        private bool m_WatchFaceAimFlip;
        private bool m_WatchFaceFlipLogged;
        private float m_WatchLastAppliedScale = -1f;
        private QuickMenuWatchFaceMode m_WatchFaceMode = QuickMenuWatchFaceMode.Compact;
        private QuickMenuWatchFaceMode m_WatchLayoutApplied = (QuickMenuWatchFaceMode)(-1);
        private bool m_WatchLayoutLabels;
        private bool m_WatchLayoutEdit;
        private bool m_WatchHudEditShown;
        private int m_WatchPressKind;
        private int m_WatchPressIdx = -1;
        private float m_WatchPressStart;
        private bool m_WatchHoldFired;
        private QuickMenuAssignableAction m_WatchPressAction;
        private readonly StringBuilder m_WatchStatusSb = new StringBuilder(64);
        private string m_WatchTipSrc;
        private string m_WatchTipFlat;
        private string m_WatchTipShown = "";

        private bool m_WatchCfgVisible = true;
        private QuickMenuVrWatchMode m_WatchCfgHand = QuickMenuVrWatchMode.OppositeToMenu;
        private QuickMenuVrWatchShowWhen m_WatchCfgShowWhen = QuickMenuVrWatchShowWhen.Glance;
        private bool m_WatchCfgFaceUser = true;
        private float m_WatchCfgShoulderBlend = VPBConfig.QuickMenuVrWatchShoulderBlendDefault;
        private bool m_WatchCfgRememberHand;
        private bool m_WatchCfgFreeze = true;
        private bool m_WatchCfgGripPin = true;
        private float m_WatchGripHeldSince = -1f;
        private float m_WatchGripReadyAt;
        private bool m_WatchCfgLabels;
        private bool m_WatchCfgHoldConfirm = true;
        private float m_WatchCfgGlanceDwell = VPBConfig.QuickMenuVrWatchGlanceDwellDefault;
        private float m_WatchCfgScaleMul = 1f;
        private float m_WatchCfgToward = 0.04f;
        private Vector3 m_WatchCfgOffset = VPBConfig.QuickMenuVrWatchOffsetDefault;
        private Vector3 m_WatchCfgFaceRotEuler;
        private Quaternion m_WatchCfgFaceRot = Quaternion.identity;
        private bool m_WatchCfgFaceRotActive;
        private string m_WatchCfgHandRaw;
        private string m_WatchCfgShowRaw;
        private bool m_WatchAssignmentsLoaded;
        private bool m_WatchFailLogged;
        private bool m_WatchHandsDumpLogged;
        private bool m_WatchAttachLogged;
        private bool m_WatchUpdateErrorLogged;
        private float m_WatchEyeRetryAt;
        private int m_WatchCanvasFailCount;
        private float m_WatchCanvasRetryAt;

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
            m_WatchCanvasGroup = null;
            m_WatchTf = null;
            m_WatchBezel = null;
            m_WatchBezelRt = null;
            m_WatchStatusText = null;
            m_WatchStatusRt = null;
            m_WatchCueText = null;
            m_WatchCueGo = null;
            m_WatchCueRt = null;
            m_WatchTipGo = null;
            m_WatchTipRt = null;
            m_WatchTipText = null;
            QuickMenuClearWatchRandomPreviewRefs();
            m_WatchTipSrc = null;
            m_WatchTipShown = "";
            m_WatchHoverStatus = null;
            m_WatchSlotGos = null;
            m_WatchSlotRts = null;
            m_WatchSlotIcons = null;
            m_WatchSlotIconRts = null;
            m_WatchSlotBackdrops = null;
            m_WatchSlotLabels = null;
            m_WatchAssignGos = null;
            m_WatchAssignRts = null;
            m_WatchAssignIcons = null;
            m_WatchAssignIconRts = null;
            m_WatchAssignBackdrops = null;
            m_WatchAssignLabels = null;
            m_WatchChromeGos = null;
            m_WatchChromeRts = null;
            m_WatchChromeBgs = null;
            m_WatchChromeHovers = null;
            m_WatchChromeTips = null;
            m_WatchChromeIcons = null;
            m_WatchPagerPrevGo = null;
            m_WatchPagerPrevRt = null;
            m_WatchPagerNextGo = null;
            m_WatchPagerNextRt = null;
            m_WatchDotGo = null;
            m_WatchDotRt = null;
            m_WatchAssignTargetIdx = -1;
            m_WatchAssignSheet = null;
            m_WatchAssignSheetRt = null;
            m_WatchAssignSheetBg = null;
            m_WatchAssignPickBgs = null;
            m_WatchHand = null;
            m_WatchOtherHand = null;
            m_WatchLatchedHand = null;
            m_WatchHandLeft = null;
            m_WatchHandRight = null;
            m_WatchVisibleNow = false;
            m_WatchActive = false;
            m_WatchFade = 0f;
            m_WatchFrozen = false;
            m_WatchWorldLocked = false;
            m_WatchCalibrating = false;
            m_WatchEditMode = false;
            m_WatchPressKind = 0;
            m_WatchPressIdx = -1;
            m_WatchHoldFired = false;
            m_WatchAddedToSc = false;
            // Rebuilt canvas may land under a different hand/basis — re-derive the flip from scratch.
            m_WatchFaceAimFlip = false;
            m_WatchLastAppliedScale = -1f;
            m_WatchLayoutApplied = (QuickMenuWatchFaceMode)(-1);
            m_WatchFailLogged = false;
            m_WatchAttachLogged = false;
            m_WatchHudEditShown = false;
            // Shown-string caches must die with the widgets they mirror: a rebuilt Text starts
            // empty, so a stale cache would suppress the first assignment and leave it blank.
            m_WatchStatusShown = "";
            m_WatchTipFlat = null;
            m_WatchStatusNeedRebuild = true;
        }

        internal void QuickMenuLogWatchUpdateError(System.Exception ex)
        {
            if (m_WatchUpdateErrorLogged) return;
            m_WatchUpdateErrorLogged = true;
            try
            {
                LogUtil.LogWarning("[VPB] VR wrist watch update failed: " + (ex != null ? ex.Message : "unknown"));
            }
            catch { }
        }

        internal void QuickMenuOnWatchVisibleToggled(bool on)
        {
            QuickMenuNotifyWatchFooters();
            if (!on)
            {
                m_WatchWorldLocked = false;
                m_WatchGlanced = false;
                m_WatchGlanceCandidateSince = -1f;
                m_WatchTutorialUntil = 0f;
                return;
            }
            var c = VPBConfig.Instance;
            if (c != null && c.QuickMenuVrWatchCollapsed)
            {
                c.QuickMenuVrWatchCollapsed = false;
                try { c.Save(false, true); } catch { }
            }
            m_WatchGlanced = true;
            try { m_WatchTutorialUntil = Time.unscaledTime + QuickMenuWatchTutorialSec; }
            catch { m_WatchTutorialUntil = 0f; }
        }

        private static void QuickMenuNotifyWatchFooters()
        {
            try
            {
                var g = Gallery.singleton;
                if (g == null || g.Panels == null) return;
                for (int i = 0; i < g.Panels.Count; i++)
                {
                    var p = g.Panels[i];
                    if (p != null) p.NotifyFooterVrWatchState();
                }
            }
            catch { }
        }

        internal bool QuickMenuIsWatchShown()
        {
            return m_WatchVisibleNow && m_WatchCanvas != null &&
                   m_WatchCanvas.gameObject.activeInHierarchy;
        }

        private void QuickMenuLogWatchFailOnce(string reason)
        {
            if (m_WatchFailLogged) return;
            m_WatchFailLogged = true;
            try { LogUtil.LogWarning("[VPB] VR wrist watch hidden: " + reason); } catch { }
        }

        private void QuickMenuLogWatchHandsDumpOnce(SuperController sc, string reason)
        {
            if (m_WatchHandsDumpLogged) return;
            m_WatchHandsDumpLogged = true;
            m_WatchFailLogged = true;
            try
            {
                StringBuilder sb = new StringBuilder(256);
                sb.Append("[VPB] VR wrist watch hidden: ").Append(reason);
                if (sc != null)
                {
                    bool openVr = false;
                    bool ovr = false;
                    try { openVr = sc.isOpenVR; } catch { }
                    try { ovr = sc.isOVR; } catch { }
                    sb.Append(" | isOpenVR=").Append(openVr ? "1" : "0");
                    sb.Append(" isOVR=").Append(ovr ? "1" : "0");
                    QuickMenuAppendHandDump(sb, " viveMountL", sc.viveHandMountLeft);
                    QuickMenuAppendHandDump(sb, " viveMountR", sc.viveHandMountRight);
                    QuickMenuAppendHandDump(sb, " viveL", sc.viveObjectLeft);
                    QuickMenuAppendHandDump(sb, " viveR", sc.viveObjectRight);
                    QuickMenuAppendHandDump(sb, " touchL", sc.touchObjectLeft);
                    QuickMenuAppendHandDump(sb, " touchR", sc.touchObjectRight);
                }
                LogUtil.LogWarning(sb.ToString());
            }
            catch { }
        }

        private void QuickMenuLogWatchAttachOnce(Transform hand)
        {
            if (m_WatchAttachLogged || hand == null) return;
            m_WatchAttachLogged = true;
            try
            {
                var sc = SuperController.singleton;
                bool openVr = false;
                try { if (sc != null) openVr = sc.isOpenVR; } catch { }
                Vector3 p = hand.position;
                LogUtil.Log("[VPB] VR wrist watch attached | openVR=" + (openVr ? "1" : "0")
                    + " left=" + (m_WatchIsLeft ? "1" : "0")
                    + " hand=" + hand.name
                    + " pos=" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2"));
            }
            catch { }
        }

        private static void QuickMenuAppendHandDump(StringBuilder sb, string label, Transform t)
        {
            sb.Append(label).Append('=');
            if (t == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append(t.name);
            bool active = false;
            try { active = t.gameObject.activeInHierarchy; } catch { }
            sb.Append(" act=").Append(active ? "1" : "0");
            Vector3 p = t.position;
            sb.Append(" (").Append(p.x.ToString("F2")).Append(',').Append(p.y.ToString("F2")).Append(',').Append(p.z.ToString("F2")).Append(')');
        }

        internal void QuickMenuSyncWatchCornerRadius(float frac)
        {
            QuickMenuApplyWatchBezelRadius(frac);
            QuickMenuSyncWatchRoundedArray(m_WatchSlotBackdrops, frac);
            QuickMenuSyncWatchRoundedArray(m_WatchAssignBackdrops, frac);
            QuickMenuSyncWatchRoundedArray(m_WatchChromeBgs, frac);
            QuickMenuSyncWatchRoundedArray(m_WatchAssignPickBgs, frac);
        }

        private static void QuickMenuSyncWatchRoundedArray(Image[] imgs, float frac)
        {
            if (imgs == null) return;
            for (int i = 0; i < imgs.Length; i++)
            {
                RoundedRect rr = imgs[i] as RoundedRect;
                if (rr != null) rr.cornerRadiusFraction = frac;
            }
        }

        /// <summary>
        /// Locale change. The assign sheet bakes its section headers, pick labels and per-pick
        /// tooltips at build time, so a piecemeal refresh cannot retranslate it — drop the whole
        /// canvas and let the next update rebuild it. Cold path (locale switches are rare).
        /// </summary>
        internal void QuickMenuInvalidateWatchStrings()
        {
            m_WatchTipSrc = null;
            m_WatchTipShown = "";
            m_WatchHoverStatus = null;
            m_WatchCanvasFailCount = 0;
            m_WatchCanvasRetryAt = 0f;
            QuickMenuDestroyWatch();
        }

        private void QuickMenuUpdateVrWatch()
        {
            if (!QuickMenuIsVrActive())
            {
                QuickMenuHideWatchImmediate();
                return;
            }

            QuickMenuPullWatchSettings();

            if (!m_WatchCfgVisible)
            {
                QuickMenuHideWatchImmediate();
                return;
            }

            float now = Time.unscaledTime;
            var sc = SuperController.singleton;
            GetVamVrHandTransforms(sc, out m_WatchHandLeft, out m_WatchHandRight);
            if (!QuickMenuHandLooksTracked(m_WatchHandLeft)) m_WatchHandLeft = null;
            if (!QuickMenuHandLooksTracked(m_WatchHandRight)) m_WatchHandRight = null;

            bool menuVis = QuickMenuIsVamMenuVisible();
            if (menuVis && !m_WatchMenuWasVisible)
            {
                m_WatchOpenTime = now;
                if (m_WatchSessionHand == 0 &&
                    (m_WatchCfgHand == QuickMenuVrWatchMode.OppositeToMenu ||
                     m_WatchCfgHand == QuickMenuVrWatchMode.SameAsMenu))
                {
                    m_WatchLatchPending = true;
                    m_WatchLatchedHand = null;
                }
                var cfg = VPBConfig.Instance;
                if (cfg != null && !cfg.QuickMenuVrWatchOnboardingSeen && m_WatchTutorialUntil <= 0f)
                    m_WatchTutorialUntil = now + QuickMenuWatchTutorialSec;
            }
            m_WatchMenuWasVisible = menuVis;

            Transform watchHand = QuickMenuResolveWatchHand(sc);
            if (watchHand == null)
            {
                QuickMenuHideWatchImmediate();
                return;
            }

            bool isLeft = QuickMenuIsLeftWatchHand(sc, watchHand);
            if (isLeft != m_WatchIsLeft)
            {
                m_WatchIsLeft = isLeft;
                m_WatchStatusNeedRebuild = true;
                QuickMenuPlaceWatchAssignSheet();
            }
            m_WatchOtherHand = isLeft ? m_WatchHandRight : m_WatchHandLeft;

            bool tutorial = m_WatchTutorialUntil > 0f && now < m_WatchTutorialUntil;
            if (m_WatchTutorialUntil > 0f && now >= m_WatchTutorialUntil)
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

            QuickMenuUpdateWatchGlance(watchHand, now);
            QuickMenuTickWatchGripPin(sc, now);

            bool wantShow;
            if (m_WatchCalibrating || tutorial || m_WatchWorldLocked || m_WatchEditMode)
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

            m_WatchVisibleNow = wantShow;

            if (!wantShow && m_WatchFade <= 0f)
            {
                QuickMenuSetWatchActive(false);
                return;
            }

            if (m_WatchCanvas == null) QuickMenuEnsureWatchCanvas();
            if (m_WatchTf == null) return;

            m_WatchHand = watchHand;
            QuickMenuEnsureWatchParent();

            if (!m_WatchTf.gameObject.activeInHierarchy && m_WatchTf.gameObject.activeSelf)
            {
                QuickMenuLogWatchFailOnce("watch parent inactive in hierarchy");
                QuickMenuHideWatchImmediate();
                return;
            }

            QuickMenuSetWatchActive(true);
            QuickMenuApplyWatchLayout();
            QuickMenuTickWatchHold(now);
            QuickMenuApplyWatchPose();
            QuickMenuTickWatchFade(wantShow);

            if (m_WatchCanvas != null && m_WatchCanvas.worldCamera == null && now >= m_WatchEyeRetryAt)
            {
                m_WatchEyeRetryAt = now + 0.5f;
                Camera eye = QuickMenuGetPlayerCameraComponent();
                if (eye != null) m_WatchCanvas.worldCamera = eye;
            }

            QuickMenuSyncWatchHudEditState();
            QuickMenuRefreshWatchStatus();
            QuickMenuSyncWatchAssignLiveIcons();
            QuickMenuSetWatchCueShown(tutorial);
        }

        private void QuickMenuEnsureWatchParent()
        {
            if (m_WatchTf == null) return;

            bool detached = m_WatchWorldLocked || m_WatchFrozen;
            Transform want = m_WatchHand;
            if (detached)
            {
                Transform root = null;
                try { root = VpbWorldSpaceUiScale.GetPlayerUiRoot(); } catch { }
                // Never leave a locked/frozen watch parented to the wrist — it would be dragged
                // along by the hand. With no usable UI root, park it at the scene root instead.
                want = (root != null && root != m_WatchHand && root.gameObject.activeInHierarchy)
                    ? root
                    : null;
            }
            else if (want == null) return;
            if (m_WatchTf.parent == want) return;

            Vector3 wp = m_WatchTf.position;
            Quaternion wr = m_WatchTf.rotation;
            m_WatchTf.SetParent(want, false);
            m_WatchLastAppliedScale = -1f;
            if (detached)
            {
                m_WatchTf.position = wp;
                m_WatchTf.rotation = wr;
            }
            QuickMenuLogWatchAttachOnce(m_WatchHand);
        }

        private void QuickMenuPullWatchSettings()
        {
            var c = VPBConfig.Instance;
            if (c == null) return;

            m_WatchCfgVisible = c.QuickMenuVrWatchVisible;
            m_WatchCfgFaceUser = c.QuickMenuVrWatchFaceUser;
            m_WatchCfgShoulderBlend = Mathf.Clamp01(c.QuickMenuVrWatchShoulderBlend);
            m_WatchCfgRememberHand = c.QuickMenuVrWatchRememberHand;
            m_WatchCfgFreeze = c.QuickMenuVrWatchFreezeOnApproach;

            bool gripPin = c.QuickMenuVrWatchGripPin;
            if (gripPin != m_WatchCfgGripPin)
            {
                m_WatchCfgGripPin = gripPin;
                m_WatchGripHeldSince = -1f;
                // Cue and pin tooltip both name the gesture.
                if (m_WatchCueText != null) m_WatchCueText.text = QuickMenuWatchCueTextValue();
                QuickMenuRefreshWatchChromeTips();
            }
            m_WatchCfgHoldConfirm = c.QuickMenuVrWatchHoldConfirm;
            m_WatchCfgGlanceDwell = c.QuickMenuVrWatchGlanceDwell;
            m_WatchCfgScaleMul = Mathf.Clamp(c.QuickMenuVrWatchScaleMul,
                VPBConfig.QuickMenuVrWatchScaleMulMin, VPBConfig.QuickMenuVrWatchScaleMulMax);
            m_WatchCfgToward = c.QuickMenuVrWatchTowardUserDist;
            m_WatchCfgOffset = c.QuickMenuVrWatchOffset;

            // Euler -> quaternion once per edit, not once per frame: this runs every Update.
            Vector3 faceRot = c.QuickMenuVrWatchFaceRotation;
            if (faceRot != m_WatchCfgFaceRotEuler)
            {
                m_WatchCfgFaceRotEuler = faceRot;
                m_WatchCfgFaceRotActive = faceRot.sqrMagnitude > 1e-6f;
                m_WatchCfgFaceRot = m_WatchCfgFaceRotActive ? Quaternion.Euler(faceRot) : Quaternion.identity;
            }

            bool labels = c.QuickMenuVrWatchLabels;
            if (labels != m_WatchCfgLabels)
            {
                m_WatchCfgLabels = labels;
                m_WatchLayoutApplied = (QuickMenuWatchFaceMode)(-1);
            }

            QuickMenuWatchFaceMode mode = c.QuickMenuVrWatchCollapsed
                ? QuickMenuWatchFaceMode.Collapsed
                : (c.QuickMenuVrWatchExpanded ? QuickMenuWatchFaceMode.Expanded : QuickMenuWatchFaceMode.Compact);
            if (mode != m_WatchFaceMode)
            {
                m_WatchFaceMode = mode;
                m_WatchStatusNeedRebuild = true;
            }

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

        private static bool QuickMenuHandLooksTracked(Transform hand)
        {
            if (hand == null) return false;
            try { return hand.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        /// <summary>
        /// Active VR controller mounts. Oculus (Quest Link) uses <c>touch*</c>; SteamVR/OpenVR uses
        /// <c>vive*</c>. VaM disables the unused rig, so <c>touchObject*</c> is dead on SteamVR.
        /// </summary>
        internal static void GetVamVrHandTransforms(SuperController sc, out Transform left, out Transform right)
        {
            left = null;
            right = null;
            if (sc == null) return;

            bool openVr = false;
            try { openVr = sc.isOpenVR; } catch { }

            if (openVr)
            {
                try { left = sc.viveHandMountLeft; } catch { }
                try { right = sc.viveHandMountRight; } catch { }
                if (left == null) try { left = sc.viveObjectLeft; } catch { }
                if (right == null) try { right = sc.viveObjectRight; } catch { }
            }
            else
            {
                try { left = sc.touchHandMountLeft; } catch { }
                try { right = sc.touchHandMountRight; } catch { }
                if (left == null) try { left = sc.touchObjectLeft; } catch { }
                if (right == null) try { right = sc.touchObjectRight; } catch { }
            }
            if (left == null) try { left = sc.leftHand; } catch { }
            if (right == null) try { right = sc.rightHand; } catch { }
        }

        private bool QuickMenuIsLeftWatchHand(SuperController sc, Transform hand)
        {
            if (hand == null || sc == null) return false;
            if (m_WatchHandLeft != null && hand == m_WatchHandLeft) return true;
            if (m_WatchHandRight != null && hand == m_WatchHandRight) return false;
            try
            {
                if (hand == sc.viveObjectLeft || hand == sc.viveHandMountLeft ||
                    hand == sc.viveCenterHandLeft || hand == sc.touchObjectLeft ||
                    hand == sc.touchHandMountLeft || hand == sc.leftHand)
                    return true;
            }
            catch { }
            return false;
        }

        private Transform QuickMenuResolveWatchHand(SuperController sc)
        {
            if (sc == null) return null;
            Transform left = m_WatchHandLeft;
            Transform right = m_WatchHandRight;

            if (left == null && right == null)
            {
                QuickMenuLogWatchHandsDumpOnce(sc, "no tracked VR controller");
                return null;
            }

            if (m_WatchSessionHand == 1) return left != null ? left : right;
            if (m_WatchSessionHand == 2) return right != null ? right : left;
            if (m_WatchCfgHand == QuickMenuVrWatchMode.LeftOnly) return left != null ? left : right;
            if (m_WatchCfgHand == QuickMenuVrWatchMode.RightOnly) return right != null ? right : left;

            Transform hud = sc.mainHUD;

            switch (m_WatchCfgHand)
            {
                case QuickMenuVrWatchMode.SameAsMenu:
                case QuickMenuVrWatchMode.OppositeToMenu:
                    if (m_WatchLatchedHand != null && !QuickMenuHandLooksTracked(m_WatchLatchedHand))
                    {
                        m_WatchLatchedHand = null;
                        m_WatchLatchPending = true;
                    }
                    if (m_WatchLatchedHand == null && hud != null && left != null && right != null &&
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
                    if (m_WatchLatchedHand != null && QuickMenuHandLooksTracked(m_WatchLatchedHand))
                        return m_WatchLatchedHand;
                    if (m_WatchCfgHand == QuickMenuVrWatchMode.SameAsMenu)
                        return right != null ? right : left;
                    return left != null ? left : right;
                default:
                    return left != null ? left : right;
            }
        }

        private void QuickMenuUpdateWatchGlance(Transform hand, float now)
        {
            Transform cam = QuickMenuGetPlayerCamera();
            if (cam == null || hand == null) return;

            Vector3 offset = m_WatchCfgOffset;
            if (!m_WatchIsLeft) offset.x = -offset.x;
            Vector3 watchPos = hand.TransformPoint(offset);
            Vector3 away = watchPos - cam.position;
            float mag2 = away.sqrMagnitude;
            if (mag2 < 0.0064f || mag2 > QuickMenuWatchGlanceMaxDistSqr) return;

            float inv = 1f / Mathf.Sqrt(mag2);
            Vector3 restFwd = (hand.rotation * QuickMenuWatchWristLocalRot) * Vector3.forward;
            float wristDot = (away.x * restFwd.x + away.y * restFwd.y + away.z * restFwd.z) * inv;

            float lookDot = -1f;
            if (mag2 <= QuickMenuWatchGlanceLookMaxDistSqr)
            {
                Vector3 camFwd = cam.forward;
                lookDot = (camFwd.x * away.x + camFwd.y * away.y + camFwd.z * away.z) * inv;
            }

            if (m_WatchGlanced)
            {
                bool hold = wristDot >= QuickMenuWatchGlanceHideDot || lookDot >= QuickMenuWatchGlanceLookHide;
                if (!hold)
                {
                    m_WatchGlanced = false;
                    m_WatchGlanceCandidateSince = -1f;
                }
                return;
            }

            bool candidate = wristDot >= QuickMenuWatchGlanceShowDot || lookDot >= QuickMenuWatchGlanceLookShow;
            if (!candidate)
            {
                m_WatchGlanceCandidateSince = -1f;
                return;
            }
            if (m_WatchGlanceCandidateSince < 0f)
            {
                m_WatchGlanceCandidateSince = now;
                if (m_WatchCfgGlanceDwell > 0f) return;
            }
            if (now - m_WatchGlanceCandidateSince >= m_WatchCfgGlanceDwell)
                m_WatchGlanced = true;
        }

        /// <summary>
        /// Squeeze the grip on the hand wearing the watch to take it off and leave it hanging in
        /// place; squeeze again to put it back on. One-handed, needs no aiming, and works on the
        /// arm the face is actually strapped to — the Pin chrome button needs the other controller.
        /// Pinning is gated on the face being turned toward you so a grab elsewhere in the scene
        /// cannot trip it; unpinning is not, because a pinned face is no longer on the wrist.
        /// </summary>
        private void QuickMenuTickWatchGripPin(SuperController sc, float now)
        {
            if (!m_WatchCfgGripPin || m_WatchEditMode || m_WatchCalibrating || sc == null)
            {
                m_WatchGripHeldSince = -1f;
                return;
            }

            bool held = false;
            try { held = m_WatchIsLeft ? sc.GetLeftHoldGrab() : sc.GetRightHoldGrab(); }
            catch { held = false; }
            if (!held)
            {
                // Wands report grip as a digital hold; analog grips can miss the above.
                try
                {
                    float v = m_WatchIsLeft ? sc.GetLeftGrabVal() : sc.GetRightGrabVal();
                    held = v >= QuickMenuWatchGripAnalog;
                }
                catch { held = false; }
            }

            if (!held)
            {
                m_WatchGripHeldSince = -1f;
                return;
            }
            if (now < m_WatchGripReadyAt) return;
            if (!m_WatchWorldLocked && !m_WatchGlanced)
            {
                m_WatchGripHeldSince = -1f;
                return;
            }

            if (m_WatchGripHeldSince < 0f)
            {
                m_WatchGripHeldSince = now;
                return;
            }
            if (now - m_WatchGripHeldSince < QuickMenuWatchGripHoldSec) return;

            m_WatchGripHeldSince = -1f;
            m_WatchGripReadyAt = now + QuickMenuWatchGripCooldownSec;
            // Keep it on screen through the swap so the state change is visible.
            m_WatchGlanced = true;
            QuickMenuSetWatchWorldLocked(!m_WatchWorldLocked);
        }

        private void QuickMenuHideWatchImmediate()
        {
            m_WatchVisibleNow = false;
            m_WatchFade = 0f;
            m_WatchPressIdx = -1;
            m_WatchHoldFired = false;
            m_WatchFrozen = false;
            // A watch that is gone cannot be dragged; a stuck calibrate flag would force it
            // permanently visible once it comes back.
            m_WatchCalibrating = false;
            m_WatchGlanceCandidateSince = -1f;
            m_WatchHoverStatus = null;
            if (m_WatchTipGo != null && m_WatchTipGo.activeSelf) m_WatchTipGo.SetActive(false);
            if (m_QmPreviewOnWatch) QuickMenuEndRandomPreview();
            if (m_WatchCanvasGroup != null)
            {
                m_WatchCanvasGroup.alpha = 0f;
                m_WatchCanvasGroup.blocksRaycasts = false;
            }
            QuickMenuSetWatchActive(false);
        }

        private void QuickMenuSetWatchActive(bool active)
        {
            if (m_WatchActive == active && (m_WatchCanvas == null || m_WatchCanvas.gameObject.activeSelf == active))
            {
                m_WatchActive = active;
                return;
            }
            m_WatchActive = active;
            if (m_WatchCanvas != null && m_WatchCanvas.gameObject.activeSelf != active)
                m_WatchCanvas.gameObject.SetActive(active);
            if (!active)
            {
                // Deactivating swallows the pending OnPointerUp/Exit. A surviving press would
                // finish its hold timer on the next show and fire a destructive action unasked.
                m_WatchPressIdx = -1;
                m_WatchHoldFired = false;
                m_WatchHoverStatus = null;
                if (m_QmPreviewOnWatch) QuickMenuEndRandomPreview();
                return;
            }

            if (m_WatchCanvas != null && m_WatchCanvas.worldCamera == null)
            {
                Camera eye = QuickMenuGetPlayerCameraComponent();
                if (eye != null) m_WatchCanvas.worldCamera = eye;
            }
            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++)
                QuickMenuSyncWatchSlot(i);
            QuickMenuSyncWatchAssignAll();
            QuickMenuRefreshWatchChromeColors();
        }

        private void QuickMenuTickWatchFade(bool wantShow)
        {
            float target = wantShow ? 1f : 0f;
            if (Mathf.Abs(m_WatchFade - target) > 1e-4f)
            {
                float step = Time.unscaledDeltaTime / QuickMenuWatchFadeSec;
                m_WatchFade = Mathf.MoveTowards(m_WatchFade, target, step);
            }
            else m_WatchFade = target;

            if (m_WatchCanvasGroup != null)
            {
                m_WatchCanvasGroup.alpha = m_WatchFade;
                // A fading-out watch is still a live raycast target; don't let a barely-visible
                // face eat the pointer (or take a click) on the way out.
                bool hit = wantShow && m_WatchFade >= QuickMenuWatchRaycastFade;
                if (m_WatchCanvasGroup.blocksRaycasts != hit) m_WatchCanvasGroup.blocksRaycasts = hit;
            }
            if (m_WatchFade <= 0f) QuickMenuSetWatchActive(false);
        }

        private void QuickMenuSetWatchCueShown(bool show)
        {
            if (m_WatchCueGo == null) return;
            if (m_WatchCueGo.activeSelf != show) m_WatchCueGo.SetActive(show);
        }

        private void QuickMenuEnsureWatchCanvas()
        {
            if (m_WatchCanvas != null) return;
            // A build that throws leaves nothing behind; without a backoff we would allocate and
            // destroy the whole widget tree every frame. Retry a few times, then stay down.
            if (m_WatchCanvasFailCount >= QuickMenuWatchCanvasMaxFails) return;
            if (m_WatchCanvasFailCount > 0 && Time.unscaledTime < m_WatchCanvasRetryAt) return;
            var sc = SuperController.singleton;
            if (sc == null || sc.mainHUD == null) return;

            try
            {
                QuickMenuEnsureWatchCanvasBody(sc);
                m_WatchCanvasFailCount = 0;
            }
            catch (System.Exception ex)
            {
                m_WatchCanvasFailCount++;
                m_WatchCanvasRetryAt = Time.unscaledTime + QuickMenuWatchCanvasRetrySec;
                QuickMenuLogWatchUpdateError(ex);
                try
                {
                    if (m_WatchCanvas != null)
                    {
                        if (m_WatchAddedToSc && sc != null) sc.RemoveCanvas(m_WatchCanvas);
                        UnityEngine.Object.Destroy(m_WatchCanvas.gameObject);
                    }
                }
                catch { }
                m_WatchCanvas = null;
                m_WatchCanvasRT = null;
                m_WatchCanvasGroup = null;
                m_WatchTf = null;
                m_WatchAddedToSc = false;
            }
        }

        private void QuickMenuEnsureWatchCanvasBody(SuperController sc)
        {
            QuickMenuEnsureWatchAssignments();

            GameObject root = new GameObject("VPB_VrWatch_Canvas");
            if (sc.mainHUD.gameObject != null) root.layer = sc.mainHUD.gameObject.layer;

            m_WatchCanvas = root.AddComponent<Canvas>();
            m_WatchCanvas.renderMode = RenderMode.WorldSpace;
            m_WatchCanvas.pixelPerfect = false;
            Camera eye = QuickMenuGetPlayerCameraComponent();
            if (eye != null) m_WatchCanvas.worldCamera = eye;
            m_WatchTf = root.transform;

            CanvasScaler cs = root.AddComponent<CanvasScaler>();
            if (cs != null)
            {
                cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                cs.dynamicPixelsPerUnit = QuickMenuWatchFontDppu;
            }
            root.AddComponent<GraphicRaycaster>();
            m_WatchCanvasGroup = root.AddComponent<CanvasGroup>();
            m_WatchCanvasGroup.alpha = 0f;
            m_WatchCanvasGroup.blocksRaycasts = false;

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
            m_WatchCanvasRT.sizeDelta = new Vector2(160f, 178f);

            GameObject bezelGo = new GameObject("Bezel");
            bezelGo.transform.SetParent(root.transform, false);
            m_WatchBezel = AddQuickMenuRoundedBg(bezelGo, QuickMenuWatchBezelColor, true);
            QuickMenuApplyWatchBezelRadius(UI.ResolveGalleryElementCornerRadiusFraction());
            m_WatchBezelRt = bezelGo.GetComponent<RectTransform>();
            m_WatchBezelRt.anchorMin = Vector2.zero;
            m_WatchBezelRt.anchorMax = Vector2.one;
            m_WatchBezelRt.sizeDelta = Vector2.zero;
            m_WatchBezelRt.anchoredPosition = Vector2.zero;
            var drag = bezelGo.AddComponent<VrWatchBezelDrag>();
            drag.owner = this;

            m_WatchStatusText = UI.CreateLabel(root, "", QuickMenuWatchStatusFont, GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.middleCenter, new Vector2(140f, QuickMenuWatchStatusH),
                Vector2.zero, "Status");
            if (m_WatchStatusText != null)
            {
                m_WatchStatusText.resizeTextForBestFit = false;
                m_WatchStatusRt = m_WatchStatusText.rectTransform;
            }

            m_WatchSlotGos = new GameObject[QuickMenuWatchHudSlotCount];
            m_WatchSlotRts = new RectTransform[QuickMenuWatchHudSlotCount];
            m_WatchSlotIcons = new Image[QuickMenuWatchHudSlotCount];
            m_WatchSlotIconRts = new RectTransform[QuickMenuWatchHudSlotCount];
            m_WatchSlotBackdrops = new Image[QuickMenuWatchHudSlotCount];
            m_WatchSlotLabels = new Text[QuickMenuWatchHudSlotCount];
            m_WatchAssignGos = new GameObject[QuickMenuWatchAssignSlotCount];
            m_WatchAssignRts = new RectTransform[QuickMenuWatchAssignSlotCount];
            m_WatchAssignIcons = new Image[QuickMenuWatchAssignSlotCount];
            m_WatchAssignIconRts = new RectTransform[QuickMenuWatchAssignSlotCount];
            m_WatchAssignBackdrops = new Image[QuickMenuWatchAssignSlotCount];
            m_WatchAssignLabels = new Text[QuickMenuWatchAssignSlotCount];
            m_WatchChromeGos = new GameObject[QuickMenuWatchChromeCount];
            m_WatchChromeRts = new RectTransform[QuickMenuWatchChromeCount];
            m_WatchChromeBgs = new Image[QuickMenuWatchChromeCount];
            m_WatchChromeHovers = new QuickMenuSquareHover[QuickMenuWatchChromeCount];
            m_WatchChromeTips = new VrWatchHoverTip[QuickMenuWatchChromeCount];
            m_WatchChromeIcons = new Image[QuickMenuWatchChromeCount];

            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++) QuickMenuBuildWatchHudSlot(root, i);
            for (int i = 0; i < QuickMenuWatchAssignSlotCount; i++) QuickMenuBuildWatchAssignSlot(root, i);

            m_WatchIconExpand = UI.LoadIconSprite("chevron-down", Color.white);
            m_WatchIconCompact = UI.LoadIconSprite("chevron-up", Color.white);
            m_WatchIconPinOn = UI.LoadIconSprite("pin", Color.white);
            m_WatchIconPinOff = UI.LoadIconSprite("pinned-off", Color.white);

            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromeCollapse, "window-minimize",
                () => { try { QuickMenuSetWatchCollapsed(true); } catch { } },
                VPBTranslation.T("hook.watch.tip.collapse", "Minimise to dot"));
            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromeHand, "switch-horizontal",
                () => { try { QuickMenuSwitchWatchHand(); } catch { } },
                VPBTranslation.T("hook.watch.tip.hand", "Switch watch hand"));
            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromePin, "pinned-off",
                () => { try { QuickMenuToggleWatchWorldLock(); } catch { } },
                QuickMenuWatchPinTipText());
            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromeEdit, "settings-plus",
                () => { try { QuickMenuToggleWatchEdit(); } catch { } },
                QuickMenuWatchEditTipText());
            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromeExpand, "chevron-down",
                () => { try { QuickMenuToggleWatchExpanded(); } catch { } },
                QuickMenuWatchExpandTipText());
            QuickMenuBuildWatchChromeButton(root, QuickMenuWatchChromeHelp, "help-square-rounded",
                () => { try { QuickMenuReplayWatchCue(); } catch { } },
                VPBTranslation.T("hook.watch.tip.help", "Replay watch tips"));

            m_WatchPagerPrevGo = QuickMenuBuildWatchPagerButton(root, "WatchPagerPrev", "chevron-left",
                () => { try { QuickMenuWatchChangePage(-1); } catch { } },
                VPBTranslation.T("hook.watch.tip.page_prev", "Previous watch page"), out m_WatchPagerPrevRt);
            m_WatchPagerNextGo = QuickMenuBuildWatchPagerButton(root, "WatchPagerNext", "chevron-right",
                () => { try { QuickMenuWatchChangePage(+1); } catch { } },
                VPBTranslation.T("hook.watch.tip.page_next", "Next watch page"), out m_WatchPagerNextRt);

            QuickMenuBuildWatchDot(root);
            QuickMenuBuildWatchAssignSheet(root);
            QuickMenuBuildWatchTip(root);
            QuickMenuBuildWatchRandomPreview(root);
            QuickMenuBuildWatchCue(root);

            root.SetActive(false);
            m_WatchStatusNeedRebuild = true;
            m_WatchLayoutApplied = (QuickMenuWatchFaceMode)(-1);
            QuickMenuApplyWatchLayout();
            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++) QuickMenuSyncWatchSlot(i);
            QuickMenuSyncWatchAssignAll();
            QuickMenuRefreshWatchChromeColors();
        }

        private void QuickMenuBuildWatchTip(GameObject root)
        {
            m_WatchTipGo = new GameObject("Tip");
            m_WatchTipGo.transform.SetParent(root.transform, false);
            m_WatchTipRt = m_WatchTipGo.AddComponent<RectTransform>();
            m_WatchTipRt.anchorMin = new Vector2(0.5f, 0.5f);
            m_WatchTipRt.anchorMax = new Vector2(0.5f, 0.5f);
            m_WatchTipRt.pivot = new Vector2(0.5f, 1f);
            m_WatchTipRt.sizeDelta = new Vector2(QuickMenuWatchTipW, QuickMenuWatchTipH);
            AddQuickMenuRoundedBg(m_WatchTipGo, QuickMenuWatchCueBackdrop, false);
            m_WatchTipText = UI.CreateLabel(m_WatchTipGo, "", QuickMenuWatchTipFont,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "TipText");
            if (m_WatchTipText != null)
            {
                m_WatchTipText.resizeTextForBestFit = false;
                RectTransform trt = m_WatchTipText.rectTransform;
                trt.offsetMin = new Vector2(8f, 4f);
                trt.offsetMax = new Vector2(-8f, -4f);
            }
            m_WatchTipGo.SetActive(false);
        }

        private void QuickMenuBuildWatchCue(GameObject root)
        {
            m_WatchCueGo = new GameObject("Cue");
            m_WatchCueGo.transform.SetParent(root.transform, false);
            m_WatchCueRt = m_WatchCueGo.AddComponent<RectTransform>();
            m_WatchCueRt.anchorMin = new Vector2(0.5f, 0.5f);
            m_WatchCueRt.anchorMax = new Vector2(0.5f, 0.5f);
            m_WatchCueRt.pivot = new Vector2(0.5f, 0.5f);
            m_WatchCueRt.sizeDelta = new Vector2(220f, 56f);
            AddQuickMenuRoundedBg(m_WatchCueGo, QuickMenuWatchCueBackdrop, false);
            m_WatchCueText = UI.CreateLabel(m_WatchCueGo, QuickMenuWatchCueTextValue(),
                QuickMenuWatchCueFont, GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "CueText");
            if (m_WatchCueText != null)
            {
                m_WatchCueText.resizeTextForBestFit = false;
                RectTransform crt = m_WatchCueText.rectTransform;
                crt.offsetMin = new Vector2(8f, 4f);
                crt.offsetMax = new Vector2(-8f, -4f);
            }
            m_WatchCueGo.SetActive(false);
        }

        private void QuickMenuBuildWatchDot(GameObject root)
        {
            m_WatchDotGo = new GameObject("WatchDot");
            m_WatchDotGo.transform.SetParent(root.transform, false);
            m_WatchDotRt = m_WatchDotGo.AddComponent<RectTransform>();
            m_WatchDotRt.anchorMin = new Vector2(0.5f, 0.5f);
            m_WatchDotRt.anchorMax = new Vector2(0.5f, 0.5f);
            m_WatchDotRt.pivot = new Vector2(0.5f, 0.5f);
            m_WatchDotRt.sizeDelta = new Vector2(QuickMenuWatchDotSize, QuickMenuWatchDotSize);
            m_WatchDotRt.anchoredPosition = Vector2.zero;
            Image img = AddQuickMenuRoundedBg(m_WatchDotGo, QuickMenuWatchChromeIdle);
            Button btn = m_WatchDotGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { try { QuickMenuSetWatchCollapsed(false); } catch { } });
            QuickMenuAttachWatchHover(m_WatchDotGo, img, QuickMenuWatchChromeIdle, QuickMenuWatchChromeIdleHover,
                VPBTranslation.T("hook.watch.tip.restore", "Restore watch face"));
            QuickMenuAddWatchIcon(m_WatchDotGo, UI.LoadIconSprite("device-watch", Color.white), 6f);
            m_WatchDotGo.SetActive(false);
        }

        private static RectTransform QuickMenuAddWatchIcon(GameObject parent, Sprite spr, float inset)
        {
            if (spr == null) return null;
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(parent.transform, false);
            Image icon = iconGO.AddComponent<Image>();
            UI.SetIconSprite(icon, spr);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RectTransform irt = iconGO.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.sizeDelta = new Vector2(-inset * 2f, -inset * 2f);
            irt.anchoredPosition = Vector2.zero;
            return irt;
        }

        private void QuickMenuBuildWatchHudSlot(GameObject root, int hudIdx)
        {
            GameObject go = new GameObject("WatchSlot_" + hudIdx);
            go.transform.SetParent(root.transform, false);
            m_WatchSlotGos[hudIdx] = go;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchBtn, QuickMenuWatchBtn);
            m_WatchSlotRts[hudIdx] = rt;

            Color nb = QmBackdropAssignedOpaque;
            Image img = AddQuickMenuRoundedBg(go, nb);
            m_WatchSlotBackdrops[hudIdx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            QuickMenuAttachWatchHover(go, img, nb, QmBackdropAssignedHoverOpaque, null);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            Image slotIcon = iconGO.AddComponent<Image>();
            slotIcon.color = Color.white;
            slotIcon.preserveAspect = true;
            slotIcon.raycastTarget = false;
            slotIcon.enabled = false;
            m_WatchSlotIcons[hudIdx] = slotIcon;
            m_WatchSlotIconRts[hudIdx] = iconGO.GetComponent<RectTransform>();

            m_WatchSlotLabels[hudIdx] = QuickMenuBuildWatchSlotLabel(go, "HudLabel");

            int idxCopy = hudIdx;
            var tip = go.AddComponent<VrWatchSlotHover>();
            tip.owner = this;
            tip.hudSlotIdx = idxCopy;
            btn.onClick.AddListener(() => { try { QuickMenuOnWatchSlotClick(idxCopy); } catch { } });
        }

        private void QuickMenuBuildWatchAssignSlot(GameObject root, int slotIdx)
        {
            GameObject go = new GameObject("WatchAssign_" + slotIdx);
            go.transform.SetParent(root.transform, false);
            m_WatchAssignGos[slotIdx] = go;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchBtn, QuickMenuWatchBtn);
            m_WatchAssignRts[slotIdx] = rt;

            Color nb = QmBackdropEmptyOpaque;
            Image img = AddQuickMenuRoundedBg(go, nb);
            m_WatchAssignBackdrops[slotIdx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            QuickMenuAttachWatchHover(go, img, nb, QmBackdropEmptyHoverOpaque, null);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            Image slotIcon = iconGO.AddComponent<Image>();
            slotIcon.color = Color.white;
            slotIcon.preserveAspect = true;
            slotIcon.raycastTarget = false;
            slotIcon.enabled = false;
            m_WatchAssignIcons[slotIdx] = slotIcon;
            m_WatchAssignIconRts[slotIdx] = iconGO.GetComponent<RectTransform>();

            m_WatchAssignLabels[slotIdx] = QuickMenuBuildWatchSlotLabel(go, "AssignLabel");

            int idxCopy = slotIdx;
            var tip = go.AddComponent<VrWatchAssignHover>();
            tip.owner = this;
            tip.slotIdx = idxCopy;
            btn.onClick.AddListener(() => { try { QuickMenuOnWatchAssignClick(idxCopy); } catch { } });
        }

        private static Text QuickMenuBuildWatchSlotLabel(GameObject parent, string name)
        {
            Text t = UI.CreateLabel(parent, "", QuickMenuWatchLabelFont, QuickMenuWatchLabelColor,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.middleCenter,
                new Vector2(QuickMenuWatchBtn + 8f, QuickMenuWatchLabelH), Vector2.zero, name);
            if (t != null)
            {
                t.resizeTextForBestFit = false;
                t.gameObject.SetActive(false);
            }
            return t;
        }

        private void QuickMenuBuildWatchChromeButton(GameObject parent, int idx, string iconPath,
            UnityEngine.Events.UnityAction onClick, string tip)
        {
            GameObject go = new GameObject("WatchChrome_" + idx);
            go.transform.SetParent(parent.transform, false);
            m_WatchChromeGos[idx] = go;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchChrome, QuickMenuWatchChrome);
            m_WatchChromeRts[idx] = rt;

            Image img = AddQuickMenuRoundedBg(go, QuickMenuWatchChromeIdle);
            m_WatchChromeBgs[idx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            QuickMenuAttachWatchHover(go, img, QuickMenuWatchChromeIdle, QuickMenuWatchChromeIdleHover, tip);
            m_WatchChromeHovers[idx] = go.GetComponent<QuickMenuSquareHover>();
            m_WatchChromeTips[idx] = go.GetComponent<VrWatchHoverTip>();

            RectTransform irt = QuickMenuAddWatchIcon(go, UI.LoadIconSprite(iconPath, Color.white), 4f);
            if (irt != null) m_WatchChromeIcons[idx] = irt.GetComponent<Image>();
        }

        private GameObject QuickMenuBuildWatchPagerButton(GameObject parent, string name, string iconPath,
            UnityEngine.Events.UnityAction onClick, string tip, out RectTransform rt)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchPagerBtn, QuickMenuWatchPagerBtn);

            Image img = AddQuickMenuRoundedBg(go, QuickMenuWatchChromeIdle);
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            QuickMenuAttachWatchHover(go, img, QuickMenuWatchChromeIdle, QuickMenuWatchChromeIdleHover, tip);
            QuickMenuAddWatchIcon(go, UI.LoadIconSprite(iconPath, Color.white), 3f);
            go.SetActive(false);
            return go;
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

        private void QuickMenuApplyWatchLayout()
        {
            if (m_WatchCanvasRT == null) return;
            if (m_WatchLayoutApplied == m_WatchFaceMode &&
                m_WatchLayoutLabels == m_WatchCfgLabels &&
                m_WatchLayoutEdit == m_WatchEditMode)
                return;

            m_WatchLayoutApplied = m_WatchFaceMode;
            m_WatchLayoutLabels = m_WatchCfgLabels;
            m_WatchLayoutEdit = m_WatchEditMode;

            switch (m_WatchFaceMode)
            {
                case QuickMenuWatchFaceMode.Collapsed:
                    QuickMenuLayoutWatchCollapsed();
                    break;
                case QuickMenuWatchFaceMode.Expanded:
                    QuickMenuLayoutWatchExpanded();
                    break;
                default:
                    QuickMenuLayoutWatchCompact();
                    break;
            }

            QuickMenuSyncWatchExpandIcon();
            QuickMenuPlaceWatchAssignSheet();
            QuickMenuPlaceWatchCue();
            m_WatchStatusNeedRebuild = true;
            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++) QuickMenuSyncWatchSlot(i);
            QuickMenuSyncWatchAssignAll();
            QuickMenuRefreshWatchChromeColors();
        }

        private void QuickMenuLayoutWatchCollapsed()
        {
            float size = QuickMenuWatchDotSize + QuickMenuWatchDotPad * 2f;
            m_WatchCanvasRT.sizeDelta = new Vector2(size, size);
            QuickMenuSetWatchWidgetsActive(false, false, false, false);
            if (m_WatchStatusText != null && m_WatchStatusText.gameObject.activeSelf)
                m_WatchStatusText.gameObject.SetActive(false);
            if (m_WatchDotGo != null && !m_WatchDotGo.activeSelf) m_WatchDotGo.SetActive(true);
            if (m_WatchDotRt != null) m_WatchDotRt.anchoredPosition = Vector2.zero;
        }

        private void QuickMenuLayoutWatchCompact()
        {
            float cellH = m_WatchCfgLabels ? QuickMenuWatchBtn + QuickMenuWatchLabelH : QuickMenuWatchBtn;
            float gridW = QuickMenuWatchCompactCols * QuickMenuWatchBtn + (QuickMenuWatchCompactCols - 1f) * QuickMenuWatchGap;
            float chromeW = 5f * QuickMenuWatchChromeSm + 4f * QuickMenuWatchChromeGapSm;
            float innerW = gridW > chromeW ? gridW : chromeW;
            float stackH = cellH * 2f + QuickMenuWatchGap;
            float bezelW = innerW + QuickMenuWatchPad * 2f;
            float bezelH = QuickMenuWatchPad + QuickMenuWatchStatusH + QuickMenuWatchStatusGap + stackH
                + QuickMenuWatchChromeGap + QuickMenuWatchChromeSm + QuickMenuWatchPad;

            m_WatchCanvasRT.sizeDelta = new Vector2(bezelW, bezelH);
            if (m_WatchDotGo != null && m_WatchDotGo.activeSelf) m_WatchDotGo.SetActive(false);

            float yTop = bezelH * 0.5f - QuickMenuWatchPad;
            QuickMenuLayoutWatchStatus(bezelW, yTop - QuickMenuWatchStatusH * 0.5f, false);

            float stackTop = yTop - QuickMenuWatchStatusH - QuickMenuWatchStatusGap;
            float colStep = QuickMenuWatchBtn + QuickMenuWatchGap;
            float rowStep = cellH + QuickMenuWatchGap;
            for (int i = 0; i < QuickMenuWatchAssignSlotCount; i++)
            {
                bool on = i < QuickMenuWatchCompactSlotCount;
                QuickMenuSetGoActive(m_WatchAssignGos[i], on);
                if (!on) continue;
                int col = i % 3;
                int row = i / 3;
                float x = (col - 1) * colStep;
                float y = stackTop - cellH * 0.5f - row * rowStep;
                QuickMenuPlaceWatchSlot(m_WatchAssignRts[i], m_WatchAssignIconRts[i], m_WatchAssignLabels[i], x, y, cellH);
            }

            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++) QuickMenuSetGoActive(m_WatchSlotGos[i], false);
            QuickMenuSetGoActive(m_WatchPagerPrevGo, false);
            QuickMenuSetGoActive(m_WatchPagerNextGo, false);

            float chromeY = stackTop - stackH - QuickMenuWatchChromeGap - QuickMenuWatchChromeSm * 0.5f;
            float chromeStep = QuickMenuWatchChromeSm + QuickMenuWatchChromeGapSm;
            float chromeX = -(chromeW * 0.5f) + QuickMenuWatchChromeSm * 0.5f;
            QuickMenuPlaceChrome(QuickMenuWatchChromeCollapse, chromeX, chromeY, QuickMenuWatchChromeSm, true);
            QuickMenuPlaceChrome(QuickMenuWatchChromePin, chromeX + chromeStep, chromeY, QuickMenuWatchChromeSm, true);
            QuickMenuPlaceChrome(QuickMenuWatchChromeExpand, chromeX + chromeStep * 2f, chromeY, QuickMenuWatchChromeSm, true);
            QuickMenuPlaceChrome(QuickMenuWatchChromeEdit, chromeX + chromeStep * 3f, chromeY, QuickMenuWatchChromeSm, true);
            QuickMenuPlaceChrome(QuickMenuWatchChromeHand, chromeX + chromeStep * 4f, chromeY, QuickMenuWatchChromeSm, true);
            QuickMenuPlaceChrome(QuickMenuWatchChromeHelp, 0f, 0f, QuickMenuWatchChromeSm, false);
        }

        private void QuickMenuLayoutWatchExpanded()
        {
            float cellH = m_WatchCfgLabels ? QuickMenuWatchBtn + QuickMenuWatchLabelH : QuickMenuWatchBtn;
            float gridW = QuickMenuWatchBtn * 2f + QuickMenuWatchGap;
            float chromeW = 3f * QuickMenuWatchChrome + 2f * QuickMenuWatchChromeGap;
            float innerW = gridW > chromeW ? gridW : chromeW;
            float stackH = cellH * 4f + QuickMenuWatchGap * 3f;
            float bezelW = QuickMenuWatchPad + QuickMenuWatchBtn + QuickMenuWatchRailGap + innerW
                + QuickMenuWatchRailGap + QuickMenuWatchBtn + QuickMenuWatchPad;
            float bezelH = QuickMenuWatchPad + QuickMenuWatchStatusH + QuickMenuWatchStatusGap + stackH + QuickMenuWatchPad;

            m_WatchCanvasRT.sizeDelta = new Vector2(bezelW, bezelH);
            if (m_WatchDotGo != null && m_WatchDotGo.activeSelf) m_WatchDotGo.SetActive(false);

            float yTop = bezelH * 0.5f - QuickMenuWatchPad;
            QuickMenuLayoutWatchStatus(bezelW, yTop - QuickMenuWatchStatusH * 0.5f, true);

            float stackTop = yTop - QuickMenuWatchStatusH - QuickMenuWatchStatusGap;
            float rowStep = cellH + QuickMenuWatchGap;
            float leftX = -(innerW * 0.5f + QuickMenuWatchRailGap + QuickMenuWatchBtn * 0.5f);
            float rightX = -leftX;
            float centerStep = QuickMenuWatchBtn + QuickMenuWatchGap;
            float chromeStep = QuickMenuWatchChrome + QuickMenuWatchChromeGap;
            float chromeX0 = -(chromeW * 0.5f) + QuickMenuWatchChrome * 0.5f;

            for (int row = 0; row < 4; row++)
            {
                float yRow = stackTop - cellH * 0.5f - row * rowStep;
                QuickMenuPlaceWatchSlot(m_WatchAssignRts[row], m_WatchAssignIconRts[row], m_WatchAssignLabels[row], leftX, yRow, cellH);
                QuickMenuSetGoActive(m_WatchAssignGos[row], true);
                QuickMenuPlaceWatchSlot(m_WatchAssignRts[row + 4], m_WatchAssignIconRts[row + 4], m_WatchAssignLabels[row + 4], rightX, yRow, cellH);
                QuickMenuSetGoActive(m_WatchAssignGos[row + 4], true);

                if (row == 0 || row == 1)
                {
                    int hud0 = row * 2;
                    QuickMenuPlaceWatchSlot(m_WatchSlotRts[hud0], m_WatchSlotIconRts[hud0], m_WatchSlotLabels[hud0], -centerStep * 0.5f, yRow, cellH);
                    QuickMenuPlaceWatchSlot(m_WatchSlotRts[hud0 + 1], m_WatchSlotIconRts[hud0 + 1], m_WatchSlotLabels[hud0 + 1], centerStep * 0.5f, yRow, cellH);
                }
                else if (row == 2)
                {
                    QuickMenuPlaceChrome(QuickMenuWatchChromeCollapse, chromeX0, yRow, QuickMenuWatchChrome, true);
                    QuickMenuPlaceChrome(QuickMenuWatchChromeHand, chromeX0 + chromeStep, yRow, QuickMenuWatchChrome, true);
                    QuickMenuPlaceChrome(QuickMenuWatchChromePin, chromeX0 + chromeStep * 2f, yRow, QuickMenuWatchChrome, true);
                }
                else
                {
                    QuickMenuPlaceChrome(QuickMenuWatchChromeEdit, chromeX0, yRow, QuickMenuWatchChrome, true);
                    QuickMenuPlaceChrome(QuickMenuWatchChromeExpand, chromeX0 + chromeStep, yRow, QuickMenuWatchChrome, true);
                    QuickMenuPlaceChrome(QuickMenuWatchChromeHelp, chromeX0 + chromeStep * 2f, yRow, QuickMenuWatchChrome, true);
                }
            }
        }

        private void QuickMenuLayoutWatchStatus(float bezelW, float y, bool pager)
        {
            if (m_WatchStatusText != null && !m_WatchStatusText.gameObject.activeSelf)
                m_WatchStatusText.gameObject.SetActive(true);

            float rowW = bezelW - QuickMenuWatchPad * 2f;
            float textW = pager ? rowW - 2f * (QuickMenuWatchPagerBtn + QuickMenuWatchPagerGap) : rowW;
            if (m_WatchStatusRt != null)
            {
                m_WatchStatusRt.sizeDelta = new Vector2(textW, QuickMenuWatchStatusH);
                m_WatchStatusRt.anchoredPosition = new Vector2(0f, y);
            }

            QuickMenuSetGoActive(m_WatchPagerPrevGo, pager);
            QuickMenuSetGoActive(m_WatchPagerNextGo, pager);
            if (!pager) return;

            float x = rowW * 0.5f - QuickMenuWatchPagerBtn * 0.5f;
            if (m_WatchPagerPrevRt != null) m_WatchPagerPrevRt.anchoredPosition = new Vector2(-x, y);
            if (m_WatchPagerNextRt != null) m_WatchPagerNextRt.anchoredPosition = new Vector2(x, y);
        }

        private void QuickMenuPlaceWatchSlot(RectTransform rt, RectTransform iconRt, Text label, float x, float y, float cellH)
        {
            if (rt == null) return;
            rt.sizeDelta = new Vector2(QuickMenuWatchBtn, cellH);
            rt.anchoredPosition = new Vector2(x, y);

            bool labels = m_WatchCfgLabels;
            if (iconRt != null)
            {
                iconRt.anchorMin = new Vector2(0.5f, 1f);
                iconRt.anchorMax = new Vector2(0.5f, 1f);
                iconRt.pivot = new Vector2(0.5f, 1f);
                iconRt.sizeDelta = new Vector2(QuickMenuWatchBtn - 8f, QuickMenuWatchBtn - 8f);
                iconRt.anchoredPosition = new Vector2(0f, -4f);
            }
            if (label != null)
            {
                if (label.gameObject.activeSelf != labels) label.gameObject.SetActive(labels);
                if (labels)
                {
                    RectTransform lrt = label.rectTransform;
                    lrt.anchorMin = new Vector2(0.5f, 0f);
                    lrt.anchorMax = new Vector2(0.5f, 0f);
                    lrt.pivot = new Vector2(0.5f, 0f);
                    lrt.sizeDelta = new Vector2(QuickMenuWatchBtn + 10f, QuickMenuWatchLabelH);
                    lrt.anchoredPosition = Vector2.zero;
                }
            }
        }

        private void QuickMenuPlaceChrome(int idx, float x, float y, float size, bool active)
        {
            if (m_WatchChromeGos == null || idx < 0 || idx >= m_WatchChromeGos.Length) return;
            QuickMenuSetGoActive(m_WatchChromeGos[idx], active);
            if (!active) return;
            RectTransform rt = m_WatchChromeRts[idx];
            if (rt == null) return;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private void QuickMenuSetWatchWidgetsActive(bool hud, bool assign, bool chrome, bool pager)
        {
            if (m_WatchSlotGos != null)
                for (int i = 0; i < m_WatchSlotGos.Length; i++) QuickMenuSetGoActive(m_WatchSlotGos[i], hud);
            if (m_WatchAssignGos != null)
                for (int i = 0; i < m_WatchAssignGos.Length; i++) QuickMenuSetGoActive(m_WatchAssignGos[i], assign);
            if (m_WatchChromeGos != null)
                for (int i = 0; i < m_WatchChromeGos.Length; i++) QuickMenuSetGoActive(m_WatchChromeGos[i], chrome);
            QuickMenuSetGoActive(m_WatchPagerPrevGo, pager);
            QuickMenuSetGoActive(m_WatchPagerNextGo, pager);
        }

        private static void QuickMenuSetGoActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private void QuickMenuPlaceWatchCue()
        {
            if (m_WatchCanvasRT == null) return;
            float bezelH = m_WatchCanvasRT.sizeDelta.y;
            if (m_WatchCueRt != null)
            {
                m_WatchCueRt.sizeDelta = new Vector2(QuickMenuWatchTipW, 56f);
                m_WatchCueRt.anchoredPosition = new Vector2(0f, bezelH * 0.5f + QuickMenuWatchTipGap + 28f);
            }
            float tipTop = -(bezelH * 0.5f + QuickMenuWatchTipGap);
            if (m_WatchTipRt != null)
                m_WatchTipRt.anchoredPosition = new Vector2(0f, tipTop);
            if (m_QmPreviewWatchRt != null)
                m_QmPreviewWatchRt.anchoredPosition = new Vector2(0f, tipTop - QuickMenuWatchTipH - QuickMenuWatchTipGap);
        }

        private void QuickMenuSyncWatchExpandIcon()
        {
            if (m_WatchChromeIcons == null) return;
            Image icon = m_WatchChromeIcons[QuickMenuWatchChromeExpand];
            if (icon == null) return;
            Sprite want = m_WatchFaceMode == QuickMenuWatchFaceMode.Expanded ? m_WatchIconCompact : m_WatchIconExpand;
            if (want != null && icon.sprite != want) UI.SetIconSprite(icon, want);
        }

        /// <summary>Action a watch HUD slot would run right now (None while an edit mode owns the click).</summary>
        private QuickMenuAssignableAction QuickMenuWatchHudSlotAction(int hudIdx)
        {
            if (m_QuickMenuEditMode || m_WatchEditMode) return QuickMenuAssignableAction.None;
            return QuickMenuGetSlotAction(hudIdx);
        }

        private QuickMenuAssignableAction QuickMenuWatchAssignSlotAction(int slotIdx)
        {
            if (m_QuickMenuEditMode || m_WatchEditMode) return QuickMenuAssignableAction.None;
            return QuickMenuGetWatchSlotAction(slotIdx);
        }

        internal string QuickMenuWatchHoverLabel(int hudIdx)
        {
            if (m_QuickMenuEditMode)
                return VPBTranslation.T("hook.watch.status.hud_edit", "HUD edit mode — core row locked");
            return QuickMenuGetActionTooltip(QuickMenuGetSlotAction(hudIdx), -1);
        }

        private void QuickMenuOnWatchSlotClick(int hudIdx)
        {
            if (m_QuickMenuEditMode) return;
            var act = QuickMenuGetSlotAction(hudIdx);
            if (!QuickMenuWatchConsumeClick(act)) return;
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
            if (act == QuickMenuAssignableAction.None)
            {
                QuickMenuSetWatchHoverStatus(VPBTranslation.T("hook.watch.status.empty_slot", "Empty — use Assign"));
                return;
            }
            if (!QuickMenuWatchConsumeClick(act)) return;
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
            var act = QuickMenuGetWatchSlotAction(slotIdx);
            if (act == QuickMenuAssignableAction.None)
                return VPBTranslation.T("hook.watch.status.empty_slot", "Empty — use Assign");
            if (QuickMenuWatchIsDestructive(act) && m_WatchCfgHoldConfirm)
                return QuickMenuGetActionTooltip(act, -1) + " · " +
                       VPBTranslation.T("hook.watch.status.hold", "hand-finger");
            return QuickMenuGetActionTooltip(act, -1);
        }

        private static bool QuickMenuWatchIsDestructive(QuickMenuAssignableAction a)
        {
            return a == QuickMenuAssignableAction.RemoveAllClothing ||
                   a == QuickMenuAssignableAction.RemoveAllHair ||
                   a == QuickMenuAssignableAction.CloseAll;
        }

        private bool QuickMenuWatchConsumeClick(QuickMenuAssignableAction act)
        {
            if (!m_WatchCfgHoldConfirm || !QuickMenuWatchIsDestructive(act)) return true;
            bool fired = m_WatchHoldFired;
            m_WatchHoldFired = false;
            if (!fired)
                QuickMenuSetWatchHoverStatus(VPBTranslation.T("hook.watch.status.hold_confirm", "Hold to confirm"));
            return false;
        }

        internal void QuickMenuWatchBeginPress(int kind, int idx)
        {
            m_WatchHoldFired = false;
            m_WatchPressKind = kind;
            m_WatchPressIdx = -1;
            if (!m_WatchCfgHoldConfirm || m_WatchEditMode) return;

            QuickMenuAssignableAction act = kind == 0
                ? (m_QuickMenuEditMode ? QuickMenuAssignableAction.None : QuickMenuGetSlotAction(idx))
                : QuickMenuGetWatchSlotAction(idx);
            if (!QuickMenuWatchIsDestructive(act)) return;

            m_WatchPressIdx = idx;
            m_WatchPressAction = act;
            m_WatchPressStart = Time.unscaledTime;
            QuickMenuSetWatchHoverStatus(VPBTranslation.T("hook.watch.status.hold_confirm", "Hold to confirm"));
        }

        internal void QuickMenuWatchEndPress()
        {
            if (m_WatchPressIdx < 0) return;
            int kind = m_WatchPressKind;
            int idx = m_WatchPressIdx;
            m_WatchPressIdx = -1;
            QuickMenuClearWatchHoverStatus(0);
            if (kind == 0) QuickMenuSyncWatchSlot(idx);
            else QuickMenuSyncWatchAssignSlot(idx);
        }

        private void QuickMenuTickWatchHold(float now)
        {
            if (m_WatchPressIdx < 0) return;
            if (!m_WatchVisibleNow)
            {
                m_WatchPressIdx = -1;
                return;
            }
            if (now - m_WatchPressStart < QuickMenuWatchHoldConfirmSec) return;

            int kind = m_WatchPressKind;
            int idx = m_WatchPressIdx;
            QuickMenuAssignableAction act = m_WatchPressAction;
            m_WatchPressIdx = -1;
            m_WatchHoldFired = true;
            QuickMenuClearWatchHoverStatus(0);
            if (kind == 0)
            {
                if (act == QuickMenuAssignableAction.Save) m_QuickMenuSavePopupTargetIdx = idx;
                QuickMenuSyncWatchSlot(idx);
            }
            else QuickMenuSyncWatchAssignSlot(idx);
            try { QuickMenuExecuteAssignment(act); } catch { }
        }

        private string QuickMenuWatchPinTipText()
        {
            if (m_WatchWorldLocked)
                return m_WatchCfgGripPin
                    ? VPBTranslation.T("hook.watch.tip.unlock_grip", "Reattach to wrist — or squeeze that hand's grip")
                    : VPBTranslation.T("hook.watch.tip.unlock", "Reattach to wrist");
            return m_WatchCfgGripPin
                ? VPBTranslation.T("hook.watch.tip.lock_grip", "Leave in place — or squeeze the watch hand's grip. Drag bezel to move")
                : VPBTranslation.T("hook.watch.tip.lock", "Lock in place — drag bezel to move");
        }

        private string QuickMenuWatchEditTipText()
        {
            return m_WatchEditMode
                ? VPBTranslation.T("hook.watch.tip.edit_done", "Done assigning")
                : VPBTranslation.T("hook.watch.tip.edit", "Assign watch buttons");
        }

        private string QuickMenuWatchExpandTipText()
        {
            return m_WatchFaceMode == QuickMenuWatchFaceMode.Expanded
                ? VPBTranslation.T("hook.watch.tip.compact", "Compact face")
                : VPBTranslation.T("hook.watch.tip.expand", "Full face");
        }

        private string QuickMenuWatchCueTextValue()
        {
            if (m_WatchCfgGripPin)
                return VPBTranslation.T("hook.watch.cue_grip",
                    "Turn your inner wrist toward you to show the watch. Squeeze that hand's grip to take it off and leave it hanging; squeeze again to put it back on.");
            return VPBTranslation.T("hook.watch.cue",
                "Turn your inner wrist toward you to show the watch. Lock pins it in place so you can drop your arm.");
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
            if (cfg.QuickMenuVrWatchButtonsPages == null) return;
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
            if (p < 0 || p >= m_WatchPageAssignments.Length) p = 0;
            var row = m_WatchPageAssignments[p];
            if (row == null || slotIdx >= row.Length) return QuickMenuAssignableAction.None;
            return row[slotIdx];
        }

        private void QuickMenuSetWatchSlotAction(int slotIdx, QuickMenuAssignableAction action)
        {
            if (m_WatchPageAssignments == null) QuickMenuEnsureWatchAssignments();
            if (m_WatchPageAssignments == null) return;
            if (slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return;
            if (m_WatchCurrentPage < 0) m_WatchCurrentPage = 0;
            if (m_WatchCurrentPage >= m_WatchPageAssignments.Length) m_WatchCurrentPage = 0;
            var row = m_WatchPageAssignments[m_WatchCurrentPage];
            if (row == null || slotIdx >= row.Length) return;
            row[slotIdx] = action;
            QuickMenuPersistWatchAssignments();
            QuickMenuSyncWatchAssignSlot(slotIdx);
        }

        private void QuickMenuPersistWatchAssignments()
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            cfg.EnsureWatchButtonPages();
            var dstPages = cfg.QuickMenuVrWatchButtonsPages;
            if (dstPages == null || m_WatchPageAssignments == null) return;
            cfg.QuickMenuVrWatchCurrentPage = Mathf.Clamp(m_WatchCurrentPage, 0, QuickMenuPageCount - 1);
            // QuickMenuPageCount and VPBConfig.QuickMenuVrWatchPageCount are declared apart; clamp
            // rather than trust them to stay equal.
            int pages = Mathf.Min(dstPages.Length, m_WatchPageAssignments.Length);
            for (int p = 0; p < pages; p++)
            {
                string[] dst = dstPages[p];
                QuickMenuAssignableAction[] src = m_WatchPageAssignments[p];
                if (dst == null || src == null) continue;
                int slots = Mathf.Min(dst.Length, src.Length);
                for (int s = 0; s < slots; s++)
                    dst[s] = QuickMenuActionToId(src[s]);
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
            m_WatchStatusNeedRebuild = true;
            QuickMenuRefreshWatchStatus();
            if (m_WatchAssignTargetIdx >= 0) QuickMenuSyncWatchAssignSlot(m_WatchAssignTargetIdx);
        }

        private void QuickMenuToggleWatchEdit()
        {
            m_WatchEditMode = !m_WatchEditMode;
            if (m_WatchEditMode)
            {
                m_WatchWorldLockBeforeEdit = m_WatchWorldLocked;
                QuickMenuSetWatchWorldLocked(true);
                QuickMenuSetWatchAssignTarget(QuickMenuClampWatchAssignTarget(m_WatchAssignTargetIdx));
            }
            else
            {
                QuickMenuSetWatchAssignTarget(-1);
                QuickMenuSetWatchWorldLocked(m_WatchWorldLockBeforeEdit);
            }

            m_WatchStatusNeedRebuild = true;
            QuickMenuApplyWatchLayout();
            QuickMenuRefreshWatchChromeColors();
            QuickMenuSyncWatchAssignAll();
            QuickMenuRefreshWatchStatus();
        }

        private void QuickMenuToggleWatchExpanded()
        {
            QuickMenuSetWatchExpanded(m_WatchFaceMode != QuickMenuWatchFaceMode.Expanded);
        }

        private void QuickMenuSetWatchExpanded(bool expanded)
        {
            var c = VPBConfig.Instance;
            if (c == null) return;
            c.QuickMenuVrWatchExpanded = expanded;
            c.QuickMenuVrWatchCollapsed = false;
            try { c.Save(false, true); } catch { }
            m_WatchFaceMode = expanded ? QuickMenuWatchFaceMode.Expanded : QuickMenuWatchFaceMode.Compact;
            if (m_WatchEditMode)
            {
                int clamped = QuickMenuClampWatchAssignTarget(m_WatchAssignTargetIdx);
                if (clamped != m_WatchAssignTargetIdx) QuickMenuSetWatchAssignTarget(clamped);
            }
            QuickMenuApplyWatchLayout();
            QuickMenuRefreshWatchChromeTips();
        }

        private int QuickMenuWatchVisibleAssignSlotCount()
        {
            return m_WatchFaceMode == QuickMenuWatchFaceMode.Expanded
                ? QuickMenuWatchAssignSlotCount
                : QuickMenuWatchCompactSlotCount;
        }

        private int QuickMenuClampWatchAssignTarget(int idx)
        {
            int max = QuickMenuWatchVisibleAssignSlotCount();
            if (idx < 0 || idx >= max) return 0;
            return idx;
        }

        private void QuickMenuSetWatchCollapsed(bool collapsed)
        {
            var c = VPBConfig.Instance;
            if (c == null) return;
            if (collapsed && m_WatchEditMode) QuickMenuToggleWatchEdit();
            c.QuickMenuVrWatchCollapsed = collapsed;
            try { c.Save(false, true); } catch { }
            m_WatchFaceMode = collapsed
                ? QuickMenuWatchFaceMode.Collapsed
                : (c.QuickMenuVrWatchExpanded ? QuickMenuWatchFaceMode.Expanded : QuickMenuWatchFaceMode.Compact);
            if (collapsed) m_WatchGlanced = true;
            QuickMenuApplyWatchLayout();
        }

        private void QuickMenuReplayWatchCue()
        {
            m_WatchTutorialUntil = Time.unscaledTime + QuickMenuWatchTutorialSec;
        }

        private void QuickMenuToggleWatchWorldLock()
        {
            bool want = !m_WatchWorldLocked;
            // Edit mode force-locks and restores the pre-edit value on exit; an explicit pin
            // press while editing must not be thrown away by that restore.
            if (m_WatchEditMode) m_WatchWorldLockBeforeEdit = want;
            QuickMenuSetWatchWorldLocked(want);
        }

        private void QuickMenuSetWatchWorldLocked(bool locked)
        {
            if (locked == m_WatchWorldLocked) return;
            m_WatchWorldLocked = locked;
            m_WatchFrozen = false;
            QuickMenuEnsureWatchParent();
            m_WatchStatusNeedRebuild = true;
            QuickMenuRefreshWatchChromeColors();
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
            int sections = QuickMenuWatchAssignSectionStarts.Length;

            float cellStepX = QuickMenuWatchAssignCellW + QuickMenuWatchAssignGap;
            float cellStepY = QuickMenuWatchAssignCellH + QuickMenuWatchAssignGap;
            float sheetW = cols * QuickMenuWatchAssignCellW + (cols - 1) * QuickMenuWatchAssignGap
                + QuickMenuWatchAssignPad * 2f;

            float sheetH = QuickMenuWatchAssignPad * 2f;
            for (int s = 0; s < sections; s++)
            {
                int start = QuickMenuWatchAssignSectionStarts[s];
                int end = (s + 1 < sections) ? QuickMenuWatchAssignSectionStarts[s + 1] : n;
                int rows = (end - start + cols - 1) / cols;
                sheetH += QuickMenuWatchAssignHeaderH + QuickMenuWatchAssignGap + rows * cellStepY;
                if (s + 1 < sections) sheetH += QuickMenuWatchAssignSectionGap;
            }
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

            m_WatchAssignPickBgs = new Image[n];
            float originX = -sheetW * 0.5f + QuickMenuWatchAssignPad + QuickMenuWatchAssignCellW * 0.5f;
            float cursorY = sheetH * 0.5f - QuickMenuWatchAssignPad;

            for (int s = 0; s < sections; s++)
            {
                int start = QuickMenuWatchAssignSectionStarts[s];
                int end = (s + 1 < sections) ? QuickMenuWatchAssignSectionStarts[s + 1] : n;

                Text header = UI.CreateLabel(sheet, QuickMenuWatchSectionTitle(s), QuickMenuWatchSheetHeaderFont,
                    GalleryUiColorTokens.TextMuted, TextAnchor.MiddleLeft,
                    HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                    AnchorPresets.middleCenter,
                    new Vector2(sheetW - QuickMenuWatchAssignPad * 2f, QuickMenuWatchAssignHeaderH),
                    new Vector2(0f, cursorY - QuickMenuWatchAssignHeaderH * 0.5f), "SectionHeader_" + s);
                if (header != null) header.resizeTextForBestFit = false;
                cursorY -= QuickMenuWatchAssignHeaderH + QuickMenuWatchAssignGap;

                for (int i = start; i < end; i++)
                {
                    int rel = i - start;
                    int col = rel % cols;
                    int row = rel / cols;
                    QuickMenuBuildWatchAssignPick(sheet, i,
                        originX + col * cellStepX,
                        cursorY - QuickMenuWatchAssignCellH * 0.5f - row * cellStepY);
                }

                int usedRows = (end - start + cols - 1) / cols;
                cursorY -= usedRows * cellStepY;
                if (s + 1 < sections) cursorY -= QuickMenuWatchAssignSectionGap;
            }

            sheet.SetActive(false);
            QuickMenuPlaceWatchAssignSheet();
        }

        private void QuickMenuBuildWatchAssignPick(GameObject sheet, int catalogIdx, float x, float y)
        {
            var act = QuickMenuWatchAssignCatalog[catalogIdx];
            GameObject go = new GameObject("WatchAssignPick_" + catalogIdx);
            go.transform.SetParent(sheet.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(QuickMenuWatchAssignCellW, QuickMenuWatchAssignCellH);
            rt.anchoredPosition = new Vector2(x, y);

            Color nb = QmBackdropAssignedOpaque;
            Image img = AddQuickMenuRoundedBg(go, nb);
            m_WatchAssignPickBgs[catalogIdx] = img;
            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.targetGraphic = img;
            QuickMenuAttachWatchHover(go, img, nb, QmBackdropAssignedHoverOpaque, QuickMenuGetActionTooltip(act, -1));

            Sprite spr = QuickMenuGetAssignPopupIcon(act);
            if (spr != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(go.transform, false);
                Image icon = iconGO.AddComponent<Image>();
                UI.SetIconSprite(icon, spr);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.5f, 1f);
                irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.sizeDelta = new Vector2(QuickMenuWatchAssignIcon, QuickMenuWatchAssignIcon);
                irt.anchoredPosition = new Vector2(0f, -2f);
            }

            Text label = UI.CreateLabel(go, QuickMenuGetActionShortLabel(act), QuickMenuWatchSheetLabelFont,
                QuickMenuWatchLabelColor, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.middleCenter, new Vector2(QuickMenuWatchAssignCellW + 6f, 14f),
                new Vector2(0f, -(QuickMenuWatchAssignCellH * 0.5f) + 8f), "PickLabel");
            if (label != null) label.resizeTextForBestFit = false;

            int iCopy = catalogIdx;
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

        private static string QuickMenuWatchSectionTitle(int section)
        {
            switch (section)
            {
                case 0: return VPBTranslation.T("hook.watch.section.core", "Core");
                case 1: return VPBTranslation.T("hook.watch.section.scene", "Scene");
                case 2: return VPBTranslation.T("hook.watch.section.toggles", "Toggles");
                case 3: return VPBTranslation.T("hook.watch.section.items", "Items");
                case 4: return VPBTranslation.T("hook.watch.section.perf", "Performance");
                case 5: return VPBTranslation.T("hook.watch.section.random", "Random");
                default: return VPBTranslation.T("hook.watch.section.open", "Open category");
            }
        }

        private static string QuickMenuGetActionShortLabel(QuickMenuAssignableAction a)
        {
            switch (a)
            {
                case QuickMenuAssignableAction.None: return VPBTranslation.T("hook.watch.lbl.none", "Empty");
                case QuickMenuAssignableAction.CoreSettingsButton: return VPBTranslation.T("hook.watch.lbl.settings", "Settings");
                case QuickMenuAssignableAction.CorePageButton: return VPBTranslation.T("hook.watch.lbl.page", "Page");
                case QuickMenuAssignableAction.CreateGallery: return VPBTranslation.T("hook.watch.lbl.gallery", "Gallery");
                case QuickMenuAssignableAction.ShowHide: return VPBTranslation.T("hook.watch.lbl.show", "Show");
                case QuickMenuAssignableAction.BringFront: return VPBTranslation.T("hook.watch.lbl.front", "Front");
                case QuickMenuAssignableAction.CloseAll: return VPBTranslation.T("hook.watch.lbl.close", "Close");
                case QuickMenuAssignableAction.Save: return VPBTranslation.T("hook.watch.lbl.save", "Save");
                case QuickMenuAssignableAction.Undo: return VPBTranslation.T("hook.watch.lbl.undo", "Undo");
                case QuickMenuAssignableAction.Redo: return VPBTranslation.T("hook.watch.lbl.redo", "Redo");
                case QuickMenuAssignableAction.Hub: return VPBTranslation.T("hook.watch.lbl.hub", "Hub");
                case QuickMenuAssignableAction.Cleanup: return VPBTranslation.T("hook.watch.lbl.cleanup", "Cleanup");
                case QuickMenuAssignableAction.CreatorMode: return VPBTranslation.T("hook.watch.lbl.tools", "Tools");
                case QuickMenuAssignableAction.TargetAtom: return VPBTranslation.T("hook.watch.lbl.target", "Target");
                case QuickMenuAssignableAction.ReplaceAddToggle: return VPBTranslation.T("hook.watch.lbl.replace", "Replace");
                case QuickMenuAssignableAction.AutoHideGallery: return VPBTranslation.T("hook.watch.lbl.autohide", "AutoHide");
                case QuickMenuAssignableAction.ShowHiddenPackages: return VPBTranslation.T("hook.watch.lbl.hidden", "Hidden");
                case QuickMenuAssignableAction.FpsCounter: return VPBTranslation.T("hook.watch.lbl.fps", "FPS");
                case QuickMenuAssignableAction.History: return VPBTranslation.T("hook.watch.lbl.history", "History");
                case QuickMenuAssignableAction.PerfMode: return VPBTranslation.T("hook.watch.lbl.perf", "Perf");
                case QuickMenuAssignableAction.ToggleImportSidebar: return VPBTranslation.T("hook.watch.lbl.import", "Import");
                case QuickMenuAssignableAction.RemoveAllClothing: return VPBTranslation.T("hook.watch.lbl.noclothes", "Strip");
                case QuickMenuAssignableAction.RemoveAllHair: return VPBTranslation.T("hook.watch.lbl.nohair", "No hair");
                case QuickMenuAssignableAction.StarFilter: return VPBTranslation.T("hook.watch.lbl.stars", "Stars");
                case QuickMenuAssignableAction.CompressCache: return VPBTranslation.T("hook.watch.lbl.compress", "Compress");
                case QuickMenuAssignableAction.PerfStepUp: return VPBTranslation.T("hook.watch.lbl.perfup", "Perf +");
                case QuickMenuAssignableAction.PerfStepDown: return VPBTranslation.T("hook.watch.lbl.perfdown", "Perf -");
                case QuickMenuAssignableAction.Random: return VPBTranslation.T("hook.watch.lbl.random", "Random");
                case QuickMenuAssignableAction.RandomScenes: return VPBTranslation.T("hook.watch.lbl.rscene", "R Scene");
                case QuickMenuAssignableAction.RandomSubScenes: return VPBTranslation.T("hook.watch.lbl.rsub", "R Sub");
                case QuickMenuAssignableAction.RandomClothing: return VPBTranslation.T("hook.watch.lbl.rcloth", "R Cloth");
                case QuickMenuAssignableAction.RandomHair: return VPBTranslation.T("hook.watch.lbl.rhair", "R Hair");
                case QuickMenuAssignableAction.RandomPose: return VPBTranslation.T("hook.watch.lbl.rpose", "R Pose");
                case QuickMenuAssignableAction.RandomAppearance: return VPBTranslation.T("hook.watch.lbl.rlook", "R Look");
                case QuickMenuAssignableAction.RandomSkin: return VPBTranslation.T("hook.watch.lbl.rskin", "R Skin");
                case QuickMenuAssignableAction.RandomSceneImport: return VPBTranslation.T("hook.watch.lbl.rimport", "R Import");
                case QuickMenuAssignableAction.OpenCategoryScenes: return VPBTranslation.T("hook.watch.lbl.scenes", "Scenes");
                case QuickMenuAssignableAction.OpenCategorySubScenes: return VPBTranslation.T("hook.watch.lbl.subscenes", "SubScene");
                case QuickMenuAssignableAction.OpenCategoryClothing: return VPBTranslation.T("hook.watch.lbl.clothing", "Clothing");
                case QuickMenuAssignableAction.OpenCategoryHair: return VPBTranslation.T("hook.watch.lbl.hair", "Hair");
                case QuickMenuAssignableAction.OpenCategoryPose: return VPBTranslation.T("hook.watch.lbl.pose", "Pose");
                case QuickMenuAssignableAction.OpenCategoryAppearance: return VPBTranslation.T("hook.watch.lbl.looks", "Looks");
                case QuickMenuAssignableAction.OpenCategoryPlugins: return VPBTranslation.T("hook.watch.lbl.plugins", "Plugins");
                case QuickMenuAssignableAction.OpenCategorySkin: return VPBTranslation.T("hook.watch.lbl.skin", "Skin");
                case QuickMenuAssignableAction.OpenCategoryAll: return VPBTranslation.T("hook.watch.lbl.all", "All");
                case QuickMenuAssignableAction.PageNext: return VPBTranslation.T("hook.watch.lbl.next", "Next");
                case QuickMenuAssignableAction.PagePrev: return VPBTranslation.T("hook.watch.lbl.prev", "Prev");
                case QuickMenuAssignableAction.SwitchWatchHand: return VPBTranslation.T("hook.watch.lbl.hand", "Hand");
                default: return "";
            }
        }

        private void QuickMenuPlaceWatchAssignSheet()
        {
            if (m_WatchAssignSheetRt == null || m_WatchCanvasRT == null) return;
            float bezelW = m_WatchCanvasRT.sizeDelta.x;
            float side = m_WatchIsLeft ? 1f : -1f;
            m_WatchAssignSheetRt.anchoredPosition = new Vector2(
                side * (bezelW * 0.5f + 12f + m_WatchAssignSheetW * 0.5f), 0f);
        }

        private void QuickMenuSyncWatchAssignAll()
        {
            for (int i = 0; i < QuickMenuWatchAssignSlotCount; i++)
                QuickMenuSyncWatchAssignSlot(i);
        }

        /// <summary>
        /// Per-frame. PerfMode is the only assignable whose icon resolves differently as state
        /// changes, and a collapsed face shows no slots at all — do nothing in every other case.
        /// </summary>
        private void QuickMenuSyncWatchAssignLiveIcons()
        {
            if (m_WatchAssignGos == null || !m_WatchActive) return;
            if (m_WatchFaceMode == QuickMenuWatchFaceMode.Collapsed) return;
            int n = QuickMenuWatchVisibleAssignSlotCount();
            for (int i = 0; i < n; i++)
            {
                if (QuickMenuGetWatchSlotAction(i) == QuickMenuAssignableAction.PerfMode)
                    QuickMenuSyncWatchAssignSlot(i);
            }
        }

        private void QuickMenuSyncWatchAssignSlot(int slotIdx)
        {
            if (m_WatchAssignGos == null || slotIdx < 0 || slotIdx >= QuickMenuWatchAssignSlotCount) return;
            GameObject go = m_WatchAssignGos[slotIdx];
            if (go == null) return;

            var act = QuickMenuGetWatchSlotAction(slotIdx);
            bool assigned = act != QuickMenuAssignableAction.None;
            Sprite icon = assigned ? QuickMenuGetAssignPopupIcon(act) : m_QmIconAssignEmpty;

            Image slotIcon = m_WatchAssignIcons[slotIdx];
            if (slotIcon != null)
            {
                if (slotIcon.sprite != icon) UI.SetIconSprite(slotIcon, icon);
                bool on = icon != null;
                if (slotIcon.enabled != on) slotIcon.enabled = on;
                if (on)
                {
                    Color want = assigned ? Color.white : QuickMenuWatchEmptyIconTint;
                    if (slotIcon.color != want) slotIcon.color = want;
                }
            }

            Text label = m_WatchAssignLabels[slotIdx];
            if (label != null && m_WatchCfgLabels)
            {
                string s = assigned ? QuickMenuGetActionShortLabel(act) : "";
                if (!string.Equals(label.text, s, System.StringComparison.Ordinal)) label.text = s;
            }

            Image bg = m_WatchAssignBackdrops[slotIdx];
            if (bg == null) return;

            bool pressing = m_WatchPressIdx == slotIdx && m_WatchPressKind == 1;
            bool selected = m_WatchEditMode && slotIdx == m_WatchAssignTargetIdx;
            Color normal;
            Color hover;
            if (pressing)
            {
                normal = QuickMenuWatchChromeDanger;
                hover = QuickMenuWatchChromeDangerHover;
            }
            else if (selected)
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

        internal void QuickMenuSyncWatchSlot(int hudIdx)
        {
            if (m_WatchSlotGos == null || hudIdx < 0 || hudIdx >= QuickMenuWatchHudSlotCount) return;
            GameObject watchGo = m_WatchSlotGos[hudIdx];
            if (watchGo == null) return;
            if (m_WatchFaceMode != QuickMenuWatchFaceMode.Expanded) return;

            GameObject hudGo = null;
            if (m_QuickMenuGridButtons != null && hudIdx < m_QuickMenuGridButtons.Length)
                hudGo = m_QuickMenuGridButtons[hudIdx];

            bool hudOn = hudGo != null && hudGo.activeSelf;
            if (watchGo.activeSelf != hudOn) watchGo.SetActive(hudOn);
            if (!hudOn) return;

            var act = QuickMenuGetSlotAction(hudIdx);
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
            if (icon == null) icon = QuickMenuGetAssignPopupIcon(act);

            Image slotIcon = m_WatchSlotIcons[hudIdx];
            if (slotIcon != null)
            {
                if (slotIcon.sprite != icon) UI.SetIconSprite(slotIcon, icon);
                bool on = icon != null;
                if (slotIcon.enabled != on) slotIcon.enabled = on;
            }

            Text label = m_WatchSlotLabels[hudIdx];
            if (label != null && m_WatchCfgLabels)
            {
                string s = QuickMenuGetActionShortLabel(act);
                if (!string.Equals(label.text, s, System.StringComparison.Ordinal)) label.text = s;
            }

            Image bg = m_WatchSlotBackdrops[hudIdx];
            if (bg == null) return;

            bool assigned = act != QuickMenuAssignableAction.None;
            bool pressing = m_WatchPressIdx == hudIdx && m_WatchPressKind == 0;
            Color normal;
            Color hover;
            if (m_QuickMenuEditMode)
            {
                normal = QuickMenuWatchSlotDisabled;
                hover = QuickMenuWatchSlotDisabled;
            }
            else if (pressing)
            {
                normal = QuickMenuWatchChromeDanger;
                hover = QuickMenuWatchChromeDangerHover;
            }
            else
            {
                normal = assigned ? QmBackdropAssignedOpaque : QmBackdropEmptyOpaque;
                hover = assigned ? QmBackdropAssignedHoverOpaque : QmBackdropEmptyHoverOpaque;
            }
            QuickMenuApplyBackdropColors(bg, normal, hover);

            if (slotIcon != null)
            {
                Color want = m_QuickMenuEditMode ? QuickMenuWatchEmptyIconTint : Color.white;
                if (slotIcon.color != want) slotIcon.color = want;
            }
        }

        private void QuickMenuSyncWatchHudEditState()
        {
            if (m_WatchHudEditShown == m_QuickMenuEditMode) return;
            m_WatchHudEditShown = m_QuickMenuEditMode;
            m_WatchStatusNeedRebuild = true;
            for (int i = 0; i < QuickMenuWatchHudSlotCount; i++) QuickMenuSyncWatchSlot(i);
        }

        private void QuickMenuRefreshWatchStatus()
        {
            if (m_WatchStatusText == null) return;
            QuickMenuSyncWatchTip();

            if (!m_WatchStatusNeedRebuild && m_WatchStatusShown.Length > 0) return;
            m_WatchStatusNeedRebuild = false;

            m_WatchStatusSb.Length = 0;
            m_WatchStatusSb.Append(m_WatchIsLeft
                ? VPBTranslation.T("hook.watch.hand.left", "L")
                : VPBTranslation.T("hook.watch.hand.right", "R"));

            if (m_WatchFaceMode == QuickMenuWatchFaceMode.Expanded)
            {
                m_WatchStatusSb.Append(" · ").Append(m_WatchCurrentPage + 1).Append('/').Append(QuickMenuPageCount);
                m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.pagesuffix", " sides"));
            }

            m_WatchStatusSb.Append(" · ");
            if (m_QuickMenuEditMode)
                m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.hud_edit_short", "HUD EDIT"));
            else if (m_WatchEditMode)
                m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.edit", "EDIT"));
            else if (m_WatchWorldLocked)
                m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.locked", "locked"));
            else
            {
                switch (m_WatchCfgShowWhen)
                {
                    case QuickMenuVrWatchShowWhen.Always:
                        m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.always", "always"));
                        break;
                    case QuickMenuVrWatchShowWhen.Menu:
                        m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.menu", "menu"));
                        break;
                    default:
                        m_WatchStatusSb.Append(VPBTranslation.T("hook.watch.status.glance", "glance"));
                        break;
                }
            }

            string s = m_WatchStatusSb.ToString();
            if (!string.Equals(m_WatchStatusShown, s, System.StringComparison.Ordinal))
            {
                m_WatchStatusText.text = s;
                m_WatchStatusShown = s;
            }
        }

        private void QuickMenuSyncWatchTip()
        {
            if (m_WatchTipGo == null || m_WatchTipText == null) return;

            string src = m_WatchHoverStatus;
            bool show = !string.IsNullOrEmpty(src);
            if (m_WatchTipGo.activeSelf != show) m_WatchTipGo.SetActive(show);
            if (!show)
            {
                m_WatchTipSrc = null;
                return;
            }

            if (!string.Equals(src, m_WatchTipSrc, System.StringComparison.Ordinal))
            {
                m_WatchTipSrc = src;
                m_WatchTipFlat = src.IndexOf('\n') >= 0 ? src.Replace("\n", "  ·  ") : src;
            }
            if (!string.Equals(m_WatchTipShown, m_WatchTipFlat, System.StringComparison.Ordinal))
            {
                m_WatchTipText.text = m_WatchTipFlat;
                m_WatchTipShown = m_WatchTipFlat;
            }
        }

        private void QuickMenuRefreshWatchChromeColors()
        {
            if (m_WatchChromeBgs == null) return;
            QuickMenuSetChromeState(QuickMenuWatchChromePin, m_WatchWorldLocked);
            QuickMenuSetChromeState(QuickMenuWatchChromeEdit, m_WatchEditMode);
            QuickMenuSetChromeState(QuickMenuWatchChromeCollapse, false);
            QuickMenuSetChromeState(QuickMenuWatchChromeHand, false);
            QuickMenuSetChromeState(QuickMenuWatchChromeExpand, m_WatchFaceMode == QuickMenuWatchFaceMode.Expanded);
            QuickMenuSetChromeState(QuickMenuWatchChromeHelp, false);

            Image pinIcon = m_WatchChromeIcons[QuickMenuWatchChromePin];
            if (pinIcon != null)
            {
                Sprite want = m_WatchWorldLocked ? m_WatchIconPinOn : m_WatchIconPinOff;
                if (want != null && pinIcon.sprite != want) UI.SetIconSprite(pinIcon, want);
            }
            QuickMenuSyncWatchExpandIcon();
            QuickMenuRefreshWatchChromeTips();
        }

        private void QuickMenuSetChromeState(int idx, bool on)
        {
            Image bg = m_WatchChromeBgs[idx];
            if (bg == null) return;
            Color normal = on ? QuickMenuWatchChromeOn : QuickMenuWatchChromeIdle;
            Color hover = on ? QuickMenuWatchChromeOnHover : QuickMenuWatchChromeIdleHover;
            bg.color = normal;
            var h = m_WatchChromeHovers[idx];
            if (h != null)
            {
                h.normal = normal;
                h.hover = hover;
            }
        }

        private void QuickMenuRefreshWatchChromeTips()
        {
            if (m_WatchChromeTips == null) return;
            QuickMenuSetChromeTip(QuickMenuWatchChromePin, QuickMenuWatchPinTipText());
            QuickMenuSetChromeTip(QuickMenuWatchChromeEdit, QuickMenuWatchEditTipText());
            QuickMenuSetChromeTip(QuickMenuWatchChromeExpand, QuickMenuWatchExpandTipText());
            QuickMenuSetChromeTip(QuickMenuWatchChromeCollapse, VPBTranslation.T("hook.watch.tip.collapse", "Minimise to dot"));
            QuickMenuSetChromeTip(QuickMenuWatchChromeHand, VPBTranslation.T("hook.watch.tip.hand", "Switch watch hand"));
            QuickMenuSetChromeTip(QuickMenuWatchChromeHelp, VPBTranslation.T("hook.watch.tip.help", "Replay watch tips"));
        }

        private void QuickMenuSetChromeTip(int idx, string tip)
        {
            var t = m_WatchChromeTips[idx];
            if (t != null) t.tip = tip;
        }

        private void QuickMenuApplyWatchPose()
        {
            Transform t = m_WatchTf;
            if (t == null || m_WatchHand == null) return;

            Transform parent = t.parent;
            float lossy = parent != null ? parent.lossyScale.x : 1f;
            if (lossy < 1e-5f) lossy = 1f;
            float metersPerPx = VpbWorldSpaceUiScale.MetersPerUiPixel * m_WatchCfgScaleMul;
            if (metersPerPx < 1e-5f) metersPerPx = VpbWorldSpaceUiScale.MetersPerUiPixel;
            float fadeScale = Mathf.Lerp(QuickMenuWatchFadeScaleFrom, 1f, m_WatchFade);
            float local = metersPerPx * fadeScale / lossy;
            if (Mathf.Abs(local - m_WatchLastAppliedScale) > 1e-7f)
            {
                m_WatchLastAppliedScale = local;
                t.localScale = new Vector3(local, local, local);
            }

            if (m_WatchWorldLocked || m_WatchFrozen)
            {
                QuickMenuTickWatchFreeze(t);
                return;
            }

            Vector3 offset = m_WatchCfgOffset;
            if (!m_WatchIsLeft) offset.x = -offset.x;
            if (m_WatchCfgToward != 0f)
                offset += (QuickMenuWatchWristLocalRot * Vector3.forward) * m_WatchCfgToward;
            t.localPosition = offset;
            // Trim rides on the wrist rotation only: with face-user on, the billboard aim below
            // replaces this rotation outright, so the trim is dead weight and the settings row hides.
            // Not mirrored on the right hand — unlike the position offset, an explicitly dialled tilt
            // should not invert when the watch changes hands.
            t.localRotation = m_WatchCfgFaceRotActive
                ? QuickMenuWatchWristLocalRot * m_WatchCfgFaceRot
                : QuickMenuWatchWristLocalRot;

            Transform cam = QuickMenuGetPlayerCamera();
            if (m_WatchCfgFaceUser && cam != null)
            {
                Vector3 from = cam.position;
                if (m_WatchCfgShoulderBlend > 0f)
                    from += (cam.right * (m_WatchIsLeft ? QuickMenuWatchShoulderOutM : -QuickMenuWatchShoulderOutM)
                          - cam.up * QuickMenuWatchShoulderDownM) * m_WatchCfgShoulderBlend;
                Vector3 dir = t.position - from;
                if (dir.sqrMagnitude > 1e-6f) QuickMenuAimWatchFaceAtEye(t, dir, cam);
            }

            QuickMenuTickWatchFreeze(t);
        }

        /// <summary>
        /// Turn the face toward the eye and keep it upright <em>on the player's retina</em>.
        ///
        /// Two things make this different from the gallery pane's billboard
        /// (<c>LookRotation(pos - camPos, Vector3.up)</c>), and both caused the upside-down face:
        ///
        /// 1. The pane lives under <c>mainHUDAttachPoint</c> — outside <c>worldScaleTransform</c>, scale ~1.
        ///    The watch is parented to a live VR controller, i.e. inside VaM's world-scale chain (that is
        ///    why the scale math above divides by <c>parent.lossyScale.x</c> at all). Assigning
        ///    <c>Transform.rotation</c> there round-trips the value through the parent's *extracted*
        ///    rotation, which is not well defined for a scaled/mirrored basis and can land the face a
        ///    half turn over. So convert the aim into parent space with the parent's inverse MATRIX
        ///    and assign a local rotation instead.
        /// 2. Any fixed up-hint (world up, <c>cam.up</c>, or the wrist's own up) bakes in an assumption
        ///    about VaM's controller basis. Rather than assume one, read the result back off the live
        ///    world matrix and flip the hint when the face's own up came out pointing down the player's
        ///    view. The read-back is ground truth, so no rig convention can leave the face inverted.
        /// </summary>
        private void QuickMenuAimWatchFaceAtEye(Transform t, Vector3 dirWorld, Transform cam)
        {
            Vector3 fwd = dirWorld.normalized;
            Vector3 camUp = cam.up;

            // Eye-relative up, not world up: tilting your head down at your own wrist keeps cam.up
            // square to the view axis, where world up would be nearly parallel to it and collapse.
            Vector3 up = camUp;
            float par = up.x * fwd.x + up.y * fwd.y + up.z * fwd.z;
            if (par > 0.999f || par < -0.999f) up = cam.forward;
            if (m_WatchFaceAimFlip) up = -up;

            QuickMenuSetWatchAimLocal(t, fwd, up);

            // Ground truth is the world MATRIX, not t.up: Transform.up/right/forward are defined as
            // rotation * axis, so they report the same extracted rotation this method is working
            // around and would happily agree with a wrong result.
            Vector3 faceUp = t.localToWorldMatrix.MultiplyVector(Vector3.up);
            float len2 = faceUp.sqrMagnitude;
            if (len2 < 1e-12f) return;
            float inv = 1f / Mathf.Sqrt(len2);
            float onRetina = (faceUp.x * camUp.x + faceUp.y * camUp.y + faceUp.z * camUp.z) * inv;
            // Band, not a sign test: a face seen edge-on sits near zero and must not chatter.
            if (onRetina >= -0.15f) return;
            m_WatchFaceAimFlip = !m_WatchFaceAimFlip;
            QuickMenuLogWatchAimFlipOnce(t);
        }

        /// <summary>
        /// Aim <paramref name="t"/> without ever assigning a world rotation — see
        /// <see cref="QuickMenuAimWatchFaceAtEye"/> for why the parent's extracted rotation is not usable.
        /// <c>MultiplyVector</c> on the parent's inverse matrix is the exact world-to-parent direction
        /// map (rotation and scale, no translation); <c>InverseTransformDirection</c> is not — it is
        /// <c>Quaternion.Inverse(parent.rotation) * v</c>, the same extraction being avoided.
        /// </summary>
        private static void QuickMenuSetWatchAimLocal(Transform t, Vector3 fwd, Vector3 up)
        {
            Transform parent = t.parent;
            if (parent == null)
            {
                t.rotation = Quaternion.LookRotation(fwd, up);
                return;
            }
            Matrix4x4 toParent = parent.worldToLocalMatrix;
            Vector3 fwdLocal = toParent.MultiplyVector(fwd);
            Vector3 upLocal = toParent.MultiplyVector(up);
            if (fwdLocal.sqrMagnitude < 1e-10f || upLocal.sqrMagnitude < 1e-10f) return;
            t.localRotation = Quaternion.LookRotation(fwdLocal, upLocal);
        }

        private void QuickMenuLogWatchAimFlipOnce(Transform t)
        {
            if (m_WatchFaceFlipLogged) return;
            m_WatchFaceFlipLogged = true;
            try
            {
                Transform p = t.parent;
                Vector3 ls = p != null ? p.lossyScale : Vector3.one;
                LogUtil.Log("[VPB] VR wrist watch face aim inverted by parent basis — corrected."
                    + " parent=" + (p != null ? p.name : "null")
                    + " lossyScale=" + ls.x.ToString("F3") + "," + ls.y.ToString("F3") + "," + ls.z.ToString("F3"));
            }
            catch { }
        }

        /// <summary>Where the face would sit on the wrist right now — valid even while frozen.</summary>
        private Vector3 QuickMenuWatchWristAnchorWorld()
        {
            Transform h = m_WatchHand;
            if (h == null) return m_WatchTf != null ? m_WatchTf.position : Vector3.zero;
            Vector3 offset = m_WatchCfgOffset;
            if (!m_WatchIsLeft) offset.x = -offset.x;
            if (m_WatchCfgToward != 0f)
                offset += (QuickMenuWatchWristLocalRot * Vector3.forward) * m_WatchCfgToward;
            return h.TransformPoint(offset);
        }

        private void QuickMenuTickWatchFreeze(Transform t)
        {
            if (m_WatchWorldLocked || !m_WatchCfgFreeze || m_WatchOtherHand == null || m_WatchHand == null)
            {
                m_WatchFrozen = false;
                return;
            }

            // Measure against the live wrist anchor, not the transform: once frozen the transform
            // stops tracking, so it is a stale reference for its own hysteresis.
            Vector3 anchor = QuickMenuWatchWristAnchorWorld();
            float d2 = (m_WatchOtherHand.position - anchor).sqrMagnitude;
            if (m_WatchFrozen)
            {
                // Release when the reaching hand leaves, and also when the wrist itself has walked
                // away — otherwise dropping the watch arm leaves the face hanging in mid-air,
                // indistinguishable from pin mode.
                if (d2 > QuickMenuWatchFreezeExitSqr ||
                    (anchor - t.position).sqrMagnitude > QuickMenuWatchFreezeBreakSqr)
                    m_WatchFrozen = false;
                return;
            }
            if (d2 <= QuickMenuWatchFreezeEnterSqr) m_WatchFrozen = true;
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
            Transform left = m_WatchHandLeft;
            Transform right = m_WatchHandRight;
            if (left == null && right == null) GetVamVrHandTransforms(sc, out left, out right);

            bool currentlyLeft = QuickMenuIsLeftWatchHand(sc, m_WatchHand);
            if (m_WatchHand == null)
                currentlyLeft = m_WatchCfgHand == QuickMenuVrWatchMode.LeftOnly;

            bool wantLeft = !currentlyLeft;
            m_WatchSessionHand = wantLeft ? (byte)1 : (byte)2;
            m_WatchLatchedHand = wantLeft ? left : right;
            m_WatchLatchPending = false;
            m_WatchStatusNeedRebuild = true;
            m_WatchFrozen = false;

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

        // Hover text drives the tip panel only; it never feeds the status line, so no status
        // rebuild (and no StringBuilder.ToString) is owed per pointer enter/exit.
        internal int QuickMenuSetWatchHoverStatus(string msg)
        {
            m_WatchHoverStatus = msg;
            unchecked { m_WatchHoverToken++; }
            if (m_WatchHoverToken == 0) m_WatchHoverToken = 1;
            return m_WatchHoverToken;
        }

        internal void QuickMenuClearWatchHoverStatus(int token)
        {
            if (token != 0 && token != m_WatchHoverToken) return;
            m_WatchHoverStatus = null;
        }

        internal void QuickMenuBeginWatchCalibrate()
        {
            m_WatchCalibrating = true;
            m_WatchFrozen = false;
        }

        internal void QuickMenuDragWatchOffset(Vector3 worldDelta)
        {
            if (m_WatchWorldLocked)
            {
                if (m_WatchTf != null) m_WatchTf.position += worldDelta;
                return;
            }
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
            if (m_WatchWorldLocked) return;
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
                if (sc != null)
                {
                    if (sc.isOpenVR && sc.ViveCenterCamera != null)
                        return sc.ViveCenterCamera.transform;
                    if (sc.isOVR && sc.OVRCenterCamera != null)
                        return sc.OVRCenterCamera.transform;
                    if (sc.centerCameraTarget != null) return sc.centerCameraTarget.transform;
                }
            }
            catch { }
            try
            {
                if (Camera.main != null) return Camera.main.transform;
            }
            catch { }
            return null;
        }

        private static Camera QuickMenuGetPlayerCameraComponent()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc != null)
                {
                    if (sc.isOpenVR && sc.ViveCenterCamera != null) return sc.ViveCenterCamera;
                    if (sc.isOVR && sc.OVRCenterCamera != null) return sc.OVRCenterCamera;
                }
            }
            catch { }
            try
            {
                if (Camera.main != null) return Camera.main;
            }
            catch { }
            try
            {
                var sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null)
                {
                    Camera c = sc.centerCameraTarget.GetComponent<Camera>();
                    if (c != null) return c;
                }
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

    /// <summary>
    /// Bezel reposition drag.
    ///
    /// Measuring against the live bezel rect made this feel like a bow string: on the wrist the
    /// face re-aims at the user every frame, so the reference plane rotated under the drag and fed
    /// its own motion back in; and because a laser drag is angular, the further out and the more
    /// oblique the ray, the more world travel a small wrist rotation produced — including along the
    /// face normal, which is why the watch kept changing distance.
    ///
    /// Fix is three parts: freeze the reference plane at grab time, keep every step in that plane
    /// so the distance you set stays set, and gear the motion down with smoothing and a per-frame
    /// cap so it can be nudged instead of flung.
    /// </summary>
    internal sealed class VrWatchBezelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float DragGain = 0.3f;
        private const float DragSmoothing = 0.3f;
        private const float DragDeadZoneSqr = 1e-8f;
        private const float DragMaxStepM = 0.04f;

        public VamHookPlugin owner;
        private Vector3 m_LastWorld;
        private Vector3 m_Smoothed;
        private bool m_Active;
        private Camera m_Cam;
        private Plane m_Plane;
        private Vector3 m_PlaneNormal;
        private bool m_HasPlane;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner == null || eventData == null) return;
            if (!UIFloatPanelDrag.AcceptPointerButton(eventData)) return;
            RectTransform rt = transform as RectTransform;
            if (rt == null) return;

            m_Cam = eventData.pressEventCamera;
            if (m_Cam == null) m_Cam = eventData.enterEventCamera;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    rt, eventData.position, m_Cam, out m_LastWorld))
                return;

            m_PlaneNormal = rt.forward;
            m_Plane = new Plane(m_PlaneNormal, m_LastWorld);
            m_HasPlane = m_Cam != null;
            m_Smoothed = Vector3.zero;
            m_Active = true;
            owner.QuickMenuBeginWatchCalibrate();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!m_Active || owner == null || eventData == null) return;

            Vector3 world;
            if (!TryGetDragWorld(eventData, out world)) return;

            Vector3 raw = world - m_LastWorld;
            m_LastWorld = world;

            // Belt and braces: plane hits are already in-plane, but the no-camera fallback is not.
            raw -= m_PlaneNormal * Vector3.Dot(raw, m_PlaneNormal);
            if (raw.sqrMagnitude < DragDeadZoneSqr) return;

            m_Smoothed = Vector3.Lerp(m_Smoothed, raw, DragSmoothing);
            Vector3 step = m_Smoothed * DragGain;
            float m2 = step.sqrMagnitude;
            if (m2 > DragMaxStepM * DragMaxStepM) step *= DragMaxStepM / Mathf.Sqrt(m2);
            owner.QuickMenuDragWatchOffset(step);
        }

        /// <summary>Pointer ray against the frozen grab plane; the live rect only as a last resort.</summary>
        private bool TryGetDragWorld(PointerEventData eventData, out Vector3 world)
        {
            world = Vector3.zero;
            if (m_HasPlane && m_Cam != null)
            {
                Ray ray = m_Cam.ScreenPointToRay(eventData.position);
                float enter;
                // Ray swung behind the plane: hold position rather than let the hit jump.
                if (!m_Plane.Raycast(ray, out enter) || enter <= 0f) return false;
                world = ray.GetPoint(enter);
                return true;
            }
            RectTransform rt = transform as RectTransform;
            if (rt == null) return false;
            return RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rt, eventData.position, m_Cam, out world);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!m_Active) return;
            m_Active = false;
            m_HasPlane = false;
            m_Smoothed = Vector3.zero;
            if (owner != null) owner.QuickMenuEndWatchCalibrate();
        }

        private void OnDisable()
        {
            if (m_Active && owner != null) owner.QuickMenuEndWatchCalibrate();
            m_Active = false;
            m_HasPlane = false;
            m_Smoothed = Vector3.zero;
        }
    }

    internal sealed class VrWatchSlotHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public VamHookPlugin owner;
        public int hudSlotIdx;
        private int m_Token;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null) return;
            m_Token = owner.QuickMenuSetWatchHoverStatus(owner.QuickMenuWatchHoverLabel(hudSlotIdx));
            owner.QuickMenuBeginWatchHudRandomPreview(hudSlotIdx);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuWatchEndPress();
            owner.QuickMenuEndRandomPreview();
            if (m_Token == 0) return;
            owner.QuickMenuClearWatchHoverStatus(m_Token);
            m_Token = 0;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuWatchBeginPress(0, hudSlotIdx);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (owner != null) owner.QuickMenuWatchEndPress();
        }

        private void OnDisable()
        {
            if (owner != null)
            {
                owner.QuickMenuEndRandomPreview();
                if (m_Token != 0) owner.QuickMenuClearWatchHoverStatus(m_Token);
            }
            m_Token = 0;
        }
    }

    internal sealed class VrWatchAssignHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public VamHookPlugin owner;
        public int slotIdx;
        private int m_Token;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null) return;
            m_Token = owner.QuickMenuSetWatchHoverStatus(owner.QuickMenuWatchAssignHoverLabel(slotIdx));
            owner.QuickMenuBeginWatchAssignRandomPreview(slotIdx);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuWatchEndPress();
            owner.QuickMenuEndRandomPreview();
            if (m_Token == 0) return;
            owner.QuickMenuClearWatchHoverStatus(m_Token);
            m_Token = 0;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (owner == null) return;
            owner.QuickMenuWatchBeginPress(1, slotIdx);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (owner != null) owner.QuickMenuWatchEndPress();
        }

        private void OnDisable()
        {
            if (owner != null)
            {
                owner.QuickMenuEndRandomPreview();
                if (m_Token != 0) owner.QuickMenuClearWatchHoverStatus(m_Token);
            }
            m_Token = 0;
        }
    }

    internal sealed class VrWatchHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public VamHookPlugin owner;
        public string tip;
        private int m_Token;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null) return;
            if (string.IsNullOrEmpty(tip)) return;
            m_Token = owner.QuickMenuSetWatchHoverStatus(tip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Token 0 means we never showed a tip (empty text, or enter went elsewhere); passing
            // it on would force-clear whichever widget owns the tip right now.
            if (owner == null || m_Token == 0) return;
            owner.QuickMenuClearWatchHoverStatus(m_Token);
            m_Token = 0;
        }

        private void OnDisable()
        {
            if (owner != null && m_Token != 0) owner.QuickMenuClearWatchHoverStatus(m_Token);
            m_Token = 0;
        }
    }
}
