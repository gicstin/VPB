using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        // Selection toolbox ("tbox")
        private GameObject tbox;
        private Text       tboxLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;

        // Expand/collapse state
        private bool  tboxIsHovered  = false;
        private bool  tboxPinned     = false;
        private float tboxExpandT    = 0f;        // 0 = collapsed, 1 = expanded

        private RectTransform tboxRT;
        private CanvasGroup   tboxLabelCG;        // fades OUT when expanding
        private CanvasGroup   tboxButtonsCG;      // fades IN when expanding
        private GameObject    tboxPinBtn;
        private Text          tboxPinBtnText;

        private const float TboxCollapsedH = 38f;  // height when showing "X Selected"
        private const float TboxExpandedH  = 56f;  // height when showing action buttons
        private const float TboxBottomY    = 120f; // sits above the hover-path bar

        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureTboxUI()
        {
            if (tbox != null) return;
            if (backgroundBoxGO == null) return;

            // ── Bar (full-width, anchored at bottom) ──────────────────────────────
            tbox = UI.AddChildGOImage(
                backgroundBoxGO,
                new Color(0f, 0f, 0f, 0.85f),
                AnchorPresets.hStretchBottom,
                0,
                TboxCollapsedH,
                new Vector2(0f, TboxBottomY)
            );
            tbox.name = "SelectionToolbox";
            tboxRT = tbox.GetComponent<RectTransform>();

            var img = tbox.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var hoverDel = tbox.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange = h => tboxIsHovered = h;

            // ── "X Selected" label (collapsed view) ───────────────────────────────
            var labelGO = new GameObject("TboxLabelLayer");
            labelGO.transform.SetParent(tbox.transform, false);
            tboxLabelCG = labelGO.AddComponent<CanvasGroup>();

            // RectTransform — fill bar, leave 48 px on right for pin button
            var labelLayerRT = labelGO.GetComponent<RectTransform>();
            if (labelLayerRT == null) labelLayerRT = labelGO.AddComponent<RectTransform>();
            labelLayerRT.anchorMin = Vector2.zero;
            labelLayerRT.anchorMax = Vector2.one;
            labelLayerRT.offsetMin = new Vector2(0f,   0f);
            labelLayerRT.offsetMax = new Vector2(-48f, 0f);

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(labelGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize  = 18;
            tboxLabel.fontStyle = FontStyle.Bold;
            tboxLabel.color     = new Color(0.92f, 0.92f, 0.92f, 1f);
            tboxLabel.alignment = TextAnchor.MiddleCenter;
            tboxLabel.raycastTarget = false;
            var labelShadow = labelTextGO.AddComponent<Shadow>();
            labelShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            labelShadow.effectDistance = new Vector2(1f, -1f);
            var labelTextRT = labelTextGO.GetComponent<RectTransform>();
            labelTextRT.anchorMin = Vector2.zero;
            labelTextRT.anchorMax = Vector2.one;
            labelTextRT.sizeDelta = Vector2.zero;

            // ── Buttons panel (expanded view) ─────────────────────────────────────
            var bpGO = new GameObject("TboxButtonsLayer");
            bpGO.transform.SetParent(tbox.transform, false);
            tboxButtonsCG = bpGO.AddComponent<CanvasGroup>();
            tboxButtonsCG.alpha          = 0f;
            tboxButtonsCG.blocksRaycasts = false;
            tboxButtonsCG.interactable   = false;

            var bpRT = bpGO.GetComponent<RectTransform>();
            if (bpRT == null) bpRT = bpGO.AddComponent<RectTransform>();
            bpRT.anchorMin = Vector2.zero;
            bpRT.anchorMax = Vector2.one;
            bpRT.offsetMin = new Vector2(0f,   0f);
            bpRT.offsetMax = new Vector2(-48f, 0f); // same right inset as label layer

            tboxCopyPkgNamesBtn = UI.CreateUIButton(
                bpGO, 210, 42,
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Package Names"), 15,
                -12, 0, AnchorPresets.middleRight,
                CopySelectedPackageNamesToClipboard
            );
            tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";
            AddTooltip(tboxCopyPkgNamesBtn, "gallery.tooltip.tbox_copy_names", "Copy package filenames of selected items to clipboard");

            tboxDeleteBtn = UI.CreateUIButton(
                bpGO, 180, 42,
                VPBTranslation.T("gallery.tbox.delete", "Delete"), 15,
                -12 - 220, 0, AnchorPresets.middleRight,
                TboxDeleteSelectedPackages
            );
            tboxDeleteBtn.name = "Tbox_Delete";
            AddTooltip(tboxDeleteBtn, "gallery.tooltip.tbox_delete", "Move selected packages to DeletedPackages folder");
            try
            {
                var delImg = tboxDeleteBtn.GetComponent<Image>();
                if (delImg != null) delImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            }
            catch { }

            // ── Pin toggle (right edge, always visible) ───────────────────────────
            tboxPinBtn = UI.CreateUIButton(
                tbox, 44, 0, "", 14,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    tboxPinned = !tboxPinned;
                    RefreshTboxPinVisual();
                }
            );
            tboxPinBtn.name = "Tbox_Pin";
            // vStretchRight: right-edge, full height of bar
            var pinRT = tboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin        = new Vector2(1f, 0f);
            pinRT.anchorMax        = new Vector2(1f, 1f);
            pinRT.pivot            = new Vector2(1f, 0.5f);
            pinRT.anchoredPosition = Vector2.zero;
            pinRT.sizeDelta        = new Vector2(44f, 0f);

            tboxPinBtnText = tboxPinBtn.GetComponentInChildren<Text>();

            // Left border line on pin button (visual separator)
            {
                var sep = new GameObject("Separator");
                sep.transform.SetParent(tboxPinBtn.transform, false);
                var sepImg = sep.AddComponent<Image>();
                sepImg.color = new Color(1f, 1f, 1f, 0.08f);
                sepImg.raycastTarget = false;
                var sepRT = sep.GetComponent<RectTransform>();
                sepRT.anchorMin        = new Vector2(0f, 0.15f);
                sepRT.anchorMax        = new Vector2(0f, 0.85f);
                sepRT.pivot            = new Vector2(0f, 0.5f);
                sepRT.anchoredPosition = Vector2.zero;
                sepRT.sizeDelta        = new Vector2(1f, 0f);
            }

            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");
            AddTooltip(tbox, "gallery.tooltip.tbox_label", "Hover to expand selection toolbar");

            tbox.SetActive(false);
        }

        private void RefreshTboxPinVisual()
        {
            if (tboxPinBtnText == null) return;
            if (tboxPinned)
            {
                tboxPinBtnText.text  = "●";
                tboxPinBtnText.color = new Color(0.45f, 0.75f, 0.90f, 1f); // teal accent
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.10f, 0.22f, 0.30f, 1f);
            }
            else
            {
                tboxPinBtnText.text  = "○";
                tboxPinBtnText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.20f, 0.20f, 0.20f, 1f);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void UpdateSelectionContextMenu()
        {
            if (canvas == null) return;
            EnsureTboxUI();
            if (tbox == null) return;

            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            bool visible = sel > 0;
            if (tbox.activeSelf != visible) tbox.SetActive(visible);
            if (!visible)
            {
                tboxExpandT   = 0f;
                tboxIsHovered = false;
                if (tboxPinned) { tboxPinned = false; RefreshTboxPinVisual(); }
                return;
            }

            if (tboxLabel != null)
                tboxLabel.text = sel == 1
                    ? VPBTranslation.T("gallery.tbox.selected_one", "1 Selected")
                    : string.Format(VPBTranslation.T("gallery.tbox.selected_many", "{0} Selected"), sel);

            // Whether the bar should be in expanded state
            bool wantExpanded = tboxIsHovered || tboxPinned;

            // Smooth animate expand T
            float targetT = wantExpanded ? 1f : 0f;
            tboxExpandT = Mathf.Lerp(tboxExpandT, targetT, Time.deltaTime * 10f);
            if (Mathf.Abs(tboxExpandT - targetT) < 0.005f) tboxExpandT = targetT;

            // Resize bar height (grows/shrinks upward, pivot is at bottom)
            if (tboxRT != null)
            {
                float h = Mathf.Lerp(TboxCollapsedH, TboxExpandedH, tboxExpandT);
                tboxRT.sizeDelta = new Vector2(0f, h);
            }

            // Cross-fade: label fades out, buttons fade in
            if (tboxLabelCG != null)
            {
                tboxLabelCG.alpha = Mathf.Lerp(tboxLabelCG.alpha, 1f - tboxExpandT, Time.deltaTime * 14f);
            }

            if (tboxButtonsCG != null)
            {
                float targetAlpha = tboxExpandT;
                tboxButtonsCG.alpha = Mathf.Lerp(tboxButtonsCG.alpha, targetAlpha, Time.deltaTime * 14f);
                if (Mathf.Abs(tboxButtonsCG.alpha - targetAlpha) < 0.01f)
                    tboxButtonsCG.alpha = targetAlpha;

                bool active = tboxButtonsCG.alpha > 0.1f;
                tboxButtonsCG.blocksRaycasts = active;
                tboxButtonsCG.interactable   = tboxButtonsCG.alpha > 0.6f;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void CopySelectedPackageNamesToClipboard()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;

                    string uid = TryGetPackageUidForEntry(f);
                    if (string.IsNullOrEmpty(uid)) continue;
                    set.Add(uid + ".var");
                }

                if (set.Count == 0)
                {
                    ShowTemporaryStatus("No package names found in selection.");
                    return;
                }

                var list = set.ToList();
                list.Sort(StringComparer.OrdinalIgnoreCase);
                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} package name(s) to clipboard.", 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedPackageNamesToClipboard error: " + ex.Message);
                ShowTemporaryStatus("Copy failed. See log.", 2f);
            }
        }

        private static string TryGetPackageUidForEntry(FileEntry f)
        {
            if (f is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                return vfe.Package.Uid;

            if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                return ple.Package.Uid;

            if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
                return mp.RequestedUid;

            string p = f.Path ?? "";
            if (string.IsNullOrEmpty(p)) return null;

            int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (internalSep >= 0) p = p.Substring(0, internalSep);

            p = p.Replace('\\', '/');
            if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return null;

            int slash = p.LastIndexOf('/');
            string file = (slash >= 0) ? p.Substring(slash + 1) : p;
            if (file.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 4);

            return string.IsNullOrEmpty(file) ? null : file;
        }
    }
}
