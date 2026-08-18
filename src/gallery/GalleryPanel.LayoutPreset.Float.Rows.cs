using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Child handles for one pooled layout-preset row. Rows re-bind whenever the visible window
    /// moves, and <c>transform.Find</c> per child per bind is a needless string walk in VR.
    /// </summary>
    internal class LayoutPresetRowRefs : MonoBehaviour
    {
        internal GalleryLayoutPreset Bound;
        internal Image Background;
        internal Color RowColor;
        internal Color HoverColor;
        internal Image ActiveStripe;
        internal GameObject MiniMap;
        internal Image[] MiniCells;
        internal GameObject TextCol;
        internal Text Name;
        internal Text Sub;
        internal GameObject ApplyBtn;
        internal GameObject MenuBtn;
        internal GameObject RenameHost;
        internal GameObject RenameConfirmBtn;
        internal GameObject RenameCancelBtn;
        internal InputField RenameField;
    }

    public partial class GalleryPanel
    {
        private GameObject _layoutRowMenuGO;
        private GalleryLayoutPreset _layoutRowMenuTarget;
        private static readonly StringBuilder s_layoutSubtitleSb = new StringBuilder(64);
        private static readonly StringBuilder s_layoutTooltipSb = new StringBuilder(192);

        /// <summary>
        /// Rebuilds the visible slice only. Row height is fixed, so the window is pure index maths and
        /// a 200-preset list costs the same to open as a 5-preset one.
        /// </summary>
        private void RefreshLayoutPresetsList(bool resetWindow)
        {
            if (_layoutFloatRoot == null || _layoutFloatRowsParent == null) return;

            CollectVisibleLayoutPresets();

            if (_layoutFloatEmptyGo != null)
            {
                bool empty = _layoutFloatVisible.Count == 0;
                if (_layoutFloatEmptyGo.activeSelf != empty) _layoutFloatEmptyGo.SetActive(empty);
                if (empty && _layoutFloatEmptyText != null)
                {
                    _layoutFloatEmptyText.text = string.IsNullOrEmpty(_layoutFloatSearch)
                        ? VPBTranslation.T("gallery.layout_preset.empty", "No saved layouts yet")
                        : VPBTranslation.T("gallery.layout_preset.empty_search", "No layouts match that search");
                }
            }

            if (resetWindow) _layoutFloatWindowStart = -1;
            RebuildLayoutPresetWindow(true);
        }

        private void CollectVisibleLayoutPresets()
        {
            _layoutFloatVisible.Clear();
            GalleryLayoutPresetStore.EnsureLoaded();

            List<GalleryLayoutPreset> all = GalleryLayoutPresetStore.All;
            int mode = CurrentLayoutPresetMode();

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    GalleryLayoutPreset e = all[i];
                    if (e == null) continue;
                    if (e.Mode != mode) continue;
                    if (pass == 0 && !e.Pinned) continue;
                    if (pass == 1 && e.Pinned) continue;
                    if (!GalleryLayoutPresetStore.MatchesSearch(e, _layoutFloatSearch)) continue;
                    _layoutFloatVisible.Add(e);
                }
            }
        }

        private void OnLayoutFloatScrolled(Vector2 normalizedPos)
        {
            RebuildLayoutPresetWindow(false);
        }

        /// <summary>
        /// <paramref name="forceRebind"/> only for data changes — scrolling inside an unchanged window
        /// must not re-bind rows, or every scroll tick pays for the whole visible slice.
        /// </summary>
        private void RebuildLayoutPresetWindow(bool forceRebind)
        {
            if (_layoutFloatRowsParent == null || _layoutFloatScrollRect == null) return;

            float s = _layoutFloatChromeScale > 0f ? _layoutFloatChromeScale : 1f;
            float rowPitch = (LayoutPresetRowHeightRef + LayoutPresetRowGapRef) * s;
            if (rowPitch <= 0f) rowPitch = 1f;

            int total = _layoutFloatVisible.Count;
            RectTransform vp = _layoutFloatScrollRect.viewport;
            float vpH = vp != null ? vp.rect.height : 0f;
            if (vpH <= 1f) vpH = LayoutFloatDefaultHeightRef * s;

            int perScreen = Mathf.CeilToInt(vpH / rowPitch) + LayoutPresetWindowMargin * 2;
            if (perScreen < 1) perScreen = 1;

            float scrolledPx = 0f;
            if (total > 0 && _layoutFloatContentRT != null)
            {
                float contentH = total * rowPitch;
                float hidden = contentH - vpH;
                if (hidden > 0f)
                {
                    float norm = Mathf.Clamp01(1f - _layoutFloatScrollRect.verticalNormalizedPosition);
                    scrolledPx = norm * hidden;
                }
            }

            int start = Mathf.FloorToInt(scrolledPx / rowPitch) - LayoutPresetWindowMargin;
            if (start < 0) start = 0;
            int count = perScreen;
            if (start + count > total) count = total - start;
            if (count < 0) count = 0;

            if (start == _layoutFloatWindowStart && count == _layoutFloatWindowCount && !forceRebind)
                return;

            _layoutFloatWindowStart = start;
            _layoutFloatWindowCount = count;

            SetLayoutSpacerHeight(_layoutFloatTopSpacer, start * rowPitch);
            SetLayoutSpacerHeight(_layoutFloatBottomSpacer, (total - start - count) * rowPitch);

            EnsureLayoutRowPool(count, s);

            for (int i = 0; i < _layoutFloatRowPool.Count; i++)
            {
                GameObject row = _layoutFloatRowPool[i];
                if (row == null) continue;
                bool live = i < count;
                if (row.activeSelf != live) row.SetActive(live);
                if (!live) continue;
                BindLayoutPresetRow(row, _layoutFloatVisible[start + i]);
            }

            if (_layoutFloatBottomSpacer != null)
                _layoutFloatBottomSpacer.transform.SetAsLastSibling();
        }

        private static void SetLayoutSpacerHeight(GameObject spacer, float h)
        {
            if (spacer == null) return;
            LayoutElement le = spacer.GetComponent<LayoutElement>();
            if (le == null) return;
            if (h < 0f) h = 0f;
            le.minHeight = h;
            le.preferredHeight = h;
        }

        private void EnsureLayoutRowPool(int needed, float s)
        {
            while (_layoutFloatRowPool.Count < needed)
            {
                GameObject row = BuildLayoutPresetRowShell(s);
                if (row == null) break;
                _layoutFloatRowPool.Add(row);
            }
            // Keep pooled rows between the spacers so index order stays stable.
            for (int i = 0; i < _layoutFloatRowPool.Count; i++)
            {
                GameObject row = _layoutFloatRowPool[i];
                if (row != null) row.transform.SetSiblingIndex(i + 1);
            }
        }

        private GameObject BuildLayoutPresetRowShell(float s)
        {
            if (_layoutFloatRowsParent == null) return null;

            float rowH = LayoutPresetRowHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float miniW = LayoutPresetMiniMapWidthRef * s;
            float miniH = rowH - LayoutPresetMiniMapInsetRef * s;

            GameObject row = UI.CreateChildRT(_layoutFloatRowsParent.gameObject, "PresetRow", AnchorPresets.hStretchTop);
            Image rowBg = UI.AddImage(row, UI.ChromePanel);
            UI.AddLE(row, minHeight: rowH, flexibleWidth: 1f);
            UI.AddHLG(row, spacing: UI.GapTight(s),
                padding: UI.Pad(
                    LayoutPresetRowPadHRef, LayoutPresetRowPadHRef,
                    LayoutPresetRowPadVRef, LayoutPresetRowPadVRef, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            LayoutPresetRowRefs refs = row.AddComponent<LayoutPresetRowRefs>();
            refs.Background = rowBg;
            refs.RowColor = UI.ChromePanel;
            refs.HoverColor = UI.ChromePanel;

            GameObject stripe = UI.CreateChildRT(row, "ActiveStripe", AnchorPresets.middleCenter,
                new Vector2(LayoutPresetActiveStripeWidthRef * s, miniH), Vector2.zero);
            refs.ActiveStripe = UI.AddImage(stripe, UI.Black(0f), raycastTarget: false);
            UI.AddLE(stripe, minWidth: LayoutPresetActiveStripeWidthRef * s, minHeight: miniH, flexibleWidth: 0f);

            BuildLayoutPresetMiniMap(row, refs, miniW, miniH);

            GameObject textCol = UI.CreateChildRT(row, "TextCol", AnchorPresets.middleCenter);
            UI.AddVLG(textCol, spacing: 0f, padding: UI.Pad(0, 0, 0, 0), childAlignment: TextAnchor.MiddleLeft);
            UI.AddLE(textCol, flexibleWidth: 1f, minWidth: 60f * s);
            if (textCol.GetComponent<RectMask2D>() == null) textCol.AddComponent<RectMask2D>();
            refs.TextCol = textCol;

            refs.Name = UI.CreateLabel(textCol, "", GalleryUiDesignTokens.PopupMenuRowFontRef, Color.white, TextAnchor.MiddleLeft,
                horizontalWrap: HorizontalWrapMode.Overflow, raycastTarget: false, name: "Name");
            GalleryUiMetrics.ApplyFont(refs.Name, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(refs.Name.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);

            refs.Sub = UI.CreateLabel(textCol, "", GalleryUiDesignTokens.PopupMenuRowFontRef, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft, horizontalWrap: HorizontalWrapMode.Overflow, raycastTarget: false, name: "Sub");
            GalleryUiMetrics.ApplyFont(refs.Sub, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(refs.Sub.gameObject, flexibleWidth: 1f, flexibleHeight: 1f);
            refs.Sub.gameObject.SetActive(false);

            refs.ApplyBtn = UI.CreateFloatChromeIconButton(
                row.transform, chromeSz, "player-play", GalleryUiColorTokens.AccentConfirm, null);
            if (refs.ApplyBtn != null)
            {
                refs.ApplyBtn.name = "ApplyBtn";
                AddTooltip(refs.ApplyBtn, "gallery.layout_preset.apply", "Apply this layout");
            }

            refs.MenuBtn = UI.CreateFloatChromeIconButton(
                row.transform, chromeSz, "edit", GalleryUiColorTokens.ChromeIconWell, null);
            if (refs.MenuBtn != null)
            {
                refs.MenuBtn.name = "RowMenu";
                AddTooltip(refs.MenuBtn, "gallery.layout_preset.row_menu", "Rename, duplicate, pin, delete");
            }

            BuildLayoutPresetRenameChrome(row, refs, s, chromeSz);

            // Row body carries the detail on hover; applying stays on the explicit play button so a
            // stray click never rearranges every pane. Hover tint rides UIHoverDelegate, not
            // UIHoverColor — the latter handles drags and would eat the list's drag-to-scroll.
            AddDynamicTooltip(row, () => BuildLayoutPresetRowTooltip(refs.Bound));
            UIHoverDelegate hover = row.GetComponent<UIHoverDelegate>();
            if (hover != null)
            {
                hover.OnHoverChange += enter =>
                {
                    if (refs == null || refs.Background == null) return;
                    refs.Background.color = enter ? refs.HoverColor : refs.RowColor;
                };
            }

            return row;
        }

        /// <summary>
        /// Framed schematic of the arrangement: dock edges and pane count as plain solid rectangles.
        /// Cells are built once and only re-anchored on bind — recycled rows must not churn GameObjects.
        /// </summary>
        private static void BuildLayoutPresetMiniMap(GameObject row, LayoutPresetRowRefs refs, float miniW, float miniH)
        {
            GameObject frame = UI.CreateChildRT(row, "MiniMap", AnchorPresets.middleCenter,
                new Vector2(miniW, miniH), Vector2.zero);
            UI.AddImage(frame, UI.White(0.10f), raycastTarget: false);
            UI.AddLE(frame, minWidth: miniW, minHeight: miniH, flexibleWidth: 0f);
            refs.MiniMap = frame;

            GameObject well = UI.CreateChildRT(frame, "Well", AnchorPresets.stretchAll);
            UI.AddImage(well, UI.Black(0.55f), raycastTarget: false);
            RectTransform wellRT = well.GetComponent<RectTransform>();
            if (wellRT != null)
            {
                wellRT.offsetMin = new Vector2(1f, 1f);
                wellRT.offsetMax = new Vector2(-1f, -1f);
            }

            refs.MiniCells = new Image[LayoutPresetMiniMapCellCount];
            for (int i = 0; i < LayoutPresetMiniMapCellCount; i++)
            {
                GameObject cell = UI.CreateChildRT(well, "Cell" + i, AnchorPresets.stretchAll);
                refs.MiniCells[i] = UI.AddImage(cell, UI.AccentBlue, raycastTarget: false);
                cell.SetActive(false);
            }
        }

        /// <summary>
        /// In-row rename, matching the filter presets: the row turns into a field with confirm/cancel,
        /// instead of borrowing the search box.
        /// </summary>
        private void BuildLayoutPresetRenameChrome(GameObject row, LayoutPresetRowRefs refs, float s, float chromeSz)
        {
            GameObject confirmBtn = UI.CreateFloatChromeIconButton(
                row.transform, chromeSz, "clipboard-check", GalleryUiColorTokens.AccentConfirm, null);
            if (confirmBtn != null) confirmBtn.name = "RenameConfirm";
            refs.RenameConfirmBtn = confirmBtn;

            GameObject cancelBtn = UI.CreateFloatChromeIconButton(
                row.transform, chromeSz, "x", GalleryUiColorTokens.SurfaceMid, null);
            if (cancelBtn != null) cancelBtn.name = "RenameCancel";
            refs.RenameCancelBtn = cancelBtn;

            GameObject host = UI.CreateChildRT(row, "RenameHost", AnchorPresets.stretchAll);
            refs.RenameHost = host;
            LayoutElement le = host.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            RectTransform hostRT = host.GetComponent<RectTransform>();
            if (hostRT != null)
            {
                hostRT.anchorMin = Vector2.zero;
                hostRT.anchorMax = Vector2.one;
                hostRT.offsetMin = new Vector2(6f * s, 3f * s);
                hostRT.offsetMax = new Vector2(-(2f * chromeSz + 10f * s), -3f * s);
            }

            float fieldH = Mathf.Max(18f, LayoutPresetRowHeightRef * s - 4f * s);
            int font = Mathf.Max(GalleryUiDesignTokens.FontMinRef,
                Mathf.RoundToInt(GalleryUiDesignTokens.PopupMenuRowFontRef * s));

            InputField field = UI.CreateChromeLayoutInputField(
                host.transform, font, fieldH, 1f, 6f * s, 2f * s,
                UI.InputFieldBg, UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.layout_preset.rename_ph", "Layout name"),
                "LayoutRenameInput");
            refs.RenameField = field;
            if (field != null)
            {
                RectTransform fRT = field.GetComponent<RectTransform>();
                if (fRT != null)
                {
                    fRT.anchorMin = Vector2.zero;
                    fRT.anchorMax = Vector2.one;
                    fRT.offsetMin = Vector2.zero;
                    fRT.offsetMax = Vector2.zero;
                }
                LayoutElement fle = field.GetComponent<LayoutElement>();
                if (fle != null) fle.ignoreLayout = true;
                try { host.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(field); }
                catch { }
                try
                {
                    var esc = host.AddComponent<SearchInputESCHandler>();
                    esc.Initialize(field, null, () => { _layoutRenameHandled = true; CancelLayoutPresetRename(); });
                }
                catch { }
            }

            host.SetActive(false);
            if (confirmBtn != null) confirmBtn.SetActive(false);
            if (cancelBtn != null) cancelBtn.SetActive(false);
        }

        private void BindLayoutPresetRow(GameObject row, GalleryLayoutPreset preset)
        {
            if (row == null || preset == null) return;
            LayoutPresetRowRefs refs = row.GetComponent<LayoutPresetRowRefs>();
            if (refs == null) return;

            refs.Bound = preset;
            bool renaming = _layoutRenamingId != 0 && _layoutRenamingId == preset.Id;
            bool deleting = !renaming && _layoutDeletingId != 0 && _layoutDeletingId == preset.Id;

            SetGoActive(refs.ActiveStripe, !renaming && !deleting);
            SetGoActive(refs.MiniMap, !renaming && !deleting);
            SetGoActive(refs.TextCol, !renaming);
            SetGoActive(refs.ApplyBtn, !renaming && !deleting);
            SetGoActive(refs.MenuBtn, !renaming && !deleting);
            SetGoActive(refs.RenameHost, renaming);
            SetGoActive(refs.RenameConfirmBtn, renaming || deleting);
            SetGoActive(refs.RenameCancelBtn, renaming || deleting);

            refs.RowColor = deleting
                ? new Color(0.42f, 0.18f, 0.18f, 1f)
                : (preset.Pinned ? UI.ChromeMid : UI.ChromePanel);
            refs.HoverColor = Color.Lerp(refs.RowColor, UI.White(1f), 0.10f);
            if (refs.Background != null) refs.Background.color = refs.RowColor;

            if (renaming)
            {
                BindLayoutPresetRenameRow(refs, preset);
                return;
            }
            if (deleting)
            {
                BindLayoutPresetDeleteRow(refs, preset);
                return;
            }

            if (refs.Name != null)
            {
                refs.Name.text = preset.Name ?? "";
                refs.Name.color = GalleryUiColorTokens.TextPrimary;
                if (refs.Name.gameObject.activeSelf != true) refs.Name.gameObject.SetActive(true);
            }
            if (refs.Sub != null && refs.Sub.gameObject.activeSelf)
                refs.Sub.gameObject.SetActive(false);

            if (refs.ActiveStripe != null)
            {
                bool isActive = preset.Id != 0 && GalleryLayoutPresetStore.ActiveId == preset.Id;
                if (!isActive) refs.ActiveStripe.color = UI.Black(0f);
                else refs.ActiveStripe.color = IsActiveLayoutPresetDirty()
                    ? GalleryUiColorTokens.ActiveWarn
                    : UI.AccentBlue;
            }

            BindLayoutPresetMiniMap(refs, preset);

            GalleryLayoutPreset captured = preset;
            WireButton(refs.ApplyBtn, true, () => ApplyNamedLayoutPreset(captured));
            WireButton(refs.MenuBtn, true, () => OpenLayoutPresetRowMenu(captured, refs.MenuBtn));
        }

        private static void BindLayoutPresetMiniMap(LayoutPresetRowRefs refs, GalleryLayoutPreset preset)
        {
            if (refs == null || refs.MiniCells == null) return;
            List<LayoutPaneState> panes = preset.PayloadLoaded ? preset.Panes : null;
            int paneCount = panes != null ? panes.Count : 0;

            for (int i = 0; i < refs.MiniCells.Length; i++)
            {
                Image cell = refs.MiniCells[i];
                if (cell == null) continue;

                LayoutPaneState p = i < paneCount ? panes[i] : null;
                bool live = p != null;
                if (cell.gameObject.activeSelf != live) cell.gameObject.SetActive(live);
                if (!live) continue;

                GalleryDockSide side = (GalleryDockSide)p.DockSlot;
                cell.color = LayoutMiniMapCellColor(side);

                RectTransform rt = cell.rectTransform;
                if (rt == null) continue;
                Vector2 min, max;
                LayoutMiniMapCellRect(side, i, out min, out max);
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.offsetMin = MiniMapCellInset;
                rt.offsetMax = -MiniMapCellInset;
            }
        }

        private static readonly Vector2 MiniMapCellInset = new Vector2(1f, 1f);

        private static void SetGoActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private static void SetGoActive(Image img, bool active)
        {
            if (img != null) SetGoActive(img.gameObject, active);
        }

        private static void WireButton(GameObject go, bool enabled, UnityEngine.Events.UnityAction onClick)
        {
            Button b = go != null ? go.GetComponent<Button>() : null;
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.interactable = enabled;
            if (enabled && onClick != null) b.onClick.AddListener(onClick);
        }

        private void BindLayoutPresetRenameRow(LayoutPresetRowRefs refs, GalleryLayoutPreset preset)
        {
            InputField field = refs != null ? refs.RenameField : null;
            if (field == null) return;

            field.onEndEdit.RemoveAllListeners();
            field.text = preset.Name ?? "";

            GalleryLayoutPreset captured = preset;
            field.onEndEdit.AddListener(v =>
            {
                if (_layoutRenameHandled) { _layoutRenameHandled = false; return; }
                CommitLayoutPresetRename(captured, v);
            });

            WireButton(refs.RenameConfirmBtn, true, () =>
            {
                _layoutRenameHandled = true;
                CommitLayoutPresetRename(captured, field.text);
            });
            WireButton(refs.RenameCancelBtn, true, () =>
            {
                _layoutRenameHandled = true;
                CancelLayoutPresetRename();
            });

            try { field.ActivateInputField(); field.MoveTextEnd(false); }
            catch { }
        }

        /// <summary>
        /// Shape of the arrangement, never a repeat of the name — the suggested name already carries
        /// pane count and category, so a duplicated line would waste the only other text slot.
        /// </summary>
        private string BuildLayoutPresetRowSubtitle(GalleryLayoutPreset preset, bool otherMode)
        {
            if (otherMode)
            {
                return preset.IsVrPreset
                    ? VPBTranslation.T("gallery.layout_preset.vr_badge", "VR only")
                    : VPBTranslation.T("gallery.layout_preset.desktop_badge", "Desktop only");
            }

            int paneCount = preset.PayloadLoaded && preset.Panes != null ? preset.Panes.Count : 0;
            if (paneCount == 0) return "";

            s_layoutSubtitleSb.Length = 0;
            s_layoutSubtitleSb.Append(paneCount);
            s_layoutSubtitleSb.Append(paneCount == 1
                ? VPBTranslation.T("gallery.layout_preset.pane_one", " pane · ")
                : VPBTranslation.T("gallery.layout_preset.pane_many", " panes · "));

            int floating = 0;
            bool first = true;
            for (int i = 0; i < paneCount; i++)
            {
                LayoutPaneState p = preset.Panes[i];
                if (p == null) continue;
                GalleryDockSide side = (GalleryDockSide)p.DockSlot;
                if (side == GalleryDockSide.None) { floating++; continue; }
                if (!first) s_layoutSubtitleSb.Append(" + ");
                s_layoutSubtitleSb.Append(GalleryDockLayout.ToConfigString(side));
                first = false;
            }

            if (floating > 0)
            {
                if (!first) s_layoutSubtitleSb.Append(" + ");
                s_layoutSubtitleSb.Append(floating);
                s_layoutSubtitleSb.Append(VPBTranslation.T("gallery.layout_preset.floating_suffix", " floating"));
            }
            return s_layoutSubtitleSb.ToString();
        }

        /// <summary>Full arrangement on hover — the row itself stays inert so nothing applies by accident.</summary>
        private string BuildLayoutPresetRowTooltip(GalleryLayoutPreset preset)
        {
            if (preset == null) return null;

            s_layoutTooltipSb.Length = 0;
            s_layoutTooltipSb.Append(preset.Name ?? "");
            if (preset.IsBuiltIn)
            {
                s_layoutTooltipSb.Append("\n");
                s_layoutTooltipSb.Append(VPBTranslation.T("gallery.layout_preset.builtin_hint",
                    "Built-in baseline — restores dock shape only. Duplicate it to edit."));
            }

            if (preset.Mode != CurrentLayoutPresetMode())
            {
                s_layoutTooltipSb.Append("\n");
                s_layoutTooltipSb.Append(preset.IsVrPreset
                    ? VPBTranslation.T("gallery.layout_preset.vr_only_hint",
                        "Saved in VR — switch to VR or convert it to apply.")
                    : VPBTranslation.T("gallery.layout_preset.desktop_only_hint",
                        "Saved on desktop — switch to desktop or convert it to apply."));
                return s_layoutTooltipSb.ToString();
            }

            GalleryLayoutPreset full = GalleryLayoutPresetStore.ResolvePayload(preset);
            List<LayoutPaneState> panes = full != null && full.PayloadLoaded ? full.Panes : null;
            if (panes != null)
            {
                for (int i = 0; i < panes.Count; i++)
                {
                    LayoutPaneState p = panes[i];
                    if (p == null) continue;
                    GalleryDockSide side = (GalleryDockSide)p.DockSlot;
                    s_layoutTooltipSb.Append("\n· ");
                    s_layoutTooltipSb.Append(side == GalleryDockSide.None
                        ? VPBTranslation.T("gallery.layout_preset.side_floating", "Floating")
                        : GalleryDockLayout.ToConfigString(side));
                    if (!string.IsNullOrEmpty(p.CategoryTitle))
                    {
                        s_layoutTooltipSb.Append(" — ");
                        s_layoutTooltipSb.Append(p.CategoryTitle);
                    }
                }
            }

            s_layoutTooltipSb.Append("\n");
            s_layoutTooltipSb.Append(VPBTranslation.T("gallery.layout_preset.apply_hint",
                "Use the play button to apply."));
            return s_layoutTooltipSb.ToString();
        }

        private static Color LayoutMiniMapCellColor(GalleryDockSide side)
        {
            return side == GalleryDockSide.None ? UI.AccentBlue : UI.AccentGreen;
        }

        private static void LayoutMiniMapCellRect(GalleryDockSide side, int index, out Vector2 min, out Vector2 max)
        {
            switch (side)
            {
                case GalleryDockSide.Left:
                    min = new Vector2(0f, 0f); max = new Vector2(0.32f, 0.78f); return;
                case GalleryDockSide.Right:
                    min = new Vector2(0.68f, 0f); max = new Vector2(1f, 0.78f); return;
                case GalleryDockSide.Top:
                    min = new Vector2(0f, 0.8f); max = new Vector2(1f, 1f); return;
            }

            // Floating panes fan out across the middle band so pane count stays readable.
            float w = 0.24f;
            float x = 0.38f + (index % 3) * 0.02f;
            float y = 0.2f + (index % 3) * 0.14f;
            min = new Vector2(x, y);
            max = new Vector2(x + w, y + 0.34f);
        }

        private void BindLayoutPresetDeleteRow(LayoutPresetRowRefs refs, GalleryLayoutPreset preset)
        {
            if (refs == null || preset == null) return;
            if (refs.Name != null)
            {
                refs.Name.gameObject.SetActive(true);
                refs.Name.text = string.Format(
                    VPBTranslation.T("gallery.layout_preset.delete_inline", "Delete '{0}'?"),
                    preset.Name ?? "");
                refs.Name.color = Color.white;
            }
            if (refs.Sub != null) refs.Sub.gameObject.SetActive(false);

            GalleryLayoutPreset captured = preset;
            WireButton(refs.RenameConfirmBtn, true, () =>
            {
                _layoutDeletingId = 0;
                DeleteLayoutPreset(captured);
                RefreshLayoutPresetsList(true);
            });
            WireButton(refs.RenameCancelBtn, true, CancelLayoutPresetDelete);
        }

        private void SaveCurrentLayoutFromFloat()
        {
            GalleryLayoutPreset created = SaveCurrentLayoutAsPreset(null);
            if (created == null) return;
            RefreshLayoutPresetsList(true);
            // Straight into rename: the suggested name is a starting point, not a decision.
            BeginLayoutPresetRename(created);
        }

        private void ImportLayoutPresetsFromFloat()
        {
            ImportLayoutPresets();
            RefreshLayoutPresetsList(true);
        }

        private GameObject _layoutRowMenuPanelGO;

        private void OpenLayoutPresetRowMenu(GalleryLayoutPreset preset, GameObject anchor)
        {
            if (preset == null || _layoutFloatPanelRT == null) return;
            _layoutRowMenuTarget = preset;

            if (_layoutRowMenuGO == null)
            {
                _layoutRowMenuGO = UI.CreatePopupMenuRoot(
                    _layoutFloatPanelRT.gameObject, "LayoutRowMenu", CloseLayoutPresetRowMenu);
                _layoutRowMenuPanelGO = UI.CreateChildRT(
                    _layoutRowMenuGO, "MenuPanel", AnchorPresets.middleCenter,
                    new Vector2(GalleryUiDesignTokens.PopupMenuPanelWidthRef, 50f), Vector2.zero);
            UI.AddImage(_layoutRowMenuPanelGO, GalleryUiColorTokens.PopupSurface);
            if (_layoutRowMenuPanelGO.GetComponent<RectMask2D>() == null)
                _layoutRowMenuPanelGO.AddComponent<RectMask2D>();
            UI.AddVLG(_layoutRowMenuPanelGO, spacing: 0f, padding: UI.PadTight());
                ContentSizeFitter csf = _layoutRowMenuPanelGO.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }

            if (_layoutRowMenuPanelGO != null)
            {
                RebuildLayoutPresetRowMenu(_layoutRowMenuPanelGO.transform, preset);
                ScaleLayoutPresetRowMenu(_layoutRowMenuPanelGO, _layoutFloatChromeScale);
                PositionLayoutPresetRowMenu(_layoutRowMenuPanelGO.GetComponent<RectTransform>(), anchor);
            }
            _layoutRowMenuGO.transform.SetAsLastSibling();
            _layoutRowMenuGO.SetActive(true);
        }

        private void RebuildLayoutPresetRowMenu(Transform panel, GalleryLayoutPreset preset)
        {
            UI.DestroyAllChildren(panel);
            GalleryLayoutPreset target = preset;

            bool builtIn = target.IsBuiltIn;

            AddLayoutPresetMenuRow(panel,
                VPBTranslation.T("gallery.layout_preset.menu_apply", "Apply layout"),
                () => { CloseLayoutPresetRowMenu(); ApplyNamedLayoutPreset(target); });
            if (!builtIn)
            {
                AddLayoutPresetMenuRow(panel,
                    VPBTranslation.T("gallery.layout_preset.menu_update", "Update from current layout"),
                    () => { CloseLayoutPresetRowMenu(); UpdateLayoutPresetFromLive(target); RefreshLayoutPresetsList(false); });
            }
            AddLayoutPresetMenuSeparator(panel);
            if (!builtIn)
            {
                AddLayoutPresetMenuRow(panel,
                    VPBTranslation.T("gallery.layout_preset.menu_rename", "Rename"),
                    () => { CloseLayoutPresetRowMenu(); BeginLayoutPresetRename(target); });
            }
            AddLayoutPresetMenuRow(panel,
                VPBTranslation.T("gallery.layout_preset.menu_duplicate", "Duplicate"),
                () => { CloseLayoutPresetRowMenu(); DuplicateLayoutPreset(target); RefreshLayoutPresetsList(true); });
            if (!builtIn)
            {
                AddLayoutPresetMenuRow(panel,
                    target.Pinned
                        ? VPBTranslation.T("gallery.layout_preset.menu_unpin", "Unpin")
                        : VPBTranslation.T("gallery.layout_preset.menu_pin", "Pin to top"),
                    () => { CloseLayoutPresetRowMenu(); ToggleLayoutPresetPinned(target); RefreshLayoutPresetsList(true); },
                    target.Pinned);
            }
            AddLayoutPresetMenuRow(panel,
                target.IsVrPreset
                    ? VPBTranslation.T("gallery.layout_preset.menu_to_desktop", "Convert to Desktop")
                    : VPBTranslation.T("gallery.layout_preset.menu_to_vr", "Convert to VR"),
                () => { CloseLayoutPresetRowMenu(); ConvertLayoutPresetToOtherMode(target); RefreshLayoutPresetsList(true); });
            AddLayoutPresetMenuRow(panel,
                VPBTranslation.T("gallery.layout_preset.menu_export", "Export to file"),
                () => { CloseLayoutPresetRowMenu(); ExportLayoutPreset(target); });
            if (!builtIn)
            {
                AddLayoutPresetMenuSeparator(panel);
                AddLayoutPresetMenuRow(panel,
                    VPBTranslation.T("gallery.layout_preset.menu_delete", "Delete"),
                    () => { CloseLayoutPresetRowMenu(); ArmLayoutPresetDelete(target); },
                    false, true);
            }
        }

        private static void AddLayoutPresetMenuSeparator(Transform panel)
        {
            GameObject go = new GameObject("Sep");
            go.transform.SetParent(panel, false);
            Image img = UI.AddImage(go, new Color(1f, 1f, 1f, 0.12f), raycastTarget: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1f;
            le.flexibleWidth = 1f;
            if (img != null) img.color = new Color(1f, 1f, 1f, 0.12f);
        }

        private static GameObject AddLayoutPresetMenuRow(
            Transform panel, string label, UnityEngine.Events.UnityAction onClick,
            bool isActive = false, bool destructive = false)
        {
            if (panel == null || onClick == null) return null;
            GameObject row = new GameObject("Row");
            row.transform.SetParent(panel, false);
            Image img = UI.AddImage(row, Color.clear);
            Button btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            UI.AddLE(row, preferredHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef, flexibleWidth: 1f);

            Color idle = isActive ? UI.PopupRowActiveBackdrop : Color.clear;
            Color hover = UI.PopupRowActiveBackdrop;
            img.color = idle;
            UIHoverDelegate hoverDel = row.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange += enter =>
            {
                if (img != null) img.color = enter ? hover : idle;
            };

            Text t = UI.CreateLabel(row, label, GalleryUiDesignTokens.PopupMenuRowFontRef,
                destructive ? GalleryUiColorTokens.AccentDangerStrong : GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, raycastTarget: false, name: "Text");
            if (t != null)
            {
                RectTransform trt = t.rectTransform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                UI.ApplyPopupMenuRowTextPadding(t, 1f);
            }
            return row;
        }

        private void ScaleLayoutPresetRowMenu(GameObject panelGO, float s)
        {
            if (panelGO == null) return;
            if (s <= 0f) s = 1f;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            float width = GalleryUiDesignTokens.PopupMenuPanelWidthRef * s;
            RectTransform rt = panelGO.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            VerticalLayoutGroup vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                int pad = Mathf.RoundToInt(4f * s);
                vlg.padding = new RectOffset(pad, pad, pad, pad);
                vlg.spacing = 0f;
            }
            Transform panel = panelGO.transform;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform ch = panel.GetChild(i);
                if (ch == null) continue;
                LayoutElement le = ch.GetComponent<LayoutElement>();
                if (le == null) continue;
                if (ch.name == "Sep")
                {
                    le.minHeight = le.preferredHeight = Mathf.Max(1f, s);
                    continue;
                }
                le.preferredHeight = rowH;
                le.minHeight = rowH;
                Text t = ch.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                    UI.ApplyPopupMenuRowTextPadding(t, s);
                }
            }
        }

        private void PositionLayoutPresetRowMenu(RectTransform panelRT, GameObject anchor)
        {
            if (panelRT == null || _layoutFloatPanelRT == null) return;
            float s = _layoutFloatChromeScale > 0f ? _layoutFloatChromeScale : 1f;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            panelRT.pivot = new Vector2(1f, 1f);
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);

            Vector2 local = Vector2.zero;
            RectTransform overlayRT = _layoutRowMenuGO != null
                ? _layoutRowMenuGO.GetComponent<RectTransform>()
                : _layoutFloatPanelRT;
            if (anchor != null && overlayRT != null)
            {
                RectTransform aRT = anchor.GetComponent<RectTransform>();
                if (aRT != null)
                {
                    Camera uiCam = ResolveLayoutFloatUiCamera();
                    Vector3[] corners = new Vector3[4];
                    aRT.GetWorldCorners(corners);
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        overlayRT, screen, uiCam, out local);
                }
            }
            panelRT.anchoredPosition = new Vector2(local.x, local.y - gap);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
        }

        private void CloseLayoutPresetRowMenu()
        {
            _layoutRowMenuTarget = null;
            if (_layoutRowMenuGO != null) _layoutRowMenuGO.SetActive(false);
        }

        private int _layoutRenamingId;
        private int _layoutDeletingId;
        private bool _layoutRenameHandled;

        private void BeginLayoutPresetRename(GalleryLayoutPreset preset)
        {
            if (preset == null) return;
            _layoutRenamingId = preset.Id;
            _layoutDeletingId = 0;
            _layoutRenameHandled = false;
            RefreshLayoutPresetsList(false);
        }

        private void ArmLayoutPresetDelete(GalleryLayoutPreset preset)
        {
            if (preset == null) return;
            _layoutRenamingId = 0;
            _layoutDeletingId = preset.Id;
            RefreshLayoutPresetsList(false);
        }

        private void CancelLayoutPresetDelete()
        {
            _layoutDeletingId = 0;
            RefreshLayoutPresetsList(false);
        }

        private void CancelLayoutPresetRename()
        {
            _layoutRenamingId = 0;
            RefreshLayoutPresetsList(false);
        }

        private void CommitLayoutPresetRename(GalleryLayoutPreset preset, string value)
        {
            _layoutRenamingId = 0;
            if (preset != null && !string.IsNullOrEmpty(value))
                RenameLayoutPreset(preset, value.Trim());
            RefreshLayoutPresetsList(true);
        }
    }
}
