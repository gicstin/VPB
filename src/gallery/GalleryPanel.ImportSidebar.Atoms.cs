using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private VpbImportSourceKind importSidebarSourceKind;
        private readonly VpbImportReadQueue importSidebarReadQueue = new VpbImportReadQueue();
        private VpbImportReadRequest importSidebarReadRequest;
        private string importSidebarSourceError;
        private JSONClass importSidebarPresetView;
        private string importSidebarPresetViewAtomId;
        private Text importSidebarSourceCaption;
        private Text importSidebarRandomLabel;
        private bool ImportSidebarHasSceneSource { get { return importSidebarSourceKind == VpbImportSourceKind.Scene; } }

        private JSONClass GetImportSidebarPresetView()
        {
            if (importSidebarPresetView == null || importSidebarPresetViewAtomId != importSidebarSourceAtomId)
            {
                importSidebarPresetView = VpbImportSource.Preset(importSidebarLoadedSceneJSON, importSidebarSourceKind, importSidebarSourceAtomId);
                importSidebarPresetViewAtomId = importSidebarSourceAtomId;
            }
            return importSidebarPresetView;
        }

        private Transform importSidebarSourceListContainer;
        private Transform importSidebarTargetListContainer;

        private const int ImportSidebarMaxRowsPerList = 32;
        private readonly List<GameObject> importSidebarSourceRowPool = new List<GameObject>(ImportSidebarMaxRowsPerList);
        private readonly List<GameObject> importSidebarTargetRowPool = new List<GameObject>(ImportSidebarMaxRowsPerList);

        private sealed class ImportSidebarGenderSlot
        {
            public Image Icon;
            public RectTransform IconRT;
            public RectTransform LabelRT;
        }
        private readonly List<ImportSidebarGenderSlot> importSidebarSourceRowGenderSlots =
            new List<ImportSidebarGenderSlot>(ImportSidebarMaxRowsPerList);
        private readonly List<ImportSidebarGenderSlot> importSidebarTargetRowGenderSlots =
            new List<ImportSidebarGenderSlot>(ImportSidebarMaxRowsPerList);

        private bool importSidebarTargetRefreshQueued;
        private Coroutine importSidebarTargetRefreshCo;
        private int importSidebarLastLoggedPersonCount = -1;

        private void BuildImportSidebarAtomRows(Transform content)
        {
            if (content == null) return;

            GameObject rndRow = new GameObject("RandomSceneRow");
            rndRow.transform.SetParent(content, false);
            LayoutElement rndLe = UI.AddLE(rndRow, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);
            Image rndBg = AddImportSidebarRoundedBg(rndRow, ImportSidebarSecondaryActionBg);
            Button rndBtn = rndRow.AddComponent<Button>();
            rndBtn.targetGraphic = rndBg;
            UI.NeutralizeSelectableColorTint(rndBtn);
            UI.EnsureFloatChromeHoverBorder(rndRow, inward: true);
            Text rndLabel = CreateImportSidebarLabel(
                rndRow.transform,
                VPBTranslation.T("gallery.import.wizard.random_scene", "\u21ba  Random Scene"),
                ImportSidebarBaseFontSize);
            importSidebarRandomLabel = rndLabel;
            rndLabel.alignment = TextAnchor.MiddleCenter;
            rndBtn.onClick.AddListener(OnImportSidebarRandomSceneClicked);
            AddTooltip(rndRow, "gallery.import.wizard.random_scene_tip",
                "Pick and import a random source from the current Scenes or Appearances grid");
            LayoutElement rndLeCaptured = rndLe;
            Text rndLabelCaptured = rndLabel;
            innerPaneScaleActions.Add(s => {
                if (rndLeCaptured != null) rndLeCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(rndLabelCaptured, ImportSidebarBaseFontSize, s);
            });

            importSidebarSourceCaption = AddImportListCaption(content, VPBTranslation.T("gallery.import.source_list_caption", "Source (from scene)"));
            importSidebarSourceListContainer = content;
            for (int i = 0; i < ImportSidebarMaxRowsPerList; i++)
                importSidebarSourceRowPool.Add(CreateImportSidebarAtomRow(content, i, true));

            AddImportListCaption(content, VPBTranslation.T("gallery.import.target_list_caption", "Target (live atoms)"));
            importSidebarTargetListContainer = content;
            for (int i = 0; i < ImportSidebarMaxRowsPerList; i++)
                importSidebarTargetRowPool.Add(CreateImportSidebarAtomRow(content, i, false));

            foreach (GameObject go in importSidebarSourceRowPool) go.SetActive(false);
            foreach (GameObject go in importSidebarTargetRowPool) go.SetActive(false);
        }

        private Text AddImportListCaption(Transform parent, string label)
        {
            Text t = UI.CreateLabel(parent.gameObject, label, ImportSidebarBaseFontSize, UI.PopupMutedText, TextAnchor.MiddleLeft, raycastTarget: false, name: "Caption");
            LayoutElement le = UI.AddLE(t.gameObject, preferredHeight: ImportSidebarBaseRowHeight * 0.7f, flexibleWidth: 1f);

            Text tCaptured = t;
            LayoutElement leCaptured = le;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * 0.7f * s;
                ApplyScaledFont(tCaptured, ImportSidebarBaseFontSize, s);
            });
            return t;
        }

        private GameObject CreateImportSidebarAtomRow(Transform parent, int index, bool isSource)
        {
            GameObject row = new GameObject("AtomRow_" + index);
            row.transform.SetParent(parent, false);

            LayoutElement le = UI.AddLE(row, preferredHeight: ImportSidebarBaseRowHeight, flexibleWidth: 1f);

            Image bg = AddImportSidebarRoundedBg(row, ColorInactiveRow);

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);
            UI.EnsureFloatChromeHoverBorder(row, inward: true);

            Text label = CreateImportSidebarLabel(row.transform, "", ImportSidebarBaseFontSize);

            ImportSidebarGenderSlot genderSlot = CreateImportSidebarRowGenderSlot(row.transform, label);
            (isSource ? importSidebarSourceRowGenderSlots : importSidebarTargetRowGenderSlots).Add(genderSlot);

            int capturedIndex = index;
            bool capturedIsSource = isSource;
            btn.onClick.AddListener(() => OnImportSidebarAtomRowClicked(capturedIndex, capturedIsSource));
            Text atomTipLabel = label;
            AddDynamicTooltip(row, () => ImportAtomRowTooltip(atomTipLabel, capturedIsSource));

            LayoutElement leCaptured = le;
            Text txtCaptured = label;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        private ImportSidebarGenderSlot CreateImportSidebarRowGenderSlot(Transform row, Text label)
        {
            GameObject go = new GameObject("GenderBadge");
            go.transform.SetParent(row, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;

            ImportSidebarGenderSlot slot = new ImportSidebarGenderSlot();
            slot.Icon = img;
            slot.IconRT = rt;
            slot.LabelRT = label != null ? label.GetComponent<RectTransform>() : null;
            go.SetActive(false);

            ImportSidebarGenderSlot captured = slot;
            innerPaneScaleActions.Add(s => ApplyImportSidebarGenderSlotLayout(captured, s));
            ApplyImportSidebarGenderSlotLayout(slot, ChromeScale);
            return slot;
        }

        private static void ApplyImportSidebarGenderSlotLayout(ImportSidebarGenderSlot slot, float s)
        {
            if (slot == null) return;
            if (s <= 0f) s = 1f;
            float pad = GalleryUiDesignTokens.ImportSidebarLabelPadLeftRef * s;
            float iconPad = GalleryUiDesignTokens.FloatChromeIconPadRef * s;
            float size = Mathf.Max(0f, GalleryUiDesignTokens.PersonGenderBadgeRef * s - iconPad * 2f);

            if (slot.IconRT != null)
            {
                slot.IconRT.sizeDelta = new Vector2(size, size);
                slot.IconRT.anchoredPosition = new Vector2(pad, 0f);
            }
            if (slot.LabelRT != null)
            {
                bool shown = slot.Icon != null && slot.Icon.gameObject.activeSelf;
                float left = shown ? pad + size + GalleryUiDesignTokens.TightGapRef * s : pad;
                slot.LabelRT.offsetMin = new Vector2(left, 0f);
            }
        }

        private void SetImportSidebarRowGender(List<ImportSidebarGenderSlot> slots, int index, LooseVapGenderProbe.Gender gender)
        {
            if (slots == null || index < 0 || index >= slots.Count) return;
            ImportSidebarGenderSlot slot = slots[index];
            if (slot == null || slot.Icon == null) return;

            Sprite spr = UI.LoadGenderIconSprite(gender);
            if (spr == null)
            {
                if (slot.Icon.gameObject.activeSelf) slot.Icon.gameObject.SetActive(false);
            }
            else
            {
                UI.SetIconSprite(slot.Icon, spr);
                if (!slot.Icon.gameObject.activeSelf) slot.Icon.gameObject.SetActive(true);
            }
            ApplyImportSidebarGenderSlotLayout(slot, ChromeScale);
        }

        private static LooseVapGenderProbe.Gender ImportSidebarSourceGenderAt(List<int> genders, int index)
        {
            if (genders == null || index < 0 || index >= genders.Count) return LooseVapGenderProbe.Gender.Unknown;
            int g = genders[index];
            if (g < (int)LooseVapGenderProbe.Gender.Unknown || g > (int)LooseVapGenderProbe.Gender.Futa)
                return LooseVapGenderProbe.Gender.Unknown;
            return (LooseVapGenderProbe.Gender)g;
        }

        partial void SubscribeToAtomEvents()
        {
            if (SuperController.singleton == null)
            {
                // Early startup (build before SuperController exists): can't subscribe yet, so the very first scene load would be missed.
                LogUtil.Log("[VPB import][diag] SubscribeToAtomEvents skipped: SuperController.singleton null");
                return;
            }
            SuperController.singleton.onAtomAddedHandlers -= OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomAddedHandlers += OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers -= OnImportSidebarAtomRemoved;
            SuperController.singleton.onAtomRemovedHandlers += OnImportSidebarAtomRemoved;
            SuperController.singleton.onSceneLoadedHandlers -= OnImportSidebarSceneLoaded;
            SuperController.singleton.onSceneLoadedHandlers += OnImportSidebarSceneLoaded;
            if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) LogUtil.Log("[VPB import][diag] subscribed atom/scene handlers");
        }

        private void OnImportSidebarSceneLoaded()
        {
            // Log BEFORE the guard so the log proves whether the handler fires at all (the open question for issue #2).
            if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) LogUtil.Log($"[VPB import][diag] onSceneLoaded fired; built={importSidebarBuilt} persons={CountLivePersonAtoms()}");
            if (!importSidebarBuilt) return;
            RefreshTargetCandidatesImmediate();
            RefreshApplyButtonEnabled();
            StartCoroutine(DeferredTargetRefreshAfterSceneLoad());
        }

        private System.Collections.IEnumerator DeferredTargetRefreshAfterSceneLoad()
        {
            // VaM may not expose the new scene's atoms via GetAtoms() immediately after onSceneLoaded fires.
            for (int attempt = 0; attempt < 36; attempt++)
            {
                for (int f = 0; f < 5; f++) yield return null;
                if (!importSidebarBuilt) yield break;
                int persons = CountLivePersonAtoms();
                if (attempt == 0 || persons != importSidebarLastLoggedPersonCount)
                    { if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) LogUtil.Log($"[VPB import][diag] onSceneLoaded deferred refresh attempt={attempt}; persons={persons}"); }
                RefreshTargetCandidatesImmediate();
                RefreshApplyButtonEnabled();
                if (persons > 0) yield break;
            }
        }

        private static int CountLivePersonAtoms()
        {
            int n = 0;
            if (SuperController.singleton != null)
                foreach (Atom a in SuperController.singleton.GetAtoms())
                    if (a != null && a.type == "Person") n++;
            return n;
        }

        private void OnImportSidebarAtomAdded(Atom a)
        {
            // Target list is Person-only.
            if (!importSidebarBuilt) return;
            if (a != null && a.type != "Person") return;
            ScheduleRefreshTargetCandidates();
        }

        private void OnImportSidebarAtomRemoved(Atom a)
        {
            if (!importSidebarBuilt) return;
            if (a != null && a.type != "Person") return;
            if (importSidebarTargetAtom == a) importSidebarTargetAtom = null;
            ScheduleRefreshTargetCandidates();
        }

        private void ScheduleRefreshTargetCandidates()
        {
            if (importSidebarTargetRefreshQueued) return;
            importSidebarTargetRefreshQueued = true;
            if (importSidebarTargetRefreshCo != null)
                StopCoroutine(importSidebarTargetRefreshCo);
            importSidebarTargetRefreshCo = StartCoroutine(CoalescedRefreshTargetCandidates());
        }

        private System.Collections.IEnumerator CoalescedRefreshTargetCandidates()
        {
            yield return new WaitForEndOfFrame();
            importSidebarTargetRefreshQueued = false;
            importSidebarTargetRefreshCo = null;
            if (!importSidebarBuilt) yield break;
            RefreshTargetCandidatesImmediate();
            RefreshApplyButtonEnabled();
        }

        partial void RefreshTargetCandidates()
        {
            RefreshTargetCandidatesImmediate();
        }

        private void RefreshTargetCandidatesImmediate()
        {
            importSidebarTargetCandidates.Clear();
            if (SuperController.singleton != null)
            {
                foreach (Atom a in SuperController.singleton.GetAtoms())
                {
                    if (a == null) continue;
                    if (a.type == "Person") importSidebarTargetCandidates.Add(a);
                }
            }
            int n = importSidebarTargetCandidates.Count;
            if (n != importSidebarLastLoggedPersonCount)
            {
                importSidebarLastLoggedPersonCount = n;
                if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) LogUtil.Log("[VPB import][diag] RefreshTargetCandidates: " + n + " person(s)");
            }
            RenderTargetList();
        }

        private void TryAutoSelectTargetIfUnset()
        {
            if (importSidebarTargetAtom != null) return;
            if (!string.IsNullOrEmpty(importSidebarSourceAtomId))
            {
                foreach (Atom a in importSidebarTargetCandidates)
                {
                    if (a != null && string.Equals(a.uid, importSidebarSourceAtomId, StringComparison.Ordinal))
                    { importSidebarTargetAtom = a; return; }
                }
            }
            if (importSidebarTargetCandidates.Count == 1)
                importSidebarTargetAtom = importSidebarTargetCandidates[0];
        }

        private void RenderTargetList()
        {
            int n = importSidebarTargetCandidates.Count;

            TryAutoSelectTargetIfUnset();

            int pool = importSidebarTargetRowPool.Count;
            for (int i = 0; i < pool; i++)
            {
                GameObject row = importSidebarTargetRowPool[i];
                if (i < n)
                {
                    Atom a = importSidebarTargetCandidates[i];
                    SetImportSidebarRowText(row, a.uid);
                    SetImportSidebarRowGender(importSidebarTargetRowGenderSlots, i, AtomGenderUtils.ClassifyForBadge(a));
                    row.SetActive(true);
                }
                else if (i == n)
                {
                    SetImportSidebarRowText(row, "<New Person Atom>");
                    SetImportSidebarRowGender(importSidebarTargetRowGenderSlots, i, LooseVapGenderProbe.Gender.Unknown);
                    row.SetActive(true);
                }
                else
                {
                    SetImportSidebarRowGender(importSidebarTargetRowGenderSlots, i, LooseVapGenderProbe.Gender.Unknown);
                    row.SetActive(false);
                }
            }
            RefreshTargetSelectionVisual();
            RebuildImportSidebarContent();
        }

        partial void RefreshTargetSelectionVisual()
        {
            var sourceIds = new HashSet<string>(importSidebarSourcePersonIds, StringComparer.Ordinal);
            for (int i = 0; i < importSidebarTargetCandidates.Count && i < importSidebarTargetRowPool.Count; i++)
            {
                Atom a = importSidebarTargetCandidates[i];
                bool selected = importSidebarTargetAtom == a;
                bool matchHint = !selected && a != null && sourceIds.Contains(a.uid);
                SetImportSidebarRowSelected(importSidebarTargetRowPool[i], selected, matchHint);
            }
        }

        private void OnImportSidebarAtomRowClicked(int index, bool isSource)
        {
            if (importSidebarApplying) return;
            if (isSource && ImportSidebarSourceEditsLocked())
            {
                try
                {
                    ShowTemporaryStatus(VPBTranslation.T(
                        "gallery.import.wizard.scenes_locked",
                        "Source locked: return to Scenes or Appearances to select a source."), 2f);
                }
                catch { }
                return;
            }

            if (isSource)
            {
                if (!ImportSidebarHasSceneSource) return;
                if (index >= 0 && index < importSidebarSourcePersonIds.Count)
                {
                    importSidebarSourceAtomId = importSidebarSourcePersonIds[index];
                    RenderSourceList();
                    TryAutoSelectTargetIfUnset();
                    RefreshTargetSelectionVisual();
                }
            }
            else
            {
                if (index >= 0 && index < importSidebarTargetCandidates.Count)
                {
                    importSidebarTargetAtom = importSidebarTargetCandidates[index];
                    RefreshTargetSelectionVisual();
                }
                else if (index == importSidebarTargetCandidates.Count)
                {
                    SpawnNewPersonAndSelect();
                }
            }
            RefreshApplyButtonEnabled();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshSceneAtomChecklist();
            RefreshSourceTypeAvailability();
        }

        private void RenderSourceList()
        {
            if (importSidebarSourceCaption != null)
                importSidebarSourceCaption.text = ImportSidebarHasSceneSource ? "Source (from scene)" : "Source appearance";
            if (importSidebarRandomLabel != null)
                importSidebarRandomLabel.text = IsAppearanceCategoryTitle() ? "Random Appearance" : "Random Scene";
            if (!ImportSidebarHasSceneSource)
            {
                for (int i = 0; i < importSidebarSourceRowPool.Count; i++)
                {
                    GameObject row = importSidebarSourceRowPool[i];
                    row.SetActive(i == 0 && importSidebarSourceScene != null);
                    if (i != 0) continue;
                    SetImportSidebarRowText(row, importSidebarSourceScene != null ? importSidebarSourceScene.Name : "");
                    SetImportSidebarRowGender(importSidebarSourceRowGenderSlots, i, LooseVapGenderProbe.Gender.Unknown);
                    SetImportSidebarRowSelected(row, true);
                }
                RebuildImportSidebarContent();
                return;
            }
            int n = importSidebarSourcePersonIds.Count;

            if (n == 1 && string.IsNullOrEmpty(importSidebarSourceAtomId))
                importSidebarSourceAtomId = importSidebarSourcePersonIds[0];

            var targetUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Atom a in importSidebarTargetCandidates)
                if (a != null) targetUids.Add(a.uid);

            int pool = importSidebarSourceRowPool.Count;
            for (int i = 0; i < pool; i++)
            {
                GameObject row = importSidebarSourceRowPool[i];
                if (i < n)
                {
                    string pid = importSidebarSourcePersonIds[i];
                    SetImportSidebarRowText(row, pid);
                    SetImportSidebarRowGender(importSidebarSourceRowGenderSlots, i,
                        ImportSidebarSourceGenderAt(importSidebarSourceGenders, i));
                    bool sel = importSidebarSourceAtomId == pid;
                    SetImportSidebarRowSelected(row, sel, !sel && targetUids.Contains(pid));
                    row.SetActive(true);
                }
                else
                {
                    SetImportSidebarRowGender(importSidebarSourceRowGenderSlots, i, LooseVapGenderProbe.Gender.Unknown);
                    row.SetActive(false);
                }
            }
            // The container is the shared body-scroll content, so never SetActive(false) it (that would hide the target list + options too).
            RebuildImportSidebarContent();
        }

        partial void LoadSourceScene(FileEntry entry)
        {
            if (ImportSidebarSourceEditsLocked())
            {
                try
                {
                    ShowTemporaryStatus(VPBTranslation.T(
                        "gallery.import.wizard.scenes_locked",
                        "Source locked: return to Scenes or Appearances to select a source."), 2f);
                }
                catch { }
                return;
            }

            if (importSidebarApplying) return;
            CancelImportSceneJsonLoad();
            importSidebarSourceScene = entry;
            importSidebarSourceKind = entry != null && (AppearanceGenderClassifier.ResolveIsPresetAppearance(entry)
                || AppearanceGenderClassifier.ResolveIsCustomAppearance(entry))
                ? VpbImportSourceKind.Appearance : VpbImportSourceKind.Scene;
            if (!ImportSidebarHasSceneSource && importSidebarMultiSelectedTypes.Contains(VpbResourceType.Appearance))
                VpbImportSource.Select(importSidebarMultiSelectedTypes, VpbResourceType.Appearance);
            importSidebarReadRequest = null;
            importSidebarSourceError = null;
            importSidebarPresetView = null;
            importSidebarPresetViewAtomId = null;
            importSidebarLoadedSceneJSON = null;
            importSidebarSourcePersonIds.Clear();
            importSidebarSourceGenders.Clear();
            importSidebarSourceAtomId = null;
            importSidebarSourcePersonsPending = false;

            if (entry == null)
            {
                RenderSourceList();
                RefreshImportSidebarAfterSourceChange();
                return;
            }

            bool cached = ImportSidebarHasSceneSource && VpbLocalDatabase.TryReadSceneAtomIds(entry,
                importSidebarSourcePersonIds, importSidebarSourceGenders);
            if (importSidebarSourcePersonIds.Count > 0) importSidebarSourceAtomId = importSidebarSourcePersonIds[0];
            BeginImportSceneJsonLoad(entry, writePersonCache: !cached);
            RenderSourceList();
            RefreshImportSidebarAfterSourceChange();
        }

        private void RefreshImportSidebarAfterSourceChange()
        {
            TryAutoSelectTargetIfUnset();
            RefreshTargetSelectionVisual();
            RefreshApplyButtonEnabled();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshSceneAtomChecklist();
            RefreshSourceTypeAvailability();
        }

        private void CancelImportSceneJsonLoad()
        {
            importSidebarSceneJsonLoadGen++;
            importSidebarReadQueue.Cancel();
            importSidebarSceneJsonLoading = false;
            importSidebarSourcePersonsPending = false;
            if (importSidebarSceneJsonLoadCo != null)
            {
                try { StopCoroutine(importSidebarSceneJsonLoadCo); } catch { }
                importSidebarSceneJsonLoadCo = null;
            }
        }

        private void BeginImportSceneJsonLoad(FileEntry entry, bool writePersonCache)
        {
            if (entry == null || importSidebarLoadedSceneJSON != null || importSidebarSceneJsonLoading || importSidebarSourceError != null) return;
            try
            {
                VarFileEntry vfe = entry as VarFileEntry;
                VarPackage package = vfe != null ? vfe.Package : null;
                string path = Path.GetFullPath(package != null ? package.Path : entry.Path);
                FileInfo file = new FileInfo(path);
                importSidebarReadRequest = new VpbImportReadRequest {
                    Path = path, InternalPath = package != null ? vfe.InternalPath : null,
                    CodePage = package != null ? package.GetKnownZipNameCodePage() : 0,
                    Size = file.Length, WriteTicks = file.LastWriteTimeUtc.Ticks,
                    Generation = importSidebarSceneJsonLoadGen,
                    WritePersonCache = writePersonCache && ImportSidebarHasSceneSource
                };
                importSidebarSceneJsonLoading = true;
                importSidebarSourcePersonsPending = ImportSidebarHasSceneSource;
                importSidebarReadQueue.Submit(importSidebarReadRequest);
                importSidebarSceneJsonLoadCo = StartCoroutine(ImportSceneJsonLoadRoutine(entry, importSidebarReadRequest));
            }
            catch (Exception ex)
            {
                importSidebarSourceError = ex.Message;
                importSidebarSceneJsonLoading = false;
                importSidebarSourcePersonsPending = false;
                RefreshApplyButtonEnabled();
            }
        }

        private IEnumerator ImportSceneJsonLoadRoutine(FileEntry entry, VpbImportReadRequest request)
        {
            yield return null;
            VpbImportReadQueue.Result result;
            while ((result = importSidebarReadQueue.Take()) == null)
            {
                if (request.Generation != importSidebarSceneJsonLoadGen) yield break;
                yield return null;
            }
            if (request.Generation != importSidebarSceneJsonLoadGen || result.Request != request) yield break;
            Exception parseEx = result.Error;
            if (parseEx != null)
                LogUtil.LogWarning("[VPB import] Failed to parse source scene " + entry.Uid + ": " + parseEx.Message);
            importSidebarSourceError = parseEx != null ? parseEx.Message : null;
            importSidebarLoadedSceneJSON = result.Root;
            importSidebarSceneJsonLoading = false;
            importSidebarSceneJsonLoadCo = null;
            if (ImportSidebarHasSceneSource && result.Root != null)
                ExtractAndCachePersonAtomsFromLoadedScene(entry, request.WritePersonCache || ImportSidebarSourceGendersNeedProbe());
            if (!ImportSidebarHasSceneSource && GetImportSidebarPresetView() == null)
            {
                importSidebarSourceError = "Appearance contains no preset data";
                importSidebarLoadedSceneJSON = null;
            }
            importSidebarSourcePersonsPending = false;
            RenderSourceList();
            ApplyImportSidebarAfterSceneJsonReady();
        }

        private bool ImportSidebarSourceGendersNeedProbe()
        {
            if (importSidebarSourceGenders.Count != importSidebarSourcePersonIds.Count) return true;
            for (int i = 0; i < importSidebarSourceGenders.Count; i++)
                if (importSidebarSourceGenders[i] == VpbLocalDatabase.SceneAtomGenderNotComputed) return true;
            return false;
        }

        private void ExtractAndCachePersonAtomsFromLoadedScene(FileEntry entry, bool writePersonCache)
        {
            List<string> ids = new List<string>(4);
            List<int> genders = new List<int>(4);
            List<JSONClass> personNodes = new List<JSONClass>(4);
            if (importSidebarLoadedSceneJSON != null && importSidebarLoadedSceneJSON["atoms"] != null)
            {
                JSONArray atoms = importSidebarLoadedSceneJSON["atoms"].AsArray;
                if (atoms != null)
                {
                    for (int i = 0; i < atoms.Count; i++)
                    {
                        JSONClass a = atoms[i].AsObject;
                        if (a == null) continue;
                        if (a["type"] != null && a["type"].Value == "Person")
                        {
                            string pid = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                                ? a["id"].Value
                                : ("Person_" + i);
                            ids.Add(pid);
                            genders.Add(VpbLocalDatabase.SceneAtomGenderToPersist(a));
                            personNodes.Add(a);
                        }
                    }
                }
            }

            if (ids.Count == 0)
            {
                if (importSidebarSourcePersonIds.Count > 0) return;
                RenderSourceList();
                return;
            }

            importSidebarSourcePersonIds.Clear();
            importSidebarSourceGenders.Clear();
            importSidebarSourcePersonIds.AddRange(ids);
            importSidebarSourceGenders.AddRange(genders);
            if (string.IsNullOrEmpty(importSidebarSourceAtomId)
                || !importSidebarSourcePersonIds.Contains(importSidebarSourceAtomId))
                importSidebarSourceAtomId = importSidebarSourcePersonIds[0];
            if (writePersonCache && entry != null)
                VpbLocalDatabase.TryWriteSceneAtoms(entry, importSidebarSourcePersonIds, personNodes);
            RenderSourceList();
        }

        private void ApplyImportSidebarAfterSceneJsonReady()
        {
            TryAutoSelectTargetIfUnset();
            RefreshTargetSelectionVisual();
            RefreshApplyButtonEnabled();
            RefreshPluginChecklist();
            RefreshCUAChecklist();
            RefreshSceneAtomChecklist();
            RefreshSourceTypeAvailability();
            try { RebuildImportSidebarContent(); } catch { }
        }

        private IEnumerator WaitForImportSourceSceneReady(float timeoutSec)
        {
            float t = 0f;
            while ((importSidebarSceneJsonLoading || importSidebarSourcePersonsPending) && t < timeoutSec)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void OnImportSidebarRandomSceneClicked()
        {
            if (importSidebarApplying) return;
            if (ImportSidebarSourceEditsLocked())
            {
                if (!TryNavigateGalleryToScenes())
                {
                    try
                    {
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.import.wizard.scenes_locked",
                            "Source locked: return to Scenes or Appearances to select a source."), 2f);
                    }
                    catch { }
                    return;
                }
            }

            var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                ? currentFilteredFiles : lastFilteredFiles;
            if (pool == null || pool.Count == 0)
            {
                LogUtil.LogWarning("[VPB import] Random Scene: no scenes in pool.");
                return;
            }
            FileEntry pick = VpbRandomHistory.Pick(GetRandomHistoryScope(), pool, selectedPath, true);
            if (pick == null)
            {
                LogUtil.LogWarning("[VPB import] Random Scene: no usable scene in pool.");
                return;
            }
            StartCoroutine(ImportSidebarRandomSceneAndApplyRoutine(pick));
        }

        private IEnumerator ImportSidebarRandomSceneAndApplyRoutine(FileEntry pick)
        {
            if (pick == null) yield break;
            LoadSourceScene(pick);
            int generation = importSidebarSceneJsonLoadGen;

            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;
            selectedFiles.Add(pick);
            if (!string.IsNullOrEmpty(pick.Path)) selectedFilePaths.Add(pick.Path);
            selectedPath = pick.Path;
            SetHoverPath("");
            try { RefreshSelectionVisuals(); } catch { }

            yield return WaitForImportSourceSceneReady(30f);
            if (generation != importSidebarSceneJsonLoadGen) yield break;

            if (GetImportSidebarPresetView() == null)
            {
                LogUtil.LogWarning("[VPB import] Random Scene: no Person atoms in scene.");
                yield break;
            }

            OnImportSidebarApplyClicked();
        }

        private void SpawnNewPersonAndSelect()
        {
            if (SuperController.singleton == null) return;
            StartCoroutine(SpawnNewPersonCoroutine());
        }

        private System.Collections.IEnumerator SpawnNewPersonCoroutine()
        {
            List<string> existingUids = new List<string>();
            foreach (Atom a in SuperController.singleton.GetAtoms())
            {
                if (a != null) existingUids.Add(a.uid);
            }

            yield return SuperController.singleton.AddAtomByType("Person", "Person", false);
            yield return new WaitForEndOfFrame();

            foreach (Atom a in SuperController.singleton.GetAtoms())
            {
                if (a != null && a.type == "Person" && !existingUids.Contains(a.uid))
                {
                    importSidebarTargetAtom = a;
                    RefreshTargetSelectionVisual();
                    RefreshApplyButtonEnabled();
                    break;
                }
            }
        }

        private void SetImportSidebarRowText(GameObject row, string text)
        {
            Text t = row.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        private void SetImportSidebarRowSelected(GameObject row, bool selected, bool matchHint = false)
        {
            Color fill = selected
                ? ImportSidebarSelectedAccent
                : (matchHint ? ImportSidebarMatchHintColor : ColorInactiveRow);
            ApplyImportSidebarSelectableChrome(row, selected, fill);
        }

        private void UnsubscribeFromAtomEvents()
        {
            CancelImportSceneJsonLoad();
            importSidebarLoadedSceneJSON = null;
            importSidebarPresetView = null;
            importSidebarReadRequest = null;
            if (SuperController.singleton == null) return;
            SuperController.singleton.onAtomAddedHandlers -= OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers -= OnImportSidebarAtomRemoved;
            SuperController.singleton.onSceneLoadedHandlers -= OnImportSidebarSceneLoaded;
            importSidebarTargetRefreshQueued = false;
            if (importSidebarTargetRefreshCo != null)
            {
                try { StopCoroutine(importSidebarTargetRefreshCo); } catch { }
                importSidebarTargetRefreshCo = null;
            }
        }
    }
}
