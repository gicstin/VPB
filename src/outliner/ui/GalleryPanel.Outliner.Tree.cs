using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal sealed class OutlinerRowBind : MonoBehaviour
        {
            internal int VisibleIndex;
            internal string NodeId;
            internal string AtomUid;
            internal Image Bg;
            internal Image Icon;
            internal Text Label;
            internal Text Sub;
            internal Text State;
            internal GameObject MenuBtn;
            internal GameObject ChevronBtn;
            internal GameObject EyeBtn;
            internal int EyeState;
            internal string EyeTip;
            internal string Tip;
            internal string TipNodeId;
            internal string TipSubLabel;
            internal int TipFacts;
        }

        RectTransform _outlinerTypeNavRowRT;
        GameObject _outlinerTypeNavRowGO;
        GameObject _outlinerTypeNavIconsGO;
        InputField _outlinerFilterInput;
        GameObject _outlinerTreeScrollGO;
        ScrollRect _outlinerTreeScroll;
        RectTransform _outlinerTreeContentRT;
        readonly List<GameObject> _outlinerRowPool = new List<GameObject>(OutlinerPoolSize);
        int _outlinerVirtFirst = -1;
        GameObject _outlinerEmptyRow;
        int _outlinerSelectAnchorVis = -1;

        void BuildOutlinerTreePane(GameObject body, float gap, float s)
        {
            _outlinerTreePane = UI.CreateChildRT(body, "TreePane", AnchorPresets.stretchAll);
            UI.AddImage(_outlinerTreePane, OutlinerPaneBg);
            if (_outlinerTreePane.GetComponent<RectMask2D>() == null)
                _outlinerTreePane.AddComponent<RectMask2D>();

            _outlinerTypeNavRowRT = UI.CreateChildRT(_outlinerTreePane, "TypeNav", AnchorPresets.hStretchTop,
                new Vector2(0f, OutlinerSlotH(s)), Vector2.zero).GetComponent<RectTransform>();
            BuildOutlinerTypeNavRow(_outlinerTypeNavRowRT.gameObject, s);

            _outlinerTreeScrollGO = UI.CreateVScrollableContent(_outlinerTreePane, OutlinerPaneBg,
                AnchorPresets.stretchAll, 0f, 0f, Vector2.zero,
                GalleryUiDesignTokens.SideTabScrollBarWidthRef * s, GalleryUiDesignTokens.HairGapRef * s, false);
            RectTransform scrollRT = _outlinerTreeScrollGO.GetComponent<RectTransform>();
            scrollRT.offsetMax = new Vector2(0f, -OutlinerSlotH(s));
            _outlinerTreeScroll = _outlinerTreeScrollGO.GetComponent<ScrollRect>();
            _outlinerTreeContentRT = _outlinerTreeScroll != null ? _outlinerTreeScroll.content : null;
            DisableOutlinerVirtContentLayout(_outlinerTreeContentRT);
            if (_outlinerTreeScroll != null)
                _outlinerTreeScroll.onValueChanged.AddListener(_ => UpdateOutlinerVirtualVisible(false));

            EnsureOutlinerRowPool();
            GameObject emptyHost = _outlinerTreeScroll != null && _outlinerTreeScroll.viewport != null
                ? _outlinerTreeScroll.viewport.gameObject
                : _outlinerTreePane;
            Text emptyTxt = UI.CreateLabel(emptyHost, "", GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter);
            GalleryUiMetrics.ApplyFont(emptyTxt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            _outlinerEmptyRow = emptyTxt.gameObject;
            RectTransform emptyRT = emptyTxt.rectTransform;
            emptyRT.anchorMin = Vector2.zero;
            emptyRT.anchorMax = Vector2.one;
            emptyRT.offsetMin = Vector2.zero;
            emptyRT.offsetMax = Vector2.zero;
            LayoutElement emptyLe = _outlinerEmptyRow.GetComponent<LayoutElement>();
            if (emptyLe == null) emptyLe = _outlinerEmptyRow.AddComponent<LayoutElement>();
            emptyLe.ignoreLayout = true;
            _outlinerEmptyRow.SetActive(false);
            LayoutOutlinerTreeChrome();
        }

        static void DisableOutlinerVirtContentLayout(RectTransform content)
        {
            if (content == null) return;
            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.enabled = false;
                UnityEngine.Object.Destroy(vlg);
            }
            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.enabled = false;
                UnityEngine.Object.Destroy(csf);
            }
            Transform spacer = content.Find("BottomSpacer");
            if (spacer != null)
            {
                spacer.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(spacer.gameObject);
            }
        }

        void BuildOutlinerTypeNavRow(GameObject row, float s)
        {
            UI.AddImage(row, OutlinerBarBg);
            float slot = OutlinerSlotH(s);
            float gap = GalleryUiDesignTokens.TightGapRef * s;
            UI.AddHLG(row, gap,
                UI.Pad(GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef,
                    GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef, s),
                TextAnchor.MiddleLeft, true, true, false, false);
            GameObject icons = new GameObject("Icons");
            icons.transform.SetParent(row.transform, false);
            UI.AddHLG(icons, gap, UI.Pad(0, 0, 0, 0),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(icons, minHeight: slot, preferredHeight: slot, flexibleWidth: 0f, flexibleHeight: 0f);
            ContentSizeFitter fit = icons.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _outlinerTypeNavIconsGO = icons;
            _outlinerTypeNavRowGO = row;
            BuildOutlinerFilterField(row, slot, s);
            RebuildOutlinerTypeNav();
        }

        void SyncOutlinerTypeNavPresence()
        {
            CreatorStripKeepKind present = OutlinerModelBuilder.PresentKinds(_outlinerFacts);
            CreatorStripKeepKind clipped = OutlinerModelBuilder.ClipKindMask(_outlinerKindMask, present);
            bool changed = present != _outlinerPresentKinds || clipped != _outlinerKindMask;
            _outlinerPresentKinds = present;
            _outlinerKindMask = clipped;
            if (changed || _outlinerTypeNavIconsGO == null || _outlinerTypeNavIconsGO.transform.childCount == 0)
                RebuildOutlinerTypeNav();
        }

        void LayoutOutlinerTreeChrome()
        {
            if (_outlinerTypeNavRowRT == null) return;
            float s = OutlinerLiveScale();
            float slot = OutlinerSlotH(s);
            float pad = GalleryUiDesignTokens.TightGapRef * s;
            float typeH = slot + pad * 2f;
            _outlinerTypeNavRowRT.sizeDelta = new Vector2(0f, typeH);
            _outlinerTypeNavRowRT.anchoredPosition = Vector2.zero;
            if (_outlinerTreeScrollGO != null)
            {
                RectTransform scrollRT = _outlinerTreeScrollGO.GetComponent<RectTransform>();
                if (scrollRT != null)
                    scrollRT.offsetMax = new Vector2(0f, -typeH);
            }
        }

        void RebuildOutlinerTypeNav()
        {
            if (_outlinerTypeNavIconsGO == null) return;
            UI.DestroyAllChildren(_outlinerTypeNavIconsGO.transform);
            float s = OutlinerLiveScale();
            float h = OutlinerSlotH(s);
            AddOutlinerTypeNavButton(CreatorStripKeepKind.None, "layout-list",
                VPBTranslation.T("intel.kind.all", "Everything"), h, s);
            CreatorStripKeepKind present = _outlinerPresentKinds;
            for (int i = 0; i < OutlinerAtomKindOrder.Length; i++)
            {
                CreatorStripKeepKind kind = OutlinerAtomKindOrder[i];
                if (present != CreatorStripKeepKind.None && (present & kind) == 0) continue;
                if (present == CreatorStripKeepKind.None) continue;
                AddOutlinerTypeNavButton(kind, OutlinerAtomKindIcon(kind),
                    OutlinerAtomKindLabel(kind), h, s);
            }
            LayoutOutlinerTreeChrome();
            try { UI.ApplyFloatRootHoverPolicy(_outlinerTypeNavRowGO); } catch { }
        }

        void AddOutlinerTypeNavButton(CreatorStripKeepKind kind, string iconRole, string label, float h, float s)
        {
            bool on = kind == CreatorStripKeepKind.None
                ? _outlinerKindMask == CreatorStripKeepKind.None
                : (_outlinerKindMask & kind) != 0;
            Color well = on ? GalleryUiColorTokens.ActiveSelected : GalleryUiColorTokens.RowIdle;
            GameObject btn = UI.CreateFloatChromeIconButton(
                _outlinerTypeNavIconsGO.transform, h, iconRole, well, () => ToggleOutlinerKindFilter(kind));
            if (btn == null) return;
            UI.AddLE(btn, minWidth: h, preferredWidth: h, minHeight: h, preferredHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(btn, OutlinerTypeNavTip(kind, label));
        }

        static string OutlinerTypeNavTip(CreatorStripKeepKind kind, string label)
        {
            if (kind == CreatorStripKeepKind.None)
                return VPBTranslation.T("intel.kind.all", "Everything");
            return label;
        }

        void ToggleOutlinerKindFilter(CreatorStripKeepKind kind)
        {
            if (kind == CreatorStripKeepKind.None)
                _outlinerKindMask = CreatorStripKeepKind.None;
            else
            {
                if (_outlinerKindMask == CreatorStripKeepKind.None)
                    _outlinerKindMask = kind;
                else if ((_outlinerKindMask & kind) != 0)
                {
                    _outlinerKindMask &= ~kind;
                    if (_outlinerKindMask == CreatorStripKeepKind.None)
                        _outlinerKindMask = CreatorStripKeepKind.None;
                }
                else _outlinerKindMask |= kind;
            }
            RebuildOutlinerTypeNav();
            QueueOutlinerRebuild();
            _outlinerLastFocused = true;
        }

        void BuildOutlinerFilterField(GameObject row, float slot, float s)
        {
            float pad = GalleryUiDesignTokens.TightGapRef * s;
            _outlinerFilterInput = UI.CreateChromeLayoutInputField(
                row.transform,
                GalleryUiDesignTokens.FontCaptionRef,
                slot,
                1f,
                pad,
                pad * 0.5f,
                UI.InputFieldBg,
                UI.InputFieldPlaceholderColor,
                "",
                "Filter");
            if (_outlinerFilterInput == null) return;
            GameObject inputGO = _outlinerFilterInput.gameObject;
            LayoutElement le = inputGO.GetComponent<LayoutElement>();
            if (le == null) le = inputGO.AddComponent<LayoutElement>();
            le.minWidth = slot * 2f;
            le.preferredWidth = slot * 3f;
            le.flexibleWidth = 1f;
            le.minHeight = le.preferredHeight = slot;
            le.flexibleHeight = 0f;
            try { UI.LayoutChromeSearchIcon(inputGO, s); } catch { }
            AddTooltip(inputGO, "outliner.filter.tip",
                "Filter by name, t:type, pkg:creator, is:off");
            _outlinerFilterInput.onValueChanged.AddListener(v =>
            {
                _outlinerFilter = v != null ? v.Trim() : "";
                QueueOutlinerRebuild();
            });
            if (!string.IsNullOrEmpty(_outlinerFilter) && _outlinerFilterInput.text != _outlinerFilter)
                _outlinerFilterInput.text = _outlinerFilter;
            StyleOutlinerInputField(_outlinerFilterInput);
            ApplyOutlinerScaledFont(_outlinerFilterInput.textComponent, GalleryUiDesignTokens.FontCaptionRef, s);
            Text ph = _outlinerFilterInput.placeholder as Text;
            ApplyOutlinerScaledFont(ph, GalleryUiDesignTokens.FontCaptionRef, s);
        }

        float OutlinerRowHeight()
        {
            return OutlinerSlotH(OutlinerLiveScale());
        }

        void EnsureOutlinerRowPool()
        {
            if (_outlinerTreeContentRT == null) return;
            while (_outlinerRowPool.Count < OutlinerPoolSize)
            {
                GameObject row = CreateOutlinerPoolRow();
                if (row == null) break;
                _outlinerRowPool.Add(row);
            }
        }

        GameObject CreateOutlinerPoolRow()
        {
            float s = OutlinerLiveScale();
            float h = OutlinerRowHeight();
            GameObject row = UI.CreateUIButton(_outlinerTreeContentRT.gameObject, 0f, h, " ",
                GalleryUiDesignTokens.FontBodyRef, 0f, 0f, AnchorPresets.hStretchTop, null);
            if (row == null) return null;
            LayoutElement rowLe = row.GetComponent<LayoutElement>();
            if (rowLe == null) rowLe = row.AddComponent<LayoutElement>();
            rowLe.ignoreLayout = true;
            rowLe.minHeight = rowLe.preferredHeight = h;
            rowLe.flexibleHeight = 0f;
            var bind = row.AddComponent<OutlinerRowBind>();
            Image bg = row.GetComponent<Image>();
            bind.Bg = bg;
            Text builtIn = row.GetComponentInChildren<Text>();
            if (builtIn != null) builtIn.text = "";
            UIHoverDelegate hov = row.GetComponent<UIHoverDelegate>();
            if (hov == null) row.AddComponent<UIHoverDelegate>();

            Button btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                OutlinerRowBind captured = bind;
                btn.onClick.AddListener(() => OnOutlinerRowClicked(captured));
            }

            float slot = OutlinerSlotH(s);
            float chevSz = OutlinerBarIconSize(slot, s);
            GameObject chev = UI.CreateUIButton(row, chevSz, chevSz, "", GalleryUiDesignTokens.FontBodyRef,
                0f, 0f, AnchorPresets.middleLeft, () => OnOutlinerChevron(bind));
            bind.ChevronBtn = chev;
            UI.ApplyTreeRowExpandIcon(chev, false, false, s, true);

            float iconSz = GalleryUiDesignTokens.PopupMenuRowIconSizeRef * s;
            GameObject iconGO = UI.AddChildGOImage(row, Color.white, AnchorPresets.middleLeft, iconSz, iconSz, Vector2.zero, rounded: false);
            bind.Icon = iconGO.GetComponent<Image>();
            bind.Icon.raycastTarget = false;
            RectTransform iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchoredPosition = new Vector2(slot, 0f);

            bind.Label = UI.CreateLabel(row, "", GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false);
            ClipOutlinerText(bind.Label);
            RectTransform lrt = bind.Label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(slot * 2f, 0f);
            lrt.offsetMax = new Vector2(-slot, 0f);

            bind.Sub = UI.CreateLabel(row, "", GalleryUiDesignTokens.FontCaptionRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleRight,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false);
            bind.Sub.gameObject.SetActive(false);

            bind.State = UI.CreateLabel(row, "", GalleryUiDesignTokens.FontCaptionRef,
                GalleryUiColorTokens.TextMuted, TextAnchor.MiddleRight,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false);
            bind.State.gameObject.SetActive(false);
            OutlinerUseInwardHoverRim(row);
            try { UI.ApplyFloatRootHoverPolicy(row); } catch { }
            AttachOutlinerRowDrag(row);

            float icon = OutlinerBarIconSize(slot, s);
            GameObject menu = UI.CreateFloatChromeIconButton(
                row.transform, icon, "dots-vertical",
                GalleryUiColorTokens.ChromeIconWell, () => OnOutlinerRowMenu(bind));
            bind.MenuBtn = menu;
            if (menu != null)
            {
                RectTransform mrt = menu.GetComponent<RectTransform>();
                mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f);
                mrt.pivot = new Vector2(0f, 0.5f);
                mrt.anchoredPosition = new Vector2(OutlinerBarInset(s), 0f);
                mrt.sizeDelta = new Vector2(icon, icon);
                AddTooltip(menu, "outliner.row_menu", "Atom actions");
            }
            CreateOutlinerRowEye(row, bind, slot);
            row.SetActive(false);
            return row;
        }

        void ApplyOutlinerContentHeight()
        {
            if (_outlinerTreeContentRT == null) return;
            float h = OutlinerRowHeight();
            float s = OutlinerLiveScale();
            float gap = GalleryUiDesignTokens.OutlinerTreeRowGapRef * s;
            float stride = OutlinerRowLayout.RowStridePx(h, gap);
            float total = Mathf.Max(h, _outlinerVisible.Count * stride);
            _outlinerTreeContentRT.sizeDelta = new Vector2(0f, total);
            bool empty = _outlinerVisible.Count == 0;
            if (_outlinerEmptyRow != null)
            {
                _outlinerEmptyRow.SetActive(empty);
                Text t = _outlinerEmptyRow.GetComponent<Text>();
                if (t != null && empty)
                {
                    t.text = OutlinerEmptyStateText();
                    ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontBodyRef, OutlinerLiveScale());
                }
            }
        }

        string OutlinerEmptyStateText()
        {
            int sceneAtoms = _outlinerModel != null ? _outlinerModel.SceneAtomCount : 0;
            if (sceneAtoms == 0)
            {
                return VPBTranslation.T("outliner.empty.scene",
                    "This scene has no atoms yet.");
            }
            bool filtered = !string.IsNullOrEmpty(_outlinerFilter);
            bool kindNarrowed = _outlinerKindMask != CreatorStripKeepKind.None;
            if (filtered && kindNarrowed)
            {
                return VPBTranslation.T("outliner.empty.both",
                    "Nothing matches the filter inside the selected kinds.\nClear the filter, or pick more kinds above.");
            }
            if (filtered)
            {
                return VPBTranslation.T("outliner.empty.filter", "Nothing matches ")
                    + "\"" + _outlinerFilter + "\""
                    + VPBTranslation.T("outliner.empty.filter_tail", ".\nEsc clears the filter.");
            }
            if (kindNarrowed)
            {
                return VPBTranslation.T("outliner.empty.kinds",
                    "No atoms of the selected kinds.\nPick another icon above, or the first one for everything.");
            }
            return VPBTranslation.T("outliner.empty.hidden",
                "Every atom in this scene is filtered out.");
        }

        void UpdateOutlinerVirtualVisible(bool force)
        {
            if (_outlinerTreeScroll == null || _outlinerTreeContentRT == null) return;
            int total = _outlinerVisible.Count;
            float rowH = OutlinerRowHeight();
            if (rowH < 1f) rowH = 36f;
            float s = OutlinerLiveScale();
            float gap = GalleryUiDesignTokens.OutlinerTreeRowGapRef * s;
            float stride = OutlinerRowLayout.RowStridePx(rowH, gap);
            if (stride < 1f) stride = rowH;
            RectTransform viewport = _outlinerTreeScroll.viewport != null
                ? _outlinerTreeScroll.viewport
                : (_outlinerTreeScroll.transform as RectTransform);
            float viewportH = viewport != null ? viewport.rect.height : 400f;
            float contentH = total * stride;
            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(_outlinerTreeScroll.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = stride > 0f ? Mathf.FloorToInt(scrollY / stride) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (!force && firstIdx == _outlinerVirtFirst) return;
            _outlinerVirtFirst = firstIdx;

            int live = 0;
            for (int i = 0; i < _outlinerRowPool.Count; i++)
            {
                int idx = firstIdx + i;
                GameObject go = _outlinerRowPool[i];
                if (go == null) continue;
                if (idx >= 0 && idx < total)
                {
                    go.SetActive(true);
                    BindOutlinerPoolRow(go, idx, rowH, stride);
                    live++;
                }
                else go.SetActive(false);
            }
            _outlinerLastLiveRows = live;
        }

        void BindOutlinerPoolRow(GameObject go, int visIndex, float rowH, float stride)
        {
            OutlinerRowBind bind = go.GetComponent<OutlinerRowBind>();
            if (bind == null || _outlinerModel == null) return;
            int nodeIndex = _outlinerVisible[visIndex];
            OutlinerNode node = _outlinerModel.Get(nodeIndex);
            if (node == null) return;
            int depth = visIndex < _outlinerVisibleDepths.Count ? _outlinerVisibleDepths[visIndex] : 0;
            bind.VisibleIndex = visIndex;
            bind.NodeId = node.Id;
            bind.AtomUid = node.AtomUid;
            bool selected = node.Kind == OutlinerNodeKind.Atom && _outlinerSelection.Contains(node.AtomUid);
            bool group = node.Kind == OutlinerNodeKind.TypeGroup || node.Kind == OutlinerNodeKind.Scene;
            if (bind.Bg != null)
            {
                if (selected) bind.Bg.color = OutlinerRowSelected;
                else if (group) bind.Bg.color = OutlinerBarBg;
                else bind.Bg.color = OutlinerRowIdle;
            }

            RectTransform rt = go.GetComponent<RectTransform>();
            float s = OutlinerLiveScale();
            float pad = (GalleryUiDesignTokens.ControlGapRef + GalleryUiDesignTokens.ControlRimGutterRef) * s;
            float step = stride > 0f ? stride : rowH;
            int visDepth = OutlinerRowLayout.ClampVisibleDepth(depth);
            float indent = OutlinerRowLayout.IndentPx(visDepth, GalleryUiDesignTokens.Space4Ref * s);
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-pad * 2f, rowH);
                rt.anchoredPosition = new Vector2(0f, -visIndex * step - pad);
            }

            bool expandable = node.ChildIndices.Count > 0;
            bool expanded = expandable && _outlinerExpanded.Contains(node.Id);
            bool showIcon = group;
            float slot = OutlinerSlotH(s);
            float gap = GalleryUiDesignTokens.HairGapRef * s;
            float iconSz = GalleryUiDesignTokens.PopupMenuRowIconSizeRef * s;
            float tight = GalleryUiDesignTokens.TightGapRef * s;
            float iconX = OutlinerRowLayout.IconX(visDepth, GalleryUiDesignTokens.Space4Ref * s, slot, gap);
            float textLeft = OutlinerRowLayout.TextLeft(visDepth, GalleryUiDesignTokens.Space4Ref * s, slot, iconSz, gap, tight, showIcon);
            if (bind.ChevronBtn != null)
            {
                bind.ChevronBtn.SetActive(expandable);
                RectTransform crt = bind.ChevronBtn.GetComponent<RectTransform>();
                if (crt != null)
                {
                    crt.anchorMin = new Vector2(0f, 0.5f);
                    crt.anchorMax = new Vector2(0f, 0.5f);
                    crt.pivot = new Vector2(0f, 0.5f);
                    float chevSz = OutlinerBarIconSize(slot, s);
                    crt.sizeDelta = new Vector2(chevSz, chevSz);
                    crt.anchoredPosition = new Vector2(indent + OutlinerBarInset(s), 0f);
                }
                UI.ApplyTreeRowExpandIcon(bind.ChevronBtn, expandable, expanded, s, true);
            }
            if (bind.Icon != null)
            {
                bind.Icon.gameObject.SetActive(showIcon);
                if (showIcon)
                {
                    RectTransform irt = bind.Icon.rectTransform;
                    irt.anchorMin = new Vector2(0f, 0.5f);
                    irt.anchorMax = new Vector2(0f, 0.5f);
                    irt.pivot = new Vector2(0f, 0.5f);
                    irt.sizeDelta = new Vector2(iconSz, iconSz);
                    irt.anchoredPosition = new Vector2(iconX, 0f);
                    try
                    {
                        Sprite sp = UI.LoadIconSprite(OutlinerNodeIcon(node), GalleryUiColorTokens.TextMuted);
                        if (sp != null)
                        {
                            UI.SetIconSprite(bind.Icon, sp);
                            bind.Icon.enabled = true;
                            bind.Icon.raycastTarget = false;
                        }
                        else bind.Icon.enabled = false;
                    }
                    catch { bind.Icon.enabled = false; }
                }
            }
            float rightReserved = LayoutOutlinerRowEye(bind, node, slot, tight);
            if (bind.Label != null)
            {
                bind.Label.text = node.Label;
                ApplyOutlinerScaledFont(bind.Label, GalleryUiDesignTokens.FontBodyRef, s);
                ClipOutlinerText(bind.Label);
                if (group) bind.Label.color = GalleryUiColorTokens.TextMuted;
                else if (node.IsSystemProtected) bind.Label.color = GalleryUiColorTokens.TextMuted;
                else if (!node.On) bind.Label.color = GalleryUiColorTokens.TextDim;
                else bind.Label.color = GalleryUiColorTokens.TextPrimary;
                RectTransform lrt = bind.Label.rectTransform;
                lrt.offsetMin = new Vector2(textLeft, 0f);
                lrt.offsetMax = new Vector2(-rightReserved, 0f);
            }
            if (bind.State != null) bind.State.gameObject.SetActive(false);
            if (bind.MenuBtn != null)
            {
                RectTransform mrt = bind.MenuBtn.GetComponent<RectTransform>();
                if (mrt != null)
                {
                    mrt.anchorMin = new Vector2(0f, 0.5f);
                    mrt.anchorMax = new Vector2(0f, 0.5f);
                    mrt.pivot = new Vector2(0f, 0.5f);
                    float icon = OutlinerBarIconSize(slot, s);
                    mrt.sizeDelta = new Vector2(icon, icon);
                    mrt.anchoredPosition = new Vector2(indent + OutlinerBarInset(s), 0f);
                }
                bind.MenuBtn.SetActive(node.Kind == OutlinerNodeKind.Atom && !expandable);
            }
            int factBits = 0;
            if (node.Kind == OutlinerNodeKind.Atom)
            {
                if (!node.On) factBits |= 1;
                if (node.Hidden) factBits |= 2;
                if (!node.Collision) factBits |= 4;
                if (!node.IsSystemProtected) factBits |= 8;
            }
            if (string.Equals(bind.TipNodeId, node.Id, StringComparison.Ordinal)
                && bind.TipFacts == factBits
                && string.Equals(bind.TipSubLabel, node.SubLabel, StringComparison.Ordinal))
                return;
            bind.TipNodeId = node.Id;
            bind.TipFacts = factBits;
            bind.TipSubLabel = node.SubLabel;
            string tip = node.Kind == OutlinerNodeKind.Atom && !string.IsNullOrEmpty(node.AtomUid)
                ? node.AtomUid
                : node.Label;
            if (!string.IsNullOrEmpty(node.SubLabel)) tip = tip + "  ·  " + node.SubLabel;
            if (node.Kind == OutlinerNodeKind.Atom)
            {
                if (!node.On) tip = tip + "  ·  off";
                if (node.Hidden) tip = tip + "  ·  hidden";
                if (!node.Collision) tip = tip + "  ·  no collision";
                if (!node.IsSystemProtected)
                {
                    tip = tip + "\n" + VPBTranslation.T("outliner.row.drag_hint",
                        "Shift-click for a range · drag sideways onto another atom to parent it");
                }
            }
            if (!string.Equals(bind.Tip, tip, StringComparison.Ordinal))
            {
                bind.Tip = tip;
                AddTooltipPlain(go, tip);
            }
        }

        static string OutlinerNodeIcon(OutlinerNode node)
        {
            if (node == null) return "box";
            if (node.Kind == OutlinerNodeKind.Scene) return "topology-star";
            if (node.Kind == OutlinerNodeKind.TypeGroup)
            {
                CreatorStripKeepKind gk = node.AtomKind != CreatorStripKeepKind.None
                    ? node.AtomKind
                    : ParseGroupKind(node.Id);
                return OutlinerAtomKindIcon(gk == CreatorStripKeepKind.None
                    ? CreatorStripKeepKind.Other
                    : gk);
            }
            if (node.Kind == OutlinerNodeKind.Atom)
                return OutlinerAtomKindIcon(node.AtomKind == CreatorStripKeepKind.None
                    ? CreatorStripKeepKind.Other
                    : node.AtomKind);
            return "list-details";
        }

        static CreatorStripKeepKind ParseGroupKind(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("group:", StringComparison.Ordinal))
                return CreatorStripKeepKind.Other;
            int n;
            if (int.TryParse(id.Substring(6), out n))
                return (CreatorStripKeepKind)n;
            return CreatorStripKeepKind.Other;
        }

        void OnOutlinerRowClicked(OutlinerRowBind bind)
        {
            if (bind == null || _outlinerModel == null) return;
            _outlinerLastFocused = true;
            OutlinerNode node = _outlinerModel.Find(bind.NodeId);
            if (node == null) { return; }
            if (node.Kind == OutlinerNodeKind.TypeGroup || node.Kind == OutlinerNodeKind.Scene)
            {
                OnOutlinerChevron(bind);
                return;
            }
            if (node.Kind != OutlinerNodeKind.Atom) { return; }
            bool extend = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool range = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool lookAt = !extend && !range && _outlinerSelection.Contains(node.AtomUid);
            if (range && _outlinerSelectAnchorVis >= 0)
            {
                SelectOutlinerRange(_outlinerSelectAnchorVis, bind.VisibleIndex, node.AtomUid);
            }
            else if (extend)
            {
                _outlinerSelection.ToggleExtend(node.AtomUid);
                _outlinerSelectAnchorVis = bind.VisibleIndex;
            }
            else
            {
                _outlinerSelection.SelectOnly(node.AtomUid);
                _outlinerSelectAnchorVis = bind.VisibleIndex;
            }
            _outlinerPendingVamUid = node.AtomUid;
            _outlinerPendingVamLookAt = lookAt;
            NotifyOutlinerSelectionChanged(true);
            RefreshOutlinerTitle();
            UpdateOutlinerVirtualVisible(true);
            RebuildOutlinerInspector();
        }

        void SelectOutlinerRange(int fromVis, int toVis, string clickedUid)
        {
            if (_outlinerModel == null)
            {
                _outlinerSelection.SelectOnly(clickedUid);
                return;
            }
            int lo = fromVis < toVis ? fromVis : toVis;
            int hi = fromVis < toVis ? toVis : fromVis;
            if (lo < 0) lo = 0;
            if (hi >= _outlinerVisible.Count) hi = _outlinerVisible.Count - 1;
            _outlinerSelection.Clear();
            int added = 0;
            for (int i = lo; i <= hi; i++)
            {
                OutlinerNode n = _outlinerModel.Get(_outlinerVisible[i]);
                if (n == null || n.Kind != OutlinerNodeKind.Atom) continue;
                if (string.IsNullOrEmpty(n.AtomUid)) continue;
                _outlinerSelection.Add(n.AtomUid);
                added++;
            }
            _outlinerSelection.Add(clickedUid);
            if (added > 1)
            {
                ShowTemporaryStatus(added
                    + VPBTranslation.T("outliner.range.selected", " atoms selected"), 1.4f);
            }
        }

        void OnOutlinerChevron(OutlinerRowBind bind)
        {
            if (bind == null || string.IsNullOrEmpty(bind.NodeId)) return;
            if (!_outlinerExpanded.Remove(bind.NodeId))
            {
                _outlinerExpanded.Add(bind.NodeId);
                _outlinerUserCollapsed.Remove(bind.NodeId);
            }
            else
            {
                _outlinerUserCollapsed.Add(bind.NodeId);
            }
            if (_outlinerModel != null)
                _outlinerModel.CollectVisible(_outlinerExpanded, _outlinerVisible, _outlinerVisibleDepths);
            ApplyOutlinerContentHeight();
            UpdateOutlinerVirtualVisible(true);
        }

        void OnOutlinerRowMenu(OutlinerRowBind bind)
        {
            if (bind == null) return;
            ShowOutlinerRowMenu(bind.AtomUid, bind.NodeId, bind.MenuBtn);
        }

        internal void FrameOutlinerAtom(string uid)
        {
            Atom atom = OutlinerEdits.GetAtom(uid);
            OutlinerEdits.SelectInVam(atom, true);
        }
    }
}
