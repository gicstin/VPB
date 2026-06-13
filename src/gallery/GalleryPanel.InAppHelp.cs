using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int InAppHelpNavColumns = 2;
        private static readonly Color InAppHelpIconLinkColor = new Color(0.53f, 0.80f, 1f, 1f);
        private static readonly Color InAppHelpIconLinkBgColor = new Color(0.20f, 0.35f, 0.50f, 0.22f);

        private GameObject _inAppHelpPanelGO;
        private RectTransform _inAppHelpPanelRT;
        private RectTransform _inAppHelpHeaderRT;
        private RectTransform _inAppHelpCloseBtnRT;
        private RectTransform _inAppHelpNavRT;
        private GridLayoutGroup _inAppHelpNavGrid;
        private RectTransform _inAppHelpBodyScrollRT;
        private Text _inAppHelpHeaderTitleText;
        private InputField _inAppHelpSearchInput;
        private Text _inAppHelpSectionTitleText;
        private Transform _inAppHelpBodyHost;
        private ScrollRect _inAppHelpScrollRect;
        private GameObject _inAppHelpIconPreviewGO;
        private RectTransform _inAppHelpIconPreviewRT;
        private Image _inAppHelpIconPreviewBackdrop;
        private Image _inAppHelpIconPreviewGlyph;
        private Text _inAppHelpIconPreviewLabel;
        private InAppHelpIconPreviewFollower _inAppHelpIconPreviewFollower;
        private RectTransform _inAppHelpIconPreviewParentRT;
        private readonly List<Text> _inAppHelpBodyTexts = new List<Text>(48);
        private readonly Dictionary<string, GameObject> _inAppHelpNavBtns = new Dictionary<string, GameObject>(12);
        private readonly List<VPBHelpContent.HelpSection> _inAppHelpSections = new List<VPBHelpContent.HelpSection>(12);
        private string _inAppHelpActiveSectionId = "filtering";
        private bool _inAppHelpOpen;
        private bool _inAppHelpScaleHooked;

        private static readonly string[] InAppHelpAnchorIds =
        {
            "filtering", "tags", "import", "cleanup", "selection", "layout",
            "save-and-apply", "hotkeys", "advanced"
        };

        private static readonly string[] InAppHelpAnchorDefaults =
        {
            "Filtering", "Tags", "Import", "Cleanup", "Selection", "Layout",
            "Save and Apply", "Hotkeys", "Advanced"
        };

        private void ApplyInAppHelpTextScale(Text txt, int baseFont)
        {
            if (txt == null) return;
            GalleryUiMetrics.ApplyFont(txt, baseFont, InAppHelpChromeScale, GalleryUiDesignTokens.FontMinRef);
            txt.fontStyle = FontStyle.Normal;
        }

        private void EnsureInAppHelpChrome(GameObject titleBarGO)
        {
            if (titleBarGO == null) return;
            if (_titleBarHelpBtnRT != null)
            {
                EnsureInAppHelpBodyReady();
                EnsureInAppHelpNavComplete();
                return;
            }

            GameObject helpBtn = UI.CreateUIButton(titleBarGO, 40, 40, "?", 22, 0, 0, AnchorPresets.middleCenter, ToggleInAppHelpPanel);
            helpBtn.name = "TitleBarHelpBtn";
            helpBtn.GetComponent<Image>().color = new Color(0.12f, 0.28f, 0.42f, 0.92f);
            Text ht = helpBtn.GetComponentInChildren<Text>();
            if (ht != null) ht.color = Color.white;
            { var s = UI.LoadIconSprite("vpb_icons/book.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(helpBtn, s, 6f, new Color(0.12f, 0.28f, 0.42f, 0.92f)); }
            _titleBarHelpBtnRT = helpBtn.GetComponent<RectTransform>();
            _titleBarHelpBtnRT.anchorMin = new Vector2(0.5f, 0.5f);
            _titleBarHelpBtnRT.anchorMax = new Vector2(0.5f, 0.5f);
            _titleBarHelpBtnRT.pivot = new Vector2(0.5f, 0.5f);
            AddTooltip(helpBtn, "gallery.help.tooltip", "Open gallery help (searchable topics)");

            if (backgroundBoxGO == null) return;

            _inAppHelpPanelGO = new GameObject("InAppHelpPanel");
            _inAppHelpPanelGO.transform.SetParent(backgroundBoxGO.transform, false);
            _inAppHelpPanelRT = _inAppHelpPanelGO.AddComponent<RectTransform>();
            _inAppHelpPanelRT.anchorMin = new Vector2(1f, 0f);
            _inAppHelpPanelRT.anchorMax = new Vector2(1f, 1f);
            _inAppHelpPanelRT.pivot = new Vector2(1f, 0.5f);

            Image panelBg = _inAppHelpPanelGO.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.10f, 0.14f, 0.98f);
            panelBg.raycastTarget = true;

            GameObject accent = new GameObject("Accent");
            accent.transform.SetParent(_inAppHelpPanelGO.transform, false);
            RectTransform accentRT = accent.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 0f);
            accentRT.anchorMax = new Vector2(0f, 1f);
            accentRT.pivot = new Vector2(0f, 0.5f);
            accentRT.sizeDelta = new Vector2(3f, 0f);
            accentRT.anchoredPosition = Vector2.zero;
            Image accentImg = accent.AddComponent<Image>();
            accentImg.color = new Color(0.14f, 0.40f, 0.62f, 1f);
            accentImg.raycastTarget = false;

            GameObject header = new GameObject("Header");
            header.transform.SetParent(_inAppHelpPanelGO.transform, false);
            _inAppHelpHeaderRT = header.AddComponent<RectTransform>();
            _inAppHelpHeaderRT.anchorMin = new Vector2(0f, 1f);
            _inAppHelpHeaderRT.anchorMax = new Vector2(1f, 1f);
            _inAppHelpHeaderRT.pivot = new Vector2(0.5f, 1f);
            _inAppHelpHeaderRT.sizeDelta = new Vector2(0f, GalleryUiDesignTokens.InAppHelpHeaderHeightRef);
            _inAppHelpHeaderRT.anchoredPosition = Vector2.zero;
            Image headerBg = header.AddComponent<Image>();
            headerBg.color = new Color(0.10f, 0.26f, 0.44f, 1f);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(header.transform, false);
            RectTransform titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = Vector2.zero;
            titleRT.anchorMax = Vector2.one;
            titleRT.offsetMin = new Vector2(12f, 0f);
            titleRT.offsetMax = new Vector2(-44f, 0f);
            _inAppHelpHeaderTitleText = titleGO.AddComponent<Text>();
            _inAppHelpHeaderTitleText.text = VPBTranslation.T("gallery.help.title", "Help");
            _inAppHelpHeaderTitleText.alignment = TextAnchor.MiddleLeft;
            _inAppHelpHeaderTitleText.color = Color.white;
            try { VPBUiFont.ApplyTo(_inAppHelpHeaderTitleText); } catch { }

            GameObject closeBtn = UI.CreateUIButton(header, 36, 36, "\u00d7", 22, 0, 0, AnchorPresets.vStretchRight,
                () => SetInAppHelpOpen(false));
            _inAppHelpCloseBtnRT = closeBtn.GetComponent<RectTransform>();
            if (_inAppHelpCloseBtnRT != null)
            {
                _inAppHelpCloseBtnRT.offsetMin = new Vector2(-40f, 4f);
                _inAppHelpCloseBtnRT.offsetMax = new Vector2(-6f, -4f);
            }
            { var s = UI.LoadIconSprite("vpb_icons/x.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(closeBtn, s, 6f, new Color(0.10f, 0.26f, 0.44f, 1f)); }

            _inAppHelpSearchInput = CreateSearchInput(_inAppHelpPanelGO, GalleryUiDesignTokens.InAppHelpPanelWidthRef - 24f, _ => OnInAppHelpSearchChanged());
            RectTransform searchRT = _inAppHelpSearchInput.GetComponent<RectTransform>();
            searchRT.anchorMin = new Vector2(0f, 1f);
            searchRT.anchorMax = new Vector2(1f, 1f);
            searchRT.pivot = new Vector2(0.5f, 1f);
            searchRT.anchoredPosition = new Vector2(0f, -GalleryUiDesignTokens.InAppHelpHeaderHeightRef - 4f);
            searchRT.sizeDelta = new Vector2(-16f, GalleryUiDesignTokens.InAppHelpSearchHeightRef);
            try
            {
                Text ph = _inAppHelpSearchInput.placeholder as Text;
                if (ph != null) ph.text = VPBTranslation.T("gallery.help.search_placeholder", "Search help…");
                Text st = _inAppHelpSearchInput.textComponent;
                if (st != null) try { VPBUiFont.ApplyTo(st); } catch { }
            }
            catch { }

            GameObject navCol = new GameObject("NavColumn");
            navCol.transform.SetParent(_inAppHelpPanelGO.transform, false);
            _inAppHelpNavRT = navCol.AddComponent<RectTransform>();
            _inAppHelpNavRT.anchorMin = new Vector2(0f, 1f);
            _inAppHelpNavRT.anchorMax = new Vector2(1f, 1f);
            _inAppHelpNavRT.pivot = new Vector2(0.5f, 1f);
            _inAppHelpNavGrid = navCol.AddComponent<GridLayoutGroup>();
            _inAppHelpNavGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _inAppHelpNavGrid.constraintCount = InAppHelpNavColumns;
            _inAppHelpNavGrid.childAlignment = TextAnchor.UpperCenter;
            _inAppHelpNavGrid.padding = new RectOffset(10, 10, 4, 6);
            _inAppHelpNavGrid.spacing = new Vector2(6f, 5f);
            _inAppHelpNavGrid.cellSize = new Vector2(210f, GalleryUiDesignTokens.InAppHelpNavBtnHeightRef);

            _inAppHelpNavBtns.Clear();
            for (int i = 0; i < InAppHelpAnchorIds.Length; i++)
            {
                string id = InAppHelpAnchorIds[i];
                string label = VPBTranslation.T("gallery.help.section." + id, InAppHelpAnchorDefaults[i]);
                string captured = id;
                GameObject row = UI.CreateUIButton(navCol, GalleryUiDesignTokens.InAppHelpNavBtnHeightRef, GalleryUiDesignTokens.InAppHelpNavBtnHeightRef, label,
                    GalleryUiDesignTokens.InAppHelpNavFontRef, 0f, 0f, AnchorPresets.stretchAll, () => SelectInAppHelpSection(captured));
                row.name = "Nav_" + id;
                Text rowTxt = row.GetComponentInChildren<Text>(true);
                if (rowTxt != null)
                {
                    rowTxt.alignment = TextAnchor.MiddleCenter;
                    rowTxt.color = Color.white;
                    try { VPBUiFont.ApplyTo(rowTxt); } catch { }
                }
                _inAppHelpNavBtns[id] = row;
            }

            GameObject bodyCard = new GameObject("BodyCard");
            bodyCard.transform.SetParent(_inAppHelpPanelGO.transform, false);
            RectTransform bodyCardRT = bodyCard.AddComponent<RectTransform>();
            bodyCardRT.anchorMin = Vector2.zero;
            bodyCardRT.anchorMax = Vector2.one;
            Image bodyCardBg = bodyCard.AddComponent<Image>();
            bodyCardBg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
            bodyCardBg.raycastTarget = false;

            GameObject scroll = UI.CreateVScrollableContent(
                bodyCard, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: 14f, spacing: 0f, addBottomFlexSpacer: false);
            _inAppHelpBodyScrollRT = scroll.GetComponent<RectTransform>();
            _inAppHelpScrollRect = scroll.GetComponent<ScrollRect>();
            AlignInAppHelpScrollViewport();

            Transform content = _inAppHelpScrollRect.content;
            VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.padding = new RectOffset(14, 14, 12, 16);
                contentVlg.spacing = 6f;
            }

            GameObject sectionTitleGO = new GameObject("SectionTitle");
            sectionTitleGO.transform.SetParent(content, false);
            _inAppHelpSectionTitleText = sectionTitleGO.AddComponent<Text>();
            _inAppHelpSectionTitleText.fontStyle = FontStyle.Normal;
            _inAppHelpSectionTitleText.color = new Color(0.62f, 0.86f, 1f, 1f);
            _inAppHelpSectionTitleText.alignment = TextAnchor.UpperLeft;
            _inAppHelpSectionTitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            try { VPBUiFont.ApplyTo(_inAppHelpSectionTitleText); } catch { }
            LayoutElement sectionTitleLe = sectionTitleGO.AddComponent<LayoutElement>();
            sectionTitleLe.flexibleWidth = 1f;

            GameObject bodyHostGO = new GameObject("SectionBodyHost");
            bodyHostGO.transform.SetParent(content, false);
            _inAppHelpBodyHost = bodyHostGO.transform;
            VerticalLayoutGroup bodyHostVlg = bodyHostGO.AddComponent<VerticalLayoutGroup>();
            bodyHostVlg.spacing = GalleryUiDesignTokens.InAppHelpBodyLineSpacingRef;
            bodyHostVlg.childControlWidth = true;
            bodyHostVlg.childForceExpandWidth = true;
            bodyHostVlg.childControlHeight = true;
            bodyHostVlg.childForceExpandHeight = false;
            ContentSizeFitter bodyHostCsf = bodyHostGO.AddComponent<ContentSizeFitter>();
            bodyHostCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement bodyHostLe = bodyHostGO.AddComponent<LayoutElement>();
            bodyHostLe.flexibleWidth = 1f;

            EnsureInAppHelpIconPreviewChrome();

            if (!_inAppHelpScaleHooked)
            {
                _inAppHelpScaleHooked = true;
                innerPaneScaleActions.Add(s =>
                {
                    try { ApplyInAppHelpPanelLayout(s); } catch { }
                    try { ApplyInAppHelpTypography(); } catch { }
                });
            }

            _inAppHelpPanelGO.SetActive(false);
            ApplyInAppHelpPanelLayout(InAppHelpChromeScale);
            ReloadInAppHelpContent();
        }

        private void EnsureInAppHelpIconPreviewChrome()
        {
            if (_inAppHelpPanelGO == null) return;

            Transform bodyCard = _inAppHelpPanelGO.transform.Find("BodyCard");
            Transform parent = bodyCard != null ? bodyCard : _inAppHelpPanelGO.transform;
            _inAppHelpIconPreviewParentRT = parent as RectTransform;

            if (_inAppHelpIconPreviewGO == null)
            {
                _inAppHelpIconPreviewGO = new GameObject("IconPreviewDock");
                _inAppHelpIconPreviewGO.transform.SetParent(parent, false);
                _inAppHelpIconPreviewRT = _inAppHelpIconPreviewGO.AddComponent<RectTransform>();

                Image popupBg = _inAppHelpIconPreviewGO.AddComponent<Image>();
                popupBg.color = new Color(0.06f, 0.08f, 0.11f, 0.94f);
                popupBg.raycastTarget = false;

                GameObject backdropGO = new GameObject("Backdrop");
                backdropGO.transform.SetParent(_inAppHelpIconPreviewGO.transform, false);
                RectTransform backdropRT = backdropGO.AddComponent<RectTransform>();
                backdropRT.anchorMin = new Vector2(0.5f, 0.5f);
                backdropRT.anchorMax = new Vector2(0.5f, 0.5f);
                backdropRT.pivot = new Vector2(0.5f, 0.5f);
                backdropRT.anchoredPosition = Vector2.zero;
                _inAppHelpIconPreviewBackdrop = backdropGO.AddComponent<Image>();
                _inAppHelpIconPreviewBackdrop.raycastTarget = false;

                GameObject glyphGO = new GameObject("Glyph");
                glyphGO.transform.SetParent(backdropGO.transform, false);
                RectTransform glyphRT = glyphGO.AddComponent<RectTransform>();
                glyphRT.anchorMin = Vector2.zero;
                glyphRT.anchorMax = Vector2.one;
                glyphRT.offsetMin = new Vector2(7f, 7f);
                glyphRT.offsetMax = new Vector2(-7f, -7f);
                _inAppHelpIconPreviewGlyph = glyphGO.AddComponent<Image>();
                _inAppHelpIconPreviewGlyph.preserveAspect = true;
                _inAppHelpIconPreviewGlyph.raycastTarget = false;

                GameObject labelGO = new GameObject("Label");
                labelGO.transform.SetParent(_inAppHelpIconPreviewGO.transform, false);
                labelGO.SetActive(false);
                _inAppHelpIconPreviewLabel = labelGO.AddComponent<Text>();

                _inAppHelpIconPreviewFollower = _inAppHelpIconPreviewGO.AddComponent<InAppHelpIconPreviewFollower>();
                _inAppHelpIconPreviewFollower.ParentRect = _inAppHelpIconPreviewParentRT;

                _inAppHelpIconPreviewGO.SetActive(false);
            }
            else if (_inAppHelpIconPreviewGO.transform.parent != parent)
            {
                _inAppHelpIconPreviewGO.transform.SetParent(parent, false);
                _inAppHelpIconPreviewParentRT = parent as RectTransform;
                if (_inAppHelpIconPreviewFollower != null)
                    _inAppHelpIconPreviewFollower.ParentRect = _inAppHelpIconPreviewParentRT;
            }
            else
            {
                _inAppHelpIconPreviewParentRT = parent as RectTransform;
                if (_inAppHelpIconPreviewFollower == null)
                    _inAppHelpIconPreviewFollower = _inAppHelpIconPreviewGO.GetComponent<InAppHelpIconPreviewFollower>();
                if (_inAppHelpIconPreviewFollower == null)
                    _inAppHelpIconPreviewFollower = _inAppHelpIconPreviewGO.AddComponent<InAppHelpIconPreviewFollower>();
                if (_inAppHelpIconPreviewFollower != null)
                    _inAppHelpIconPreviewFollower.ParentRect = _inAppHelpIconPreviewParentRT;
            }

            ApplyInAppHelpIconPreviewLayout(InAppHelpChromeScale);
        }

        private void ApplyInAppHelpIconPreviewLayout(float s)
        {
            if (_inAppHelpIconPreviewRT == null) return;
            if (s <= 0f) s = 1f;

            float dockSz = GalleryUiDesignTokens.InAppHelpIconPreviewDockSizeRef * s;
            _inAppHelpIconPreviewRT.sizeDelta = new Vector2(dockSz, dockSz);

            if (_inAppHelpIconPreviewFollower != null)
                _inAppHelpIconPreviewFollower.LiftPx = 10f * s;

            Transform backdropT = _inAppHelpIconPreviewBackdrop != null
                ? _inAppHelpIconPreviewBackdrop.transform : null;
            if (backdropT != null)
            {
                RectTransform backdropRT = backdropT as RectTransform;
                if (backdropRT != null)
                {
                    float glyphSz = GalleryUiDesignTokens.InAppHelpIconPreviewGlyphSizeRef * s;
                    backdropRT.sizeDelta = new Vector2(glyphSz, glyphSz);
                }
            }
        }

        private void HideInAppHelpIconPreview()
        {
            if (_inAppHelpIconPreviewFollower != null)
                _inAppHelpIconPreviewFollower.SetFollowActive(false);
            if (_inAppHelpIconPreviewGO != null)
                _inAppHelpIconPreviewGO.SetActive(false);
        }

        private void ShowInAppHelpIconPreview(string iconId, string label)
        {
            if (_inAppHelpIconPreviewGO == null || _inAppHelpIconPreviewRT == null) return;
            VPBHelpContent.HelpIconSpec spec;
            if (!VPBHelpContent.TryResolveHelpIcon(iconId, out spec))
            {
                HideInAppHelpIconPreview();
                return;
            }

            Sprite spr = null;
            try { spr = UI.LoadIconSprite(spec.Path, UI.SideRailIconGlyphTint); } catch { }
            if (_inAppHelpIconPreviewBackdrop != null)
                _inAppHelpIconPreviewBackdrop.color = spec.Backdrop;
            if (_inAppHelpIconPreviewGlyph != null)
            {
                _inAppHelpIconPreviewGlyph.sprite = spr;
                _inAppHelpIconPreviewGlyph.enabled = spr != null;
            }
            if (_inAppHelpIconPreviewLabel != null)
                _inAppHelpIconPreviewLabel.text = "";

            float s = InAppHelpChromeScale;
            ApplyInAppHelpIconPreviewLayout(s);
            _inAppHelpIconPreviewGO.transform.SetAsLastSibling();
            _inAppHelpIconPreviewGO.SetActive(true);
            if (_inAppHelpIconPreviewFollower != null)
            {
                _inAppHelpIconPreviewFollower.ParentRect = _inAppHelpIconPreviewParentRT;
                _inAppHelpIconPreviewFollower.SetFollowActive(true);
            }
        }

        private void EnsureInAppHelpBodyReady()
        {
            if (_inAppHelpPanelGO == null)
            {
                try
                {
                    if (backgroundBoxGO != null)
                    {
                        Transform panelT = backgroundBoxGO.transform.Find("InAppHelpPanel");
                        if (panelT != null) _inAppHelpPanelGO = panelT.gameObject;
                    }
                }
                catch { }
            }
            if (_inAppHelpPanelGO == null) return;

            if (_inAppHelpScrollRect == null)
            {
                Transform bodyCard = _inAppHelpPanelGO.transform.Find("BodyCard");
                if (bodyCard != null)
                {
                    ScrollRect sr = bodyCard.GetComponentInChildren<ScrollRect>(true);
                    if (sr != null) _inAppHelpScrollRect = sr;
                }
            }

            Transform content = _inAppHelpScrollRect != null ? _inAppHelpScrollRect.content : null;
            if (content == null) return;

            if (_inAppHelpSectionTitleText == null)
            {
                Transform titleT = content.Find("SectionTitle");
                if (titleT != null) _inAppHelpSectionTitleText = titleT.GetComponent<Text>();
            }

            if (_inAppHelpBodyHost != null) return;

            Transform legacyBody = content.Find("SectionBody");
            if (legacyBody != null)
            {
                try { Destroy(legacyBody.gameObject); } catch { }
            }

            Transform existingHost = content.Find("SectionBodyHost");
            if (existingHost != null)
            {
                _inAppHelpBodyHost = existingHost;
                return;
            }

            GameObject bodyHostGO = new GameObject("SectionBodyHost");
            bodyHostGO.transform.SetParent(content, false);
            _inAppHelpBodyHost = bodyHostGO.transform;
            VerticalLayoutGroup bodyHostVlg = bodyHostGO.AddComponent<VerticalLayoutGroup>();
            bodyHostVlg.spacing = GalleryUiDesignTokens.InAppHelpBodyLineSpacingRef;
            bodyHostVlg.childControlWidth = true;
            bodyHostVlg.childForceExpandWidth = true;
            bodyHostVlg.childControlHeight = true;
            bodyHostVlg.childForceExpandHeight = false;
            ContentSizeFitter bodyHostCsf = bodyHostGO.AddComponent<ContentSizeFitter>();
            bodyHostCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement bodyHostLe = bodyHostGO.AddComponent<LayoutElement>();
            bodyHostLe.flexibleWidth = 1f;

            EnsureInAppHelpIconPreviewChrome();
        }

        private void EnsureInAppHelpNavComplete()
        {
            if (_inAppHelpNavRT == null) return;
            for (int i = 0; i < InAppHelpAnchorIds.Length; i++)
            {
                string id = InAppHelpAnchorIds[i];
                if (_inAppHelpNavBtns.ContainsKey(id) && _inAppHelpNavBtns[id] != null) continue;
                string label = VPBTranslation.T("gallery.help.section." + id, InAppHelpAnchorDefaults[i]);
                string captured = id;
                GameObject row = UI.CreateUIButton(_inAppHelpNavRT.gameObject, GalleryUiDesignTokens.InAppHelpNavBtnHeightRef, GalleryUiDesignTokens.InAppHelpNavBtnHeightRef, label,
                    GalleryUiDesignTokens.InAppHelpNavFontRef, 0f, 0f, AnchorPresets.stretchAll, () => SelectInAppHelpSection(captured));
                row.name = "Nav_" + id;
                Text rowTxt = row.GetComponentInChildren<Text>(true);
                if (rowTxt != null)
                {
                    rowTxt.alignment = TextAnchor.MiddleCenter;
                    rowTxt.color = Color.white;
                    try { VPBUiFont.ApplyTo(rowTxt); } catch { }
                }
                _inAppHelpNavBtns[id] = row;
            }
        }

        private void ClearInAppHelpBodyHost()
        {
            _inAppHelpBodyTexts.Clear();
            HideInAppHelpIconPreview();
            if (_inAppHelpBodyHost == null) return;
            for (int i = _inAppHelpBodyHost.childCount - 1; i >= 0; i--)
            {
                try
                {
                    GameObject child = _inAppHelpBodyHost.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }
                catch { }
            }
        }

        private void CreateInAppHelpLine(VPBHelpContent.HelpContentLine line)
        {
            if (_inAppHelpBodyHost == null || line == null) return;

            if (VPBHelpContent.LineHasIconRef(line))
            {
                CreateInAppHelpIconLineBlock(line);
                return;
            }

            string lineRich = VPBHelpContent.JoinTextSegments(line.Segments);
            if (string.IsNullOrEmpty(lineRich)) return;

            CreateInAppHelpBodyText(_inAppHelpBodyHost, lineRich, line.IsSubheading, line.IsBullet,
                line.IsNumbered ? line.NumberPrefix + "." : null);
        }

        private void CreateInAppHelpIconLineBlock(VPBHelpContent.HelpContentLine line)
        {
            if (_inAppHelpBodyHost == null || line == null || line.Segments == null) return;

            string rich = VPBHelpContent.BuildLinePlainText(line);
            if (string.IsNullOrEmpty(rich)) return;

            GameObject blockGO = new GameObject("IconLineBlock");
            blockGO.transform.SetParent(_inAppHelpBodyHost, false);
            VerticalLayoutGroup blockVlg = blockGO.AddComponent<VerticalLayoutGroup>();
            blockVlg.spacing = 4f;
            blockVlg.padding = new RectOffset(0, 0, 0, 0);
            blockVlg.childAlignment = TextAnchor.UpperLeft;
            blockVlg.childControlWidth = true;
            blockVlg.childForceExpandWidth = true;
            blockVlg.childControlHeight = true;
            blockVlg.childForceExpandHeight = false;
            ContentSizeFitter blockCsf = blockGO.AddComponent<ContentSizeFitter>();
            blockCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement blockLe = blockGO.AddComponent<LayoutElement>();
            blockLe.flexibleWidth = 1f;

            Text bodyTxt = CreateInAppHelpBodyText(blockGO.transform, rich, line.IsSubheading);
            bodyTxt.raycastTarget = false;

            GameObject linkRowGO = new GameObject("LinkRow");
            linkRowGO.transform.SetParent(blockGO.transform, false);
            HorizontalLayoutGroup linkHlg = linkRowGO.AddComponent<HorizontalLayoutGroup>();
            linkHlg.spacing = 6f;
            linkHlg.padding = new RectOffset(14, 0, 0, 0);
            linkHlg.childAlignment = TextAnchor.MiddleLeft;
            linkHlg.childControlWidth = false;
            linkHlg.childForceExpandWidth = false;
            linkHlg.childControlHeight = true;
            linkHlg.childForceExpandHeight = false;
            ContentSizeFitter linkCsf = linkRowGO.AddComponent<ContentSizeFitter>();
            linkCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement linkRowLe = linkRowGO.AddComponent<LayoutElement>();
            linkRowLe.flexibleWidth = 1f;

            for (int i = 0; i < line.Segments.Count; i++)
            {
                VPBHelpContent.HelpInlineSegment seg = line.Segments[i];
                if (seg != null && seg.Kind == VPBHelpContent.HelpInlineKind.IconRef)
                    CreateInAppHelpIconLinkButton(linkRowGO.transform, seg);
            }
        }

        private void CreateInAppHelpIconLinkButton(Transform row, VPBHelpContent.HelpInlineSegment seg)
        {
            if (seg == null || string.IsNullOrEmpty(seg.IconId)) return;
            string label = string.IsNullOrEmpty(seg.IconLabel) ? seg.IconId : seg.IconLabel;
            string capturedId = seg.IconId;

            GameObject btnGO = new GameObject("Link_" + capturedId);
            btnGO.transform.SetParent(row, false);

            Image bg = btnGO.AddComponent<Image>();
            bg.color = InAppHelpIconLinkBgColor;
            bg.raycastTarget = true;

            Button btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.fadeDuration = 0.05f;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(4f, 1f);
            labelRT.offsetMax = new Vector2(-4f, -1f);

            Text txt = labelGO.AddComponent<Text>();
            txt.text = label;
            txt.color = InAppHelpIconLinkColor;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            try { VPBUiFont.ApplyTo(txt); } catch { }
            _inAppHelpBodyTexts.Add(txt);

            LayoutElement le = btnGO.AddComponent<LayoutElement>();
            le.minHeight = 22f;

            var del = btnGO.AddComponent<UIHoverDelegate>();
            del.OnHoverChange = enter =>
            {
                if (enter)
                    ShowInAppHelpIconPreview(capturedId, label);
                else
                    HideInAppHelpIconPreview();
            };
        }

        private void FinalizeInAppHelpFlowLayout()
        {
            if (_inAppHelpBodyHost == null) return;
            try
            {
                for (int i = 0; i < _inAppHelpBodyHost.childCount; i++)
                {
                    Transform block = _inAppHelpBodyHost.GetChild(i);
                    if (block == null || block.name != "IconLineBlock") continue;

                    Transform linkRow = block.Find("LinkRow");
                    if (linkRow == null) continue;

                    for (int c = 0; c < linkRow.childCount; c++)
                    {
                        Transform child = linkRow.GetChild(c);
                        if (child == null || child.GetComponent<Button>() == null) continue;

                        LayoutElement le = child.GetComponent<LayoutElement>();
                        Text labelTxt = child.GetComponentInChildren<Text>(true);
                        if (le == null || labelTxt == null) continue;

                        le.preferredWidth = labelTxt.preferredWidth + 12f;
                        le.preferredHeight = Mathf.Max(labelTxt.fontSize + 6f, 22f);
                    }
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(_inAppHelpBodyHost as RectTransform);
            }
            catch { }
        }

        private void RenderInAppHelpBody(VPBHelpContent.HelpSection sec)
        {
            EnsureInAppHelpBodyReady();
            ClearInAppHelpBodyHost();
            if (_inAppHelpBodyHost == null) return;

            if (sec == null)
                return;

            if (sec.ContentLines == null || sec.ContentLines.Count == 0)
            {
                if (!string.IsNullOrEmpty(sec.RichTextBody))
                    CreateInAppHelpBodyText(_inAppHelpBodyHost, sec.RichTextBody);
                return;
            }

            for (int i = 0; i < sec.ContentLines.Count; i++)
            {
                VPBHelpContent.HelpContentLine line = sec.ContentLines[i];
                if (line == null) continue;
                if (line.IsBlank)
                {
                    GameObject spacer = new GameObject("Spacer");
                    spacer.transform.SetParent(_inAppHelpBodyHost, false);
                    LayoutElement le = spacer.AddComponent<LayoutElement>();
                    le.preferredHeight = 2f;
                    continue;
                }

                CreateInAppHelpLine(line);
            }

            if (_inAppHelpBodyHost.childCount == 0 && !string.IsNullOrEmpty(sec.RichTextBody))
                CreateInAppHelpBodyText(_inAppHelpBodyHost, sec.RichTextBody);

            try
            {
                if (_inAppHelpBodyHost != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_inAppHelpBodyHost as RectTransform);
            }
            catch { }

            ApplyInAppHelpTypography();
        }

        private Text CreateInAppHelpBodyText(Transform parent, string richText, bool subheading = false, bool bullet = false, string prefix = null)
        {
            GameObject go = new GameObject(subheading ? "Subheading" : (bullet ? "Bullet" : "Text"));
            go.transform.SetParent(parent, false);
            Text txt = go.AddComponent<Text>();
            txt.supportRichText = true;
            txt.color = subheading
                ? new Color(0.78f, 0.90f, 1f, 1f)
                : new Color(0.94f, 0.96f, 0.98f, 1f);
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.resizeTextForBestFit = false;
            string body = richText ?? "";
            if (bullet) body = (string.IsNullOrEmpty(prefix) ? "  \u2022 " : prefix + " ") + body;
            txt.text = body;
            try { VPBUiFont.ApplyTo(txt); } catch { }
            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            _inAppHelpBodyTexts.Add(txt);
            return txt;
        }

        private void AlignInAppHelpScrollViewport()
        {
            if (_inAppHelpBodyScrollRT == null) return;
            Transform vp = _inAppHelpBodyScrollRT.Find("Viewport");
            if (vp == null) return;
            RectTransform vprt = vp as RectTransform;
            if (vprt == null) return;
            vprt.sizeDelta = new Vector2(-14f, 0f);
            vprt.anchoredPosition = new Vector2(-7f, 0f);
        }

        private void OnInAppHelpSearchChanged()
        {
            RefreshInAppHelpNavVisibility();
            EnsureInAppHelpActiveSectionValid();
            RefreshInAppHelpActiveSection();
        }

        private void ToggleInAppHelpPanel()
        {
            SetInAppHelpOpen(!_inAppHelpOpen);
        }

        public void OpenInAppHelpToSection(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) sectionId = "filtering";
            _inAppHelpActiveSectionId = sectionId;
            SetInAppHelpOpen(true);
        }

        private void SetInAppHelpOpen(bool open)
        {
            _inAppHelpOpen = open;
            if (!open) HideInAppHelpIconPreview();
            if (_inAppHelpPanelGO != null)
            {
                _inAppHelpPanelGO.SetActive(open);
                if (open)
                {
                    try { _inAppHelpPanelGO.transform.SetAsLastSibling(); } catch { }
                    try { EnsureInAppHelpIconPreviewChrome(); } catch { }
                    try { ApplyInAppHelpPanelLayout(InAppHelpChromeScale); } catch { }
                    ReloadInAppHelpContent();
                }
            }
            if (_titleBarHelpBtnRT != null)
            {
                Image img = _titleBarHelpBtnRT.GetComponent<Image>();
                if (img != null)
                    img.color = open
                        ? new Color(0.2f, 0.45f, 0.75f, 0.95f)
                        : new Color(0.12f, 0.28f, 0.42f, 0.92f);
            }
        }

        public void ReloadInAppHelpContent()
        {
            EnsureInAppHelpBodyReady();
            EnsureInAppHelpNavComplete();
            _inAppHelpSections.Clear();
            try
            {
                string locale = VPBTranslation.CurrentLocale;
                _inAppHelpSections.AddRange(VPBHelpContent.LoadSections(locale));
            }
            catch { }
            if (_inAppHelpHeaderTitleText != null)
                _inAppHelpHeaderTitleText.text = VPBTranslation.T("gallery.help.title", "Help");
            for (int i = 0; i < InAppHelpAnchorIds.Length; i++)
            {
                string id = InAppHelpAnchorIds[i];
                GameObject navGo;
                if (!_inAppHelpNavBtns.TryGetValue(id, out navGo) || navGo == null) continue;
                Text navTxt = navGo.GetComponentInChildren<Text>(true);
                if (navTxt != null)
                    navTxt.text = VPBTranslation.T("gallery.help.section." + id, InAppHelpAnchorDefaults[i]);
            }
            EnsureInAppHelpActiveSectionValid();
            RefreshInAppHelpNavVisibility();
            RefreshInAppHelpActiveSection();
            ApplyInAppHelpTypography();
        }

        private void EnsureInAppHelpActiveSectionValid()
        {
            if (_inAppHelpSections.Count == 0) return;
            for (int i = 0; i < _inAppHelpSections.Count; i++)
            {
                if (_inAppHelpSections[i] != null && _inAppHelpSections[i].Id == _inAppHelpActiveSectionId)
                    return;
            }
            for (int i = 0; i < InAppHelpAnchorIds.Length; i++)
            {
                string id = InAppHelpAnchorIds[i];
                if (SectionMatchesHelpSearch(id) && SectionExists(id))
                {
                    _inAppHelpActiveSectionId = id;
                    return;
                }
            }
            _inAppHelpActiveSectionId = _inAppHelpSections[0].Id;
        }

        private bool SectionExists(string id)
        {
            for (int i = 0; i < _inAppHelpSections.Count; i++)
            {
                if (_inAppHelpSections[i] != null && _inAppHelpSections[i].Id == id)
                    return true;
            }
            return false;
        }

        private VPBHelpContent.HelpSection FindHelpSection(string id)
        {
            for (int i = 0; i < _inAppHelpSections.Count; i++)
            {
                if (_inAppHelpSections[i] != null && _inAppHelpSections[i].Id == id)
                    return _inAppHelpSections[i];
            }
            return null;
        }

        private string InAppHelpSearchQuery()
        {
            try { return (_inAppHelpSearchInput != null ? _inAppHelpSearchInput.text : null) ?? ""; }
            catch { return ""; }
        }

        private bool SectionMatchesHelpSearch(string sectionId)
        {
            string query = InAppHelpSearchQuery().Trim();
            if (string.IsNullOrEmpty(query)) return true;
            VPBHelpContent.HelpSection sec = FindHelpSection(sectionId);
            if (sec == null) return false;
            if (sec.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sec.RichTextBody.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void RefreshInAppHelpNavVisibility()
        {
            foreach (var kv in _inAppHelpNavBtns)
            {
                if (kv.Value == null) continue;
                bool show = SectionExists(kv.Key) && SectionMatchesHelpSearch(kv.Key);
                kv.Value.SetActive(show);
            }
            RefreshInAppHelpNavHighlight();
        }

        private void RefreshInAppHelpNavHighlight()
        {
            foreach (var kv in _inAppHelpNavBtns)
            {
                if (kv.Value == null) continue;
                Image img = kv.Value.GetComponent<Image>();
                if (img == null) continue;
                bool active = kv.Key == _inAppHelpActiveSectionId && kv.Value.activeSelf;
                img.color = active
                    ? new Color(0.14f, 0.40f, 0.62f, 1f)
                    : new Color(0.20f, 0.24f, 0.30f, 1f);
            }
        }

        private void SelectInAppHelpSection(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId) || !SectionExists(sectionId)) return;
            _inAppHelpActiveSectionId = sectionId;
            RefreshInAppHelpActiveSection();
        }

        private void RefreshInAppHelpActiveSection()
        {
            VPBHelpContent.HelpSection sec = FindHelpSection(_inAppHelpActiveSectionId);
            if (_inAppHelpSectionTitleText != null)
                _inAppHelpSectionTitleText.text = sec != null ? sec.Title : "";
            RenderInAppHelpBody(sec);
            RefreshInAppHelpNavHighlight();
            if (_inAppHelpScrollRect != null)
                _inAppHelpScrollRect.verticalNormalizedPosition = 1f;
            try
            {
                if (_inAppHelpScrollRect != null && _inAppHelpScrollRect.content != null)
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_inAppHelpScrollRect.content);
            }
            catch { }
        }

        private void ScrollInAppHelpToSection(string sectionId)
        {
            if (!_inAppHelpOpen) SetInAppHelpOpen(true);
            if (_inAppHelpSearchInput != null) _inAppHelpSearchInput.text = "";
            RefreshInAppHelpNavVisibility();
            SelectInAppHelpSection(sectionId);
        }

        private void ApplyInAppHelpTypography()
        {
            ApplyInAppHelpTextScale(_inAppHelpHeaderTitleText, GalleryUiDesignTokens.FontRef);
            ApplyInAppHelpTextScale(_inAppHelpSectionTitleText, GalleryUiDesignTokens.FontRef);
            for (int i = 0; i < _inAppHelpBodyTexts.Count; i++)
            {
                if (_inAppHelpBodyTexts[i] != null)
                    ApplyInAppHelpTextScale(_inAppHelpBodyTexts[i], GalleryUiDesignTokens.FontRef);
            }
            try
            {
                if (_inAppHelpSearchInput != null)
                {
                    Text st = _inAppHelpSearchInput.textComponent;
                    if (st != null) ApplyInAppHelpTextScale(st, GalleryUiDesignTokens.FontRef);
                    Text ph = _inAppHelpSearchInput.placeholder as Text;
                    if (ph != null) ApplyInAppHelpTextScale(ph, GalleryUiDesignTokens.FontRef);
                }
            }
            catch { }
            foreach (var kv in _inAppHelpNavBtns)
            {
                if (kv.Value == null) continue;
                Text t = kv.Value.GetComponentInChildren<Text>(true);
                if (t != null) ApplyInAppHelpTextScale(t, GalleryUiDesignTokens.FontRef);
            }
            FinalizeInAppHelpFlowLayout();
        }

        private int InAppHelpNavRows()
        {
            return (InAppHelpAnchorIds.Length + InAppHelpNavColumns - 1) / InAppHelpNavColumns;
        }

        private float InAppHelpTopChromeHeight(float s)
        {
            int rows = InAppHelpNavRows();
            float pad = 10f * s;
            float rowSpacing = 5f * s;
            float navH = pad + rows * (GalleryUiDesignTokens.InAppHelpNavBtnHeightRef * s)
                + (rows > 1 ? (rows - 1) * rowSpacing : 0f) + pad;
            return (GalleryUiDesignTokens.InAppHelpHeaderHeightRef + 4f + GalleryUiDesignTokens.InAppHelpSearchHeightRef + 4f) * s + navH;
        }

        private void ApplyInAppHelpPanelLayout(float paneScale)
        {
            if (_inAppHelpPanelRT == null) return;
            float s = paneScale <= 0f ? 1f : paneScale;
            float w = GalleryUiDesignTokens.InAppHelpPanelWidthRef * s;
            float top = 65f * s;
            float bottom = 68f * s;
            _inAppHelpPanelRT.offsetMin = new Vector2(-w, bottom);
            _inAppHelpPanelRT.offsetMax = new Vector2(0f, -top);

            if (_inAppHelpHeaderRT != null)
                _inAppHelpHeaderRT.sizeDelta = new Vector2(0f, GalleryUiDesignTokens.InAppHelpHeaderHeightRef * s);

            if (_inAppHelpCloseBtnRT != null)
            {
                _inAppHelpCloseBtnRT.offsetMin = new Vector2(-GalleryUiDesignTokens.InAppHelpCloseBtnLeftInsetRef * s, 4f * s);
                _inAppHelpCloseBtnRT.offsetMax = new Vector2(-GalleryUiDesignTokens.InAppHelpCloseBtnRightInsetRef * s, -4f * s);
                Text closeTxt = _inAppHelpCloseBtnRT.GetComponentInChildren<Text>();
                if (closeTxt != null)
                    GalleryUiMetrics.ApplyGlyphFont(closeTxt, GalleryUiDesignTokens.InAppHelpCloseBtnSizeRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            if (_inAppHelpSearchInput != null)
            {
                RectTransform searchRT = _inAppHelpSearchInput.GetComponent<RectTransform>();
                if (searchRT != null)
                {
                    searchRT.anchoredPosition = new Vector2(0f, -(GalleryUiDesignTokens.InAppHelpHeaderHeightRef + 4f) * s);
                    searchRT.sizeDelta = new Vector2(-16f * s, GalleryUiDesignTokens.InAppHelpSearchHeightRef * s);
                }
                RescaleSearchInput(_inAppHelpSearchInput, s);
            }

            float chromeTop = InAppHelpTopChromeHeight(s);
            if (_inAppHelpNavRT != null && _inAppHelpNavGrid != null)
            {
                float pad = 10f * s;
                float spacing = 6f * s;
                float innerW = GalleryUiDesignTokens.InAppHelpPanelWidthRef * s - pad * 2f;
                float cellW = (innerW - spacing * (InAppHelpNavColumns - 1)) / InAppHelpNavColumns;
                float cellH = GalleryUiDesignTokens.InAppHelpNavBtnHeightRef * s;
                int rows = InAppHelpNavRows();

                _inAppHelpNavGrid.padding = new RectOffset(
                    Mathf.RoundToInt(pad), Mathf.RoundToInt(pad),
                    Mathf.RoundToInt(4f * s), Mathf.RoundToInt(6f * s));
                _inAppHelpNavGrid.spacing = new Vector2(spacing, 5f * s);
                _inAppHelpNavGrid.cellSize = new Vector2(cellW, cellH);

                float navH = _inAppHelpNavGrid.padding.top + rows * cellH
                    + (rows > 1 ? (rows - 1) * (5f * s) : 0f) + _inAppHelpNavGrid.padding.bottom;
                _inAppHelpNavRT.sizeDelta = new Vector2(0f, navH);
                _inAppHelpNavRT.anchoredPosition = new Vector2(0f, -(GalleryUiDesignTokens.InAppHelpHeaderHeightRef + 4f + GalleryUiDesignTokens.InAppHelpSearchHeightRef) * s);
            }

            Transform bodyCard = _inAppHelpPanelGO != null ? _inAppHelpPanelGO.transform.Find("BodyCard") : null;
            if (bodyCard != null)
            {
                RectTransform bcRT = bodyCard as RectTransform;
                if (bcRT != null)
                {
                    bcRT.offsetMin = new Vector2(8f * s, 8f * s);
                    bcRT.offsetMax = new Vector2(-8f * s, -chromeTop);
                }
            }

            if (_inAppHelpBodyScrollRT != null)
            {
                float scrollPad = 6f * s;
                _inAppHelpBodyScrollRT.offsetMin = new Vector2(scrollPad, scrollPad);
                _inAppHelpBodyScrollRT.offsetMax = new Vector2(-scrollPad, -scrollPad);
            }

            AlignInAppHelpScrollViewport();
            ApplyInAppHelpIconPreviewLayout(s);
            ApplyInAppHelpTypography();
        }
    }
}
