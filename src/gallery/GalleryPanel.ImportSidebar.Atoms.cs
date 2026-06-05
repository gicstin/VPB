using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using MVR.FileManagement;

namespace VPB
{
    public partial class GalleryPanel
    {
        private Transform importSidebarSourceListContainer;
        private Transform importSidebarTargetListContainer;

        private const int ImportSidebarMaxRowsPerList = 16;
        private readonly List<GameObject> importSidebarSourceRowPool = new List<GameObject>(ImportSidebarMaxRowsPerList);
        private readonly List<GameObject> importSidebarTargetRowPool = new List<GameObject>(ImportSidebarMaxRowsPerList);

        // Adds the Source/Target captions + atom row pools directly into the single body-scroll content (which already
        // carries a VerticalLayoutGroup + ContentSizeFitter from CreateVScrollableContent). Rows toggle active to show.
        private void BuildImportSidebarAtomRows(Transform content)
        {
            if (content == null) return;

            AddImportListCaption(content, VPBTranslation.T("gallery.import.source_list_caption", "Source (from scene)"));
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

        // Caption row sized by LayoutElement (so the content VLG places it) with a scaled height + font.
        private void AddImportListCaption(Transform parent, string label)
        {
            GameObject go = new GameObject("Caption");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = ImportSidebarBaseRowHeight * 0.7f;
            le.flexibleWidth = 1f;

            Text t = go.AddComponent<Text>();
            t.text = label;
            t.color = UI.PopupMutedText;
            t.fontSize = ImportSidebarBaseFontSize;
            t.alignment = TextAnchor.MiddleLeft;
            VPBUiFont.ApplyTo(t);
            t.raycastTarget = false;

            Text tCaptured = t;
            LayoutElement leCaptured = le;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * 0.7f * s;
                ApplyScaledFont(tCaptured, ImportSidebarBaseFontSize, s);
            });
        }

        private GameObject CreateImportSidebarAtomRow(Transform parent, int index, bool isSource)
        {
            GameObject row = new GameObject("AtomRow_" + index);
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            // Row height matches the sidebar's tab-derived row convention so atoms read
            // at the same visual weight as Creator/Category rows on a normal panel.
            le.preferredHeight = ImportSidebarBaseRowHeight;
            le.flexibleWidth = 1f;

            Image bg = row.AddComponent<Image>();
            // ColorInactiveRow is the same tone Creator/Category rows use when not selected,
            // so a glance at the sidebar matches the look of the rest of the side-tab UI.
            bg.color = ColorInactiveRow;

            Button btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            UI.NeutralizeSelectableColorTint(btn);

            Text label = CreateImportSidebarLabel(row.transform, "", ImportSidebarBaseFontSize);

            int capturedIndex = index;
            bool capturedIsSource = isSource;
            btn.onClick.AddListener(() => OnImportSidebarAtomRowClicked(capturedIndex, capturedIsSource));

            // Track inner-pane scale: row height + font use the same scale+localScale trick
            // GalleryPanel.Tabs.cs uses, so the sidebar visually tracks the UI scale slider.
            LayoutElement leCaptured = le;
            Text txtCaptured = label;
            innerPaneScaleActions.Add(s => {
                if (leCaptured != null) leCaptured.preferredHeight = ImportSidebarBaseRowHeight * s;
                ApplyScaledFont(txtCaptured, ImportSidebarBaseFontSize, s);
            });
            return row;
        }

        partial void SubscribeToAtomEvents()
        {
            if (SuperController.singleton == null)
            {
                // Early startup (build before SuperController exists): can't subscribe yet, so the very first scene
                // load would be missed. The activate path re-ensures, but log the gap so it isn't a silent miss.
                LogUtil.Log("[VPB import][diag] SubscribeToAtomEvents skipped: SuperController.singleton null");
                return;
            }
            // Idempotent (-= then +=) so re-ensuring on every sidebar open can't double-subscribe. A full scene load
            // swaps the atom set without reliable per-atom callbacks, so onSceneLoaded is the catch-all refresh.
            SuperController.singleton.onAtomAddedHandlers -= OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomAddedHandlers += OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers -= OnImportSidebarAtomRemoved;
            SuperController.singleton.onAtomRemovedHandlers += OnImportSidebarAtomRemoved;
            SuperController.singleton.onSceneLoadedHandlers -= OnImportSidebarSceneLoaded;
            SuperController.singleton.onSceneLoadedHandlers += OnImportSidebarSceneLoaded;
            LogUtil.Log("[VPB import][diag] subscribed atom/scene handlers");
        }

        private void OnImportSidebarSceneLoaded()
        {
            // Log BEFORE the guard so the log proves whether the handler fires at all (the open question for issue #2).
            LogUtil.Log($"[VPB import][diag] onSceneLoaded fired; built={importSidebarBuilt} persons={CountLivePersonAtoms()}");
            if (!importSidebarBuilt) return;
            RefreshTargetCandidates();
            RefreshApplyButtonEnabled();
            StartCoroutine(DeferredTargetRefreshAfterSceneLoad());
        }

        private System.Collections.IEnumerator DeferredTargetRefreshAfterSceneLoad()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (!importSidebarBuilt) yield break;
            int deferred = CountLivePersonAtoms();
            LogUtil.Log($"[VPB import][diag] onSceneLoaded deferred refresh; persons now={deferred}");
            RefreshTargetCandidates();
            RefreshApplyButtonEnabled();
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
            // Newly-spawned atoms can flip the apply button's enabled state (e.g. user
            // opens the sidebar in an empty scene, then adds a Person via VaM's atom
            // creator). Refresh both candidates and the apply state to stay in sync
            // with OnImportSidebarAtomRemoved's behavior on removal.
            if (!importSidebarBuilt) return;
            RefreshTargetCandidates();
            RefreshApplyButtonEnabled();
        }

        private void OnImportSidebarAtomRemoved(Atom a)
        {
            if (!importSidebarBuilt) return;
            if (importSidebarTargetAtom == a) importSidebarTargetAtom = null;
            RefreshTargetCandidates();
            RefreshApplyButtonEnabled();
        }

        partial void RefreshTargetCandidates()
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
            LogUtil.Log("[VPB import][diag] RefreshTargetCandidates: " + importSidebarTargetCandidates.Count + " person(s)");
            RenderTargetList();
        }

        private void RenderTargetList()
        {
            int n = importSidebarTargetCandidates.Count;
            int pool = importSidebarTargetRowPool.Count;
            for (int i = 0; i < pool; i++)
            {
                GameObject row = importSidebarTargetRowPool[i];
                if (i < n)
                {
                    SetImportSidebarRowText(row, importSidebarTargetCandidates[i].uid);
                    row.SetActive(true);
                }
                else if (i == n)
                {
                    SetImportSidebarRowText(row, "<New Atom>");
                    row.SetActive(true);
                }
                else
                {
                    row.SetActive(false);
                }
            }
            RefreshTargetSelectionVisual();
            RebuildImportSidebarContent();  // row count changed -> recompute scroll content height
        }

        partial void RefreshTargetSelectionVisual()
        {
            for (int i = 0; i < importSidebarTargetCandidates.Count && i < importSidebarTargetRowPool.Count; i++)
            {
                bool selected = importSidebarTargetAtom == importSidebarTargetCandidates[i];
                SetImportSidebarRowSelected(importSidebarTargetRowPool[i], selected);
            }
        }

        private void OnImportSidebarAtomRowClicked(int index, bool isSource)
        {
            if (isSource)
            {
                if (index >= 0 && index < importSidebarSourcePersonIds.Count)
                {
                    importSidebarSourceAtomId = importSidebarSourcePersonIds[index];
                    RenderSourceList();
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
            // Source change reselects which plugins exist; target change re-evaluates the on-target sort.
            RefreshPluginChecklist();
        }

        private void RenderSourceList()
        {
            int n = importSidebarSourcePersonIds.Count;
            int pool = importSidebarSourceRowPool.Count;
            for (int i = 0; i < pool; i++)
            {
                GameObject row = importSidebarSourceRowPool[i];
                if (i < n)
                {
                    SetImportSidebarRowText(row, importSidebarSourcePersonIds[i]);
                    SetImportSidebarRowSelected(row, importSidebarSourceAtomId == importSidebarSourcePersonIds[i]);
                    row.SetActive(true);
                }
                else row.SetActive(false);
            }
            // The container is the shared body-scroll content, so never SetActive(false) it (that would hide the
            // target list + options too). Inactive source rows already collapse out of the VLG.
            RebuildImportSidebarContent();  // row count changed -> recompute scroll content height
        }

        partial void LoadSourceScene(FileEntry entry)
        {
            importSidebarSourceScene = entry;
            importSidebarLoadedSceneJSON = null;
            importSidebarSourcePersonIds.Clear();
            importSidebarSourceAtomId = null;

            if (entry == null)
            {
                RenderSourceList();
                return;
            }

            // Cache HIT: load the person-atom ids from the DB and skip the full-scene parse. The selected
            // atom's JSON is fetched lazily at Apply. importSidebarLoadedSceneJSON stays null on this path.
            if (VpbLocalDatabase.TryReadSceneAtomIds(entry, importSidebarSourcePersonIds))
            {
                if (importSidebarSourcePersonIds.Count > 0)
                    importSidebarSourceAtomId = importSidebarSourcePersonIds[0];
                RenderSourceList();
                RefreshApplyButtonEnabled();
                RefreshPluginChecklist();
                return;
            }

            // MISS / STALE: parse the whole scene once (the one-time freeze), then write all person atoms
            // + the current sig so every later click is instant until the scene file changes.
            try
            {
                using (FileEntryStreamReader r = entry.OpenStreamReader())
                {
                    string raw = r.ReadToEnd();
                    JSONNode parsed = JSON.Parse(raw);
                    importSidebarLoadedSceneJSON = parsed != null ? parsed.AsObject : null;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB import] Failed to read source scene " + entry.Uid + ": " + ex.Message);
            }

            List<JSONClass> personNodes = new List<JSONClass>(4);
            if (importSidebarLoadedSceneJSON != null && importSidebarLoadedSceneJSON["atoms"] != null)
            {
                JSONArray atoms = importSidebarLoadedSceneJSON["atoms"].AsArray;
                for (int i = 0; i < atoms.Count; i++)
                {
                    JSONClass a = atoms[i].AsObject;
                    if (a == null) continue;
                    if (a["type"] != null && a["type"].Value == "Person")
                    {
                        // Index-suffixed fallback keeps each row uniquely selectable when a malformed
                        // scene omits the id (normally non-empty), instead of collapsing to one "Person" row.
                        string pid = (a["id"] != null && !string.IsNullOrEmpty(a["id"].Value))
                            ? a["id"].Value
                            : ("Person_" + i);
                        importSidebarSourcePersonIds.Add(pid);
                        personNodes.Add(a);
                    }
                }
                if (importSidebarSourcePersonIds.Count > 0)
                {
                    importSidebarSourceAtomId = importSidebarSourcePersonIds[0];
                    VpbLocalDatabase.TryWriteSceneAtoms(entry, importSidebarSourcePersonIds, personNodes);
                }
            }
            RenderSourceList();
            RefreshApplyButtonEnabled();
            RefreshPluginChecklist();
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

        private void SetImportSidebarRowSelected(GameObject row, bool selected)
        {
            Image bg = row.GetComponent<Image>();
            // Active row uses ColorCategory (the same red Creator/Category rows use when selected),
            // so the user reads the active selection at a glance with no new visual vocabulary.
            if (bg != null) bg.color = selected ? ColorCategory : ColorInactiveRow;
        }

        private void UnsubscribeFromAtomEvents()
        {
            if (SuperController.singleton == null) return;
            SuperController.singleton.onAtomAddedHandlers -= OnImportSidebarAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers -= OnImportSidebarAtomRemoved;
            SuperController.singleton.onSceneLoadedHandlers -= OnImportSidebarSceneLoaded;
        }
    }
}
