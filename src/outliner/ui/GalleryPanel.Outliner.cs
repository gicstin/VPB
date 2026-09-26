using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.Outliner;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        const int OutlinerPoolSize = 120;

        static readonly Color OutlinerPanelBg = GalleryUiColorTokens.SurfaceDeep;
        static readonly Color OutlinerBarBg = GalleryUiColorTokens.SurfaceDark;
        static readonly Color OutlinerPaneBg = GalleryUiColorTokens.SurfaceDarker;
        static readonly Color OutlinerRowIdle = GalleryUiColorTokens.RowIdle;
        static readonly Color OutlinerRowSelected = GalleryUiColorTokens.ActiveSelected;

        GameObject _outlinerRoot;
        RectTransform _outlinerPanelRT;
        RectTransform _outlinerTitleBarRT;
        GameObject _outlinerBodyGO;
        GameObject _outlinerFooterGO;
        Text _outlinerFooterStatusText;
        UIFloatPanelDrag _outlinerHeaderDrag;
        UIFloatPanelDrag _outlinerFooterDrag;
        UIFloatPanelDrag _outlinerFooterSpacerDrag;
        Text _outlinerTitleText;
        GameObject _outlinerCollapseBtn;
        Image _outlinerCollapseIcon;
        GameObject _outlinerTreePane;
        GameObject _outlinerInspectorPane;
        RectTransform _outlinerSplitHandleRT;
        GameObject _outlinerSplitBarGO;
        UIFloatPanelResize _outlinerFloatResizer;
        GameObject _outlinerResizeHandleGO;
        GameObject _outlinerLayoutBtn;
        OutlinerRailWidthDrag _outlinerRailWidthDrag;
        bool _outlinerCollapsed;
        bool _outlinerLastFocused;
        bool _outlinerRebuildQueued;
        string _outlinerVamObservedUid = "";
        int _outlinerVamSelectGrace;
        bool _outlinerListening;
        bool _outlinerStartupSettled;
        float _outlinerChromeScale = 1f;
        int _outlinerPollCounter;
        int _outlinerLastLiveRows;
        Vector2? _outlinerSavedPosCenter;
        Vector2? _outlinerSavedSizeRef;
        float? _outlinerExpandHeightRef;
        Vector2? _outlinerCollapsedTopLeftPos;
        string _outlinerPendingVamUid;
        bool _outlinerPendingVamLookAt;

        readonly OutlinerSelection _outlinerSelection = new OutlinerSelection();
        readonly OutlinerUndo _outlinerUndo = new OutlinerUndo();
        readonly HashSet<string> _outlinerExpanded = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _outlinerUserCollapsed = new HashSet<string>(StringComparer.Ordinal);
        readonly List<OutlinerAtomFacts> _outlinerFacts = new List<OutlinerAtomFacts>(128);
        readonly List<int> _outlinerVisible = new List<int>(128);
        readonly List<int> _outlinerVisibleDepths = new List<int>(128);
        OutlinerModel _outlinerModel;
        CreatorStripKeepKind _outlinerKindMask = CreatorStripKeepKind.None;
        CreatorStripKeepKind _outlinerPresentKinds = CreatorStripKeepKind.None;
        string _outlinerFooterShown;
        int _outlinerFooterColorKind = int.MinValue;
        string _outlinerFilter = "";
        string _outlinerDragParamKey = "";
        Dictionary<string, List<string>> _outlinerPinMap;

        internal bool IsSceneOutlinerOpen()
        {
            return _outlinerRoot != null && _outlinerRoot.activeSelf;
        }

        public void OpenSceneOutliner(bool forceShow = true)
        {
            if (!forceShow && IsSceneOutlinerOpen())
            {
                HideSceneOutliner();
                return;
            }
            if (canvas == null) return;
            RehostOutlinerIfNeeded();
            EnsureOutlinerBuilt();
            if (_outlinerRoot == null) { return; }
            if (_outlinerCollapsed) ToggleOutlinerCollapsed();
            _outlinerRoot.SetActive(true);
            try { _outlinerRoot.transform.SetAsLastSibling(); } catch { }
            BindOutlinerAtomEvents(true);
            if (VPBConfig.Instance != null) VPBConfig.Instance.OutlinerOpen = true;
            RescaleOutlinerIfOpen(ChromeScale);
            QueueOutlinerRebuild();
            ApplyOutlinerLayout();
            _outlinerLastFocused = true;
            RefreshTboxSceneOutlinerTint();
        }

        internal void ShowAtomInOutliner(string uid)
        {
            OpenSceneOutliner(true);
            if (string.IsNullOrEmpty(uid)) return;
            _outlinerSelection.SelectOnly(uid);
            _outlinerExpanded.Add("scene");
            _outlinerExpanded.Add("atom:" + uid);
            QueueOutlinerRebuild();
            Atom atom = OutlinerEdits.GetAtom(uid);
            OutlinerEdits.SelectInVam(atom, false);
            NotifyOutlinerSelectionChanged(true);
        }

        internal void ToggleSceneOutliner()
        {
            OpenSceneOutliner(forceShow: !IsSceneOutlinerOpen());
        }

        internal void HideSceneOutliner()
        {
            try { ReleaseOutlinerTargets(); } catch { }
            CloseOutlinerRowMenu();
            CaptureOutlinerGeometryToMemory();
            PersistOutlinerGeometry();
            if (VPBConfig.Instance != null) VPBConfig.Instance.OutlinerOpen = false;
            if (_outlinerRoot != null) _outlinerRoot.SetActive(false);
            _outlinerLastFocused = false;
            RefreshTboxSceneOutlinerTint();
        }

        internal bool TryHandleOutlinerEsc()
        {
            if (!IsSceneOutlinerOpen()) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            if (_outlinerRowMenuGO != null)
            {
                CloseOutlinerRowMenu();
                return true;
            }
            if (!string.IsNullOrEmpty(_outlinerInspectorSection))
            {
                _outlinerInspectorSection = OutlinerInspectorSections.All;
                RebuildOutlinerInspector();
                return true;
            }
            if (_outlinerKindMask != CreatorStripKeepKind.None)
            {
                _outlinerKindMask = CreatorStripKeepKind.None;
                RebuildOutlinerTypeNav();
                QueueOutlinerRebuild();
                return true;
            }
            if (_outlinerFilterInput != null && !string.IsNullOrEmpty(_outlinerFilterInput.text))
            {
                _outlinerFilterInput.text = "";
                _outlinerFilter = "";
                QueueOutlinerRebuild();
                return true;
            }
            HideSceneOutliner();
            return true;
        }

        internal bool TryHandleOutlinerUndo()
        {
            if (!_outlinerLastFocused || !IsSceneOutlinerOpen()) return false;
            if (_outlinerUndo.UndoCount == 0) return false;
            ApplyOutlinerUndoRecord(_outlinerUndo.Undo(), true);
            return true;
        }

        internal bool TryHandleOutlinerRedo()
        {
            if (!_outlinerLastFocused || !IsSceneOutlinerOpen()) return false;
            if (_outlinerUndo.RedoCount == 0) return false;
            ApplyOutlinerUndoRecord(_outlinerUndo.Redo(), false);
            return true;
        }

        void EnsureOutlinerBuilt()
        {
            if (_outlinerRoot != null) return;
            try { BuildOutliner(); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Outliner] Build failed: " + ex);
            }
        }

        float OutlinerLiveScale()
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            return s;
        }

        static void ApplyOutlinerScaledFont(Text txt, int designPt, float s)
        {
            GalleryUiMetrics.ApplyFont(txt, designPt, s, GalleryUiDesignTokens.FontMinRef);
        }

        static void StyleOutlinerInputField(InputField inf)
        {
            if (inf == null) return;
            StyleOutlinerInputText(inf.textComponent);
            Text ph = inf.placeholder as Text;
            StyleOutlinerInputText(ph);
        }

        static void StyleOutlinerInputText(Text t)
        {
            if (t == null) return;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform rt = t.rectTransform;
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        static void DestroyOutlinerGameObject(GameObject go)
        {
            if (go == null) return;
            try { UnityEngine.Object.DestroyImmediate(go); }
            catch
            {
                try { UnityEngine.Object.Destroy(go); } catch { }
            }
        }

        int PurgeNamedOutlinerRoots(GameObject host, GameObject keep)
        {
            if (host == null) return 0;
            Transform t = host.transform;
            if (t == null) return 0;
            int n = 0;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform c = t.GetChild(i);
                if (c == null) continue;
                if (c.name != "VPB_SceneOutliner") continue;
                if (keep != null && c.gameObject == keep) continue;
                DestroyOutlinerGameObject(c.gameObject);
                n++;
            }
            return n;
        }

        void PurgeStaleOutlinerPanes(GameObject keep)
        {
            int n = 0;
            n += PurgeNamedOutlinerRoots(canvas != null ? canvas.gameObject : null, keep);
            n += PurgeNamedOutlinerRoots(backgroundBoxGO, keep);
            GameObject pluginsHost = ResolvePluginsFloatHost();
            if (pluginsHost != null
                && pluginsHost != backgroundBoxGO
                && (canvas == null || pluginsHost != canvas.gameObject))
                n += PurgeNamedOutlinerRoots(pluginsHost, keep);
            if (n > 0)
            {
                try
                {
                    LogUtil.Log("[VPB.Outliner] removed " + n + " leftover Scene Outliner pane(s)");
                }
                catch { }
            }
        }

        void RehostOutlinerIfNeeded()
        {
            GameObject host = ResolveOutlinerHost();
            if (_outlinerRoot == null || host == null) return;
            if (_outlinerRoot.transform.parent == host.transform) return;
            bool open = _outlinerRoot.activeSelf;
            CaptureOutlinerGeometryToMemory();
            DestroyOutlinerChrome();
            if (!open) return;
            EnsureOutlinerBuilt();
            if (_outlinerRoot == null) return;
            _outlinerRoot.SetActive(true);
            BindOutlinerAtomEvents(true);
            ApplyOutlinerLayout();
            InvalidateOutlinerCards();
            QueueOutlinerRebuild();
        }

        void DestroyOutlinerChrome()
        {
            try { ReleaseOutlinerTargets(); } catch { }
            CloseOutlinerRowMenu();
            _outlinerRowPool.Clear();
            DropOutlinerCardCache();
            _outlinerCardCacheUid = "";
            _outlinerCardCacheKeep = false;
            _outlinerNavSig = 0;
            GameObject dying = _outlinerRoot;
            _outlinerRoot = null;
            DestroyOutlinerGameObject(dying);
            PurgeStaleOutlinerPanes(null);
            _outlinerPanelRT = null;
            _outlinerDropHoverImg = null;
            _outlinerDropHoverOn = false;
            _outlinerTitleBarRT = null;
            _outlinerBodyGO = null;
            _outlinerFooterGO = null;
            _outlinerFooterStatusText = null;
            _outlinerPresentKinds = CreatorStripKeepKind.None;
            _outlinerFooterShown = null;
            _outlinerFooterColorKind = int.MinValue;
            _outlinerPendingVamUid = null;
            _outlinerResetUndoBtn = null;
            _outlinerResetUndoLabel = null;
            _outlinerUndoBtn = null;
            _outlinerRedoBtn = null;
            _outlinerUndoShown = -1;
            _outlinerRedoShown = -1;
            _outlinerDropRow = null;
            _outlinerSelectAnchorVis = -1;
            _outlinerHeaderDrag = null;
            _outlinerFooterDrag = null;
            _outlinerFooterSpacerDrag = null;
            _outlinerTitleText = null;
            _outlinerTargetsBtn = null;
            _outlinerLayoutBtn = null;
            _outlinerCollapseBtn = null;
            _outlinerCollapseIcon = null;
            _outlinerTreePane = null;
            _outlinerInspectorPane = null;
            _outlinerSplitHandleRT = null;
            _outlinerSplitBarGO = null;
            _outlinerFloatResizer = null;
            _outlinerRailWidthDrag = null;
            _outlinerResizeHandleGO = null;
            _outlinerTypeNavRowRT = null;
            _outlinerTypeNavRowGO = null;
            _outlinerTypeNavIconsGO = null;
            _outlinerFilterInput = null;
            _outlinerTreeScrollGO = null;
            _outlinerTreeScroll = null;
            _outlinerTreeContentRT = null;
            _outlinerEmptyRow = null;
            _outlinerInspectorScroll = null;
            _outlinerInspectorContent = null;
            _outlinerUidField = null;
            _outlinerInspectorEmpty = null;
            _outlinerInspectorFilterRowRT = null;
            _outlinerInspectorFilterGO = null;
            _outlinerInspectorFilterGrid = null;
            _outlinerInspectorBuiltUid = null;
            _outlinerPendingVamUid = null;
            _outlinerLookBuildQueued = false;
            _outlinerVirtFirst = -1;
        }

        void RescaleOutlinerIfOpen(float chromeScale)
        {
            if (_outlinerRoot == null) return;
            float s = chromeScale > 0f ? chromeScale : ChromeScale;
            if (s <= 0f) s = 1f;
            if (Mathf.Abs(_outlinerChromeScale - s) < 0.01f) return;
            CaptureOutlinerGeometryToMemory();
            ApplyOutlinerBuiltChrome(s);
            if (_outlinerCollapsed)
            {
                float titleH = OutlinerChromeBarHeight(_outlinerChromeScale);
                SyncOutlinerCollapseChrome(titleH);
            }
            ApplyOutlinerLayout();
            if (IsSceneOutlinerOpen()) QueueOutlinerRebuild();
            PersistOutlinerGeometry();
        }

        void ApplyOutlinerBuiltChrome(float s)
        {
            if (s <= 0f) s = 1f;
            _outlinerChromeScale = s;
            float titleH = OutlinerChromeBarHeight(s);
            float footerH = titleH;
            float chrome = OutlinerChromeSize(s);
            if (_outlinerTitleBarRT != null)
            {
                _outlinerTitleBarRT.sizeDelta = new Vector2(0f, titleH);
                HorizontalLayoutGroup titleHlg = _outlinerTitleBarRT.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = _outlinerTitleBarRT.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(titleHlg, gripTr != null ? gripTr.gameObject : null, s);
                UI.LayoutFloatTitleWindowIcon(
                    _outlinerTitleBarRT.gameObject,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                ApplyOutlinerScaledFont(_outlinerTitleText, GalleryUiDesignTokens.FontTitleRef, s);
                ScaleOutlinerChromeIconButtons(_outlinerTitleBarRT, chrome, s);
            }
            if (_outlinerFooterGO != null)
            {
                RectTransform footerRT = _outlinerFooterGO.GetComponent<RectTransform>();
                if (footerRT != null) footerRT.sizeDelta = new Vector2(0f, footerH);
                HorizontalLayoutGroup footerHlg = _outlinerFooterGO.GetComponent<HorizontalLayoutGroup>();
                if (footerHlg != null)
                {
                    footerHlg.spacing = GalleryUiDesignTokens.TightGapRef * s;
                    footerHlg.padding = UI.PadFloatFooter(s);
                }
                ScaleOutlinerChromeIconButtons(_outlinerFooterGO.transform, chrome, s);
                ApplyOutlinerScaledFont(_outlinerFooterStatusText, GalleryUiDesignTokens.StatusBarFontRef, s);
                ApplyOutlinerScaledFont(_outlinerResetUndoLabel, GalleryUiDesignTokens.FontBodyRef, s);
                if (_outlinerResetUndoBtn != null)
                {
                    LayoutElement undoLe = _outlinerResetUndoBtn.GetComponent<LayoutElement>();
                    if (undoLe != null)
                    {
                        undoLe.minHeight = undoLe.preferredHeight = chrome;
                        undoLe.minWidth = GalleryUiDesignTokens.ButtonSizeRef * 3f * s;
                    }
                }
                SizeOutlinerFooterIcon(_outlinerUndoBtn, chrome);
                SizeOutlinerFooterIcon(_outlinerRedoBtn, chrome);
                _outlinerUndoShown = -1;
                _outlinerRedoShown = -1;
                SyncOutlinerUndoButtons();
            }
            if (_outlinerBodyGO != null)
            {
                RectTransform bodyRT = _outlinerBodyGO.GetComponent<RectTransform>();
                if (bodyRT != null)
                {
                    bodyRT.offsetMin = new Vector2(0f, footerH);
                    bodyRT.offsetMax = new Vector2(0f, -titleH);
                }
            }
        }

        static void ScaleOutlinerChromeIconButtons(Transform parent, float chrome, float s)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform t = parent.GetChild(i);
                if (t == null) continue;
                if (t.name == "WindowIcon" || t.name == "Grip" || t.name == "Title" || t.name == "Status")
                    continue;
                if (t.Find("Icon") == null) continue;
                UI.ScaleFloatChromeIconButton(t.gameObject, chrome, s);
            }
        }

        float OutlinerChromeSize(float s)
        {
            return GalleryUiDesignTokens.ButtonSizeRef * s;
        }

        float OutlinerChromeBarHeight(float s)
        {
            return GalleryUiDesignTokens.OutlinerChromeBarHeightRef * s;
        }

        void SyncOutlinerChromeDragEnabled()
        {
            bool floatOn = !OutlinerIsRail();
            if (_outlinerHeaderDrag != null) _outlinerHeaderDrag.enabled = floatOn;
            if (_outlinerFooterDrag != null) _outlinerFooterDrag.enabled = floatOn;
            if (_outlinerFooterSpacerDrag != null) _outlinerFooterSpacerDrag.enabled = floatOn;
            SyncOutlinerResizeMode();
        }

        void SyncOutlinerResizeMode()
        {
            bool rail = OutlinerIsRail() && !_outlinerCollapsed;
            if (_outlinerFloatResizer != null) _outlinerFloatResizer.enabled = !rail;
            if (_outlinerRailWidthDrag != null) _outlinerRailWidthDrag.enabled = rail;
            SyncOutlinerResizeHandleCorner(rail);
        }

        void SyncOutlinerResizeHandleCorner(bool rail)
        {
            if (_outlinerResizeHandleGO == null) return;
            bool leftCorner = rail && !OutlinerDockLeft();
            Transform tr = _outlinerResizeHandleGO.transform;
            if (leftCorner) { if (tr.GetSiblingIndex() != 0) tr.SetAsFirstSibling(); }
            else if (tr.GetSiblingIndex() != tr.parent.childCount - 1) tr.SetAsLastSibling();
            try { UI.ApplyBarIconFromPath(_outlinerResizeHandleGO, leftCorner ? "chevrons-down-left" : "chevrons-down-right"); } catch { }
        }

        GameObject ResolveOutlinerHost()
        {
            if (canvas != null) return canvas.gameObject;
            if (backgroundBoxGO != null) return backgroundBoxGO;
            return ResolvePluginsFloatHost();
        }

        void BuildOutliner()
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            _outlinerChromeScale = s;
            LoadOutlinerGeometryFromConfig();
            GameObject host = ResolveOutlinerHost();
            if (host == null) return;
            PurgeStaleOutlinerPanes(null);

            _outlinerRoot = UI.CreateChildRT(host, "VPB_SceneOutliner", AnchorPresets.stretchAll);
            try { SetLayerRecursiveLocal(_outlinerRoot, host.layer); } catch { }

            GameObject panel = UI.CreateChildRT(_outlinerRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(GalleryUiDesignTokens.OutlinerRailWidthRef * s, 720f * s), Vector2.zero);
            Image panelImg = UI.AddImage(panel, OutlinerPanelBg);
            BindOutlinerDropHoverImage(panelImg);
            if (panel.GetComponent<RectMask2D>() == null) panel.AddComponent<RectMask2D>();
            _outlinerPanelRT = panel.GetComponent<RectTransform>();
            _outlinerPanelRT.pivot = new Vector2(0f, 1f);

            float titleH = OutlinerChromeBarHeight(s);
            float footerH = titleH;
            float gap = GalleryUiDesignTokens.ControlGapRef * s;
            float chrome = OutlinerChromeSize(s);

            BuildOutlinerHeader(panel, titleH, chrome, gap, s);
            BuildOutlinerFooter(panel, footerH, gap, s, chrome);

            _outlinerBodyGO = UI.CreateChildRT(panel, "Body", AnchorPresets.stretchAll);
            if (_outlinerBodyGO.GetComponent<RectMask2D>() == null)
                _outlinerBodyGO.AddComponent<RectMask2D>();
            RectTransform bodyRT = _outlinerBodyGO.GetComponent<RectTransform>();
            bodyRT.offsetMin = new Vector2(0f, footerH);
            bodyRT.offsetMax = new Vector2(0f, -titleH);

            BuildOutlinerTreePane(_outlinerBodyGO, gap, s);
            BuildOutlinerInspectorPane(_outlinerBodyGO, gap, s);
            BuildOutlinerSplitBar(_outlinerBodyGO, s);

            if (_outlinerCollapsed) SyncOutlinerCollapseChrome(titleH);
            try { UI.ApplyFloatRootHoverPolicy(_outlinerRoot); } catch { }
            _outlinerRoot.SetActive(false);
        }

        void BuildOutlinerHeader(GameObject panel, float titleH, float chrome, float gap, float s)
        {
            GameObject bar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            UI.AddImage(bar, OutlinerBarBg);
            _outlinerTitleBarRT = bar.GetComponent<RectTransform>();
            HorizontalLayoutGroup titleHlg = UI.AddHLG(
                bar, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (bar.GetComponent<RectMask2D>() == null) bar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(bar, "⠇", GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(titleHlg, grip.gameObject, s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(bar, "list-details", winIconSz);

            _outlinerTitleText = UI.CreateEmphasisTitleLabel(
                bar, VPBTranslation.T("outliner.title", "Scene"),
                GalleryUiDesignTokens.FontTitleRef, GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleLeft, "Title");
            ApplyOutlinerScaledFont(_outlinerTitleText, GalleryUiDesignTokens.FontTitleRef, s);
            UI.AddLE(_outlinerTitleText.gameObject, flexibleWidth: 1f, minWidth: 48f * s);

            _outlinerTargetsBtn = UI.CreateFloatChromeIconButton(
                bar.transform, chrome, "target-off",
                GalleryUiColorTokens.ChromeIconWell, CycleOutlinerTargetMode);
            SyncOutlinerTargetsButton();

            _outlinerLayoutBtn = UI.CreateFloatChromeIconButton(
                bar.transform, chrome, "layout-sidebar",
                GalleryUiColorTokens.ChromeIconWell, CycleOutlinerLayoutMode);
            SyncOutlinerLayoutButton();

            GameObject splitBtn = UI.CreateFloatChromeIconButton(
                bar.transform, chrome, "layout-list",
                GalleryUiColorTokens.ChromeIconWell, CycleOutlinerSplit);
            AddTooltip(splitBtn, "outliner.split", "Tree only / split / inspector only");

            _outlinerCollapseBtn = UI.CreateFloatChromeIconButton(
                bar.transform, chrome, "chevron-up",
                GalleryUiColorTokens.ChromeIconWell, ToggleOutlinerCollapsed);
            if (_outlinerCollapseBtn != null)
            {
                Transform iconTr = _outlinerCollapseBtn.transform.Find("Icon");
                _outlinerCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
                AddTooltip(_outlinerCollapseBtn, "outliner.collapse", "Collapse to title bar");
            }

            GameObject closeBtn = UI.CreateFloatChromeIconButton(
                bar.transform, chrome, "x",
                GalleryUiColorTokens.ChromeIconWell, HideSceneOutliner);
            AddTooltipPlain(closeBtn, VPBTranslation.T("outliner.close", "Close Scene Overview") +
                "  (" + VpbShortcutMap.GetPattern(VpbShortcut.SceneOutliner) + ")");

            var headerDrag = bar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _outlinerPanelRT;
            headerDrag.OnMoved = OnOutlinerFloatMoved;
            _outlinerHeaderDrag = headerDrag;
        }

        void BuildOutlinerFooter(GameObject panel, float footerH, float gap, float s, float chrome)
        {
            _outlinerFooterGO = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(_outlinerFooterGO, OutlinerBarBg);
            RectTransform footerRT = _outlinerFooterGO.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(_outlinerFooterGO, spacing: GalleryUiDesignTokens.TightGapRef * s,
                padding: UI.PadFloatFooter(s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (_outlinerFooterGO.GetComponent<RectMask2D>() == null)
                _outlinerFooterGO.AddComponent<RectMask2D>();

            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_outlinerFooterGO);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _outlinerPanelRT;
                footerDrag.OnMoved = OnOutlinerFloatMoved;
                _outlinerFooterDrag = footerDrag;
            }

            GameObject statusHost = new GameObject("Status");
            statusHost.transform.SetParent(_outlinerFooterGO.transform, false);
            UI.AddLE(statusHost, flexibleWidth: 1f, minWidth: GalleryUiDesignTokens.ButtonSizeRef * s,
                preferredHeight: chrome, minHeight: chrome);
            if (statusHost.GetComponent<RectMask2D>() == null)
                statusHost.AddComponent<RectMask2D>();
            UI.EnsureFloatFooterSpacerDragHit(statusHost);
            var spacerDrag = statusHost.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = _outlinerPanelRT;
            spacerDrag.OnMoved = OnOutlinerFloatMoved;
            _outlinerFooterSpacerDrag = spacerDrag;

            _outlinerFooterStatusText = UI.CreateLabel(statusHost, "",
                GalleryUiDesignTokens.StatusBarFontRef, GalleryUiColorTokens.TextDim, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false);
            ApplyOutlinerScaledFont(_outlinerFooterStatusText, GalleryUiDesignTokens.StatusBarFontRef, s);
            ClipOutlinerFooterStatus(_outlinerFooterStatusText);
            RectTransform stRT = _outlinerFooterStatusText.rectTransform;
            stRT.anchorMin = Vector2.zero;
            stRT.anchorMax = Vector2.one;
            stRT.offsetMin = Vector2.zero;
            stRT.offsetMax = Vector2.zero;

            BuildOutlinerUndoButtons(_outlinerFooterGO, chrome, s);
            BuildOutlinerResetUndoBar(_outlinerFooterGO, chrome, s);

            GameObject resizeHandle = UI.AddChildGOImage(
                _outlinerFooterGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chrome, chrome, Vector2.zero, rounded: true);
            resizeHandle.name = "ResizeHandle";
            _outlinerResizeHandleGO = resizeHandle;
            UI.EnsureFloatChromeHoverBorder(resizeHandle);
            LayoutElement rhLe = resizeHandle.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = resizeHandle.AddComponent<LayoutElement>();
            rhLe.minWidth = rhLe.preferredWidth = chrome;
            rhLe.minHeight = rhLe.preferredHeight = chrome;
            rhLe.flexibleWidth = 0f;
            rhLe.flexibleHeight = 0f;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(resizeHandle, rhSpr, GalleryUiDesignTokens.FloatChromeIconPadRef * s, UI.IconButtonBackdrop);
            }
            catch { }
            var resizer = resizeHandle.AddComponent<UIFloatPanelResize>();
            resizer.Target = _outlinerPanelRT;
            resizer.GetMinSize = () => new Vector2(
                GalleryUiDesignTokens.OutlinerFloatMinWidthRef * _outlinerChromeScale,
                GalleryUiDesignTokens.OutlinerFloatMinHeightRef * _outlinerChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                GalleryUiDesignTokens.OutlinerFloatMaxWidthRef * _outlinerChromeScale,
                GalleryUiDesignTokens.OutlinerFloatMaxHeightRef * _outlinerChromeScale);
            resizer.OnResized = OnOutlinerFloatResized;
            _outlinerFloatResizer = resizer;
            var railDrag = resizeHandle.AddComponent<OutlinerRailWidthDrag>();
            railDrag.Panel = _outlinerPanelRT;
            railDrag.DockLeft = OutlinerDockLeft;
            railDrag.Scale = OutlinerLiveScale;
            railDrag.SetWidthRef = w => ApplyOutlinerRailWidth(w, false);
            railDrag.OnEnd = () =>
            {
                if (VPBConfig.Instance != null) VPBConfig.Instance.Save();
            };
            _outlinerRailWidthDrag = railDrag;
            AddTooltip(resizeHandle, "outliner.resize", "Drag to resize");
            SyncOutlinerResizeHandleCorner(OutlinerIsRail() && !_outlinerCollapsed);
        }

        enum OutlinerPlacement { RailLeft, RailRight, Float }

        OutlinerPlacement OutlinerPlacementSetting()
        {
            if (!OutlinerIsRail()) return OutlinerPlacement.Float;
            return OutlinerDockLeft() ? OutlinerPlacement.RailLeft : OutlinerPlacement.RailRight;
        }

        static OutlinerPlacement NextOutlinerPlacement(OutlinerPlacement p)
        {
            if (p == OutlinerPlacement.RailLeft) return OutlinerPlacement.RailRight;
            if (p == OutlinerPlacement.RailRight) return OutlinerPlacement.Float;
            return OutlinerPlacement.RailLeft;
        }

        void CycleOutlinerLayoutMode()
        {
            if (VPBConfig.Instance == null) return;
            OutlinerPlacement next = NextOutlinerPlacement(OutlinerPlacementSetting());
            VPBConfig.Instance.OutlinerLayoutMode = next == OutlinerPlacement.Float ? 1 : 0;
            if (next != OutlinerPlacement.Float)
                VPBConfig.Instance.OutlinerDockSide = next == OutlinerPlacement.RailLeft ? 1 : 2;
            ApplyOutlinerLayout();
            PersistOutlinerGeometry();
            VPBConfig.Instance.Save();
            SyncOutlinerLayoutButton();
            _outlinerLastFocused = true;
        }

        static string OutlinerPlacementIcon(OutlinerPlacement p)
        {
            if (p == OutlinerPlacement.RailLeft) return "layout-sidebar";
            if (p == OutlinerPlacement.RailRight) return "layout-sidebar-right";
            return "float-center";
        }

        static string OutlinerPlacementLabel(OutlinerPlacement p)
        {
            if (p == OutlinerPlacement.RailLeft) return VPBTranslation.T("outliner.dock.left", "dock left");
            if (p == OutlinerPlacement.RailRight) return VPBTranslation.T("outliner.dock.right", "dock right");
            return VPBTranslation.T("outliner.dock.float", "floating window");
        }

        void SyncOutlinerLayoutButton()
        {
            if (_outlinerLayoutBtn == null) return;
            bool vr = XrUtils.IsVrActive();
            if (_outlinerLayoutBtn.activeSelf == vr) _outlinerLayoutBtn.SetActive(!vr);
            if (vr) return;
            OutlinerPlacement p = OutlinerPlacementSetting();
            float size = OutlinerChromeSize(_outlinerChromeScale > 0f ? _outlinerChromeScale : 1f);
            try { UI.StyleFloatChromeIconButton(_outlinerLayoutBtn, size, OutlinerPlacementIcon(p), GalleryUiColorTokens.ChromeIconWell); }
            catch { }
            AddTooltipPlain(_outlinerLayoutBtn, VPBTranslation.T("outliner.layout.tip", "Placement")
                + ": " + OutlinerPlacementLabel(p)
                + "  —  " + VPBTranslation.T("outliner.targets.tip_next", "click for")
                + " " + OutlinerPlacementLabel(NextOutlinerPlacement(p)));
        }

        void CycleOutlinerSplit()
        {
            if (VPBConfig.Instance == null) return;
            float split = VPBConfig.Instance.OutlinerSplit;
            if (split <= 0.01f) split = GalleryUiDesignTokens.OutlinerSplitTreeShareRef;
            else if (split < 0.9f) split = 1f;
            else split = 0f;
            VPBConfig.Instance.OutlinerSplit = split;
            ApplyOutlinerSplit();
            try { ScheduleQuickFiltersConfigSave(); } catch { }
            _outlinerLastFocused = true;
        }

        bool OutlinerIsRail()
        {
            if (XrUtils.IsVrActive()) return false;
            return VPBConfig.Instance == null || VPBConfig.Instance.OutlinerLayoutMode == 0;
        }

        bool OutlinerDockLeft()
        {
            if (VPBConfig.Instance != null && VPBConfig.Instance.OutlinerDockSide == 1) return true;
            if (VPBConfig.Instance != null && VPBConfig.Instance.OutlinerDockSide == 2) return false;
            try
            {
                GalleryDockSide dock = EffectiveDockSide;
                if (dock == GalleryDockSide.Left) return false;
            }
            catch { }
            return true;
        }

        void ApplyOutlinerLayout()
        {
            if (_outlinerPanelRT == null) return;
            float s = OutlinerLiveScale();
            float w = GalleryUiDesignTokens.OutlinerRailWidthRef * s;
            if (VPBConfig.Instance != null)
                w = VPBConfig.ClampOutlinerWidth(VPBConfig.Instance.OutlinerWidth) * s;

            if (OutlinerIsRail())
            {
                bool left = OutlinerDockLeft();
                _outlinerPanelRT.anchorMin = left ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
                _outlinerPanelRT.anchorMax = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
                _outlinerPanelRT.pivot = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
                _outlinerPanelRT.anchoredPosition = Vector2.zero;
                _outlinerPanelRT.sizeDelta = new Vector2(w, 0f);
                _outlinerPanelRT.offsetMin = new Vector2(_outlinerPanelRT.offsetMin.x, 0f);
                _outlinerPanelRT.offsetMax = new Vector2(_outlinerPanelRT.offsetMax.x, 0f);
            }
            else
            {
                w = GalleryUiDesignTokens.OutlinerFloatDefaultWidthRef * s;
                float h = GalleryUiDesignTokens.OutlinerFloatDefaultHeightRef * s;
                if (_outlinerSavedSizeRef.HasValue)
                {
                    w = VPBConfig.ClampOutlinerFloatWidth(_outlinerSavedSizeRef.Value.x) * s;
                    h = VPBConfig.ClampOutlinerFloatHeight(_outlinerSavedSizeRef.Value.y) * s;
                }
                _outlinerPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _outlinerPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _outlinerPanelRT.pivot = new Vector2(0f, 1f);
                _outlinerPanelRT.sizeDelta = new Vector2(w, h);
                Vector2 center = _outlinerSavedPosCenter.HasValue ? _outlinerSavedPosCenter.Value : new Vector2(40f, 40f);
                _outlinerPanelRT.anchoredPosition = FloatPanelCoords.CenterToTopLeft(center, _outlinerPanelRT.sizeDelta);
            }
            ApplyOutlinerSplit();
            SyncOutlinerResizeMode();
            LayoutOutlinerTreeChrome();
            LayoutOutlinerInspectorChrome();
            SyncOutlinerChromeDragEnabled();
            RefreshOutlinerPlayBanner();
        }

        static void ClipOutlinerText(Text t)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            t.raycastTarget = false;
        }

        void BuildOutlinerSplitBar(GameObject body, float s)
        {
            float thick = GalleryUiDesignTokens.TightGapRef * s;
            _outlinerSplitBarGO = UI.CreateChildRT(body, "SplitBar", AnchorPresets.hStretchMiddle,
                new Vector2(0f, thick), Vector2.zero);
            UI.AddImage(_outlinerSplitBarGO, OutlinerBarBg);
            _outlinerSplitHandleRT = _outlinerSplitBarGO.GetComponent<RectTransform>();
            _outlinerSplitBarGO.SetActive(false);
        }

        void ApplyOutlinerSplit()
        {
            if (_outlinerTreePane == null || _outlinerInspectorPane == null) return;
            float split = OutlinerSplitLayout.ClampTreeShare(
                VPBConfig.Instance != null
                    ? VPBConfig.Instance.OutlinerSplit
                    : GalleryUiDesignTokens.OutlinerSplitTreeShareRef);
            RectTransform treeRT = _outlinerTreePane.GetComponent<RectTransform>();
            RectTransform inspRT = _outlinerInspectorPane.GetComponent<RectTransform>();
            float s = OutlinerLiveScale();
            float gap = GalleryUiDesignTokens.TightGapRef * s;
            bool rail = OutlinerIsRail();
            if (split <= 0.01f)
            {
                treeRT.anchorMin = Vector2.zero;
                treeRT.anchorMax = Vector2.one;
                treeRT.offsetMin = Vector2.zero;
                treeRT.offsetMax = Vector2.zero;
                if (_outlinerInspectorPane != null) _outlinerInspectorPane.SetActive(false);
                _outlinerTreePane.SetActive(true);
                if (_outlinerSplitBarGO != null) _outlinerSplitBarGO.SetActive(false);
            }
            else if (split >= 0.99f)
            {
                inspRT.anchorMin = Vector2.zero;
                inspRT.anchorMax = Vector2.one;
                inspRT.offsetMin = Vector2.zero;
                inspRT.offsetMax = Vector2.zero;
                _outlinerTreePane.SetActive(false);
                inspRT.gameObject.SetActive(true);
                if (_outlinerSplitBarGO != null) _outlinerSplitBarGO.SetActive(false);
            }
            else
            {
                _outlinerTreePane.SetActive(true);
                inspRT.gameObject.SetActive(true);
                if (rail)
                {
                    float seam = OutlinerSplitLayout.RailInspectorShare(split);
                    treeRT.anchorMin = new Vector2(0f, seam);
                    treeRT.anchorMax = Vector2.one;
                    treeRT.offsetMin = new Vector2(0f, gap * 0.5f);
                    treeRT.offsetMax = Vector2.zero;
                    inspRT.anchorMin = Vector2.zero;
                    inspRT.anchorMax = new Vector2(1f, seam);
                    inspRT.offsetMin = Vector2.zero;
                    inspRT.offsetMax = new Vector2(0f, -gap * 0.5f);
                    if (_outlinerSplitHandleRT != null)
                    {
                        _outlinerSplitHandleRT.anchorMin = new Vector2(0f, seam);
                        _outlinerSplitHandleRT.anchorMax = new Vector2(1f, seam);
                        _outlinerSplitHandleRT.pivot = new Vector2(0.5f, 0.5f);
                        _outlinerSplitHandleRT.sizeDelta = new Vector2(0f, gap);
                        _outlinerSplitHandleRT.anchoredPosition = Vector2.zero;
                    }
                }
                else
                {
                    treeRT.anchorMin = Vector2.zero;
                    treeRT.anchorMax = new Vector2(split, 1f);
                    treeRT.offsetMin = Vector2.zero;
                    treeRT.offsetMax = new Vector2(-gap * 0.5f, 0f);
                    inspRT.anchorMin = new Vector2(split, 0f);
                    inspRT.anchorMax = Vector2.one;
                    inspRT.offsetMin = new Vector2(gap * 0.5f, 0f);
                    inspRT.offsetMax = Vector2.zero;
                    if (_outlinerSplitHandleRT != null)
                    {
                        _outlinerSplitHandleRT.anchorMin = new Vector2(split, 0f);
                        _outlinerSplitHandleRT.anchorMax = new Vector2(split, 1f);
                        _outlinerSplitHandleRT.pivot = new Vector2(0.5f, 0.5f);
                        _outlinerSplitHandleRT.sizeDelta = new Vector2(gap, 0f);
                        _outlinerSplitHandleRT.anchoredPosition = Vector2.zero;
                    }
                }
                if (_outlinerSplitHandleRT != null) _outlinerSplitHandleRT.SetAsLastSibling();
                if (_outlinerSplitBarGO != null) _outlinerSplitBarGO.SetActive(true);
            }
            LayoutOutlinerTreeChrome();
            LayoutOutlinerInspectorChrome();
        }

        void RefreshOutlinerPlayBanner()
        {
            RefreshOutlinerFooter();
        }

        static string OutlinerFooterOneLine(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return "";
            int n = msg.IndexOf('\n');
            if (n < 0) return msg;
            return msg.Substring(0, n);
        }

        static void ClipOutlinerFooterStatus(Text t)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = false;
            t.raycastTarget = false;
        }

        bool OutlinerOwnsFooterTip()
        {
            if (string.IsNullOrEmpty(temporaryStatusMsg)) return false;
            if (temporaryStatusOwner == null) return _outlinerLastFocused;
            if (_outlinerRoot == null) return false;
            Transform t = temporaryStatusOwner.transform;
            Transform root = _outlinerRoot.transform;
            return t == root || t.IsChildOf(root);
        }

        void RefreshOutlinerFooter()
        {
            if (_outlinerFooterStatusText == null) return;
            string text = "";
            int colorKind = 0;
            Color color = GalleryUiColorTokens.TextDim;
            if (OutlinerOwnsFooterTip())
            {
                text = OutlinerFooterOneLine(temporaryStatusMsg);
                colorKind = 1;
                color = GalleryUiColorTokens.TextMuted;
            }
            else
            {
                bool play = false;
                try
                {
                    SuperController sc = SuperController.singleton;
                    play = sc != null && sc.gameMode != SuperController.GameMode.Edit;
                }
                catch { }
                if (play)
                {
                    text = VPBTranslation.T(
                        "outliner.play_banner", "Play mode — switch to Edit to change atoms");
                    colorKind = 2;
                    color = GalleryUiColorTokens.ActiveWarnText;
                }
                else
                {
                    string targets = OutlinerTargetsFooterText();
                    if (!string.IsNullOrEmpty(targets))
                    {
                        text = targets;
                        colorKind = 3;
                    }
                }
            }
            if (colorKind == _outlinerFooterColorKind
                && string.Equals(text, _outlinerFooterShown, StringComparison.Ordinal))
                return;
            _outlinerFooterColorKind = colorKind;
            _outlinerFooterShown = text;
            _outlinerFooterStatusText.text = text;
            _outlinerFooterStatusText.color = color;
            float s = _outlinerChromeScale > 0f ? _outlinerChromeScale : 1f;
            ApplyOutlinerScaledFont(_outlinerFooterStatusText, GalleryUiDesignTokens.StatusBarFontRef, s);
        }

        void ToggleOutlinerCollapsed()
        {
            if (_outlinerPanelRT == null) return;
            float s = _outlinerChromeScale > 0f ? _outlinerChromeScale : 1f;
            float titleH = OutlinerChromeBarHeight(s);
            if (!_outlinerCollapsed)
            {
                CaptureOutlinerGeometryToMemory();
                _outlinerExpandHeightRef = _outlinerSavedSizeRef.HasValue
                    ? _outlinerSavedSizeRef.Value.y
                    : GalleryUiDesignTokens.OutlinerFloatDefaultHeightRef;
                _outlinerCollapsedTopLeftPos = _outlinerPanelRT.anchoredPosition;
                _outlinerCollapsed = true;
            }
            else
            {
                _outlinerCollapsed = false;
            }
            SyncOutlinerCollapseChrome(titleH);
        }

        void SyncOutlinerCollapseChrome(float titleH)
        {
            if (_outlinerBodyGO != null) _outlinerBodyGO.SetActive(!_outlinerCollapsed);
            if (_outlinerFooterGO != null) _outlinerFooterGO.SetActive(!_outlinerCollapsed);
            if (_outlinerPanelRT != null && _outlinerCollapsed && OutlinerIsRail() == false)
                _outlinerPanelRT.sizeDelta = new Vector2(_outlinerPanelRT.sizeDelta.x, titleH);
            if (_outlinerCollapseIcon != null)
            {
                try
                {
                    Sprite sp = UI.LoadIconSprite(_outlinerCollapsed ? "chevron-down" : "chevron-up", UI.BarIconGlyphTint);
                    if (sp != null) _outlinerCollapseIcon.sprite = sp;
                }
                catch { }
            }
            if (!_outlinerCollapsed) ApplyOutlinerLayout();
            else SyncOutlinerResizeMode();
        }

        void QueueOutlinerRebuild()
        {
            _outlinerRebuildQueued = true;
        }

        void TickOutliner()
        {
            if (canvas == null) return;
            RehostOutlinerIfNeeded();
            RescaleOutlinerIfOpen(ChromeScale);
            if (!IsSceneOutlinerOpen())
            {
                if (!_outlinerStartupSettled)
                {
                    _outlinerStartupSettled = true;
                    if (VPBConfig.Instance != null && !AnySceneOutlinerOpen())
                        VPBConfig.Instance.OutlinerOpen = false;
                    return;
                }
                if (VPBConfig.Instance != null && VPBConfig.Instance.OutlinerOpen && !AnySceneOutlinerOpen())
                    OpenSceneOutliner(true);
                return;
            }
            _outlinerStartupSettled = true;
            if (!_outlinerListening) BindOutlinerAtomEvents(true);
            RefreshOutlinerPlayBanner();
            TickOutlinerTargets();
            TickOutlinerResetUndo();
            SyncOutlinerUndoButtons();
            if (_outlinerRebuildQueued)
            {
                _outlinerRebuildQueued = false;
                RebuildOutlinerNow();
            }
            if (!string.IsNullOrEmpty(_outlinerPendingVamUid))
            {
                string vamUid = _outlinerPendingVamUid;
                bool lookAt = _outlinerPendingVamLookAt;
                _outlinerPendingVamUid = null;
                OutlinerEdits.SelectInVam(OutlinerEdits.GetAtom(vamUid), lookAt);
                NoteOutlinerVamSelection(OutlinerSelectedUidInVam());
                _outlinerVamSelectGrace = 2;
            }
            else
            {
                PumpOutlinerLook();
            }
            int n = 10;
            if (VPBConfig.Instance != null) n = Mathf.Clamp(VPBConfig.Instance.OutlinerPollFrames, 1, 60);
            _outlinerPollCounter++;
            if (_outlinerPollCounter >= n)
            {
                _outlinerPollCounter = 0;
                PollOutlinerVisibleFacts();
            }
            UpdateOutlinerVirtualVisible(false);
        }

        void RebuildOutlinerNow()
        {
            float t0 = Time.realtimeSinceStartup;
            OutlinerCapture.CaptureAtoms(_outlinerFacts);
            SyncOutlinerTypeNavPresence();
            OutlinerGrouping grouping = OutlinerGrouping.TypeGroups;
            if (!_outlinerExpanded.Contains("scene"))
                _outlinerExpanded.Add("scene");
            _outlinerModel = OutlinerModelBuilder.Build(
                _outlinerFacts, grouping, _outlinerFilter, _outlinerKindMask);
            SeedOutlinerTypeGroupExpand();
            if (_outlinerModel != null)
                _outlinerModel.CollectVisible(_outlinerExpanded, _outlinerVisible, _outlinerVisibleDepths);
            ApplyOutlinerContentHeight();
            UpdateOutlinerVirtualVisible(true);
            RefreshOutlinerTitle();
            string uid = _outlinerSelection.PrimaryUid ?? "";
            if (!string.Equals(uid, _outlinerInspectorBuiltUid, StringComparison.Ordinal))
                RebuildOutlinerInspector();
        }

        void SeedOutlinerTypeGroupExpand()
        {
            OutlinerModel.ExpandDefaultCategories(_outlinerModel, _outlinerExpanded, _outlinerUserCollapsed);
        }

        void RefreshOutlinerTitle()
        {
            if (_outlinerTitleText == null) return;
            int count = _outlinerModel != null ? _outlinerModel.AtomCount : 0;
            string sel = _outlinerSelection.PrimaryUid;
            if (string.IsNullOrEmpty(sel))
                sel = VPBTranslation.T("outliner.none_selected", "none");
            _outlinerTitleText.text = VPBTranslation.T("outliner.title", "Scene")
                + "  ·  " + count
                + "  ·  " + sel;
            ApplyOutlinerScaledFont(_outlinerTitleText, GalleryUiDesignTokens.FontTitleRef, OutlinerLiveScale());
        }

        void BindOutlinerAtomEvents(bool bind)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                sc.onAtomAddedHandlers -= OnOutlinerAtomAdded;
                sc.onAtomRemovedHandlers -= OnOutlinerAtomRemoved;
                if (bind)
                {
                    sc.onAtomAddedHandlers += OnOutlinerAtomAdded;
                    sc.onAtomRemovedHandlers += OnOutlinerAtomRemoved;
                    _outlinerListening = true;
                }
                else _outlinerListening = false;
            }
            catch { }
        }

        static bool AnySceneOutlinerOpen()
        {
            try
            {
                if (Gallery.singleton == null) return false;
                var panels = Gallery.singleton.Panels;
                if (panels == null) return false;
                for (int i = 0; i < panels.Count; i++)
                {
                    GalleryPanel p = panels[i];
                    if (p != null && p.IsSceneOutlinerOpen()) return true;
                }
            }
            catch { }
            return false;
        }

        internal static void ClearOutlinerUndoForSceneLoad()
        {
            if (Gallery.singleton == null || Gallery.singleton.Panels == null) return;
            var panels = Gallery.singleton.Panels;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel panel = panels[i];
                if (panel == null) continue;
                panel._outlinerUndo.Clear();
                panel._outlinerResetUndo = null;
                panel._outlinerResetUndoUntil = 0f;
                panel._outlinerPendingVamUid = null;
                panel.SyncOutlinerResetUndoBar();
                panel.SyncOutlinerUndoButtons();
            }
        }

        internal void RefreshOutlinerAfterSceneChange()
        {
            if (_outlinerRoot == null) return;
            _outlinerStorableContentMemo.Clear();
            _outlinerInspectorBuiltUid = null;
            InvalidateOutlinerCards();
            if (IsSceneOutlinerOpen() && !_outlinerListening) BindOutlinerAtomEvents(true);
            QueueOutlinerRebuild();
        }

        void OnOutlinerAtomAdded(Atom a)
        {
            InvalidateOutlinerCards();
            QueueOutlinerRebuild();
        }

        void OnOutlinerAtomRemoved(Atom a)
        {
            InvalidateOutlinerCards();
            QueueOutlinerRebuild();
        }

        void PollOutlinerVisibleFacts()
        {
            if (_outlinerCollapsed) return;
            if (_outlinerModel == null) return;
            float t0 = Time.realtimeSinceStartup;
            int first = _outlinerVirtFirst;
            int n = Mathf.Min(_outlinerRowPool.Count, _outlinerVisible.Count - first);
            for (int i = 0; i < n; i++)
            {
                int vis = first + i;
                if (vis < 0 || vis >= _outlinerVisible.Count) continue;
                OutlinerNode node = _outlinerModel.Get(_outlinerVisible[vis]);
                if (node == null || node.Kind != OutlinerNodeKind.Atom) continue;
                if (string.Equals(_outlinerDragParamKey, node.AtomUid, StringComparison.Ordinal)) continue;
                Atom atom = OutlinerEdits.GetAtom(node.AtomUid);
                if (atom == null) continue;
                try { node.On = atom.on; } catch { }
                try { node.Hidden = atom.hidden; } catch { }
                try { node.Collision = atom.collisionEnabled; } catch { }
            }
            if (PollOutlinerOnFlags()) UpdateOutlinerVirtualVisible(true);
            SyncOutlinerFactButtons();
            SyncOutlinerSelectionFromVam();
            PollOutlinerTargetsFromVam();
            SyncOutlinerTransformFieldsFromScene();
            PollOutlinerPoseMorphs();
            if (OutlinerEdits.ConsumeEditModeAutoSwitch())
            {
                RefreshOutlinerPlayBanner();
                ShowTemporaryStatus(VPBTranslation.T("outliner.auto_edit_mode",
                    "Switched VaM to Edit mode so the scene could be changed."), 2f);
            }
        }

        void SyncOutlinerSelectionFromVam()
        {
            try
            {
                if (!string.IsNullOrEmpty(_outlinerPendingVamUid)) return;
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                Atom sel = sc.GetSelectedAtom();
                string uid = sel != null ? sel.uid : "";
                if (string.IsNullOrEmpty(uid)) return;
                bool vamChanged = !string.Equals(uid, _outlinerVamObservedUid, StringComparison.Ordinal);
                _outlinerVamObservedUid = uid;
                if (ReferenceEquals(sel, OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid))) return;
                if (string.Equals(uid, _outlinerSelection.PrimaryUid, StringComparison.Ordinal)) return;
                if (_outlinerVamSelectGrace > 0)
                {
                    _outlinerVamSelectGrace--;
                    return;
                }
                if (!vamChanged) return;
                _outlinerSelection.SelectOnly(uid);
                RefreshOutlinerTitle();
                UpdateOutlinerVirtualVisible(true);
                RebuildOutlinerInspector();
            }
            catch { }
        }

        void NoteOutlinerVamSelection(string uid)
        {
            _outlinerVamObservedUid = uid ?? "";
        }

        static string OutlinerSelectedUidInVam()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return "";
                Atom sel = sc.GetSelectedAtom();
                return sel != null ? (sel.uid ?? "") : "";
            }
            catch { return ""; }
        }

        void LoadOutlinerGeometryFromConfig()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            var g = cfg.GalleryOutlinerFloatGeometry.Current;
            if (g.PosSaved) _outlinerSavedPosCenter = new Vector2(g.PosX, g.PosY);
            if (g.SizeSaved)
            {
                _outlinerSavedSizeRef = new Vector2(
                    VPBConfig.ClampOutlinerFloatWidth(g.WidthRef),
                    VPBConfig.ClampOutlinerFloatHeight(g.HeightRef));
            }
            _outlinerPinMap = OutlinerPins.Parse(cfg.OutlinerPinsJson);
        }

        void CaptureOutlinerGeometryToMemory()
        {
            if (_outlinerPanelRT == null || OutlinerIsRail()) return;
            _outlinerSavedPosCenter = FloatPanelCoords.TopLeftToCenter(
                _outlinerPanelRT.anchoredPosition, _outlinerPanelRT.sizeDelta);
            float s = _outlinerChromeScale > 0.01f ? _outlinerChromeScale : 1f;
            _outlinerSavedSizeRef = _outlinerPanelRT.sizeDelta / s;
        }

        void PersistOutlinerGeometry()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (_outlinerSavedPosCenter.HasValue)
            {
                cfg.GalleryOutlinerFloatGeometry.Current.PosSaved = true;
                cfg.GalleryOutlinerFloatGeometry.Current.PosX = _outlinerSavedPosCenter.Value.x;
                cfg.GalleryOutlinerFloatGeometry.Current.PosY = _outlinerSavedPosCenter.Value.y;
            }
            if (_outlinerSavedSizeRef.HasValue)
            {
                cfg.GalleryOutlinerFloatGeometry.Current.SizeSaved = true;
                cfg.GalleryOutlinerFloatGeometry.Current.WidthRef =
                    VPBConfig.ClampOutlinerFloatWidth(_outlinerSavedSizeRef.Value.x);
                cfg.GalleryOutlinerFloatGeometry.Current.HeightRef =
                    VPBConfig.ClampOutlinerFloatHeight(_outlinerSavedSizeRef.Value.y);
            }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        void OnOutlinerFloatMoved()
        {
            CaptureOutlinerGeometryToMemory();
            PersistOutlinerGeometry();
            _outlinerLastFocused = true;
        }

        void OnOutlinerFloatResized()
        {
            CaptureOutlinerGeometryToMemory();
            PersistOutlinerGeometry();
            LayoutOutlinerTreeChrome();
            LayoutOutlinerInspectorChrome();
            _outlinerLastFocused = true;
        }

        void ApplyOutlinerRailWidth(float widthRef, bool persist)
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.OutlinerWidth = VPBConfig.ClampOutlinerWidth(widthRef);
            ApplyOutlinerLayout();
            if (persist) VPBConfig.Instance.Save();
        }

        sealed class OutlinerRailWidthDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            internal RectTransform Panel;
            internal Func<bool> DockLeft;
            internal Func<float> Scale;
            internal Action<float> SetWidthRef;
            internal Action OnEnd;
            Camera _cam;
            Vector2 _lastLocal;
            bool _active;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (Panel == null || eventData == null) return;
                if (!UIFloatPanelDrag.AcceptPointerButton(eventData)) return;
                RectTransform parent = Panel.parent as RectTransform;
                if (parent == null) return;
                _cam = UIFloatPanelDrag.ResolveDragCamera(Panel, eventData);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parent, eventData.position, _cam, out _lastLocal))
                    return;
                _active = true;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!_active || Panel == null || eventData == null) return;
                RectTransform parent = Panel.parent as RectTransform;
                if (parent == null) return;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parent, eventData.position, _cam, out local))
                    return;
                float dx = local.x - _lastLocal.x;
                _lastLocal = local;
                float s = Scale != null ? Scale() : 1f;
                if (s < 0.01f) s = 1f;
                bool left = DockLeft != null && DockLeft();
                float cur = Panel.rect.width / s;
                float next = left ? cur + dx / s : cur - dx / s;
                if (SetWidthRef != null) SetWidthRef(next);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (!_active) return;
                _active = false;
                if (OnEnd != null) OnEnd();
            }

            void OnDisable()
            {
                _active = false;
            }
        }
    }
}
