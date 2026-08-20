using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VPB.src.util;

namespace VPB
{
    public partial class VamHookPlugin
    {
        // Layout constants (must match CreateQuickMenuButton grid construction).
        private const float QuickMenuAnchorOldButtonW = 100f;
        private const float QuickMenuAnchorOldButtonH = 40f;
        private const float QuickMenuGridButtonSize = 44f;
        private const float QuickMenuGridGap = 6f;
        private const float QuickMenuGridCell = QuickMenuGridButtonSize + QuickMenuGridGap;
        internal static readonly Vector2 QuickMenuAnchorBaseline = new Vector2(-515f, -12f); // shown as (0,0) in the position UI
        private static readonly Vector2 QuickMenuPopupOffset = new Vector2(260f, -20f);

        private static readonly Color QmBackdropAssignedTransparent = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color QmBackdropAssignedHoverTransparent = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color QmBackdropEmptyTransparent = new Color(0f, 0f, 0f, 0f);
        private static readonly Color QmBackdropEmptyHoverTransparent = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color QmBackdropAssignedOpaque = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color QmBackdropAssignedHoverOpaque = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color QmBackdropEmptyOpaque = new Color(0.10f, 0.10f, 0.10f, 1f);
        private static readonly Color QmBackdropEmptyHoverOpaque = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color QmBackdropEditOnOpaque = new Color(0.10f, 0.55f, 0.18f, 1f);
        private static readonly Color QmBackdropEditOnHoverOpaque = new Color(0.12f, 0.70f, 0.22f, 1f);

        internal void SyncQuickMenuElementCornerRadiusLive()
        {
            float frac = UI.ResolveGalleryElementCornerRadiusFraction();
            if (m_QuickMenuGridBackdropImages != null)
            {
                for (int i = 0; i < m_QuickMenuGridBackdropImages.Length; i++)
                {
                    RoundedRect rr = m_QuickMenuGridBackdropImages[i] as RoundedRect;
                    if (rr != null) rr.cornerRadiusFraction = frac;
                }
            }
            SyncQuickMenuPopupRoundedBg(m_QuickMenuSavePopupRoot, frac);
            RoundedRect tooltipRounded = m_QmTooltipBackdrop as RoundedRect;
            if (tooltipRounded != null) tooltipRounded.cornerRadiusFraction = frac;
            QuickMenuSyncRandomPreviewCornerRadius(frac);
            try { QuickMenuSyncWatchCornerRadius(frac); } catch { }
            if (m_QuickMenuGridButtons != null)
            {
                for (int i = 0; i < m_QuickMenuGridButtons.Length; i++)
                {
                    GameObject go = m_QuickMenuGridButtons[i];
                    if (go == null) continue;
                    UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
                    if (hb != null)
                    {
                        try { hb.ApplyBorderSettings(); } catch { }
                    }
                }
            }
        }

        private static void SyncQuickMenuPopupRoundedBg(GameObject root, float frac)
        {
            if (root == null) return;
            RoundedRect rr = root.GetComponent<RoundedRect>();
            if (rr != null) rr.cornerRadiusFraction = frac;
        }

        private static Image AddQuickMenuRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private static readonly Color QmBackdropEditOffOpaque = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color QmBackdropEditOffHoverOpaque = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color QmTooltipBackdropTransparent = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color QmTooltipBackdropOpaque = new Color(0.12f, 0.12f, 0.12f, 1f);

        private enum QuickMenuAssignableAction
        {
            None = 0,
            CreateGallery,
            ShowHide,
            BringFront,
            CloseAll,
            Save,
            Random,
            RandomScenes,
            RandomSubScenes,
            RandomClothing,
            RandomHair,
            RandomPose,
            RandomAppearance,
            RandomSkin,
            RandomSceneImport,
            Undo,
            Redo,
            Hub,
            Cleanup,
            CreatorMode,
            ReplaceAddToggle,
            CompressCache,
            AutoHideGallery,
            LayoutPresets,
            ShowHiddenPackages,
            FpsCounter,
            OpenCategoryScenes,
            OpenCategorySubScenes,
            OpenCategoryClothing,
            OpenCategoryHair,
            OpenCategoryPose,
            OpenCategoryAppearance,
            OpenCategoryPlugins,
            OpenCategoryAll,
            CoreSettingsButton,
            CorePageButton,
            TargetAtom,
            PageNext,
            PagePrev,
            SwitchWatchHand,
            History,
            PerfMode,
            RemoveAllClothing,
            RemoveAllHair,
            ToggleImportSidebar,
            StarFilter,
            OpenCategorySkin,
            PerfStepUp,
            PerfStepDown,
        }

        private const int QuickMenuGridCols = 4;
        private const int QuickMenuGridRows = 4; // 16 slots total (4 rows × 4 cols)
        private const int QuickMenuGridSlotCount = QuickMenuGridCols * QuickMenuGridRows;
        private const int QuickMenuPageCount = 10; // pages 0..9

        private GameObject[] m_QuickMenuGridButtons;
        private Button[] m_QuickMenuGridUnityButtons;
        private Image[] m_QuickMenuGridBackdropImages;
        private RectTransform[] m_QuickMenuGridButtonRTs;
        private QuickMenuAssignableAction[][] m_QuickMenuPageAssignments; // [page][slot]
        private int m_QuickMenuCurrentPage;
        private int m_QuickMenuPageToggleSlotIdx = 15; // slot 16 (1-based)

        private GameObject m_QuickMenuAssignPopupRoot;
        private RectTransform m_QuickMenuAssignPopupRT;
        private readonly List<GameObject> m_QuickMenuAssignPopupButtons = new List<GameObject>(64);
        private GameObject m_QuickMenuSavePopupRoot;
        private RectTransform m_QuickMenuSavePopupRT;
        private readonly List<GameObject> m_QuickMenuSavePopupButtons = new List<GameObject>(16);
        private bool m_QmAssignDragActive;
        private QuickMenuAssignableAction m_QmAssignDragAction = QuickMenuAssignableAction.None;
        private GameObject m_QmAssignDragGhostGO;
        private RectTransform m_QmAssignDragGhostRT;
        private Image m_QmAssignDragGhostImage;
        private int m_QuickMenuAssignPopupTargetIdx = -1;
        private bool m_QuickMenuEditMode;
        private int m_QuickMenuEditSlotIdx = -1;
        private int m_QuickMenuSavePopupTargetIdx = -1;
        private GalleryPanel m_QuickMenuSavePopupPanel;

        private RectTransform m_QmTooltipRT;
        private Text m_QmTooltipText;
        private Image m_QmTooltipBackdrop;
        private string m_QmTooltipCurrent;
        private int m_QmTooltipHoverSlotIdx = -1;
        // Same principle as gallery sticky tips: instant show, hide grace only (no fade).
        private bool m_QmTooltipHidePending;
        private float m_QmTooltipHideAt;

        private float m_QmFpsLastLabelUpdateTime = -999f;
        private string m_QmFpsCachedLabel = "";
        private string[] m_QmLastAppliedSlotLabels;

        private void QuickMenuEnsureSlotLabelCache()
        {
            if (m_QmLastAppliedSlotLabels == null || m_QmLastAppliedSlotLabels.Length != QuickMenuGridSlotCount)
                m_QmLastAppliedSlotLabels = new string[QuickMenuGridSlotCount];
        }

        private Sprite m_QmIconCreate;
        private Sprite m_QmIconEyeOn;
        private Sprite m_QmIconEyeOff;
        private Sprite m_QmIconBringFront;
        private Sprite m_QmIconCloseAll;
        private Sprite m_QmIconEditPlus;
        private Sprite m_QmIconEditOff;
        private Sprite m_QmIconAssignEmpty;
        private Sprite m_QmIconSave;
        private Sprite m_QmIconRandom;
        private Sprite m_QmIconUndo;
        private Sprite m_QmIconRedo;
        private Sprite m_QmIconHub;
        private Sprite m_QmIconCleanup;
        private Sprite m_QmIconLayoutPresets;
        private Sprite m_QmIconReplace;
        private Sprite m_QmIconAdd;
        private Sprite m_QmIconCompressCache;
        private Sprite m_QmIconAutoHideOn;
        private Sprite m_QmIconAutoHideOff;
        private Sprite m_QmIconShowHiddenOn;
        private Sprite m_QmIconShowHiddenOff;
        private Sprite m_QmIconOpenCategory;
        private Sprite m_QmIconCategoryScenes;
        private Sprite m_QmIconCategorySubScenes;
        private Sprite m_QmIconCategoryClothing;
        private Sprite m_QmIconCategoryHair;
        private Sprite m_QmIconCategoryPose;
        private Sprite m_QmIconCategoryAppearance;
        private Sprite m_QmIconCategoryPlugins;
        private Sprite m_QmIconCategoryAll;
        private Sprite[] m_QmIconPages; // 10 icons: page_0..page_9
        private Sprite[] m_QmIconPerfLevels; // 10 icons: level_0..level_9

        private Sprite m_QmIconHexAppearance;
        private Sprite m_QmIconHexPose;
        private Sprite m_QmIconHexScene;
        private Sprite m_QmIconHexSkin;
        private Sprite m_QmIconHexSubScene;
        private Sprite m_QmIconHexHair;
        private Sprite m_QmIconHexClothing;
        private Sprite m_QmIconTargetAtom;
        private Sprite m_QmIconNavNext;
        private Sprite m_QmIconNavPrev;
        private Sprite m_QmIconSwitchHand;
        private Sprite m_QmIconHistory;
        private Sprite m_QmIconPerfModeOn;
        private Sprite m_QmIconPerfModeOff;
        private Sprite m_QmIconRemoveClothing;
        private Sprite m_QmIconRemoveHair;
        private Sprite m_QmIconImportSidebar;
        private Sprite m_QmIconStarOn;
        private Sprite m_QmIconStarOff;
        private Sprite m_QmIconCategorySkin;
        private Sprite m_QmIconPerfStepUp;
        private Sprite m_QmIconPerfStepDown;

        private Vector2 m_QmLastAnchorCenter = new Vector2(float.NaN, float.NaN);
        private bool m_QmLastAnchorIsVR = false;

        private static bool QuickMenuIsVrActive()
        {
            return XrUtils.IsVrActive();
        }

        private Vector2 QuickMenuGetAnchorCenter(bool isVR)
        {
            try
            {
                return isVR ? Settings.Instance.QuickMenuCreateGalleryPosVR.Value
                            : Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value;
            }
            catch
            {
                return Vector2.zero;
            }
        }

        private void QuickMenuApplyGridLayoutFromAnchor(Vector2 createCenter)
        {
            if (m_QuickMenuGridButtonRTs == null) return;

            // Root = top-left corner of the *old* Create Gallery button.
            Vector2 rootTopLeft = createCenter + new Vector2(-QuickMenuAnchorOldButtonW * 0.5f, QuickMenuAnchorOldButtonH * 0.5f);
            Vector2 slot0Center = new Vector2(rootTopLeft.x + (QuickMenuGridButtonSize * 0.5f),
                                              rootTopLeft.y - (QuickMenuGridButtonSize * 0.5f));

            int n = m_QuickMenuGridButtonRTs.Length;
            for (int i = 0; i < n; i++)
            {
                var rt = m_QuickMenuGridButtonRTs[i];
                if (rt == null) continue;
                int col = i % QuickMenuGridCols;
                int row = i / QuickMenuGridCols;
                rt.anchoredPosition = slot0Center + new Vector2(col * QuickMenuGridCell, -row * QuickMenuGridCell);
            }

            // Tooltip bar centered above first row
            if (m_QmTooltipRT != null)
            {
                float topEdge = slot0Center.y + QuickMenuGridButtonSize * 0.5f;
                float centerX = slot0Center.x + QuickMenuGridCell * 1.5f;
                m_QmTooltipRT.anchoredPosition = new Vector2(centerX, topEdge + 8f);
                var sd = m_QmTooltipRT.sizeDelta;
                m_QmTooltipRT.sizeDelta = new Vector2(QuickMenuGridCell * 4f - QuickMenuGridGap, sd.y);
                QuickMenuPositionDeskPreview();
            }
        }

        private void QuickMenuEnsureTooltipUI()
        {
            if (m_QuickMenuCanvas == null) return;
            if (m_QmTooltipRT != null && m_QmTooltipText != null && m_QmTooltipBackdrop != null) return;

            GameObject go = new GameObject("VPB_QM_TooltipBar");
            go.transform.SetParent(m_QuickMenuCanvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            // Grow upward for multi-line text to avoid overlap/clipping into quick-menu row.
            rt.pivot = new Vector2(0.5f, 0f);
            m_QmTooltipRT = rt;

            var img = AddQuickMenuRoundedBg(go, QuickMenuAssignablesForceOpaque() ? QmTooltipBackdropOpaque : QmTooltipBackdropTransparent, false);
            m_QmTooltipBackdrop = img;

            GameObject tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            var t = tgo.AddComponent<Text>();
            try { VPBUiFont.ApplyTo(t); } catch { }
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.fontSize = 18;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = "";
            t.raycastTarget = false;
            m_QmTooltipText = t;

            // Start fully hidden (no backdrop shown until we have a message).
            m_QmTooltipHidePending = false;
            QuickMenuUpdateTooltipVisual(forceHide: true);
        }

        private void QuickMenuUpdateTooltipVisual(bool forceHide = false)
        {
            if (m_QmTooltipRT == null || m_QmTooltipText == null) return;

            bool hasMsg = !forceHide && !string.IsNullOrEmpty(m_QmTooltipCurrent);
            if (!hasMsg)
            {
                if (m_QmTooltipBackdrop != null) m_QmTooltipBackdrop.enabled = false;
                m_QmTooltipText.enabled = false;

                var sd0 = m_QmTooltipRT.sizeDelta;
                m_QmTooltipRT.sizeDelta = new Vector2(sd0.x, 0f);
                QuickMenuPositionDeskPreview();
                return;
            }

            if (m_QmTooltipBackdrop != null)
            {
                m_QmTooltipBackdrop.enabled = true;
                m_QmTooltipBackdrop.color = QuickMenuAssignablesForceOpaque() ? QmTooltipBackdropOpaque : QmTooltipBackdropTransparent;
            }
            m_QmTooltipText.enabled = true;

            // Auto-size height to fit wrapped text.
            // Width is set by layout; height is derived from preferredHeight + padding.
            try
            {
                Canvas.ForceUpdateCanvases();
            }
            catch { }

            float prefH = 0f;
            try { prefH = m_QmTooltipText.preferredHeight; } catch { prefH = 0f; }
            const float padY = 14f;
            const float minH = 28f;
            float h = Mathf.Max(minH, prefH + padY);

            var sd = m_QmTooltipRT.sizeDelta;
            m_QmTooltipRT.sizeDelta = new Vector2(sd.x, h);
            QuickMenuPositionDeskPreview();
        }

        private void QuickMenuSetTooltip(string msg)
        {
            if (m_QmTooltipText == null) return;
            m_QmTooltipHidePending = false;
            string next = msg ?? "";
            // Skip layout rebuild when text unchanged (hide-grace cancel / same tip).
            if (m_QmTooltipCurrent == next) return;
            m_QmTooltipCurrent = next;
            m_QmTooltipText.text = m_QmTooltipCurrent;
            QuickMenuUpdateTooltipVisual();
        }

        private void QuickMenuClearTooltip(string expected)
        {
            if (m_QmTooltipText == null) return;
            if (expected != null && m_QmTooltipCurrent != expected) return;
            if (m_QuickMenuEditMode)
            {
                QuickMenuSetTooltip(VPBTranslation.T("hook.qmtooltip.drag_assign", "Edit mode: drag an action from the list and drop it on a slot to assign."));
                return;
            }
            if (string.IsNullOrEmpty(m_QmTooltipCurrent))
            {
                m_QmTooltipHidePending = false;
                return;
            }
            // Instant show stays; only clear is deferred (adjacent slot scrub).
            m_QmTooltipHidePending = true;
            m_QmTooltipHideAt = Time.unscaledTime + GalleryUiDesignTokens.TooltipHideGraceSec;
        }

        private void QuickMenuAdvanceTooltipHide()
        {
            if (!m_QmTooltipHidePending) return;
            if (Time.unscaledTime < m_QmTooltipHideAt) return;
            m_QmTooltipHidePending = false;
            if (m_QmTooltipText == null) return;
            m_QmTooltipCurrent = "";
            m_QmTooltipText.text = "";
            QuickMenuUpdateTooltipVisual(forceHide: true);
        }

        private string QuickMenuGetTooltipForSlot(int idx)
        {
            if (idx == m_QuickMenuPageToggleSlotIdx)
                return VPBTranslation.T("hook.qmtooltip.page_nav", "Next Page (Left Click), Previous Page (Right Click)");
            if (idx == m_QuickMenuEditSlotIdx)
                return m_QuickMenuEditMode
                    ? VPBTranslation.T("hook.qmtooltip.edit_off", "Disable editing")
                    : VPBTranslation.T("hook.qmtooltip.edit_on", "Enable editing");

            var a = QuickMenuGetSlotAction(idx);
            return QuickMenuGetActionTooltip(a, idx);
        }

        private string QuickMenuGetActionTooltip(QuickMenuAssignableAction a, int idx)
        {
            switch (a)
            {
                case QuickMenuAssignableAction.CreateGallery: return (idx >= 0 && idx <= 3) ? VPBTranslation.T("hook.qmtooltip.core_locked_create_gallery", "Create Gallery (core locked)") : VPBTranslation.T("hook.qmbutton.create_gallery", "Create Gallery");
                case QuickMenuAssignableAction.ShowHide: return (idx >= 0 && idx <= 3) ? VPBTranslation.T("hook.qmtooltip.core_locked_show_hide", "Show/Hide (core locked)") : VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide");
                case QuickMenuAssignableAction.BringFront: return (idx >= 0 && idx <= 3) ? VPBTranslation.T("hook.qmtooltip.core_locked_bring_front", "Bring Front (core locked)") : VPBTranslation.T("hook.qmbutton.bring_front", "Bring Front");
                case QuickMenuAssignableAction.CloseAll: return (idx >= 0 && idx <= 3) ? VPBTranslation.T("hook.qmtooltip.core_locked_close_all", "Close All (core locked)") : VPBTranslation.T("hook.qmbutton.close_all", "Close All");
                case QuickMenuAssignableAction.Save: return VPBTranslation.T("hook.qmtooltip.save_methods", "Save (choose method)");
                case QuickMenuAssignableAction.Random: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random", "Random"));
                case QuickMenuAssignableAction.RandomScenes: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_scenes", "Random: Scenes"));
                case QuickMenuAssignableAction.RandomSubScenes: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_subscenes", "Random: SubScenes"));
                case QuickMenuAssignableAction.RandomClothing: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_clothing_tip", "Left Click: Random Clothing\nRight Click: Remove All Clothing"));
                case QuickMenuAssignableAction.RandomHair: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_hair_tip", "Left Click: Random Hair\nRight Click: Remove All Hair"));
                case QuickMenuAssignableAction.RandomPose: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_pose", "Random: Pose"));
                case QuickMenuAssignableAction.RandomAppearance: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_appearance", "Random: Appearance"));
                case QuickMenuAssignableAction.RandomSkin: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_skin", "Random: Skin"));
                case QuickMenuAssignableAction.RandomSceneImport: return QuickMenuAppendRandomPreviewScrollTip(VPBTranslation.T("hook.qmbutton.random_scene_import", "Random: Scene Import"));
                case QuickMenuAssignableAction.Undo: return VPBTranslation.T("hook.qmbutton.undo", "Undo");
                case QuickMenuAssignableAction.Redo: return VPBTranslation.T("hook.qmbutton.redo", "Redo");
                case QuickMenuAssignableAction.Hub: return VPBTranslation.T("hook.qmbutton.hub", "Hub");
                case QuickMenuAssignableAction.Cleanup: return VPBTranslation.T("hook.qmbutton.cleanup", "Cleanup");
                case QuickMenuAssignableAction.CreatorMode: return VPBTranslation.T("hook.qmtooltip.creator_mode", "Scene Tools{hint:scene_tools} — sticky scene authoring tools. Esc exits.");
                case QuickMenuAssignableAction.ReplaceAddToggle: return VPBTranslation.T("hook.qmbutton.replace_add", "Replace/Add");
                case QuickMenuAssignableAction.CompressCache: return VPBTranslation.T("hook.qmbutton.compress_cache", "Compress Cache");
                case QuickMenuAssignableAction.AutoHideGallery: return VPBTranslation.T("hook.qmbutton.autohide", "Auto-Hide");
                case QuickMenuAssignableAction.LayoutPresets: return VPBTranslation.T("hook.qmbutton.layout_presets", "Layouts");
                case QuickMenuAssignableAction.ShowHiddenPackages: return VPBTranslation.T("hook.qmbutton.show_hidden", "Show Hidden");
                case QuickMenuAssignableAction.FpsCounter: return VPBTranslation.T("hook.qmbutton.fps", "FPS Counter");
                case QuickMenuAssignableAction.OpenCategoryScenes: return VPBTranslation.T("hook.qmbutton.open_category_scenes", "Open Category: Scenes");
                case QuickMenuAssignableAction.OpenCategorySubScenes: return VPBTranslation.T("hook.qmbutton.open_category_subscenes", "Open Category: SubScenes");
                case QuickMenuAssignableAction.OpenCategoryClothing: return VPBTranslation.T("hook.qmbutton.open_category_clothing", "Open Category: Clothing");
                case QuickMenuAssignableAction.OpenCategoryHair: return VPBTranslation.T("hook.qmbutton.open_category_hair", "Open Category: Hair");
                case QuickMenuAssignableAction.OpenCategoryPose: return VPBTranslation.T("hook.qmbutton.open_category_pose", "Open Category: Pose");
                case QuickMenuAssignableAction.OpenCategoryAppearance: return VPBTranslation.T("hook.qmbutton.open_category_appearance", "Open Category: Appearance");
                case QuickMenuAssignableAction.OpenCategoryPlugins: return VPBTranslation.T("hook.qmbutton.open_category_plugins", "Open Category: Plugins");
                case QuickMenuAssignableAction.OpenCategoryAll: return VPBTranslation.T("hook.qmbutton.open_category_all", "Open Category: All");
                case QuickMenuAssignableAction.CoreSettingsButton: return VPBTranslation.T("hook.qmbutton.core_settings", "Core: Settings");
                case QuickMenuAssignableAction.CorePageButton: return VPBTranslation.T("hook.qmbutton.core_page", "Core: Page");
                case QuickMenuAssignableAction.TargetAtom:
                {
                    string cur = QuickMenuGetCurrentTargetPersonLabel();
                    if (string.IsNullOrEmpty(cur)) cur = VPBTranslation.T("hook.qmtarget.none", "None");
                    return VPBTranslation.T("hook.qmbutton.target_atom_tip", "Target Atom\nLeft Click: cycle person\nDrag: point at person to target\n\nCurrent: ") + cur;
                }
                case QuickMenuAssignableAction.PageNext: return VPBTranslation.T("hook.qmbutton.page_next", "Page Forward");
                case QuickMenuAssignableAction.PagePrev: return VPBTranslation.T("hook.qmbutton.page_prev", "Page Backward");
                case QuickMenuAssignableAction.SwitchWatchHand: return VPBTranslation.T("hook.qmbutton.switch_watch_hand", "Switch Watch Hand");
                case QuickMenuAssignableAction.History: return VPBTranslation.T("hook.qmbutton.history", "History");
                case QuickMenuAssignableAction.PerfMode: return VPBTranslation.T("hook.qmbutton.perf_mode", "Perf Mode");
                case QuickMenuAssignableAction.RemoveAllClothing: return VPBTranslation.T("hook.qmbutton.remove_all_clothing", "Remove All Clothing");
                case QuickMenuAssignableAction.RemoveAllHair: return VPBTranslation.T("hook.qmbutton.remove_all_hair", "Remove All Hair");
                case QuickMenuAssignableAction.ToggleImportSidebar: return VPBTranslation.T("hook.qmbutton.toggle_import_sidebar", "Toggle Import Sidebar");
                case QuickMenuAssignableAction.StarFilter: return VPBTranslation.T("hook.qmbutton.star_filter", "Star Filter (Rated / Not rated)");
                case QuickMenuAssignableAction.OpenCategorySkin: return VPBTranslation.T("hook.qmbutton.open_category_skin", "Open Category: Skin");
                case QuickMenuAssignableAction.PerfStepUp: return VPBTranslation.T("hook.qmbutton.perf_step_up", "Perf Step Up");
                case QuickMenuAssignableAction.PerfStepDown: return VPBTranslation.T("hook.qmbutton.perf_step_down", "Perf Step Down");
                case QuickMenuAssignableAction.None:
                default:
                    if (idx >= 0 && idx <= 3)
                        return VPBTranslation.T("hook.qmtooltip.core_locked", "Core button (locked)");
                    return m_QuickMenuEditMode ? VPBTranslation.T("hook.qmtooltip.assign", "Assign button") : "";
            }
        }

        private static string QuickMenuAppendRandomPreviewScrollTip(string baseTip)
        {
            if (string.IsNullOrEmpty(baseTip)) return baseTip;
            if (!QuickMenuRandomPreviewEnabled()) return baseTip;
            return baseTip + "\n" + VPBTranslation.T("hook.qmtooltip.random_preview_scroll", "Scroll wheel: next preview");
        }

        private void QuickMenuMigrateAnchorBaselineOnce()
        {
            try
            {
                if (Settings.Instance == null || Settings.Instance.QuickMenuCreateGalleryAnchorBaselineMigrated == null)
                    return;

                if (Settings.Instance.QuickMenuCreateGalleryAnchorBaselineMigrated.Value)
                    return;

                // Force everyone to the new baseline anchor once.
                Settings.Instance.QuickMenuCreateGalleryPosDesktop.Value = QuickMenuAnchorBaseline;
                Settings.Instance.QuickMenuCreateGalleryPosVR.Value = QuickMenuAnchorBaseline;
                if (Settings.Instance.QuickMenuCreateGalleryUseSameInVR != null)
                    Settings.Instance.QuickMenuCreateGalleryUseSameInVR.Value = true;

                Settings.Instance.QuickMenuCreateGalleryAnchorBaselineMigrated.Value = true;
                Settings.SaveConfig();
            }
            catch { }
        }

        // Called from Update() to provide live anchor preview without recreating buttons.
        private void QuickMenuUpdateGridLayoutLive()
        {
            if (m_QuickMenuCanvas == null) return;
            if (m_QuickMenuGridButtonRTs == null || m_QuickMenuGridButtonRTs.Length == 0) return;

            bool isVR = QuickMenuIsVrActive();
            Vector2 center = QuickMenuGetAnchorCenter(isVR);

            // If the Quick Menu position window is open, use its preview values (not saved Settings yet).
            // Otherwise Update() would fight the preview and some slots would appear "stuck".
            try
            {
                var panel = GalleryPanel.GetAnchoredInstance();
                if (panel != null && panel.TryGetQuickMenuPositionEditorPreview(out float relX, out float relY))
                {
                    isVR = false;
                    center = QuickMenuAnchorBaseline + new Vector2(relX, relY);
                }
            }
            catch { }

            if (isVR == m_QmLastAnchorIsVR &&
                Mathf.Abs(center.x - m_QmLastAnchorCenter.x) < 0.01f &&
                Mathf.Abs(center.y - m_QmLastAnchorCenter.y) < 0.01f)
            {
                return;
            }

            QuickMenuApplyGridLayoutFromAnchor(center);
            m_QmLastAnchorCenter = center;
            m_QmLastAnchorIsVR = isVR;
        }

        private void EnsureQuickMenuGridArrays()
        {
            if (m_QuickMenuGridButtons == null || m_QuickMenuGridButtons.Length != QuickMenuGridSlotCount)
                m_QuickMenuGridButtons = new GameObject[QuickMenuGridSlotCount];
            if (m_QuickMenuGridUnityButtons == null || m_QuickMenuGridUnityButtons.Length != QuickMenuGridSlotCount)
                m_QuickMenuGridUnityButtons = new Button[QuickMenuGridSlotCount];
            if (m_QuickMenuGridBackdropImages == null || m_QuickMenuGridBackdropImages.Length != QuickMenuGridSlotCount)
                m_QuickMenuGridBackdropImages = new Image[QuickMenuGridSlotCount];
            if (m_QuickMenuGridButtonRTs == null || m_QuickMenuGridButtonRTs.Length != QuickMenuGridSlotCount)
                m_QuickMenuGridButtonRTs = new RectTransform[QuickMenuGridSlotCount];
            if (m_QuickMenuPageAssignments == null || m_QuickMenuPageAssignments.Length != QuickMenuPageCount)
                m_QuickMenuPageAssignments = new QuickMenuAssignableAction[QuickMenuPageCount][];
            for (int p = 0; p < QuickMenuPageCount; p++)
            {
                if (m_QuickMenuPageAssignments[p] == null || m_QuickMenuPageAssignments[p].Length != QuickMenuGridSlotCount)
                    m_QuickMenuPageAssignments[p] = new QuickMenuAssignableAction[QuickMenuGridSlotCount];
            }
        }

        private static string QuickMenuActionToId(QuickMenuAssignableAction a)
        {
            switch (a)
            {
                case QuickMenuAssignableAction.CreateGallery: return "create_gallery";
                case QuickMenuAssignableAction.ShowHide: return "show_hide";
                case QuickMenuAssignableAction.BringFront: return "bring_front";
                case QuickMenuAssignableAction.CloseAll: return "close_all";
                case QuickMenuAssignableAction.Save: return "save";
                case QuickMenuAssignableAction.Random: return "random";
                case QuickMenuAssignableAction.RandomScenes: return "random_scenes";
                case QuickMenuAssignableAction.RandomSubScenes: return "random_subscenes";
                case QuickMenuAssignableAction.RandomClothing: return "random_clothing";
                case QuickMenuAssignableAction.RandomHair: return "random_hair";
                case QuickMenuAssignableAction.RandomPose: return "random_pose";
                case QuickMenuAssignableAction.RandomAppearance: return "random_appearance";
                case QuickMenuAssignableAction.RandomSkin: return "random_skin";
                case QuickMenuAssignableAction.RandomSceneImport: return "random_scene_import";
                case QuickMenuAssignableAction.Undo: return "undo";
                case QuickMenuAssignableAction.Redo: return "redo";
                case QuickMenuAssignableAction.Hub: return "hub";
                case QuickMenuAssignableAction.Cleanup: return "cleanup";
                case QuickMenuAssignableAction.CreatorMode: return "creator_mode";
                case QuickMenuAssignableAction.ReplaceAddToggle: return "replace_add_toggle";
                case QuickMenuAssignableAction.CompressCache: return "compress_cache";
                case QuickMenuAssignableAction.AutoHideGallery: return "autohide_gallery";
                case QuickMenuAssignableAction.LayoutPresets: return "layout_presets";
                case QuickMenuAssignableAction.ShowHiddenPackages: return "show_hidden_packages";
                case QuickMenuAssignableAction.FpsCounter: return "fps_counter";
                case QuickMenuAssignableAction.OpenCategoryScenes: return "open_category_scenes";
                case QuickMenuAssignableAction.OpenCategorySubScenes: return "open_category_subscenes";
                case QuickMenuAssignableAction.OpenCategoryClothing: return "open_category_clothing";
                case QuickMenuAssignableAction.OpenCategoryHair: return "open_category_hair";
                case QuickMenuAssignableAction.OpenCategoryPose: return "open_category_pose";
                case QuickMenuAssignableAction.OpenCategoryAppearance: return "open_category_appearance";
                case QuickMenuAssignableAction.OpenCategoryPlugins: return "open_category_plugins";
                case QuickMenuAssignableAction.OpenCategoryAll: return "open_category_all";
                case QuickMenuAssignableAction.TargetAtom: return "target_atom";
                case QuickMenuAssignableAction.PageNext: return "page_next";
                case QuickMenuAssignableAction.PagePrev: return "page_prev";
                case QuickMenuAssignableAction.SwitchWatchHand: return "switch_watch_hand";
                case QuickMenuAssignableAction.History: return "history";
                case QuickMenuAssignableAction.PerfMode: return "perf_mode";
                case QuickMenuAssignableAction.RemoveAllClothing: return "remove_all_clothing";
                case QuickMenuAssignableAction.RemoveAllHair: return "remove_all_hair";
                case QuickMenuAssignableAction.ToggleImportSidebar: return "toggle_import_sidebar";
                case QuickMenuAssignableAction.StarFilter: return "star_filter";
                case QuickMenuAssignableAction.OpenCategorySkin: return "open_category_skin";
                case QuickMenuAssignableAction.PerfStepUp: return "perf_step_up";
                case QuickMenuAssignableAction.PerfStepDown: return "perf_step_down";
                case QuickMenuAssignableAction.None:
                default:
                    return "";
            }
        }

        private static QuickMenuAssignableAction QuickMenuIdToAction(string id)
        {
            if (string.IsNullOrEmpty(id)) return QuickMenuAssignableAction.None;
            string v = id.Trim().ToLowerInvariant();
            switch (v)
            {
                case "create_gallery": return QuickMenuAssignableAction.CreateGallery;
                case "show_hide": return QuickMenuAssignableAction.ShowHide;
                case "bring_front": return QuickMenuAssignableAction.BringFront;
                case "close_all": return QuickMenuAssignableAction.CloseAll;
                case "save": return QuickMenuAssignableAction.Save;
                case "random": return QuickMenuAssignableAction.Random;
                case "random_scenes": return QuickMenuAssignableAction.RandomScenes;
                case "random_subscenes": return QuickMenuAssignableAction.RandomSubScenes;
                case "random_clothing": return QuickMenuAssignableAction.RandomClothing;
                case "random_hair": return QuickMenuAssignableAction.RandomHair;
                case "random_pose": return QuickMenuAssignableAction.RandomPose;
                case "random_appearance": return QuickMenuAssignableAction.RandomAppearance;
                case "random_skin": return QuickMenuAssignableAction.RandomSkin;
                case "random_scene_import": return QuickMenuAssignableAction.RandomSceneImport;
                case "undo": return QuickMenuAssignableAction.Undo;
                case "redo": return QuickMenuAssignableAction.Redo;
                case "hub": return QuickMenuAssignableAction.Hub;
                case "cleanup": return QuickMenuAssignableAction.Cleanup;
                case "creator_mode":
                case "strip_scene": // legacy id from one-click Strip assignment
                    return QuickMenuAssignableAction.CreatorMode;
                case "replace_add_toggle": return QuickMenuAssignableAction.ReplaceAddToggle;
                case "compress_cache": return QuickMenuAssignableAction.CompressCache;
                case "autohide_gallery": return QuickMenuAssignableAction.AutoHideGallery;
                case "layout_presets": return QuickMenuAssignableAction.LayoutPresets;
                case "show_hidden_packages": return QuickMenuAssignableAction.ShowHiddenPackages;
                case "fps_counter": return QuickMenuAssignableAction.FpsCounter;
                case "open_category_scenes": return QuickMenuAssignableAction.OpenCategoryScenes;
                case "open_category_subscenes": return QuickMenuAssignableAction.OpenCategorySubScenes;
                case "open_category_clothing": return QuickMenuAssignableAction.OpenCategoryClothing;
                case "open_category_hair": return QuickMenuAssignableAction.OpenCategoryHair;
                case "open_category_pose": return QuickMenuAssignableAction.OpenCategoryPose;
                case "open_category_appearance": return QuickMenuAssignableAction.OpenCategoryAppearance;
                case "open_category_plugins": return QuickMenuAssignableAction.OpenCategoryPlugins;
                case "open_category_all": return QuickMenuAssignableAction.OpenCategoryAll;
                case "target_atom": return QuickMenuAssignableAction.TargetAtom;
                case "page_next": return QuickMenuAssignableAction.PageNext;
                case "page_prev": return QuickMenuAssignableAction.PagePrev;
                case "switch_watch_hand": return QuickMenuAssignableAction.SwitchWatchHand;
                case "history": return QuickMenuAssignableAction.History;
                case "perf_mode": return QuickMenuAssignableAction.PerfMode;
                case "remove_all_clothing": return QuickMenuAssignableAction.RemoveAllClothing;
                case "remove_all_hair": return QuickMenuAssignableAction.RemoveAllHair;
                case "toggle_import_sidebar": return QuickMenuAssignableAction.ToggleImportSidebar;
                case "star_filter": return QuickMenuAssignableAction.StarFilter;
                case "open_category_skin": return QuickMenuAssignableAction.OpenCategorySkin;
                case "perf_step_up": return QuickMenuAssignableAction.PerfStepUp;
                case "perf_step_down": return QuickMenuAssignableAction.PerfStepDown;
                default: return QuickMenuAssignableAction.None;
            }
        }

        /// <summary>Action this grid slot would run on click — None for the core edit / page slots, whose
        /// clicks never reach <see cref="QuickMenuExecuteAssignment"/>.</summary>
        private QuickMenuAssignableAction QuickMenuGridSlotPreviewAction(int slotIdx)
        {
            if (m_QuickMenuEditMode) return QuickMenuAssignableAction.None;
            if (slotIdx == m_QuickMenuEditSlotIdx || slotIdx == m_QuickMenuPageToggleSlotIdx)
                return QuickMenuAssignableAction.None;
            return QuickMenuGetSlotAction(slotIdx);
        }

        private QuickMenuAssignableAction QuickMenuGetSlotAction(int slotIdx)
        {
            EnsureQuickMenuGridArrays();
            if (slotIdx < 0 || slotIdx >= QuickMenuGridSlotCount) return QuickMenuAssignableAction.None;
            if (m_QuickMenuCurrentPage < 0) m_QuickMenuCurrentPage = 0;
            if (m_QuickMenuCurrentPage >= QuickMenuPageCount) m_QuickMenuCurrentPage = 0;
            // First row (1-4) is shared across all pages: always read from page 0.
            if (slotIdx >= 0 && slotIdx <= 3) return m_QuickMenuPageAssignments[0][slotIdx];
            return m_QuickMenuPageAssignments[m_QuickMenuCurrentPage][slotIdx];
        }

        private void QuickMenuSetSlotAction(int slotIdx, QuickMenuAssignableAction action)
        {
            EnsureQuickMenuGridArrays();
            if (slotIdx < 0 || slotIdx >= QuickMenuGridSlotCount) return;
            if (m_QuickMenuCurrentPage < 0) m_QuickMenuCurrentPage = 0;
            if (m_QuickMenuCurrentPage >= QuickMenuPageCount) m_QuickMenuCurrentPage = 0;
            // First row shared across all pages. Page-nav buttons are global too: once placed they
            // occupy that slot on every page, replacing whatever per-page action was there.
            bool global = (slotIdx >= 0 && slotIdx <= 3) ||
                          action == QuickMenuAssignableAction.PageNext ||
                          action == QuickMenuAssignableAction.PagePrev;
            if (global)
            {
                for (int p = 0; p < QuickMenuPageCount; p++)
                    m_QuickMenuPageAssignments[p][slotIdx] = action;
            }
            else
            {
                m_QuickMenuPageAssignments[m_QuickMenuCurrentPage][slotIdx] = action;
            }
            QuickMenuPersistAssignments();
            QuickMenuRefreshSlotVisual(slotIdx);
        }

        private void QuickMenuEnsureDefaultsAndLoadFromConfig()
        {
            EnsureQuickMenuGridArrays();
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;

            // Core/global button slots (settings + page) are persisted separately from per-page actions.
            m_QuickMenuEditSlotIdx = Mathf.Clamp(cfg.QuickMenuEditSlotIdx, 4, QuickMenuGridSlotCount - 1);
            m_QuickMenuPageToggleSlotIdx = Mathf.Clamp(cfg.QuickMenuPageToggleSlotIdx, 4, QuickMenuGridSlotCount - 1);
            if (m_QuickMenuPageToggleSlotIdx == m_QuickMenuEditSlotIdx)
            {
                m_QuickMenuPageToggleSlotIdx = 15;
                if (m_QuickMenuPageToggleSlotIdx == m_QuickMenuEditSlotIdx) m_QuickMenuPageToggleSlotIdx = 14;
            }

            bool hasValid = cfg.QuickMenuButtonsPages != null && cfg.QuickMenuButtonsPages.Length > 0;
            if (hasValid)
            {
                // Load into runtime pages
                int loadPages = Mathf.Min(cfg.QuickMenuButtonsPages.Length, QuickMenuPageCount);
                for (int p = 0; p < loadPages; p++)
                {
                    var src = cfg.QuickMenuButtonsPages[p] ?? new string[0];
                    for (int s = 0; s < QuickMenuGridSlotCount; s++)
                    {
                        string id = (s < src.Length) ? src[s] : "";
                        m_QuickMenuPageAssignments[p][s] = QuickMenuIdToAction(id);
                    }
                }
                for (int p = loadPages; p < QuickMenuPageCount; p++)
                    for (int s = 0; s < QuickMenuGridSlotCount; s++)
                        m_QuickMenuPageAssignments[p][s] = QuickMenuAssignableAction.None;

                m_QuickMenuCurrentPage = Mathf.Clamp(cfg.QuickMenuButtonsCurrentPage, 0, QuickMenuPageCount - 1);

                // Enforce shared first row from page 0 across all pages.
                for (int s = 0; s <= 3; s++)
                {
                    var a = m_QuickMenuPageAssignments[0][s];
                    for (int p = 1; p < QuickMenuPageCount; p++)
                        m_QuickMenuPageAssignments[p][s] = a;
                }
                return;
            }

            // First-time defaults: page 1 has 1-4 assigned L→R, settings on 13; slot16 is page toggle.
            for (int p = 0; p < QuickMenuPageCount; p++)
                for (int s = 0; s < QuickMenuGridSlotCount; s++)
                    m_QuickMenuPageAssignments[p][s] = QuickMenuAssignableAction.None;

            m_QuickMenuCurrentPage = 0;
            for (int p = 0; p < QuickMenuPageCount; p++)
            {
                m_QuickMenuPageAssignments[p][0] = QuickMenuAssignableAction.CreateGallery;
                m_QuickMenuPageAssignments[p][1] = QuickMenuAssignableAction.ShowHide;
                m_QuickMenuPageAssignments[p][2] = QuickMenuAssignableAction.BringFront;
                m_QuickMenuPageAssignments[p][3] = QuickMenuAssignableAction.CloseAll;
            }

            QuickMenuPersistAssignments(firstTime: true);
        }

        private void QuickMenuPersistAssignments(bool firstTime = false)
        {
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null) return;

                cfg.QuickMenuButtonsVersion = 1;
                cfg.QuickMenuButtonsCurrentPage = Mathf.Clamp(m_QuickMenuCurrentPage, 0, QuickMenuPageCount - 1);
                cfg.QuickMenuEditSlotIdx = Mathf.Clamp(m_QuickMenuEditSlotIdx, 4, QuickMenuGridSlotCount - 1);
                cfg.QuickMenuPageToggleSlotIdx = Mathf.Clamp(m_QuickMenuPageToggleSlotIdx, 4, QuickMenuGridSlotCount - 1);
                cfg.QuickMenuButtonsPages = new string[QuickMenuPageCount][];
                for (int p = 0; p < QuickMenuPageCount; p++)
                {
                    var arr = new string[QuickMenuGridSlotCount];
                    for (int s = 0; s < QuickMenuGridSlotCount; s++)
                        arr[s] = QuickMenuActionToId(m_QuickMenuPageAssignments[p][s]);
                    cfg.QuickMenuButtonsPages[p] = arr;
                }

                // Save without notifying listeners; this is UI-only state.
                cfg.Save(false, true);
            }
            catch { }
        }

        private void QuickMenuChangePage(int delta)
        {
            EnsureQuickMenuGridArrays();
            int p = m_QuickMenuCurrentPage;
            int n = QuickMenuPageCount;
            p = (p + delta) % n;
            if (p < 0) p += n;
            m_QuickMenuCurrentPage = p;
            QuickMenuPersistAssignments();
        }

        private void QuickMenuRefreshAllSlotVisuals()
        {
            for (int i = 0; i < QuickMenuGridSlotCount; i++) QuickMenuRefreshSlotVisual(i);
        }

        private class QuickMenuRightClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public System.Action onRightClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null) return;
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    try { if (onRightClick != null) onRightClick(); } catch { }
                }
            }
        }

        private class QuickMenuTooltipHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
        {
            public VamHookPlugin owner;
            public int slotIdx;
            private string _msg;
            private float _notchAccum;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (owner == null) return;
                try
                {
                    owner.m_QmTooltipHoverSlotIdx = slotIdx;
                    _msg = owner.QuickMenuGetTooltipForSlot(slotIdx);
                    if (!string.IsNullOrEmpty(_msg)) owner.QuickMenuSetTooltip(_msg);
                    owner.QuickMenuBeginRandomPreview(owner.QuickMenuGridSlotPreviewAction(slotIdx), false);
                }
                catch { }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (owner == null) return;
                try
                {
                    if (owner.m_QmTooltipHoverSlotIdx == slotIdx) owner.m_QmTooltipHoverSlotIdx = -1;
                    owner.QuickMenuClearTooltip(_msg);
                    owner.QuickMenuEndRandomPreview();
                }
                catch { }
            }

            public void OnScroll(PointerEventData eventData)
            {
                if (owner == null || eventData == null) return;
                int notches = VpbScrollTuning.TakeNotches(ref _notchAccum, eventData.scrollDelta.y);
                if (notches == 0) return;
                try
                {
                    QuickMenuAssignableAction act = owner.QuickMenuGridSlotPreviewAction(slotIdx);
                    string unused;
                    if (!QuickMenuTryGetRandomPreviewCategory(act, out unused)) return;
                    owner.QuickMenuBeginRandomPreview(act, false);
                }
                catch { }
            }

            // Deactivating the quick menu swallows OnPointerExit; a surviving pinned pick would then be
            // launched by the next click on that button without ever being previewed.
            private void OnDisable()
            {
                if (owner == null) return;
                try { owner.QuickMenuEndRandomPreview(); } catch { }
            }
        }

        private void QuickMenuSetAssignment(int idx, QuickMenuAssignableAction action)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return;
            if (idx >= 0 && idx <= 3) return;
            if (action == QuickMenuAssignableAction.CoreSettingsButton)
            {
                QuickMenuMoveCoreButtonSlot(isSettings: true, targetIdx: idx);
                return;
            }
            if (action == QuickMenuAssignableAction.CorePageButton)
            {
                QuickMenuMoveCoreButtonSlot(isSettings: false, targetIdx: idx);
                return;
            }
            EnsureQuickMenuGridArrays();
            QuickMenuSetSlotAction(idx, action);
        }

        private bool QuickMenuIsSlotAssignableTarget(int idx)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return false;
            if (idx >= 0 && idx <= 3) return false; // locked top row
            if (idx == m_QuickMenuEditSlotIdx) return false;
            if (idx == m_QuickMenuPageToggleSlotIdx) return false;
            return true;
        }

        private bool QuickMenuCanPlaceActionInSlot(QuickMenuAssignableAction action, int idx)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return false;
            if (idx >= 0 && idx <= 3) return false;

            if (action == QuickMenuAssignableAction.CoreSettingsButton)
                return idx != m_QuickMenuPageToggleSlotIdx;
            if (action == QuickMenuAssignableAction.CorePageButton)
                return idx != m_QuickMenuEditSlotIdx;

            return idx != m_QuickMenuEditSlotIdx && idx != m_QuickMenuPageToggleSlotIdx;
        }

        private bool QuickMenuTryAssignDraggedActionToSlot(int idx)
        {
            if (!m_QuickMenuEditMode) return false;
            if (!m_QmAssignDragActive) return false;
            if (!QuickMenuCanPlaceActionInSlot(m_QmAssignDragAction, idx)) return false;
            QuickMenuSetAssignment(idx, m_QmAssignDragAction);
            return true;
        }

        // Resolve the drop-target slot under the pointer and assign to it. Needed because IDropHandler
        // is not reliably invoked in VR; called from OnEndDrag while the drag is still active.
        private bool QuickMenuTryAssignDraggedActionAtPointer(PointerEventData eventData)
        {
            if (!m_QmAssignDragActive || eventData == null) return false;
            try
            {
                var es = EventSystem.current;
                if (es == null) return false;
                var results = new List<RaycastResult>();
                es.RaycastAll(eventData, results);
                for (int i = 0; i < results.Count; i++)
                {
                    var go = results[i].gameObject;
                    if (go == null) continue;
                    var h = go.GetComponentInParent<QuickMenuAssignDropTargetHandler>();
                    if (h != null && h.owner == this)
                        return QuickMenuTryAssignDraggedActionToSlot(h.slotIdx);
                }
            }
            catch { }
            return false;
        }

        private void QuickMenuMoveCoreButtonSlot(bool isSettings, int targetIdx)
        {
            if (!QuickMenuCanPlaceActionInSlot(isSettings ? QuickMenuAssignableAction.CoreSettingsButton : QuickMenuAssignableAction.CorePageButton, targetIdx))
                return;

            int oldIdx = isSettings ? m_QuickMenuEditSlotIdx : m_QuickMenuPageToggleSlotIdx;
            if (oldIdx == targetIdx) return;

            // Core buttons are unique/global and should free previous slot when moved.
            QuickMenuSetSlotAction(oldIdx, QuickMenuAssignableAction.None);
            QuickMenuSetSlotAction(targetIdx, QuickMenuAssignableAction.None);

            if (isSettings) m_QuickMenuEditSlotIdx = targetIdx;
            else m_QuickMenuPageToggleSlotIdx = targetIdx;

            QuickMenuPersistAssignments();
            QuickMenuRefreshSlotVisual(oldIdx);
            QuickMenuRefreshSlotVisual(targetIdx);
        }

        private void QuickMenuEnsureAssignDragGhost()
        {
            if (m_QmAssignDragGhostGO != null && m_QmAssignDragGhostRT != null && m_QmAssignDragGhostImage != null) return;
            if (m_QuickMenuCanvas == null) return;

            m_QmAssignDragGhostGO = new GameObject("VPB_QM_AssignDragGhost");
            m_QmAssignDragGhostGO.transform.SetParent(m_QuickMenuCanvas.transform, false);
            m_QmAssignDragGhostRT = m_QmAssignDragGhostGO.AddComponent<RectTransform>();
            m_QmAssignDragGhostRT.anchorMin = new Vector2(0.5f, 0.5f);
            m_QmAssignDragGhostRT.anchorMax = new Vector2(0.5f, 0.5f);
            m_QmAssignDragGhostRT.pivot = new Vector2(0.5f, 0.5f);
            m_QmAssignDragGhostRT.sizeDelta = new Vector2(34f, 34f);

            m_QmAssignDragGhostImage = m_QmAssignDragGhostGO.AddComponent<Image>();
            m_QmAssignDragGhostImage.color = new Color(1f, 1f, 1f, 0.5f);
            m_QmAssignDragGhostImage.preserveAspect = true;
            m_QmAssignDragGhostImage.raycastTarget = false;

            m_QmAssignDragGhostGO.SetActive(false);
        }

        private void QuickMenuBeginAssignDragGhost(QuickMenuAssignableAction action, PointerEventData eventData)
        {
            QuickMenuEnsureAssignDragGhost();
            if (m_QmAssignDragGhostGO == null || m_QmAssignDragGhostRT == null || m_QmAssignDragGhostImage == null) return;

            UI.SetIconSprite(m_QmAssignDragGhostImage, QuickMenuGetAssignPopupIcon(action));
            m_QmAssignDragGhostImage.color = new Color(1f, 1f, 1f, 0.5f);
            m_QmAssignDragGhostGO.SetActive(m_QmAssignDragGhostImage.sprite != null);
            QuickMenuSetAssignFloatRaycasts(false);
            QuickMenuUpdateAssignDragGhostPosition(eventData);
        }

        private void QuickMenuUpdateAssignDragGhostPosition(PointerEventData eventData)
        {
            if (m_QmAssignDragGhostGO == null || !m_QmAssignDragGhostGO.activeSelf || m_QmAssignDragGhostRT == null) return;
            if (eventData == null || m_QuickMenuCanvas == null) return;
            RectTransform canvasRT = m_QuickMenuCanvas.GetComponent<RectTransform>();
            if (canvasRT == null) return;

            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, eventData.position, m_QuickMenuCanvas.worldCamera, out local))
                m_QmAssignDragGhostRT.anchoredPosition = local;
        }

        private void QuickMenuEndAssignDragGhost()
        {
            QuickMenuSetAssignFloatRaycasts(true);
            if (m_QmAssignDragGhostGO != null) m_QmAssignDragGhostGO.SetActive(false);
        }

        private void QuickMenuSetDropTargetHighlight(int idx, bool on)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return;
            var bg = (m_QuickMenuGridBackdropImages != null && idx < m_QuickMenuGridBackdropImages.Length) ? m_QuickMenuGridBackdropImages[idx] : null;
            if (bg == null) return;
            if (on)
            {
                Color c = new Color(1f, 0.92f, 0.2f, 0.65f);
                bg.color = c;
                var hh = bg.GetComponent<QuickMenuSquareHover>();
                if (hh != null) { hh.normal = c; hh.hover = c; }
            }
            else
            {
                QuickMenuRefreshSlotVisual(idx);
            }
        }

        private void QuickMenuClearAllDropTargetHighlights()
        {
            for (int i = 0; i < QuickMenuGridSlotCount; i++) QuickMenuRefreshSlotVisual(i);
        }

        private static void QuickMenuSetIcon(GameObject buttonGO, Sprite icon, float padding)
        {
            if (buttonGO == null) return;

            // Remove any label; icon-only buttons should not retain stale text across pages.
            try
            {
                var labelTr = buttonGO.transform.Find("Label");
                if (labelTr != null) DestroyImmediate(labelTr.gameObject);
            }
            catch { }

            Transform existingIconTr = null;
            try { existingIconTr = buttonGO.transform.Find("Icon"); } catch { }

            if (icon == null)
            {
                if (existingIconTr != null)
                {
                    try { DestroyImmediate(existingIconTr.gameObject); } catch { }
                }
                return;
            }

            // Reuse path: same Image, same padding. Mutate only when actually different.
            if (existingIconTr != null)
            {
                Image existingImg = existingIconTr.GetComponent<Image>();
                RectTransform existingRT = existingIconTr.GetComponent<RectTransform>();
                if (existingImg != null && existingRT != null)
                {
                    if (existingImg.sprite != icon)
                    {
                        if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.QmIconSwap++;
                        UI.SetIconSprite(existingImg, icon);
                    }
                    Vector2 desiredSize = new Vector2(-padding * 2f, -padding * 2f);
                    if (existingRT.sizeDelta != desiredSize) existingRT.sizeDelta = desiredSize;
                    return;
                }
                // Component shape doesn't match; fall through to rebuild.
                try { DestroyImmediate(existingIconTr.gameObject); } catch { }
            }

            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.QmIconCreate++;

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = iconGO.AddComponent<Image>();
            UI.SetIconSprite(img, icon);
            img.preserveAspect = true;
            img.raycastTarget = false;

            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(-padding * 2f, -padding * 2f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void QuickMenuSetLabel(GameObject buttonGO, string text, bool clearIcon)
        {
            if (buttonGO == null) return;
            if (clearIcon)
            {
                try
                {
                    var iconTr = buttonGO.transform.Find("Icon");
                    if (iconTr != null) DestroyImmediate(iconTr.gameObject);
                }
                catch { }
            }
            var labelTr = buttonGO.transform.Find("Label");
            Text t = null;
            if (labelTr != null) t = labelTr.GetComponent<Text>();
            if (t == null)
            {
                GameObject labelGO = new GameObject("Label");
                labelGO.transform.SetParent(buttonGO.transform, false);
                t = labelGO.AddComponent<Text>();
                try { VPBUiFont.ApplyTo(t); } catch { }
                t.alignment = TextAnchor.MiddleCenter;
                t.color = Color.white;
                t.raycastTarget = false;
                var rt = labelGO.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
            }
            t.text = text ?? "";
            t.fontSize = 18;
        }

        internal void RefreshQuickMenuAssignableTransparency()
        {
            if (m_QuickMenuGridButtons == null) return;
            for (int i = 0; i < QuickMenuGridSlotCount; i++)
            {
                try { QuickMenuRefreshSlotVisual(i); } catch { }
            }
            try { QuickMenuUpdateTooltipVisual(); } catch { }
        }

        private static bool QuickMenuAssignablesForceOpaque()
        {
            try { return VPBConfig.Instance != null && VPBConfig.Instance.ShouldDisableGalleryAssignableButtonsTransparency(); }
            catch { return true; }
        }

        private static void QuickMenuApplyBackdropColors(Image bg, Color normal, Color hover)
        {
            if (bg == null) return;
            bg.color = normal;
            var hh = bg.GetComponent<QuickMenuSquareHover>();
            if (hh != null) { hh.normal = normal; hh.hover = hover; }
        }

        private void QuickMenuRefreshSlotVisual(int idx)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return;
            if (m_QuickMenuGridButtons == null) return;
            var go = m_QuickMenuGridButtons[idx];
            if (go == null) return;
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.QmRefresh++;
            QuickMenuEnsureSlotLabelCache();

            // Note: this quick-menu grid is built from scratch (no VaM prefab),
            // so we must NOT blanket-hide Text components here. Some slots (e.g. FPS) are text-only.

            // Backdrop rules:
            // - Assigned slots: permanent grey @ 50%
            // - Unassigned slots: fully transparent (no backdrop)
            // - Hover: highlight still shows (stronger for assigned, subtle for unassigned)
            bool isEditSlot = idx == m_QuickMenuEditSlotIdx;

            // Special-case: edit toggle slot is not assignable.
            if (isEditSlot)
            {
                // Settings slot reflects edit mode state.
                QuickMenuSetIcon(go, m_QuickMenuEditMode ? m_QmIconEditOff : m_QmIconEditPlus, padding: 6f);

                Image bg = (m_QuickMenuGridBackdropImages != null && idx < m_QuickMenuGridBackdropImages.Length) ? m_QuickMenuGridBackdropImages[idx] : null;
                if (bg != null)
                {
                    if (QuickMenuAssignablesForceOpaque())
                    {
                        Color normal = m_QuickMenuEditMode ? QmBackdropEditOnOpaque : QmBackdropEditOffOpaque;
                        Color hover = m_QuickMenuEditMode ? QmBackdropEditOnHoverOpaque : QmBackdropEditOffHoverOpaque;
                        QuickMenuApplyBackdropColors(bg, normal, hover);
                    }
                    else
                    {
                        Color normal = m_QuickMenuEditMode ? new Color(0.10f, 0.55f, 0.18f, 0.75f) : new Color(0f, 0f, 0f, 0.35f);
                        Color hover = m_QuickMenuEditMode ? new Color(0.12f, 0.70f, 0.22f, 0.90f) : new Color(0f, 0f, 0f, 0.55f);
                        QuickMenuApplyBackdropColors(bg, normal, hover);
                    }
                }
                return;
            }

            // Page toggle slot (slot 16): always assigned
            if (idx == m_QuickMenuPageToggleSlotIdx)
            {
                Sprite pageIcon = null;
                try
                {
                    int p = m_QuickMenuCurrentPage;
                    if (m_QmIconPages != null && p >= 0 && p < m_QmIconPages.Length) pageIcon = m_QmIconPages[p];
                }
                catch { }
                QuickMenuSetIcon(go, pageIcon, padding: 6f);

                Image bg = (m_QuickMenuGridBackdropImages != null && idx < m_QuickMenuGridBackdropImages.Length) ? m_QuickMenuGridBackdropImages[idx] : null;
                if (bg != null)
                {
                    if (QuickMenuAssignablesForceOpaque())
                        QuickMenuApplyBackdropColors(bg, QmBackdropAssignedOpaque, QmBackdropAssignedHoverOpaque);
                    else
                        QuickMenuApplyBackdropColors(bg, QmBackdropAssignedTransparent, QmBackdropAssignedHoverTransparent);
                }
                return;
            }

            var action = QuickMenuGetSlotAction(idx);

            Sprite icon = null;

            switch (action)
            {
                case QuickMenuAssignableAction.CreateGallery:
                    icon = m_QmIconCreate;
                    break;
                case QuickMenuAssignableAction.ShowHide:
                {
                    bool isVisible = false;
                    try { isVisible = (Gallery.singleton != null) && Gallery.singleton.IsVisible; } catch { }
                    icon = isVisible ? m_QmIconEyeOn : m_QmIconEyeOff;
                    break;
                }
                case QuickMenuAssignableAction.BringFront:
                    icon = m_QmIconBringFront;
                    break;
                case QuickMenuAssignableAction.CloseAll:
                    icon = m_QmIconCloseAll;
                    break;
                case QuickMenuAssignableAction.Save:
                    icon = m_QmIconSave;
                    break;
                case QuickMenuAssignableAction.Random:
                    icon = m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomScenes:
                    icon = m_QmIconHexScene ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomSubScenes:
                    icon = m_QmIconHexSubScene ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomClothing:
                    icon = m_QmIconHexClothing ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomHair:
                    icon = m_QmIconHexHair ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomPose:
                    icon = m_QmIconHexPose ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomAppearance:
                    icon = m_QmIconHexAppearance ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomSkin:
                    icon = m_QmIconHexSkin ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.RandomSceneImport:
                    icon = m_QmIconHexScene ?? m_QmIconRandom;
                    break;
                case QuickMenuAssignableAction.Undo:
                    icon = m_QmIconUndo;
                    break;
                case QuickMenuAssignableAction.Redo:
                    icon = m_QmIconRedo;
                    break;
                case QuickMenuAssignableAction.Hub:
                    icon = m_QmIconHub;
                    break;
                case QuickMenuAssignableAction.Cleanup:
                    icon = m_QmIconCleanup;
                    break;
                case QuickMenuAssignableAction.CreatorMode:
                    icon = m_QmIconHexClothing ?? m_QmIconHexScene ?? m_QmIconCleanup;
                    break;
                case QuickMenuAssignableAction.ReplaceAddToggle:
                {
                    bool replace = false;
                    try { replace = VPBConfig.Instance != null && VPBConfig.Instance.DragDropReplaceMode; } catch { }
                    icon = replace ? m_QmIconReplace : m_QmIconAdd;
                    break;
                }
                case QuickMenuAssignableAction.CompressCache:
                    icon = m_QmIconCompressCache;
                    break;
                case QuickMenuAssignableAction.AutoHideGallery:
                {
                    bool on = false;
                    try { on = VPBConfig.Instance != null && VPBConfig.Instance.DesktopFixedAutoCollapse; } catch { }
                    icon = on ? m_QmIconAutoHideOn : m_QmIconAutoHideOff;
                    break;
                }
                case QuickMenuAssignableAction.LayoutPresets:
                    icon = m_QmIconLayoutPresets;
                    break;
                case QuickMenuAssignableAction.ShowHiddenPackages:
                {
                    bool on = false;
                    try { on = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
                    icon = on ? m_QmIconShowHiddenOn : m_QmIconShowHiddenOff;
                    break;
                }
                case QuickMenuAssignableAction.FpsCounter:
                    // No icon by request; label will be used (live FPS text).
                    icon = null;
                    break;
                case QuickMenuAssignableAction.OpenCategoryScenes:
                    icon = m_QmIconCategoryScenes ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategorySubScenes:
                    icon = m_QmIconCategorySubScenes ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryClothing:
                    icon = m_QmIconCategoryClothing ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryHair:
                    icon = m_QmIconCategoryHair ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryPose:
                    icon = m_QmIconCategoryPose ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryAppearance:
                    icon = m_QmIconCategoryAppearance ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryPlugins:
                    icon = m_QmIconCategoryPlugins ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.OpenCategoryAll:
                    icon = m_QmIconCategoryAll ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.TargetAtom:
                    icon = m_QmIconTargetAtom;
                    break;
                case QuickMenuAssignableAction.PageNext:
                    icon = m_QmIconNavNext;
                    break;
                case QuickMenuAssignableAction.PagePrev:
                    icon = m_QmIconNavPrev;
                    break;
                case QuickMenuAssignableAction.SwitchWatchHand:
                    icon = m_QmIconSwitchHand;
                    break;
                case QuickMenuAssignableAction.History:
                    icon = m_QmIconHistory;
                    break;
                case QuickMenuAssignableAction.PerfMode:
                {
                    bool on = false;
                    try { on = VpbPerfController.IsPerfModeWanted; } catch { }
                    if (on)
                    {
                        int step = 0;
                        try { step = VpbPerfController.StepIndex; } catch { }
                        icon = QuickMenuGetPerfLevelIcon(step) ?? m_QmIconPerfModeOn;
                    }
                    else
                    {
                        icon = m_QmIconPerfModeOff;
                    }
                    break;
                }
                case QuickMenuAssignableAction.RemoveAllClothing:
                    icon = m_QmIconRemoveClothing;
                    break;
                case QuickMenuAssignableAction.RemoveAllHair:
                    icon = m_QmIconRemoveHair;
                    break;
                case QuickMenuAssignableAction.ToggleImportSidebar:
                {
                    bool on = false;
                    try { var p = QuickMenuGetTargetPanel(); on = p != null && p.IsImportSidebarActive; } catch { }
                    icon = on ? m_QmIconImportSidebar : (m_QmIconImportSidebar ?? m_QmIconAssignEmpty);
                    break;
                }
                case QuickMenuAssignableAction.StarFilter:
                {
                    bool on = false;
                    try { var p = QuickMenuGetTargetPanel(); on = p != null && p.QuickMenu_IsStarFilterEnabled(); } catch { }
                    icon = on ? m_QmIconStarOn : m_QmIconStarOff;
                    break;
                }
                case QuickMenuAssignableAction.OpenCategorySkin:
                    icon = m_QmIconCategorySkin ?? m_QmIconOpenCategory;
                    break;
                case QuickMenuAssignableAction.PerfStepUp:
                    icon = m_QmIconPerfStepUp;
                    break;
                case QuickMenuAssignableAction.PerfStepDown:
                    icon = m_QmIconPerfStepDown;
                    break;
                case QuickMenuAssignableAction.None:
                default:
                    if (m_QuickMenuEditMode) icon = m_QmIconAssignEmpty;
                    break;
            }

            if (action == QuickMenuAssignableAction.FpsCounter)
            {
                // Throttle text refresh to max 2 Hz; slot visuals may update more frequently.
                const float interval = 0.5f;
                float now = 0f;
                try { now = Time.unscaledTime; } catch { now = 0f; }

                if (string.IsNullOrEmpty(m_QmFpsCachedLabel) || (now - m_QmFpsLastLabelUpdateTime) >= interval)
                {
                    m_QmFpsLastLabelUpdateTime = now;

                    float fps = 0f;
                    try
                    {
                        float dt = Time.unscaledDeltaTime;
                        if (dt <= 0f) dt = Time.deltaTime;
                        fps = 1f / Mathf.Max(0.00001f, dt);
                    }
                    catch { fps = 0f; }

                    // Display as an integer 0..999 (no decimals), per quick-menu compact constraint.
                    if (fps > 999f) fps = 999f;
                    if (fps < 0f) fps = 0f;
                    m_QmFpsCachedLabel = ((int)(fps + 0.5f)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Only touch UI text if it actually changed (avoids per-frame layout dirties).
                string last = (m_QmLastAppliedSlotLabels != null) ? m_QmLastAppliedSlotLabels[idx] : null;
                if (last != m_QmFpsCachedLabel)
                {
                    if (m_QmLastAppliedSlotLabels != null) m_QmLastAppliedSlotLabels[idx] = m_QmFpsCachedLabel;
                    QuickMenuSetLabel(go, m_QmFpsCachedLabel, clearIcon: true);
                }
            }
            else
            {
                if (m_QmLastAppliedSlotLabels != null) m_QmLastAppliedSlotLabels[idx] = null;
                QuickMenuSetIcon(go, icon, padding: 6f);
            }

            bool isAssigned = action != QuickMenuAssignableAction.None;
            Image bgImg = (m_QuickMenuGridBackdropImages != null && idx < m_QuickMenuGridBackdropImages.Length) ? m_QuickMenuGridBackdropImages[idx] : null;
            if (bgImg != null)
            {
                Color normal;
                Color hover;
                if (QuickMenuAssignablesForceOpaque())
                {
                    normal = isAssigned ? QmBackdropAssignedOpaque : QmBackdropEmptyOpaque;
                    hover = isAssigned ? QmBackdropAssignedHoverOpaque : QmBackdropEmptyHoverOpaque;
                }
                else
                {
                    normal = isAssigned ? QmBackdropAssignedTransparent : QmBackdropEmptyTransparent;
                    hover = isAssigned ? QmBackdropAssignedHoverTransparent : QmBackdropEmptyHoverTransparent;
                }
                QuickMenuApplyBackdropColors(bgImg, normal, hover);
            }

            if (idx >= 0 && idx < QuickMenuWatchHudSlotCount)
                QuickMenuSyncWatchSlot(idx);
        }

        private void QuickMenuExecuteAssignment(QuickMenuAssignableAction action)
        {
            switch (action)
            {
                case QuickMenuAssignableAction.CreateGallery:
                    OpenCreateGallery();
                    break;
                case QuickMenuAssignableAction.ShowHide:
                    if (Gallery.singleton != null)
                    {
                        if (Gallery.singleton.IsVisible) Gallery.singleton.Hide();
                        else OpenGallery();
                    }
                    break;
                case QuickMenuAssignableAction.BringFront:
                    if (Gallery.singleton != null) Gallery.singleton.BringAllToFront();
                    break;
                case QuickMenuAssignableAction.CloseAll:
                    if (Gallery.singleton != null) Gallery.singleton.CloseAll();
                    break;
                case QuickMenuAssignableAction.Save:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null)
                    {
                        // Open save methods submenu (bottom-up) instead of executing directly.
                        int slotIdx = m_QuickMenuSavePopupTargetIdx;
                        Vector2 pos = Vector2.zero;
                        try
                        {
                            if (m_QuickMenuGridButtonRTs != null && slotIdx >= 0 &&
                                slotIdx < m_QuickMenuGridButtonRTs.Length &&
                                m_QuickMenuGridButtonRTs[slotIdx] != null)
                            {
                                pos = m_QuickMenuGridButtonRTs[slotIdx].anchoredPosition;
                            }
                        }
                        catch { }
                        // If we can't resolve the RT, fall back to current popup position.
                        if (pos == Vector2.zero && m_QuickMenuSavePopupRT != null) pos = m_QuickMenuSavePopupRT.anchoredPosition;
                        QuickMenuShowSavePopup(slotIdx, pos + QuickMenuPopupOffset, p);
                    }
                    break;
                }
                case QuickMenuAssignableAction.Random:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_LoadRandom(QuickMenuConsumeRandomPreviewPick(action));
                    break;
                }
                case QuickMenuAssignableAction.RandomScenes: QuickMenuExecuteRandomInCategory(action, "Scenes", preservePersonTarget: false, preserveUi: false); break;
                case QuickMenuAssignableAction.RandomSubScenes: QuickMenuExecuteRandomInCategory(action, "SubScenes", preservePersonTarget: false, preserveUi: false); break;
                case QuickMenuAssignableAction.RandomClothing: QuickMenuExecuteRandomInCategory(action, "Clothing", preservePersonTarget: true, preserveUi: true); break;
                case QuickMenuAssignableAction.RandomHair: QuickMenuExecuteRandomInCategory(action, "Hair", preservePersonTarget: true, preserveUi: true); break;
                case QuickMenuAssignableAction.RandomPose: QuickMenuExecuteRandomInCategory(action, "Pose", preservePersonTarget: true, preserveUi: true); break;
                case QuickMenuAssignableAction.RandomAppearance: QuickMenuExecuteRandomInCategory(action, "Appearance", preservePersonTarget: true, preserveUi: true); break;
                case QuickMenuAssignableAction.RandomSkin: QuickMenuExecuteRandomInCategory(action, "Skin", preservePersonTarget: true, preserveUi: true); break;
                case QuickMenuAssignableAction.RandomSceneImport:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_RandomSceneImport(QuickMenuConsumeRandomPreviewPick(action));
                    break;
                }
                case QuickMenuAssignableAction.Undo:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_Undo();
                    break;
                }
                case QuickMenuAssignableAction.Redo:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_Redo();
                    break;
                }
                case QuickMenuAssignableAction.Hub:
                    OpenHubBrowse();
                    break;
                case QuickMenuAssignableAction.Cleanup:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null)
                    {
                        p.QuickMenu_ToggleCleanupMode();
                    }
                    else
                    {
                        OpenGallery();
                        p = QuickMenuGetTargetPanel();
                        if (p != null) p.QuickMenu_ToggleCleanupMode();
                    }
                    break;
                }
                case QuickMenuAssignableAction.CreatorMode:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p == null) { OpenGallery(); p = QuickMenuGetTargetPanel(); }
                    if (p != null) p.QuickMenu_ToggleCreatorMode();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.CreatorMode)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.ReplaceAddToggle:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_ToggleReplaceMode();
                    // Visual may need update because icon depends on mode
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.ReplaceAddToggle)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.CompressCache:
                    QuickMenuOpenCompressCache();
                    break;
                case QuickMenuAssignableAction.AutoHideGallery:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_ToggleAutoHide();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.AutoHideGallery)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.LayoutPresets:
                    GalleryPanel.OpenLayoutPresetsFloatAnywhere();
                    break;
                case QuickMenuAssignableAction.ShowHiddenPackages:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_ToggleShowHiddenPackages();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.ShowHiddenPackages)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.FpsCounter:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_ToggleFpsCounter();
                    break;
                }
                case QuickMenuAssignableAction.OpenCategoryScenes: QuickMenuOpenGalleryCategory("Scenes"); break;
                case QuickMenuAssignableAction.OpenCategorySubScenes: QuickMenuOpenGalleryCategory("SubScenes"); break;
                case QuickMenuAssignableAction.OpenCategoryClothing: QuickMenuOpenGalleryCategory("Clothing"); break;
                case QuickMenuAssignableAction.OpenCategoryHair: QuickMenuOpenGalleryCategory("Hair"); break;
                case QuickMenuAssignableAction.OpenCategoryPose: QuickMenuOpenGalleryCategory("Pose"); break;
                case QuickMenuAssignableAction.OpenCategoryAppearance: QuickMenuOpenGalleryCategory("Appearance"); break;
                case QuickMenuAssignableAction.OpenCategoryPlugins: OpenCategoryPlugins(); break;
                case QuickMenuAssignableAction.OpenCategoryAll: QuickMenuOpenGalleryCategory("All"); break;
                case QuickMenuAssignableAction.TargetAtom:
                {
                    QuickMenuCyclePersonTarget(+1);
                    break;
                }
                case QuickMenuAssignableAction.PageNext:
                {
                    QuickMenuChangePage(+1);
                    QuickMenuRefreshAllSlotVisuals();
                    break;
                }
                case QuickMenuAssignableAction.PagePrev:
                {
                    QuickMenuChangePage(-1);
                    QuickMenuRefreshAllSlotVisuals();
                    break;
                }
                case QuickMenuAssignableAction.SwitchWatchHand:
                {
                    QuickMenuSwitchWatchHand();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.SwitchWatchHand)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.History:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p == null) { OpenGallery(); p = QuickMenuGetTargetPanel(); }
                    if (p != null) p.QuickMenu_OpenGalleryHistory();
                    break;
                }
                case QuickMenuAssignableAction.PerfMode:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_TogglePerfMode();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.PerfMode)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.RemoveAllClothing:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_RemoveAllClothing();
                    break;
                }
                case QuickMenuAssignableAction.RemoveAllHair:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_RemoveAllHair();
                    break;
                }
                case QuickMenuAssignableAction.ToggleImportSidebar:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p == null) { OpenGallery(); p = QuickMenuGetTargetPanel(); }
                    if (p != null) p.ToggleImportSidebar();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.ToggleImportSidebar)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.StarFilter:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_ToggleStarFilter();
                    for (int i = 0; i < QuickMenuGridSlotCount; i++)
                        if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.StarFilter)
                            QuickMenuRefreshSlotVisual(i);
                    break;
                }
                case QuickMenuAssignableAction.OpenCategorySkin:
                    QuickMenuOpenGalleryCategory("Skin");
                    break;
                case QuickMenuAssignableAction.PerfStepUp:
                    try { VpbPerfController.StepBy(+1, true, true); } catch { }
                    break;
                case QuickMenuAssignableAction.PerfStepDown:
                    try { VpbPerfController.StepBy(-1, true, true); } catch { }
                    break;
                case QuickMenuAssignableAction.CoreSettingsButton:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p == null) p = GalleryPanel.EnsureFloatHostPane();
                    if (p == null) { OpenGallery(); p = QuickMenuGetTargetPanel(); }
                    if (p != null) p.OpenSettingsSideTab();
                    break;
                }
                case QuickMenuAssignableAction.CorePageButton:
                    QuickMenuChangePage(+1);
                    QuickMenuRefreshAllSlotVisuals();
                    break;
                case QuickMenuAssignableAction.None:
                default:
                    break;
            }
        }

        private void QuickMenuExecuteAssignmentRightClick(QuickMenuAssignableAction action)
        {
            switch (action)
            {
                case QuickMenuAssignableAction.RandomHair:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_RemoveAllHair();
                    break;
                }
                case QuickMenuAssignableAction.RandomClothing:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_RemoveAllClothing();
                    break;
                }
                default:
                    break;
            }
        }

        private void QuickMenuOpenGalleryCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            // Plugins = float palette only — never switch gallery grid to Plugins.
            if (string.Equals(categoryName, "Plugins", StringComparison.OrdinalIgnoreCase))
            {
                OpenCategoryPlugins();
                return;
            }
            if (Gallery.singleton == null) return;

            try
            {
                if (!m_GalleryCatsInited) InitGalleryCategories();
                if (m_GalleryCategories != null)
                {
                    for (int i = 0; i < m_GalleryCategories.Count; i++)
                    {
                        var c = m_GalleryCategories[i];
                        if (!string.Equals(c.name, categoryName, System.StringComparison.OrdinalIgnoreCase)) continue;
                        Gallery.singleton.Show(c.name, c.extension, c.path);
                        return;
                    }
                }
            }
            catch { }
        }

        private void QuickMenuExecuteRandomInCategory(QuickMenuAssignableAction action, string categoryName, bool preservePersonTarget, bool preserveUi)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            var p = QuickMenuGetTargetPanel();
            if (p == null) return;
            // Hover preview pins the item the user saw; without one this falls back to a fresh draw.
            p.QuickMenu_LoadPickedFromCategory(categoryName, QuickMenuConsumeRandomPreviewPick(action),
                preserveUi: preserveUi, preserveTarget: preservePersonTarget);
        }

        private void QuickMenuOpenCompressCache()
        {
            try { GalleryPanel.GalleryTriggerBulkZstdCompression(); }
            catch { }
        }

        private GalleryPanel QuickMenuGetTargetPanel()
        {
            try
            {
                var p = GalleryPanel.GetAnchoredInstance();
                if (p != null) return p;
            }
            catch { }

            try
            {
                var g = Gallery.singleton;
                var list = (g != null) ? g.Panels : null;
                if (list == null || list.Count == 0) return null;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p != null && p.IsVisible) return p;
                }
                return list[0];
            }
            catch { return null; }
        }

        private void QuickMenuClampPopupToScreen()
        {
            try
            {
                if (m_QuickMenuAssignPopupRT == null) return;
                QuickMenuClampRectToScreen(m_QuickMenuAssignPopupRT);
            }
            catch { }
        }

        private void QuickMenuClampRectToScreen(RectTransform rt)
        {
            try
            {
                if (rt == null) return;
                RectTransform parent = rt.parent as RectTransform;
                if (parent == null) return;

                // Parent-local clamp — avoid Camera.main ScreenToWorldPoint (frustum spam on edges / VR).
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 local = parent.InverseTransformPoint(corners[i]);
                    if (local.x < minX) minX = local.x;
                    if (local.x > maxX) maxX = local.x;
                    if (local.y < minY) minY = local.y;
                    if (local.y > maxY) maxY = local.y;
                }

                const float pad = 6f;
                Rect pr = parent.rect;
                float dx = 0f;
                float dy = 0f;

                if (minX < pr.xMin + pad) dx = (pr.xMin + pad) - minX;
                else if (maxX > pr.xMax - pad) dx = (pr.xMax - pad) - maxX;

                if (minY < pr.yMin + pad) dy = (pr.yMin + pad) - minY;
                else if (maxY > pr.yMax - pad) dy = (pr.yMax - pad) - maxY;

                if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f) return;
                rt.anchoredPosition += new Vector2(dx, dy);
            }
            catch { }
        }

        private void QuickMenuBuildSavePopup(GameObject host)
        {
            if (host == null) return;
            m_QuickMenuSavePopupRoot = UI.AddChildGOImage(host, new Color(0f, 0f, 0f, 0.85f), AnchorPresets.topLeft, 300f, 260f, Vector2.zero, rounded: true);
            m_QuickMenuSavePopupRoot.name = "VPB_QM_SavePopup";
            m_QuickMenuSavePopupRT = m_QuickMenuSavePopupRoot.GetComponent<RectTransform>();
            if (m_QuickMenuSavePopupRT != null)
            {
                m_QuickMenuSavePopupRT.anchorMin = new Vector2(0.5f, 0.5f);
                m_QuickMenuSavePopupRT.anchorMax = new Vector2(0.5f, 0.5f);
                m_QuickMenuSavePopupRT.pivot = new Vector2(0f, 0f);
            }
            m_QuickMenuSavePopupRoot.SetActive(false);
        }

        private void QuickMenuShowSavePopup(int slotIdx, Vector2 anchoredPos, GalleryPanel panel)
        {
            if (m_QuickMenuSavePopupRoot == null || m_QuickMenuSavePopupRT == null) return;
            m_QuickMenuSavePopupTargetIdx = slotIdx;
            m_QuickMenuSavePopupPanel = panel;

            QuickMenuRebuildSavePopupButtons(panel);
            m_QuickMenuSavePopupRT.anchoredPosition = anchoredPos;
            if (!m_QuickMenuSavePopupRoot.activeSelf) m_QuickMenuSavePopupRoot.SetActive(true);
            try { m_QuickMenuSavePopupRoot.transform.SetAsLastSibling(); } catch { }
            QuickMenuClampRectToScreen(m_QuickMenuSavePopupRT);
        }

        private void QuickMenuHideSavePopup()
        {
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;
            if (m_QuickMenuSavePopupRoot != null && m_QuickMenuSavePopupRoot.activeSelf)
                m_QuickMenuSavePopupRoot.SetActive(false);
        }

        private static void QuickMenuAddLeftIconAndKeepLabel(GameObject buttonGO, Sprite icon)
        {
            if (buttonGO == null || icon == null) return;
            try
            {
                var old = buttonGO.transform.Find("Icon");
                if (old != null) DestroyImmediate(old.gameObject);
            }
            catch { }

            Text t = null;
            try { t = buttonGO.GetComponentInChildren<Text>(true); } catch { t = null; }
            if (t != null)
            {
                t.gameObject.SetActive(true);
                t.alignment = TextAnchor.MiddleLeft;
                var trt = t.GetComponent<RectTransform>();
                if (trt != null)
                {
                    trt.anchorMin = Vector2.zero;
                    trt.anchorMax = Vector2.one;
                    trt.sizeDelta = Vector2.zero;
                    trt.anchoredPosition = new Vector2(42f, 0f);
                }
            }

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = iconGO.AddComponent<Image>();
            UI.SetIconSprite(img, icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            RectTransform rt = iconGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(8f, 0f);
            rt.sizeDelta = new Vector2(24f, 24f);
        }

        private void QuickMenuAttachAssignDragSource(GameObject buttonGO, QuickMenuAssignableAction action)
        {
            if (buttonGO == null) return;
            var h = buttonGO.GetComponent<QuickMenuAssignDragSourceHandler>();
            if (h == null) h = buttonGO.AddComponent<QuickMenuAssignDragSourceHandler>();
            h.owner = this;
            h.action = action;
        }

        private Sprite QuickMenuGetAssignPopupIcon(QuickMenuAssignableAction action)
        {
            switch (action)
            {
                case QuickMenuAssignableAction.None: return m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.CreateGallery: return m_QmIconCreate;
                case QuickMenuAssignableAction.ShowHide: return m_QmIconEyeOn ?? m_QmIconEyeOff;
                case QuickMenuAssignableAction.BringFront: return m_QmIconBringFront;
                case QuickMenuAssignableAction.CloseAll: return m_QmIconCloseAll;
                case QuickMenuAssignableAction.Save: return m_QmIconSave;
                case QuickMenuAssignableAction.Random: return m_QmIconRandom;
                case QuickMenuAssignableAction.RandomScenes: return m_QmIconHexScene ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomSubScenes: return m_QmIconHexSubScene ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomClothing: return m_QmIconHexClothing ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomHair: return m_QmIconHexHair ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomPose: return m_QmIconHexPose ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomAppearance: return m_QmIconHexAppearance ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomSkin: return m_QmIconHexSkin ?? m_QmIconRandom;
                case QuickMenuAssignableAction.RandomSceneImport: return m_QmIconHexScene ?? m_QmIconRandom;
                case QuickMenuAssignableAction.Undo: return m_QmIconUndo;
                case QuickMenuAssignableAction.Redo: return m_QmIconRedo;
                case QuickMenuAssignableAction.Hub: return m_QmIconHub;
                case QuickMenuAssignableAction.Cleanup: return m_QmIconCleanup;
                case QuickMenuAssignableAction.CreatorMode: return m_QmIconHexClothing ?? m_QmIconHexScene ?? m_QmIconCleanup;
                case QuickMenuAssignableAction.ReplaceAddToggle: return m_QmIconReplace ?? m_QmIconAdd;
                case QuickMenuAssignableAction.CompressCache: return m_QmIconCompressCache;
                case QuickMenuAssignableAction.AutoHideGallery: return m_QmIconAutoHideOff ?? m_QmIconAutoHideOn;
                case QuickMenuAssignableAction.LayoutPresets: return m_QmIconLayoutPresets;
                case QuickMenuAssignableAction.ShowHiddenPackages: return m_QmIconShowHiddenOff ?? m_QmIconShowHiddenOn;
                case QuickMenuAssignableAction.FpsCounter: return m_QmIconPages != null && m_QmIconPages.Length > 0 ? m_QmIconPages[0] : m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.OpenCategoryScenes: return m_QmIconCategoryScenes ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategorySubScenes: return m_QmIconCategorySubScenes ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryClothing: return m_QmIconCategoryClothing ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryHair: return m_QmIconCategoryHair ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryPose: return m_QmIconCategoryPose ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryAppearance: return m_QmIconCategoryAppearance ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryPlugins: return m_QmIconCategoryPlugins ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.OpenCategoryAll: return m_QmIconCategoryAll ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.CoreSettingsButton: return m_QmIconEditPlus ?? m_QmIconEditOff;
                case QuickMenuAssignableAction.CorePageButton: return (m_QmIconPages != null && m_QmIconPages.Length > 0) ? m_QmIconPages[0] : m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.TargetAtom: return m_QmIconTargetAtom ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.PageNext: return m_QmIconNavNext ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.PagePrev: return m_QmIconNavPrev ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.SwitchWatchHand: return m_QmIconSwitchHand ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.History: return m_QmIconHistory ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.PerfMode:
                {
                    bool on = false;
                    try { on = VpbPerfController.IsPerfModeWanted; } catch { }
                    if (on)
                    {
                        int step = 0;
                        try { step = VpbPerfController.StepIndex; } catch { }
                        return QuickMenuGetPerfLevelIcon(step) ?? m_QmIconPerfModeOn ?? m_QmIconAssignEmpty;
                    }
                    return m_QmIconPerfModeOff ?? m_QmIconAssignEmpty;
                }
                case QuickMenuAssignableAction.RemoveAllClothing: return m_QmIconRemoveClothing ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.RemoveAllHair: return m_QmIconRemoveHair ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.ToggleImportSidebar: return m_QmIconImportSidebar ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.StarFilter: return m_QmIconStarOff ?? m_QmIconStarOn ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.OpenCategorySkin: return m_QmIconCategorySkin ?? m_QmIconOpenCategory;
                case QuickMenuAssignableAction.PerfStepUp: return m_QmIconPerfStepUp ?? m_QmIconAssignEmpty;
                case QuickMenuAssignableAction.PerfStepDown: return m_QmIconPerfStepDown ?? m_QmIconAssignEmpty;
                default: return m_QmIconAssignEmpty;
            }
        }

        private Sprite QuickMenuGetPerfLevelIcon(int step)
        {
            if (m_QmIconPerfLevels == null || step < 0 || step >= m_QmIconPerfLevels.Length) return null;
            return m_QmIconPerfLevels[step];
        }

        internal void RefreshQuickMenuPerfModeSlots()
        {
            if (m_QuickMenuGridButtons == null) return;
            for (int i = 0; i < QuickMenuGridSlotCount; i++)
                if (QuickMenuGetSlotAction(i) == QuickMenuAssignableAction.PerfMode)
                    try { QuickMenuRefreshSlotVisual(i); } catch { }
        }

        private static bool QuickMenuTryGetPersonIsMale(Atom person, out bool isMale)
        {
            isMale = false;
            if (person == null) return false;
            try
            {
                // Prefer DAZCharacter name heuristic (used elsewhere in VPB).
                if (person.type == "Person")
                {
                    isMale = VPB.src.util.AtomGenderUtils.IsMale(person);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static string QuickMenuFormatPersonLabel(Atom person)
        {
            if (person == null) return "";
            string name = "";
            try { name = person.name; } catch { name = ""; }
            if (string.IsNullOrEmpty(name))
            {
                try { name = person.uid; } catch { name = ""; }
            }

            bool isMale;
            if (QuickMenuTryGetPersonIsMale(person, out isMale))
                return (isMale ? "Male: " : "Female: ") + name;
            return "Person: " + name;
        }

        private string QuickMenuGetCurrentTargetPersonLabel()
        {
            var p = QuickMenuGetTargetPanel();
            if (p == null) return "";
            try
            {
                Atom a = p.SelectedTargetAtom;
                if (a == null) return "";
                if (!SceneUtils.IsPersonLikeAtom(a)) return "";
                return QuickMenuFormatPersonLabel(a);
            }
            catch { return ""; }
        }

        private void QuickMenuCyclePersonTarget(int delta)
        {
            if (delta == 0) return;
            var p = QuickMenuGetTargetPanel();
            if (p == null) return;

            List<Atom> persons = null;
            try
            {
                persons = new List<Atom>();
                var atoms = SuperController.singleton != null ? SuperController.singleton.GetAtoms() : null;
                if (atoms != null)
                {
                    for (int i = 0; i < atoms.Count; i++)
                    {
                        var a = atoms[i];
                        if (a == null) continue;
                        try { if (SceneUtils.IsPersonLikeAtom(a)) persons.Add(a); } catch { }
                    }
                }
            }
            catch { persons = null; }

            if (persons == null || persons.Count == 0) return;

            string curUid = null;
            try { curUid = p.QuickMenu_GetSelectedTargetPersonUid(); } catch { curUid = null; }

            int curIdx = -1;
            if (!string.IsNullOrEmpty(curUid))
            {
                for (int i = 0; i < persons.Count; i++)
                {
                    Atom a = persons[i];
                    if (a == null) continue;
                    try { if (a.uid == curUid) { curIdx = i; break; } } catch { }
                }
            }

            int nextIdx;
            if (curIdx < 0) nextIdx = 0;
            else
            {
                int n = persons.Count;
                nextIdx = ((curIdx + delta) % n + n) % n;
            }

            Atom next = persons[nextIdx];
            if (next == null) return;
            try { p.QuickMenu_SetSelectedTargetPersonUid(next.uid); } catch { }

            // If user still hovering TargetAtom slot, refresh tooltip text immediately.
            try
            {
                int hoverIdx = m_QmTooltipHoverSlotIdx;
                if (hoverIdx >= 0 && hoverIdx < QuickMenuGridSlotCount && QuickMenuGetSlotAction(hoverIdx) == QuickMenuAssignableAction.TargetAtom)
                    QuickMenuSetTooltip(QuickMenuGetTooltipForSlot(hoverIdx));
            }
            catch { }
        }

        private void QuickMenuAttachTargetAtomDragSource(GameObject slotButtonGO, int slotIdx)
        {
            if (slotButtonGO == null) return;
            var h = slotButtonGO.GetComponent<QuickMenuTargetAtomDragSourceHandler>();
            if (h == null) h = slotButtonGO.AddComponent<QuickMenuTargetAtomDragSourceHandler>();
            h.owner = this;
            h.slotIdx = slotIdx;
        }

        private GameObject m_QmTargetAtomDragGlyphGO;
        private SpriteRenderer m_QmTargetAtomDragGlyphRenderer;
        private Atom m_QmTargetAtomDragHoverAtom;
        private Camera m_QmTargetAtomDragCam;

        private void QuickMenuTargetAtomGlyphDestroy()
        {
            try { if (m_QmTargetAtomDragGlyphGO != null) Destroy(m_QmTargetAtomDragGlyphGO); } catch { }
            m_QmTargetAtomDragGlyphGO = null;
            m_QmTargetAtomDragGlyphRenderer = null;
            m_QmTargetAtomDragHoverAtom = null;
            m_QmTargetAtomDragCam = null;
        }

        private void QuickMenuTargetAtomGlyphEnsure()
        {
            if (m_QmTargetAtomDragGlyphGO != null && m_QmTargetAtomDragGlyphRenderer != null) return;

            QuickMenuTargetAtomGlyphDestroy();

            var go = new GameObject("VPB_QM_TargetAtomGlyph");
            go.hideFlags = HideFlags.HideAndDontSave;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = m_QmIconTargetAtom;
            sr.color = new Color(1f, 0f, 0f, 0.9f);
            sr.sortingOrder = 9999;
            go.transform.localScale = Vector3.one * 0.12f;
            m_QmTargetAtomDragGlyphGO = go;
            m_QmTargetAtomDragGlyphRenderer = sr;
        }

        private static Color QuickMenuTargetAtomColorForPerson(Atom personOrNull)
        {
            if (personOrNull == null) return new Color(1f, 0f, 0f, 0.9f); // red
            bool isMale;
            if (QuickMenuTryGetPersonIsMale(personOrNull, out isMale))
            {
                if (isMale) return new Color(0.2f, 0.5f, 1f, 0.9f);  // blue
                return new Color(1f, 0.3f, 0.7f, 0.9f);               // pink
            }
            return new Color(1f, 0f, 0f, 0.9f); // red (unknown)
        }

        private void QuickMenuTargetAtomDragUpdate(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (m_QmTargetAtomDragGlyphGO == null || m_QmTargetAtomDragGlyphRenderer == null) return;

            Camera cam = m_QmTargetAtomDragCam;
            if (cam == null) try { cam = m_QuickMenuCanvas != null ? m_QuickMenuCanvas.worldCamera : null; } catch { cam = null; }
            if (cam == null) cam = eventData.pressEventCamera;
            if (cam == null) cam = eventData.enterEventCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            string hitMsg;
            RaycastHit hit = default(RaycastHit);
            Atom atom = null;
            try { atom = SceneUtils.RaycastAtom(eventData.position, cam, out hitMsg, out hit); } catch { atom = null; }
            if (atom != null && !SceneUtils.IsPersonLikeAtom(atom)) atom = null;
            m_QmTargetAtomDragHoverAtom = atom;

            Ray ray = cam.ScreenPointToRay(eventData.position);
            Vector3 pos = (hit.collider != null) ? hit.point : ray.GetPoint(1.4f);
            m_QmTargetAtomDragGlyphGO.transform.position = pos;

            Vector3 look = pos - cam.transform.position;
            if (look.sqrMagnitude > 0.0001f)
                m_QmTargetAtomDragGlyphGO.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            m_QmTargetAtomDragGlyphRenderer.color = QuickMenuTargetAtomColorForPerson(atom);
        }

        private void QuickMenuTargetAtomDragCommit()
        {
            var p = QuickMenuGetTargetPanel();
            if (p == null) return;
            Atom atom = m_QmTargetAtomDragHoverAtom;
            if (atom == null) return;
            try { p.QuickMenu_SetSelectedTargetPersonUid(atom.uid); } catch { }
        }

        private void QuickMenuRebuildSavePopupButtons(GalleryPanel panel)
        {
            if (m_QuickMenuSavePopupRoot == null) return;
            foreach (var b in m_QuickMenuSavePopupButtons)
            {
                try { if (b != null) DestroyImmediate(b); } catch { }
            }
            m_QuickMenuSavePopupButtons.Clear();

            var opts = (panel != null) ? panel.QuickMenu_GetSaveOptions() : null;
            if (opts == null) opts = new List<GalleryPanel.QuickMenuSaveOption>();

            float w = 260f;
            float h = 40f;
            float y = 20f;
            float gap = 42f;
            int font = 22;

            int n = opts.Count;
            for (int i = 0; i < n; i++)
            {
                int iCopy = i;
                var o = opts[iCopy];
                string label = (o != null && !string.IsNullOrEmpty(o.Label)) ? o.Label : ("Option " + (iCopy + 1));
                bool enabled = (o != null) ? o.Enabled : false;

                // Bottom-up coordinates: place option 0 at the top and the Cancel row at the
                // bottom so the visual order matches the gallery Save popup (issue #62).
                var btnGo = UI.CreateUIButton(m_QuickMenuSavePopupRoot, w, h, label, font, 10f, y + gap * (n - i), AnchorPresets.bottomLeft, () =>
                {
                    try
                    {
                        if (o != null && o.Enabled && o.Action != null) o.Action();
                    }
                    catch { }
                    QuickMenuHideSavePopup();
                });

                // Disabled visual (best-effort)
                try
                {
                    var b = btnGo != null ? btnGo.GetComponent<Button>() : null;
                    if (b != null) b.interactable = enabled;
                    var img = btnGo != null ? btnGo.GetComponent<Image>() : null;
                    if (img != null && !enabled) img.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
                }
                catch { }

                m_QuickMenuSavePopupButtons.Add(btnGo);
            }

            var closeBtn = UI.CreateUIButton(m_QuickMenuSavePopupRoot, w, h,
                VPBTranslation.T("hook.qmbutton.cancel", "Cancel"), font, 10f, y, AnchorPresets.bottomLeft, () =>
            {
                QuickMenuHideSavePopup();
            });
            try
            {
                var img = closeBtn != null ? closeBtn.GetComponent<Image>() : null;
                if (img != null) img.color = new Color(0.55f, 0.22f, 0.22f, 1f);
            }
            catch { }
            m_QuickMenuSavePopupButtons.Add(closeBtn);

            float totalH = 20f + (n + 1) * gap + 10f;
            if (totalH < 120f) totalH = 120f;
            m_QuickMenuSavePopupRT.sizeDelta = new Vector2(300f, totalH);
            try { UI.ApplyGalleryPaneHoverPolicy(m_QuickMenuSavePopupRoot); } catch { }
        }

        private class QuickMenuSquareHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Image target;
            public Color normal;
            public Color hover;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (target != null) target.color = hover;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (target != null) target.color = normal;
            }
        }

        private class QuickMenuAssignDragSourceHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public VamHookPlugin owner;
            public QuickMenuAssignableAction action;
            private CanvasGroup _cg;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (owner == null) return;
                if (_cg == null) _cg = GetComponent<CanvasGroup>();
                if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
                _cg.blocksRaycasts = false;
                owner.m_QmAssignDragAction = action;
                owner.m_QmAssignDragActive = true;
                owner.QuickMenuBeginAssignDragGhost(action, eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (owner == null) return;
                owner.QuickMenuUpdateAssignDragGhostPosition(eventData);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (_cg != null) _cg.blocksRaycasts = true;
                if (owner == null) return;
                // Fallback: in VR the EventSystem doesn't reliably deliver IDropHandler.OnDrop, so the
                // drop never lands. Resolve the slot under the pointer here and assign directly.
                owner.QuickMenuTryAssignDraggedActionAtPointer(eventData);
                owner.m_QmAssignDragActive = false;
                owner.QuickMenuEndAssignDragGhost();
                owner.QuickMenuClearAllDropTargetHighlights();
            }
        }

        private class QuickMenuAssignDropTargetHandler : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public VamHookPlugin owner;
            public int slotIdx;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (owner == null) return;
                if (!owner.m_QmAssignDragActive) return;
                if (!owner.QuickMenuCanPlaceActionInSlot(owner.m_QmAssignDragAction, slotIdx)) return;
                owner.QuickMenuSetDropTargetHighlight(slotIdx, true);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (owner == null) return;
                owner.QuickMenuSetDropTargetHighlight(slotIdx, false);
            }

            public void OnDrop(PointerEventData eventData)
            {
                if (owner == null) return;
                owner.QuickMenuTryAssignDraggedActionToSlot(slotIdx);
                owner.QuickMenuSetDropTargetHighlight(slotIdx, false);
            }
        }

        private class QuickMenuTargetAtomDragSourceHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public VamHookPlugin owner;
            public int slotIdx;
            private string _lastTooltip;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (owner == null) return;
                if (owner.m_QuickMenuEditMode) return;
                if (slotIdx < 0 || slotIdx >= QuickMenuGridSlotCount) return;
                var a = owner.QuickMenuGetSlotAction(slotIdx);
                if (a != QuickMenuAssignableAction.TargetAtom) return;

                owner.m_QmTargetAtomDragCam = eventData != null ? (eventData.pressEventCamera ?? Camera.main) : Camera.main;
                owner.QuickMenuTargetAtomGlyphEnsure();
                owner.QuickMenuTargetAtomDragUpdate(eventData);
                _lastTooltip = null;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (owner == null) return;
                if (owner.m_QmTargetAtomDragGlyphGO == null) return;
                owner.QuickMenuTargetAtomDragUpdate(eventData);

                Atom a = owner.m_QmTargetAtomDragHoverAtom;
                string msg;
                if (a != null)
                    msg = "Release to select " + QuickMenuFormatPersonLabel(a);
                else
                    msg = VPBTranslation.T("hook.qmbutton.target_atom_drag", "Drag over Person. Release to select.");

                if (_lastTooltip != msg)
                {
                    _lastTooltip = msg;
                    owner.QuickMenuSetTooltip(msg);
                }
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (owner == null) return;
                if (owner.m_QmTargetAtomDragGlyphGO != null)
                {
                    owner.QuickMenuTargetAtomDragUpdate(eventData);
                    owner.QuickMenuTargetAtomDragCommit();
                }
                owner.QuickMenuTargetAtomGlyphDestroy();
                if (!string.IsNullOrEmpty(_lastTooltip))
                    owner.QuickMenuClearTooltip(_lastTooltip);
                _lastTooltip = null;
            }
        }
    }
}

