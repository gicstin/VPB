using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Geometry mirrors the tab columns in GalleryPanel.Lifecycle.Init.cs (220 width, 10 side margin, -95 top).
        // Bottom inset comes from SideTabScrollBottomInsetY() so the Apply button clears the full footer chrome.
        private const float ImportSidebarBaseWidth = 220f;
        private const float ImportSidebarBaseHeaderHeight = 32f;
        private const float ImportSidebarBaseApplyHeight = 30f;
        private const float ImportSidebarBaseSideMargin = 10f;
        private const float ImportSidebarBaseTopOffset = -95f;

        // Match Creator/Category row sizing in GalleryPanel.Tabs.cs (preferredHeight = 35*s,
        // fontSize 18) so the Import sidebar reads as part of the same UI family.
        public const float ImportSidebarBaseRowHeight = 35f;
        public const int ImportSidebarBaseFontSize = 18;
        public const int ImportSidebarBaseFontSizeMin = 10;

        private RectTransform importSidebarRT;
        private Transform importSidebarHeaderRoot;
        private RectTransform importSidebarHeaderRT;
        private Text importSidebarHeaderLabel;
        // Single scroll body: header pinned top, Apply pinned bottom, everything else scrolls between them.
        private RectTransform importSidebarBodyScrollRT;     // CreateVScrollableContent root (the scroll viewport host)
        private RectTransform importSidebarScrollContentRT;  // VLG content node holding all rows (target of ForceRebuild)
        private RectTransform importSidebarApplyRT;          // pinned Apply button

        partial void BuildImportSidebar()
        {
            // Parent is backgroundBoxGO so the sidebar layers above the gallery grid
            // at the same z-depth as rightTabScrollGO (Creator/Category column).
            Transform parent = ResolveImportSidebarParent();
            if (parent == null)
            {
                LogUtil.LogError("[VPB import] Could not resolve sidebar parent: gallery panel not initialized");
                return;
            }

            // [diag] Root is created active and only deactivated at the end, so a mid-build throw leaves a half-rendered header; stage logs pin the throw, the catch destroys the partial tree so failure is a clean no-op.
            try
            {
                LogUtil.Log("[VPB import][diag] build: start");

                importSidebarRoot = new GameObject("VPB_ImportSidebar");
                importSidebarRoot.transform.SetParent(parent, false);

                importSidebarRT = importSidebarRoot.AddComponent<RectTransform>();
                ApplyImportSidebarBaseRect(1f);

                // Transparent root, like leftTabScrollGO / rightTabScrollGO. Rows render against
                // the gallery panel background, so the sidebar visually reads as part of the same
                // UI family rather than a foreign popup tinted with PopupBackdrop.
                Image bg = importSidebarRoot.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0f);
                bg.raycastTarget = false;

                int siblingIndex = ResolveImportSidebarSiblingIndex(parent);
                importSidebarRoot.transform.SetSiblingIndex(siblingIndex);

                LogUtil.Log("[VPB import][diag] build: header");
                BuildImportSidebarHeader();
                LogUtil.Log("[VPB import][diag] build: body scroll");
                BuildImportSidebarBodyScroll();
                LogUtil.Log("[VPB import][diag] build: pinned apply");
                BuildImportSidebarPinnedApply();
                LogUtil.Log("[VPB import][diag] build: atom rows");
                BuildImportSidebarAtomRows(importSidebarScrollContentRT);
                LogUtil.Log("[VPB import][diag] build: options rows");
                BuildImportSidebarOptionsRows(importSidebarScrollContentRT);

                // Re-run rect/font scaling whenever VPB's inner-pane scale changes (Settings UI scale slider).
                innerPaneScaleActions.Add(ApplyImportSidebarBaseRect);
                // The scroll content (VLG + ContentSizeFitter) only recomputes when forced; rebuild it after a scale
                // change so row heights / the type-radio grid settle to the new scale.
                innerPaneScaleActions.Add(s => RebuildImportSidebarContent());

                ApplyImportSidebarBaseRect(VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f);
                RebuildImportSidebarContent();
                importSidebarRoot.SetActive(false);
                LogUtil.Log("[VPB import][diag] build: complete OK");
            }
            catch (System.Exception ex)
            {
                LogUtil.LogError("[VPB import][diag] build FAILED (stage just logged above): " + ex);
                if (importSidebarRoot != null)
                {
                    UnityEngine.Object.Destroy(importSidebarRoot);
                    importSidebarRoot = null;
                }
            }
        }

        // Vertical-stretch rect (anchored to panel top AND bottom) with raw-px insets, mirroring leftTabRT/rightTabRT
        // so it tracks the panel at any UI scale instead of a fixed-height box that overflows the column.
        private void ApplyImportSidebarBaseRect(float s)
        {
            if (importSidebarRT == null) return;
            float w = ImportSidebarBaseWidth;
            float margin = ImportSidebarBaseSideMargin;
            float top = ImportSidebarBaseTopOffset;          // negative: inset down from panel top
            // Clear the full footer chrome (the tab-column container uses 68, but its visible content stops at this
            // larger inset; the sidebar's Apply button is visible at the bottom, so it must clear the whole footer).
            float bottom = SideTabScrollBottomInsetY();

            importSidebarRT.pivot = new Vector2(0.5f, 0.5f);
            if (importSidebarOnLeft)
            {
                importSidebarRT.anchorMin = new Vector2(0f, 0f);
                importSidebarRT.anchorMax = new Vector2(0f, 1f);
                importSidebarRT.offsetMin = new Vector2(margin, bottom);
                importSidebarRT.offsetMax = new Vector2(margin + w, top);
            }
            else
            {
                importSidebarRT.anchorMin = new Vector2(1f, 0f);
                importSidebarRT.anchorMax = new Vector2(1f, 1f);
                importSidebarRT.offsetMin = new Vector2(-margin - w, bottom);
                importSidebarRT.offsetMax = new Vector2(-margin, top);
            }

            float headerH = ImportSidebarBaseHeaderHeight * s;
            float applyH = ImportSidebarBaseApplyHeight * s;
            if (importSidebarHeaderRT != null)
                importSidebarHeaderRT.sizeDelta = new Vector2(0f, headerH);
            // Apply pinned to the bottom; body scroll fills between the header and the Apply button.
            if (importSidebarApplyRT != null)
                importSidebarApplyRT.sizeDelta = new Vector2(0f, applyH);
            if (importSidebarBodyScrollRT != null)
            {
                importSidebarBodyScrollRT.offsetMin = new Vector2(0f, applyH);
                importSidebarBodyScrollRT.offsetMax = new Vector2(0f, -headerH);
            }
            ApplyScaledFont(importSidebarHeaderLabel, ImportSidebarBaseFontSize, s);
        }

        // Same clamp-and-localScale technique GalleryPanel.Tabs.cs uses to keep text legible
        // at low scales (Unity Text.fontSize is int and visually clamps below ~10).
        public static void ApplyScaledFont(Text txt, int baseFont, float s)
        {
            if (txt == null) return;
            if (baseFont <= 0) baseFont = 1;
            int minFont = ImportSidebarBaseFontSizeMin;
            float fontScale = Mathf.Clamp(s, (float)minFont / (float)baseFont, 100f);
            txt.fontSize = Mathf.RoundToInt(baseFont * fontScale);
            float extra = (fontScale > 0f) ? (s / fontScale) : 1f;
            txt.transform.localScale = new Vector3(extra, extra, 1f);
        }

        private Transform ResolveImportSidebarParent()
        {
            return backgroundBoxGO != null ? backgroundBoxGO.transform : null;
        }

        private int ResolveImportSidebarSiblingIndex(Transform parent)
        {
            return Mathf.Max(0, parent.childCount - 1);
        }

        private void BuildImportSidebarHeader()
        {
            GameObject header = new GameObject("Header");
            header.transform.SetParent(importSidebarRoot.transform, false);
            importSidebarHeaderRT = header.AddComponent<RectTransform>();
            importSidebarHeaderRoot = importSidebarHeaderRT;
            importSidebarHeaderRT.anchorMin = new Vector2(0f, 1f);
            importSidebarHeaderRT.anchorMax = new Vector2(1f, 1f);
            importSidebarHeaderRT.pivot = new Vector2(0.5f, 1f);
            importSidebarHeaderRT.sizeDelta = new Vector2(0f, ImportSidebarBaseHeaderHeight);
            importSidebarHeaderRT.anchoredPosition = Vector2.zero;

            // Header tone matches the selected-Category row color so users recognize it as
            // a side-column header rather than a generic popup chrome.
            Image bg = header.AddComponent<Image>();
            bg.color = ColorCategory;

            importSidebarHeaderLabel = CreateImportSidebarLabel(
                header.transform,
                VPBTranslation.T("gallery.import.sidebar_header", "Import"),
                ImportSidebarBaseFontSize);
            importSidebarHeaderLabel.color = Color.white;
            importSidebarHeaderLabel.alignment = TextAnchor.MiddleCenter;
        }

        // One scroll for the whole body (between the pinned header and pinned Apply): all rows live in its VLG content
        // and scroll as a unit when the panel is short, instead of fixed bands that clip. Insets set in ApplyImportSidebarBaseRect.
        private void BuildImportSidebarBodyScroll()
        {
            GameObject scroll = UI.CreateVScrollableContent(
                importSidebarRoot, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 10f, spacing: 2f, addBottomFlexSpacer: false);
            importSidebarBodyScrollRT = scroll.GetComponent<RectTransform>();
            importSidebarScrollContentRT = scroll.GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
        }

        private void BuildImportSidebarPinnedApply()
        {
            BuildImportSidebarApplyButton(importSidebarRoot.transform);
            importSidebarApplyRT = importSidebarApplyButton != null
                ? importSidebarApplyButton.GetComponent<RectTransform>() : null;
        }

        // Force the scroll content's VLG + ContentSizeFitter to recompute (size changes after scale, type swap, or row
        // count change don't settle on their own reliably for nested layout groups).
        private void RebuildImportSidebarContent()
        {
            if (importSidebarScrollContentRT != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(importSidebarScrollContentRT);
        }

        private Text CreateImportSidebarLabel(Transform parent, string text, int fontSize)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);

            Text t = go.AddComponent<Text>();
            t.text = text;
            t.color = UI.TextPrimary;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleLeft;
            VPBUiFont.ApplyTo(t);
            t.raycastTarget = false;
            return t;
        }

        // [diag] Dump resolved rects one frame after activation so an empty-body symptom can be
        // attributed to zero-size / off-screen containers vs missing children, without guessing.
        private System.Collections.IEnumerator DiagDumpImportSidebarRects()
        {
            yield return new WaitForEndOfFrame();
            RebuildImportSidebarContent();
            yield return null;  // let the forced rebuild settle before reading rects
            DiagLogRect("root", importSidebarRT);
            DiagLogRect("header", importSidebarHeaderRT);
            DiagLogRect("bodyScroll", importSidebarBodyScrollRT);
            DiagLogRect("scrollContent", importSidebarScrollContentRT);
            DiagLogRect("typeRadio", importSidebarTypeRadioContainer as RectTransform);
            DiagLogRect("optionsHost", importSidebarOptionsPanelHost as RectTransform);
            DiagLogRect("apply", importSidebarApplyRT);
            // The type-radio overflow check: cellSize vs panel width tells if the cellW fix took.
            RectTransform trc = importSidebarTypeRadioContainer as RectTransform;
            GridLayoutGroup g = trc != null ? trc.GetComponent<GridLayoutGroup>() : null;
            if (g != null)
                LogUtil.Log("[VPB import][diag] typeRadio cellSize=(" + g.cellSize.x.ToString("F1") + "x"
                    + g.cellSize.y.ToString("F1") + ") gridWidth=" + (trc != null ? trc.rect.width.ToString("F0") : "?"));
        }

        private void DiagLogRect(string name, RectTransform rt)
        {
            if (rt == null) { LogUtil.Log("[VPB import][diag] rect " + name + " = NULL"); return; }
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            LogUtil.Log("[VPB import][diag] rect " + name
                + " size=(" + rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0") + ")"
                + " active=" + rt.gameObject.activeInHierarchy
                + " worldBL=(" + c[0].x.ToString("F0") + "," + c[0].y.ToString("F0") + ")"
                + " worldTR=(" + c[2].x.ToString("F0") + "," + c[2].y.ToString("F0") + ")");
        }

        partial void UpdateImportToggleBtnVisual()
        {
            // Highlight the toolbox "Scene Import" button (the sidebar toggle) with a filled accent while open.
            if (tboxSceneImportBtn == null) return;
            Image img = tboxSceneImportBtn.GetComponent<Image>();
            if (img == null) return;
            img.color = importSidebarActive
                ? new Color(0.2f, 0.45f, 0.75f, 0.9f)
                : UI.IconButtonBackdrop;
        }

    }
}
