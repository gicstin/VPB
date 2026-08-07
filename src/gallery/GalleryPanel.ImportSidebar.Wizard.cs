using System;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int ImportWizardStepCount = 3;
        private readonly GameObject[] _importWizardStepHeaders = new GameObject[ImportWizardStepCount];
        private readonly GameObject[] _importWizardStepContents = new GameObject[ImportWizardStepCount];
        private readonly Text[] _importWizardStepHeaderLabels = new Text[ImportWizardStepCount];
        // Bit i set = step i collapsed. Persisted as ImportSidebarPrefs.wizardCollapsedMask.
        private int _importWizardCollapsedMask;
        private Text _importWizardMultiSelectHint;

        private Transform CreateImportWizardStep(Transform scrollContent, int stepIndex, string titleKey, string titleDefault)
        {
            if (scrollContent == null) return null;

            GameObject block = new GameObject("ImportWizardStep_" + stepIndex);
            block.transform.SetParent(scrollContent, false);
            UI.AddLE(block, flexibleWidth: 1f);

            UI.AddVLG(block, spacing: ImportSidebarBaseRowSpacing);

            GameObject header = new GameObject("StepHeader");
            header.transform.SetParent(block.transform, false);
            Image hdrBg = AddImportSidebarRoundedBg(header, ImportSidebarStepHeaderBg, raycastTarget: true);

            Text hdrTxt = UI.CreateLabel(header, "", ImportSidebarBaseFontSize,
                new Color(0.92f, 0.94f, 0.98f, 1f), TextAnchor.MiddleLeft, raycastTarget: false, name: "Label");
            RectTransform hdrLabelRT = hdrTxt.GetComponent<RectTransform>();

            LayoutElement hdrLe = UI.AddLE(header, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            _importWizardStepHeaders[stepIndex] = header;
            _importWizardStepHeaderLabels[stepIndex] = hdrTxt;

            Button hdrBtn = header.AddComponent<Button>();
            hdrBtn.targetGraphic = hdrBg;
            UI.NeutralizeSelectableColorTint(hdrBtn);
            int capturedStep = stepIndex;
            hdrBtn.onClick.AddListener(() => ToggleImportWizardStepCollapsed(capturedStep));
            AddTooltip(header, "gallery.import.wizard.step_toggle_tip", "Click to expand or collapse this step");

            LayoutElement hdrLeCaptured = hdrLe;
            Text hdrTxtCaptured = hdrTxt;
            RectTransform hdrLabelRTCaptured = hdrLabelRT;
            innerPaneScaleActions.Add(s => {
                if (hdrLeCaptured != null) hdrLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyImportSidebarLabelInsets(hdrLabelRTCaptured, s);
                ApplyScaledFont(hdrTxtCaptured, ImportSidebarBaseFontSize, s);
            });
            ApplyImportSidebarLabelInsets(hdrLabelRT, ChromeScale);

            GameObject content = new GameObject("StepContent");
            content.transform.SetParent(block.transform, false);
            VerticalLayoutGroup contentVlg = UI.AddVLG(content, spacing: ImportSidebarBaseRowSpacing,
                padding: new RectOffset(0, 0, 0, Mathf.RoundToInt(ImportSidebarBaseRowSpacing * 0.5f)));
            UI.AddLE(content, flexibleWidth: 1f);
            VerticalLayoutGroup contentVlgCaptured = contentVlg;
            innerPaneScaleActions.Add(s => {
                if (contentVlgCaptured == null) return;
                contentVlgCaptured.spacing = ImportSidebarBaseRowSpacing * s;
                contentVlgCaptured.padding = new RectOffset(0, 0, 0,
                    Mathf.RoundToInt(ImportSidebarBaseRowSpacing * 0.5f * s));
            });
            _importWizardStepContents[stepIndex] = content;
            content.SetActive(!IsImportWizardStepCollapsed(stepIndex));
            return content.transform;
        }

        private bool IsImportWizardStepCollapsed(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= ImportWizardStepCount) return false;
            return (_importWizardCollapsedMask & (1 << stepIndex)) != 0;
        }

        private void ToggleImportWizardStepCollapsed(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= ImportWizardStepCount) return;
            int bit = 1 << stepIndex;
            if ((_importWizardCollapsedMask & bit) != 0)
                _importWizardCollapsedMask &= ~bit;
            else
                _importWizardCollapsedMask |= bit;
            ApplyImportWizardStepCollapsedVisual(stepIndex);
            RefreshImportWizardStepHeaderGlyphs();
            RebuildImportSidebarContent();
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        private void ExpandImportWizardStep(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= ImportWizardStepCount) return;
            int bit = 1 << stepIndex;
            if ((_importWizardCollapsedMask & bit) == 0) return;
            _importWizardCollapsedMask &= ~bit;
            ApplyImportWizardStepCollapsedVisual(stepIndex);
            RefreshImportWizardStepHeaderGlyphs();
            RebuildImportSidebarContent();
            if (importSidebarBuilt) SaveImportSidebarPrefs();
        }

        private void ApplyImportWizardStepCollapsedVisual(int stepIndex)
        {
            GameObject content = _importWizardStepContents[stepIndex];
            if (content != null)
                content.SetActive(!IsImportWizardStepCollapsed(stepIndex));
        }

        private void RefreshImportWizardStepHeaderGlyphs()
        {
            for (int i = 0; i < ImportWizardStepCount; i++)
            {
                Text t = _importWizardStepHeaderLabels[i];
                if (t == null) continue;
                string title = VPBTranslation.T(ImportWizardStepTitleKeys[i], ImportWizardStepTitleDefaults[i]);
                // Types step: surface selection count on the accordion (was on removed summary strip).
                if (i == 1)
                {
                    int n = importSidebarMultiSelectedTypes != null ? importSidebarMultiSelectedTypes.Count : 0;
                    if (n > 0)
                        title = title + " (" + n + ")";
                }
                string glyph = IsImportWizardStepCollapsed(i) ? "\u25B6 " : "\u25BC ";
                t.text = glyph + (i + 1) + ". " + title;
            }
        }

        private void BuildImportWizardMultiSelectHint(Transform parent)
        {
            if (parent == null) return;

            GameObject hintRow = new GameObject("MultiSelectHint");
            hintRow.transform.SetParent(parent, false);
            LayoutElement hintLe = UI.AddLE(hintRow, preferredHeight: ImportSidebarBaseRowHeight * 0.75f, flexibleWidth: 1f);
            _importWizardMultiSelectHint = hintRow.AddComponent<Text>();
            _importWizardMultiSelectHint.text = "";
            _importWizardMultiSelectHint.color = new Color(1f, 0.75f, 0.45f, 1f);
            _importWizardMultiSelectHint.fontSize = ImportSidebarBaseFontSize;
            _importWizardMultiSelectHint.alignment = TextAnchor.MiddleLeft;
            try { VPBUiFont.ApplyTo(_importWizardMultiSelectHint); } catch { }
            _importWizardMultiSelectHint.raycastTarget = false;
            hintRow.SetActive(false);
            LayoutElement hintLeCaptured = hintLe;
            Text hintTxtCaptured = _importWizardMultiSelectHint;
            innerPaneScaleActions.Add(s => {
                if (hintLeCaptured != null) hintLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * 0.75f * s;
                ApplyScaledFont(hintTxtCaptured, ImportSidebarBaseFontSize, s);
            });
        }

        internal void BuildImportSidebarWizardBody()
        {
            if (importSidebarScrollContentRT == null) return;

            // No separate "1✓ Atoms · 2✓ Types" strip — step headers are the sole progress chrome
            // (Shneiderman: reduce STM load; Johnson: one locus for section state).
            BuildImportWizardMultiSelectHint(importSidebarScrollContentRT);

            Transform stepAtoms = CreateImportWizardStep(
                importSidebarScrollContentRT, 0,
                "gallery.import.wizard.step_atoms", "Atoms");
            BuildImportSidebarAtomRows(stepAtoms);

            Transform stepType = CreateImportWizardStep(
                importSidebarScrollContentRT, 1,
                "gallery.import.wizard.step_type", "Resource type");
            BuildImportSidebarTypeRadio(stepType);

            Transform stepOptions = CreateImportWizardStep(
                importSidebarScrollContentRT, 2,
                "gallery.import.wizard.step_options", "Options");
            BuildImportSidebarOptionsPanelHost(stepOptions);

            for (int i = 0; i < ImportWizardStepCount; i++)
                ApplyImportWizardStepCollapsedVisual(i);
            RefreshImportWizardStepHeaderGlyphs();
            RefreshTypeRadioVisibility();
            OnImportSidebarTypeChosen(importSidebarPresetType, true);
            RefreshSourceTypeAvailability();
        }

        private void RefreshImportSidebarWizardHeader()
        {
            string typeName = ImportSidebarSelectedTypesSummary();
            string targetName = importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "\u2014";

            int stepNum = 1;
            for (int i = 0; i < ImportWizardStepCount; i++)
            {
                if (!IsImportWizardStepCollapsed(i)) { stepNum = i + 1; break; }
                stepNum = i + 1;
            }

            string full = string.Format(
                VPBTranslation.T("gallery.import.wizard.header_summary_steps", "Import {0}/{1}  {2} \u2192 {3}"),
                stepNum, ImportWizardStepCount, typeName, targetName);

            // One title locus: docked header chip OR float title bar — never both.
            if (importSidebarDetached)
            {
                if (importSidebarFloatTitleLabel != null)
                    importSidebarFloatTitleLabel.text = full;
            }
            else if (importSidebarHeaderLabel != null)
            {
                ApplyImportSidebarHeaderLabelText(full);
            }

            RefreshImportWizardStepHeaderGlyphs();
            RefreshImportWizardBannerHint();
        }

        /// <summary>Multi-scene block OR float outside Scenes (source locked).</summary>
        private void RefreshImportWizardBannerHint()
        {
            if (_importWizardMultiSelectHint == null) return;

            bool multi = selectedFiles != null && selectedFiles.Count > 1;
            bool scenesLocked = importSidebarActive && importSidebarDetached && !ImportSidebarCategoryAllowed();

            if (multi)
            {
                _importWizardMultiSelectHint.gameObject.SetActive(true);
                _importWizardMultiSelectHint.color = new Color(1f, 0.75f, 0.45f, 1f);
                _importWizardMultiSelectHint.text = VPBTranslation.T(
                    "gallery.import.wizard.multi_select",
                    "Select only one scene package in the grid to import.");
                return;
            }

            if (scenesLocked)
            {
                _importWizardMultiSelectHint.gameObject.SetActive(true);
                _importWizardMultiSelectHint.color = ImportSidebarScenesLockedBanner;
                _importWizardMultiSelectHint.text = VPBTranslation.T(
                    "gallery.import.wizard.scenes_locked",
                    "Source locked — return to Scenes to change scene/person.");
                return;
            }

            _importWizardMultiSelectHint.gameObject.SetActive(false);
        }

        private bool ImportSidebarMultiSelectBlocked()
        {
            return selectedFiles != null && selectedFiles.Count > 1;
        }
    }
}

