using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        readonly List<OutlinerLookItem> _outlinerLookItems = new List<OutlinerLookItem>(16);
        readonly List<RawImage> _outlinerLookThumbTargets = new List<RawImage>(12);
        readonly List<OutlinerLookItem> _outlinerLookThumbItems = new List<OutlinerLookItem>(12);
        readonly List<string> _outlinerWornParamIds = new List<string>(16);
        GameObject _outlinerLookCard;
        bool _outlinerLookBuildQueued;
        int _outlinerLookThumbAt;
        int _outlinerLookClothingMore;
        int _outlinerLookHairMore;

        bool OutlinerLookPreviewsEnabled()
        {
            return VPBConfig.Instance == null || VPBConfig.Instance.OutlinerLookPreviews;
        }

        int OutlinerLookSignature()
        {
            return OutlinerLookSignatureOf(_outlinerLookItems, _outlinerLookClothingMore, _outlinerLookHairMore);
        }

        static int OutlinerLookSignatureOf(List<OutlinerLookItem> items, int clothingMore, int hairMore)
        {
            int sig = 17;
            sig = sig * 31 + items.Count;
            sig = sig * 31 + clothingMore;
            sig = sig * 31 + hairMore;
            for (int i = 0; i < items.Count; i++)
            {
                OutlinerLookItem item = items[i];
                if (item == null) continue;
                sig = sig * 31 + (item.Group ?? "").GetHashCode();
                sig = sig * 31 + (item.ItemUid ?? "").GetHashCode();
                sig = sig * 31 + (item.Label ?? "").GetHashCode();
                sig = sig * 31 + (item.Hidden ? 1 : 0);
            }
            return sig;
        }

        void BuildOutlinerLookCardShell(Atom atom, float s)
        {
            _outlinerLookCard = null;
            _outlinerLookReused = false;
            _outlinerLookBuildQueued = false;
            if (!OutlinerLookPreviewsEnabled()) return;
            if (!OutlinerLookPreview.Supports(atom)) return;
            string type = "";
            try { type = atom.type; } catch { }
            if (!OutlinerInspectorShows("look", type)) return;

            bool open = OutlinerCardIsOpen("look", true);
            int sig = 0;
            if (open)
            {
                int clothingMore;
                int hairMore;
                OutlinerLookPreview.Collect(atom, _outlinerLookItems,
                    GalleryUiDesignTokens.OutlinerLookTileMax, out clothingMore, out hairMore);
                _outlinerLookClothingMore = clothingMore;
                _outlinerLookHairMore = hairMore;
                sig = OutlinerLookSignature();
            }
            GameObject card = BeginOutlinerCard("look",
                VPBTranslation.T("outliner.look", "Worn items"), true, s, null, sig);
            _outlinerLookCard = card;
            if (_outlinerCardReused)
            {
                _outlinerLookReused = true;
                return;
            }
            if (!open)
            {
                UI.AddLE(CreateOutlinerPlaceholder(card, s),
                    preferredHeight: GalleryUiDesignTokens.HairGapRef * s);
                return;
            }

            Text wait = UI.CreateLabel(card,
                VPBTranslation.T("outliner.look.reading", "Reading worn items..."),
                GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(wait, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(wait);
            UI.AddLE(wait.gameObject, preferredHeight: OutlinerSlotH(s), flexibleWidth: 1f);
            _outlinerLookBuildQueued = true;
        }

        void FillOutlinerLookCardBody(Atom atom, float s)
        {
            GameObject card = _outlinerLookCard;
            if (card == null || atom == null) return;
            ClearOutlinerCardBody(card);
            _outlinerLookThumbTargets.Clear();
            _outlinerLookThumbItems.Clear();
            _outlinerLookThumbAt = 0;

            if (_outlinerLookItems.Count == 0)
            {
                Text empty = UI.CreateLabel(card,
                    VPBTranslation.T("outliner.look.empty", "Nothing is being worn."),
                    GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                    TextAnchor.MiddleLeft);
                ApplyOutlinerScaledFont(empty, GalleryUiDesignTokens.FontCaptionRef, s);
                ClipOutlinerText(empty);
                UI.AddLE(empty.gameObject, preferredHeight: OutlinerSlotH(s), flexibleWidth: 1f);
                return;
            }

            BuildOutlinerLookGroup(card, atom, OutlinerLookPreview.CharacterGroup,
                VPBTranslation.T("outliner.look.character", "Character"),
                null, null, null, null, 0, s);
            BuildOutlinerLookGroup(card, atom, OutlinerLookPreview.ClothingGroup,
                VPBTranslation.T("outliner.look.clothing", "Clothing"),
                VPBTranslation.T("outliner.look.undress", "Undress"),
                VPBTranslation.T("outliner.look.undress_tip",
                    "Take off every active clothing item.\nUndo stays on the footer bar for 10 seconds."),
                "shirt-off",
                VPBTranslation.T("outliner.look.clothing", "Clothing"),
                _outlinerLookClothingMore, s);
            BuildOutlinerLookGroup(card, atom, OutlinerLookPreview.HairGroup,
                VPBTranslation.T("outliner.look.hair", "Hair"),
                VPBTranslation.T("outliner.look.bald", "Remove hair"),
                VPBTranslation.T("outliner.look.bald_tip",
                    "Take off every active hair item.\nUndo stays on the footer bar for 10 seconds."),
                "scissors-off",
                VPBTranslation.T("outliner.look.hair", "Hair"),
                _outlinerLookHairMore, s);
        }

        void ClearOutlinerCardBody(GameObject card)
        {
            if (card == null) return;
            Transform t = card.transform;
            for (int i = t.childCount - 1; i >= 1; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                ReleaseOutlinerCardThumbnails(child);
                try { child.transform.SetParent(null, false); } catch { }
                try { UnityEngine.Object.Destroy(child); } catch { }
            }
        }

        void BuildOutlinerLookGroup(GameObject card, Atom atom, string group, string title,
            string actionLabel, string actionTip, string actionIcon, string undoLabel,
            int more, float s)
        {
            int count = OutlinerLookPreview.CountGroup(_outlinerLookItems, group);
            if (count == 0) return;

            AddOutlinerLookGroupHeader(card, atom, title, count + more, actionLabel, actionTip, actionIcon,
                group, undoLabel, s);

            float gap = GalleryUiDesignTokens.TightGapRef * s;
            float cap = GalleryUiDesignTokens.OutlinerLookTileCapRef * s;
            float actionH = GalleryUiDesignTokens.ButtonSizeRef * s;
            int cols = OutlinerLookTileColumns(s);
            float cellW = OutlinerLookTileWidth(s, cols, gap);
            float cellH = cellW + cap + actionH + gap;

            GameObject grid = new GameObject("LookGrid_" + group);
            grid.transform.SetParent(card.transform, false);
            GridLayoutGroup gl = grid.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(cellW, cellH);
            gl.spacing = new Vector2(gap, gap);
            gl.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gl.startAxis = GridLayoutGroup.Axis.Horizontal;
            gl.childAlignment = TextAnchor.UpperLeft;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = cols;

            int shown = 0;
            for (int i = 0; i < _outlinerLookItems.Count; i++)
            {
                OutlinerLookItem item = _outlinerLookItems[i];
                if (item == null || !string.Equals(item.Group, group, StringComparison.Ordinal))
                    continue;
                AddOutlinerLookTile(grid, atom, item, cellW, cellH, cap, actionH, gap, s);
                shown++;
            }

            int rows = (shown + cols - 1) / cols;
            if (rows < 1) rows = 1;
            float h = rows * cellH + Mathf.Max(0, rows - 1) * gap;
            UI.AddLE(grid, preferredHeight: h, minHeight: h, flexibleWidth: 1f);

            if (more > 0)
            {
                Text moreT = UI.CreateLabel(card,
                    VPBTranslation.T("outliner.look.more", "+ ")
                        + more
                        + VPBTranslation.T("outliner.look.more_tail", " more"),
                    GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                    TextAnchor.MiddleLeft);
                ApplyOutlinerScaledFont(moreT, GalleryUiDesignTokens.FontCaptionRef, s);
                ClipOutlinerText(moreT);
                UI.AddLE(moreT.gameObject, preferredHeight: GalleryUiDesignTokens.ButtonSizeRef * s,
                    flexibleWidth: 1f);
            }
        }

        void AddOutlinerLookGroupHeader(GameObject card, Atom atom, string title, int count,
            string actionLabel, string actionTip, string actionIcon,
            string group, string undoLabel, float s)
        {
            float h = OutlinerSlotH(s);
            GameObject row = new GameObject("LookGroupHeader");
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            string heading = title;
            if (count > 0) heading = title + "  ·  " + count.ToString();
            Text caption = UI.CreateLabel(row, heading, GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(caption, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(caption);
            UI.AddLE(caption.gameObject, preferredHeight: h, minHeight: h,
                preferredWidth: GalleryUiDesignTokens.OutlinerAxisNameWidthRef * s,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s,
                flexibleWidth: 1f);

            if (string.IsNullOrEmpty(actionLabel)) return;
            AddOutlinerLookActionButton(row, atom, h, s, actionLabel, actionTip, actionIcon,
                group, undoLabel);
        }

        int OutlinerLookTileColumns(float s)
        {
            float width = 0f;
            if (_outlinerInspectorPane != null)
            {
                RectTransform rt = _outlinerInspectorPane.GetComponent<RectTransform>();
                if (rt != null) width = rt.rect.width;
            }
            if (width < 8f) width = GalleryUiDesignTokens.OutlinerRailWidthRef * s;
            float want = GalleryUiDesignTokens.OutlinerLookTileWidthRef * s;
            int cols = Mathf.FloorToInt(width / Mathf.Max(1f, want));
            if (cols < 2) cols = 2;
            if (cols > 5) cols = 5;
            return cols;
        }

        float OutlinerLookTileWidth(float s, int cols, float gap)
        {
            float width = 0f;
            if (_outlinerInspectorPane != null)
            {
                RectTransform rt = _outlinerInspectorPane.GetComponent<RectTransform>();
                if (rt != null) width = rt.rect.width;
            }
            if (width < 8f) width = GalleryUiDesignTokens.OutlinerRailWidthRef * s;
            float pad = GalleryUiDesignTokens.TightGapRef * s * 4f
                + GalleryUiDesignTokens.SideTabScrollBarWidthRef * s;
            float usable = width - pad - gap * (cols - 1);
            float cell = usable / cols;
            float min = GalleryUiDesignTokens.ButtonSizeRef * 1.5f * s;
            return Mathf.Max(min, cell);
        }

        void AddOutlinerLookTile(GameObject grid, Atom atom, OutlinerLookItem item,
            float cellW, float cellH, float cap, float actionH, float gap, float s)
        {
            if (item == null) return;
            string pkg = item.PackageUid ?? "";
            GameObject tile = UI.CreateChildRT(grid, "LookTile", AnchorPresets.middleCenter,
                new Vector2(cellW, cellH), Vector2.zero);
            if (tile == null) return;
            UI.AddLE(tile, preferredWidth: cellW, preferredHeight: cellH,
                minWidth: cellW, minHeight: cellH);
            UI.AddImage(tile, GalleryUiColorTokens.RowIdle, true);
            FitOutlinerCornerRadius(tile, Mathf.Min(cellW, cellH));

            float footer = cap + actionH + gap;
            GameObject thumbSlot = UI.CreateChildRT(tile, "Thumb", AnchorPresets.stretchAll);
            RectTransform thumbRT = thumbSlot.GetComponent<RectTransform>();
            thumbRT.offsetMin = new Vector2(gap * 0.5f, footer);
            thumbRT.offsetMax = new Vector2(-gap * 0.5f, -gap * 0.5f);
            UI.AddImage(thumbSlot, GalleryUiColorTokens.SurfaceDeep, false);
            AddOutlinerLookThumbPlaceholder(thumbSlot, item, s);
            GameObject imgGO = UI.CreateChildRT(thumbSlot, "Image", AnchorPresets.stretchAll);
            RawImage img = imgGO.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.clear;
            _outlinerLookThumbTargets.Add(img);
            _outlinerLookThumbItems.Add(item);
            if (item.Hidden) ApplyOutlinerLookHiddenLook(thumbSlot, true);

            Text label = UI.CreateLabel(tile, item.Label, GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.hStretchBottom, new Vector2(0f, cap), Vector2.zero, "Label");
            RectTransform lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(-gap, cap);
            lrt.anchoredPosition = new Vector2(0f, actionH + gap * 0.5f);
            ApplyOutlinerScaledFont(label, GalleryUiDesignTokens.FontBodyRef, s);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            label.raycastTarget = false;
            if (label.gameObject.GetComponent<RectMask2D>() == null)
                label.gameObject.AddComponent<RectMask2D>();
            AddOutlinerLookKindBadge(tile, item, gap, s);
            AddOutlinerLookTileActions(tile, atom, item, thumbSlot, actionH, gap, s);

            string tip = item.Label;
            if (!string.IsNullOrEmpty(item.Group)) tip = item.Group + "  ·  " + tip;
            if (!string.IsNullOrEmpty(pkg)) tip = tip + "\n" + pkg;
            AddTooltipPlain(tile, tip);
        }

        void AddOutlinerLookTileActions(GameObject tile, Atom atom, OutlinerLookItem item,
            GameObject thumbSlot, float actionH, float gap, float s)
        {
            GameObject row = UI.CreateChildRT(tile, "Actions", AnchorPresets.hStretchBottom,
                new Vector2(0f, actionH), Vector2.zero);
            RectTransform rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(1f, 0f);
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.sizeDelta = new Vector2(-gap, actionH);
            rrt.anchoredPosition = Vector2.zero;
            UI.AddHLG(row, GalleryUiDesignTokens.HairGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: false, childControlHeight: false,
                childForceExpandWidth: false, childForceExpandHeight: false);

            OutlinerLookItem captured = item;
            bool wearable = OutlinerLookPreview.IsWearable(item);
            if (wearable)
            {
                GameObject hideBtn = null;
                hideBtn = AddOutlinerLookActionIcon(row, item.Hidden ? "eye-off" : "eye",
                    item.Hidden
                        ? VPBTranslation.T("outliner.look.unhide_tip", "Show this item again (material hide off).")
                        : VPBTranslation.T("outliner.look.hide_tip", "Hide this item without taking it off (material hide)."),
                    () => ToggleOutlinerWornHidden(atom, captured, hideBtn, thumbSlot), s);
                bool hair = string.Equals(item.Group, OutlinerLookPreview.HairGroup, StringComparison.Ordinal);
                AddOutlinerLookActionIcon(row, hair ? "scissors-off" : "shirt-off",
                    VPBTranslation.T("outliner.look.menu.remove", "Take off"),
                    () => RemoveOutlinerWornItem(atom, captured), s);
            }
            else
            {
                AddOutlinerLookActionIcon(row, "user",
                    VPBTranslation.T("outliner.look.menu.open_vam_character", "Open Appearance"),
                    () => OpenOutlinerWornItemInVam(atom, captured), s);
            }
            GameObject src = AddOutlinerLookActionIcon(row, "package",
                string.IsNullOrEmpty(item.PackageUid)
                    ? VPBTranslation.T("outliner.look.builtin", "This item is built into VaM — no package to reveal.")
                    : VPBTranslation.T("outliner.look.source_tip", "Take me to source: filter the gallery to ")
                        + item.PackageUid,
                () => RevealOutlinerLookItem(captured.PackageUid), s);
            if (src != null && string.IsNullOrEmpty(item.PackageUid))
            {
                CanvasGroup cg = src.GetComponent<CanvasGroup>();
                if (cg == null) cg = src.AddComponent<CanvasGroup>();
                cg.alpha = OutlinerDisabledIconAlpha;
            }
        }

        GameObject AddOutlinerLookActionIcon(GameObject row, string icon, string tip,
            UnityEngine.Events.UnityAction act, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateFloatChromeIconButton(row.transform, h, icon,
                GalleryUiColorTokens.ChromeIconWell, act);
            if (btn == null) return null;
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(btn, tip);
            return btn;
        }

        static void ApplyOutlinerLookHiddenLook(GameObject thumbSlot, bool hidden)
        {
            if (thumbSlot == null) return;
            CanvasGroup cg = thumbSlot.GetComponent<CanvasGroup>();
            if (cg == null) cg = thumbSlot.AddComponent<CanvasGroup>();
            cg.alpha = hidden ? OutlinerDisabledIconAlpha : 1f;
        }

        void AddOutlinerLookThumbPlaceholder(GameObject thumbSlot, OutlinerLookItem item, float s)
        {
            if (thumbSlot == null || item == null) return;
            string role = OutlinerLookGroupIcon(item.Group);
            if (string.IsNullOrEmpty(role)) return;
            Sprite sp = null;
            try { sp = UI.LoadIconSprite(role, UI.BarIconGlyphTint); } catch { }
            if (sp == null) return;
            float sz = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject glyph = UI.AddChildGOImage(thumbSlot, Color.white, AnchorPresets.middleCenter,
                sz, sz, Vector2.zero, false);
            if (glyph == null) return;
            glyph.name = "Placeholder";
            Image img = glyph.GetComponent<Image>();
            if (img == null) return;
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.color = GalleryUiColorTokens.TextDim;
            UI.SetIconSprite(img, sp);
        }

        void AddOutlinerLookKindBadge(GameObject tile, OutlinerLookItem item, float gap, float s)
        {
            if (tile == null || item == null) return;
            string role = OutlinerLookGroupIcon(item.Group);
            if (string.IsNullOrEmpty(role)) return;
            Sprite sp = null;
            try { sp = UI.LoadIconSprite(role, UI.BarIconGlyphTint); } catch { }
            if (sp == null) return;
            float sz = GalleryUiDesignTokens.Space5Ref * s;
            GameObject badge = UI.AddChildGOImage(tile, GalleryUiColorTokens.ChromeIconWell,
                AnchorPresets.topLeft, sz, sz, new Vector2(gap, -gap), true);
            if (badge == null) return;
            badge.name = "Kind";
            badge.transform.SetAsLastSibling();
            Image well = badge.GetComponent<Image>();
            if (well != null) well.raycastTarget = false;
            float inset = GalleryUiDesignTokens.HairGapRef * s;
            GameObject glyph = UI.CreateChildRT(badge, "Glyph", AnchorPresets.stretchAll);
            if (glyph == null) return;
            Image gimg = UI.AddImage(glyph, Color.white, false);
            if (gimg == null) return;
            gimg.preserveAspect = true;
            UI.SetIconSprite(gimg, sp);
            RectTransform grt = glyph.GetComponent<RectTransform>();
            if (grt != null)
            {
                grt.offsetMin = new Vector2(inset, inset);
                grt.offsetMax = new Vector2(-inset, -inset);
            }
        }

        static string OutlinerLookGroupIcon(string group)
        {
            if (string.Equals(group, OutlinerLookPreview.ClothingGroup, StringComparison.Ordinal))
                return "shirt";
            if (string.Equals(group, OutlinerLookPreview.HairGroup, StringComparison.Ordinal))
                return "wash-gentle";
            if (string.Equals(group, OutlinerLookPreview.CharacterGroup, StringComparison.Ordinal))
                return "user";
            return "";
        }

        void AddOutlinerLookActionButton(GameObject row, Atom atom, float h, float s,
            string label, string tip, string iconRole, string group, string undoLabel)
        {
            AddOutlinerActionButton(row, h, s, label, tip, iconRole,
                () => RemoveOutlinerWornGroup(atom, group, undoLabel));
        }

        GameObject AddOutlinerActionButton(GameObject row, float h, float s,
            string label, string tip, string iconRole, UnityEngine.Events.UnityAction act)
        {
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, 0f, h, label,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle, act);
            if (btn == null) return null;
            UI.AddLE(btn, preferredHeight: h, minHeight: h,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2.5f * s,
                flexibleWidth: 1f, flexibleHeight: 0f);
            Text txt = btn.GetComponentInChildren<Text>();
            float iconPad = GalleryUiDesignTokens.TightGapRef * s;
            float iconSize = h - iconPad * 2f;
            if (txt != null)
            {
                txt.alignment = TextAnchor.MiddleLeft;
                ApplyOutlinerScaledFont(txt, GalleryUiDesignTokens.FontBodyRef, s);
                ClipOutlinerText(txt);
                RectTransform trt = txt.rectTransform;
                trt.offsetMin = new Vector2(iconSize + iconPad * 2f, 0f);
                trt.offsetMax = new Vector2(-iconPad, 0f);
            }
            Sprite sp = null;
            try { sp = UI.LoadIconSprite(iconRole, UI.BarIconGlyphTint); } catch { }
            if (sp != null)
            {
                GameObject iconGO = UI.AddChildGOImage(btn, Color.white, AnchorPresets.middleLeft,
                    iconSize, iconSize, new Vector2(iconPad, 0f), false);
                if (iconGO != null)
                {
                    iconGO.name = "Icon";
                    Image img = iconGO.GetComponent<Image>();
                    if (img != null)
                    {
                        img.raycastTarget = false;
                        img.preserveAspect = true;
                        UI.SetIconSprite(img, sp);
                    }
                }
            }
            OutlinerUseInwardHoverRim(btn);
            AddTooltipPlain(btn, tip);
            return btn;
        }

        void RemoveOutlinerWornGroup(Atom atom, string group, string undoLabel)
        {
            if (atom == null) return;
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            OutlinerLookPreview.CollectWornParamIds(atom, group, _outlinerWornParamIds);
            if (_outlinerWornParamIds.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.none_worn",
                    "Nothing of that kind is being worn."), 2f);
                return;
            }
            var snap = new OutlinerResetSnapshot();
            snap.Label = undoLabel;
            for (int i = 0; i < _outlinerWornParamIds.Count; i++)
                snap.CaptureBool(atom, "geometry", _outlinerWornParamIds[i]);
            if (snap.IsEmpty)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.remove_failed",
                    "VaM would not release that item."), 2f);
                return;
            }
            int done = 0;
            for (int i = 0; i < _outlinerWornParamIds.Count; i++)
            {
                if (OutlinerEdits.WriteBool(atom, "geometry", _outlinerWornParamIds[i], false))
                    done++;
            }
            if (done == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.remove_failed",
                    "VaM would not release that item."), 2f);
                return;
            }
            ArmOutlinerResetUndo(snap);
            _outlinerLastFocused = true;
            ShowTemporaryStatus(done + VPBTranslation.T("outliner.look.removed_n", " items removed"), 2f);
            QueueOutlinerLookRefresh();
        }

        void RemoveOutlinerWornItem(Atom atom, OutlinerLookItem item)
        {
            if (atom == null || !OutlinerLookPreview.IsWearable(item)) return;
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            string paramId = OutlinerLookPreview.GeometryParamId(item);
            var snap = new OutlinerResetSnapshot();
            snap.Label = item.Label;
            snap.CaptureBool(atom, "geometry", paramId);
            if (!OutlinerLookPreview.SetWorn(atom, item, false))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.remove_failed",
                    "VaM would not release that item."), 2f);
                return;
            }
            ArmOutlinerResetUndo(snap);
            _outlinerLastFocused = true;
            ShowTemporaryStatus(VPBTranslation.T("outliner.look.removed", "Took off ") + item.Label, 2f);
            QueueOutlinerLookRefresh();
        }

        void OpenOutlinerWornItemInVam(Atom atom, OutlinerLookItem item)
        {
            if (atom == null) return;
            OutlinerEdits.SelectInVam(atom, false);
            string want = item != null && string.Equals(item.Group, OutlinerLookPreview.HairGroup, StringComparison.Ordinal)
                ? "hair"
                : (item != null && OutlinerLookPreview.IsWearable(item) ? "cloth" : "appearance");
            StartCoroutine(OpenOutlinerAtomTabRoutine(atom, want));
        }

        IEnumerator OpenOutlinerAtomTabRoutine(Atom atom, string wantFragment)
        {
            float deadline = Time.unscaledTime + 1.5f;
            while (Time.unscaledTime < deadline)
            {
                yield return null;
                if (atom == null) yield break;
                UITabSelector selector = null;
                try { selector = atom.gameObject.GetComponentInChildren<UITabSelector>(true); }
                catch { selector = null; }
                if (selector == null || selector.toggleContainer == null) continue;
                string tab = FindOutlinerAtomTabName(selector, wantFragment);
                if (string.IsNullOrEmpty(tab))
                {
                    ShowTemporaryStatus(VPBTranslation.T("outliner.look.no_tab",
                        "VaM has no matching tab for this atom."), 2f);
                    yield break;
                }
                try { selector.SetActiveTab(tab); }
                catch { }
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.opened_tab", "Opened ") + tab, 1.6f);
                yield break;
            }
        }

        static string FindOutlinerAtomTabName(UITabSelector selector, string wantFragment)
        {
            if (selector == null || selector.toggleContainer == null) return "";
            foreach (Transform child in selector.toggleContainer)
            {
                if (child == null) continue;
                string name = child.name ?? "";
                if (name.IndexOf(wantFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return name;
            }
            return "";
        }

        const float OutlinerLookRecheckSeconds = 2.5f;
        const int OutlinerLookRecheckFrames = 8;
        readonly List<OutlinerLookItem> _outlinerLookRecheckItems = new List<OutlinerLookItem>(16);
        float _outlinerLookRecheckUntil;
        int _outlinerLookRecheckCounter;

        void QueueOutlinerLookRefresh()
        {
            InvalidateOutlinerParamCard(OutlinerLookPreview.GeometryStorableId);
            _outlinerCardStale.Add("look");
            ArmOutlinerLookRecheck();
            RebuildOutlinerInspector();
        }

        void ArmOutlinerLookRecheck()
        {
            _outlinerLookRecheckUntil = Time.unscaledTime + OutlinerLookRecheckSeconds;
            _outlinerLookRecheckCounter = 0;
        }

        void RecheckOutlinerLookItems()
        {
            if (_outlinerLookRecheckUntil <= 0f) return;
            if (Time.unscaledTime > _outlinerLookRecheckUntil)
            {
                _outlinerLookRecheckUntil = 0f;
                return;
            }
            _outlinerLookRecheckCounter++;
            if (_outlinerLookRecheckCounter < OutlinerLookRecheckFrames) return;
            _outlinerLookRecheckCounter = 0;
            Atom atom = OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
            if (atom == null || _outlinerLookCard == null) return;
            int clothingMore;
            int hairMore;
            OutlinerLookPreview.Collect(atom, _outlinerLookRecheckItems,
                GalleryUiDesignTokens.OutlinerLookTileMax, out clothingMore, out hairMore);
            if (OutlinerLookSignatureOf(_outlinerLookRecheckItems, clothingMore, hairMore) == OutlinerLookSignature())
                return;
            _outlinerLookRecheckUntil = 0f;
            _outlinerCardStale.Add("look");
            RebuildOutlinerInspector();
        }

        void PumpOutlinerLook()
        {
            RecheckOutlinerLookItems();
            if (_outlinerLookBuildQueued)
            {
                _outlinerLookBuildQueued = false;
                Atom atom = OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
                if (atom == null) return;
                FillOutlinerLookCardBody(atom, OutlinerLiveScale());
                LayoutOutlinerInspectorChrome();
                return;
            }
            if (_outlinerLookThumbAt >= _outlinerLookThumbTargets.Count) return;
            if (_outlinerLookThumbAt >= _outlinerLookThumbItems.Count) return;
            RawImage img = _outlinerLookThumbTargets[_outlinerLookThumbAt];
            OutlinerLookItem item = _outlinerLookThumbItems[_outlinerLookThumbAt];
            _outlinerLookThumbAt++;
            if (img == null || item == null) return;
            FileEntry file = OutlinerLookPreview.ResolveThumbFile(item);
            if (file == null) { return; }
            try { LoadThumbnail(file, img, true, 2, false); }
            catch { try { ClearThumbnailTarget(img); } catch { } }
        }

        void ToggleOutlinerWornHidden(Atom atom, OutlinerLookItem item, GameObject button, GameObject thumbSlot)
        {
            if (atom == null || !OutlinerLookPreview.IsWearable(item)) return;
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            bool hidden = !OutlinerLookPreview.IsHidden(atom, item);
            if (!OutlinerLookPreview.SetHidden(atom, item, hidden))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.hide_failed",
                    "This item has no material to hide."), 2f);
                return;
            }
            item.Hidden = hidden;
            _outlinerLastFocused = true;
            ApplyOutlinerLookHiddenLook(thumbSlot, hidden);
            if (button != null)
            {
                float h = GalleryUiDesignTokens.ButtonSizeRef * OutlinerLiveScale();
                UI.StyleFloatChromeIconButton(button, h, hidden ? "eye-off" : "eye");
                AddTooltipPlain(button, hidden
                    ? VPBTranslation.T("outliner.look.unhide_tip", "Show this item again (material hide off).")
                    : VPBTranslation.T("outliner.look.hide_tip", "Hide this item without taking it off (material hide)."));
            }
            ShowTemporaryStatus((hidden
                ? VPBTranslation.T("outliner.look.hidden", "Hid ")
                : VPBTranslation.T("outliner.look.unhidden", "Showing ")) + item.Label, 1.6f);
        }

        void RevealOutlinerLookItem(string packageUid)
        {
            if (string.IsNullOrEmpty(packageUid))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.look.builtin",
                    "This item is built into VaM — no package to reveal."), 2f);
                return;
            }
            RevealOutlinerHostPane();
            try { ClearPackageFilter(); } catch { }
            try
            {
                ClearTitleSearchChipsState();
                GalleryTitleSearchChipUtil.TryAdd(_titleSearchChips, TitleSearchChipKind.Broad,
                    TitleSearchChipPolarity.Include, packageUid, 0);
                ApplySerializedTitleSearchChips();
            }
            catch { }
            ShowTemporaryStatus(VPBTranslation.T("outliner.look.source_status", "Gallery filtered to ") + packageUid, 2f);
        }

        void RevealOutlinerHostPane()
        {
            try { if (isCollapsed) SetCollapsed(false); } catch { }
            try { if (_floatsOnly) SetFloatsOnly(false, true); } catch { }
            _userHidden = false;
            try
            {
                if (canvas != null && !canvas.enabled)
                    ApplyImmediateVisibility(true);
            }
            catch { }
        }
    }
}
