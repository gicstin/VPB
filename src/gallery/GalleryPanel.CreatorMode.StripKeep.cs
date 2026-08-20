using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Strip Scene keep selector — expandable categories + per-atom picks.
    /// Defaults: Persons + Lights. CoreControl/CameraRig kept silently.
    /// Environment always dropped (blank new-scene look). Cold/warm path only.
    /// </summary>
    public partial class GalleryPanel
    {
        private struct StripKeepItem
        {
            public string Uid;
            public string Name;
            public string Type;
            public CreatorStripKeepKind Kind;
            /// <summary>True for synthetic rows (e.g. Default 3P Lights) — not live atoms.</summary>
            public bool Synthetic;
        }

        /// <summary>Synthetic uid: spawn appearance-import 3P after strip rebuild (not a live atom).</summary>
        private const string StripKeepSyntheticDefault3PUid = "VPB_SYNTH_DEFAULT_3P_LIGHTS";

        private GameObject _stripKeepModalRoot;
        private Transform _stripKeepListParent;
        private LayoutElement _stripKeepScrollHostLe;
        private GameObject _stripKeepDefault3PHost;
        private Toggle _stripKeepDefault3PToggle;
        private Button _stripKeepConfirmBtn;
        private Image _stripKeepConfirmImg;
        private GameObject _stripKeepPresetsHost;

        private CreatorStripKeepKind _stripKeepMask = SceneUtils.CreatorStripKeepDefault;
        private int _stripKeepForcedDropCount;
        private int _stripKeepDropCount;
        private int _stripKeepKeepCount;
        private bool _stripKeepAwaitingSoftConfirm;
        private Text _stripKeepConfirmBtnLabel;
        private Text _stripKeepCancelBtnLabel;
        private readonly int[] _stripKeepCounts = new int[12];
        private readonly List<StripKeepItem> _stripKeepItems = new List<StripKeepItem>(64);
        private readonly HashSet<string> _stripKeepSelectedUids =
            new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Original atom uid → name to use in keepers scene JSON (only when different).</summary>
        private readonly Dictionary<string, string> _stripKeepRenames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<CreatorStripKeepKind> _stripKeepExpanded =
            new HashSet<CreatorStripKeepKind>();
        private readonly Dictionary<CreatorStripKeepKind, Toggle> _stripKeepCatToggles =
            new Dictionary<CreatorStripKeepKind, Toggle>(12);
        private readonly Dictionary<CreatorStripKeepKind, Text> _stripKeepCatLabels =
            new Dictionary<CreatorStripKeepKind, Text>(12);
        private readonly Dictionary<CreatorStripKeepKind, Text> _stripKeepExpandGlyphs =
            new Dictionary<CreatorStripKeepKind, Text>(12);
        private bool _stripKeepSuppressToggleEvents;
        private GameObject _stripKeepRenameOverlayRoot;

        private static int StripKeepKindIndex(CreatorStripKeepKind kind)
        {
            switch (kind)
            {
                case CreatorStripKeepKind.Persons: return 0;
                case CreatorStripKeepKind.Lights: return 1;
                case CreatorStripKeepKind.Sound: return 2;
                case CreatorStripKeepKind.Cameras: return 3;
                case CreatorStripKeepKind.Forces: return 4;
                case CreatorStripKeepKind.Animation: return 5;
                case CreatorStripKeepKind.Assets: return 6;
                case CreatorStripKeepKind.Toys: return 7;
                case CreatorStripKeepKind.Props: return 8;
                case CreatorStripKeepKind.UI: return 9;
                case CreatorStripKeepKind.SubScenes: return 10;
                case CreatorStripKeepKind.Other: return 11;
                default: return -1;
            }
        }

        private void TboxCreatorStripSceneOpenKeepSelector()
        {
            if (!creatorModeActive) return;
            if (creatorModeStripBusy)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_busy", "Strip in progress — wait."),
                    1.5f);
                return;
            }
            // Hotkey / toolbox can open strip while SubScene pick banner still active.
            if (_stripKeepSubScenePickActive)
            {
                try { StripKeepAbortSubScenePickMode(reopenStrip: false); } catch { }
            }
            if (SuperController.singleton == null)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.creator.no_sc", "Scene controller unavailable."), 1.5f);
                return;
            }
            if (StripKeepResolveUiHost() == null) return;

            StripKeepPowerUiResetSessionState();
            _stripKeepAwaitingSoftConfirm = false;
            LoadStripKeepMaskFromConfig();
            LoadStripKeepPossessOptionsFromConfig();
            LoadStripKeepCreateOptionsFromConfig();
            _stripKeepRenames.Clear();
            RebuildStripKeepItemList();
            if (!StripKeepTryApplyLastRecipeOnOpen())
                SeedStripKeepSelectionFromMask();
            RecalcStripKeepTotalsFromSelection();

            if (_stripKeepDropCount <= 0 && !StripKeepHasAnyRealAtoms())
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_none_empty", "Nothing to strip."),
                    2f);
                return;
            }

            HideStripKeepSelector();
            BuildStripKeepSelectorModal();
            RefreshCreatorModeChrome();
        }

        /// <summary>
        /// Host for Strip Scene chrome. Prefer gallery canvas (sibling of pane) so modal
        /// survives dock collapse / backgroundBox SetActive(false) — same pattern as tag menu.
        /// </summary>
        private GameObject StripKeepResolveUiHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        /// <summary>
        /// Empty check: ignore synthetic create-rows (Default 3P) — not strip targets.
        /// </summary>
        private bool StripKeepHasAnyRealAtoms()
        {
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                if (!_stripKeepItems[i].Synthetic) return true;
            }
            return false;
        }

        private void LoadStripKeepMaskFromConfig()
        {
            _stripKeepMask = SceneUtils.CreatorStripKeepDefault;
            try
            {
                if (VPBConfig.Instance != null)
                {
                    int raw = VPBConfig.Instance.CreatorStripKeepMask;
                    if (raw != 0)
                        _stripKeepMask = (CreatorStripKeepKind)raw & SceneUtils.CreatorStripKeepAllUser;
                    else
                        _stripKeepMask = SceneUtils.CreatorStripKeepDefault;
                }
            }
            catch
            {
                _stripKeepMask = SceneUtils.CreatorStripKeepDefault;
            }
        }

        private void PersistStripKeepMaskFromSelection()
        {
            CreatorStripKeepKind mask = CreatorStripKeepKind.None;
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                CreatorStripKeepKind kind = order[i];
                int selected;
                int total;
                CountStripKeepKindSelection(kind, out selected, out total);
                if (selected > 0)
                    mask |= kind;
            }
            if (mask == CreatorStripKeepKind.None)
                mask = SceneUtils.CreatorStripKeepDefault;
            _stripKeepMask = mask;
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.CreatorStripKeepMask = (int)(_stripKeepMask & SceneUtils.CreatorStripKeepAllUser);
                VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        /// <summary>
        /// Single pass: fill item list + per-kind counts + system count. Does not touch selection.
        /// </summary>
        private void RebuildStripKeepItemList()
        {
            _stripKeepItems.Clear();
            for (int i = 0; i < _stripKeepCounts.Length; i++)
                _stripKeepCounts[i] = 0;
            _stripKeepForcedDropCount = 0;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            List<Atom> all = null;
            try { all = sc.GetAtoms(); } catch { all = null; }
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (a == null) continue;

                // CoreControl / CameraRig — kept silently, never listed.
                if (SceneUtils.IsSystemProtectedAtom(a))
                    continue;

                // Environment sky/sphere — always strip, never listed.
                if (SceneUtils.IsCreatorStripAlwaysDropAtom(a))
                {
                    _stripKeepForcedDropCount++;
                    continue;
                }

                string uid = null;
                string type = null;
                string name = null;
                try { uid = a.uid; } catch { uid = null; }
                try { type = a.type; } catch { type = null; }
                try { name = a.name; } catch { name = null; }
                if (string.IsNullOrEmpty(uid)) continue;

                CreatorStripKeepKind kind = SceneUtils.ClassifyCreatorStripKeepKind(type);
                if (kind == CreatorStripKeepKind.None) continue;
                int idx = StripKeepKindIndex(kind);
                if (idx >= 0) _stripKeepCounts[idx]++;

                StripKeepItem item;
                item.Uid = uid;
                item.Type = type ?? "";
                item.Kind = kind;
                item.Synthetic = false;
                if (!string.IsNullOrEmpty(name))
                    item.Name = name;
                else
                    item.Name = uid;
                _stripKeepItems.Add(item);
            }

            // Default 3P is an external create-option (not nested under Lights expand).
            StripKeepResortItems();
        }

        private bool StripKeepHasAnyRealLightAtoms()
        {
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue;
                if (it.Kind == CreatorStripKeepKind.Lights) return true;
            }
            return false;
        }

        private int StripKeepCountKeptRealLights()
        {
            int n = 0;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue;
                if (it.Kind != CreatorStripKeepKind.Lights) continue;
                if (_stripKeepSelectedUids.Contains(it.Uid)) n++;
            }
            return n;
        }

        /// <summary>
        /// Show create-3P when scene lights will be stripped (none kept) — easy check, outside Lights dropdown.
        /// </summary>
        private bool StripKeepShouldShowDefault3P()
        {
            return StripKeepCountKeptRealLights() == 0;
        }

        private void SeedStripKeepSelectionFromMask()
        {
            StripKeepClearSoftConfirm();
            _stripKeepSelectedUids.Clear();
            bool anyRealLight = false;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue;
                if (SceneUtils.CreatorStripMaskContains(_stripKeepMask, it.Kind))
                    _stripKeepSelectedUids.Add(it.Uid);
                if (it.Kind == CreatorStripKeepKind.Lights)
                    anyRealLight = true;
            }

            // Lights kept but scene has none → auto-offer create fill (3P or saved SubScene).
            if (SceneUtils.CreatorStripMaskContains(_stripKeepMask, CreatorStripKeepKind.Lights)
                && !anyRealLight)
            {
                StripKeepSeedCreateFillWhenNoLights();
            }
        }

        private void RecalcStripKeepTotalsFromSelection()
        {
            _stripKeepKeepCount = 0;
            _stripKeepDropCount = _stripKeepForcedDropCount;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue; // create-entry, not a live atom
                if (_stripKeepSelectedUids.Contains(it.Uid))
                    _stripKeepKeepCount++;
                else
                    _stripKeepDropCount++;
            }
        }

        private void StripKeepClearSoftConfirm()
        {
            if (!_stripKeepAwaitingSoftConfirm) return;
            _stripKeepAwaitingSoftConfirm = false;
        }

        private void CountStripKeepKindSelection(CreatorStripKeepKind kind, out int selected, out int total)
        {
            selected = 0;
            total = 0;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != kind) continue;
                if (it.Synthetic) continue;
                total++;
                if (_stripKeepSelectedUids.Contains(it.Uid))
                    selected++;
            }
        }

        private void SetStripKeepKindSelection(CreatorStripKeepKind kind, bool keepAll)
        {
            StripKeepClearSoftConfirm();
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != kind) continue;
                if (it.Synthetic) continue;
                if (keepAll) _stripKeepSelectedUids.Add(it.Uid);
                else _stripKeepSelectedUids.Remove(it.Uid);
            }
        }

        private void HideStripKeepSelector()
        {
            HideStripKeepSelectorInternal(resetSession: true);
        }

        /// <summary>
        /// Destroy strip-keep chrome. When <paramref name="resetSession"/> is false, keep
        /// selection/filter/sort for live ChromeScale rebuild.
        /// </summary>
        private void HideStripKeepSelectorInternal(bool resetSession)
        {
            try { HideStripKeepRenameOverlay(); } catch { }
            try { HideStripKeepRecipeSaveInline(); } catch { }
            try { HideStripKeepShortcutHelp(); } catch { }
            try { StripKeepPersistPanelGeometry(); } catch { }
            if (_stripKeepModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_stripKeepModalRoot); } catch { }
                _stripKeepModalRoot = null;
            }
            _stripKeepPanelRT = null;
            _stripKeepResizeGO = null;
            _stripKeepListParent = null;
            _stripKeepScrollHostLe = null;
            StripKeepCreateOptionsResetUiRefs();
            _stripKeepConfirmBtn = null;
            _stripKeepConfirmImg = null;
            _stripKeepConfirmBtnLabel = null;
            _stripKeepCancelBtnLabel = null;
            _stripKeepPresetsHost = null;
            _stripKeepCatToggles.Clear();
            _stripKeepCatLabels.Clear();
            _stripKeepExpandGlyphs.Clear();
            _stripKeepSuppressToggleEvents = false;
            if (resetSession)
            {
                _stripKeepAwaitingSoftConfirm = false;
                try { StripKeepPowerUiOnHide(); } catch { }
            }
            else
            {
                try { StripKeepPowerUiClearUiRefsForRebuild(); } catch { }
            }
            RefreshCreatorModeChrome();
            // Mode stays on — panel close ≠ exit Creator Mode.
        }

        /// <summary>Live ChromeScale adapt — rebuild chrome, keep picks/filter/recipes session.</summary>
        private void RescaleStripKeepSelectorInternal(float s)
        {
            if (!IsStripKeepSelectorOpen()) return;
            if (s <= 0.01f) s = 1f;
            if (_stripKeepBuiltChromeScale > 0f
                && Mathf.Abs(s - _stripKeepBuiltChromeScale) < 0.0005f)
                return;

            bool more = _stripKeepMoreToolsExpanded;
            bool help = _stripKeepShortcutHelpVisible;
            bool saveInline = IsStripKeepRecipeSaveInlineOpen();
            string saveDraft = null;
            if (saveInline && _stripKeepRecipeSaveInlineInput != null)
                saveDraft = _stripKeepRecipeSaveInlineInput.text;
            bool soft = _stripKeepAwaitingSoftConfirm;

            HideStripKeepSelectorInternal(resetSession: false);
            _stripKeepMoreToolsExpanded = more;
            _stripKeepAwaitingSoftConfirm = soft;
            BuildStripKeepSelectorModal();
            if (help) ShowStripKeepShortcutHelp();
            if (saveInline)
            {
                OpenStripKeepRecipeSaveInline();
                if (_stripKeepRecipeSaveInlineInput != null && !string.IsNullOrEmpty(saveDraft))
                    _stripKeepRecipeSaveInlineInput.text = saveDraft;
            }
        }

        private bool IsStripKeepSelectorOpen()
        {
            return _stripKeepModalRoot != null;
        }

        private void BuildStripKeepSelectorModal()
        {
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerBtnH = chromeSz;
            Vector2 panelSize = StripKeepResolvePanelSize(s);
            Vector2 panelPos = StripKeepResolvePanelPos();

            // Floating panel on canvas (sibling of pane) — same host pattern as Settings float.
            GameObject host = StripKeepResolveUiHost();
            if (host == null) return;
            _stripKeepModalRoot = UI.CreateChildRT(host, "VPB_StripKeepFloat", AnchorPresets.stretchAll);
            try { _stripKeepModalRoot.transform.SetAsLastSibling(); } catch { }
            // No dim — work-surface float like Settings / Plugins (not locked modal).

            // Top-left pivot — resize from BR keeps TL fixed. Config still stores center pos.
            GameObject panel = UI.CreateChildRT(
                _stripKeepModalRoot, "Panel", AnchorPresets.middleCenter, panelSize,
                StripKeepCenterPosToTopLeft(panelPos, panelSize));
            _stripKeepPanelRT = panel.GetComponent<RectTransform>();
            if (_stripKeepPanelRT != null)
            {
                _stripKeepPanelRT.pivot = new Vector2(0f, 1f);
                _stripKeepPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _stripKeepPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _stripKeepPanelRT.sizeDelta = panelSize;
                _stripKeepPanelRT.anchoredPosition = StripKeepCenterPosToTopLeft(panelPos, panelSize);
            }
            UI.AddImage(panel, StripKeepPanelBg);
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();

            VerticalLayoutGroup panelVlg = UI.AddVLG(panel, spacing: 0f, padding: UI.Pad(0, 0, 0, 0));
            panelVlg.childForceExpandHeight = false;
            panelVlg.childControlHeight = true;
            panelVlg.childForceExpandWidth = true;

            // --- Titlebar: grip · window icon · title · X (drag handle) ---
            GameObject header = new GameObject("TitleBar");
            header.transform.SetParent(panel.transform, false);
            Image headerImg = UI.AddImage(header, StripKeepTitleBarBg);
            if (headerImg != null) headerImg.raycastTarget = true;
            HorizontalLayoutGroup headerHlg = UI.AddHLG(
                header, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            UI.AddLE(header, minHeight: titleH, preferredHeight: titleH, flexibleWidth: 1f, flexibleHeight: 0f);
            if (header.GetComponent<RectMask2D>() == null)
                header.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(header, "\u2807", font,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(headerHlg, grip.gameObject, s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(header, "geometry", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                header,
                VPBTranslation.T("gallery.creator.strip_keep_title", "Strip Scene — Keep"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 80f * s);
            AddTooltipPlain(header,
                VPBTranslation.T(
                    "gallery.creator.strip_keep_hint_short",
                    "/ filter · ? shortcuts · Space/F2/Enter"));

            // X / Cancel dismiss Scene Tools — Settings Cancel/X parity (not panel-only hide).
            GameObject closeGo = SettingsFloatSquareIconButton(
                header.transform, chromeSz, "x",
                GalleryUiColorTokens.ChromeIconWell, () => ExitCreatorMode());
            if (closeGo != null)
            {
                closeGo.name = "TitleClose";
                AddTooltipPlain(closeGo,
                    VPBTranslation.T("gallery.creator.strip_close_tip", "Close — exit Scene Tools (Esc)"));
            }

            var headerDrag = header.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _stripKeepPanelRT;
            headerDrag.OnMoved = StripKeepOnPanelMoved;

            // Body — flush under titlebar; list owns flex space (no dead zone above footer).
            GameObject body = new GameObject("Body");
            body.transform.SetParent(panel.transform, false);
            VerticalLayoutGroup bodyVlg = UI.AddVLG(body, spacing: UI.GapTight(s), padding: UI.Pad(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.HairGapRef, s));
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.childControlHeight = true;
            UI.AddLE(body, flexibleHeight: 1f, flexibleWidth: 1f, minHeight: 200f * s);

            // Presets + recipes always shown. Category picks live in list only (no type chips).
            BuildStripKeepPresetsCollapseSection(body.transform, footerBtnH, font, s);

            BuildStripKeepToolbarRows(body.transform, footerBtnH, font, s);
            BuildStripKeepShortcutHelpRow(body.transform, footerBtnH, font, s);
            BuildStripKeepCreateOptionsSection(body.transform, footerBtnH, font, s);

            GameObject scrollHost = new GameObject("ScrollHost");
            scrollHost.transform.SetParent(body.transform, false);
            _stripKeepScrollHostLe = UI.AddLE(scrollHost, flexibleHeight: 1f, minHeight: 80f * s, flexibleWidth: 1f);
            UI.AddImage(scrollHost, StripKeepScrollBg);

            float sbW = GalleryUiDesignTokens.StripKeepFloatScrollBarWidthRef * s;
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollHost.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f * s, 4f * s);
            vpRt.offsetMax = new Vector2(-(4f * s + sbW), -4f * s);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup cv = UI.AddVLG(content, spacing: UI.GapTight(s), padding: UI.PadTight(s));
            cv.childForceExpandHeight = false;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _stripKeepListParent = content.transform;

            ScrollRect sr = scrollHost.AddComponent<ScrollRect>();
            sr.viewport = vpRt;
            sr.content = contentRt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            // Low step + inertia — avoid wheel jumping 0↔1 extremes (Unity Game Optimization: ScrollRect).
            sr.scrollSensitivity = VpbScrollTuning.Sensitivity(12f * s, s);
            sr.inertia = true;
            sr.decelerationRate = 0.135f;
            sr.verticalScrollbar = null;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            try
            {
                GameObject sbGo = UI.CreateScrollBar(scrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
                if (sbGo != null)
                {
                    Scrollbar sb = sbGo.GetComponent<Scrollbar>();
                    ScrollbarSync sync = sbGo.AddComponent<ScrollbarSync>();
                    sync.scrollRect = sr;
                    sync.scrollbar = sb;
                    sync.minSizePixels = 28f * s;
                }
            }
            catch { }

            RebuildStripKeepToggleRows();
            RefreshStripKeepDefault3PChrome();

            // Footer actions only — Keep/Remove counts live on Strip button tooltip + status.
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            GameObject footer = new GameObject("Footer");
            footer.transform.SetParent(panel.transform, false);
            UI.AddImage(footer, StripKeepFooterBarBg);
            UI.AddLE(footer, minHeight: footerH, preferredHeight: footerH, flexibleWidth: 1f, flexibleHeight: 0f);
            UI.AddHLG(
                footer, spacing: UI.GapTight(s), padding: UI.PadFloatFooter(s),
                childAlignment: TextAnchor.MiddleRight,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (footer.GetComponent<RectMask2D>() == null)
                footer.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind Cancel/Strip/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(footer);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _stripKeepPanelRT;
                footerDrag.OnMoved = StripKeepOnPanelMoved;
            }

            // Flex spacer pushes actions right; drag hit for empty footer area.
            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(footer.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f * s);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = _stripKeepPanelRT;
            spacerDrag.OnMoved = StripKeepOnPanelMoved;

            float cancelW = GalleryUiDesignTokens.StripKeepFloatFooterCancelBtnWRef * s;
            float confirmW = GalleryUiDesignTokens.StripKeepFloatFooterConfirmBtnWRef * s;

            GameObject cancelGo = StripKeepChromeButton(footer.transform, cancelW, chromeSz,
                VPBTranslation.T("gallery.creator.strip_keep_cancel", "Cancel"), font, s,
                StripKeepCancelBg, OnStripKeepCancelOrBackClicked, flexibleWidth: false);
            if (cancelGo != null)
                _stripKeepCancelBtnLabel = cancelGo.GetComponentInChildren<Text>(true);

            GameObject confirmGo = StripKeepChromeButton(footer.transform, confirmW, chromeSz,
                VPBTranslation.T("gallery.creator.strip_keep_confirm", "Strip Scene"), font, s,
                GalleryUiColorTokens.AccentDanger, OnStripKeepConfirmClicked, flexibleWidth: false);
            if (confirmGo != null)
            {
                _stripKeepConfirmBtn = confirmGo.GetComponent<Button>();
                _stripKeepConfirmImg = confirmGo.GetComponent<Image>();
                _stripKeepConfirmBtnLabel = confirmGo.GetComponentInChildren<Text>(true);
            }

            // Resize — right of Strip (Settings float pattern).
            _stripKeepResizeGO = UI.AddChildGOImage(
                footer, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chromeSz, chromeSz, Vector2.zero, rounded: true);
            _stripKeepResizeGO.name = "ResizeHandle";
            Image rhImg = _stripKeepResizeGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(_stripKeepResizeGO);
            try
            {
                Sprite rhChevron = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
                if (rhChevron != null)
                    UI.AddIconToButton(_stripKeepResizeGO, rhChevron, 5f * s, UI.IconButtonBackdrop);
            }
            catch { }
            UI.AddLE(
                _stripKeepResizeGO,
                preferredWidth: chromeSz, preferredHeight: chromeSz,
                minWidth: chromeSz, minHeight: chromeSz,
                flexibleWidth: 0f, flexibleHeight: 0f);
            var resizer = _stripKeepResizeGO.AddComponent<UIFloatPanelResize>();
            resizer.Target = _stripKeepPanelRT;
            resizer.GetMinSize = StripKeepPanelMinSizeScaled;
            resizer.GetMaxSize = StripKeepPanelMaxSizeScaled;
            // Live wrap while dragging; persist + final wrap on release.
            resizer.OnResizing = StripKeepPresetChipFlowNow;
            resizer.OnResized = StripKeepOnPanelResized;
            AddTooltipPlain(_stripKeepResizeGO,
                VPBTranslation.T("gallery.creator.strip_resize_tip", "Drag to resize"));

            // Block clicks through panel into gallery
            Image panelHit = panel.GetComponent<Image>();
            if (panelHit != null) panelHit.raycastTarget = true;

            RefreshStripKeepSummaryAndConfirm();
            _stripKeepBuiltChromeScale = s;
            try
            {
                GameObject layerSrc = StripKeepResolveUiHost();
                if (layerSrc != null)
                    SetLayerRecursive(_stripKeepModalRoot, layerSrc.layer);
            }
            catch { }
            ScheduleStripKeepSnugPanelHeight();
            try
            {
                if (_stripKeepPanelRT != null)
                    UI.ApplyFloatRootHoverPolicy(_stripKeepPanelRT.gameObject);
            }
            catch { }
        }

        private CreatorStripKeepKind StripKeepPresentKindsMask()
        {
            CreatorStripKeepKind mask = CreatorStripKeepKind.None;
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                CreatorStripKeepKind k = order[i];
                int idx = StripKeepKindIndex(k);
                if (idx >= 0 && _stripKeepCounts[idx] > 0)
                    mask |= k;
            }
            if (mask == CreatorStripKeepKind.None)
                mask = SceneUtils.CreatorStripKeepDefault;
            return mask;
        }

        /// <summary>
        /// External 3P create row — pinned above list (not inside Lights expand). Visible when lights stripped.
        /// </summary>
        private void BuildStripKeepDefault3PRow(Transform parent, float btnH, int font, float s)
        {
            _stripKeepDefault3PHost = new GameObject("Default3PRow");
            _stripKeepDefault3PHost.transform.SetParent(parent, false);
            UI.AddLE(_stripKeepDefault3PHost, minHeight: btnH, preferredHeight: btnH, flexibleWidth: 1f, flexibleHeight: 0f);
            UI.AddImage(_stripKeepDefault3PHost, GalleryUiColorTokens.SurfaceDarker);
            HorizontalLayoutGroup hlg = UI.AddHLG(
                _stripKeepDefault3PHost, spacing: UI.GapTight(s), padding: UI.PadHV(GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.HairGapRef, s),
                childForceExpandWidth: false, childForceExpandHeight: false,
                childControlWidth: true, childControlHeight: true,
                childAlignment: TextAnchor.MiddleLeft);

            GameObject toggleHost = new GameObject("ToggleHost");
            toggleHost.transform.SetParent(_stripKeepDefault3PHost.transform, false);
            if (toggleHost.GetComponent<RectTransform>() == null) toggleHost.AddComponent<RectTransform>();
            UI.AddLE(toggleHost, flexibleWidth: 1f, minWidth: 40f * s, minHeight: btnH, preferredHeight: btnH, flexibleHeight: 0f);

            string label = VPBTranslation.T(
                "gallery.creator.strip_default_3p",
                "Default 3P Lights (create)");
            _stripKeepSuppressToggleEvents = true;
            GameObject toggleGo = UI.CreateUIToggle(
                toggleHost, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll,
                OnStripKeepDefault3PToggle);
            StripKeepFillInParent(toggleGo);
            _stripKeepDefault3PToggle = toggleGo.GetComponent<Toggle>();
            if (_stripKeepDefault3PToggle != null)
                _stripKeepDefault3PToggle.isOn = _stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid);
            _stripKeepSuppressToggleEvents = false;

            Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
            if (toggleLabel != null)
            {
                GalleryUiMetrics.ApplyFont(toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                toggleLabel.raycastTarget = false;
                StripKeepClampText(toggleLabel);
                toggleLabel.color = GalleryUiColorTokens.TextMuted;
                RectTransform lrt = toggleLabel.GetComponent<RectTransform>();
                if (lrt != null)
                {
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(35f, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
            }
            if (toggleHost.GetComponent<RectMask2D>() == null)
                toggleHost.AddComponent<RectMask2D>();

            AddTooltipPlain(_stripKeepDefault3PHost,
                VPBTranslation.T(
                    "gallery.creator.strip_default_3p_tip",
                    "Create MeshedVR 3-point lights after strip when scene lights are removed. Exclusive with Import SubScene."));
        }

        private void OnStripKeepDefault3PToggle(bool on)
        {
            if (_stripKeepSuppressToggleEvents) return;
            StripKeepClearSoftConfirm();
            if (on)
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
                _stripKeepSelectedUids.Add(StripKeepSyntheticDefault3PUid);
                _stripKeepCreateFillMode = StripKeepCreateFillMode.Default3P;
                PersistStripKeepCreateOptionsToConfig();
                RefreshStripKeepImportSubSceneToggleOnly();
            }
            else
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                if (!_stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
                    _stripKeepCreateFillMode = StripKeepCreateFillMode.None;
                PersistStripKeepCreateOptionsToConfig();
            }
            RefreshStripKeepSummaryAndConfirm();
        }

        private void RefreshStripKeepDefault3PChrome()
        {
            RefreshStripKeepCreateOptionsChrome();
        }

        private void RebuildStripKeepToggleRows()
        {
            if (_stripKeepListParent == null) return;
            float s = ChromeScale;
            int font = new GalleryModalTypography(s).Body;
            float rowH = GalleryUiDesignTokens.ButtonSizeRef * s;
            float expandW = rowH;

            UI.DestroyAllChildren(_stripKeepListParent);
            _stripKeepCatToggles.Clear();
            _stripKeepCatLabels.Clear();
            _stripKeepExpandGlyphs.Clear();
            StripKeepClearNav();

            bool flat = _stripKeepSortMode != StripKeepSortMode.TypeThenName;
            bool filtering = !string.IsNullOrEmpty(_stripKeepFilterLower)
                || _stripKeepViewFilter != StripKeepViewFilter.All;
            bool any = false;

            if (flat)
            {
                // Flat expert density: ignore category expand; list all visible items in sort order.
                for (int i = 0; i < _stripKeepItems.Count; i++)
                {
                    StripKeepItem it = _stripKeepItems[i];
                    if (!StripKeepItemVisible(it)) continue;
                    any = true;
                    AddStripKeepFlatItemRow(it, rowH, expandW, font, s);
                }
            }
            else
            {
                CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
                for (int i = 0; i < order.Length; i++)
                {
                    CreatorStripKeepKind kind = order[i];
                    int idx = StripKeepKindIndex(kind);
                    int count = idx >= 0 ? _stripKeepCounts[idx] : 0;
                    bool alwaysShow = !filtering
                        && (kind == CreatorStripKeepKind.Persons || kind == CreatorStripKeepKind.Lights);
                    if (filtering)
                    {
                        if (!StripKeepKindHasVisibleItems(kind)) continue;
                    }
                    else if (!alwaysShow && count <= 0)
                    {
                        continue;
                    }
                    any = true;
                    AddStripKeepCategoryRow(kind, count, rowH, expandW, font, s);

                    if (count > 0 && _stripKeepExpanded.Contains(kind))
                        AddStripKeepItemRowsForKind(kind, rowH, expandW, font, s);
                }
            }

            if (!any)
            {
                string emptyMsg = filtering
                    ? VPBTranslation.T("gallery.creator.strip_keep_filter_empty", "No atoms match filter / view.")
                    : VPBTranslation.T("gallery.creator.strip_keep_empty", "No keepable atoms in scene.");
                Text empty = UI.CreateLabel(_stripKeepListParent.gameObject,
                    emptyMsg,
                    font, new Color(0.7f, 0.7f, 0.72f, 1f), TextAnchor.MiddleLeft,
                    HorizontalWrapMode.Wrap, name: "Empty");
                GalleryUiMetrics.ApplyFont(empty, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                UI.AddLE(empty.gameObject, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
            }

            if (_stripKeepNavIndex >= _stripKeepNav.Count)
                _stripKeepNavIndex = _stripKeepNav.Count - 1;
            StripKeepApplyNavHighlight();
        }

        /// <summary>Flat-row variant used by name/type/kept sorts (no category chrome).</summary>
        private void AddStripKeepFlatItemRow(StripKeepItem it, float rowH, float expandW, int font, float s)
        {
            float itemH = rowH * 0.92f;
            float renameW = itemH;

            GameObject row = new GameObject("KeepItem_" + it.Uid);
            row.transform.SetParent(_stripKeepListParent, false);
            UI.AddLE(row, minHeight: itemH, preferredHeight: itemH, flexibleWidth: 1f);
            Color baseBg = new Color(0.07f, 0.075f, 0.09f, 1f);
            Image rowBg = UI.AddImage(row, baseBg);
            if (rowBg != null) rowBg.raycastTarget = false;
            StripKeepRegisterNav(false, it.Kind, it.Uid, rowBg, baseBg);
            HorizontalLayoutGroup hlg = UI.AddHLG(row, spacing: UI.GapTight(s), padding: UI.PadHair(s), childForceExpandWidth: false);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            string renameTo = null;
            bool hasRename = !it.Synthetic
                && _stripKeepRenames.TryGetValue(it.Uid, out renameTo)
                && !string.IsNullOrEmpty(renameTo)
                && !string.Equals(renameTo, it.Uid, StringComparison.Ordinal);
            string kindTag = StripKeepTypeChipShortLabel(it.Kind);
            string typeSuffix = it.Synthetic
                ? "  ·  create"
                : (string.IsNullOrEmpty(it.Type) ? "" : "  ·  " + it.Type);
            string label;
            if (hasRename)
                label = "[" + kindTag + "] " + renameTo + "  ←  " + it.Name + typeSuffix;
            else
                label = "[" + kindTag + "] " + it.Name + typeSuffix;
            bool isOn = _stripKeepSelectedUids.Contains(it.Uid);
            string uid = it.Uid;

            GameObject toggleHost = new GameObject("ItemToggleHost");
            toggleHost.transform.SetParent(row.transform, false);
            if (toggleHost.GetComponent<RectTransform>() == null) toggleHost.AddComponent<RectTransform>();
            UI.AddLE(toggleHost, flexibleWidth: 1f, minWidth: 40f * s, minHeight: itemH, preferredHeight: itemH, flexibleHeight: 0f);

            _stripKeepSuppressToggleEvents = true;
            GameObject toggleGo = UI.CreateUIToggle(
                toggleHost, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll,
                (on) => OnStripKeepItemToggle(uid, on));
            StripKeepFillInParent(toggleGo);
            Toggle toggle = toggleGo.GetComponent<Toggle>();
            if (toggle != null)
                toggle.isOn = isOn;
            _stripKeepSuppressToggleEvents = false;

            Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
            if (toggleLabel != null)
            {
                GalleryUiMetrics.ApplyFont(toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                toggleLabel.raycastTarget = false;
                StripKeepClampText(toggleLabel);
                toggleLabel.color = it.Synthetic
                    ? new Color(0.55f, 0.82f, 0.95f, 1f)
                    : (hasRename
                        ? new Color(0.92f, 0.78f, 0.45f, 1f)
                        : new Color(0.78f, 0.80f, 0.84f, 1f));
                RectTransform lrt = toggleLabel.GetComponent<RectTransform>();
                if (lrt != null)
                {
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(35f, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
            }
            if (toggleHost.GetComponent<RectMask2D>() == null)
                toggleHost.AddComponent<RectMask2D>();

            if (it.Synthetic) return;

            string capturedUid = uid;
            GameObject renameBtn = UI.CreateUIButton(
                row, renameW, itemH, "", font, 0, 0, AnchorPresets.middleLeft,
                () => OpenStripKeepRenameOverlay(capturedUid));
            renameBtn.name = "Rename_" + uid;
            StripKeepFixedLayoutChild(renameBtn, renameW, itemH);
            Image renameBg = renameBtn.GetComponent<Image>();
            if (renameBg != null)
                renameBg.color = hasRename
                    ? new Color(0.32f, 0.26f, 0.14f, 1f)
                    : new Color(0.14f, 0.15f, 0.18f, 1f);
            UI.AddLE(renameBtn, minWidth: renameW, preferredWidth: renameW, flexibleWidth: 0f, minHeight: itemH, preferredHeight: itemH, flexibleHeight: 0f);
            Text renameGlyph = renameBtn.GetComponentInChildren<Text>();
            if (renameGlyph != null)
            {
                renameGlyph.text = "✎";
                renameGlyph.alignment = TextAnchor.MiddleCenter;
                renameGlyph.raycastTarget = false;
                renameGlyph.color = hasRename
                    ? new Color(0.95f, 0.82f, 0.45f, 1f)
                    : new Color(0.72f, 0.74f, 0.78f, 1f);
                GalleryUiMetrics.ApplyFont(renameGlyph, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            try
            {
                Color tint = hasRename
                    ? new Color(0.95f, 0.82f, 0.45f, 1f)
                    : new Color(0.78f, 0.80f, 0.86f, 1f);
                Sprite spr = UI.LoadIconSprite("pencil-check", tint);
                if (spr != null)
                {
                    UI.AddIconToButton(renameBtn, spr, padding: 6f * s);
                    if (renameGlyph != null) renameGlyph.text = "";
                }
            }
            catch { }
            AddTooltipPlain(renameBtn,
                VPBTranslation.T("gallery.creator.strip_rename_tip",
                    "Set name used in the rebuilt scene (does not rename live atom until Strip)."));
        }

        private static void StripKeepFillInParent(GameObject go)
        {
            if (go == null) return;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            // Inside a sized host: stretch fill with zero sizeDelta so hit box == visible row.
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        /// <summary>
        /// Fixed-size HLG child: left-middle anchors + explicit size (no stretchAll overshoot).
        /// </summary>
        private static void StripKeepFixedLayoutChild(GameObject go, float width, float height)
        {
            if (go == null) return;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
            rt.localScale = Vector3.one;
        }

        private void AddStripKeepCategoryRow(
            CreatorStripKeepKind kind, int count, float rowH, float expandW, int font, float s)
        {
            int selected;
            int total;
            CountStripKeepKindSelection(kind, out selected, out total);

            GameObject row = new GameObject("KeepCat_" + kind);
            row.transform.SetParent(_stripKeepListParent, false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
            Color baseBg = count > 0
                ? new Color(0.10f, 0.11f, 0.14f, 1f)
                : new Color(0.08f, 0.08f, 0.10f, 1f);
            Image rowBg = UI.AddImage(row, baseBg);
            if (rowBg != null) rowBg.raycastTarget = false;
            StripKeepRegisterNav(true, kind, null, rowBg, baseBg);
            HorizontalLayoutGroup hlg = UI.AddHLG(row, spacing: UI.GapTight(s), padding: UI.PadHair(s), childForceExpandWidth: false);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            CreatorStripKeepKind captured = kind;
            bool canExpand = count > 0;
            bool expanded = canExpand && _stripKeepExpanded.Contains(kind);

            // Expand affordance: chevron-right / chevron-down (shared tree language).
            GameObject expandBtn = UI.CreateUIButton(
                row, expandW, rowH, "", font, 0, 0, AnchorPresets.middleLeft,
                canExpand ? (UnityAction)(() => ToggleStripKeepExpand(captured)) : null);
            expandBtn.name = "Expand_" + kind;
            StripKeepFixedLayoutChild(expandBtn, expandW, rowH);
            UI.AddLE(expandBtn, minWidth: expandW, preferredWidth: expandW, flexibleWidth: 0f, minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0f);
            UI.ApplyTreeRowExpandIcon(expandBtn, canExpand, expanded, s, transparentWhenEmpty: false);
            Text expandGlyph = expandBtn.GetComponentInChildren<Text>(true);
            if (expandGlyph != null)
            {
                GalleryUiMetrics.ApplyFont(expandGlyph, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                _stripKeepExpandGlyphs[kind] = expandGlyph;
            }

            string labelKey = "gallery.creator.strip_kind_" + kind.ToString().ToLowerInvariant();
            string labelBase = VPBTranslation.T(labelKey, SceneUtils.CreatorStripKeepKindLabel(kind));
            string label;
            if (total <= 0)
                label = string.Format("{0}  (0)", labelBase);
            else if (selected == total)
                label = string.Format("{0}  ({1})", labelBase, total);
            else
                label = string.Format("{0}  ({1}/{2})", labelBase, selected, total);

            GameObject toggleHost = new GameObject("CatToggleHost");
            toggleHost.transform.SetParent(row.transform, false);
            if (toggleHost.GetComponent<RectTransform>() == null) toggleHost.AddComponent<RectTransform>();
            UI.AddLE(toggleHost, flexibleWidth: 1f, minWidth: 40f * s, minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0f);

            _stripKeepSuppressToggleEvents = true;
            GameObject toggleGo = UI.CreateUIToggle(
                toggleHost, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll,
                (on) => OnStripKeepCategoryToggle(captured, on));
            StripKeepFillInParent(toggleGo);
            Toggle toggle = toggleGo.GetComponent<Toggle>();
            if (toggle != null)
            {
                // On only when all selected; partial/none → off (click selects all).
                toggle.isOn = total > 0 && selected == total;
                _stripKeepCatToggles[kind] = toggle;
            }
            _stripKeepSuppressToggleEvents = false;

            Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
            if (toggleLabel != null)
            {
                GalleryUiMetrics.ApplyFont(toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                // Label must not steal raycasts with its own oversized stretch rect.
                toggleLabel.raycastTarget = false;
                StripKeepClampText(toggleLabel);
                if (total <= 0)
                    toggleLabel.color = new Color(0.55f, 0.56f, 0.58f, 1f);
                else if (selected > 0 && selected < total)
                    toggleLabel.color = new Color(0.92f, 0.78f, 0.45f, 1f); // partial = amber cue
                _stripKeepCatLabels[kind] = toggleLabel;
                RectTransform lrt = toggleLabel.GetComponent<RectTransform>();
                if (lrt != null)
                {
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(35f, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
            }
            if (toggleHost.GetComponent<RectMask2D>() == null)
                toggleHost.AddComponent<RectMask2D>();
        }

        private void AddStripKeepItemRowsForKind(
            CreatorStripKeepKind kind, float rowH, float expandW, int font, float s)
        {
            float itemH = rowH * 0.92f;
            float renameW = itemH;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != kind) continue;
                if (!StripKeepItemVisible(it)) continue;

                GameObject row = new GameObject("KeepItem_" + it.Uid);
                row.transform.SetParent(_stripKeepListParent, false);
                UI.AddLE(row, minHeight: itemH, preferredHeight: itemH, flexibleWidth: 1f);
                Color baseBg = new Color(0.07f, 0.075f, 0.09f, 1f);
                Image rowBg = UI.AddImage(row, baseBg);
                if (rowBg != null) rowBg.raycastTarget = false;
                StripKeepRegisterNav(false, kind, it.Uid, rowBg, baseBg);
                HorizontalLayoutGroup hlg = UI.AddHLG(row, spacing: UI.GapTight(s), padding: UI.PadHair(s), childForceExpandWidth: false);
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandHeight = true;

                // Indent under category expand column.
                GameObject spacer = new GameObject("Indent");
                spacer.transform.SetParent(row.transform, false);
                if (spacer.GetComponent<RectTransform>() == null) spacer.AddComponent<RectTransform>();
                UI.AddLE(spacer, minWidth: expandW, preferredWidth: expandW, flexibleWidth: 0f, minHeight: itemH, preferredHeight: itemH, flexibleHeight: 0f);

                string renameTo = null;
                bool hasRename = !it.Synthetic
                    && _stripKeepRenames.TryGetValue(it.Uid, out renameTo)
                    && !string.IsNullOrEmpty(renameTo)
                    && !string.Equals(renameTo, it.Uid, StringComparison.Ordinal);
                string typeSuffix = it.Synthetic
                    ? "  ·  create"
                    : (string.IsNullOrEmpty(it.Type) ? "" : "  ·  " + it.Type);
                string label;
                if (hasRename)
                    label = renameTo + "  ←  " + it.Name + typeSuffix;
                else
                    label = it.Name + typeSuffix;
                bool isOn = _stripKeepSelectedUids.Contains(it.Uid);
                string uid = it.Uid;

                GameObject toggleHost = new GameObject("ItemToggleHost");
                toggleHost.transform.SetParent(row.transform, false);
                if (toggleHost.GetComponent<RectTransform>() == null) toggleHost.AddComponent<RectTransform>();
                UI.AddLE(toggleHost, flexibleWidth: 1f, minWidth: 40f * s, minHeight: itemH, preferredHeight: itemH, flexibleHeight: 0f);

                _stripKeepSuppressToggleEvents = true;
                GameObject toggleGo = UI.CreateUIToggle(
                    toggleHost, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll,
                    (on) => OnStripKeepItemToggle(uid, on));
                StripKeepFillInParent(toggleGo);
                Toggle toggle = toggleGo.GetComponent<Toggle>();
                if (toggle != null)
                    toggle.isOn = isOn;
                _stripKeepSuppressToggleEvents = false;

                Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
                if (toggleLabel != null)
                {
                    GalleryUiMetrics.ApplyFont(toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                    toggleLabel.raycastTarget = false;
                    StripKeepClampText(toggleLabel);
                    toggleLabel.color = it.Synthetic
                        ? new Color(0.55f, 0.82f, 0.95f, 1f)
                        : (hasRename
                            ? new Color(0.92f, 0.78f, 0.45f, 1f)
                            : new Color(0.78f, 0.80f, 0.84f, 1f));
                    RectTransform lrt = toggleLabel.GetComponent<RectTransform>();
                    if (lrt != null)
                    {
                        lrt.anchorMin = Vector2.zero;
                        lrt.anchorMax = Vector2.one;
                        lrt.offsetMin = new Vector2(35f, 0f);
                        lrt.offsetMax = new Vector2(-4f * s, 0f);
                    }
                }
                if (toggleHost.GetComponent<RectMask2D>() == null)
                    toggleHost.AddComponent<RectMask2D>();

                if (it.Synthetic) continue;

                // Trailing rename — separate hit target from keep toggle (Fitts / risk separation).
                string capturedUid = uid;
                GameObject renameBtn = UI.CreateUIButton(
                    row, renameW, itemH, "", font, 0, 0, AnchorPresets.middleLeft,
                    () => OpenStripKeepRenameOverlay(capturedUid));
                renameBtn.name = "Rename_" + uid;
                StripKeepFixedLayoutChild(renameBtn, renameW, itemH);
                Image renameBg = renameBtn.GetComponent<Image>();
                if (renameBg != null)
                    renameBg.color = hasRename
                        ? new Color(0.32f, 0.26f, 0.14f, 1f)
                        : new Color(0.14f, 0.15f, 0.18f, 1f);
                UI.AddLE(renameBtn, minWidth: renameW, preferredWidth: renameW, flexibleWidth: 0f, minHeight: itemH, preferredHeight: itemH, flexibleHeight: 0f);
                Text renameGlyph = renameBtn.GetComponentInChildren<Text>();
                if (renameGlyph != null)
                {
                    renameGlyph.text = "✎";
                    renameGlyph.alignment = TextAnchor.MiddleCenter;
                    renameGlyph.raycastTarget = false;
                    renameGlyph.color = hasRename
                        ? new Color(0.95f, 0.82f, 0.45f, 1f)
                        : new Color(0.72f, 0.74f, 0.78f, 1f);
                    GalleryUiMetrics.ApplyFont(renameGlyph, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                }
                try
                {
                    Color tint = hasRename
                        ? new Color(0.95f, 0.82f, 0.45f, 1f)
                        : new Color(0.78f, 0.80f, 0.86f, 1f);
                    Sprite spr = UI.LoadIconSprite("pencil-check", tint);
                    if (spr != null)
                    {
                        UI.AddIconToButton(renameBtn, spr, padding: 6f * s);
                        if (renameGlyph != null) renameGlyph.text = "";
                    }
                }
                catch { }
                AddTooltipPlain(renameBtn,
                    VPBTranslation.T("gallery.creator.strip_rename_tip",
                        "Set name used in the rebuilt scene (does not rename live atom until Strip)."));
            }
        }
        private void ToggleStripKeepExpand(CreatorStripKeepKind kind)
        {
            if (_stripKeepExpanded.Contains(kind))
                _stripKeepExpanded.Remove(kind);
            else
                _stripKeepExpanded.Add(kind);
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void OnStripKeepCategoryToggle(CreatorStripKeepKind kind, bool on)
        {
            if (_stripKeepSuppressToggleEvents) return;

            // Ctrl+click category = expand only (no select-all).
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl)
            {
                SyncStripKeepCategoryToggle(kind);
                StripKeepExpandOnlyKind(kind);
                return;
            }

            SetStripKeepKindSelection(kind, on);
            if (on) _stripKeepMask |= kind;
            else _stripKeepMask &= ~kind;

            // Lights kept but none in scene → auto-offer create fill (3P or saved SubScene).
            if (kind == CreatorStripKeepKind.Lights && on && !StripKeepHasAnyRealLightAtoms())
                StripKeepSeedCreateFillWhenNoLights();

            RecalcStripKeepTotalsFromSelection();
            // Refresh labels / partial tint without full rebuild when collapsed.
            if (_stripKeepExpanded.Contains(kind))
                RebuildStripKeepToggleRows();
            else
                RefreshStripKeepCategoryLabel(kind);
            RefreshStripKeepDefault3PChrome();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void OnStripKeepItemToggle(string uid, bool on)
        {
            if (_stripKeepSuppressToggleEvents) return;
            if (string.IsNullOrEmpty(uid)) return;
            StripKeepClearSoftConfirm();
            if (on) _stripKeepSelectedUids.Add(uid);
            else _stripKeepSelectedUids.Remove(uid);

            CreatorStripKeepKind kind = CreatorStripKeepKind.Other;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                if (_stripKeepItems[i].Uid == uid)
                {
                    kind = _stripKeepItems[i].Kind;
                    break;
                }
            }

            int selected;
            int total;
            CountStripKeepKindSelection(kind, out selected, out total);
            if (selected == total && total > 0) _stripKeepMask |= kind;
            else if (selected == 0) _stripKeepMask &= ~kind;

            RecalcStripKeepTotalsFromSelection();
            SyncStripKeepCategoryToggle(kind);
            RefreshStripKeepCategoryLabel(kind);
            if (_stripKeepSortMode == StripKeepSortMode.KeptFirst)
            {
                StripKeepResortItems();
                RebuildStripKeepToggleRows();
            }
            RefreshStripKeepDefault3PChrome();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void SyncStripKeepCategoryToggle(CreatorStripKeepKind kind)
        {
            Toggle t;
            if (!_stripKeepCatToggles.TryGetValue(kind, out t) || t == null) return;
            int selected;
            int total;
            CountStripKeepKindSelection(kind, out selected, out total);
            bool want = total > 0 && selected == total;
            if (t.isOn == want) return;
            _stripKeepSuppressToggleEvents = true;
            t.isOn = want;
            _stripKeepSuppressToggleEvents = false;
        }

        private void RefreshStripKeepCategoryLabel(CreatorStripKeepKind kind)
        {
            Text label;
            if (!_stripKeepCatLabels.TryGetValue(kind, out label) || label == null) return;
            int selected;
            int total;
            CountStripKeepKindSelection(kind, out selected, out total);
            string labelKey = "gallery.creator.strip_kind_" + kind.ToString().ToLowerInvariant();
            string labelBase = VPBTranslation.T(labelKey, SceneUtils.CreatorStripKeepKindLabel(kind));
            if (total <= 0)
            {
                label.text = string.Format("{0}  (0)", labelBase);
                label.color = new Color(0.55f, 0.56f, 0.58f, 1f);
            }
            else if (selected == total)
            {
                label.text = string.Format("{0}  ({1})", labelBase, total);
                label.color = Color.white;
            }
            else
            {
                label.text = string.Format("{0}  ({1}/{2})", labelBase, selected, total);
                label.color = new Color(0.92f, 0.78f, 0.45f, 1f);
            }
        }

        private void StripKeepApplyPreset(CreatorStripKeepKind mask)
        {
            _stripKeepMask = mask & SceneUtils.CreatorStripKeepAllUser;
            SeedStripKeepSelectionFromMask();
            RecalcStripKeepTotalsFromSelection();
            RebuildStripKeepToggleRows();
            RefreshStripKeepDefault3PChrome();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void RefreshStripKeepSummaryAndConfirm()
        {
            string summary = BuildStripKeepSummaryLine();
            string rules = VPBTranslation.T(
                "gallery.creator.strip_rules_hint",
                "Sky blanked · Environment removed · CoreControl kept · More: possess / rename");
            string tip = string.IsNullOrEmpty(summary) ? rules : (summary + "\n" + rules);

            if (_stripKeepConfirmBtn != null)
                AddTooltipPlain(_stripKeepConfirmBtn.gameObject, tip);

            // Soft-confirm / count feedback — status strip (footer stays buttons-only).
            try
            {
                if (_stripKeepAwaitingSoftConfirm || _stripKeepDropCount > 0)
                    SetStatus(summary);
            }
            catch { }

            bool canStrip = _stripKeepDropCount > 0;
            // Always clickable — explain-why when empty (no silent disabled primary).
            if (_stripKeepConfirmBtn != null)
                _stripKeepConfirmBtn.interactable = true;
            if (_stripKeepConfirmImg != null)
            {
                if (_stripKeepAwaitingSoftConfirm)
                    _stripKeepConfirmImg.color = GalleryUiColorTokens.AccentDangerStrong;
                else if (canStrip)
                    _stripKeepConfirmImg.color = GalleryUiColorTokens.AccentDanger;
                else
                    _stripKeepConfirmImg.color = GalleryUiColorTokens.SurfaceMid;
            }
            if (_stripKeepConfirmBtnLabel != null)
            {
                _stripKeepConfirmBtnLabel.text = _stripKeepAwaitingSoftConfirm
                    ? VPBTranslation.T("gallery.creator.strip_keep_confirm_yes", "Confirm")
                    : VPBTranslation.T("gallery.creator.strip_keep_confirm", "Strip Scene");
            }
            if (_stripKeepCancelBtnLabel != null)
            {
                _stripKeepCancelBtnLabel.text = _stripKeepAwaitingSoftConfirm
                    ? VPBTranslation.T("gallery.creator.strip_keep_back", "Back")
                    : VPBTranslation.T("gallery.creator.strip_keep_cancel", "Cancel");
            }
        }

        private string BuildStripKeepSummaryLine()
        {
            int renameCount = 0;
            if (_stripKeepRenames.Count > 0)
            {
                Dictionary<string, string>.Enumerator en = _stripKeepRenames.GetEnumerator();
                while (en.MoveNext())
                {
                    string k = en.Current.Key;
                    string v = en.Current.Value;
                    if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v)) continue;
                    if (!string.Equals(k, v, StringComparison.Ordinal))
                        renameCount++;
                }
            }

            if (_stripKeepAwaitingSoftConfirm)
            {
                return string.Format(
                    VPBTranslation.T(
                        "gallery.creator.strip_keep_summary_confirm",
                        "Confirm: remove {0} atom(s)? Enter again or Confirm."),
                    _stripKeepDropCount);
            }
            if (renameCount > 0)
            {
                return string.Format(
                    VPBTranslation.T(
                        "gallery.creator.strip_keep_summary_renames",
                        "Keep {0}  ·  Remove {1}  ·  Renames {2}"),
                    _stripKeepKeepCount, _stripKeepDropCount, renameCount)
                    + StripKeepPossessSummarySuffix()
                    + StripKeepCreateOptionsSummarySuffix();
            }
            return string.Format(
                VPBTranslation.T(
                    "gallery.creator.strip_keep_summary",
                    "Keep {0}  ·  Remove {1}"),
                _stripKeepKeepCount, _stripKeepDropCount)
                + StripKeepPossessSummarySuffix()
                + StripKeepCreateOptionsSummarySuffix();
        }

        private void OnStripKeepCancelOrBackClicked()
        {
            if (_stripKeepAwaitingSoftConfirm)
            {
                _stripKeepAwaitingSoftConfirm = false;
                RefreshStripKeepSummaryAndConfirm();
                return;
            }
            ExitCreatorMode();
        }

        private void OnStripKeepConfirmClicked()
        {
            ConfirmStripKeepSelector();
        }

        private bool StripKeepNeedsSoftConfirm()
        {
            // Risk policy Medium: any drop requires second Confirm (no threshold lottery).
            return RiskPolicyStripNeedsSoftConfirm(_stripKeepDropCount);
        }

        private void ConfirmStripKeepSelector()
        {
            if (creatorModeStripBusy) return;
            SuperController scConfirm = SuperController.singleton;
            if (scConfirm == null)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.creator.no_sc", "Scene controller unavailable."), 1.5f);
                return;
            }
            bool sceneBusy = false;
            try { sceneBusy = scConfirm.isLoading; } catch { }
            try { if (!sceneBusy && LogUtil.IsSceneLoading()) sceneBusy = true; } catch { }
            if (sceneBusy)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_scene_busy",
                        "Scene still loading — wait, then strip again."),
                    2.5f);
                return;
            }
            RecalcStripKeepTotalsFromSelection();
            if (_stripKeepDropCount <= 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_none", "Nothing to strip — selection keeps everything."),
                    2f);
                return;
            }

            string clashMsg;
            if (!ValidateStripKeepRenames(out clashMsg))
            {
                ShowTemporaryStatus(clashMsg, 2.5f);
                return;
            }

            string createMsg;
            if (!StripKeepValidateCreateOptionsForConfirm(out createMsg))
            {
                ShowTemporaryStatus(createMsg, 2.5f);
                return;
            }

            if (!_stripKeepAwaitingSoftConfirm && StripKeepNeedsSoftConfirm())
            {
                _stripKeepAwaitingSoftConfirm = true;
                RefreshStripKeepSummaryAndConfirm();
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_soft_confirm",
                        "Strip removes atoms — Confirm / Enter again. Undo restores the scene after{hint:undo}."),
                    2.5f);
                return;
            }
            _stripKeepAwaitingSoftConfirm = false;

            // Re-apply rename mode so strip uses latest gender-aware names.
            if (_stripKeepPersonRenameMode != StripKeepPersonRenameMode.Off)
                StripKeepApplyPersonRenameModeToSelection(overwriteExisting: true);

            PersistStripKeepMaskFromSelection();
            PersistStripKeepPossessOptionsToConfig();
            PersistStripKeepCreateOptionsToConfig();
            try { StripKeepPersistLastRecipeFromSelection(); } catch { }

            bool spawnDefault3P = _stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid);
            string importSubScenePath = null;
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
                importSubScenePath = _stripKeepDefaultSubScenePath;
            bool applyPossess = StripKeepPossessOptionsActive();
            bool possClear = _stripKeepRemovePossessable;
            bool possMale = _stripKeepAddPossessableMale;
            bool possFemale = _stripKeepAddPossessableFemale;

            HashSet<string> keepUids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue;
                if (_stripKeepSelectedUids.Contains(it.Uid))
                    keepUids.Add(it.Uid);
            }

            Dictionary<string, string> renames = null;
            if (_stripKeepRenames.Count > 0)
            {
                renames = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in _stripKeepRenames)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                    if (string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)) continue;
                    // Only apply renames for atoms we keep.
                    if (!keepUids.Contains(kv.Key)) continue;
                    renames[kv.Key] = kv.Value;
                }
                if (renames.Count == 0) renames = null;
            }

            HideStripKeepSelector();

            if (creatorModeStripRoutine != null)
            {
                try { StopCoroutine(creatorModeStripRoutine); } catch { }
                creatorModeStripRoutine = null;
            }
            creatorModeStripRoutine = StartCoroutine(
                CreatorModeStripSceneRoutine(
                    keepUids, renames, spawnDefault3P, importSubScenePath,
                    applyPossess, possClear, possMale, possFemale));
        }

        private bool ValidateStripKeepRenames(out string errorMsg)
        {
            errorMsg = null;
            if (_stripKeepRenames.Count == 0) return true;

            HashSet<string> finalNames = new HashSet<string>(StringComparer.Ordinal);
            // System ids always reserved.
            finalNames.Add("CameraRig");

            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (!_stripKeepSelectedUids.Contains(it.Uid)) continue;
                string finalName = it.Uid;
                string renamed;
                if (_stripKeepRenames.TryGetValue(it.Uid, out renamed)
                    && !string.IsNullOrEmpty(renamed)
                    && !string.Equals(renamed, it.Uid, StringComparison.Ordinal))
                {
                    finalName = renamed;
                }
                if (SceneUtils.IsSystemProtectedAtomId(finalName)
                    && !string.Equals(finalName, it.Uid, StringComparison.Ordinal))
                {
                    errorMsg = VPBTranslation.T(
                        "gallery.creator.strip_rename_system",
                        "Rename cannot use a system atom name.");
                    return false;
                }
                if (!finalNames.Add(finalName))
                {
                    errorMsg = string.Format(
                        VPBTranslation.T(
                            "gallery.creator.strip_rename_clash",
                            "Rename clash: '{0}' used more than once."),
                        finalName);
                    return false;
                }
            }
            return true;
        }

        private string StripKeepFinalNameForUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return uid;
            string renamed;
            if (_stripKeepRenames.TryGetValue(uid, out renamed)
                && !string.IsNullOrEmpty(renamed)
                && !string.Equals(renamed, uid, StringComparison.Ordinal))
                return renamed;
            return uid;
        }

        private void HideStripKeepRenameOverlay()
        {
            if (_stripKeepRenameOverlayRoot != null)
            {
                try { UnityEngine.Object.Destroy(_stripKeepRenameOverlayRoot); } catch { }
                _stripKeepRenameOverlayRoot = null;
            }
        }

        private void OpenStripKeepRenameOverlay(string uid)
        {
            if (string.IsNullOrEmpty(uid) || _stripKeepModalRoot == null) return;
            if (string.Equals(uid, StripKeepSyntheticDefault3PUid, StringComparison.Ordinal)) return;
            if (string.Equals(uid, StripKeepSyntheticImportSubSceneUid, StringComparison.Ordinal)) return;
            HideStripKeepRenameOverlay();

            string currentName = StripKeepFinalNameForUid(uid);
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;

            GameObject panel;
            // Nested above keep modal — same host so Esc ladder still owns keep modal first after close.
            _stripKeepRenameOverlayRoot = UI.CreateModalChrome(
                _stripKeepModalRoot, "VPB_StripKeepRename",
                440f * s, 300f * s,
                new Color(0.10f, 0.11f, 0.13f, 1f),
                HideStripKeepRenameOverlay,
                out panel,
                dimAlpha: 0.45f);

            UI.AddVLG(panel, spacing: UI.GapControl(s), padding: UI.PadDialog(s));

            Text title = UI.CreateEmphasisTitleLabel(
                panel,
                VPBTranslation.T("gallery.creator.strip_rename_title", "Name in new scene"),
                font);
            GalleryUiMetrics.ApplyFont(title, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(title.gameObject, minHeight: btnH * 0.8f, preferredHeight: btnH * 0.8f);

            Text oldLabel = UI.CreateLabel(panel,
                string.Format(
                    VPBTranslation.T("gallery.creator.strip_rename_current", "Current: {0}"),
                    uid),
                font, new Color(0.70f, 0.72f, 0.76f, 1f), TextAnchor.MiddleLeft,
                name: "OldName");
            GalleryUiMetrics.ApplyFont(oldLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(oldLabel.gameObject, minHeight: btnH * 0.7f, preferredHeight: btnH * 0.7f);

            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(panel.transform, false);
            UI.AddImage(inputGO, new Color(0.18f, 0.19f, 0.22f, 1f));
            UI.AddLE(inputGO, minHeight: btnH, preferredHeight: btnH, flexibleWidth: 1f);
            InputField input = inputGO.AddComponent<InputField>();

            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRt = textArea.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(10f * s, 6f * s);
            textAreaRt.offsetMax = new Vector2(-10f * s, -6f * s);

            Text tComp = UI.CreateLabel(textArea, "", font, Color.white, TextAnchor.MiddleLeft, name: "Text");
            GalleryUiMetrics.ApplyFont(tComp, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            input.textComponent = tComp;
            input.text = currentName;
            try { inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input); } catch { }

            string capturedUid = uid;
            GameObject footer = new GameObject("RenameFooter");
            footer.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup fh = UI.AddHLG(footer, spacing: UI.GapControl(s), padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: true);
            fh.childForceExpandHeight = false;
            UI.AddLE(footer, minHeight: btnH, preferredHeight: btnH);

            StripKeepChromeButton(footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.creator.strip_rename_clear", "Clear"), font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f),
                () =>
                {
                    _stripKeepRenames.Remove(capturedUid);
                    HideStripKeepRenameOverlay();
                    RebuildStripKeepToggleRows();
                    RefreshStripKeepSummaryAndConfirm();
                },
                flexibleWidth: true);

            StripKeepChromeButton(footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.creator.strip_rename_cancel", "Cancel"), font, s,
                new Color(0.35f, 0.28f, 0.22f, 1f),
                HideStripKeepRenameOverlay,
                flexibleWidth: true);

            StripKeepChromeButton(footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.creator.strip_rename_apply", "Apply"), font, s,
                new Color(0.22f, 0.42f, 0.32f, 1f),
                () =>
                {
                    string newName = input != null ? input.text : null;
                    if (newName != null) newName = newName.Trim();
                    if (string.IsNullOrEmpty(newName))
                    {
                        ShowTemporaryStatus(VPBTranslation.T("gallery.rename.empty", "Enter a name."), 2f);
                        return;
                    }
                    if (string.Equals(newName, capturedUid, StringComparison.Ordinal))
                    {
                        _stripKeepRenames.Remove(capturedUid);
                        HideStripKeepRenameOverlay();
                        RebuildStripKeepToggleRows();
                        RefreshStripKeepSummaryAndConfirm();
                        return;
                    }
                    if (SceneUtils.IsSystemProtectedAtomId(newName))
                    {
                        ShowTemporaryStatus(
                            VPBTranslation.T("gallery.creator.strip_rename_system", "Rename cannot use a system atom name."),
                            2f);
                        return;
                    }
                    // Clash against other kept atoms' final names.
                    for (int i = 0; i < _stripKeepItems.Count; i++)
                    {
                        StripKeepItem other = _stripKeepItems[i];
                        if (string.Equals(other.Uid, capturedUid, StringComparison.Ordinal)) continue;
                        if (!_stripKeepSelectedUids.Contains(other.Uid)) continue;
                        if (string.Equals(StripKeepFinalNameForUid(other.Uid), newName, StringComparison.Ordinal))
                        {
                            ShowTemporaryStatus(
                                string.Format(
                                    VPBTranslation.T("gallery.creator.strip_rename_clash", "Rename clash: '{0}' used more than once."),
                                    newName),
                                2.5f);
                            return;
                        }
                    }

                    _stripKeepRenames[capturedUid] = newName;
                    // Selecting rename implies keep intent — auto-check atom.
                    if (!_stripKeepSelectedUids.Contains(capturedUid))
                    {
                        _stripKeepSelectedUids.Add(capturedUid);
                        RecalcStripKeepTotalsFromSelection();
                    }
                    HideStripKeepRenameOverlay();
                    RebuildStripKeepToggleRows();
                    RefreshStripKeepSummaryAndConfirm();
                },
                flexibleWidth: true);

            try
            {
                if (_stripKeepRenameOverlayRoot != null)
                    SetLayerRecursive(_stripKeepRenameOverlayRoot, _stripKeepModalRoot.layer);
            }
            catch { }
            try
            {
                input.ActivateInputField();
                input.MoveTextEnd(false);
            }
            catch { }
        }

        private static GameObject StripKeepChromeButton(
            Transform parent, float width, float height, string label, int font, float s,
            Color bg, UnityAction onClick, bool flexibleWidth = false)
        {
            // Layout-group button (not stretchAll CreateUIButton) — keeps chip widths honest.
            float w = flexibleWidth ? 0f : (width > 0f ? width : StripKeepChipWidth(label, font, s));
            GameObject go = UI.CreateChromeLayoutButton(parent, w, height, label, font, bg, onClick);
            StripKeepStyleChromeButtonText(go, s);
            // Inward hover rim — survives chip-strip RectMask2D + avoids sides-only clip.
            UIHoverBorder hb = go != null ? go.GetComponent<UIHoverBorder>() : null;
            if (hb != null)
            {
                hb.inward = true;
                try { hb.ApplyBorderSettings(); } catch { }
            }
            if (flexibleWidth)
            {
                LayoutElement le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minWidth = 64f * s;
                le.minHeight = height;
                le.preferredHeight = height;
            }
            return go;
        }
    }
}
