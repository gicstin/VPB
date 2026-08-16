using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Strip Scene power-user chrome: filter, presets/recipes, keyboard, bulk rename.
    /// Category keep picks live in list rows only.
    /// </summary>
    public partial class GalleryPanel
    {
        private enum StripKeepViewFilter
        {
            All = 0,
            KeptOnly = 1,
            DroppedOnly = 2,
            RenamedOnly = 3,
            TriggersOnly = 4,
        }

        private enum StripKeepSortMode
        {
            TypeThenName = 0,
            Name = 1,
            Type = 2,
            KeptFirst = 3,
        }

        private struct StripKeepNavEntry
        {
            public bool IsCategory;
            public CreatorStripKeepKind Kind;
            public string Uid;
            public Image RowBg;
            public Color BaseBg;
        }

        private const int StripKeepMaxRecipes = 8;

        private string _stripKeepFilterText = "";
        private string _stripKeepFilterLower = "";
        private InputField _stripKeepFilterInput;
        private GameObject _stripKeepFilterClearGo;
        private StripKeepViewFilter _stripKeepViewFilter = StripKeepViewFilter.All;
        private StripKeepSortMode _stripKeepSortMode = StripKeepSortMode.TypeThenName;
        private Transform _stripKeepRecipeChipRow;
        private GameObject _stripKeepRecipeRowHost;
        private LayoutElement _stripKeepRecipeRowHostLe;
        private float _stripKeepRecipeRowHostH;
        private RectTransform _stripKeepPresetChipContentRt;
        private Coroutine _stripKeepPresetFlowCo;
        private Text _stripKeepSortBtnLabel;
        private readonly Dictionary<StripKeepViewFilter, Image> _stripKeepViewChipImgs =
            new Dictionary<StripKeepViewFilter, Image>(4);
        private readonly List<StripKeepNavEntry> _stripKeepNav = new List<StripKeepNavEntry>(128);
        private int _stripKeepNavIndex = -1;
        private GameObject _stripKeepRecipeSaveInlineHost;
        private InputField _stripKeepRecipeSaveInlineInput;
        private GameObject _stripKeepMoreToolsHost;
        private GameObject _stripKeepMoreToggleGo;
        private bool _stripKeepMoreToolsExpanded;
        private GameObject _stripKeepShortcutHelpGO;
        private bool _stripKeepShortcutHelpVisible;
        private float _stripKeepBuiltChromeScale = -1f;
        private Coroutine _stripKeepSnugPanelCo;

        private static string StripKeepTypeChipShortLabel(CreatorStripKeepKind kind)
        {
            switch (kind)
            {
                case CreatorStripKeepKind.Persons: return "Persons";
                case CreatorStripKeepKind.Lights: return "Lights";
                case CreatorStripKeepKind.Sound: return "Sound";
                case CreatorStripKeepKind.Cameras: return "Cams";
                case CreatorStripKeepKind.Forces: return "Forces";
                case CreatorStripKeepKind.Animation: return "Anim";
                case CreatorStripKeepKind.Assets: return "CUA";
                case CreatorStripKeepKind.Toys: return "Toys";
                case CreatorStripKeepKind.Props: return "Props";
                case CreatorStripKeepKind.UI: return "UI";
                case CreatorStripKeepKind.SubScenes: return "SubSc";
                case CreatorStripKeepKind.Other: return "Other";
                default: return kind.ToString();
            }
        }

        private void StripKeepPowerUiResetSessionState()
        {
            _stripKeepFilterText = "";
            _stripKeepFilterLower = "";
            _stripKeepViewFilter = StripKeepViewFilter.All;
            _stripKeepSortMode = StripKeepSortMode.TypeThenName;
            _stripKeepNavIndex = -1;
            _stripKeepNav.Clear();
            _stripKeepViewChipImgs.Clear();
            _stripKeepFilterInput = null;
            _stripKeepFilterClearGo = null;
            _stripKeepRecipeChipRow = null;
            _stripKeepRecipeRowHost = null;
            _stripKeepRecipeRowHostLe = null;
            _stripKeepRecipeRowHostH = 0f;
            _stripKeepPresetChipContentRt = null;
            if (_stripKeepPresetFlowCo != null)
            {
                try { StopCoroutine(_stripKeepPresetFlowCo); } catch { }
                _stripKeepPresetFlowCo = null;
            }
            _stripKeepSortBtnLabel = null;
            _stripKeepRecipeSaveInlineHost = null;
            _stripKeepRecipeSaveInlineInput = null;
            _stripKeepMoreToolsHost = null;
            _stripKeepMoreToggleGo = null;
            _stripKeepMoreToolsExpanded = false;
            _stripKeepShortcutHelpGO = null;
            _stripKeepShortcutHelpVisible = false;
            _stripKeepBuiltChromeScale = -1f;
            _stripKeepScrollHostLe = null;
            StripKeepCreateOptionsResetUiRefs();
            StripKeepPossessOptionsResetUiRefs();
            if (_stripKeepSnugPanelCo != null)
            {
                try { StopCoroutine(_stripKeepSnugPanelCo); } catch { }
                _stripKeepSnugPanelCo = null;
            }
        }

        private void StripKeepPowerUiOnHide()
        {
            StripKeepPowerUiResetSessionState();
        }

        /// <summary>Clear UI refs before live rebuild — keep filter/selection/sort session.</summary>
        private void StripKeepPowerUiClearUiRefsForRebuild()
        {
            _stripKeepViewChipImgs.Clear();
            _stripKeepNav.Clear();
            _stripKeepNavIndex = -1;
            _stripKeepFilterInput = null;
            _stripKeepFilterClearGo = null;
            _stripKeepRecipeChipRow = null;
            _stripKeepRecipeRowHost = null;
            _stripKeepRecipeRowHostLe = null;
            _stripKeepPresetChipContentRt = null;
            if (_stripKeepPresetFlowCo != null)
            {
                try { StopCoroutine(_stripKeepPresetFlowCo); } catch { }
                _stripKeepPresetFlowCo = null;
            }
            _stripKeepSortBtnLabel = null;
            _stripKeepRecipeSaveInlineHost = null;
            _stripKeepRecipeSaveInlineInput = null;
            _stripKeepMoreToolsHost = null;
            _stripKeepMoreToggleGo = null;
            _stripKeepShortcutHelpGO = null;
            _stripKeepScrollHostLe = null;
            StripKeepCreateOptionsResetUiRefs();
            StripKeepPossessOptionsResetUiRefs();
            if (_stripKeepSnugPanelCo != null)
            {
                try { StopCoroutine(_stripKeepSnugPanelCo); } catch { }
                _stripKeepSnugPanelCo = null;
            }
        }

        private bool StripKeepItemMatchesFilter(StripKeepItem it)
        {
            if (string.IsNullOrEmpty(_stripKeepFilterLower)) return true;
            if (!string.IsNullOrEmpty(it.Name)
                && it.Name.IndexOf(_stripKeepFilterLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(it.Uid)
                && it.Uid.IndexOf(_stripKeepFilterLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(it.Type)
                && it.Type.IndexOf(_stripKeepFilterLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string renameTo;
            if (_stripKeepRenames.TryGetValue(it.Uid, out renameTo)
                && !string.IsNullOrEmpty(renameTo)
                && renameTo.IndexOf(_stripKeepFilterLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private bool StripKeepItemMatchesView(StripKeepItem it)
        {
            bool kept = _stripKeepSelectedUids.Contains(it.Uid);
            bool renamed = false;
            string renameTo;
            if (_stripKeepRenames.TryGetValue(it.Uid, out renameTo)
                && !string.IsNullOrEmpty(renameTo)
                && !string.Equals(renameTo, it.Uid, StringComparison.Ordinal))
                renamed = true;

            switch (_stripKeepViewFilter)
            {
                case StripKeepViewFilter.KeptOnly: return kept;
                case StripKeepViewFilter.DroppedOnly: return !kept;
                case StripKeepViewFilter.RenamedOnly: return renamed;
                case StripKeepViewFilter.TriggersOnly:
                    return SceneUtils.IsCreatorStripTriggerOrButtonType(it.Type);
                default: return true;
            }
        }

        private bool StripKeepItemVisible(StripKeepItem it)
        {
            return StripKeepItemMatchesFilter(it) && StripKeepItemMatchesView(it);
        }

        private bool StripKeepKindHasVisibleItems(CreatorStripKeepKind kind)
        {
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != kind) continue;
                if (StripKeepItemVisible(it)) return true;
            }
            return false;
        }

        private int CompareStripKeepItemsPower(StripKeepItem a, StripKeepItem b)
        {
            switch (_stripKeepSortMode)
            {
                case StripKeepSortMode.Name:
                {
                    int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (n != 0) return n;
                    return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
                }
                case StripKeepSortMode.Type:
                {
                    int t = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
                    if (t != 0) return t;
                    int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (n != 0) return n;
                    return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
                }
                case StripKeepSortMode.KeptFirst:
                {
                    bool ka = _stripKeepSelectedUids.Contains(a.Uid);
                    bool kb = _stripKeepSelectedUids.Contains(b.Uid);
                    if (ka != kb) return ka ? -1 : 1;
                    int ia = StripKeepKindIndex(a.Kind);
                    int ib = StripKeepKindIndex(b.Kind);
                    if (ia != ib) return ia.CompareTo(ib);
                    int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (n != 0) return n;
                    return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
                }
                default:
                {
                    int ia = StripKeepKindIndex(a.Kind);
                    int ib = StripKeepKindIndex(b.Kind);
                    if (ia != ib) return ia.CompareTo(ib);
                    int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (n != 0) return n;
                    return string.Compare(a.Uid, b.Uid, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void StripKeepResortItems()
        {
            _stripKeepItems.Sort(CompareStripKeepItemsPower);
        }

        private int StripKeepExpandMaskToInt()
        {
            int mask = 0;
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                CreatorStripKeepKind k = order[i];
                if (_stripKeepExpanded.Contains(k))
                    mask |= (int)k;
            }
            return mask;
        }

        private void StripKeepApplyExpandMask(int raw)
        {
            _stripKeepExpanded.Clear();
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                CreatorStripKeepKind k = order[i];
                if ((raw & (int)k) != 0)
                    _stripKeepExpanded.Add(k);
            }
        }

        private void StripKeepExpandAllPresent()
        {
            CreatorStripKeepKind[] order = SceneUtils.CreatorStripKeepDisplayOrder;
            for (int i = 0; i < order.Length; i++)
            {
                CreatorStripKeepKind k = order[i];
                int idx = StripKeepKindIndex(k);
                if (idx >= 0 && _stripKeepCounts[idx] > 0)
                    _stripKeepExpanded.Add(k);
            }
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void StripKeepCollapseAll()
        {
            _stripKeepExpanded.Clear();
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void StripKeepInvertSelection()
        {
            StripKeepClearSoftConfirm();
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                string uid = _stripKeepItems[i].Uid;
                if (_stripKeepSelectedUids.Contains(uid))
                    _stripKeepSelectedUids.Remove(uid);
                else
                    _stripKeepSelectedUids.Add(uid);
            }
            PersistStripKeepMaskFromSelection();
            RecalcStripKeepTotalsFromSelection();
            RebuildStripKeepToggleRows();
            RefreshStripKeepDefault3PChrome();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void StripKeepExpandOnlyKind(CreatorStripKeepKind kind)
        {
            int idx = StripKeepKindIndex(kind);
            if (idx < 0 || _stripKeepCounts[idx] <= 0) return;
            if (!_stripKeepExpanded.Contains(kind))
                _stripKeepExpanded.Add(kind);
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void OnStripKeepFilterChanged(string text)
        {
            _stripKeepFilterText = text ?? "";
            _stripKeepFilterLower = _stripKeepFilterText.Trim();
            RefreshStripKeepFilterClearVisible();
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void StripKeepClearFilter()
        {
            if (_stripKeepFilterInput != null)
            {
                _stripKeepFilterInput.text = "";
                try
                {
                    _stripKeepFilterInput.ActivateInputField();
                    _stripKeepFilterInput.MoveTextEnd(false);
                }
                catch { }
            }
            else
            {
                OnStripKeepFilterChanged("");
            }
        }

        private void RefreshStripKeepFilterClearVisible()
        {
            if (_stripKeepFilterClearGo == null) return;
            bool show = !string.IsNullOrEmpty(_stripKeepFilterText);
            if (_stripKeepFilterClearGo.activeSelf != show)
                _stripKeepFilterClearGo.SetActive(show);
        }

        private void StripKeepFocusFilterField()
        {
            if (_stripKeepFilterInput == null) return;
            try
            {
                _stripKeepFilterInput.ActivateInputField();
                _stripKeepFilterInput.MoveTextEnd(false);
            }
            catch { }
        }

        private void StripKeepSetViewFilter(StripKeepViewFilter filter)
        {
            _stripKeepViewFilter = filter;
            RefreshStripKeepViewChips();
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private void StripKeepCycleSortMode()
        {
            int next = ((int)_stripKeepSortMode + 1) % 4;
            _stripKeepSortMode = (StripKeepSortMode)next;
            StripKeepResortItems();
            RefreshStripKeepSortButtonLabel();
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        private string StripKeepSortModeLabel(StripKeepSortMode mode)
        {
            switch (mode)
            {
                case StripKeepSortMode.Name: return "Sort:name";
                case StripKeepSortMode.Type: return "Sort:type";
                case StripKeepSortMode.KeptFirst: return "Sort:kept";
                default: return "Sort:cat";
            }
        }

        private void RefreshStripKeepSortButtonLabel()
        {
            if (_stripKeepSortBtnLabel == null) return;
            string label = StripKeepSortModeLabel(_stripKeepSortMode);
            _stripKeepSortBtnLabel.text = label;
            LayoutElement le = _stripKeepSortBtnLabel.transform.parent != null
                ? _stripKeepSortBtnLabel.transform.parent.GetComponent<LayoutElement>()
                : null;
            if (le != null)
            {
                float s = ChromeScale;
                int font = new GalleryModalTypography(s).Body;
                float w = StripKeepChipWidth(label, font, s);
                le.minWidth = w;
                le.preferredWidth = w;
            }
        }

        private void StripKeepRegisterNav(bool isCategory, CreatorStripKeepKind kind, string uid, Image rowBg, Color baseBg)
        {
            StripKeepNavEntry e;
            e.IsCategory = isCategory;
            e.Kind = kind;
            e.Uid = uid;
            e.RowBg = rowBg;
            e.BaseBg = baseBg;
            _stripKeepNav.Add(e);
        }

        private void StripKeepClearNav()
        {
            _stripKeepNav.Clear();
            if (_stripKeepNavIndex >= _stripKeepNav.Count)
                _stripKeepNavIndex = _stripKeepNav.Count - 1;
        }

        private void StripKeepApplyNavHighlight()
        {
            for (int i = 0; i < _stripKeepNav.Count; i++)
            {
                StripKeepNavEntry e = _stripKeepNav[i];
                if (e.RowBg == null) continue;
                bool focused = i == _stripKeepNavIndex;
                e.RowBg.color = focused
                    ? new Color(0.18f, 0.28f, 0.38f, 1f)
                    : e.BaseBg;
            }
        }

        private void StripKeepMoveNav(int delta)
        {
            if (_stripKeepNav.Count <= 0)
            {
                _stripKeepNavIndex = -1;
                return;
            }
            if (_stripKeepNavIndex < 0)
                _stripKeepNavIndex = delta > 0 ? 0 : _stripKeepNav.Count - 1;
            else
                _stripKeepNavIndex = (_stripKeepNavIndex + delta + _stripKeepNav.Count) % _stripKeepNav.Count;
            StripKeepApplyNavHighlight();
        }

        private void StripKeepToggleFocusedRow()
        {
            if (_stripKeepNavIndex < 0 || _stripKeepNavIndex >= _stripKeepNav.Count) return;
            StripKeepNavEntry e = _stripKeepNav[_stripKeepNavIndex];
            if (e.IsCategory)
            {
                int selected;
                int total;
                CountStripKeepKindSelection(e.Kind, out selected, out total);
                if (total <= 0) return;
                OnStripKeepCategoryToggle(e.Kind, selected < total);
            }
            else if (!string.IsNullOrEmpty(e.Uid))
            {
                bool on = !_stripKeepSelectedUids.Contains(e.Uid);
                OnStripKeepItemToggle(e.Uid, on);
                if (_stripKeepSortMode != StripKeepSortMode.KeptFirst)
                    RebuildStripKeepToggleRows();
            }
        }

        private void StripKeepNavCollapseOrExpand(bool expand)
        {
            if (_stripKeepNavIndex < 0 || _stripKeepNavIndex >= _stripKeepNav.Count) return;
            StripKeepNavEntry e = _stripKeepNav[_stripKeepNavIndex];
            CreatorStripKeepKind kind = e.Kind;
            if (!e.IsCategory)
            {
                // Item focus: ← collapse parent, → expand parent.
            }
            int idx = StripKeepKindIndex(kind);
            if (idx < 0 || _stripKeepCounts[idx] <= 0) return;
            bool isExpanded = _stripKeepExpanded.Contains(kind);
            if (expand && !isExpanded)
            {
                _stripKeepExpanded.Add(kind);
                RebuildStripKeepToggleRows();
                RefreshStripKeepSummaryAndConfirm();
            }
            else if (!expand && isExpanded)
            {
                _stripKeepExpanded.Remove(kind);
                RebuildStripKeepToggleRows();
                RefreshStripKeepSummaryAndConfirm();
            }
        }

        private void StripKeepNavRenameFocused()
        {
            if (_stripKeepNavIndex < 0 || _stripKeepNavIndex >= _stripKeepNav.Count) return;
            StripKeepNavEntry e = _stripKeepNav[_stripKeepNavIndex];
            if (e.IsCategory || string.IsNullOrEmpty(e.Uid)) return;
            OpenStripKeepRenameOverlay(e.Uid);
        }

        /// <summary>
        /// Keyboard path while keep selector open. Returns true if consumed.
        /// </summary>
        private bool StripKeepHandleKeyboard()
        {
            if (!IsStripKeepSelectorOpen()) return false;
            if (_stripKeepRenameOverlayRoot != null) return false;
            if (IsStripKeepRecipeSaveInlineOpen()) return false;

            // ? = shortcut sheet (recognition). / = filter focus.
            if (Input.GetKeyDown(KeyCode.Question)
                || (Input.GetKeyDown(KeyCode.Slash)
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))))
            {
                ToggleStripKeepShortcutHelp();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Slash))
            {
                bool inFilter = false;
                try
                {
                    if (EventSystem.current != null
                        && EventSystem.current.currentSelectedGameObject != null
                        && _stripKeepFilterInput != null
                        && EventSystem.current.currentSelectedGameObject == _stripKeepFilterInput.gameObject)
                        inFilter = true;
                }
                catch { }
                if (!inFilter)
                {
                    StripKeepFocusFilterField();
                    return true;
                }
            }

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel.GetComponent<InputField>() != null) return false;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                StripKeepMoveNav(-1);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                StripKeepMoveNav(1);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                StripKeepToggleFocusedRow();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                StripKeepNavCollapseOrExpand(false);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                StripKeepNavCollapseOrExpand(true);
                return true;
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                StripKeepNavRenameFocused();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_stripKeepDropCount > 0)
                {
                    ConfirmStripKeepSelector();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Scroll list fills leftover height. Panel size = saved/default — never auto-snug to row count.
        /// </summary>
        private void ScheduleStripKeepSnugPanelHeight()
        {
            StripKeepEnsureScrollFlex();
        }

        private void StripKeepEnsureScrollFlex()
        {
            if (_stripKeepScrollHostLe == null) return;
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            _stripKeepScrollHostLe.minHeight = 80f * s;
            _stripKeepScrollHostLe.preferredHeight = -1f;
            _stripKeepScrollHostLe.flexibleHeight = 1f;
        }

        private void BuildStripKeepToolbarRows(Transform panel, float btnH, int font, float s)
        {
            // Filter + clear + expand/collapse + More
            GameObject filterRow = new GameObject("FilterRow");
            filterRow.transform.SetParent(panel, false);
            HorizontalLayoutGroup fh = UI.AddHLG(filterRow, spacing: 6f * s, padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: false);
            fh.childForceExpandHeight = false;
            fh.childAlignment = TextAnchor.MiddleLeft;
            fh.childControlWidth = true;
            UI.AddLE(filterRow, minHeight: btnH, preferredHeight: btnH, flexibleWidth: 1f);

            GameObject filterHost = new GameObject("FilterHost");
            filterHost.transform.SetParent(filterRow.transform, false);
            UI.AddLE(filterHost, minHeight: btnH, preferredHeight: btnH, flexibleWidth: 1f, minWidth: 80f * s);

            // Raised inset vs panel — looks like textbox, not section header.
            Color filterBg = GalleryUiColorTokens.SurfaceDarker;
            _stripKeepFilterInput = UI.CreateChromeLayoutInputField(
                filterHost.transform, font, btnH, 1f, 8f * s, 4f * s,
                filterBg, UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.creator.strip_filter_ph", "/ filter"),
                "StripFilter");
            if (_stripKeepFilterInput != null)
            {
                // Stretch input inside host so clear button can pin right.
                RectTransform inputRt = _stripKeepFilterInput.GetComponent<RectTransform>();
                if (inputRt != null)
                {
                    inputRt.anchorMin = Vector2.zero;
                    inputRt.anchorMax = Vector2.one;
                    inputRt.offsetMin = Vector2.zero;
                    inputRt.offsetMax = Vector2.zero;
                }
                LayoutElement inputLe = _stripKeepFilterInput.GetComponent<LayoutElement>();
                if (inputLe != null) inputLe.ignoreLayout = true;

                // Leave room for clear chip on right.
                Transform textArea = _stripKeepFilterInput.transform.Find("TextArea");
                if (textArea != null)
                {
                    RectTransform taRt = textArea.GetComponent<RectTransform>();
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-btnH * 0.85f, -4f * s);
                }

                _stripKeepFilterInput.onValueChanged.AddListener(OnStripKeepFilterChanged);
                if (!string.IsNullOrEmpty(_stripKeepFilterText))
                    _stripKeepFilterInput.text = _stripKeepFilterText;
                try { _stripKeepFilterInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(_stripKeepFilterInput); } catch { }
                Text tc = _stripKeepFilterInput.textComponent;
                if (tc != null) StripKeepClampText(tc);
                if (_stripKeepFilterInput.placeholder is Text ph)
                {
                    ph.fontStyle = FontStyle.Italic;
                    ph.color = new Color(0.55f, 0.57f, 0.62f, 1f);
                }
            }

            float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
            _stripKeepFilterClearGo = UI.CreateUIButton(
                filterHost, clearSz, clearSz, "X", font, 0, 0, AnchorPresets.middleRight,
                StripKeepClearFilter);
            if (_stripKeepFilterClearGo != null)
            {
                _stripKeepFilterClearGo.name = "FilterClear";
                RectTransform clearRt = _stripKeepFilterClearGo.GetComponent<RectTransform>();
                if (clearRt != null)
                {
                    clearRt.anchorMin = new Vector2(1f, 0.5f);
                    clearRt.anchorMax = new Vector2(1f, 0.5f);
                    clearRt.pivot = new Vector2(1f, 0.5f);
                    clearRt.sizeDelta = new Vector2(clearSz, clearSz);
                    clearRt.anchoredPosition = new Vector2(-2f * s, 0f);
                }
                Image clearImg = _stripKeepFilterClearGo.GetComponent<Image>();
                if (clearImg != null) clearImg.color = new Color(0f, 0f, 0f, 0.01f);
                try
                {
                    Sprite clearSpr = UI.LoadIconSprite("x", GalleryUiColorTokens.SearchClearIconTint);
                    if (clearSpr != null)
                        UI.AddIconToButton(_stripKeepFilterClearGo, clearSpr, GalleryUiDesignTokens.FloatChromeIconPadRef * s, new Color(0f, 0f, 0f, 0f));
                }
                catch { }
                Text clearLabel = _stripKeepFilterClearGo.GetComponentInChildren<Text>(true);
                if (clearLabel != null) clearLabel.gameObject.SetActive(false);
                AddTooltipPlain(_stripKeepFilterClearGo,
                    VPBTranslation.T("gallery.creator.strip_filter_clear", "Clear filter"));
            }
            RefreshStripKeepFilterClearVisible();

            string expandLbl = VPBTranslation.T("gallery.creator.strip_expand_all", "Expand");
            string collapseLbl = VPBTranslation.T("gallery.creator.strip_collapse_all", "Collapse");
            StripKeepChromeButton(filterRow.transform, StripKeepChipWidth(expandLbl, font, s), btnH,
                expandLbl, font, s,
                new Color(0.24f, 0.28f, 0.34f, 1f), StripKeepExpandAllPresent);
            StripKeepChromeButton(filterRow.transform, StripKeepChipWidth(collapseLbl, font, s), btnH,
                collapseLbl, font, s,
                new Color(0.24f, 0.28f, 0.34f, 1f), StripKeepCollapseAll);

            string moreLbl = _stripKeepMoreToolsExpanded
                ? VPBTranslation.T("gallery.creator.strip_more_hide", "Less")
                : VPBTranslation.T("gallery.creator.strip_more", "More");
            _stripKeepMoreToggleGo = StripKeepChromeButton(
                filterRow.transform, StripKeepChipWidth(moreLbl, font, s), btnH,
                moreLbl, font, s,
                new Color(0.26f, 0.30f, 0.38f, 1f), ToggleStripKeepMoreTools);
            AddTooltipPlain(_stripKeepMoreToggleGo,
                VPBTranslation.T("gallery.creator.strip_more_tip", "View filters, sort, possessable, rename modes"));

            // Secondary tools — collapsed by default (Hick).
            _stripKeepMoreToolsHost = new GameObject("MoreTools");
            _stripKeepMoreToolsHost.transform.SetParent(panel, false);
            VerticalLayoutGroup moreV = UI.AddVLG(_stripKeepMoreToolsHost, spacing: 0f, padding: UI.Pad(0, 0, 0, 0));
            moreV.childForceExpandHeight = false;
            UI.AddLE(_stripKeepMoreToolsHost, flexibleWidth: 1f, flexibleHeight: 0f);

            ScrollRect viewScroll;
            GameObject viewHostGo;
            Transform viewContent = StripKeepCreateHScrollChipStrip(
                _stripKeepMoreToolsHost.transform, "ViewRow", btnH * 0.85f, s, out viewScroll, out viewHostGo);

            BuildStripKeepViewChip(viewContent, StripKeepViewFilter.All, "All", btnH * 0.8f, font, s);
            BuildStripKeepViewChip(viewContent, StripKeepViewFilter.KeptOnly, "Kept", btnH * 0.8f, font, s);
            BuildStripKeepViewChip(viewContent, StripKeepViewFilter.DroppedOnly, "Drop", btnH * 0.8f, font, s);
            BuildStripKeepViewChip(viewContent, StripKeepViewFilter.RenamedOnly, "Renamed", btnH * 0.8f, font, s);
            BuildStripKeepViewChip(viewContent, StripKeepViewFilter.TriggersOnly, "Trig", btnH * 0.8f, font, s);
            Image trigImg;
            if (_stripKeepViewChipImgs.TryGetValue(StripKeepViewFilter.TriggersOnly, out trigImg) && trigImg != null)
            {
                AddTooltipPlain(trigImg.gameObject,
                    VPBTranslation.T(
                        "gallery.creator.strip_view_trig_tip",
                        "Show Triggers/Buttons only (UIButton, UIToggle, UISlider, *Trigger*)."));
            }

            string sortLbl = StripKeepSortModeLabel(_stripKeepSortMode);
            GameObject sortGo = StripKeepChromeButton(viewContent, StripKeepChipWidth(sortLbl, font, s), btnH * 0.8f,
                sortLbl, font, s,
                new Color(0.26f, 0.27f, 0.30f, 1f), StripKeepCycleSortMode);
            if (sortGo != null)
                _stripKeepSortBtnLabel = sortGo.GetComponentInChildren<Text>();

            string bulkLbl = VPBTranslation.T("gallery.creator.strip_bulk_actor", "Actor#");
            GameObject bulkGo = StripKeepChromeButton(viewContent, StripKeepChipWidth(bulkLbl, font, s), btnH * 0.8f,
                bulkLbl, font, s,
                new Color(0.30f, 0.26f, 0.18f, 1f), StripKeepBulkRenameSelectedPersons);
            if (sortGo != null)
            {
                AddTooltipPlain(sortGo,
                    VPBTranslation.T("gallery.creator.strip_sort_tip", "Cycle sort: type+name → name → type → kept"));
            }
            if (bulkGo != null)
            {
                AddTooltipPlain(bulkGo,
                    VPBTranslation.T("gallery.creator.strip_bulk_tip", "Rename selected Persons → Actor1, Actor2…"));
            }

            // Possessable + rename mode (Session Plugins parity) — always under More.
            BuildStripKeepPossessOptionsSection(_stripKeepMoreToolsHost.transform, btnH, font, s);

            RefreshStripKeepViewChips();
            RefreshStripKeepSortButtonLabel();
            ApplyStripKeepMoreToolsVisibility();
        }

        private void ToggleStripKeepMoreTools()
        {
            _stripKeepMoreToolsExpanded = !_stripKeepMoreToolsExpanded;
            ApplyStripKeepMoreToolsVisibility();
            RefreshStripKeepMoreToggleLabel();
        }

        private void ApplyStripKeepMoreToolsVisibility()
        {
            if (_stripKeepMoreToolsHost != null)
                _stripKeepMoreToolsHost.SetActive(_stripKeepMoreToolsExpanded);
        }

        private void RefreshStripKeepMoreToggleLabel()
        {
            if (_stripKeepMoreToggleGo == null) return;
            Text t = _stripKeepMoreToggleGo.GetComponentInChildren<Text>(true);
            if (t == null) return;
            t.text = _stripKeepMoreToolsExpanded
                ? VPBTranslation.T("gallery.creator.strip_more_hide", "Less")
                : VPBTranslation.T("gallery.creator.strip_more", "More");
        }

        private void BuildStripKeepViewChip(
            Transform parent, StripKeepViewFilter filter, string label, float h, int font, float s)
        {
            StripKeepViewFilter captured = filter;
            float w = StripKeepChipWidth(label, font, s);
            GameObject go = StripKeepChromeButton(parent, w, h, label, font, s,
                new Color(0.22f, 0.23f, 0.26f, 1f),
                () => StripKeepSetViewFilter(captured));
            Image img = go != null ? go.GetComponent<Image>() : null;
            if (img != null) _stripKeepViewChipImgs[filter] = img;
        }

        private void RefreshStripKeepViewChips()
        {
            Dictionary<StripKeepViewFilter, Image>.Enumerator en = _stripKeepViewChipImgs.GetEnumerator();
            while (en.MoveNext())
            {
                StripKeepViewFilter f = en.Current.Key;
                Image img = en.Current.Value;
                if (img == null) continue;
                img.color = f == _stripKeepViewFilter
                    ? new Color(0.18f, 0.36f, 0.48f, 1f)
                    : new Color(0.22f, 0.23f, 0.26f, 1f);
            }
        }

        /// <summary>
        /// Presets + Last + recipes in one wrap strip. Last sits with Default/+Save on row 1 when space.
        /// </summary>
        private void BuildStripKeepPresetsCollapseSection(Transform body, float btnH, int font, float s)
        {
            _stripKeepPresetsHost = new GameObject("PresetsHost");
            _stripKeepPresetsHost.transform.SetParent(body, false);
            VerticalLayoutGroup hostV = UI.AddVLG(_stripKeepPresetsHost, spacing: 4f * s, padding: UI.Pad(0, 0, 0, 0));
            hostV.childForceExpandHeight = false;
            hostV.childControlHeight = true;
            UI.AddLE(_stripKeepPresetsHost, flexibleWidth: 1f, flexibleHeight: 0f);

            float chipH = btnH * 0.85f;
            GameObject presetHostGo;
            LayoutElement presetHostLe;
            RectTransform contentRt;
            _stripKeepRecipeChipRow = StripKeepCreateWrapChipHost(
                _stripKeepPresetsHost.transform, "PresetRecipeRow", chipH, s,
                out presetHostGo, out presetHostLe, out contentRt);
            _stripKeepRecipeRowHost = presetHostGo;
            _stripKeepRecipeRowHostLe = presetHostLe;
            _stripKeepRecipeRowHostH = chipH;
            _stripKeepPresetChipContentRt = contentRt;

            RefreshStripKeepRecipeChips();
            BuildStripKeepRecipeSaveInlineRow(_stripKeepPresetsHost.transform, btnH, font, s);
        }

        private void RefreshStripKeepRecipeChips()
        {
            if (_stripKeepRecipeChipRow == null) return;
            float s = ChromeScale;
            int font = new GalleryModalTypography(s).Body;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s * 0.85f;
            UI.DestroyAllChildren(_stripKeepRecipeChipRow);

            string presetDefault = VPBTranslation.T("gallery.creator.strip_preset_default", "Default");
            string presetPlusSound = VPBTranslation.T("gallery.creator.strip_preset_plus_sound", "+Sound");
            string presetPersons = VPBTranslation.T("gallery.creator.strip_preset_persons", "Persons");
            string presetPersonsSnd = VPBTranslation.T("gallery.creator.strip_preset_persons_snd", "Persons+Snd");
            string presetPossess = VPBTranslation.T("gallery.creator.strip_preset_possess", "Possess");
            string presetAll = VPBTranslation.T("gallery.creator.strip_preset_all", "All");
            string presetNone = VPBTranslation.T("gallery.creator.strip_preset_none", "None");
            string presetInvert = VPBTranslation.T("gallery.creator.strip_preset_invert", "Invert");
            string saveLbl = VPBTranslation.T("gallery.creator.strip_recipe_save", "+Save");

            // Fixed presets first — Last follows so it lands on row 1 when width allows.
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetDefault, font, s), btnH,
                presetDefault, font, s,
                new Color(0.22f, 0.38f, 0.52f, 1f), () => StripKeepApplyPreset(SceneUtils.CreatorStripKeepDefault));
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetPlusSound, font, s), btnH,
                presetPlusSound, font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f),
                () => StripKeepApplyPreset(SceneUtils.CreatorStripKeepDefault | CreatorStripKeepKind.Sound));
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetPersons, font, s), btnH,
                presetPersons, font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f),
                () => StripKeepApplyPreset(CreatorStripKeepKind.Persons));
            GameObject personsSndBtn = StripKeepChromeButton(
                _stripKeepRecipeChipRow, StripKeepChipWidth(presetPersonsSnd, font, s), btnH,
                presetPersonsSnd, font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f),
                () => StripKeepApplyPreset(CreatorStripKeepKind.Persons | CreatorStripKeepKind.Sound));
            if (personsSndBtn != null)
            {
                AddTooltipPlain(personsSndBtn,
                    VPBTranslation.T(
                        "gallery.creator.strip_preset_persons_snd_tip",
                        "Keep Persons + Sound only (Session Plugins: Remove all but Persons and Sounds)."));
            }
            GameObject possessBtn = StripKeepChromeButton(
                _stripKeepRecipeChipRow, StripKeepChipWidth(presetPossess, font, s), btnH,
                presetPossess, font, s,
                new Color(0.34f, 0.28f, 0.42f, 1f), StripKeepApplyPossessionReadyPreset);
            if (possessBtn != null)
            {
                AddTooltipPlain(possessBtn,
                    VPBTranslation.T(
                        "gallery.creator.strip_preset_possess_tip",
                        "Persons only + clear possessables + head/hands by gender + Prefix rename."));
            }
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetAll, font, s), btnH,
                presetAll, font, s,
                new Color(0.28f, 0.28f, 0.32f, 1f), () => StripKeepApplyPreset(StripKeepPresentKindsMask()));
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetNone, font, s), btnH,
                presetNone, font, s,
                new Color(0.32f, 0.24f, 0.24f, 1f), () => StripKeepApplyPreset(CreatorStripKeepKind.None));
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(presetInvert, font, s), btnH,
                presetInvert, font, s,
                new Color(0.36f, 0.28f, 0.18f, 1f), StripKeepInvertSelection);
            StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(saveLbl, font, s), btnH,
                saveLbl, font, s,
                new Color(0.28f, 0.34f, 0.28f, 1f), OpenStripKeepRecipeSaveInline);

            bool hasLast = !string.IsNullOrEmpty(StripKeepPeekLastRecipeJson());
            if (hasLast)
            {
                string lastLbl = VPBTranslation.T("gallery.creator.strip_recipe_last", "Last");
                StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(lastLbl, font, s), btnH,
                    lastLbl, font, s,
                    new Color(0.22f, 0.38f, 0.32f, 1f), () => StripKeepApplyLastRecipe(true));
            }

            List<StripKeepRecipe> recipes = StripKeepLoadRecipesFromConfig();
            for (int i = 0; i < recipes.Count; i++)
            {
                StripKeepRecipe r = recipes[i];
                if (string.IsNullOrEmpty(r.Name)) continue;
                string name = r.Name;
                float w = StripKeepChipWidth(name, font, s);
                StripKeepChromeButton(_stripKeepRecipeChipRow, w, btnH, name, font, s,
                    new Color(0.26f, 0.28f, 0.34f, 1f),
                    () => StripKeepApplyNamedRecipe(name));
            }

            if (recipes.Count > 0)
            {
                string delLbl = VPBTranslation.T("gallery.creator.strip_recipe_del", "Del…");
                StripKeepChromeButton(_stripKeepRecipeChipRow, StripKeepChipWidth(delLbl, font, s), btnH,
                    delLbl, font, s,
                    new Color(0.34f, 0.24f, 0.24f, 1f), StripKeepDeleteFocusedOrLastRecipe);
            }

            if (_stripKeepRecipeRowHost != null)
                _stripKeepRecipeRowHost.SetActive(true);
            if (_stripKeepRecipeRowHostLe != null)
                _stripKeepRecipeRowHostLe.ignoreLayout = false;

            ScheduleStripKeepPresetChipFlow();
        }

        private void ScheduleStripKeepPresetChipFlow()
        {
            if (_stripKeepPresetFlowCo != null)
            {
                try { StopCoroutine(_stripKeepPresetFlowCo); } catch { }
                _stripKeepPresetFlowCo = null;
            }
            if (!IsStripKeepSelectorOpen()) return;
            try { _stripKeepPresetFlowCo = StartCoroutine(StripKeepPresetChipFlowCo()); }
            catch { StripKeepPresetChipFlowNow(); }
        }

        private System.Collections.IEnumerator StripKeepPresetChipFlowCo()
        {
            yield return null;
            try { Canvas.ForceUpdateCanvases(); } catch { }
            StripKeepPresetChipFlowNow();
            _stripKeepPresetFlowCo = null;
            ScheduleStripKeepSnugPanelHeight();
        }

        private void StripKeepPresetChipFlowNow()
        {
            if (_stripKeepPresetChipContentRt == null || _stripKeepRecipeRowHostLe == null) return;
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            float rowH = _stripKeepRecipeRowHostH > 1f
                ? _stripKeepRecipeRowHostH
                : GalleryUiDesignTokens.ButtonSizeRef * s * 0.85f;

            // Prefer panel width (stable during resize) — body pad 8+8.
            float availW = 0f;
            try
            {
                if (_stripKeepPanelRT != null)
                    availW = _stripKeepPanelRT.rect.width - 20f * s;
            }
            catch { }
            if (availW <= 1f)
            {
                try
                {
                    if (_stripKeepRecipeRowHost != null)
                    {
                        RectTransform hostRt = _stripKeepRecipeRowHost.GetComponent<RectTransform>();
                        if (hostRt != null) availW = hostRt.rect.width - 4f * s;
                    }
                }
                catch { }
            }
            if (availW <= 1f)
            {
                try { availW = _stripKeepPresetChipContentRt.rect.width; } catch { availW = 0f; }
            }
            if (availW <= 1f) availW = 400f * s;

            StripKeepFlowWrapChips(
                _stripKeepPresetChipContentRt, _stripKeepRecipeRowHostLe, rowH, s, availW);
        }

        // --- Recipes ---

        private struct StripKeepRecipe
        {
            public string Name;
            public int Mask;
            public int Expand;
            public Dictionary<string, string> Renames;
            public bool PossClear;
            public bool PossMale;
            public bool PossFemale;
            public int RenameMode;
            public bool HasPossessOptions;
        }

        private static string StripKeepPeekLastRecipeJson()
        {
            try
            {
                if (VPBConfig.Instance == null) return null;
                return VPBConfig.Instance.CreatorStripLastRecipeJson;
            }
            catch { return null; }
        }

        private List<StripKeepRecipe> StripKeepLoadRecipesFromConfig()
        {
            List<StripKeepRecipe> list = new List<StripKeepRecipe>(StripKeepMaxRecipes);
            string raw = null;
            try
            {
                if (VPBConfig.Instance != null)
                    raw = VPBConfig.Instance.CreatorStripRecipesJson;
            }
            catch { }
            if (string.IsNullOrEmpty(raw) || raw == "[]") return list;

            JSONNode root = null;
            try { root = JSON.Parse(raw); } catch { root = null; }
            if (root == null) return list;
            JSONArray arr = root as JSONArray;
            if (arr == null) return list;

            for (int i = 0; i < arr.Count && list.Count < StripKeepMaxRecipes; i++)
            {
                JSONClass jc = arr[i] as JSONClass;
                if (jc == null) continue;
                StripKeepRecipe r;
                r.Name = jc["name"] != null ? jc["name"].Value : null;
                r.Mask = jc["mask"] != null ? jc["mask"].AsInt : 0;
                r.Expand = jc["expand"] != null ? jc["expand"].AsInt : 0;
                r.Renames = new Dictionary<string, string>(StringComparer.Ordinal);
                r.PossClear = true;
                r.PossMale = true;
                r.PossFemale = false;
                r.RenameMode = 0;
                r.HasPossessOptions = false;
                if (jc["possClear"] != null || jc["possMale"] != null || jc["renameMode"] != null)
                {
                    r.HasPossessOptions = true;
                    if (jc["possClear"] != null) r.PossClear = jc["possClear"].AsBool;
                    if (jc["possMale"] != null) r.PossMale = jc["possMale"].AsBool;
                    if (jc["possFemale"] != null) r.PossFemale = jc["possFemale"].AsBool;
                    if (jc["renameMode"] != null) r.RenameMode = jc["renameMode"].AsInt;
                }
                JSONClass ren = jc["renames"] as JSONClass;
                if (ren != null)
                {
                    foreach (string k in ren.Keys)
                    {
                        if (string.IsNullOrEmpty(k) || ren[k] == null) continue;
                        string v = ren[k].Value;
                        if (string.IsNullOrEmpty(v)) continue;
                        r.Renames[k] = v;
                    }
                }
                if (!string.IsNullOrEmpty(r.Name))
                    list.Add(r);
            }
            return list;
        }

        private void StripKeepPersistRecipes(List<StripKeepRecipe> recipes)
        {
            JSONArray arr = new JSONArray();
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Count && i < StripKeepMaxRecipes; i++)
                {
                    StripKeepRecipe r = recipes[i];
                    if (string.IsNullOrEmpty(r.Name)) continue;
                    JSONClass jc = new JSONClass();
                    jc["name"] = r.Name;
                    jc["mask"].AsInt = r.Mask & (int)SceneUtils.CreatorStripKeepAllUser;
                    jc["expand"].AsInt = r.Expand & (int)SceneUtils.CreatorStripKeepAllUser;
                    jc["possClear"].AsBool = r.PossClear;
                    jc["possMale"].AsBool = r.PossMale;
                    jc["possFemale"].AsBool = r.PossFemale;
                    jc["renameMode"].AsInt = r.RenameMode;
                    JSONClass ren = new JSONClass();
                    if (r.Renames != null)
                    {
                        Dictionary<string, string>.Enumerator en = r.Renames.GetEnumerator();
                        while (en.MoveNext())
                        {
                            if (string.IsNullOrEmpty(en.Current.Key) || string.IsNullOrEmpty(en.Current.Value))
                                continue;
                            ren[en.Current.Key] = en.Current.Value;
                        }
                    }
                    jc["renames"] = ren;
                    arr.Add(jc);
                }
            }
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.CreatorStripRecipesJson = arr.ToString();
                VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        private void StripKeepPersistLastRecipeFromSelection()
        {
            JSONClass jc = new JSONClass();
            PersistStripKeepMaskFromSelection();
            jc["mask"].AsInt = (int)(_stripKeepMask & SceneUtils.CreatorStripKeepAllUser);
            jc["expand"].AsInt = StripKeepExpandMaskToInt();
            JSONArray uids = new JSONArray();
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (_stripKeepSelectedUids.Contains(it.Uid))
                    uids.Add(it.Uid);
            }
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid))
                uids.Add(StripKeepSyntheticDefault3PUid);
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
                uids.Add(StripKeepSyntheticImportSubSceneUid);
            jc["uids"] = uids;
            JSONClass ren = new JSONClass();
            Dictionary<string, string>.Enumerator en = _stripKeepRenames.GetEnumerator();
            while (en.MoveNext())
            {
                string k = en.Current.Key;
                string v = en.Current.Value;
                if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v)) continue;
                if (string.Equals(k, v, StringComparison.Ordinal)) continue;
                if (!_stripKeepSelectedUids.Contains(k)) continue;
                ren[k] = v;
            }
            jc["renames"] = ren;
            StripKeepWritePossessOptionsToJson(jc);
            StripKeepWriteCreateOptionsToJson(jc);
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.CreatorStripLastRecipeJson = jc.ToString();
                VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        private bool StripKeepTryApplyLastRecipeOnOpen()
        {
            return StripKeepApplyLastRecipe(false);
        }

        private bool StripKeepApplyLastRecipe(bool rebuildUi)
        {
            string raw = StripKeepPeekLastRecipeJson();
            if (string.IsNullOrEmpty(raw)) return false;

            JSONNode root = null;
            try { root = JSON.Parse(raw); } catch { root = null; }
            JSONClass jc = root as JSONClass;
            if (jc == null) return false;

            int mask = jc["mask"] != null ? jc["mask"].AsInt : 0;
            int expand = jc["expand"] != null ? jc["expand"].AsInt : 0;
            _stripKeepMask = (CreatorStripKeepKind)(mask & (int)SceneUtils.CreatorStripKeepAllUser);
            if (_stripKeepMask == CreatorStripKeepKind.None)
                _stripKeepMask = SceneUtils.CreatorStripKeepDefault;

            HashSet<string> want = new HashSet<string>(StringComparer.Ordinal);
            JSONArray uids = jc["uids"] as JSONArray;
            if (uids != null)
            {
                for (int i = 0; i < uids.Count; i++)
                {
                    if (uids[i] == null) continue;
                    string id = uids[i].Value;
                    if (!string.IsNullOrEmpty(id)) want.Add(id);
                }
            }

            _stripKeepSelectedUids.Clear();
            StripKeepClearSoftConfirm();
            if (want.Count > 0)
            {
                for (int i = 0; i < _stripKeepItems.Count; i++)
                {
                    if (want.Contains(_stripKeepItems[i].Uid))
                        _stripKeepSelectedUids.Add(_stripKeepItems[i].Uid);
                }
                if (want.Contains(StripKeepSyntheticDefault3PUid)
                    && StripKeepShouldShowDefault3P())
                    _stripKeepSelectedUids.Add(StripKeepSyntheticDefault3PUid);
                if (want.Contains(StripKeepSyntheticImportSubSceneUid)
                    && StripKeepShouldShowDefault3P())
                {
                    _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                    _stripKeepSelectedUids.Add(StripKeepSyntheticImportSubSceneUid);
                }
            }
            else
            {
                SeedStripKeepSelectionFromMask();
            }

            _stripKeepRenames.Clear();
            JSONClass ren = jc["renames"] as JSONClass;
            if (ren != null)
            {
                foreach (string k in ren.Keys)
                {
                    if (string.IsNullOrEmpty(k) || ren[k] == null) continue;
                    string v = ren[k].Value;
                    if (string.IsNullOrEmpty(v)) continue;
                    bool present = false;
                    for (int i = 0; i < _stripKeepItems.Count; i++)
                    {
                        if (string.Equals(_stripKeepItems[i].Uid, k, StringComparison.Ordinal))
                        {
                            present = true;
                            break;
                        }
                    }
                    if (present) _stripKeepRenames[k] = v;
                }
            }

            StripKeepApplyExpandMask(expand);
            StripKeepReadPossessOptionsFromJson(jc);
            StripKeepReadCreateOptionsFromJson(jc);
            RecalcStripKeepTotalsFromSelection();
            PersistStripKeepMaskFromSelection();

            if (rebuildUi && IsStripKeepSelectorOpen())
            {
                RebuildStripKeepToggleRows();
                RefreshStripKeepDefault3PChrome();
                RefreshStripKeepSummaryAndConfirm();
                ScheduleStripKeepSnugPanelHeight();
            }
            return true;
        }

        private void StripKeepApplyNamedRecipe(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            List<StripKeepRecipe> recipes = StripKeepLoadRecipesFromConfig();
            StripKeepRecipe found = default(StripKeepRecipe);
            bool ok = false;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (string.Equals(recipes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = recipes[i];
                    ok = true;
                    break;
                }
            }
            if (!ok) return;

            _stripKeepMask = (CreatorStripKeepKind)(found.Mask & (int)SceneUtils.CreatorStripKeepAllUser);
            if (_stripKeepMask == CreatorStripKeepKind.None)
                _stripKeepMask = SceneUtils.CreatorStripKeepDefault;
            SeedStripKeepSelectionFromMask();
            StripKeepApplyExpandMask(found.Expand);

            if (found.HasPossessOptions)
            {
                _stripKeepRemovePossessable = found.PossClear;
                _stripKeepAddPossessableMale = found.PossMale;
                _stripKeepAddPossessableFemale = found.PossFemale;
                int rm = found.RenameMode;
                if (rm < 0) rm = 0;
                if (rm > 3) rm = 3;
                _stripKeepPersonRenameMode = (StripKeepPersonRenameMode)rm;
                PersistStripKeepPossessOptionsToConfig();
                RefreshStripKeepPossessOptionsChrome();
            }

            if (found.Renames != null)
            {
                Dictionary<string, string>.Enumerator en = found.Renames.GetEnumerator();
                while (en.MoveNext())
                {
                    string k = en.Current.Key;
                    string v = en.Current.Value;
                    if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v)) continue;
                    bool present = false;
                    for (int i = 0; i < _stripKeepItems.Count; i++)
                    {
                        if (string.Equals(_stripKeepItems[i].Uid, k, StringComparison.Ordinal))
                        {
                            present = true;
                            break;
                        }
                    }
                    if (present) _stripKeepRenames[k] = v;
                }
            }

            RecalcStripKeepTotalsFromSelection();
            RebuildStripKeepToggleRows();
            RefreshStripKeepDefault3PChrome();
            RefreshStripKeepSummaryAndConfirm();
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.creator.strip_recipe_loaded", "Recipe '{0}' loaded."),
                    name),
                1.25f);
        }

        private void StripKeepSaveNamedRecipe(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            PersistStripKeepMaskFromSelection();
            StripKeepRecipe r;
            r.Name = name;
            r.Mask = (int)(_stripKeepMask & SceneUtils.CreatorStripKeepAllUser);
            r.Expand = StripKeepExpandMaskToInt();
            r.PossClear = _stripKeepRemovePossessable;
            r.PossMale = _stripKeepAddPossessableMale;
            r.PossFemale = _stripKeepAddPossessableFemale;
            r.RenameMode = (int)_stripKeepPersonRenameMode;
            r.HasPossessOptions = true;
            r.Renames = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string>.Enumerator en = _stripKeepRenames.GetEnumerator();
            while (en.MoveNext())
            {
                string k = en.Current.Key;
                string v = en.Current.Value;
                if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v)) continue;
                if (string.Equals(k, v, StringComparison.Ordinal)) continue;
                r.Renames[k] = v;
            }

            List<StripKeepRecipe> recipes = StripKeepLoadRecipesFromConfig();
            bool replaced = false;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (string.Equals(recipes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    recipes[i] = r;
                    replaced = true;
                    break;
                }
            }
            if (!replaced)
            {
                if (recipes.Count >= StripKeepMaxRecipes)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.creator.strip_recipe_full", "Recipe slots full (8). Delete one first."),
                        2f);
                    return;
                }
                recipes.Add(r);
            }
            StripKeepPersistRecipes(recipes);
            RefreshStripKeepRecipeChips();
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.creator.strip_recipe_saved", "Recipe '{0}' saved."),
                    name),
                1.5f);
        }

        private void StripKeepDeleteFocusedOrLastRecipe()
        {
            List<StripKeepRecipe> recipes = StripKeepLoadRecipesFromConfig();
            if (recipes.Count <= 0) return;
            string removed = recipes[recipes.Count - 1].Name;
            recipes.RemoveAt(recipes.Count - 1);
            StripKeepPersistRecipes(recipes);
            RefreshStripKeepRecipeChips();
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.creator.strip_recipe_deleted", "Deleted recipe '{0}'."),
                    removed),
                1.5f);
        }

        private void HideStripKeepRecipeSaveOverlay()
        {
            HideStripKeepRecipeSaveInline();
        }

        private bool IsStripKeepRecipeSaveInlineOpen()
        {
            return _stripKeepRecipeSaveInlineHost != null && _stripKeepRecipeSaveInlineHost.activeSelf;
        }

        private void BuildStripKeepRecipeSaveInlineRow(Transform parent, float btnH, int font, float s)
        {
            _stripKeepRecipeSaveInlineHost = new GameObject("RecipeSaveInline");
            _stripKeepRecipeSaveInlineHost.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = UI.AddHLG(
                _stripKeepRecipeSaveInlineHost, spacing: 6f * s, padding: UI.Pad(2, 2, 2, 2, s),
                childForceExpandWidth: false, childForceExpandHeight: false,
                childControlWidth: true, childControlHeight: true,
                childAlignment: TextAnchor.MiddleLeft);
            UI.AddLE(_stripKeepRecipeSaveInlineHost, minHeight: btnH, preferredHeight: btnH, flexibleWidth: 1f);
            UI.AddImage(_stripKeepRecipeSaveInlineHost, new Color(0.10f, 0.12f, 0.14f, 1f));

            GameObject inputHost = new GameObject("NameHost");
            inputHost.transform.SetParent(_stripKeepRecipeSaveInlineHost.transform, false);
            UI.AddLE(inputHost, minHeight: btnH * 0.92f, preferredHeight: btnH * 0.92f, flexibleWidth: 1f, minWidth: 80f * s);

            _stripKeepRecipeSaveInlineInput = UI.CreateChromeLayoutInputField(
                inputHost.transform, font, btnH * 0.92f, 1f, 8f * s, 4f * s,
                new Color(0.16f, 0.17f, 0.21f, 1f), UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.creator.strip_recipe_name_ph", "Workbench / Lighting pass / Audio only"),
                "RecipeNameInline");
            if (_stripKeepRecipeSaveInlineInput != null)
            {
                RectTransform inputRt = _stripKeepRecipeSaveInlineInput.GetComponent<RectTransform>();
                if (inputRt != null)
                {
                    inputRt.anchorMin = Vector2.zero;
                    inputRt.anchorMax = Vector2.one;
                    inputRt.offsetMin = Vector2.zero;
                    inputRt.offsetMax = Vector2.zero;
                }
                LayoutElement ile = _stripKeepRecipeSaveInlineInput.GetComponent<LayoutElement>();
                if (ile != null) ile.ignoreLayout = true;
                try
                {
                    _stripKeepRecipeSaveInlineHost.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(_stripKeepRecipeSaveInlineInput);
                }
                catch { }
            }

            string cancelLbl = VPBTranslation.T("gallery.creator.strip_rename_cancel", "Cancel");
            StripKeepChromeButton(_stripKeepRecipeSaveInlineHost.transform,
                StripKeepChipWidth(cancelLbl, font, s), btnH * 0.92f,
                cancelLbl, font, s,
                new Color(0.35f, 0.28f, 0.22f, 1f), HideStripKeepRecipeSaveInline);

            string applyLbl = VPBTranslation.T("gallery.creator.strip_recipe_save_apply", "Save");
            StripKeepChromeButton(_stripKeepRecipeSaveInlineHost.transform,
                StripKeepChipWidth(applyLbl, font, s), btnH * 0.92f,
                applyLbl, font, s,
                new Color(0.22f, 0.42f, 0.32f, 1f), CommitStripKeepRecipeSaveInline);

            _stripKeepRecipeSaveInlineHost.SetActive(false);
        }

        private void OpenStripKeepRecipeSaveInline()
        {
            if (_stripKeepRecipeSaveInlineHost == null) return;
            try { HideStripKeepShortcutHelp(); } catch { }
            _stripKeepAwaitingSoftConfirm = false;
            _stripKeepRecipeSaveInlineHost.SetActive(true);
            try
            {
                if (_stripKeepRecipeSaveInlineInput != null)
                {
                    _stripKeepRecipeSaveInlineInput.text = "";
                    _stripKeepRecipeSaveInlineInput.ActivateInputField();
                    _stripKeepRecipeSaveInlineInput.MoveTextEnd(false);
                }
            }
            catch { }
            try { RefreshStripKeepSummaryAndConfirm(); } catch { }
        }

        private void HideStripKeepRecipeSaveInline()
        {
            if (_stripKeepRecipeSaveInlineHost != null)
                _stripKeepRecipeSaveInlineHost.SetActive(false);
            if (_stripKeepRecipeSaveInlineInput != null)
                _stripKeepRecipeSaveInlineInput.text = "";
        }

        private void CommitStripKeepRecipeSaveInline()
        {
            string n = _stripKeepRecipeSaveInlineInput != null ? _stripKeepRecipeSaveInlineInput.text : null;
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(n.Trim()))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.rename.empty", "Enter a name."), 2f);
                return;
            }
            StripKeepSaveNamedRecipe(n);
            HideStripKeepRecipeSaveInline();
        }

        private void OpenStripKeepRecipeSaveOverlay()
        {
            OpenStripKeepRecipeSaveInline();
        }

        private void ToggleStripKeepShortcutHelp()
        {
            if (_stripKeepShortcutHelpVisible) HideStripKeepShortcutHelp();
            else ShowStripKeepShortcutHelp();
        }

        private void ShowStripKeepShortcutHelp()
        {
            if (_stripKeepShortcutHelpGO == null) return;
            _stripKeepShortcutHelpVisible = true;
            _stripKeepShortcutHelpGO.SetActive(true);
        }

        private void HideStripKeepShortcutHelp()
        {
            _stripKeepShortcutHelpVisible = false;
            if (_stripKeepShortcutHelpGO != null)
                _stripKeepShortcutHelpGO.SetActive(false);
        }

        private void BuildStripKeepShortcutHelpRow(Transform parent, float btnH, int font, float s)
        {
            _stripKeepShortcutHelpGO = new GameObject("ShortcutHelp");
            _stripKeepShortcutHelpGO.transform.SetParent(parent, false);
            UI.AddLE(_stripKeepShortcutHelpGO, minHeight: btnH * 1.6f, preferredHeight: btnH * 1.6f, flexibleWidth: 1f);
            UI.AddImage(_stripKeepShortcutHelpGO, new Color(0.09f, 0.11f, 0.14f, 1f));
            if (_stripKeepShortcutHelpGO.GetComponent<RectMask2D>() == null)
                _stripKeepShortcutHelpGO.AddComponent<RectMask2D>();

            Text help = UI.CreateLabel(_stripKeepShortcutHelpGO,
                VPBTranslation.T(
                    "gallery.creator.strip_shortcuts",
                    "Ctrl+Shift+S toggle · / filter · ? help · ↑↓ nav · Space toggle · ←→ expand · F2 rename · Enter strip · Esc back · More: possess + rename"),
                font, new Color(0.75f, 0.78f, 0.82f, 1f), TextAnchor.MiddleLeft,
                raycastTarget: false, name: "HelpText");
            GalleryUiMetrics.ApplyFont(help, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            StripKeepClampText(help);
            RectTransform hrt = help.GetComponent<RectTransform>();
            if (hrt != null)
            {
                hrt.anchorMin = Vector2.zero;
                hrt.anchorMax = Vector2.one;
                hrt.offsetMin = new Vector2(8f * s, 2f * s);
                hrt.offsetMax = new Vector2(-8f * s, -2f * s);
            }
            _stripKeepShortcutHelpGO.SetActive(false);
            _stripKeepShortcutHelpVisible = false;
        }

        // Kept for API compatibility — redirects to inline.

        private void StripKeepBulkRenameSelectedPersons()
        {
            int n = 0;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != CreatorStripKeepKind.Persons) continue;
                if (!_stripKeepSelectedUids.Contains(it.Uid)) continue;
                n++;
            }

            if (n <= 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_bulk_none", "Select one or more Persons first."),
                    2f);
                return;
            }

            StripKeepBulkRenameSelectedPersonsInternal(overwriteExisting: true);
            _stripKeepPersonRenameMode = StripKeepPersonRenameMode.ActorNumber;
            PersistStripKeepPossessOptionsToConfig();
            RefreshStripKeepRenameModeButtonLabel();

            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.creator.strip_bulk_done", "Renamed {0} person(s) → Actor#."),
                    n),
                1.5f);
        }

        private bool StripKeepBulkNameTaken(string newName, string exceptUid)
        {
            if (SceneUtils.IsSystemProtectedAtomId(newName)) return true;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem other = _stripKeepItems[i];
                if (string.Equals(other.Uid, exceptUid, StringComparison.Ordinal)) continue;
                if (!_stripKeepSelectedUids.Contains(other.Uid)) continue;
                if (string.Equals(StripKeepFinalNameForUid(other.Uid), newName, StringComparison.Ordinal))
                    return true;
            }
            // Also clash with Actor names we just assigned in renames map for other uids.
            Dictionary<string, string>.Enumerator en = _stripKeepRenames.GetEnumerator();
            while (en.MoveNext())
            {
                if (string.Equals(en.Current.Key, exceptUid, StringComparison.Ordinal)) continue;
                if (string.Equals(en.Current.Value, newName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
