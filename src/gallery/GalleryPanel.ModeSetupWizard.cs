using System;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private enum ModeSetupClass { DesktopFixed, DesktopFloating, VR }

        private GameObject _modeSetupWizardGO;
        private Text _modeSetupTitleText;
        private Text _modeSetupBodyText;
        private Text _modeSetupStepText;
        private int _modeSetupStep;
        private string _modeSetupDockChoice = "Right";
        private string _modeSetupLeftPanel = "None";
        private string _modeSetupRightPanel = "Category";
        private int _modeSetupDensity = 1;

        private ModeSetupClass ResolveModeSetupClass()
        {
            try
            {
                if (VPBConfig.Instance != null && VPBConfig.Instance.IsVR) return ModeSetupClass.VR;
            }
            catch { }
            try { if (XrUtils.IsVrActive()) return ModeSetupClass.VR; } catch { }
            return isFixedLocally ? ModeSetupClass.DesktopFixed : ModeSetupClass.DesktopFloating;
        }

        private bool IsModeSetupPending()
        {
            if (VPBConfig.Instance == null) return false;
            switch (ResolveModeSetupClass())
            {
                case ModeSetupClass.VR: return !VPBConfig.Instance.ModeSetupWizardDoneVR;
                case ModeSetupClass.DesktopFixed: return !VPBConfig.Instance.ModeSetupWizardDoneDesktopFixed;
                default: return !VPBConfig.Instance.ModeSetupWizardDoneDesktopFloating;
            }
        }

        private void MarkModeSetupDone()
        {
            if (VPBConfig.Instance == null) return;
            switch (ResolveModeSetupClass())
            {
                case ModeSetupClass.VR: VPBConfig.Instance.ModeSetupWizardDoneVR = true; break;
                case ModeSetupClass.DesktopFixed: VPBConfig.Instance.ModeSetupWizardDoneDesktopFixed = true; break;
                default: VPBConfig.Instance.ModeSetupWizardDoneDesktopFloating = true; break;
            }
            try { VPBConfig.Instance.Save(true, true); } catch { }
            if (_modeSetupWizardGO != null) _modeSetupWizardGO.SetActive(false);
            _showPostWizardFirstRunHint = true;
            try { RefreshFirstRunHintStrip(); } catch { }
        }

        public void TryShowModeSetupWizard()
        {
            if (!IsModeSetupPending()) return;
            if (_modeSetupWizardGO != null && _modeSetupWizardGO.activeSelf) return;
            if (_modeSetupWizardGO == null) BuildModeSetupWizard();
            if (_modeSetupWizardGO == null) return;
            _modeSetupStep = 0;
            _modeSetupDockChoice = "Right";
            _modeSetupLeftPanel = "None";
            _modeSetupRightPanel = "Category";
            _modeSetupDensity = 1;
            _modeSetupPresetCycle = 0;
            RefreshModeSetupWizardStep();
            _modeSetupWizardGO.SetActive(true);
            try { _modeSetupWizardGO.transform.SetAsLastSibling(); } catch { }
            try { RefreshFirstRunHintStrip(); } catch { }
        }

        private System.Collections.IEnumerator ShowModeSetupWizardDeferred()
        {
            yield return null;
            TryShowModeSetupWizard();
        }

        private void BuildModeSetupWizard()
        {
            if (backgroundBoxGO == null) return;

            _modeSetupWizardGO = UI.CreateChildRT(backgroundBoxGO, "ModeSetupWizard", AnchorPresets.stretchAll);

            Image blocker = UI.AddImage(_modeSetupWizardGO, new Color(0f, 0f, 0f, 0.55f));

            GameObject panel = UI.CreateChildRT(_modeSetupWizardGO, "Panel", AnchorPresets.middleCenter, new Vector2(440f, 320f));
            Image panelBg = UI.AddImage(panel, UI.PopupBackdrop);
            Button panelBtn = panel.AddComponent<Button>();
            panelBtn.targetGraphic = panelBg;
            panelBtn.transition = Selectable.Transition.None;
            UI.NeutralizeSelectableColorTint(panelBtn);
            panelBtn.onClick.AddListener(() =>
            {
                if (_modeSetupStep == 2)
                {
                    CycleModeSetupPanelPreset();
                    RefreshModeSetupWizardStep();
                }
            });

            _modeSetupTitleText = CreateWizardLabel(panel.transform, "", GalleryUiDesignTokens.FontRef, TextAnchor.UpperCenter,
                new Vector2(12f, -12f), new Vector2(-12f, -44f));
            _modeSetupTitleText.fontStyle = FontStyle.Normal;
            _modeSetupStepText = CreateWizardLabel(panel.transform, "", GalleryUiDesignTokens.FontRef, TextAnchor.UpperLeft,
                new Vector2(16f, -48f), new Vector2(-16f, -68f));
            _modeSetupStepText.color = new Color(0.75f, 0.8f, 0.88f, 1f);
            _modeSetupBodyText = CreateWizardLabel(panel.transform, "", GalleryUiDesignTokens.FontRef, TextAnchor.UpperLeft,
                new Vector2(16f, -72f), new Vector2(-16f, -200f));
            _modeSetupBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modeSetupBodyText.verticalOverflow = VerticalWrapMode.Overflow;

            float btnY = -248f;
            UI.CreateUIButton(panel, 90f, 34f,
                VPBTranslation.T("gallery.mode_setup.skip", "Skip"), GalleryUiDesignTokens.FontRef, 0f, btnY, AnchorPresets.centre,
                () => MarkModeSetupDone());
            UI.CreateUIButton(panel, 90f, 34f,
                VPBTranslation.T("gallery.mode_setup.back", "Back"), GalleryUiDesignTokens.FontRef, -100f, btnY, AnchorPresets.centre,
                () => { if (_modeSetupStep > 0) { _modeSetupStep--; RefreshModeSetupWizardStep(); } });
            UI.CreateUIButton(panel, 110f, 34f,
                VPBTranslation.T("gallery.mode_setup.next", "Next"), GalleryUiDesignTokens.FontRef, 100f, btnY, AnchorPresets.centre,
                () => ModeSetupWizardAdvance());

            _modeSetupWizardGO.SetActive(false);
        }

        private static Text CreateWizardLabel(Transform parent, string text, int fontSize, TextAnchor align,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            Text t = UI.CreateLabel(parent.gameObject, text, fontSize, Color.white, align, raycastTarget: false, anchorPreset: AnchorPresets.hStretchTop, name: "Label");
            RectTransform rt = t.GetComponent<RectTransform>();
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return t;
        }

        private void RefreshModeSetupWizardStep()
        {
            if (_modeSetupTitleText == null || _modeSetupBodyText == null || _modeSetupStepText == null) return;
            ModeSetupClass mode = ResolveModeSetupClass();
            int totalSteps = 3;
            _modeSetupStepText.text = string.Format(
                VPBTranslation.T("gallery.mode_setup.step_of", "Step {0} of {1}"),
                (_modeSetupStep + 1).ToString(), totalSteps.ToString());

            if (_modeSetupStep == 0)
            {
                _modeSetupTitleText.text = VPBTranslation.T("gallery.mode_setup.welcome_title", "Quick gallery setup");
                if (mode == ModeSetupClass.VR)
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.welcome_vr",
                        "VR mode detected. Three short steps set comfortable defaults for side panels and grid density.");
                else if (mode == ModeSetupClass.DesktopFixed)
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.welcome_fixed",
                        "Fixed dock mode. Choose dock edge, default side lists, and how dense the grid feels.");
                else
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.welcome_floating",
                        "Floating gallery. Pick which side lists open by default and grid density.");
            }
            else if (_modeSetupStep == 1)
            {
                _modeSetupTitleText.text = VPBTranslation.T("gallery.mode_setup.layout_title", "Layout");
                if (mode == ModeSetupClass.DesktopFixed)
                {
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.layout_fixed_body",
                        "Dock edge defaults to Right. Change later in Settings → Desktop.\n\nUse Back on step 3 to try another preset before finishing.");
                }
                else if (mode == ModeSetupClass.VR)
                {
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.layout_vr_body",
                        "Side lists stay available on both rails. VR hover tooltips help identify icon-only buttons.");
                }
                else
                {
                    _modeSetupBodyText.text = VPBTranslation.T("gallery.mode_setup.layout_floating_body",
                        "The panel floats in world space. Side rails stay on left and right edges.");
                }
            }
            else
            {
                _modeSetupTitleText.text = VPBTranslation.T("gallery.mode_setup.panels_title", "Side lists & density");
                string densityName = _modeSetupDensity == 0
                    ? VPBTranslation.T("gallery.mode_setup.density_compact", "Compact")
                    : (_modeSetupDensity == 2
                        ? VPBTranslation.T("gallery.mode_setup.density_spacious", "Spacious")
                        : VPBTranslation.T("gallery.mode_setup.density_comfortable", "Comfortable"));
                _modeSetupBodyText.text = string.Format(
                    VPBTranslation.T("gallery.mode_setup.panels_body",
                        "Preset: left {0}, right {1}, density {2}.\n\nTap the panel background to cycle presets, then Next to apply.\n\nA tip strip appears under the title bar — use ? for in-app help."),
                    _modeSetupLeftPanel, _modeSetupRightPanel, densityName);
            }
        }

        private int _modeSetupPresetCycle;

        private void ModeSetupWizardAdvance()
        {
            if (_modeSetupStep < 2)
            {
                _modeSetupStep++;
                RefreshModeSetupWizardStep();
                return;
            }
            ApplyModeSetupChoices();
            MarkModeSetupDone();
        }

        private void CycleModeSetupPanelPreset()
        {
            if (_modeSetupPresetCycle == 0)
            {
                _modeSetupLeftPanel = "None"; _modeSetupRightPanel = "Category"; _modeSetupDensity = 1;
            }
            else if (_modeSetupPresetCycle == 1)
            {
                _modeSetupLeftPanel = "Creator"; _modeSetupRightPanel = "None"; _modeSetupDensity = 1;
            }
            else if (_modeSetupPresetCycle == 2)
            {
                _modeSetupLeftPanel = "None"; _modeSetupRightPanel = "None"; _modeSetupDensity = 1;
            }
            else if (_modeSetupPresetCycle == 3)
            {
                _modeSetupDensity = 0;
            }
            else if (_modeSetupPresetCycle == 4)
            {
                _modeSetupDensity = 1;
            }
            else
            {
                _modeSetupLeftPanel = "None"; _modeSetupRightPanel = "Category"; _modeSetupDensity = 2;
            }
            _modeSetupPresetCycle = (_modeSetupPresetCycle + 1) % 6;
        }

        private void ApplyModeSetupChoices()
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return;
            ModeSetupClass mode = ResolveModeSetupClass();
            bool vr = mode == ModeSetupClass.VR;

            if (mode == ModeSetupClass.DesktopFixed)
            {
                string dock = VPBConfig.NormalizeDesktopFixedDockSide(_modeSetupDockChoice);
                cfg.DesktopFixedDockSide = dock;
                cfg.DesktopFixedDefaultDockSide = dock;
                if (cfg.DesktopFixedEnforceDockSide)
                    cfg.DesktopFixedEnforcedDockSide = dock;
            }

            cfg.GalleryDefaultLeftSidePanel = VPBConfig.NormalizeGallerySidePanel(_modeSetupLeftPanel);
            cfg.GalleryDefaultRightSidePanel = VPBConfig.NormalizeGallerySidePanel(_modeSetupRightPanel);

            if (_modeSetupDensity == 0)
            {
                cfg.GridColumnCount = vr ? 4 : 5;
                if (vr) cfg.InnerPaneScaleVR = 1.0f;
                else cfg.InnerPaneScaleDesktop = 0.85f;
            }
            else if (_modeSetupDensity == 2)
            {
                cfg.GridColumnCount = vr ? 3 : 4;
                if (vr)
                {
                    cfg.InnerPaneScaleVR = 1.2f;
                    cfg.VrHoverTooltipEnabled = true;
                }
                else cfg.InnerPaneScaleDesktop = 1.05f;
            }
            else
            {
                cfg.GridColumnCount = 4;
                if (vr) cfg.InnerPaneScaleVR = 1.05f;
                else cfg.InnerPaneScaleDesktop = 0.9f;
            }

            try { cfg.TriggerChange(); } catch { }
            try { ApplyInnerPaneScale(); } catch { }
            try { ApplySidePanelDefaultsFromConfig(); } catch { }
            try
            {
                if (contentGO != null)
                {
                    RecyclingGridView rgv = contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        rgv.fixedColumns = GridColumnCount;
                        rgv.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), GridColumnCount);
                        rgv.SetAdaptiveConfig(true, 200f, GridColumnCount, false);
                    }
                }
                RebuildGridLayout();
            }
            catch { }
            if (mode == ModeSetupClass.DesktopFixed)
            {
                try { UpdateFooterDockButtonState(); } catch { }
                try { UpdateSideButtonsVisibility(); } catch { }
            }
            try { UpdateLayout(); } catch { }
            try { cfg.Save(true, true); } catch { }
        }
    }
}
