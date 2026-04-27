using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        private static readonly Vector2 QuickMenuAnchorBaseline = new Vector2(-515f, -12f); // shown as (0,0) in the position UI
        private static readonly Vector2 QuickMenuPopupOffset = new Vector2(260f, -20f);

        private enum QuickMenuAssignableAction
        {
            None = 0,
            CreateGallery,
            ShowHide,
            BringFront,
            CloseAll,
            Save,
            Random,
            Undo,
            Redo,
            Hub,
            Cleanup,
            ReplaceAddToggle,
            CompressCache,
            AutoHideGallery,
            ShowHiddenPackages,
            FpsCounter,
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
        private readonly List<GameObject> m_QuickMenuAssignPopupButtons = new List<GameObject>(32);
        private int m_QuickMenuAssignPopupTargetIdx = -1;
        private bool m_QuickMenuEditMode;
        private int m_QuickMenuEditSlotIdx = -1;
        private int m_QuickMenuSavePopupTargetIdx = -1;
        private GalleryPanel m_QuickMenuSavePopupPanel;

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
        private Sprite m_QmIconReplace;
        private Sprite m_QmIconAdd;
        private Sprite m_QmIconCompressCache;
        private Sprite m_QmIconAutoHideOn;
        private Sprite m_QmIconAutoHideOff;
        private Sprite m_QmIconShowHiddenOn;
        private Sprite m_QmIconShowHiddenOff;
        private Sprite[] m_QmIconPages; // 10 icons: page_0..page_9

        private Vector2 m_QmLastAnchorCenter = new Vector2(float.NaN, float.NaN);
        private bool m_QmLastAnchorIsVR = false;

        private static bool QuickMenuIsVrActive()
        {
            try { return UnityEngine.XR.XRSettings.enabled; }
            catch { return false; }
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
                if (m_ShowQuickMenuPosWindow)
                {
                    isVR = false; // window is desktop-only
                    // In the UI, X/Y are shown relative to the baseline.
                    center = QuickMenuAnchorBaseline + new Vector2(m_QuickMenuPosCreateX, m_QuickMenuPosCreateY);
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
                case QuickMenuAssignableAction.Undo: return "undo";
                case QuickMenuAssignableAction.Redo: return "redo";
                case QuickMenuAssignableAction.Hub: return "hub";
                case QuickMenuAssignableAction.Cleanup: return "cleanup";
                case QuickMenuAssignableAction.ReplaceAddToggle: return "replace_add_toggle";
                case QuickMenuAssignableAction.CompressCache: return "compress_cache";
                case QuickMenuAssignableAction.AutoHideGallery: return "autohide_gallery";
                case QuickMenuAssignableAction.ShowHiddenPackages: return "show_hidden_packages";
                case QuickMenuAssignableAction.FpsCounter: return "fps_counter";
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
                case "undo": return QuickMenuAssignableAction.Undo;
                case "redo": return QuickMenuAssignableAction.Redo;
                case "hub": return QuickMenuAssignableAction.Hub;
                case "cleanup": return QuickMenuAssignableAction.Cleanup;
                case "replace_add_toggle": return QuickMenuAssignableAction.ReplaceAddToggle;
                case "compress_cache": return QuickMenuAssignableAction.CompressCache;
                case "autohide_gallery": return QuickMenuAssignableAction.AutoHideGallery;
                case "show_hidden_packages": return QuickMenuAssignableAction.ShowHiddenPackages;
                case "fps_counter": return QuickMenuAssignableAction.FpsCounter;
                default: return QuickMenuAssignableAction.None;
            }
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
            // First row shared across all pages.
            if (slotIdx >= 0 && slotIdx <= 3)
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

        private void QuickMenuSetAssignment(int idx, QuickMenuAssignableAction action)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return;
            EnsureQuickMenuGridArrays();
            QuickMenuSetSlotAction(idx, action);
        }

        private static void QuickMenuSetIcon(GameObject buttonGO, Sprite icon, float padding)
        {
            if (buttonGO == null) return;

            try
            {
                var iconTr = buttonGO.transform.Find("Icon");
                if (iconTr != null) DestroyImmediate(iconTr.gameObject);
            }
            catch { }

            if (icon == null) return;

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(buttonGO.transform, false);
            Image img = iconGO.AddComponent<Image>();
            img.sprite = icon;
            img.color = Color.white;
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

        private void QuickMenuRefreshSlotVisual(int idx)
        {
            if (idx < 0 || idx >= QuickMenuGridSlotCount) return;
            if (m_QuickMenuGridButtons == null) return;
            var go = m_QuickMenuGridButtons[idx];
            if (go == null) return;

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
                    Color normal = new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    Color hover  = new Color(0.35f, 0.35f, 0.35f, 0.75f);
                    bg.color = normal;
                    var hh = bg.GetComponent<QuickMenuSquareHover>();
                    if (hh != null) { hh.normal = normal; hh.hover = hover; }
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
                    Color normal = new Color(0.35f, 0.35f, 0.35f, 0.5f);
                    Color hover  = new Color(0.35f, 0.35f, 0.35f, 0.75f);
                    bg.color = normal;
                    var hh = bg.GetComponent<QuickMenuSquareHover>();
                    if (hh != null) { hh.normal = normal; hh.hover = hover; }
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
                case QuickMenuAssignableAction.None:
                default:
                    if (m_QuickMenuEditMode) icon = m_QmIconAssignEmpty;
                    break;
            }

            if (action == QuickMenuAssignableAction.FpsCounter)
            {
                float fps = 0f;
                try
                {
                    fps = 1f / Mathf.Max(0.00001f, m_FpsSmoothedDelta);
                }
                catch { fps = 0f; }
                // Display as an integer 0..999 (no decimals), per quick-menu compact constraint.
                if (fps > 999f) fps = 999f;
                if (fps < 0f) fps = 0f;
                string txt = ((int)(fps + 0.5f)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                QuickMenuSetLabel(go, txt, clearIcon: true);
            }
            else
                QuickMenuSetIcon(go, icon, padding: 6f);

            bool isAssigned = action != QuickMenuAssignableAction.None;
            Image bgImg = (m_QuickMenuGridBackdropImages != null && idx < m_QuickMenuGridBackdropImages.Length) ? m_QuickMenuGridBackdropImages[idx] : null;
            if (bgImg != null)
            {
                Color normalAssigned = new Color(0.35f, 0.35f, 0.35f, 0.5f);
                Color hoverAssigned  = new Color(0.35f, 0.35f, 0.35f, 0.75f);
                Color normalEmpty    = new Color(0.35f, 0.35f, 0.35f, 0.0f);
                Color hoverEmpty     = new Color(0.35f, 0.35f, 0.35f, 0.35f);

                Color normal = isAssigned ? normalAssigned : normalEmpty;
                Color hover  = isAssigned ? hoverAssigned  : hoverEmpty;

                bgImg.color = normal;
                var hh = bgImg.GetComponent<QuickMenuSquareHover>();
                if (hh != null) { hh.normal = normal; hh.hover = hover; }
            }
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
                        if (pos == Vector2.zero && m_QuickMenuAssignPopupRT != null) pos = m_QuickMenuAssignPopupRT.anchoredPosition;
                        QuickMenuShowSavePopup(slotIdx, pos + QuickMenuPopupOffset, p);
                    }
                    break;
                }
                case QuickMenuAssignableAction.Random:
                {
                    var p = QuickMenuGetTargetPanel();
                    if (p != null) p.QuickMenu_LoadRandom();
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
                    CacheCleanupManager.FlushHitsBatch();
                    break;
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
                case QuickMenuAssignableAction.None:
                default:
                    break;
            }
        }

        private void QuickMenuOpenCompressCache()
        {
            try
            {
                // Open the existing "Compress Cache (Zstd)" window in the main plugin UI.
                m_ShowSpaceSaverWindow = true;
                m_Show = true;
            }
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

        private void QuickMenuHideAssignPopup()
        {
            m_QuickMenuAssignPopupTargetIdx = -1;
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;
            if (m_QuickMenuAssignPopupRoot != null && m_QuickMenuAssignPopupRoot.activeSelf)
                m_QuickMenuAssignPopupRoot.SetActive(false);
        }

        private void QuickMenuShowAssignPopup(int slotIdx, Vector2 anchoredPos)
        {
            if (m_QuickMenuAssignPopupRoot == null || m_QuickMenuAssignPopupRT == null) return;
            m_QuickMenuAssignPopupTargetIdx = slotIdx;
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;

            m_QuickMenuAssignPopupRT.anchoredPosition = anchoredPos;
            if (!m_QuickMenuAssignPopupRoot.activeSelf) m_QuickMenuAssignPopupRoot.SetActive(true);
        }

        private void QuickMenuShowSavePopup(int slotIdx, Vector2 anchoredPos, GalleryPanel panel)
        {
            if (m_QuickMenuAssignPopupRoot == null || m_QuickMenuAssignPopupRT == null) return;
            m_QuickMenuAssignPopupTargetIdx = -1;
            m_QuickMenuSavePopupTargetIdx = slotIdx;
            m_QuickMenuSavePopupPanel = panel;

            QuickMenuRebuildSavePopupButtons(panel);
            m_QuickMenuAssignPopupRT.anchoredPosition = anchoredPos;
            if (!m_QuickMenuAssignPopupRoot.activeSelf) m_QuickMenuAssignPopupRoot.SetActive(true);
        }

        private void QuickMenuRebuildAssignPopupButtons()
        {
            if (m_QuickMenuAssignPopupRoot == null) return;
            foreach (var b in m_QuickMenuAssignPopupButtons)
            {
                try { if (b != null) DestroyImmediate(b); } catch { }
            }
            m_QuickMenuAssignPopupButtons.Clear();

            QuickMenuAssignableAction[] actions = new QuickMenuAssignableAction[]
            {
                QuickMenuAssignableAction.None,
                QuickMenuAssignableAction.CreateGallery,
                QuickMenuAssignableAction.ShowHide,
                QuickMenuAssignableAction.BringFront,
                QuickMenuAssignableAction.CloseAll,
                QuickMenuAssignableAction.Save,
                QuickMenuAssignableAction.Random,
                QuickMenuAssignableAction.Undo,
                QuickMenuAssignableAction.Redo,
                QuickMenuAssignableAction.Hub,
                QuickMenuAssignableAction.Cleanup,
                QuickMenuAssignableAction.ReplaceAddToggle,
                QuickMenuAssignableAction.CompressCache,
                QuickMenuAssignableAction.AutoHideGallery,
                QuickMenuAssignableAction.ShowHiddenPackages,
                QuickMenuAssignableAction.FpsCounter,
            };

            string[] labels = new string[]
            {
                VPBTranslation.T("hook.qmassign.none", "None"),
                VPBTranslation.T("hook.qmbutton.create_gallery", "Create Gallery"),
                VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide"),
                VPBTranslation.T("hook.qmbutton.bring_front", "Bring Front"),
                VPBTranslation.T("hook.qmbutton.close_all", "Close All"),
                VPBTranslation.T("hook.qmbutton.save", "Save"),
                VPBTranslation.T("hook.qmbutton.random", "Random"),
                VPBTranslation.T("hook.qmbutton.undo", "Undo"),
                VPBTranslation.T("hook.qmbutton.redo", "Redo"),
                VPBTranslation.T("hook.qmbutton.hub", "Hub"),
                VPBTranslation.T("hook.qmbutton.cleanup", "Cleanup"),
                VPBTranslation.T("hook.qmbutton.replace_add", "Replace/Add"),
                VPBTranslation.T("hook.qmbutton.compress_cache", "Compress Cache"),
                VPBTranslation.T("hook.qmbutton.autohide", "Auto-Hide"),
                VPBTranslation.T("hook.qmbutton.show_hidden", "Show Hidden"),
                VPBTranslation.T("hook.qmbutton.fps", "FPS Counter"),
            };

            float w = 240f;
            float h = 40f;
            // Bottom-up list: place items upward from the popup bottom so options are less likely to be clipped.
            float y = 20f;
            float gap = 42f;
            int font = 24;

            int n = Mathf.Min(actions.Length, labels.Length);
            for (int i = 0; i < n; i++)
            {
                int iCopy = i;
                var btnGo = UI.CreateUIButton(m_QuickMenuAssignPopupRoot, w, h, labels[i], font, 10f, y + gap * i, AnchorPresets.bottomLeft, () =>
                {
                    int idx = m_QuickMenuAssignPopupTargetIdx;
                    if (idx >= 0 && idx < QuickMenuGridSlotCount)
                    {
                        QuickMenuSetAssignment(idx, actions[iCopy]);
                    }
                    QuickMenuHideAssignPopup();
                });
                m_QuickMenuAssignPopupButtons.Add(btnGo);
            }

            // Resize popup to fit
            float totalH = 20f + n * gap + 10f;
            m_QuickMenuAssignPopupRT.sizeDelta = new Vector2(260f, totalH);
        }

        private void QuickMenuRebuildSavePopupButtons(GalleryPanel panel)
        {
            if (m_QuickMenuAssignPopupRoot == null) return;
            foreach (var b in m_QuickMenuAssignPopupButtons)
            {
                try { if (b != null) DestroyImmediate(b); } catch { }
            }
            m_QuickMenuAssignPopupButtons.Clear();

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

                var btnGo = UI.CreateUIButton(m_QuickMenuAssignPopupRoot, w, h, label, font, 10f, y + gap * i, AnchorPresets.bottomLeft, () =>
                {
                    try
                    {
                        if (o != null && o.Enabled && o.Action != null) o.Action();
                    }
                    catch { }
                    QuickMenuHideAssignPopup();
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

                m_QuickMenuAssignPopupButtons.Add(btnGo);
            }

            float totalH = 20f + n * gap + 10f;
            if (totalH < 120f) totalH = 120f;
            m_QuickMenuAssignPopupRT.sizeDelta = new Vector2(300f, totalH);
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
    }
}

