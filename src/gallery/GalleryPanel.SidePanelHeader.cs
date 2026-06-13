using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const float SidePanelHeaderHeightRef = 32f;
        private const float SidePanelFilterRowTopRef = 44f;
        private const float SidePanelHeaderGapRef = 4f;
        private const float SidePanelHeaderColumnWidthRef = 210f;
        private const int SidePanelHeaderFontRef = GalleryUiDesignTokens.FontCaptionRef;

        private GameObject _leftSidePanelHeaderGO;
        private Text _leftSidePanelHeaderTitle;
        private GameObject _rightSidePanelHeaderGO;
        private Text _rightSidePanelHeaderTitle;

        private string ResolveDesktopFixedDockSide()
        {
            if (VPBConfig.Instance == null) return "Right";
            try
            {
                if (VPBConfig.Instance.DesktopFixedEnforceDockSide)
                    return VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedEnforcedDockSide);
                return VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
            }
            catch { return "Right"; }
        }

        private bool SideTabColumnFlushLeftEdge()
        {
            if (CategoryQuickSwitchUsesAnchoredTitleLayout()) return true;
            if (!isFixedLocally) return false;
            return string.Equals(ResolveDesktopFixedDockSide(), "Left", System.StringComparison.OrdinalIgnoreCase);
        }

        private bool SideTabColumnFlushRightEdge()
        {
            if (!isFixedLocally) return false;
            return string.Equals(ResolveDesktopFixedDockSide(), "Right", System.StringComparison.OrdinalIgnoreCase);
        }

        private float SideTabColumnLeftInsetX(float s)
        {
            if (s <= 0f) s = 1f;
            return SideTabColumnFlushLeftEdge() ? 0f : ImportSidebarBaseSideMargin * s;
        }

        private float SideTabColumnRightInsetX(float s)
        {
            if (s <= 0f) s = 1f;
            return SideTabColumnFlushRightEdge() ? 0f : -ImportSidebarBaseSideMargin * s;
        }

        private static string SidePanelHeaderTranslation(string key, string fallback)
        {
            string t = VPBTranslation.T(key, fallback);
            if (string.IsNullOrEmpty(t)) return fallback;
            return t.Replace("\\n", " ").Replace("\n", " ").Trim();
        }

        private bool SidePanelHeaderVisibleForSide(bool isLeft)
        {
            if (isLeft)
                return leftTabScrollGO != null && leftTabScrollGO.activeSelf && ShouldShowSidePanelHeader(leftActiveContent);
            return rightTabScrollGO != null && rightTabScrollGO.activeSelf && ShouldShowSidePanelHeader(rightActiveContent);
        }

        private float SidePanelHeaderInsetForSide(bool isLeft, float s)
        {
            if (!SidePanelHeaderVisibleForSide(isLeft)) return 0f;
            return SidePanelHeaderHeightRef * s + SidePanelHeaderGapRef * s;
        }

        internal float SidePanelFilterRowYForSide(bool isLeft, float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            // Side-rail chrome always sits below the full-width title bar (top dock included);
            // TabScrollTopOffset reserves the same SideTabTopOffsetRef, so they must stay aligned.
            float rowTop = SidePanelFilterRowTopRef * s;
            return -(rowTop + SidePanelHeaderInsetForSide(isLeft, s));
        }

        private float SidePanelHeaderAnchorY(float s)
        {
            if (s <= 0f) s = 1f;
            return -SidePanelFilterRowTopRef * s;
        }

        private float SideTabColumnLeftMarginPx(float s) => SideTabColumnLeftInsetX(s);

        private float SideTabColumnRightMarginPx(float s) => -SideTabColumnRightInsetX(s);

        private void ApplySideTabColumnHorizontalInset(RectTransform rt, bool isLeft, float s)
        {
            if (rt == null) return;
            float tabW = GalleryUiDesignTokens.SideTabColumnWidthRef * s;
            if (isLeft)
            {
                float m = SideTabColumnLeftMarginPx(s);
                rt.offsetMin = new Vector2(m, rt.offsetMin.y);
                rt.offsetMax = new Vector2(m + tabW, rt.offsetMax.y);
            }
            else
            {
                float m = SideTabColumnRightMarginPx(s);
                rt.offsetMin = new Vector2(-(tabW + m), rt.offsetMin.y);
                rt.offsetMax = new Vector2(-m, rt.offsetMax.y);
            }
        }

        internal void SyncSideTabColumnHorizontalInsets(float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            if (leftTabScrollGO != null)
                ApplySideTabColumnHorizontalInset(leftTabScrollGO.GetComponent<RectTransform>(), true, s);
            if (leftSubTabScrollGO != null)
                ApplySideTabColumnHorizontalInset(leftSubTabScrollGO.GetComponent<RectTransform>(), true, s);
            if (rightTabScrollGO != null)
                ApplySideTabColumnHorizontalInset(rightTabScrollGO.GetComponent<RectTransform>(), false, s);
            if (rightSubTabScrollGO != null)
                ApplySideTabColumnHorizontalInset(rightSubTabScrollGO.GetComponent<RectTransform>(), false, s);
        }

        private string ResolveCleanupSidePanelHeaderTitle()
        {
            string cleanup = SidePanelHeaderTranslation("gallery.tbox.cleanup", "Cleanup");
            if (cleanupModeActive && GetCleanupFilterMode() == 4)
            {
                string stale = SidePanelHeaderTranslation("gallery.cleanup.tab.stale", "Stale Cache");
                return cleanup + " \u00b7 " + stale;
            }
            return cleanup;
        }

        private float SidePanelHeaderExtraTopInset()
        {
            float s = ChromeScale;
            float extra = 0f;
            if (SidePanelHeaderVisibleForSide(true)) extra = Mathf.Max(extra, SidePanelHeaderInsetForSide(true, s));
            if (SidePanelHeaderVisibleForSide(false)) extra = Mathf.Max(extra, SidePanelHeaderInsetForSide(false, s));
            if (importSidebarActive)
                extra = Mathf.Max(extra, ImportSidebarBaseHeaderHeight * s + SidePanelHeaderGapRef * s);
            return extra;
        }

        private static bool ShouldShowSidePanelHeader(ContentType? content)
        {
            if (!content.HasValue) return false;
            switch (content.Value)
            {
                case ContentType.Category:
                case ContentType.Creator:
                case ContentType.UserTags:
                case ContentType.Path:
                case ContentType.History:
                case ContentType.Settings:
                case ContentType.SavePresets:
                case ContentType.RemoveClothing:
                case ContentType.RemoveHair:
                case ContentType.RemoveAtom:
                case ContentType.CleanupCategories:
                    return true;
                default:
                    return false;
            }
        }

        private string ResolveSidePanelHeaderTitle(ContentType content)
        {
            switch (content)
            {
                case ContentType.Category: return VPBTranslation.T("gallery.side.category", "Category");
                case ContentType.Creator: return VPBTranslation.T("gallery.side.creator", "Creator");
                case ContentType.UserTags: return VPBTranslation.T("gallery.side.tags", "User Tags");
                case ContentType.Path: return VPBTranslation.T("gallery.side.path", "Path");
                case ContentType.History: return VPBTranslation.T("gallery.history.title", "History");
                case ContentType.Settings: return VPBTranslation.T("settings.title", "Settings");
                case ContentType.SavePresets: return SidePanelHeaderTranslation("gallery.side.save", "Save");
                case ContentType.RemoveClothing: return SidePanelHeaderTranslation("gallery.side.remove_clothing", "Remove Clothing");
                case ContentType.RemoveHair: return SidePanelHeaderTranslation("gallery.side.remove_hair", "Remove Hair");
                case ContentType.RemoveAtom: return SidePanelHeaderTranslation("gallery.side.remove_atom", "Remove Atom");
                case ContentType.CleanupCategories: return ResolveCleanupSidePanelHeaderTitle();
                default: return "";
            }
        }

        private static Color ResolveSidePanelHeaderColor(ContentType content)
        {
            switch (content)
            {
                case ContentType.Creator:         return ColorCreator;
                case ContentType.UserTags:        return ColorUserTagFilter;
                case ContentType.Path:            return ColorSourceFilter;
                case ContentType.History:         return ColorSubfilterFilter;
                case ContentType.Settings:        return ColorSubfilterFilter;
                case ContentType.SavePresets:     return ColorSourceFilter;
                case ContentType.RemoveClothing:  return ColorSubfilterFilter;
                case ContentType.RemoveHair:      return ColorSubfilterFilter;
                case ContentType.RemoveAtom:      return ColorSubfilterFilter;
                case ContentType.CleanupCategories: return ColorSubfilterFilter;
                default:                          return ColorCategory;
            }
        }

        private static string FormatSidePanelHeaderLabel(bool isLeft, string title)
        {
            if (string.IsNullOrEmpty(title))
                return "\u00d7";
            return isLeft ? ("\u00d7 " + title) : (title + " \u00d7");
        }

        private void EnsureSidePanelHeaderChrome(bool isLeft)
        {
            if (backgroundBoxGO == null) return;
            GameObject existing = isLeft ? _leftSidePanelHeaderGO : _rightSidePanelHeaderGO;
            if (existing != null)
            {
                if (existing.GetComponent<Button>() != null) return;
                try { Object.Destroy(existing); } catch { }
                if (isLeft) { _leftSidePanelHeaderGO = null; _leftSidePanelHeaderTitle = null; }
                else { _rightSidePanelHeaderGO = null; _rightSidePanelHeaderTitle = null; }
            }

            float s = ChromeScale;
            float headerH = SidePanelHeaderHeightRef * s;
            float colW = SidePanelHeaderColumnWidthRef * s;
            int font = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            float sideInsetX = isLeft ? SideTabColumnLeftInsetX(s) : SideTabColumnRightInsetX(s);

            GameObject headerGo = UI.CreateUIButton(
                backgroundBoxGO,
                colW,
                headerH,
                FormatSidePanelHeaderLabel(isLeft, "…"),
                font,
                sideInsetX,
                SidePanelHeaderAnchorY(s),
                isLeft ? AnchorPresets.topLeft : AnchorPresets.topRight,
                () => OnSidePanelHeaderClicked(isLeft));
            headerGo.name = isLeft ? "VPB_LeftSideHeader" : "VPB_RightSideHeader";

            Image bg = headerGo.GetComponent<Image>();
            if (bg != null) bg.color = ColorCategory;
            Text titleTxt = headerGo.GetComponentInChildren<Text>();
            if (titleTxt != null)
            {
                titleTxt.fontStyle = FontStyle.Normal;
                titleTxt.color = Color.white;
                titleTxt.alignment = TextAnchor.MiddleCenter;
                titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                titleTxt.verticalOverflow = VerticalWrapMode.Truncate;
            }
            AddTooltip(headerGo, "gallery.side.collapse_tip", "Close this panel");
            headerGo.SetActive(false);

            if (isLeft)
            {
                _leftSidePanelHeaderGO = headerGo;
                _leftSidePanelHeaderTitle = titleTxt;
            }
            else
            {
                _rightSidePanelHeaderGO = headerGo;
                _rightSidePanelHeaderTitle = titleTxt;
            }
        }

        private void OnSidePanelHeaderClicked(bool isLeft)
        {
            ContentType? ct = isLeft ? leftActiveContent : rightActiveContent;
            if (!ct.HasValue) return;
            switch (ct.Value)
            {
                case ContentType.SavePresets:
                    ToggleSaveSubmenuFromSideButtons(isLeft);
                    return;
                case ContentType.RemoveClothing:
                    ToggleClothingSubmenuFromSideButtons(null, isLeft);
                    return;
                case ContentType.RemoveHair:
                    ToggleHairSubmenuFromSideButtons(null, isLeft);
                    return;
                case ContentType.RemoveAtom:
                    ToggleAtomSubmenuFromSideButtons(isLeft);
                    return;
                case ContentType.CleanupCategories:
                    ExitCleanupModeForSidePanelNavigation();
                    return;
                default:
                    if (isLeft) ToggleLeft(ct.Value);
                    else ToggleRight(ct.Value);
                    return;
            }
        }

        private void SyncSidePanelHeaderChrome(float paneScale)
        {
            try
            {
                if (backgroundBoxGO != null)
                {
                    Transform stale = backgroundBoxGO.transform.Find("VPB_SidePanelCollapseBtn");
                    if (stale != null) Object.Destroy(stale.gameObject);
                }
            }
            catch { }

            float s = paneScale <= 0f ? 1f : paneScale;
            float headerH = SidePanelHeaderHeightRef * s;
            SyncSidePanelHeaderSide(true, s, headerH);
            SyncSidePanelHeaderSide(false, s, headerH);
            try { SyncSideTabColumnHorizontalInsets(paneScale); } catch { }
        }

        private void SyncSidePanelHeaderSide(bool isLeft, float s, float headerH)
        {
            EnsureSidePanelHeaderChrome(isLeft);
            GameObject headerGo = isLeft ? _leftSidePanelHeaderGO : _rightSidePanelHeaderGO;
            Text titleTxt = isLeft ? _leftSidePanelHeaderTitle : _rightSidePanelHeaderTitle;
            ContentType? ct = isLeft ? leftActiveContent : rightActiveContent;
            bool show = SidePanelHeaderVisibleForSide(isLeft);
            if (headerGo != null) headerGo.SetActive(show);
            if (titleTxt != null)
                GalleryUiMetrics.ApplyFont(titleTxt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            if (show && ct.HasValue)
            {
                if (titleTxt != null)
                    titleTxt.text = FormatSidePanelHeaderLabel(isLeft, ResolveSidePanelHeaderTitle(ct.Value));
                if (headerGo != null)
                {
                    Image hdrBg = headerGo.GetComponent<Image>();
                    if (hdrBg != null) hdrBg.color = ResolveSidePanelHeaderColor(ct.Value);
                }
            }

            if (headerGo != null)
            {
                RectTransform rt = headerGo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(SidePanelHeaderColumnWidthRef * s, headerH);
                    float sideInsetX = isLeft ? SideTabColumnLeftInsetX(s) : SideTabColumnRightInsetX(s);
                    rt.anchoredPosition = new Vector2(sideInsetX, SidePanelHeaderAnchorY(s));
                }
            }
        }
    }
}
