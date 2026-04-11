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
        private Text       tboxHintLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;
        private GameObject tboxAutoInstallBtn;
        private GameObject tboxDisableAutoInstallBtn;
        private GameObject tboxHideBtn;
        private GameObject tboxUnhideBtn;

        // Expand/collapse state
        private bool  tboxIsHovered  = false;
        private bool  tboxPinned     = false;
        private float tboxExpandT    = 0f;        // 0 = collapsed, 1 = expanded

        private RectTransform tboxRT;
        private CanvasGroup   tboxLabelCG;        // fades OUT when expanding
        private CanvasGroup   tboxButtonsCG;      // fades IN when expanding
        private GameObject    tboxPinBtn;
        private Text          tboxPinBtnText;

        // Row height: matches the collapsed bar height set by the layout system.
        // Updated by layout code (UI.Layout.cs) and innerPaneScaleActions.
        private float tboxInfoRowHeight = 60f;   // single row height (= collapsed bar height)
        private float tboxTopOffsetBase = 120f;   // bar's top offset (offsetMax.y) when fully collapsed

        private RectTransform tboxLabelLayerRT;   // reference for scale updates
        private RectTransform tboxButtonsLayerRT; // reference for scale updates

        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureTboxUI()
        {
            if (tbox != null) return;
            // Reuse the unified info bar (hoverPath container) as the tbox
            if (hoverPathRT == null) return;

            tbox   = hoverPathRT.gameObject;
            tboxRT = hoverPathRT;
            tbox.name = "InfoBar";

            // Background already set to opaque grey in UI.cs; ensure raycastTarget on
            var img = tbox.GetComponent<Image>();
            if (img != null) { img.color = new Color(0.15f, 0.15f, 0.15f, 1f); img.raycastTarget = true; }

            var hoverDel = tbox.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange = h => tboxIsHovered = h;

            // ── "X Selected" + hover hint, one row (collapsed view) ─────────────
            var labelGO = new GameObject("TboxLabelLayer");
            labelGO.transform.SetParent(tbox.transform, false);
            tboxLabelCG = labelGO.AddComponent<CanvasGroup>();

            // Label layer occupies the BOTTOM row (always visible), leaving 48 px on right for pin
            var labelLayerRT = labelGO.GetComponent<RectTransform>();
            if (labelLayerRT == null) labelLayerRT = labelGO.AddComponent<RectTransform>();
            labelLayerRT.anchorMin        = new Vector2(0f, 0f);
            labelLayerRT.anchorMax        = new Vector2(1f, 0f);
            labelLayerRT.pivot            = new Vector2(0.5f, 0f);
            labelLayerRT.anchoredPosition = Vector2.zero;
            labelLayerRT.sizeDelta        = new Vector2(-48f, tboxInfoRowHeight);
            tboxLabelLayerRT = labelLayerRT;

            var rowGO = new GameObject("TboxLabelRow");
            rowGO.transform.SetParent(labelGO.transform, false);
            var rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.childAlignment      = TextAnchor.MiddleCenter;
            rowHLG.spacing             = 12f;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childForceExpandHeight = true;
            rowHLG.childControlWidth   = true;
            rowHLG.childControlHeight  = true;
            rowHLG.padding             = new RectOffset(8, 8, 0, 0);

            const int tboxCollapsedFont = 18;

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(rowGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize  = tboxCollapsedFont;
            tboxLabel.fontStyle = FontStyle.Bold;
            tboxLabel.color     = new Color(0.92f, 0.92f, 0.92f, 1f);
            tboxLabel.alignment = TextAnchor.MiddleCenter;
            tboxLabel.raycastTarget = false;
            var labelShadow = labelTextGO.AddComponent<Shadow>();
            labelShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            labelShadow.effectDistance = new Vector2(1f, -1f);
            var labelCSF = labelTextGO.AddComponent<ContentSizeFitter>();
            labelCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var hintTextGO = new GameObject("HoverHint");
            hintTextGO.transform.SetParent(rowGO.transform, false);
            tboxHintLabel = hintTextGO.AddComponent<Text>();
            tboxHintLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxHintLabel.fontSize  = tboxCollapsedFont;
            tboxHintLabel.fontStyle = FontStyle.Normal;
            tboxHintLabel.color     = new Color(0.50f, 0.50f, 0.50f, 1f);
            tboxHintLabel.alignment = TextAnchor.MiddleCenter;
            tboxHintLabel.raycastTarget = false;
            tboxHintLabel.text      = VPBTranslation.T("gallery.tbox.hover_expand", "Hover to expand");
            hintTextGO.SetActive(false);
            var hintShadow = hintTextGO.AddComponent<Shadow>();
            hintShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
            var hintCSF = hintTextGO.AddComponent<ContentSizeFitter>();
            hintCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            hintCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Buttons panel (expanded view) ─────────────────────────────────────
            var bpGO = new GameObject("TboxButtonsLayer");
            bpGO.transform.SetParent(tbox.transform, false);
            tboxButtonsCG = bpGO.AddComponent<CanvasGroup>();
            tboxButtonsCG.alpha          = 0f;
            tboxButtonsCG.blocksRaycasts = false;
            tboxButtonsCG.interactable   = false;

            // Buttons layer sits in the TOP row — directly above the label row.
            // Revealed by RectMask2D as the bar grows upward.
            var bpRT = bpGO.GetComponent<RectTransform>();
            if (bpRT == null) bpRT = bpGO.AddComponent<RectTransform>();
            bpRT.anchorMin        = new Vector2(0f, 0f);
            bpRT.anchorMax        = new Vector2(1f, 0f);
            bpRT.pivot            = new Vector2(0.5f, 0f);
            bpRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight); // sits one row above bottom
            bpRT.sizeDelta        = new Vector2(-48f, tboxInfoRowHeight);
            tboxButtonsLayerRT = bpRT;

            const int tboxActionBtnFont = 16;
            tboxCopyPkgNamesBtn = UI.CreateUIButton(
                bpGO, 210, 42,
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Names"), tboxActionBtnFont,
                -12, 0, AnchorPresets.middleRight,
                CopySelectedPackageNamesToClipboard
            );
            tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";
            AddTooltip(tboxCopyPkgNamesBtn, "gallery.tooltip.tbox_copy_names", "Copy package .var names and local Saves/scene paths (one per line) to clipboard");

            tboxDeleteBtn = UI.CreateUIButton(
                bpGO, 180, 42,
                VPBTranslation.T("gallery.tbox.delete", "Delete"), tboxActionBtnFont,
                -12 - 220, 0, AnchorPresets.middleRight,
                TboxDeleteSelectedPackages
            );
            tboxDeleteBtn.name = "Tbox_Delete";
            AddTooltip(tboxDeleteBtn, "gallery.tooltip.tbox_delete", "Move selected packages to DeletedPackages; local Saves/scene JSON (+ preview) to DeletedScenes");
            try
            {
                var delImg = tboxDeleteBtn.GetComponent<Image>();
                if (delImg != null) delImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            }
            catch { }

            tboxAutoInstallBtn = UI.CreateUIButton(
                bpGO, 168, 42,
                VPBTranslation.T("gallery.tbox.autoinstall", "Autoinstall"), tboxActionBtnFont,
                -12 - 220 - 190, 0, AnchorPresets.middleRight,
                TboxAutoInstallSelectedPackages
            );
            tboxAutoInstallBtn.name = "Tbox_AutoInstall";
            AddTooltip(tboxAutoInstallBtn, "gallery.tooltip.tbox_autoinstall", "Flag selected packages for auto-install and auto-load. Packages in AllPackages are copied to AddonPackages on the next VaM start (not immediately).");

            tboxHideBtn = UI.CreateUIButton(
                bpGO, 100, 42,
                VPBTranslation.T("gallery.tbox.hide", "Hide"), tboxActionBtnFont,
                -12 - 220 - 190 - 178, 0, AnchorPresets.middleRight,
                TboxHideSelectedPackages
            );
            tboxHideBtn.name = "Tbox_Hide";
            AddTooltip(tboxHideBtn, "gallery.tooltip.tbox_hide", "Hide selected packages in VaM file lists (AddonPackagesFilePrefs … .hide)");

            const float tboxHideX = -12f - 220f - 190f - 178f;
            tboxUnhideBtn = UI.CreateUIButton(
                bpGO, 100, 42,
                VPBTranslation.T("gallery.tbox.unhide", "Unhide"), tboxActionBtnFont,
                tboxHideX - 10f - 100f, 0, AnchorPresets.middleRight,
                TboxUnhideSelectedPackages
            );
            tboxUnhideBtn.name = "Tbox_Unhide";
            tboxUnhideBtn.SetActive(false);
            AddTooltip(tboxUnhideBtn, "gallery.tooltip.tbox_unhide", "Remove .hide markers for selected packages");

            tboxDisableAutoInstallBtn = UI.CreateUIButton(
                bpGO, 168, 42,
                VPBTranslation.T("gallery.tbox.no_autoinstall", "No autoinstall"), tboxActionBtnFont,
                tboxHideX - 10f - 100f - 10f - 168f, 0, AnchorPresets.middleRight,
                TboxDisableAutoInstallSelectedPackages
            );
            tboxDisableAutoInstallBtn.name = "Tbox_NoAutoInstall";
            tboxDisableAutoInstallBtn.SetActive(false);
            AddTooltip(tboxDisableAutoInstallBtn, "gallery.tooltip.tbox_no_autoinstall", "Clear auto-install and VPB auto-load for selected packages");

            // ── Pin toggle (right edge, always visible) ───────────────────────────
            tboxPinBtn = UI.CreateUIButton(
                tbox, 44, 0, "", 15,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    tboxPinned = !tboxPinned;
                    RefreshTboxPinVisual();
                }
            );
            tboxPinBtn.name = "Tbox_Pin";
            // Pin button is anchored to the bottom row (tooltip row), not the full bar
            var pinRT = tboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin        = new Vector2(1f, 0f);
            pinRT.anchorMax        = new Vector2(1f, 0f);
            pinRT.pivot            = new Vector2(1f, 0f);
            pinRT.anchoredPosition = Vector2.zero;
            pinRT.sizeDelta        = new Vector2(44f, tboxInfoRowHeight);

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

            // Thin separator line at the row boundary (between tooltip row and toolbox row)
            {
                var rowSepGO = new GameObject("RowSeparator");
                rowSepGO.transform.SetParent(tbox.transform, false);
                var rowSepImg = rowSepGO.AddComponent<Image>();
                rowSepImg.color = new Color(1f, 1f, 1f, 0.12f);
                rowSepImg.raycastTarget = false;
                var rowSepRT = rowSepGO.GetComponent<RectTransform>();
                rowSepRT.anchorMin        = new Vector2(0f, 0f);
                rowSepRT.anchorMax        = new Vector2(1f, 0f);
                rowSepRT.pivot            = new Vector2(0.5f, 0f);
                rowSepRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight);
                rowSepRT.sizeDelta        = new Vector2(0f, 1f);

                // Scale action to reposition separator when InnerPaneScale changes
                var rsRT = rowSepRT;
                innerPaneScaleActions.Add(s => {
                    if (rsRT != null) rsRT.anchoredPosition = new Vector2(0f, 60f * s);
                });
            }

            // Scale actions to resize rows when InnerPaneScale changes
            {
                var lRT = tboxLabelLayerRT;
                var bRT = tboxButtonsLayerRT;
                var pRT = pinRT;
                innerPaneScaleActions.Add(s => {
                    float rowH = 60f * s;
                    tboxInfoRowHeight = rowH;
                    if (lRT != null) lRT.sizeDelta = new Vector2(lRT.sizeDelta.x, rowH);
                    if (bRT != null) { bRT.anchoredPosition = new Vector2(0f, rowH); bRT.sizeDelta = new Vector2(bRT.sizeDelta.x, rowH); }
                    if (pRT != null) pRT.sizeDelta = new Vector2(pRT.sizeDelta.x, rowH);
                });
            }

            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");
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

            int sel   = (selectedFiles != null) ? selectedFiles.Count : 0;
            int total = (currentFilteredFiles != null) ? currentFilteredFiles.Count : 0;

            // Update label: "X Selected  ·  Y Items" when selected, or just "Y Items"
            if (tboxLabel != null)
            {
                string countStr = string.Format(VPBTranslation.T("gallery.items.count", "{0} Items"), total);
                if (sel > 0)
                {
                    string selStr = sel == 1
                        ? VPBTranslation.T("gallery.tbox.selected_one", "1 Selected")
                        : string.Format(VPBTranslation.T("gallery.tbox.selected_many", "{0} Selected"), sel);
                    tboxLabel.text = string.Format("{0}  ·  {1}", selStr, countStr);
                }
                else
                {
                    tboxLabel.text = countStr;
                }
            }

            // Expansion only when there is a selection
            bool canExpand = sel > 0;
            if (!canExpand)
            {
                tboxExpandT   = 0f;
                tboxIsHovered = false;
                if (tboxPinned) { tboxPinned = false; RefreshTboxPinVisual(); }
            }

            bool wantExpanded = canExpand && (tboxIsHovered || tboxPinned);

            // Smooth animate expand T — fast snap
            float targetT = wantExpanded ? 1f : 0f;
            tboxExpandT = Mathf.Lerp(tboxExpandT, targetT, Time.deltaTime * 22f);
            if (Mathf.Abs(tboxExpandT - targetT) < 0.005f) tboxExpandT = targetT;

            // Animate bar height: grow offsetMax upward to reveal the buttons row
            if (tboxRT != null)
            {
                float targetTop = tboxTopOffsetBase + tboxInfoRowHeight * tboxExpandT;
                float newTop = Mathf.Lerp(tboxRT.offsetMax.y, targetTop, Time.deltaTime * 22f);
                if (Mathf.Abs(newTop - targetTop) < 0.5f) newTop = targetTop;
                tboxRT.offsetMax = new Vector2(tboxRT.offsetMax.x, newTop);
            }

            // Label is suppressed when path/status is actually visible, or buttons are expanded
            bool pathVisible = hoverPathText != null && hoverPathText.gameObject.activeSelf
                            && hoverPathCanvasGroup != null && hoverPathCanvasGroup.alpha > 0.1f;
            bool infoShowing = pathVisible
                             || !string.IsNullOrEmpty(dragStatusMsg)
                             || !string.IsNullOrEmpty(temporaryStatusMsg);
            // Label alpha tracks collapse directly — no separate lerp needed
            float labelTarget = (infoShowing || tboxExpandT > 0.05f) ? 0f : 1f;
            if (tboxLabelCG != null)
                tboxLabelCG.alpha = Mathf.Lerp(tboxLabelCG.alpha, labelTarget, Time.deltaTime * 22f);

            // Buttons stay fully opaque — RectMask2D handles the slide-in reveal as the bar grows.
            // Gate on tboxExpandT only (not infoShowing) so that a fading hover-path label
            // doesn't suppress buttons and cause them to flash when the path finally fades out.
            if (tboxButtonsCG != null)
            {
                bool showButtons = canExpand && tboxExpandT > 0.05f;
                tboxButtonsCG.alpha          = showButtons ? 1f : 0f;
                tboxButtonsCG.blocksRaycasts = canExpand && tboxExpandT > 0.5f;
                tboxButtonsCG.interactable   = canExpand && tboxExpandT > 0.85f;
            }

            if (sel > 0)
                RefreshTboxConditionalActionButtons();

            // Keep grid / side tab scrollers above the footer while tbox height animates.
            try
            {
                if (contentScrollRT != null)
                {
                    float tabTop = TabScrollTopOffset();
                    SyncGalleryMainAreaBottomEdge(
                        contentScrollRT.offsetMin.x,
                        contentScrollRT.offsetMax.x,
                        contentScrollRT.offsetMax.y,
                        tabTop);
                }
            }
            catch { }
        }

        /// <summary>Copy/Delete/Hide/Unhide/Autoinstall: counts in labels and compact layout for the hide/AI group.</summary>
        private void RefreshTboxConditionalActionButtons()
        {
            int copyN = 0, deleteN = 0, hideN = 0, unhideN = 0, aiN = 0, noAiN = 0;
            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                copyN = CollectUniquePackageUidsFromSelection(selectedFiles).Count
                    + CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles).Count;
                try { deleteN = GetTboxDeleteEligiblePackageCount() + GetTboxDeleteEligibleLocalSceneCount(); } catch { deleteN = 0; }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out bool hidden, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    if (fiAi || uidAl) noAiN++;
                    if (!fiAi || !uidAl) aiN++;
                }
            }

            if (tboxCopyPkgNamesBtn != null)
                SetTboxCountButtonLabel(tboxCopyPkgNamesBtn, "gallery.tbox.copy_names_count", "Copy Names ({0})", copyN);
            if (tboxDeleteBtn != null)
                SetTboxCountButtonLabel(tboxDeleteBtn, "gallery.tbox.delete_count", "Delete ({0})", deleteN);

            bool showHide = hideN > 0;
            bool showUnhide = unhideN > 0;
            bool showAi = aiN > 0;
            bool showNoAi = noAiN > 0;

            if (tboxHideBtn != null)
            {
                tboxHideBtn.SetActive(showHide);
                if (showHide) SetTboxCountButtonLabel(tboxHideBtn, "gallery.tbox.hide_count", "Hide ({0})", hideN);
            }
            if (tboxUnhideBtn != null)
            {
                tboxUnhideBtn.SetActive(showUnhide);
                if (showUnhide) SetTboxCountButtonLabel(tboxUnhideBtn, "gallery.tbox.unhide_count", "Unhide ({0})", unhideN);
            }
            if (tboxAutoInstallBtn != null)
            {
                tboxAutoInstallBtn.SetActive(showAi);
                if (showAi) SetTboxCountButtonLabel(tboxAutoInstallBtn, "gallery.tbox.autoinstall_count", "Autoinstall ({0})", aiN);
            }
            if (tboxDisableAutoInstallBtn != null)
            {
                tboxDisableAutoInstallBtn.SetActive(showNoAi);
                if (showNoAi) SetTboxCountButtonLabel(tboxDisableAutoInstallBtn, "gallery.tbox.no_autoinstall_count", "No autoinstall ({0})", noAiN);
            }

            LayoutTboxHideAutoinstallButtonRow(showAi, showHide, showUnhide, showNoAi);
        }

        private static void SetTboxCountButtonLabel(GameObject go, string key, string fallbackFmt, int count)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
                t.text = string.Format(VPBTranslation.T(key, fallbackFmt), count);
        }

        /// <summary>Repack Autoinstall / Hide / Unhide / No autoinstall against Delete so hidden buttons leave no gap.</summary>
        private void LayoutTboxHideAutoinstallButtonRow(bool showAi, bool showHide, bool showUnhide, bool showNoAi)
        {
            const float gap = 10f;
            const float wAi = 168f;
            const float wHide = 100f;
            const float wUnhide = 100f;
            const float wNoAi = 168f;
            const float deletePivotX = -232f;
            const float deleteW = 180f;
            float x = deletePivotX - deleteW - gap;

            void Place(GameObject go, float width)
            {
                if (go == null) return;
                var rt = go.GetComponent<RectTransform>();
                if (rt == null) return;
                Vector2 p = rt.anchoredPosition;
                rt.anchoredPosition = new Vector2(x, p.y);
                x -= width + gap;
            }

            if (showAi) Place(tboxAutoInstallBtn, wAi);
            if (showHide) Place(tboxHideBtn, wHide);
            if (showUnhide) Place(tboxUnhideBtn, wUnhide);
            if (showNoAi) Place(tboxDisableAutoInstallBtn, wNoAi);
        }

        /// <summary>Unique gallery-relative paths for on-disk Saves/scene JSON rows (for Copy Names).</summary>
        private static HashSet<string> CollectUniqueLocalSceneGalleryRelativePathsFromSelection(IList<FileEntry> files)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return set;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false)) continue;
                if (!string.IsNullOrEmpty(rel)) set.Add(rel.Replace('\\', '/'));
            }
            return set;
        }

        /// <summary>Resolve a gallery row to an on-disk .var for tbox hide/autoinstall actions (one row may share a UID).</summary>
        private bool TryGetTboxResolvablePackageState(FileEntry f, out string uid, out FileEntry diskFe, out bool isHidden, out bool fileAutoInstall, out bool uidAutoLoad)
        {
            uid = null;
            diskFe = null;
            isHidden = false;
            fileAutoInstall = false;
            uidAutoLoad = false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string relGallery, false))
            {
                uid = relGallery.Replace('\\', '/');
                diskFe = f;
                isHidden = PackageHidePrefs.IsLocalSceneJsonHidden(f);
                try { fileAutoInstall = LocalSceneGallerySupport.IsLocalSceneAutoInstallMarked(f); }
                catch { fileAutoInstall = false; }
                uidAutoLoad = false;
                return true;
            }

            uid = TryGetPackageUidForEntry(f);
            if (string.IsNullOrEmpty(uid)) return false;

            string path = ResolveVarPathForUid(uid);
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                var fe = FileManager.GetFileEntry(path, true);
                if (fe == null) return false;
                diskFe = fe;
                isHidden = PackageHidePrefs.IsPackageVarHidden(fe);
                try { fileAutoInstall = fe.IsAutoInstall(); }
                catch { fileAutoInstall = false; }
                try
                {
                    uidAutoLoad = AutoLoadPackagesManager.Instance != null && AutoLoadPackagesManager.Instance.IsAutoLoad(uid);
                }
                catch { uidAutoLoad = false; }
                return true;
            }
            catch
            {
                return false;
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

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
                var localScenes = CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles);
                if (uids.Count == 0 && localScenes.Count == 0)
                {
                    ShowTemporaryStatus("No package or local scene paths in selection.");
                    return;
                }

                var list = new List<string>(uids.Count + localScenes.Count);
                foreach (var uid in uids)
                    list.Add(uid + ".var");
                foreach (var rel in localScenes)
                    list.Add(rel);
                list.Sort(StringComparer.OrdinalIgnoreCase);

                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} name(s) to clipboard.", 2f);
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
