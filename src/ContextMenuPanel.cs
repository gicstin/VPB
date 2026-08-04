using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    /// <summary>
    /// Floating world-space context menu (drop disambiguator / intent router).
    /// Flat panel layout — options + cancel are direct children of the panel VLG
    /// (nested list + CSF caused overflow / square buttons). Warm path: button pool,
    /// Update only while visible.
    /// </summary>
    public class ContextMenuPanel : MonoBehaviour
    {
        public enum OptionKind
        {
            Normal = 0,
            Primary = 1,
            Destructive = 2,
            Quiet = 3,
            Header = 4
        }

        // --- Design tokens (dense power-user) ---
        private const int FontUI = 16;
        private const int FontMeta = 12;
        private const float SpaceXs = 4f;
        private const float SpaceSm = 8f;
        private const float SpaceMd = 12f;
        private const float RowHeightSingle = 36f;
        private const float RowHeightDual = 48f;
        private const float RowHeightLarge = 64f;
        private const float SectionHeight = 22f;
        private const float HeaderHeight = 28f;
        private const float HeaderHeightMulti = 44f;
        private const float HeaderHeightTriple = 58f;
        private const float PanelWidth = 360f;
        private const float PanelInner = PanelWidth - SpaceSm * 2f;
        private const float CornerRadius = 8f;
        private const float RowCornerRadius = 5f;
        private const float DigitColWidth = 22f;
        private const float ChevColWidth = 18f;
        private const float RowPadX = 10f;
        private const float RowPadY = 4f;
        private const float LabelLineH = 18f;
        private const float SubLineH = 14f;
        private const float CancelGap = SpaceMd;

        /// <summary>Scene drop sticky ids (persisted).</summary>
        public static class SceneActionId
        {
            public const string Load = "load";
            public const string Persons = "persons";
            public const string Props = "props";
            public const string PersonsNear = "persons_near";
            public const string PropsNear = "props_near";
            public const string ImportPerson = "import_person";
            public const string PickAtoms = "pick_atoms";
            public const string ImportOpen = "import_open";
            public const string MergeFilter = "merge_filter";
            public const string FullMerge = "full_merge";
            public const string FullMergeNear = "full_merge_near";
        }

        /// <summary>Appearance drop sticky ids (persisted).</summary>
        public static class AppearanceActionId
        {
            public const string Spawn = "spawn";
            public const string SpawnNoCollide = "spawn_nocollide";
            public const string ApplySelected = "apply_selected";
        }

        public struct Option
        {
            public string Label;
            public string Subtitle;
            public UnityAction Action;
            public bool IsLarge;
            public bool IsSubMenu;
            public OptionKind Kind;
            public bool Enabled;

            public Option(string label, UnityAction action, bool isLarge = false, bool isSubMenu = false)
            {
                Label = label;
                Subtitle = null;
                Action = action;
                IsLarge = isLarge;
                IsSubMenu = isSubMenu;
                Kind = OptionKind.Normal;
                Enabled = true;
            }

            public Option(string label, string subtitle, UnityAction action, OptionKind kind, bool isSubMenu = false, bool isLarge = false)
            {
                Label = label;
                Subtitle = subtitle;
                Action = action;
                IsLarge = isLarge;
                IsSubMenu = isSubMenu;
                Kind = kind;
                Enabled = true;
            }

            public static Option Section(string title)
            {
                Option o = new Option();
                o.Label = title;
                o.Subtitle = null;
                o.Action = null;
                o.IsLarge = false;
                o.IsSubMenu = false;
                o.Kind = OptionKind.Header;
                o.Enabled = false;
                return o;
            }
        }

        private struct HotkeySlot
        {
            public UnityAction Action;
            public bool IsSubMenu;
        }

        private class Page
        {
            public string Title;
            public List<Option> Options;
        }

        private static ContextMenuPanel _instance;
        private readonly Stack<Page> pageStack = new Stack<Page>();
        private readonly HotkeySlot[] _hotkeys = new HotkeySlot[9];
        private int _hotkeyCount;
        private bool _waitMouseUpBeforeOutside;
        private bool _desktopOutsideDismiss;
        private Canvas _canvas;

        private GameObject headerGO;
        private Text headerText;
        private Button backButton;
        private LayoutElement headerLE;
        private LayoutElement headerTitleLE;

        private GameObject cancelGapGO;
        private GameObject cancelGO;

        public static ContextMenuPanel Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("VPB_ContextMenu");
                    go.layer = 5;
                    _instance = go.AddComponent<ContextMenuPanel>();
                }
                return _instance;
            }
        }

        public static ContextMenuPanel ExistingInstance => _instance;

        private GameObject canvasGO;
        private RectTransform panelRT;
        private readonly List<GameObject> buttonPool = new List<GameObject>(16);

        private static readonly Color PanelBg = new Color(0.09f, 0.09f, 0.11f, 0.97f);
        private static readonly Color BtnNormal = new Color(0.22f, 0.23f, 0.26f, 1f);
        private static readonly Color BtnPrimary = new Color(0.16f, 0.40f, 0.58f, 1f);
        private static readonly Color BtnDestructive = new Color(0.45f, 0.18f, 0.16f, 1f);
        private static readonly Color BtnQuiet = new Color(0.17f, 0.17f, 0.19f, 1f);
        private static readonly Color BtnHeader = new Color(0.09f, 0.09f, 0.11f, 0.01f);
        private static readonly Color BtnDisabled = new Color(0.16f, 0.16f, 0.18f, 0.9f);
        private static readonly Color TextPrimary = new Color(0.94f, 0.95f, 0.97f, 1f);
        private static readonly Color TextMuted = new Color(0.62f, 0.65f, 0.70f, 1f);
        private static readonly Color TextSection = new Color(0.55f, 0.60f, 0.68f, 1f);
        private static readonly Color AccentBar = new Color(0.35f, 0.72f, 0.95f, 1f);

        void Awake()
        {
            canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            canvasGO.layer = 5;
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;

            GraphicRaycaster gr = canvasGO.AddComponent<GraphicRaycaster>();
            gr.ignoreReversedGraphics = true;

            if (SuperController.singleton != null)
                SuperController.singleton.AddCanvas(_canvas);

            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.localScale = Vector3.one;

            GameObject panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.layer = 5;
            panelRT = panelGO.AddComponent<RectTransform>();

            RoundedRect bg = panelGO.AddComponent<RoundedRect>();
            bg.color = PanelBg;
            bg.cornerRadius = CornerRadius;

            // Flat column: header → options → gap → cancel. Panel owns width/height.
            VerticalLayoutGroup vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)SpaceSm, (int)SpaceSm, (int)SpaceSm, (int)SpaceSm);
            vlg.spacing = SpaceXs;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.UpperLeft;

            ContentSizeFitter csf = panelGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement panelLE = panelGO.AddComponent<LayoutElement>();
            panelLE.minWidth = PanelWidth;
            panelLE.preferredWidth = PanelWidth;

            CreateHeader(panelGO);
            CreateCancelChrome(panelGO);

            canvasGO.SetActive(false);
            enabled = false;
        }

        private void CreateCancelChrome(GameObject panelGO)
        {
            // Proximity: gap separates actions from dismiss (Fitts / accidental-hit).
            cancelGapGO = new GameObject("CancelGap");
            cancelGapGO.transform.SetParent(panelGO.transform, false);
            cancelGapGO.layer = 5;
            LayoutElement gapLE = cancelGapGO.AddComponent<LayoutElement>();
            gapLE.minHeight = CancelGap;
            gapLE.preferredHeight = CancelGap;
            gapLE.minWidth = PanelInner;
            gapLE.preferredWidth = PanelInner;
            gapLE.flexibleWidth = 1f;
            cancelGapGO.SetActive(false);

            cancelGO = CreateButtonRow(panelGO.transform);
            cancelGO.name = "Cancel";
            cancelGO.SetActive(false);
        }

        /// <summary>Alt or Ctrl held — expert skip past menu (run last sticky action).</summary>
        public static bool IsSkipModifierHeld()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                    || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
            catch
            {
                return false;
            }
        }

        public static void RememberSceneAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (string.Equals(cfg.LastContextSceneAction, actionId, StringComparison.Ordinal))
                return;
            cfg.LastContextSceneAction = actionId;
            try { cfg.Save(false); } catch { }
        }

        public static void RememberAppearanceAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            if (string.Equals(cfg.LastContextAppearanceAction, actionId, StringComparison.Ordinal))
                return;
            cfg.LastContextAppearanceAction = actionId;
            try { cfg.Save(false); } catch { }
        }

        public static string GetLastSceneAction()
        {
            VPBConfig cfg = VPBConfig.Instance;
            return cfg != null ? (cfg.LastContextSceneAction ?? string.Empty) : string.Empty;
        }

        public static string GetLastAppearanceAction()
        {
            VPBConfig cfg = VPBConfig.Instance;
            return cfg != null ? (cfg.LastContextAppearanceAction ?? string.Empty) : string.Empty;
        }

        private void EnsurePlayerUiSpace()
        {
            if (canvasGO != null)
            {
                Transform canvasTf = canvasGO.transform;
                if (canvasTf.parent != transform)
                {
                    canvasTf.SetParent(transform, false);
                    canvasTf.localPosition = Vector3.zero;
                    canvasTf.localRotation = Quaternion.identity;
                    canvasTf.localScale = Vector3.one;
                }
                else
                {
                    Vector3 ls = canvasTf.localScale;
                    if (Mathf.Abs(ls.x - 1f) > 1e-6f || Mathf.Abs(ls.y - 1f) > 1e-6f || Mathf.Abs(ls.z - 1f) > 1e-6f)
                        canvasTf.localScale = Vector3.one;
                }
            }

            VpbWorldSpaceUiScale.ApplyConstantWorldScale(transform);

            if (canvasGO != null)
                canvasGO.layer = gameObject.layer;
        }

        void OnDestroy()
        {
            if (_canvas != null && SuperController.singleton != null)
            {
                try { SuperController.singleton.RemoveCanvas(_canvas); } catch { }
            }
            else if (canvasGO != null)
            {
                Canvas canvas = canvasGO.GetComponent<Canvas>();
                if (canvas != null && SuperController.singleton != null)
                    SuperController.singleton.RemoveCanvas(canvas);
            }
            ClearHotkeys();
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        void Update()
        {
            if (canvasGO == null || !canvasGO.activeSelf)
                return;

            bool cancel = false;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    cancel = true;
                else if (SuperController.singleton != null && SuperController.singleton.GetCancel())
                    cancel = true;
            }
            catch { }

            if (cancel)
            {
                Hide();
                return;
            }

            if (TryInvokeDigitHotkey())
                return;

            if (!_desktopOutsideDismiss)
                return;

            if (_waitMouseUpBeforeOutside)
            {
                try
                {
                    if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                        _waitMouseUpBeforeOutside = false;
                }
                catch { _waitMouseUpBeforeOutside = false; }
                return;
            }

            bool down = false;
            try { down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1); } catch { }
            if (!down) return;

            if (!IsPointerOverPanel())
                Hide();
        }

        private bool TryInvokeDigitHotkey()
        {
            if (_hotkeyCount <= 0) return false;

            int digit = -1;
            try
            {
                for (int i = 0; i < _hotkeyCount; i++)
                {
                    KeyCode alpha = (KeyCode)((int)KeyCode.Alpha1 + i);
                    KeyCode pad = (KeyCode)((int)KeyCode.Keypad1 + i);
                    if (Input.GetKeyDown(alpha) || Input.GetKeyDown(pad))
                    {
                        digit = i;
                        break;
                    }
                }
            }
            catch { return false; }

            if (digit < 0 || digit >= _hotkeyCount) return false;

            HotkeySlot slot = _hotkeys[digit];
            if (slot.Action == null) return false;

            if (!slot.IsSubMenu)
                Hide();
            try { slot.Action.Invoke(); } catch { }
            return true;
        }

        private bool IsPointerOverPanel()
        {
            if (panelRT == null) return false;
            Camera eventCam = null;
            if (_canvas != null)
                eventCam = _canvas.worldCamera;
            if (eventCam == null)
                eventCam = Camera.main;
            try
            {
                return RectTransformUtility.RectangleContainsScreenPoint(panelRT, Input.mousePosition, eventCam);
            }
            catch
            {
                return false;
            }
        }

        private void ClearHotkeys()
        {
            for (int i = 0; i < _hotkeys.Length; i++)
            {
                _hotkeys[i].Action = null;
                _hotkeys[i].IsSubMenu = false;
            }
            _hotkeyCount = 0;
        }

        private void CreateHeader(GameObject parent)
        {
            headerGO = new GameObject("Header");
            headerGO.transform.SetParent(parent.transform, false);
            headerGO.layer = 5;

            Image bg = headerGO.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.01f);

            UIDraggable drag = headerGO.AddComponent<UIDraggable>();
            drag.target = transform;

            headerLE = headerGO.AddComponent<LayoutElement>();
            headerLE.minHeight = HeaderHeight;
            headerLE.preferredHeight = HeaderHeight;
            headerLE.minWidth = PanelInner;
            headerLE.preferredWidth = PanelInner;
            headerLE.flexibleWidth = 1f;

            HorizontalLayoutGroup hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = SpaceXs;
            hlg.padding = new RectOffset(0, 0, 0, 0);

            GameObject backBtnGO = new GameObject("BackBtn");
            backBtnGO.transform.SetParent(headerGO.transform, false);
            backBtnGO.layer = 5;
            RoundedRect backImg = backBtnGO.AddComponent<RoundedRect>();
            backImg.color = BtnNormal;
            backImg.cornerRadius = RowCornerRadius;
            backButton = backBtnGO.AddComponent<Button>();
            backButton.onClick.AddListener(PopPage);

            LayoutElement backLE = backBtnGO.AddComponent<LayoutElement>();
            backLE.minWidth = 56f;
            backLE.preferredWidth = 56f;
            backLE.minHeight = 24f;
            backLE.preferredHeight = 24f;
            backLE.flexibleWidth = 0f;

            Text backText = CreateText(backBtnGO, "< Back", FontMeta);
            backText.color = TextPrimary;
            try { VPBUiFont.ApplyTo(backText); } catch { }

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(headerGO.transform, false);
            titleGO.layer = 5;
            headerTitleLE = titleGO.AddComponent<LayoutElement>();
            headerTitleLE.flexibleWidth = 1f;
            headerTitleLE.minWidth = 80f;
            headerTitleLE.minHeight = HeaderHeight;
            headerTitleLE.preferredHeight = HeaderHeight;

            headerText = titleGO.AddComponent<Text>();
            headerText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            headerText.text = "Menu";
            headerText.fontSize = FontUI;
            headerText.fontStyle = FontStyle.Bold;
            headerText.alignment = TextAnchor.MiddleLeft;
            headerText.supportRichText = true;
            headerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            headerText.verticalOverflow = VerticalWrapMode.Overflow;
            headerText.color = TextPrimary;
            headerText.raycastTarget = false;
            try { VPBUiFont.ApplyTo(headerText); } catch { }
        }

        private Text CreateText(GameObject parent, string content, int size)
        {
            GameObject tGO = new GameObject("Text");
            tGO.transform.SetParent(parent.transform, false);
            tGO.layer = 5;
            RectTransform rt = tGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text t = tGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = content;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return t;
        }

        public void Hide()
        {
            ClearHotkeys();
            pageStack.Clear();
            _waitMouseUpBeforeOutside = false;
            if (canvasGO != null) canvasGO.SetActive(false);
            enabled = false;
        }

        public bool IsVisible
        {
            get { return canvasGO != null && canvasGO.activeSelf; }
        }

        public void Show(Vector3 position, List<Option> options, string title = "Menu")
        {
            pageStack.Clear();
            PushPage(title, options);

            Camera cam = Camera.main;
            if (cam == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                cam = SuperController.singleton.centerCameraTarget.GetComponent<Camera>();

            if (_canvas != null && cam != null)
                _canvas.worldCamera = cam;

            Transform camTrans = cam != null ? cam.transform : null;
            if (camTrans == null && SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                camTrans = SuperController.singleton.centerCameraTarget.transform;

            if (camTrans != null)
            {
                float dist = 2.0f;
                if (VPBConfig.Instance != null) dist = VPBConfig.Instance.BringToFrontDistance;

                Vector3 dir = position - camTrans.position;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = camTrans.forward;
                position = camTrans.position + dir.normalized * dist;
            }

            EnsurePlayerUiSpace();
            transform.position = position;

            if (camTrans != null)
            {
                Vector3 lookPos = transform.position - camTrans.position;
                if (lookPos.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(lookPos, Vector3.up);
            }

            _desktopOutsideDismiss = true;
            try
            {
                if (SuperController.singleton != null && !SuperController.singleton.IsMonitorOnly)
                    _desktopOutsideDismiss = false;
            }
            catch { }

            try
            {
                _waitMouseUpBeforeOutside = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            }
            catch { _waitMouseUpBeforeOutside = false; }

            if (canvasGO != null)
                canvasGO.SetActive(true);

            gameObject.SetActive(true);
            enabled = true;
        }

        public void ReplaceCurrentOptions(List<Option> options, string title = null)
        {
            if (pageStack.Count == 0)
            {
                PushPage(string.IsNullOrEmpty(title) ? "Menu" : title, options);
                return;
            }
            Page current = pageStack.Peek();
            if (!string.IsNullOrEmpty(title))
                current.Title = title;
            current.Options = options;
            RefreshUI();
        }

        public void ShowBusy(Vector3 position, string title, string message)
        {
            List<Option> opts = new List<Option>(1);
            Option busy = new Option(message, null, null, OptionKind.Quiet);
            busy.Enabled = false;
            opts.Add(busy);
            Show(position, opts, title);
        }

        public void PushPage(string title, List<Option> options)
        {
            Page p = new Page { Title = title, Options = options };
            pageStack.Push(p);
            RefreshUI();
        }

        public void PopPage()
        {
            if (pageStack.Count > 1)
            {
                pageStack.Pop();
                RefreshUI();
            }
            else
            {
                Hide();
            }
        }

        private void RefreshUI()
        {
            if (pageStack.Count == 0) return;
            Page currentPage = pageStack.Peek();

            headerText.text = currentPage.Title;
            headerText.fontSize = FontUI;
            int lineBreaks = 0;
            string title = currentPage.Title;
            if (title != null)
            {
                for (int i = 0; i < title.Length; i++)
                {
                    if (title[i] == '\n') lineBreaks++;
                }
            }
            float h = HeaderHeight;
            if (lineBreaks >= 2) h = HeaderHeightTriple;
            else if (lineBreaks >= 1) h = HeaderHeightMulti;
            if (headerLE != null)
            {
                headerLE.minHeight = h;
                headerLE.preferredHeight = h;
            }
            if (headerTitleLE != null)
            {
                headerTitleLE.minHeight = h;
                headerTitleLE.preferredHeight = h;
            }
            bool showBack = pageStack.Count > 1;
            backButton.gameObject.SetActive(showBack);

            RenderOptions(currentPage.Options);
            RebuildPanelLayout();
        }

        private void RebuildPanelLayout()
        {
            if (panelRT == null) return;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT); } catch { }
            Canvas.ForceUpdateCanvases();
        }

        private void RenderOptions(List<Option> options)
        {
            ClearHotkeys();

            for (int i = 0; i < buttonPool.Count; i++)
                buttonPool[i].SetActive(false);

            if (cancelGapGO != null) cancelGapGO.SetActive(false);
            if (cancelGO != null) cancelGO.SetActive(false);

            // Sibling order: Header(0) → options → CancelGap → Cancel
            if (headerGO != null)
                headerGO.transform.SetSiblingIndex(0);

            int count = options != null ? options.Count : 0;
            for (int i = 0; i < count; i++)
            {
                GameObject btnGO = i < buttonPool.Count ? buttonPool[i] : null;
                if (btnGO == null)
                {
                    btnGO = CreateButtonRow(panelRT);
                    buttonPool.Add(btnGO);
                }
                else
                {
                    if (btnGO.transform.parent != panelRT)
                        btnGO.transform.SetParent(panelRT, false);
                    btnGO.SetActive(true);
                }

                btnGO.transform.SetSiblingIndex(1 + i);

                int digit = -1;
                Option opt = options[i];
                if (opt.Kind != OptionKind.Header && opt.Enabled && opt.Action != null && _hotkeyCount < 9)
                {
                    digit = _hotkeyCount + 1;
                    _hotkeys[_hotkeyCount].Action = opt.Action;
                    _hotkeys[_hotkeyCount].IsSubMenu = opt.IsSubMenu;
                    _hotkeyCount++;
                }

                SetupButton(btnGO, opt, false, digit);
            }

            if (pageStack.Count == 1 && cancelGO != null)
            {
                if (cancelGapGO != null)
                {
                    cancelGapGO.SetActive(true);
                    cancelGapGO.transform.SetSiblingIndex(1 + count);
                }
                cancelGO.SetActive(true);
                cancelGO.transform.SetSiblingIndex(1 + count + 1);
                SetupButton(cancelGO, new Option(
                    VPBTranslation.T("ctx.cancel", "Cancel"),
                    VPBTranslation.T("ctx.cancel_sub", "Esc"),
                    Hide,
                    OptionKind.Quiet), true, -1);
            }
        }

        private GameObject CreateButtonRow(Transform parent)
        {
            GameObject btnGO = new GameObject("Button");
            btnGO.transform.SetParent(parent, false);
            btnGO.layer = 5;

            RoundedRect img = btnGO.AddComponent<RoundedRect>();
            img.color = BtnNormal;
            img.cornerRadius = RowCornerRadius;

            Button btn = btnGO.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            cb.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.55f);
            btn.colors = cb;

            LayoutElement le = btnGO.AddComponent<LayoutElement>();
            le.minHeight = RowHeightSingle;
            le.preferredHeight = RowHeightSingle;
            le.minWidth = PanelInner;
            le.preferredWidth = PanelInner;
            le.flexibleWidth = 1f;

            GameObject accentGO = new GameObject("Accent");
            accentGO.transform.SetParent(btnGO.transform, false);
            accentGO.layer = 5;
            Image accentImg = accentGO.AddComponent<Image>();
            accentImg.color = AccentBar;
            accentImg.raycastTarget = false;
            RectTransform accentRT = accentGO.GetComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 0.15f);
            accentRT.anchorMax = new Vector2(0f, 0.85f);
            accentRT.pivot = new Vector2(0f, 0.5f);
            accentRT.sizeDelta = new Vector2(3f, 0f);
            accentRT.anchoredPosition = new Vector2(2f, 0f);
            accentGO.SetActive(false);

            GameObject row = new GameObject("Row");
            row.transform.SetParent(btnGO.transform, false);
            row.layer = 5;
            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = new Vector2(RowPadX, RowPadY);
            rowRT.offsetMax = new Vector2(-RowPadX, -RowPadY);

            HorizontalLayoutGroup rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.childAlignment = TextAnchor.MiddleLeft;
            rowHlg.childControlHeight = true;
            rowHlg.childControlWidth = true;
            rowHlg.childForceExpandHeight = true;
            rowHlg.childForceExpandWidth = false;
            rowHlg.spacing = SpaceXs;
            rowHlg.padding = new RectOffset(0, 0, 0, 0);

            GameObject digitGO = new GameObject("Digit");
            digitGO.transform.SetParent(row.transform, false);
            digitGO.layer = 5;
            LayoutElement digitLe = digitGO.AddComponent<LayoutElement>();
            digitLe.minWidth = DigitColWidth;
            digitLe.preferredWidth = DigitColWidth;
            digitLe.flexibleWidth = 0f;
            digitLe.minHeight = LabelLineH;
            digitLe.preferredHeight = LabelLineH;
            Text digit = digitGO.AddComponent<Text>();
            digit.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            digit.fontSize = FontMeta;
            digit.color = TextMuted;
            digit.alignment = TextAnchor.MiddleCenter;
            digit.horizontalOverflow = HorizontalWrapMode.Overflow;
            digit.verticalOverflow = VerticalWrapMode.Truncate;
            digit.raycastTarget = false;
            try { VPBUiFont.ApplyTo(digit); } catch { }
            digitGO.SetActive(false);

            GameObject texts = new GameObject("Texts");
            texts.transform.SetParent(row.transform, false);
            texts.layer = 5;
            LayoutElement textsLe = texts.AddComponent<LayoutElement>();
            textsLe.flexibleWidth = 1f;
            textsLe.minWidth = 40f;
            textsLe.preferredWidth = 100f;
            VerticalLayoutGroup textsVlg = texts.AddComponent<VerticalLayoutGroup>();
            textsVlg.childAlignment = TextAnchor.MiddleLeft;
            textsVlg.childControlHeight = true;
            textsVlg.childControlWidth = true;
            textsVlg.childForceExpandHeight = false;
            textsVlg.childForceExpandWidth = true;
            textsVlg.spacing = 0;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(texts.transform, false);
            labelGO.layer = 5;
            Text label = labelGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = FontUI;
            label.color = TextPrimary;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            LayoutElement labelLe = labelGO.AddComponent<LayoutElement>();
            labelLe.minHeight = LabelLineH;
            labelLe.preferredHeight = LabelLineH;
            labelLe.flexibleWidth = 1f;
            try { VPBUiFont.ApplyTo(label); } catch { }

            GameObject subGO = new GameObject("Subtitle");
            subGO.transform.SetParent(texts.transform, false);
            subGO.layer = 5;
            Text sub = subGO.AddComponent<Text>();
            sub.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            sub.fontSize = FontMeta;
            sub.color = TextMuted;
            sub.alignment = TextAnchor.MiddleLeft;
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            sub.verticalOverflow = VerticalWrapMode.Truncate;
            sub.raycastTarget = false;
            LayoutElement subLe = subGO.AddComponent<LayoutElement>();
            subLe.minHeight = SubLineH;
            subLe.preferredHeight = SubLineH;
            subLe.flexibleWidth = 1f;
            try { VPBUiFont.ApplyTo(sub); } catch { }
            subGO.SetActive(false);

            GameObject chevGO = new GameObject("Chevron");
            chevGO.transform.SetParent(row.transform, false);
            chevGO.layer = 5;
            Text chev = chevGO.AddComponent<Text>();
            chev.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            chev.fontSize = FontUI;
            chev.color = TextMuted;
            chev.alignment = TextAnchor.MiddleCenter;
            chev.text = "›";
            chev.raycastTarget = false;
            LayoutElement chevLe = chevGO.AddComponent<LayoutElement>();
            chevLe.minWidth = ChevColWidth;
            chevLe.preferredWidth = ChevColWidth;
            chevLe.flexibleWidth = 0f;
            chevLe.minHeight = LabelLineH;
            chevLe.preferredHeight = LabelLineH;
            try { VPBUiFont.ApplyTo(chev); } catch { }
            chevGO.SetActive(false);

            return btnGO;
        }

        private static void FindRowParts(
            GameObject btnGO,
            out Text label,
            out Text subtitle,
            out Text chevron,
            out Text digit,
            out GameObject accent)
        {
            label = null;
            subtitle = null;
            chevron = null;
            digit = null;
            accent = null;
            if (btnGO == null) return;

            Transform accentTf = btnGO.transform.Find("Accent");
            if (accentTf != null) accent = accentTf.gameObject;

            Transform row = btnGO.transform.Find("Row");
            if (row == null)
            {
                label = btnGO.GetComponentInChildren<Text>();
                return;
            }

            Transform digitTf = row.Find("Digit");
            if (digitTf != null) digit = digitTf.GetComponent<Text>();

            Transform texts = row.Find("Texts");
            if (texts != null)
            {
                Transform lt = texts.Find("Label");
                Transform st = texts.Find("Subtitle");
                if (lt != null) label = lt.GetComponent<Text>();
                if (st != null) subtitle = st.GetComponent<Text>();
            }
            Transform ct = row.Find("Chevron");
            if (ct != null) chevron = ct.GetComponent<Text>();
        }

        private void SetupButton(GameObject btnGO, Option option, bool isCancel, int digitHotkey)
        {
            Text label;
            Text subtitle;
            Text chevron;
            Text digit;
            GameObject accent;
            FindRowParts(btnGO, out label, out subtitle, out chevron, out digit, out accent);

            bool isHeader = option.Kind == OptionKind.Header;
            bool hasSubtitle = !isHeader && !string.IsNullOrEmpty(option.Subtitle);
            string labelText = option.Label ?? string.Empty;

            if (label != null)
            {
                label.text = labelText;
                label.fontSize = isHeader ? FontMeta : FontUI;
                label.color = isHeader ? TextSection : TextPrimary;
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = isHeader ? FontStyle.Bold : FontStyle.Normal;
                LayoutElement labelLe = label.GetComponent<LayoutElement>();
                if (labelLe != null)
                {
                    float lh = isHeader ? SectionHeight - 4f : LabelLineH;
                    labelLe.minHeight = lh;
                    labelLe.preferredHeight = lh;
                }
            }

            if (subtitle != null)
            {
                subtitle.gameObject.SetActive(hasSubtitle);
                if (hasSubtitle)
                {
                    subtitle.fontSize = FontMeta;
                    subtitle.color = TextMuted;
                    subtitle.alignment = TextAnchor.MiddleLeft;
                    subtitle.text = option.Subtitle;
                    LayoutElement subLe = subtitle.GetComponent<LayoutElement>();
                    if (subLe != null)
                    {
                        subLe.minHeight = SubLineH;
                        subLe.preferredHeight = SubLineH;
                    }
                }
            }

            if (digit != null)
            {
                bool showDigit = digitHotkey > 0 && !isHeader && !isCancel;
                digit.gameObject.SetActive(showDigit);
                LayoutElement digitLe = digit.GetComponent<LayoutElement>();
                if (digitLe != null)
                {
                    if (showDigit)
                    {
                        digitLe.minWidth = DigitColWidth;
                        digitLe.preferredWidth = DigitColWidth;
                        digitLe.ignoreLayout = false;
                    }
                    else
                    {
                        digitLe.minWidth = 0f;
                        digitLe.preferredWidth = 0f;
                        digitLe.ignoreLayout = true;
                    }
                }
                if (showDigit)
                {
                    digit.text = DigitGlyph(digitHotkey);
                    digit.fontSize = FontMeta;
                    digit.color = TextMuted;
                    digit.alignment = TextAnchor.MiddleCenter;
                }
            }

            if (chevron != null)
            {
                bool showChev = option.IsSubMenu && !isHeader && !isCancel;
                chevron.gameObject.SetActive(showChev);
                LayoutElement chevLe = chevron.GetComponent<LayoutElement>();
                if (chevLe != null)
                {
                    if (showChev)
                    {
                        chevLe.minWidth = ChevColWidth;
                        chevLe.preferredWidth = ChevColWidth;
                        chevLe.ignoreLayout = false;
                    }
                    else
                    {
                        chevLe.minWidth = 0f;
                        chevLe.preferredWidth = 0f;
                        chevLe.ignoreLayout = true;
                    }
                }
                if (showChev)
                {
                    chevron.text = "›";
                    chevron.fontSize = FontUI;
                    chevron.color = TextMuted;
                }
            }

            if (accent != null)
                accent.SetActive(!isCancel && option.Kind == OptionKind.Primary && option.Enabled);

            Image img = btnGO.GetComponent<Image>();
            if (img != null)
            {
                // Cancel = Quiet tertiary (von Restorff: one primary, dismiss stays quiet).
                if (isCancel || option.Kind == OptionKind.Quiet)
                    img.color = BtnQuiet;
                else if (isHeader)
                    img.color = BtnHeader;
                else if (!option.Enabled)
                    img.color = BtnDisabled;
                else if (option.Kind == OptionKind.Primary)
                    img.color = BtnPrimary;
                else if (option.Kind == OptionKind.Destructive)
                    img.color = BtnDestructive;
                else
                    img.color = BtnNormal;
            }

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                float rowH;
                if (isHeader)
                    rowH = SectionHeight;
                else if (option.IsLarge)
                    rowH = RowHeightLarge;
                else if (hasSubtitle)
                    rowH = RowHeightDual;
                else
                    rowH = RowHeightSingle;
                le.minHeight = rowH;
                le.preferredHeight = rowH;
                le.minWidth = PanelInner;
                le.preferredWidth = PanelInner;
                le.flexibleWidth = 1f;
            }

            Button b = btnGO.GetComponent<Button>();
            b.onClick.RemoveAllListeners();

            if (isHeader)
            {
                b.interactable = false;
                return;
            }

            b.interactable = isCancel || option.Enabled;

            Option captured = option;
            b.onClick.AddListener(() =>
            {
                if (!isCancel && !captured.Enabled)
                    return;

                if (!captured.IsSubMenu)
                    Hide();

                if (captured.Action != null)
                    captured.Action.Invoke();
            });
        }

        private static string DigitGlyph(int digit)
        {
            switch (digit)
            {
                case 1: return "1";
                case 2: return "2";
                case 3: return "3";
                case 4: return "4";
                case 5: return "5";
                case 6: return "6";
                case 7: return "7";
                case 8: return "8";
                case 9: return "9";
                default: return string.Empty;
            }
        }
    }
}
