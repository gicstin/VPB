using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Floating Save popup. The green side-rail Save button opens this instead of a side
        // tab, mirroring the Save popup used by the quick-menu assignable grid button so the
        // save experience is unified (see issue #62). Reuses BuildSaveMenuOptions() so the
        // option order matches the old side panel.

        private GameObject _saveMenuPopupGO;
        private bool _saveMenuPopupOpen;
        private bool _saveMenuPopupOpenedLeft;

        private void EnsureSaveMenuPopupChrome()
        {
            if (_saveMenuPopupGO != null) return;
            if (backgroundBoxGO == null) return;

            _saveMenuPopupGO = new GameObject("SaveMenuPopup");
            _saveMenuPopupGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform overlayRT = _saveMenuPopupGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayRT.pivot = new Vector2(0.5f, 0.5f);
            Image overlayBg = _saveMenuPopupGO.AddComponent<Image>();
            overlayBg.color = new Color(0f, 0f, 0f, 0.001f);
            overlayBg.raycastTarget = true;
            Button overlayBtn = _saveMenuPopupGO.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(CloseSaveMenuPopup);
            _saveMenuPopupGO.SetActive(false);

            GameObject panel = new GameObject("SaveMenuPanel");
            panel.transform.SetParent(_saveMenuPopupGO.transform, false);
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0f, 0.5f);
            panelRT.sizeDelta = new Vector2(260f, 50f);
            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);
            VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RebuildSaveMenuPopupRows(Transform panel)
        {
            if (panel == null) return;
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            List<SaveMenuOption> options = BuildSaveMenuOptions();
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    SaveMenuOption o = options[i];
                    if (o == null) continue;
                    SaveMenuOption captured = o;
                    GameObject row = UI.CreateUIButton(panel.gameObject, 0f, 36f, captured.Label, 15, 0f, 0f, AnchorPresets.stretchAll, () =>
                    {
                        if (!captured.Enabled) return;
                        if (captured.AutoClose) CloseSaveMenuPopup();
                        try { if (captured.Action != null) captured.Action(); } catch { }
                    });
                    if (row == null) continue;
                    Image img = row.GetComponent<Image>();
                    if (img != null) img.color = captured.Enabled ? UI.PopupRowBackdrop : new Color(0.2f, 0.2f, 0.2f, 0.7f);
                    Button btn = row.GetComponent<Button>();
                    if (btn != null) btn.interactable = captured.Enabled;
                    Text t = row.GetComponentInChildren<Text>();
                    if (t != null)
                    {
                        t.alignment = TextAnchor.MiddleLeft;
                        t.color = captured.Enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                    }
                    LayoutElement le = row.GetComponent<LayoutElement>();
                    if (le == null) le = row.AddComponent<LayoutElement>();
                    le.preferredHeight = 36f;
                    le.flexibleWidth = 1f;
                }
            }

            GameObject cancel = UI.CreateUIButton(panel.gameObject, 0f, 36f,
                VPBTranslation.T("hook.qmbutton.cancel", "Cancel"), 15, 0f, 0f, AnchorPresets.stretchAll, CloseSaveMenuPopup);
            if (cancel != null)
            {
                Image cImg = cancel.GetComponent<Image>();
                if (cImg != null) cImg.color = new Color(0.55f, 0.22f, 0.22f, 1f);
                Text ct = cancel.GetComponentInChildren<Text>();
                if (ct != null)
                {
                    ct.alignment = TextAnchor.MiddleCenter;
                    ct.color = Color.white;
                }
                LayoutElement cle = cancel.GetComponent<LayoutElement>();
                if (cle == null) cle = cancel.AddComponent<LayoutElement>();
                cle.preferredHeight = 36f;
                cle.flexibleWidth = 1f;
            }
        }

        private void ToggleSaveMenuPopup(bool useLeftSide)
        {
            EnsureSaveMenuPopupChrome();
            if (_saveMenuPopupGO == null) return;

            // Toggle off if the same side is reopened while showing.
            if (_saveMenuPopupOpen && _saveMenuPopupOpenedLeft == useLeftSide)
            {
                CloseSaveMenuPopup();
                return;
            }

            _saveMenuPopupOpen = true;
            _saveMenuPopupOpenedLeft = useLeftSide;

            Transform panel = _saveMenuPopupGO.transform.Find("SaveMenuPanel");
            if (panel != null)
            {
                RebuildSaveMenuPopupRows(panel);
                PositionSaveMenuPopupPanel(panel as RectTransform, useLeftSide);
                try { RescaleSaveMenuPopupInternal(ChromeScale); } catch { }
            }
            _saveMenuPopupGO.transform.SetAsLastSibling();
            _saveMenuPopupGO.SetActive(true);
        }

        private void PositionSaveMenuPopupPanel(RectTransform panelRT, bool useLeftSide)
        {
            if (panelRT == null) return;
            RectTransform overlayRT = _saveMenuPopupGO != null ? _saveMenuPopupGO.GetComponent<RectTransform>() : null;
            GameObject anchorGO = useLeftSide ? leftSaveBtnGO : rightSaveBtnGO;
            RectTransform anchorRT = anchorGO != null ? anchorGO.GetComponent<RectTransform>() : null;

            float gap = 12f;
            if (overlayRT == null || anchorRT == null)
            {
                // Fallback: pin just inside the chosen rail, vertically centered.
                panelRT.pivot = new Vector2(useLeftSide ? 0f : 1f, 0.5f);
                panelRT.anchoredPosition = new Vector2(useLeftSide ? -overlayHalfWidthFallback() : overlayHalfWidthFallback(), 0f);
                return;
            }

            Vector3 worldCenter = anchorRT.TransformPoint(anchorRT.rect.center);
            Vector3 local = overlayRT.InverseTransformPoint(worldCenter);
            float halfBtn = anchorRT.rect.width * 0.5f;
            if (useLeftSide)
            {
                panelRT.pivot = new Vector2(0f, 0.5f);
                panelRT.anchoredPosition = new Vector2(local.x + halfBtn + gap, 0f);
            }
            else
            {
                panelRT.pivot = new Vector2(1f, 0.5f);
                panelRT.anchoredPosition = new Vector2(local.x - halfBtn - gap, 0f);
            }
        }

        private float overlayHalfWidthFallback()
        {
            RectTransform overlayRT = _saveMenuPopupGO != null ? _saveMenuPopupGO.GetComponent<RectTransform>() : null;
            if (overlayRT == null) return 0f;
            return overlayRT.rect.width * 0.5f - 20f;
        }

        private void CloseSaveMenuPopup()
        {
            _saveMenuPopupOpen = false;
            if (_saveMenuPopupGO != null) _saveMenuPopupGO.SetActive(false);
        }

        private void RescaleSaveMenuPopupInternal(float s)
        {
            if (_saveMenuPopupGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _saveMenuPopupGO.transform.Find("SaveMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef);
        }
    }
}
