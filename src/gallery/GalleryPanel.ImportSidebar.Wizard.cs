using System;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int ImportWizardStepCount = 4;
        private readonly GameObject[] _importWizardStepHeaders = new GameObject[ImportWizardStepCount];
        private Text _importWizardPackageLabel;
        private Text _importWizardMultiSelectHint;

        private Transform CreateImportWizardStep(Transform scrollContent, int stepIndex, string titleKey, string titleDefault)
        {
            if (scrollContent == null) return null;

            GameObject block = new GameObject("ImportWizardStep_" + stepIndex);
            block.transform.SetParent(scrollContent, false);
            LayoutElement blockLe = block.AddComponent<LayoutElement>();
            blockLe.flexibleWidth = 1f;

            VerticalLayoutGroup blockVlg = block.AddComponent<VerticalLayoutGroup>();
            blockVlg.spacing = 4f;
            blockVlg.childControlWidth = true;
            blockVlg.childControlHeight = true;
            blockVlg.childForceExpandWidth = true;
            blockVlg.childForceExpandHeight = false;

            GameObject header = new GameObject("StepHeader");
            header.transform.SetParent(block.transform, false);
            Image hdrBg = header.AddComponent<Image>();
            hdrBg.color = ImportSidebarStepHeaderBg;
            hdrBg.raycastTarget = false;

            GameObject hdrLabelGO = new GameObject("Label");
            hdrLabelGO.transform.SetParent(header.transform, false);
            RectTransform hdrLabelRT = hdrLabelGO.AddComponent<RectTransform>();
            hdrLabelRT.anchorMin = Vector2.zero;
            hdrLabelRT.anchorMax = Vector2.one;
            hdrLabelRT.offsetMin = new Vector2(ImportSidebarInnerPadHRef, 0f);
            hdrLabelRT.offsetMax = new Vector2(-ImportSidebarInnerPadHRef, 0f);
            Text hdrTxt = hdrLabelGO.AddComponent<Text>();
            hdrTxt.alignment = TextAnchor.MiddleLeft;
            hdrTxt.color = new Color(0.92f, 0.94f, 0.98f, 1f);
            hdrTxt.fontSize = ImportSidebarBaseFontSize;
            hdrTxt.text = (stepIndex + 1) + ". " + VPBTranslation.T(ImportWizardStepTitleKeys[stepIndex], ImportWizardStepTitleDefaults[stepIndex]);
            hdrTxt.raycastTarget = false;
            VPBUiFont.ApplyTo(hdrTxt);

            LayoutElement hdrLe = header.AddComponent<LayoutElement>();
            hdrLe.preferredHeight = ImportSidebarBaseRowHeight * 0.85f;
            hdrLe.flexibleWidth = 1f;
            _importWizardStepHeaders[stepIndex] = header;

            GameObject content = new GameObject("StepContent");
            content.transform.SetParent(block.transform, false);
            VerticalLayoutGroup contentVlg = content.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 3f;
            contentVlg.padding = new RectOffset(
                Mathf.RoundToInt(ImportSidebarInnerPadHRef),
                Mathf.RoundToInt(ImportSidebarInnerPadHRef), 0, 4);
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = true;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            LayoutElement contentLe = content.AddComponent<LayoutElement>();
            contentLe.flexibleWidth = 1f;
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

        private void BuildImportWizardPackageStep(Transform parent)
        {
            if (parent == null) return;

            GameObject row = new GameObject("PackageRow");
            row.transform.SetParent(parent, false);
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight;
            le.flexibleWidth = 1f;
            Image bg = row.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.2f, 0.9f);
            bg.raycastTarget = false;

            _importWizardPackageLabel = CreateImportSidebarLabel(
                row.transform,
                VPBTranslation.T("gallery.import.wizard.no_package", "(select a scene in the grid)"),
                ImportSidebarBaseFontSize);
            _importWizardPackageLabel.fontStyle = FontStyle.Italic;

            GameObject hintRow = new GameObject("MultiSelectHint");
            hintRow.transform.SetParent(parent, false);
            LayoutElement hintLe = hintRow.AddComponent<LayoutElement>();
            hintLe.preferredHeight = ImportSidebarBaseRowHeight * 0.75f;
            hintLe.flexibleWidth = 1f;
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

            Transform stepPackage = CreateImportWizardStep(
                importSidebarScrollContentRT, 0,
                "gallery.import.wizard.step_package", "Package");
            BuildImportWizardPackageStep(stepPackage);

            Transform stepAtoms = CreateImportWizardStep(
                importSidebarScrollContentRT, 1,
                "gallery.import.wizard.step_atoms", "Atoms");
            BuildImportSidebarAtomRows(stepAtoms);

            Transform stepType = CreateImportWizardStep(
                importSidebarScrollContentRT, 2,
                "gallery.import.wizard.step_type", "Resource type");
            BuildImportSidebarTypeRadio(stepType);

            Transform stepOptions = CreateImportWizardStep(
                importSidebarScrollContentRT, 3,
                "gallery.import.wizard.step_options", "Options");
            BuildImportSidebarOptionsPanelHost(stepOptions);

            RefreshImportWizardStepHeaderGlyphs();
            RefreshTypeRadioVisibility();
            OnImportSidebarTypeChosen(importSidebarPresetType);
            RefreshSourceTypeAvailability();
        }

        private void RefreshImportSidebarWizardHeader()
        {
            if (importSidebarHeaderLabel == null) return;

            string typeName = ImportSidebarSelectedTypesSummary();
            string targetName = importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "\u2014";

            importSidebarHeaderLabel.text = string.Format(
                VPBTranslation.T("gallery.import.wizard.header_summary", "Import  {0} \u2192 {1}"),
                typeName, targetName);

            if (_importWizardPackageLabel != null)
            {
                if (importSidebarSourceScene != null)
                {
                    _importWizardPackageLabel.fontStyle = FontStyle.Normal;
                    string pkgLabel = importSidebarSourceScene.Uid ?? importSidebarSourceScene.Path ?? "?";
                    try
                    {
                        if (!string.IsNullOrEmpty(importSidebarSourceScene.Path))
                            pkgLabel = System.IO.Path.GetFileName(importSidebarSourceScene.Path);
                    }
                    catch { }
                    _importWizardPackageLabel.text = pkgLabel;
                }
                else
                {
                    _importWizardPackageLabel.fontStyle = FontStyle.Italic;
                    _importWizardPackageLabel.text = VPBTranslation.T(
                        "gallery.import.wizard.no_package", "(select a scene in the grid)");
                }
            }

            bool multi = selectedFiles != null && selectedFiles.Count > 1;
            if (_importWizardMultiSelectHint != null)
            {
                _importWizardMultiSelectHint.transform.parent.gameObject.SetActive(multi);
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
