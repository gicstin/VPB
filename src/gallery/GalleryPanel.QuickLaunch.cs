using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal const string QuickLaunchButtonName = "QuickLaunchBtn";

        internal static bool ShouldOfferQuickLaunch(
            bool isInternalSettingRow,
            bool settingsListView,
            bool importSidebar,
            bool stripKeepSubScenePick,
            bool removeMode,
            bool cleanupMode,
            bool holdToLaunch,
            ApplyMode applyMode)
        {
            if (isInternalSettingRow || settingsListView) return false;
            if (importSidebar || stripKeepSubScenePick || removeMode || cleanupMode) return false;
            if (holdToLaunch) return false;
            return applyMode == ApplyMode.DoubleClick;
        }

        internal bool ShouldOfferQuickLaunch(FileEntry file, GalleryLayoutMode hostLayout)
        {
            if (file == null) return false;
            if (layoutMode != hostLayout) return false;
            return ShouldOfferQuickLaunch(
                file is InternalSettingRowEntry,
                settingsListViewActive,
                importSidebarActive,
                _stripKeepSubScenePickActive,
                _removeModeActive,
                cleanupModeActive,
                holdToLaunchEnabled,
                ItemApplyMode);
        }

        private static void CreateGridQuickLaunchButton(GameObject gridLabelGO, Text captionText)
        {
            if (gridLabelGO == null) return;
            Image captionBg = gridLabelGO.GetComponent<Image>();
            if (captionBg != null) captionBg.raycastTarget = true;
            CreateQuickLaunchHost(gridLabelGO, captionText, GalleryLayoutMode.Grid,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        }

        private static void CreateListQuickLaunchButton(GameObject listNameGO, Text nameText)
        {
            if (listNameGO == null) return;
            if (nameText != null) nameText.raycastTarget = true;
            CreateQuickLaunchHost(listNameGO, nameText, GalleryLayoutMode.List,
                new Vector2(1f, 0.1f), new Vector2(1f, 0.9f), new Vector2(1f, 0.5f));
        }

        private static void CreateQuickLaunchHost(
            GameObject hostGO, Text sourceText, GalleryLayoutMode hostLayout,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            GameObject go = new GameObject(QuickLaunchButtonName);
            go.transform.SetParent(hostGO.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image bg = UI.AddImage(go, GalleryUiColorTokens.FamilyGreen, true);
            Text label = UI.CreateLabel(
                go, "", GalleryUiDesignTokens.GridLabelFontRef, Color.white, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, raycastTarget: false, richText: false, name: "Text");
            RectTransform labelRT = label.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            UI.ConfigButtonFlat(btn, applyColors: true);
            go.SetActive(false);

            GalleryQuickLaunchHost host = hostGO.AddComponent<GalleryQuickLaunchHost>();
            host.button = btn;
            host.label = label;
            host.sourceText = sourceText;
            host.hostLayout = hostLayout;
        }

        internal const string QuickLaunchIconText = "▶";

        internal string QuickLaunchFullText()
        {
            return QuickLaunchIconText + "  " + VPBTranslation.T("gallery.caption.quick_launch", "Launch");
        }

        internal void AttachQuickLaunchTooltip(GameObject go)
        {
            AddTooltipPlain(go, VPBTranslation.T("gallery.caption.quick_launch_tip", "Launch now (one click)."));
        }

        internal void QuickLaunchFromHost(FileEntry file, GalleryLayoutMode hostLayout)
        {
            if (!ShouldOfferQuickLaunch(file, hostLayout)) return;
            lastClickTime = 0f;
            ApplyFileEntryNow(file);
        }
    }

    public class GalleryQuickLaunchHost : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Button button;
        public Text label;
        public Text sourceText;
        public GalleryLayoutMode hostLayout;
        private bool _clickWired;

        void OnDestroy()
        {
            if (!_clickWired || button == null) return;
            button.onClick.RemoveListener(OnLaunchClicked);
            _clickWired = false;
        }

        void OnDisable()
        {
            SetShown(null, false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            GalleryPanel panel;
            FileEntry file;
            bool offer = TryResolve(out panel, out file) && panel.ShouldOfferQuickLaunch(file, hostLayout);
            SetShown(offer ? panel : null, offer);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetShown(null, false);
        }

        private void SetShown(GalleryPanel panel, bool shown)
        {
            if (button == null) return;
            GameObject go = button.gameObject;
            if (shown && panel != null)
            {
                if (!_clickWired)
                {
                    button.onClick.AddListener(OnLaunchClicked);
                    _clickWired = true;
                }
                FitToHost(panel);
                panel.AttachQuickLaunchTooltip(go);
                go.transform.SetAsLastSibling();
            }
            if (go.activeSelf != shown) go.SetActive(shown);
        }

        private void FitToHost(GalleryPanel panel)
        {
            if (label == null) return;
            RectTransform hostRT = transform as RectTransform;
            RectTransform btnRT = button.transform as RectTransform;
            float hostW = hostRT != null ? hostRT.rect.width : 0f;
            float hostH = hostRT != null ? hostRT.rect.height : 0f;
            bool pill = hostLayout == GalleryLayoutMode.List;
            float barH = pill ? hostH * 0.8f : hostH;

            int font = sourceText != null ? sourceText.fontSize : label.fontSize;
            if (barH > 1f) font = Mathf.Min(font, Mathf.FloorToInt(barH * 0.7f));
            label.fontSize = Mathf.Max(8, font);

            label.text = panel.QuickLaunchFullText();
            float pad = Mathf.Max(6f, barH * 0.4f);
            float fullW = label.preferredWidth + pad * 2f;
            float room = pill ? hostW * 0.5f : hostW;
            bool iconOnly = hostW > 1f && fullW > room;
            if (iconOnly) label.text = GalleryPanel.QuickLaunchIconText;

            if (pill && btnRT != null)
            {
                float w = iconOnly ? Mathf.Max(barH * 1.2f, label.preferredWidth + pad) : fullW;
                btnRT.sizeDelta = new Vector2(w, 0f);
                btnRT.anchoredPosition = Vector2.zero;
            }
        }

        private bool TryResolve(out GalleryPanel panel, out FileEntry file)
        {
            panel = null;
            file = null;
            UIHoverReveal reveal = GetComponentInParent<UIHoverReveal>();
            if (reveal == null) return false;
            panel = reveal.panel;
            file = reveal.file;
            return panel != null && file != null;
        }

        private void OnLaunchClicked()
        {
            GalleryPanel panel;
            FileEntry file;
            if (!TryResolve(out panel, out file)) return;
            try { panel.QuickLaunchFromHost(file, hostLayout); }
            catch (Exception ex) { LogUtil.LogError("[VPB] Quick launch failed for " + (file.Path ?? file.Uid) + ": " + ex); }
        }
    }
}
