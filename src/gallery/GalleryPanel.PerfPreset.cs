using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject footerPerfToggleBtn;
        private Image footerPerfToggleBtnImage;
        private Text footerPerfToggleBtnText;
        private GameObject footerPerfMinusBtn;
        private Image footerPerfMinusBtnImage;
        private GameObject footerPerfPlusBtn;
        private Image footerPerfPlusBtnImage;

        private void CreateFooterPerfControls(GameObject parent)
        {
            float chip = GalleryUiDesignTokens.ButtonSizeRef;
            footerPerfToggleBtn = UI.CreateUIButton(parent, chip, chip, VpbPerfController.GetToggleLabel(), 16, 0, 0,
                AnchorPresets.middleCenter, ToggleFooterPerfMode);
            footerPerfToggleBtn.name = "Footer_PerfToggle";
            footerPerfToggleBtnImage = footerPerfToggleBtn.GetComponent<Image>();
            footerPerfToggleBtnText = footerPerfToggleBtn.GetComponentInChildren<Text>();
            if (footerPerfToggleBtnText != null)
            {
                footerPerfToggleBtnText.resizeTextForBestFit = true;
                footerPerfToggleBtnText.resizeTextMinSize = 10;
                footerPerfToggleBtnText.resizeTextMaxSize = 18;
            }
            AddTooltipPlain(footerPerfToggleBtn,
                VpbPerfController.GetTooltip() + "\n" +
                VPBTranslation.T("gallery.perf.tooltip.scroll", "Mouse wheel: change quality level.") + "\n" +
                VPBTranslation.T("gallery.perf.tooltip.open_settings", "Right-click: Quality settings."));
            try
            {
                if (footerPerfToggleBtn.GetComponent<FooterPerfToggleScroll>() == null)
                    footerPerfToggleBtn.AddComponent<FooterPerfToggleScroll>();
            }
            catch { }
            try
            {
                var perfEvt = footerPerfToggleBtn.GetComponent<EventTrigger>() ?? footerPerfToggleBtn.AddComponent<EventTrigger>();
                var right = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                right.callback.AddListener(data =>
                {
                    var ped = data as PointerEventData;
                    if (ped == null || ped.button != PointerEventData.InputButton.Right) return;
                    try { OpenSettingsGroup("performance"); } catch { }
                });
                perfEvt.triggers.Add(right);
            }
            catch { }

            footerPerfMinusBtn = UI.CreateUIButton(parent, chip, chip, "-", 24, 0, 0,
                AnchorPresets.middleCenter, FooterPerfStepDown);
            footerPerfMinusBtn.name = "Footer_PerfMinus";
            footerPerfMinusBtnImage = footerPerfMinusBtn.GetComponent<Image>();
            UI.ApplyBarIconFromPath(footerPerfMinusBtn, "photo-down");

            footerPerfPlusBtn = UI.CreateUIButton(parent, chip, chip, "+", 24, 0, 0,
                AnchorPresets.middleCenter, FooterPerfStepUp);
            footerPerfPlusBtn.name = "Footer_PerfPlus";
            footerPerfPlusBtnImage = footerPerfPlusBtn.GetComponent<Image>();
            UI.ApplyBarIconFromPath(footerPerfPlusBtn, "photo-up");

            AddTooltipPlain(footerPerfMinusBtn, VpbPerfController.GetStepTooltip());
            AddTooltipPlain(footerPerfPlusBtn, VpbPerfController.GetStepTooltip());

            RefreshFooterPerfChrome();
        }

        private void ToggleFooterPerfMode()
        {
            try
            {
                if (VpbPerfController.IsAutoEnablePending)
                    VpbPerfController.CancelAutoEnableAfterSceneLoad(true, true);
                else
                    VpbPerfController.SetEnabled(!VpbPerfController.IsPerfModeWanted, true, true);
            }
            catch { }
        }

        private void FooterPerfStepDown()
        {
            try { VpbPerfController.StepBy(-1, true, true); } catch { }
        }

        private void FooterPerfStepUp()
        {
            try { VpbPerfController.StepBy(1, true, true); } catch { }
        }

        internal void RefreshFooterPerfChrome()
        {
            Color backdropColor = VpbPerfController.GetToggleBackdropColor();
            if (footerPerfToggleBtnImage != null)
                footerPerfToggleBtnImage.color = backdropColor;

            bool perfWanted = VpbPerfController.IsPerfModeWanted;
            Transform iconTr = footerPerfToggleBtn != null ? footerPerfToggleBtn.transform.Find("Icon") : null;
            if (perfWanted)
            {
                int step = VpbPerfController.StepIndex;
                // Activate icon child if it was previously hidden
                if (iconTr != null) iconTr.gameObject.SetActive(true);
                UI.ApplyBarIconFromPath(footerPerfToggleBtn, "rosette-number-" + step, backdropOverride: backdropColor);
                // Restore backdrop (ApplyBarIconFromPath may overwrite it)
                if (footerPerfToggleBtnImage != null) footerPerfToggleBtnImage.color = backdropColor;
                // Tint the icon to match the perf state color
                iconTr = footerPerfToggleBtn != null ? footerPerfToggleBtn.transform.Find("Icon") : null;
                if (iconTr != null)
                {
                    Image iconImg = iconTr.GetComponent<Image>();
                    if (iconImg != null) iconImg.color = VpbPerfController.GetToggleTextColor();
                }
                // Hide text label (icon replaces it)
                if (footerPerfToggleBtnText != null) footerPerfToggleBtnText.gameObject.SetActive(false);
            }
            else
            {
                // Hide icon, show "Off" text
                if (iconTr != null) iconTr.gameObject.SetActive(false);
                if (footerPerfToggleBtnText != null)
                {
                    footerPerfToggleBtnText.gameObject.SetActive(true);
                    footerPerfToggleBtnText.text = VpbPerfController.GetToggleLabel();
                    footerPerfToggleBtnText.color = VpbPerfController.GetToggleTextColor();
                }
            }

            bool showStep = VpbPerfController.IsPerfModeWanted;

            if (footerPerfMinusBtn != null)
            {
                footerPerfMinusBtn.SetActive(showStep);
                if (showStep)
                {
                    if (footerPerfMinusBtnImage != null)
                        footerPerfMinusBtnImage.color = VpbPerfController.GetStepButtonColor(VpbPerfController.CanStepDown());
                    var btn = footerPerfMinusBtn.GetComponent<Button>();
                    if (btn != null) btn.interactable = VpbPerfController.CanStepDown();
                }
            }
            if (footerPerfPlusBtn != null)
            {
                footerPerfPlusBtn.SetActive(showStep);
                if (showStep)
                {
                    if (footerPerfPlusBtnImage != null)
                        footerPerfPlusBtnImage.color = VpbPerfController.GetStepButtonColor(VpbPerfController.CanStepUp());
                    var btn = footerPerfPlusBtn.GetComponent<Button>();
                    if (btn != null) btn.interactable = VpbPerfController.CanStepUp();
                }
            }
        }
    }
}
