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
        private Text _importWizardMultiSelectHint;

        private Transform CreateImportWizardStep(Transform scrollContent, int stepIndex, string titleKey, string titleDefault)
        {
            if (scrollContent == null) return null;

            GameObject block = new GameObject("ImportWizardStep_" + stepIndex);
            block.transform.SetParent(scrollContent, false);
            LayoutElement blockLe = UI.AddLE(block, flexibleWidth: 1f);

            VerticalLayoutGroup blockVlg = UI.AddVLG(block, spacing: ImportSidebarBaseRowSpacing);

            GameObject header = new GameObject("StepHeader");
            header.transform.SetParent(block.transform, false);
            Image hdrBg = AddImportSidebarRoundedBg(header, ImportSidebarStepHeaderBg, raycastTarget: false);

            Text hdrTxt = UI.CreateLabel(header, (stepIndex + 1) + ". " + VPBTranslation.T(ImportWizardStepTitleKeys[stepIndex], ImportWizardStepTitleDefaults[stepIndex]), ImportSidebarBaseFontSize, new Color(0.92f, 0.94f, 0.98f, 1f), TextAnchor.MiddleLeft, raycastTarget: false, name: "Label");
            RectTransform hdrLabelRT = hdrTxt.GetComponent<RectTransform>();

            LayoutElement hdrLe = UI.AddLE(header, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            _importWizardStepHeaders[stepIndex] = header;

            // Step headers must track the inner-pane scale like every other label, otherwise
            // at scale > 1 they render at base size and read smaller than the rest of the UI.
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
            VerticalLayoutGroup contentVlg = UI.AddVLG(content, spacing: ImportSidebarBaseRowSpacing, padding: new RectOffset(0, 0, 0, Mathf.RoundToInt(ImportSidebarBaseRowSpacing * 0.5f)));
            LayoutElement contentLe = UI.AddLE(content, flexibleWidth: 1f);
            VerticalLayoutGroup contentVlgCaptured = contentVlg;
            innerPaneScaleActions.Add(s => {
                if (contentVlgCaptured == null) return;
                contentVlgCaptured.spacing = ImportSidebarBaseRowSpacing * s;
                contentVlgCaptured.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(ImportSidebarBaseRowSpacing * 0.5f * s));
            });
            content.SetActive(true);
            return content.transform;
        }

        private void RefreshImportWizardStepHeaderGlyphs()
        {
            for (int i = 0; i < ImportWizardStepCount; i++)
            {
                GameObject hdr = _importWizardStepHeaders[i];
                if (hdr == null) continue;
                Text t = hdr.GetComponentInChildren<Text>();
                if (t == null) continue;
                string title = VPBTranslation.T(ImportWizardStepTitleKeys[i], ImportWizardStepTitleDefaults[i]);
                t.text = (i + 1) + ". " + title;
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

            RefreshImportWizardStepHeaderGlyphs();
            RefreshTypeRadioVisibility();
            OnImportSidebarTypeChosen(importSidebarPresetType, true);
            RefreshSourceTypeAvailability();
        }

        private void RefreshImportSidebarWizardHeader()
        {
            if (importSidebarHeaderLabel == null) return;

            string typeName = ImportSidebarSelectedTypesSummary();
            string targetName = importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "\u2014";

            string full = string.Format(
                VPBTranslation.T("gallery.import.wizard.header_summary", "Import  {0} \u2192 {1}"),
                typeName, targetName);
            ApplyImportSidebarHeaderLabelText(full);

            bool multi = selectedFiles != null && selectedFiles.Count > 1;
            if (_importWizardMultiSelectHint != null)
            {
                _importWizardMultiSelectHint.gameObject.SetActive(multi);
                if (multi)
                {
                    _importWizardMultiSelectHint.text = VPBTranslation.T(
                        "gallery.import.wizard.multi_select",
                        "Select only one scene package in the grid to import.");
                }
            }
        }

        private bool ImportSidebarMultiSelectBlocked()
        {
            return selectedFiles != null && selectedFiles.Count > 1;
        }
    }
}
