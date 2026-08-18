using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public string GetCurrentPath() => currentPath;
        public string GetCurrentExtension() => currentExtension;
        public string GetCurrentCreator() => currentCreator;
        // Prefer browse category over chrome titleText — Settings/History overlays paint
        // titleText without changing currentCategoryTitle; hide/restore must not treat those as category.
        public string GetTitle() =>
            !string.IsNullOrEmpty(currentCategoryTitle)
                ? currentCategoryTitle
                : (titleText != null ? titleText.text : "");
        public RectTransform GetBackgroundRT() => backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;

        public ContentType? GetLeftActiveContent() => leftActiveContent;
        public ContentType? GetRightActiveContent() => rightActiveContent;

        public void SetLeftActiveContent(ContentType? type)
        {
            if (type.HasValue && (type.Value == ContentType.Category || type.Value == ContentType.Creator))
                ExitCleanupModeForSidePanelNavigation();
            leftActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        public void SetRightActiveContent(ContentType? type)
        {
            if (type.HasValue && (type.Value == ContentType.Category || type.Value == ContentType.Creator))
                ExitCleanupModeForSidePanelNavigation();
            rightActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        /// <summary>
        /// Applies side-rail / Import layout for a new pane.
        /// Cold VaM launch: <see cref="VPBConfig.GalleryDefaultLeftSidePanel"/> / Right (settings).
        /// In-session Close/reopen: <see cref="VPBConfig.LastGalleryLeftSidePanel"/> / Right (browse memory).
        /// </summary>
        public void ApplySidePanelDefaultsFromConfig()
        {
            if (VPBConfig.Instance == null) return;

            // Disk Last* alone must not win on cold start — only in-session memory (Jakob: settings = launch contract).
            bool restoreRemembered = Gallery.SessionBrowseMemoryActive;

            string ls = restoreRemembered
                ? VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.LastGalleryLeftSidePanel)
                : VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultLeftSidePanel);
            string rs = restoreRemembered
                ? VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.LastGalleryRightSidePanel)
                : VPBConfig.NormalizeGallerySidePanel(VPBConfig.Instance.GalleryDefaultRightSidePanel);

            bool leftImport = string.Equals(ls, "Import", StringComparison.OrdinalIgnoreCase);
            bool rightImport = string.Equals(rs, "Import", StringComparison.OrdinalIgnoreCase);
            if (leftImport && rightImport)
                rightImport = false;

            ContentType? l = leftImport ? null : SidePanelStringToContentType(ls);
            ContentType? r = rightImport ? null : SidePanelStringToContentType(rs);
            if (VPBConfig.Instance.GalleryHideCreatorSideButtons)
            {
                if (l == ContentType.Creator) l = null;
                if (r == ContentType.Creator) r = null;
            }
            if (l.HasValue && r.HasValue && l.Value == r.Value)
                r = null;

            ContentType? targetL = l;
            ContentType? targetR = r;
            // Floating fallback: only for desktop (non-VR) floating mode on first-open defaults —
            // not when restoring remembered None/None (user closed both rails).
            bool isVR = XrUtils.IsVrActive();
            bool vrAnchorMode = isVR && VPBConfig.Instance != null && VPBConfig.Instance.GalleryAnchorToVamMenu;
            bool wantImport = !importSidebarInitAsClone && (leftImport || rightImport);
            if (!restoreRemembered && !isFixedLocally && !vrAnchorMode && !targetL.HasValue && !targetR.HasValue && !wantImport)
                targetR = ContentType.Category;

            bool contentSame = NullableContentTypeEqual(leftActiveContent, targetL)
                && NullableContentTypeEqual(rightActiveContent, targetR);
            bool importSame = !wantImport
                || (importSidebarOpenIntent && importSidebarOnLeft == leftImport);
            if (contentSame && importSame)
                return;

            leftActiveContent = targetL;
            rightActiveContent = targetR;

            if (wantImport)
            {
                importSidebarOpenIntent = true;
                importSidebarOpenIntentLoaded = true;
                importSidebarForceOnLeft = leftImport;
                try { RefreshImportSidebarCategoryGate(); } catch { }
                try { PersistImportSidebarOpenIntent(); } catch { }
            }
            else if (restoreRemembered)
            {
                // Remembered layout had no Import — do not let TryRestoreImportSidebarOpenFromGlobalPref reopen it.
                importSidebarOpenIntent = false;
                importSidebarOpenIntentLoaded = true;
                try { RefreshImportSidebarCategoryGate(); } catch { }
            }
            else if (!importSidebarInitAsClone && importSidebarOpenIntent)
            {
                importSidebarOpenIntent = false;
                importSidebarOpenIntentLoaded = true;
                try { RefreshImportSidebarCategoryGate(); } catch { }
                try { PersistImportSidebarOpenIntent(); } catch { }
            }

            UpdateLayout();
            UpdateTabs();
            if (leftActiveContent == ContentType.UserTags || rightActiveContent == ContentType.UserTags)
                try { ApplyDefaultUserTagAvailModeOnTagsPanelOpen(); } catch { }
            try { RefreshSceneImportSideButtonVisibility(); } catch { }
            try { UpdateSideButtonPositions(); } catch { }
        }

        private static bool NullableContentTypeEqual(ContentType? a, ContentType? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            return a.Value == b.Value;
        }

        private static ContentType? SidePanelStringToContentType(string normalized)
        {
            if (string.Equals(normalized, "Category", StringComparison.OrdinalIgnoreCase))
                return ContentType.Category;
            if (string.Equals(normalized, "Creator", StringComparison.OrdinalIgnoreCase))
                return ContentType.Creator;
            if (string.Equals(normalized, "Tags", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "UserTags", StringComparison.OrdinalIgnoreCase))
                return ContentType.UserTags;
            if (string.Equals(normalized, "Path", StringComparison.OrdinalIgnoreCase))
                return ContentType.Path;
            if (string.Equals(normalized, "History", StringComparison.OrdinalIgnoreCase))
                return ContentType.History;
            return null;
        }

        /// <summary>Maps open side content to config token (None / Category / Creator / Tags / Path / History).</summary>
        private static string ContentTypeToSidePanelString(ContentType? type)
        {
            if (!type.HasValue) return "None";
            switch (type.Value)
            {
                case ContentType.Category: return "Category";
                case ContentType.Creator: return "Creator";
                case ContentType.UserTags: return "Tags";
                case ContentType.Path: return "Path";
                case ContentType.History: return "History";
                default: return "None";
            }
        }

        public void SetFilters(string path, string extension, string creator)
        {
            currentPath = path;
            currentExtension = extension;
            currentCreator = creator;
            _currentCreatorSetSrc = null;
            try { UpdateTitleCreatorButtonVisual(); } catch { }
            creatorsCached = false;
            tagsCached = false;
            categoriesCached = false;
        }

        private void CreateResizeHandles()
        {
            // Handles are seated as real children of the bar layout groups so they inherit the exact
            // padding/spacing of the existing bar buttons. Only the show/hide (dock mode + anchor) differs.
            GameObject leftSlot = _footerLeftSectionRT != null ? _footerLeftSectionRT.gameObject : backgroundBoxGO;
            GameObject rightSlot = _footerRightSectionRT != null ? _footerRightSectionRT.gameObject : backgroundBoxGO;
            Transform titleBarTr = backgroundBoxGO != null ? backgroundBoxGO.transform.Find("TitleBar") : null;
            GameObject titleBar = titleBarTr != null ? titleBarTr.gameObject : backgroundBoxGO;

            // Floating-mode corner handles, alongside the bar buttons.
            _resizeHandleBottomRightGO = CreateFloatingResizeHandle(AnchorPresets.bottomRight, rightSlot, false);
            _resizeHandleBottomLeftGO = CreateFloatingResizeHandle(AnchorPresets.bottomLeft, leftSlot, true);
            _resizeHandleTopLeftGO = CreateTitleBarResizeHandle(titleBar);

            // Fixed-dock handles share the same footer corner slots.
            CreateFixedModeResizeHandle(leftSlot, rightSlot);
        }

        private void CreateFixedModeResizeHandle(GameObject leftSlot, GameObject rightSlot)
        {
            // Create Preview Border
            GameObject previewGO = new GameObject("ResizePreviewBorder");
            previewGO.transform.SetParent(canvas.transform, false);
            Image previewImg = previewGO.AddComponent<Image>();
            previewImg.color = new Color(0.1f, 0.3f, 0.1f, 0.4f); // 40% visibility fill
            previewImg.raycastTarget = false;
            Outline previewOutline = previewGO.AddComponent<Outline>();
            previewOutline.effectColor = new Color(0.1f, 0.3f, 0.1f, 0.4f); // 40% visibility border
            previewOutline.effectDistance = new Vector2(2, -2);
            previewBorderRT = previewGO.GetComponent<RectTransform>();
            previewGO.SetActive(false);
            previewGO.transform.SetAsLastSibling();

            // Handle for Right/Top dock: footer left slot, drags anchorMin.x (width) + height.
            {
                GameObject handleGO = UI.AddChildGOImage(leftSlot, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                    GalleryUiDesignTokens.ResizeHandleFixedHitRef, GalleryUiDesignTokens.ResizeHandleFixedHitRef, Vector2.zero, rounded: true);
                handleGO.name = "ResizeHandle_FixedBottom";
                handleGO.GetComponent<Image>().raycastTarget = true;
                handleGO.AddComponent<UIDragBlocker>();
                handleGO.AddComponent<UIHoverBorder>();

                {
                    Sprite chevron = UI.LoadIconSprite("chevrons-down-left", UI.BarIconGlyphTint);
                    if (chevron != null)
                        UI.AddIconToButton(handleGO, chevron);
                }

                UIAnchorResizer resizer = handleGO.AddComponent<UIAnchorResizer>();
                resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
                resizer.previewTarget = previewBorderRT;
                resizer.deferred = true;
                resizer.resizeX = true;
                resizer.resizeY = true;
                resizer.minAnchorX = 0.05f;
                resizer.maxAnchorX = 0.85f;
                resizer.minAnchorY = 0.05f;
                resizer.maxAnchorY = 0.85f;

                resizer.onResizedVec2 = (val) => {
                    if (VPBConfig.Instance != null) {
                        // Right dock: update width+height. Top dock: update height only (width anchored full).
                        if (EffectiveDockSide == GalleryDockSide.Right)
                            DockWidthFree = ClampDockWidthFreeAgainstOpposite(GalleryDockSide.Right, val.x);
                        DockCustomHeight = val.y;
                        if (DockHeightMode == 0 && val.y > 0.05f) {
                            DockHeightMode = 1;
                            UpdateFooterHeightState();
                        }
                        UpdateLayout();
                    }
                };
                resizer.onResizeStatusChange = (isResizing) => {
                     this.isResizing = isResizing;
                     if (!isResizing && VPBConfig.Instance != null) {
                         VPBConfig.Instance.Save();
                     }
                };

                AddHoverDelegate(handleGO);
                AddTooltip(handleGO, "gallery.tooltip.resize_handle", "Drag to resize the panel");
                handleGO.transform.SetAsFirstSibling();
                { var rt = handleGO.GetComponent<RectTransform>(); innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); }); }
                handleGO.SetActive(false);
                _resizeHandleFixedBottomGO = handleGO;
            }

            // Handle for Left dock: footer right slot, drags anchorMax.x (width) + height.
            {
                GameObject handleGO = UI.AddChildGOImage(rightSlot, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                    GalleryUiDesignTokens.ResizeHandleFixedHitRef, GalleryUiDesignTokens.ResizeHandleFixedHitRef, Vector2.zero, rounded: true);
                handleGO.name = "ResizeHandle_FixedBottomRight";
                handleGO.GetComponent<Image>().raycastTarget = true;
                handleGO.AddComponent<UIDragBlocker>();
                handleGO.AddComponent<UIHoverBorder>();

                {
                    Sprite chevron = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
                    if (chevron != null)
                        UI.AddIconToButton(handleGO, chevron);
                }

                UIAnchorResizer resizer = handleGO.AddComponent<UIAnchorResizer>();
                resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
                resizer.previewTarget = previewBorderRT;
                resizer.deferred = true;
                resizer.resizeX = true;
                resizer.resizeY = true;
                resizer.resizeAnchorMaxX = true;
                resizer.minAnchorX = 0.15f; // min panel width (anchorMax.x)
                resizer.maxAnchorX = 0.95f; // max panel width (anchorMax.x)
                resizer.minAnchorY = 0.05f;
                resizer.maxAnchorY = 0.85f;

                resizer.onResizedVec2 = (val) => {
                    if (VPBConfig.Instance != null) {
                        // Right/Top dock: height only (width anchored full). Left dock: anchorMax.x → "right inset".
                        if (EffectiveDockSide == GalleryDockSide.Left)
                            DockWidthFree = ClampDockWidthFreeAgainstOpposite(GalleryDockSide.Left, 1f - val.x);
                        DockCustomHeight = val.y;
                        if (DockHeightMode == 0 && val.y > 0.05f) {
                            DockHeightMode = 1;
                            UpdateFooterHeightState();
                        }
                        UpdateLayout();
                    }
                };
                resizer.onResizeStatusChange = (isResizing) => {
                     this.isResizing = isResizing;
                     if (!isResizing && VPBConfig.Instance != null) {
                         VPBConfig.Instance.Save();
                     }
                };

                AddHoverDelegate(handleGO);
                AddTooltip(handleGO, "gallery.tooltip.resize_handle", "Drag to resize the panel");
                handleGO.transform.SetAsLastSibling();
                { var rt = handleGO.GetComponent<RectTransform>(); innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); }); }
                handleGO.SetActive(false);
                _resizeHandleFixedBottomRightGO = handleGO;
            }
        }

        private static string CornerResizeChevronPath(int anchor)
        {
            if (anchor == AnchorPresets.bottomRight) return "chevrons-down-right";
            if (anchor == AnchorPresets.bottomLeft) return "chevrons-down-left";
            if (anchor == AnchorPresets.topRight) return "chevrons-up-right";
            return "chevrons-up-left"; // topLeft
        }

        /// <summary>Floating-mode corner handle seated as a footer-bar layout child (inherits bar padding/spacing).</summary>
        private GameObject CreateFloatingResizeHandle(int anchor, GameObject parent, bool asFirstSibling)
        {
            GameObject handleGO = UI.AddChildGOImage(parent, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                GalleryUiDesignTokens.ResizeHandleCornerHitRef, GalleryUiDesignTokens.ResizeHandleCornerHitRef, Vector2.zero, rounded: true);
            handleGO.name = "ResizeHandle_" + anchor;
            handleGO.GetComponent<Image>().raycastTarget = true;
            handleGO.AddComponent<UIHoverBorder>();

            Sprite chevron = UI.LoadIconSprite(CornerResizeChevronPath(anchor), UI.BarIconGlyphTint);
            if (chevron != null)
                UI.AddIconToButton(handleGO, chevron);

            UIResizable resizer = handleGO.AddComponent<UIResizable>();
            resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
            resizer.anchor = anchor;
            resizer.onResizeStatusChange = (isResizing) => { this.isResizing = isResizing; };

            AddHoverDelegate(handleGO);
            AddTooltip(handleGO, "gallery.tooltip.resize_handle", "Drag to resize the panel");

            if (asFirstSibling) handleGO.transform.SetAsFirstSibling();
            else handleGO.transform.SetAsLastSibling();

            // Match the bar buttons: 40px square scaled by chrome scale.
            { var rt = handleGO.GetComponent<RectTransform>(); innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); }); }

            return handleGO;
        }

        /// <summary>Floating-mode top-left handle in the (non-layout) title bar, vertically centred like its buttons.</summary>
        private GameObject CreateTitleBarResizeHandle(GameObject titleBar)
        {
            const int anchor = AnchorPresets.topLeft;
            GameObject handleGO = UI.AddChildGOImage(titleBar, UI.IconButtonBackdrop, AnchorPresets.topLeft,
                GalleryUiDesignTokens.ButtonSizeRef, GalleryUiDesignTokens.ButtonSizeRef, Vector2.zero, rounded: true);
            handleGO.name = "ResizeHandle_" + anchor;
            handleGO.GetComponent<Image>().raycastTarget = true;
            handleGO.AddComponent<UIHoverBorder>();

            // Far-left of the title bar, vertically centred on the bar like the title-bar buttons.
            RectTransform handleRT = handleGO.GetComponent<RectTransform>();
            handleRT.pivot = new Vector2(0.5f, 0.5f);
            float xInset = GalleryUiDesignTokens.ResizeHandleEdgeMarginRef + GalleryUiDesignTokens.ButtonSizeRef * 0.5f;
            float yInset = GalleryUiDesignTokens.ResizeHandleTitleCenterYRef;
            handleRT.anchoredPosition = new Vector2(xInset, -yInset);

            Sprite chevron = UI.LoadIconSprite(CornerResizeChevronPath(anchor), UI.BarIconGlyphTint);
            if (chevron != null)
                UI.AddIconToButton(handleGO, chevron);

            UIResizable resizer = handleGO.AddComponent<UIResizable>();
            resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
            resizer.anchor = anchor;
            resizer.onResizeStatusChange = (isResizing) => { this.isResizing = isResizing; };

            AddHoverDelegate(handleGO);
            AddTooltip(handleGO, "gallery.tooltip.resize_handle", "Drag to resize the panel");

            { var rt = handleRT; innerPaneScaleActions.Add(s => { if (rt) { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.ButtonSizeRef * s, GalleryUiDesignTokens.ButtonSizeRef * s); rt.anchoredPosition = new Vector2(xInset * s, -yInset * s); } }); }

            return handleGO;
        }
    }
}
