using System;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int ImportWizardStepCount = 4;
        private readonly bool[] _importWizardStepOpen = { true, true, true, true };
        private readonly GameObject[] _importWizardStepHeaders = new GameObject[ImportWizardStepCount];
        private readonly Transform[] _importWizardStepContents = new Transform[ImportWizardStepCount];
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

            GameObject header = UI.CreateUIButton(
                block, 0f, ImportSidebarBaseRowHeight * 0.85f,
                "", ImportSidebarBaseFontSize, 0f, 0f, AnchorPresets.stretchAll,
                () => ToggleImportWizardStep(stepIndex));
            header.name = "StepHeader";
            Image hdrBg = header.GetComponent<Image>();
            if (hdrBg != null) hdrBg.color = ImportSidebarStepHeaderBg;
            Text hdrTxt = header.GetComponentInChildren<Text>();
            if (hdrTxt != null)
            {
                hdrTxt.alignment = TextAnchor.MiddleLeft;
                hdrTxt.color = new Color(0.92f, 0.94f, 0.98f, 1f);
                hdrTxt.text = (stepIndex + 1) + ". " + VPBTranslation.T(ImportWizardStepTitleKeys[stepIndex], ImportWizardStepTitleDefaults[stepIndex]);
                RectTransform hdrTxtRt = hdrTxt.GetComponent<RectTransform>();
                if (hdrTxtRt != null)
                {
                    hdrTxtRt.offsetMin = new Vector2(ImportSidebarInnerPadHRef, 0f);
                    hdrTxtRt.offsetMax = new Vector2(-ImportSidebarInnerPadHRef, 0f);
                }
            }
            LayoutElement hdrLe = header.GetComponent<LayoutElement>();
            if (hdrLe == null) hdrLe = header.AddComponent<LayoutElement>();
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
            _importWizardStepContents[stepIndex] = content.transform;
            content.SetActive(_importWizardStepOpen[stepIndex]);
            return content.transform;
        }

        private void ToggleImportWizardStep(int stepIndex)
        {
            if (stepIndex < 0 || stepIndex >= ImportWizardStepCount) return;
            _importWizardStepOpen[stepIndex] = !_importWizardStepOpen[stepIndex];
            if (_importWizardStepContents[stepIndex] != null)
                _importWizardStepContents[stepIndex].gameObject.SetActive(_importWizardStepOpen[stepIndex]);
            RefreshImportWizardStepHeaderGlyphs();
            try { RebuildImportSidebarContent(); } catch { }
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
                string glyph = _importWizardStepOpen[i] ? "\u25BC " : "\u25B6 ";
                t.text = glyph + (i + 1) + ". " + title;
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
            _importWizardMultiSelectHint.fontSize = ImportSidebarBaseFontSize - 2;
            _importWizardMultiSelectHint.alignment = TextAnchor.MiddleLeft;
            try { VPBUiFont.ApplyTo(_importWizardMultiSelectHint); } catch { }
            _importWizardMultiSelectHint.raycastTarget = false;
            hintRow.SetActive(false);
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
        }

        private void RefreshImportSidebarWizardHeader()
        {
            if (importSidebarHeaderLabel == null) return;

            string typeName = ShortNameForType(importSidebarPresetType);
            string targetName = importSidebarTargetAtom != null ? importSidebarTargetAtom.uid : "?";
            string sourceName = !string.IsNullOrEmpty(importSidebarSourceAtomId)
                ? importSidebarSourceAtomId
                : (importSidebarSourceScene != null ? (importSidebarSourceScene.Uid ?? importSidebarSourceScene.Path ?? "?") : "?");

            importSidebarHeaderLabel.text = string.Format(
                VPBTranslation.T("gallery.import.wizard.header_summary", "{0} \u2192 {1} from {2}"),
                typeName, targetName, sourceName);

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
