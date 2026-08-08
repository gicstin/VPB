using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color RemapFloatTitleBarBg = new Color(0.10f, 0.22f, 0.34f, 1f);
        private static readonly Color RemapFloatFooterBarBg = new Color(0.08f, 0.10f, 0.13f, 1f);
        private static readonly Color RemapRowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color RemapRowExpandedBg = new Color(0.12f, 0.16f, 0.20f, 1f);
        private static readonly Color RemapRowUnresolvedBg = new Color(0.16f, 0.10f, 0.09f, 1f);
        private static readonly Color RemapExpandOptionBg = new Color(0.14f, 0.15f, 0.18f, 1f);
        private static readonly Color RemapExpandOptionActiveBg = new Color(0.18f, 0.36f, 0.30f, 1f);
        private static readonly Color RemapDestNormalBg = new Color(0.18f, 0.19f, 0.22f, 1f);
        private static readonly Color RemapDestSuggestedBg = new Color(0.14f, 0.22f, 0.28f, 1f);
        private static readonly Color RemapDestUnresolvedBg = new Color(0.26f, 0.16f, 0.13f, 1f);
        private static readonly Color RemapImportPrimaryBg = new Color(0.20f, 0.48f, 0.34f, 1f);
        private static readonly Color RemapImportArmedBg = new Color(0.48f, 0.36f, 0.16f, 1f);
        private static readonly Color RemapTitleEmphasis = new Color(1f, 1f, 1f, 1f);
        private static readonly Color RemapUnresolvedMuted = new Color(0.78f, 0.62f, 0.48f, 1f);
        private static readonly Color RemapAllMappedMuted = new Color(0.45f, 0.62f, 0.52f, 1f);

        private const float RemapFloatDefaultWRef = 820f;
        private const float RemapFloatDefaultHRef = 460f;
        private const float RemapFloatMinWRef = 520f;
        private const float RemapFloatMinHRef = 280f;
        private const float RemapFloatMaxWRef = 1100f;
        private const float RemapFloatMaxHRef = 900f;
        private const int RemapExpandFilterThreshold = 8;
        private const float RemapFooterBtnWRef = 96f;
        private const float RemapFooterImportBtnWRef = 118f;

        private GameObject _remapAtomUidsModalRoot;
        private RectTransform _remapAtomUidsPanelRT;
        private Transform _remapAtomUidsListParent;
        private List<SceneAtomImporter.BrokenUidRef> _remapAtomUidsRows;
        private readonly Dictionary<string, string> _remapAtomUidsChoices
            = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Original → auto-filled choice at open (suggested live / Create new).</summary>
        private readonly Dictionary<string, string> _remapAtomUidsAutoSuggested
            = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Original UID → chosen live plugin store id (primary receiver).</summary>
        private readonly Dictionary<string, string> _remapAtomUidsReceiverChoices
            = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _remapAtomUidsReceiverAutoSuggested
            = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, InputField> _remapAtomUidsInputs
            = new Dictionary<string, InputField>(StringComparer.Ordinal);
        private readonly Dictionary<string, InputField> _remapAtomUidsReceiverInputs
            = new Dictionary<string, InputField>(StringComparer.Ordinal);
        private readonly Dictionary<string, Image> _remapAtomUidsDestBgs
            = new Dictionary<string, Image>(StringComparer.Ordinal);
        private readonly Dictionary<string, Image> _remapAtomUidsReceiverBgs
            = new Dictionary<string, Image>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _remapAtomUidsRowShells
            = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _remapAtomUidsExpandHosts
            = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, Image> _remapAtomUidsRowBgs
            = new Dictionary<string, Image>(StringComparer.Ordinal);
        private readonly Dictionary<string, Image> _remapAtomUidsChevronIcons
            = new Dictionary<string, Image>(StringComparer.Ordinal);
        /// <summary>uidRemap, createNew, receiverRemapByOriginalUid.</summary>
        private Action<
            Dictionary<string, string>,
            HashSet<string>,
            Dictionary<string, Dictionary<string, string>>> _remapAtomUidsOnConfirm;
        private string _remapAtomUidsOpenPickerFor;
        /// <summary>When true, expand lists live plugin receivers for the open row's destination.</summary>
        private bool _remapAtomUidsExpandIsReceiver;
        private float _remapAtomUidsChromeScale = 1f;
        private Vector2? _remapAtomUidsSavedPosCenter;
        private Vector2? _remapAtomUidsSavedSizeRef;
        private Text _remapAtomUidsUnresolvedText;
        private Text _remapAtomUidsImportLabel;
        private Image _remapAtomUidsImportBg;
        private bool _remapAtomUidsImportForceArmed;
        private InputField _remapAtomUidsExpandFilterInput;
        private string _remapAtomUidsExpandFilter = string.Empty;
        private string _remapAtomUidsExpandSourceType;
        private bool _remapAtomUidsExpandCanCreate;
        private readonly List<string> _remapAtomUidsExpandSameType = new List<string>(16);
        private readonly List<string> _remapAtomUidsExpandOther = new List<string>(32);

        private static string RemapAtomUidsCreateNewLabel()
        {
            return VPBTranslation.T("gallery.import.remap_uids.create_new", "Create new");
        }

        private static bool RemapAtomUidsIsCreateNewChoice(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            if (string.Equals(stored, SceneAtomImporter.CreateNewUidSentinel, StringComparison.Ordinal))
                return true;
            return string.Equals(stored.Trim(), RemapAtomUidsCreateNewLabel(), StringComparison.OrdinalIgnoreCase);
        }

        private static string RemapAtomUidsChoiceToDisplay(string stored)
        {
            if (RemapAtomUidsIsCreateNewChoice(stored))
                return RemapAtomUidsCreateNewLabel();
            return stored ?? string.Empty;
        }

        private static string RemapAtomUidsDisplayToStored(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return string.Empty;
            string t = typed.Trim();
            if (string.Equals(t, RemapAtomUidsCreateNewLabel(), StringComparison.OrdinalIgnoreCase))
                return SceneAtomImporter.CreateNewUidSentinel;
            if (string.Equals(t, SceneAtomImporter.CreateNewUidSentinel, StringComparison.Ordinal))
                return SceneAtomImporter.CreateNewUidSentinel;
            return t;
        }

        internal bool IsRemapAtomUidsModalOpen
        {
            get { return _remapAtomUidsModalRoot != null && _remapAtomUidsModalRoot.activeInHierarchy; }
        }

        /// <summary>
        /// Power-user remap table: Original UID → live UID, typed name, or Create new (co-import donor).
        /// Optional Receiver column remaps trigger <c>plugin#N_Class</c> onto the live destination atom.
        /// Floating drag/resize chrome (filter-presets pattern). Esc / close / Cancel = cancel.
        /// Empty <paramref name="broken"/> still opens (Jakob: predictable import gate) with empty state;
        /// Enter / Import continues with no remaps.
        /// </summary>
        private void ShowRemapAtomUidsModal(
            List<SceneAtomImporter.BrokenUidRef> broken,
            Action<
                Dictionary<string, string>,
                HashSet<string>,
                Dictionary<string, Dictionary<string, string>>> onConfirm)
        {
            if (backgroundBoxGO == null || onConfirm == null)
            {
                if (onConfirm != null) onConfirm(null, null, null);
                return;
            }

            if (broken == null)
                broken = new List<SceneAtomImporter.BrokenUidRef>();

            _remapAtomUidsRows = broken;
            _remapAtomUidsOnConfirm = onConfirm;
            _remapAtomUidsChoices.Clear();
            _remapAtomUidsAutoSuggested.Clear();
            _remapAtomUidsReceiverChoices.Clear();
            _remapAtomUidsReceiverAutoSuggested.Clear();
            _remapAtomUidsImportForceArmed = false;
            _remapAtomUidsExpandIsReceiver = false;
            for (int i = 0; i < broken.Count; i++)
            {
                SceneAtomImporter.BrokenUidRef row = broken[i];
                if (string.IsNullOrEmpty(row.OriginalUid)) continue;
                // Live same-type unique → suggest remap; else Create new when donor still has the atom.
                if (!string.IsNullOrEmpty(row.SuggestedLiveUid))
                {
                    _remapAtomUidsChoices[row.OriginalUid] = row.SuggestedLiveUid;
                    _remapAtomUidsAutoSuggested[row.OriginalUid] = row.SuggestedLiveUid;
                }
                else if (row.CanCreateFromSource)
                {
                    _remapAtomUidsChoices[row.OriginalUid] = SceneAtomImporter.CreateNewUidSentinel;
                    _remapAtomUidsAutoSuggested[row.OriginalUid] = SceneAtomImporter.CreateNewUidSentinel;
                }
                else
                    _remapAtomUidsChoices[row.OriginalUid] = string.Empty;

                TryAutoSuggestRemapReceiver(row.OriginalUid, force: true);
            }

            HideRemapAtomUidsModal();
            // Hide clears UI refs only — choices / onConfirm / rows stay.
            _remapAtomUidsOnConfirm = onConfirm;
            _remapAtomUidsRows = broken;
            BuildRemapAtomUidsModal();
        }

        private void HideRemapAtomUidsModal()
        {
            CaptureRemapAtomUidsGeometryToMemory();
            PersistRemapAtomUidsGeometry();
            CollapseRemapAtomUidsExpand();
            if (_remapAtomUidsModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_remapAtomUidsModalRoot); } catch { }
                _remapAtomUidsModalRoot = null;
            }
            _remapAtomUidsPanelRT = null;
            _remapAtomUidsListParent = null;
            _remapAtomUidsUnresolvedText = null;
            _remapAtomUidsImportLabel = null;
            _remapAtomUidsImportBg = null;
            _remapAtomUidsImportForceArmed = false;
            _remapAtomUidsExpandIsReceiver = false;
            _remapAtomUidsInputs.Clear();
            _remapAtomUidsReceiverInputs.Clear();
            _remapAtomUidsDestBgs.Clear();
            _remapAtomUidsReceiverBgs.Clear();
            _remapAtomUidsRowShells.Clear();
            _remapAtomUidsExpandHosts.Clear();
            _remapAtomUidsRowBgs.Clear();
            _remapAtomUidsChevronIcons.Clear();
            _remapAtomUidsOpenPickerFor = null;
            _remapAtomUidsExpandFilterInput = null;
            _remapAtomUidsExpandFilter = string.Empty;
            _remapAtomUidsExpandSameType.Clear();
            _remapAtomUidsExpandOther.Clear();
        }

        private void CancelRemapAtomUidsModal()
        {
            _remapAtomUidsOnConfirm = null;
            HideRemapAtomUidsModal();
        }

        /// <summary>
        /// Esc ladder before InputField gate: clear expand filter → collapse picker → cancel float.
        /// </summary>
        internal bool TryHandleRemapAtomUidsEsc()
        {
            if (!IsRemapAtomUidsModalOpen) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;

            if (!string.IsNullOrEmpty(_remapAtomUidsOpenPickerFor))
            {
                if (_remapAtomUidsExpandFilterInput != null
                    && !string.IsNullOrEmpty(_remapAtomUidsExpandFilterInput.text))
                {
                    _remapAtomUidsExpandFilterInput.text = string.Empty;
                    _remapAtomUidsExpandFilter = string.Empty;
                    RebuildRemapAtomUidsExpandOptions();
                    return true;
                }
                CollapseRemapAtomUidsExpand();
                return true;
            }

            CancelRemapAtomUidsModal();
            return true;
        }

        /// <summary>
        /// Enter after InputField gate: collapse picker, else Import (soft-warn unresolved).
        /// </summary>
        internal bool TryHandleRemapAtomUidsEnter()
        {
            if (!IsRemapAtomUidsModalOpen) return false;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
                return false;

            if (!string.IsNullOrEmpty(_remapAtomUidsOpenPickerFor))
            {
                CollapseRemapAtomUidsExpand();
                return true;
            }

            ConfirmRemapAtomUidsModal();
            return true;
        }

        private void ConfirmRemapAtomUidsModal()
        {
            SyncRemapAtomUidsChoicesFromInputs();

            int unresolved = CountRemapAtomUidsAllGaps();
            if (unresolved > 0 && !_remapAtomUidsImportForceArmed)
            {
                _remapAtomUidsImportForceArmed = true;
                RefreshRemapAtomUidsUnresolvedUi();
                int destGaps = CountRemapAtomUidsUnresolved();
                int recvGaps = CountRemapAtomUidsUnresolvedReceivers();
                if (destGaps > 0 && recvGaps > 0)
                {
                    SetStatus(string.Format(
                        VPBTranslation.T(
                            "gallery.import.remap_uids.warn_unresolved_both",
                            "{0} dest + {1} receiver gap(s) — Import again to continue"),
                        destGaps, recvGaps));
                }
                else if (recvGaps > 0)
                {
                    SetStatus(string.Format(
                        VPBTranslation.T(
                            "gallery.import.remap_uids.warn_unresolved_receiver",
                            "{0} plugin receiver(s) unmatched — Import again to continue with gaps"),
                        recvGaps));
                }
                else
                {
                    SetStatus(string.Format(
                        VPBTranslation.T(
                            "gallery.import.remap_uids.warn_unresolved",
                            "{0} row(s) have no destination — Import again to continue with gaps"),
                        destGaps));
                }
                return;
            }

            _remapAtomUidsImportForceArmed = false;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var createNew = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in _remapAtomUidsChoices)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (RemapAtomUidsIsCreateNewChoice(kv.Value))
                {
                    createNew.Add(kv.Key);
                    continue;
                }
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)) continue;
                map[kv.Key] = kv.Value;
            }

            Dictionary<string, Dictionary<string, string>> receiverRemap
                = SceneAtomImporter.BuildReceiverRemapByUid(
                    _remapAtomUidsRows, _remapAtomUidsChoices, _remapAtomUidsReceiverChoices);

            var cb = _remapAtomUidsOnConfirm;
            _remapAtomUidsOnConfirm = null;
            HideRemapAtomUidsModal();
            if (cb != null)
            {
                cb(
                    map.Count > 0 ? map : null,
                    createNew.Count > 0 ? createNew : null,
                    receiverRemap != null && receiverRemap.Count > 0 ? receiverRemap : null);
            }
        }

        private void SyncRemapAtomUidsChoicesFromInputs()
        {
            foreach (KeyValuePair<string, InputField> kv in _remapAtomUidsInputs)
            {
                if (kv.Value == null) continue;
                string typed = kv.Value.text != null ? kv.Value.text.Trim() : string.Empty;
                _remapAtomUidsChoices[kv.Key] = RemapAtomUidsDisplayToStored(typed);
            }
            foreach (KeyValuePair<string, InputField> kv in _remapAtomUidsReceiverInputs)
            {
                if (kv.Value == null) continue;
                string typed = kv.Value.text != null ? kv.Value.text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(typed))
                    _remapAtomUidsReceiverChoices.Remove(kv.Key);
                else
                    _remapAtomUidsReceiverChoices[kv.Key] = typed;
            }
        }

        private int CountRemapAtomUidsUnresolved()
        {
            int n = 0;
            if (_remapAtomUidsRows == null) return 0;
            for (int i = 0; i < _remapAtomUidsRows.Count; i++)
            {
                string uid = _remapAtomUidsRows[i].OriginalUid;
                if (string.IsNullOrEmpty(uid)) continue;
                string choice;
                if (!_remapAtomUidsChoices.TryGetValue(uid, out choice) || string.IsNullOrEmpty(choice))
                    n++;
            }
            return n;
        }

        /// <summary>
        /// Plugin receivers that need a live dest storable and still have no auto/explicit match.
        /// Create new / empty dest → not counted (N/A).
        /// </summary>
        private int CountRemapAtomUidsUnresolvedReceivers()
        {
            int n = 0;
            if (_remapAtomUidsRows == null) return 0;
            for (int i = 0; i < _remapAtomUidsRows.Count; i++)
            {
                SceneAtomImporter.BrokenUidRef row = _remapAtomUidsRows[i];
                if (string.IsNullOrEmpty(row.OriginalUid)) continue;
                string dest;
                if (!_remapAtomUidsChoices.TryGetValue(row.OriginalUid, out dest))
                    dest = string.Empty;
                string explicitRecv;
                if (!_remapAtomUidsReceiverChoices.TryGetValue(row.OriginalUid, out explicitRecv))
                    explicitRecv = null;
                n += SceneAtomImporter.CountUnresolvedPluginReceivers(row, dest, explicitRecv);
            }
            return n;
        }

        private int CountRemapAtomUidsAllGaps()
        {
            return CountRemapAtomUidsUnresolved() + CountRemapAtomUidsUnresolvedReceivers();
        }

        private bool RemapAtomUidsChoiceStillSuggested(string originalUid, string stored)
        {
            if (string.IsNullOrEmpty(originalUid) || string.IsNullOrEmpty(stored)) return false;
            string auto;
            if (!_remapAtomUidsAutoSuggested.TryGetValue(originalUid, out auto) || string.IsNullOrEmpty(auto))
                return false;
            return string.Equals(stored, auto, StringComparison.Ordinal);
        }

        private bool RemapAtomUidsHasRows()
        {
            return _remapAtomUidsRows != null && _remapAtomUidsRows.Count > 0;
        }

        private void RefreshRemapAtomUidsUnresolvedUi()
        {
            int unresolvedDest = CountRemapAtomUidsUnresolved();
            int unresolvedRecv = CountRemapAtomUidsUnresolvedReceivers();
            int unresolved = unresolvedDest + unresolvedRecv;
            if (_remapAtomUidsUnresolvedText != null)
            {
                if (!RemapAtomUidsHasRows())
                {
                    _remapAtomUidsUnresolvedText.text = VPBTranslation.T(
                        "gallery.import.remap_uids.no_remaps_needed",
                        "No remaps needed");
                    _remapAtomUidsUnresolvedText.color = RemapAllMappedMuted;
                }
                else if (unresolved > 0)
                {
                    if (unresolvedDest > 0 && unresolvedRecv > 0)
                    {
                        _remapAtomUidsUnresolvedText.text = string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.unresolved_both",
                                "{0} dest · {1} receiver"),
                            unresolvedDest, unresolvedRecv);
                    }
                    else if (unresolvedRecv > 0)
                    {
                        _remapAtomUidsUnresolvedText.text = string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.unresolved_receiver",
                                "{0} without receiver"),
                            unresolvedRecv);
                    }
                    else
                    {
                        _remapAtomUidsUnresolvedText.text = string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.unresolved_count",
                                "{0} without destination"),
                            unresolvedDest);
                    }
                    _remapAtomUidsUnresolvedText.color = RemapUnresolvedMuted;
                }
                else
                {
                    _remapAtomUidsUnresolvedText.text = VPBTranslation.T(
                        "gallery.import.remap_uids.all_mapped",
                        "All mapped");
                    _remapAtomUidsUnresolvedText.color = RemapAllMappedMuted;
                }
            }

            if (_remapAtomUidsImportLabel != null)
            {
                _remapAtomUidsImportLabel.text = _remapAtomUidsImportForceArmed && unresolved > 0
                    ? VPBTranslation.T("gallery.import.remap_uids.import_anyway", "Import anyway")
                    : VPBTranslation.T("gallery.import.remap_uids.import", "Import");
            }
            if (_remapAtomUidsImportBg != null)
            {
                _remapAtomUidsImportBg.color = _remapAtomUidsImportForceArmed && unresolved > 0
                    ? RemapImportArmedBg
                    : RemapImportPrimaryBg;
            }

            if (_remapAtomUidsRows == null) return;
            for (int i = 0; i < _remapAtomUidsRows.Count; i++)
            {
                string uid = _remapAtomUidsRows[i].OriginalUid;
                if (string.IsNullOrEmpty(uid)) continue;
                SyncRemapAtomUidsRowDestVisual(uid);
                SyncRemapAtomUidsReceiverInputVisual(uid);
            }
        }

        private void SyncRemapAtomUidsRowDestVisual(string originalUid)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            string stored;
            if (!_remapAtomUidsChoices.TryGetValue(originalUid, out stored))
                stored = string.Empty;

            bool empty = string.IsNullOrEmpty(stored);
            bool suggested = RemapAtomUidsChoiceStillSuggested(originalUid, stored);
            bool expanded = string.Equals(_remapAtomUidsOpenPickerFor, originalUid, StringComparison.Ordinal);

            string recv;
            if (!_remapAtomUidsReceiverChoices.TryGetValue(originalUid, out recv)) recv = string.Empty;
            SceneAtomImporter.BrokenUidRef row = FindRemapAtomUidsRow(originalUid);
            bool destOk = !empty && !RemapAtomUidsIsCreateNewChoice(stored);
            int recvGaps = destOk
                ? SceneAtomImporter.CountUnresolvedPluginReceivers(row, stored, recv)
                : 0;
            bool recvGap = recvGaps > 0;

            Image rowBg;
            if (_remapAtomUidsRowBgs.TryGetValue(originalUid, out rowBg) && rowBg != null && !expanded)
            {
                if (empty || recvGap) rowBg.color = RemapRowUnresolvedBg;
                else rowBg.color = RemapRowBg;
            }

            Image destBg;
            if (_remapAtomUidsDestBgs.TryGetValue(originalUid, out destBg) && destBg != null)
            {
                if (empty) destBg.color = RemapDestUnresolvedBg;
                else if (suggested) destBg.color = RemapDestSuggestedBg;
                else destBg.color = RemapDestNormalBg;
            }
        }

        private void OnRemapAtomUidsChoiceEdited(string originalUid, string displayOrStored)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            _remapAtomUidsChoices[originalUid] = RemapAtomUidsDisplayToStored(displayOrStored);
            TryAutoSuggestRemapReceiver(originalUid, force: true);
            SyncRemapAtomUidsReceiverInputVisual(originalUid);
            if (_remapAtomUidsImportForceArmed && CountRemapAtomUidsAllGaps() == 0)
                _remapAtomUidsImportForceArmed = false;
            RefreshRemapAtomUidsUnresolvedUi();
        }

        private SceneAtomImporter.BrokenUidRef FindRemapAtomUidsRow(string originalUid)
        {
            SceneAtomImporter.BrokenUidRef empty = default(SceneAtomImporter.BrokenUidRef);
            if (_remapAtomUidsRows == null || string.IsNullOrEmpty(originalUid)) return empty;
            for (int i = 0; i < _remapAtomUidsRows.Count; i++)
            {
                if (string.Equals(_remapAtomUidsRows[i].OriginalUid, originalUid, StringComparison.Ordinal))
                    return _remapAtomUidsRows[i];
            }
            return empty;
        }

        private bool RemapAtomUidsRowHasPluginReceivers(string originalUid)
        {
            SceneAtomImporter.BrokenUidRef row = FindRemapAtomUidsRow(originalUid);
            return row.SourcePluginReceivers != null && row.SourcePluginReceivers.Count > 0;
        }

        /// <summary>
        /// Auto-fill Receiver from unique ClassName match on the current Destination live atom.
        /// </summary>
        private void TryAutoSuggestRemapReceiver(string originalUid, bool force)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            if (!RemapAtomUidsRowHasPluginReceivers(originalUid))
            {
                _remapAtomUidsReceiverChoices.Remove(originalUid);
                _remapAtomUidsReceiverAutoSuggested.Remove(originalUid);
                return;
            }

            string dest;
            if (!_remapAtomUidsChoices.TryGetValue(originalUid, out dest) || string.IsNullOrEmpty(dest))
            {
                if (force)
                {
                    _remapAtomUidsReceiverChoices.Remove(originalUid);
                    _remapAtomUidsReceiverAutoSuggested.Remove(originalUid);
                }
                return;
            }
            if (RemapAtomUidsIsCreateNewChoice(dest))
            {
                _remapAtomUidsReceiverChoices.Remove(originalUid);
                _remapAtomUidsReceiverAutoSuggested.Remove(originalUid);
                return;
            }

            if (!force)
            {
                string existing;
                if (_remapAtomUidsReceiverChoices.TryGetValue(originalUid, out existing)
                    && !string.IsNullOrEmpty(existing))
                {
                    string autoPrev;
                    if (!_remapAtomUidsReceiverAutoSuggested.TryGetValue(originalUid, out autoPrev)
                        || !string.Equals(existing, autoPrev, StringComparison.Ordinal))
                        return; // user override
                }
            }

            SceneAtomImporter.BrokenUidRef row = FindRemapAtomUidsRow(originalUid);
            string primary = row.SourcePluginReceivers[0];
            string url = null;
            if (row.SourcePluginReceiverUrls != null && row.SourcePluginReceiverUrls.Count > 0)
                url = row.SourcePluginReceiverUrls[0];
            string suggested = SceneAtomImporter.SuggestLiveReceiverStoreId(dest, primary, url);
            if (!string.IsNullOrEmpty(suggested))
            {
                _remapAtomUidsReceiverChoices[originalUid] = suggested;
                _remapAtomUidsReceiverAutoSuggested[originalUid] = suggested;
            }
            else if (force)
            {
                _remapAtomUidsReceiverChoices.Remove(originalUid);
                _remapAtomUidsReceiverAutoSuggested.Remove(originalUid);
            }
        }

        private void SyncRemapAtomUidsReceiverInputVisual(string originalUid)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            InputField input;
            if (!_remapAtomUidsReceiverInputs.TryGetValue(originalUid, out input) || input == null)
                return;

            bool hasPlugin = RemapAtomUidsRowHasPluginReceivers(originalUid);
            string dest;
            _remapAtomUidsChoices.TryGetValue(originalUid, out dest);
            bool destOk = !string.IsNullOrEmpty(dest) && !RemapAtomUidsIsCreateNewChoice(dest);
            bool enabled = hasPlugin && destOk;

            string recv;
            if (!_remapAtomUidsReceiverChoices.TryGetValue(originalUid, out recv)) recv = string.Empty;

            // Input stores raw primary choice; display suffix for multi is via placeholder/status only
            // when not focused — keep raw store id in field for edit/export.
            if (input.text != recv) input.text = recv ?? string.Empty;
            input.interactable = enabled;

            SceneAtomImporter.BrokenUidRef row = FindRemapAtomUidsRow(originalUid);
            int recvGaps = enabled
                ? SceneAtomImporter.CountUnresolvedPluginReceivers(row, dest, recv)
                : 0;

            Image bg;
            if (_remapAtomUidsReceiverBgs.TryGetValue(originalUid, out bg) && bg != null)
            {
                if (!enabled) bg.color = new Color(0.12f, 0.13f, 0.15f, 1f);
                else if (recvGaps > 0 || string.IsNullOrEmpty(recv)) bg.color = RemapDestUnresolvedBg;
                else if (RemapAtomUidsReceiverStillSuggested(originalUid, recv)) bg.color = RemapDestSuggestedBg;
                else bg.color = RemapDestNormalBg;
            }

            // Cue multi-plugin state on placeholder when empty / partial (change blindness).
            if (input.placeholder != null)
            {
                Text ph = input.placeholder as Text;
                if (ph != null)
                {
                    int srcCount = row.SourcePluginReceivers != null ? row.SourcePluginReceivers.Count : 0;
                    if (!enabled)
                        ph.text = string.Empty;
                    else if (srcCount > 1 && recvGaps > 0)
                        ph.text = string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.receiver_ph_multi",
                                "{0} plugin receivers — pick…"),
                            srcCount);
                    else if (hasPlugin)
                        ph.text = VPBTranslation.T(
                            "gallery.import.remap_uids.receiver_ph", "plugin#N_Class…");
                    else
                        ph.text = string.Empty;
                    ph.gameObject.SetActive(string.IsNullOrEmpty(recv) && enabled);
                }
            }
        }

        private bool RemapAtomUidsReceiverStillSuggested(string originalUid, string stored)
        {
            if (string.IsNullOrEmpty(originalUid) || string.IsNullOrEmpty(stored)) return false;
            string auto;
            if (!_remapAtomUidsReceiverAutoSuggested.TryGetValue(originalUid, out auto) || string.IsNullOrEmpty(auto))
                return false;
            return string.Equals(stored, auto, StringComparison.Ordinal);
        }

        private void OnRemapAtomUidsReceiverEdited(string originalUid, string typed)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            string t = typed != null ? typed.Trim() : string.Empty;
            if (string.IsNullOrEmpty(t))
                _remapAtomUidsReceiverChoices.Remove(originalUid);
            else
                _remapAtomUidsReceiverChoices[originalUid] = t;
            SyncRemapAtomUidsReceiverInputVisual(originalUid);
            if (_remapAtomUidsImportForceArmed && CountRemapAtomUidsAllGaps() == 0)
                _remapAtomUidsImportForceArmed = false;
            RefreshRemapAtomUidsUnresolvedUi();
        }

        private GameObject ResolveRemapAtomUidsFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        private void BuildRemapAtomUidsModal()
        {
            float s = ChromeScale;
            _remapAtomUidsChromeScale = s;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float colHdrH = Mathf.Max(26f * s, chromeSz * 0.85f);
            float rowH = chromeSz;

            LoadRemapAtomUidsGeometryFromConfig();
            float panelWRef = RemapFloatDefaultWRef;
            float panelHRef = RemapFloatDefaultHRef;
            if (_remapAtomUidsSavedSizeRef.HasValue)
            {
                panelWRef = Mathf.Clamp(_remapAtomUidsSavedSizeRef.Value.x, RemapFloatMinWRef, RemapFloatMaxWRef);
                panelHRef = Mathf.Clamp(_remapAtomUidsSavedSizeRef.Value.y, RemapFloatMinHRef, RemapFloatMaxHRef);
            }
            float panelW = panelWRef * s;
            float panelH = panelHRef * s;

            GameObject host = ResolveRemapAtomUidsFloatHost();
            if (host == null) return;

            _remapAtomUidsModalRoot = UI.CreateChildRT(host, "VPB_RemapAtomUidsFloat", AnchorPresets.stretchAll);
            try { SetLayerRecursiveLocal(_remapAtomUidsModalRoot, host.layer); } catch { }

            // Soft focus dim — blocks gallery; does not dismiss (independent float).
            GameObject dim = UI.CreateChildRT(_remapAtomUidsModalRoot, "Dim", AnchorPresets.stretchAll);
            Image dimImg = UI.AddImage(dim, new Color(0f, 0f, 0f, 0.45f));
            if (dimImg != null) dimImg.raycastTarget = true;

            GameObject panel = UI.CreateChildRT(
                _remapAtomUidsModalRoot, "Panel", AnchorPresets.middleCenter,
                new Vector2(panelW, panelH), Vector2.zero);
            UI.AddImage(panel, new Color(0.07f, 0.08f, 0.10f, 1f));
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();
            _remapAtomUidsPanelRT = panel.GetComponent<RectTransform>();
            if (_remapAtomUidsPanelRT != null)
            {
                // Top-left pivot — resize from footer handle keeps title fixed (filter-presets pattern).
                _remapAtomUidsPanelRT.pivot = new Vector2(0f, 1f);
                _remapAtomUidsPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _remapAtomUidsPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _remapAtomUidsPanelRT.sizeDelta = new Vector2(panelW, panelH);
                Vector2 center = _remapAtomUidsSavedPosCenter.HasValue
                    ? _remapAtomUidsSavedPosCenter.Value
                    : Vector2.zero;
                _remapAtomUidsPanelRT.anchoredPosition = RemapAtomUidsCenterToTopLeft(center, _remapAtomUidsPanelRT.sizeDelta);
            }

            // ── Title bar (drag) ───────────────────────────────────────────
            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBar, RemapFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            RectTransform titleRT = titleBar.GetComponent<RectTransform>();
            if (titleRT != null)
            {
                titleRT.pivot = new Vector2(0.5f, 1f);
                titleRT.anchoredPosition = Vector2.zero;
                titleRT.sizeDelta = new Vector2(0f, titleH);
            }
            UI.AddHLG(
                titleBar, spacing: 4f * s, padding: UI.Pad(6, 6, 4, 4, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null)
                titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "\u2807", font,
                new Color(0.65f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(grip.gameObject, minWidth: 18f * s, preferredWidth: 18f * s);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("gallery.import.remap_uids.title", "Remap Atom UIDs"),
                font, RemapTitleEmphasis, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 60f * s);

            GameObject closeBtn = RemapAtomUidsSquareIconButton(
                titleBar.transform, chromeSz, "vpb_icons/x.png",
                new Color(0f, 0f, 0f, 0.5f), CancelRemapAtomUidsModal);
            if (closeBtn != null)
            {
                closeBtn.name = "TitleClose";
                var closeHover = closeBtn.AddComponent<UIHoverDelegate>();
                closeHover.OnHoverChange += (enter) =>
                {
                    if (enter) SetStatus(VPBTranslation.T("gallery.import.remap_uids.tip_close", "Cancel remap"));
                    else SetStatus(null);
                };
            }

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = _remapAtomUidsPanelRT;
            headerDrag.OnMoved = OnRemapAtomUidsFloatMoved;

            // ── Column headers (Source → Destination) ─────────────────────
            GameObject colHdr = UI.CreateChildRT(panel, "ColHeader", AnchorPresets.hStretchTop,
                new Vector2(0f, colHdrH), new Vector2(0f, -titleH));
            RectTransform colRT = colHdr.GetComponent<RectTransform>();
            if (colRT != null)
            {
                colRT.pivot = new Vector2(0.5f, 1f);
                colRT.sizeDelta = new Vector2(0f, colHdrH);
                colRT.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddImage(colHdr, new Color(0.11f, 0.13f, 0.17f, 1f));
            HorizontalLayoutGroup ch = UI.AddHLG(
                colHdr, spacing: 6f * s, padding: UI.Pad(10, 10, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            RemapAtomUidsColLabel(colHdr.transform,
                VPBTranslation.T("gallery.import.remap_uids.col_source", "Source"),
                font, s, 1.0f, colHdrH - 8f * s);
            RemapAtomUidsArrowGlyph(colHdr.transform, chromeSz * 0.5f, s, font);
            RemapAtomUidsColLabel(colHdr.transform,
                VPBTranslation.T("gallery.import.remap_uids.col_destination", "Destination"),
                font, s, 1.2f, colHdrH - 8f * s);
            RemapAtomUidsColLabel(colHdr.transform,
                VPBTranslation.T("gallery.import.remap_uids.col_receiver", "Receiver"),
                font, s, 1.1f, colHdrH - 8f * s);

            // ── Footer ────────────────────────────────────────────────────
            GameObject footer = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(footer, RemapFloatFooterBarBg);
            RectTransform footerRT = footer.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                footer, spacing: 6f * s, padding: UI.Pad(8, 8, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (footer.GetComponent<RectMask2D>() == null)
                footer.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind Cancel/Import/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(footer);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = _remapAtomUidsPanelRT;
                footerDrag.OnMoved = OnRemapAtomUidsFloatMoved;
            }

            // Spacer pushes actions right (primary Import rightmost before resize).
            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(footer.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f * s);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = _remapAtomUidsPanelRT;
            spacerDrag.OnMoved = OnRemapAtomUidsFloatMoved;

            Text unresolvedTxt = UI.CreateLabel(
                footer,
                string.Empty,
                font, RemapAllMappedMuted, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Unresolved");
            unresolvedTxt.fontSize = font;
            unresolvedTxt.transform.localScale = Vector3.one;
            UI.AddLE(unresolvedTxt.gameObject, flexibleWidth: 0f, minWidth: 72f * s,
                preferredWidth: 160f * s, preferredHeight: chromeSz);
            _remapAtomUidsUnresolvedText = unresolvedTxt;

            RemapAtomUidsChromeButton(footer.transform, RemapFooterBtnWRef * s, chromeSz,
                VPBTranslation.T("gallery.import.remap_uids.cancel", "Cancel"), font, s,
                new Color(0.35f, 0.28f, 0.22f, 1f), CancelRemapAtomUidsModal, flexibleWidth: false);

            GameObject importBtn = RemapAtomUidsChromeButton(
                footer.transform, RemapFooterImportBtnWRef * s, chromeSz,
                VPBTranslation.T("gallery.import.remap_uids.import", "Import"), font, s,
                RemapImportPrimaryBg, ConfirmRemapAtomUidsModal, flexibleWidth: false);
            if (importBtn != null)
            {
                _remapAtomUidsImportBg = importBtn.GetComponent<Image>();
                _remapAtomUidsImportLabel = importBtn.GetComponentInChildren<Text>(true);
                var importHover = importBtn.AddComponent<UIHoverDelegate>();
                importHover.OnHoverChange += (enter) =>
                {
                    if (!enter) { SetStatus(null); return; }
                    if (!RemapAtomUidsHasRows())
                    {
                        SetStatus(VPBTranslation.T(
                            "gallery.import.remap_uids.tip_import_empty",
                            "No remaps needed — Import (Enter)"));
                        return;
                    }
                    int u = CountRemapAtomUidsAllGaps();
                    if (u > 0 && !_remapAtomUidsImportForceArmed)
                    {
                        SetStatus(string.Format(
                            VPBTranslation.T(
                                "gallery.import.remap_uids.tip_import_unresolved",
                                "Import — {0} gap(s) still empty (warns once)"),
                            u));
                    }
                    else
                    {
                        SetStatus(VPBTranslation.T(
                            "gallery.import.remap_uids.tip_import",
                            "Import with remaps (Enter)"));
                    }
                };
            }

            GameObject resizeHandle = UI.AddChildGOImage(
                footer, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chromeSz, chromeSz, Vector2.zero, rounded: true);
            resizeHandle.name = "ResizeHandle";
            Image rhImg = resizeHandle.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            LayoutElement rhLe = resizeHandle.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = resizeHandle.AddComponent<LayoutElement>();
            rhLe.minWidth = rhLe.preferredWidth = chromeSz;
            rhLe.minHeight = rhLe.preferredHeight = chromeSz;
            rhLe.flexibleWidth = 0f;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("vpb_icons/chevrons_down_right.png", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(resizeHandle, rhSpr, 5f * s, UI.IconButtonBackdrop);
            }
            catch { }
            var resizer = resizeHandle.AddComponent<UIFloatPanelResize>();
            resizer.Target = _remapAtomUidsPanelRT;
            resizer.GetMinSize = () => new Vector2(RemapFloatMinWRef * _remapAtomUidsChromeScale, RemapFloatMinHRef * _remapAtomUidsChromeScale);
            resizer.GetMaxSize = () => new Vector2(RemapFloatMaxWRef * _remapAtomUidsChromeScale, RemapFloatMaxHRef * _remapAtomUidsChromeScale);
            resizer.OnResized = OnRemapAtomUidsFloatResized;
            var rhHover = resizeHandle.AddComponent<UIHoverDelegate>();
            rhHover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T("gallery.import.remap_uids.tip_resize", "Drag to resize"));
                else SetStatus(null);
            };

            // ── Scroll list between col header + footer ───────────────────
            GameObject scrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            RectTransform scrollRT = scrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(0f, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + colHdrH));
            }
            UI.AddImage(scrollHost, new Color(0.05f, 0.055f, 0.07f, 1f));
            if (scrollHost.GetComponent<RectMask2D>() == null)
                scrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            ScrollRect sr = scrollHost.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;
            sr.verticalScrollbar = null;

            GameObject viewport = UI.CreateChildRT(scrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            sr.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(scrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1f, 0f);
                sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 0.5f);
                sbRT.offsetMin = new Vector2(-sbW, 0f);
                sbRT.offsetMax = Vector2.zero;
            }
            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = sr;
            sync.scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sync.minSizePixels = 20f;

            GameObject content = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            sr.content = contentRt;
            VerticalLayoutGroup cv = UI.AddVLG(content, spacing: 4f * s, padding: UI.Pad(6, 6, 6, 6, s));
            cv.childForceExpandHeight = false;
            cv.childForceExpandWidth = true;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _remapAtomUidsListParent = content.transform;

            RebuildRemapAtomUidsRows(font, s, rowH);
            RefreshRemapAtomUidsUnresolvedUi();
            _remapAtomUidsModalRoot.transform.SetAsLastSibling();
        }

        private void RebuildRemapAtomUidsRows(int font, float s, float rowH)
        {
            if (_remapAtomUidsListParent == null || _remapAtomUidsRows == null) return;
            for (int c = _remapAtomUidsListParent.childCount - 1; c >= 0; c--)
            {
                try { UnityEngine.Object.Destroy(_remapAtomUidsListParent.GetChild(c).gameObject); } catch { }
            }
            _remapAtomUidsInputs.Clear();
            _remapAtomUidsReceiverInputs.Clear();
            _remapAtomUidsDestBgs.Clear();
            _remapAtomUidsReceiverBgs.Clear();
            _remapAtomUidsRowShells.Clear();
            _remapAtomUidsExpandHosts.Clear();
            _remapAtomUidsRowBgs.Clear();
            _remapAtomUidsChevronIcons.Clear();
            _remapAtomUidsOpenPickerFor = null;

            if (_remapAtomUidsRows.Count == 0)
            {
                BuildRemapAtomUidsEmptyState(font, s, rowH);
                return;
            }

            for (int i = 0; i < _remapAtomUidsRows.Count; i++)
            {
                SceneAtomImporter.BrokenUidRef row = _remapAtomUidsRows[i];
                if (string.IsNullOrEmpty(row.OriginalUid)) continue;
                BuildRemapAtomUidsRow(row, font, s, rowH);
            }
        }

        /// <summary>
        /// Empty / success surface when import has no broken external UID refs.
        /// Keeps gate predictable; Enter confirms (Doherty + keyboard path).
        /// </summary>
        private void BuildRemapAtomUidsEmptyState(int font, float s, float rowH)
        {
            GameObject shell = new GameObject("RemapEmpty");
            shell.transform.SetParent(_remapAtomUidsListParent, false);
            VerticalLayoutGroup v = UI.AddVLG(shell, spacing: 6f * s, padding: UI.Pad(16, 16, 20, 20, s));
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childAlignment = TextAnchor.MiddleCenter;
            UI.AddLE(shell, minHeight: rowH * 3f, preferredHeight: rowH * 3.5f, flexibleWidth: 1f);

            Text title = UI.CreateLabel(
                shell,
                VPBTranslation.T(
                    "gallery.import.remap_uids.empty_title",
                    "No external UID remaps needed"),
                font, RemapAllMappedMuted, TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "EmptyTitle");
            GalleryUiMetrics.ApplyFont(title, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            title.fontStyle = FontStyle.Bold;
            UI.AddLE(title.gameObject, flexibleWidth: 1f, preferredHeight: rowH);

            Text hint = UI.CreateLabel(
                shell,
                VPBTranslation.T(
                    "gallery.import.remap_uids.empty_hint",
                    "Press Enter or Import to continue"),
                font, new Color(0.62f, 0.66f, 0.70f, 1f), TextAnchor.MiddleCenter,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "EmptyHint");
            GalleryUiMetrics.ApplyFont(hint, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(hint.gameObject, flexibleWidth: 1f, preferredHeight: rowH * 0.9f);
        }

        private void BuildRemapAtomUidsRow(SceneAtomImporter.BrokenUidRef row, int font, float s, float rowH)
        {
            string original = row.OriginalUid;
            string mapped;
            if (!_remapAtomUidsChoices.TryGetValue(original, out mapped)) mapped = string.Empty;

            GameObject shell = new GameObject("RemapRow_" + original);
            shell.transform.SetParent(_remapAtomUidsListParent, false);
            Image shellBg = UI.AddImage(shell, RemapRowBg);
            if (shellBg != null) shellBg.raycastTarget = true;
            VerticalLayoutGroup v = UI.AddVLG(shell, spacing: 2f * s, padding: UI.Pad(0, 0, 0, 0));
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            UI.AddLE(shell, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
            _remapAtomUidsRowShells[original] = shell;
            _remapAtomUidsRowBgs[original] = shellBg;

            // Main hit row — whole row toggles expand (InputField steals for typing).
            GameObject main = new GameObject("Main");
            main.transform.SetParent(shell.transform, false);
            Image mainBg = UI.AddImage(main, new Color(0f, 0f, 0f, 0.01f));
            if (mainBg != null) mainBg.raycastTarget = true;
            HorizontalLayoutGroup h = UI.AddHLG(
                main, spacing: 6f * s, padding: UI.Pad(8, 8, 4, 4, s),
                childForceExpandWidth: false);
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            UI.AddLE(main, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

            string capturedOriginal = original;
            string capturedType = row.SourceType;
            bool canCreate = row.CanCreateFromSource;

            Button rowBtn = main.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            rowBtn.targetGraphic = mainBg;
            rowBtn.onClick.AddListener(() => ToggleRemapAtomUidsExpand(capturedOriginal, capturedType, canCreate));

            string origLabel = original;
            if (!string.IsNullOrEmpty(row.SourceType))
                origLabel = original + "  (" + row.SourceType + ")";
            Text origTxt = UI.CreateLabel(
                main, origLabel, font, new Color(0.90f, 0.91f, 0.93f, 1f),
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.middleLeft,
                size: new Vector2(120f * s, rowH - 8f * s),
                name: "Original");
            GalleryUiMetrics.ApplyFont(origTxt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(origTxt.gameObject, flexibleWidth: 1.1f, minWidth: 120f * s,
                preferredHeight: rowH - 8f * s);

            RemapAtomUidsArrowGlyph(main.transform, rowH - 14f * s, s, font);

            GameObject remapHost = new GameObject("RemapHost");
            remapHost.transform.SetParent(main.transform, false);
            HorizontalLayoutGroup rh = UI.AddHLG(
                remapHost, spacing: 4f * s, padding: UI.Pad(0, 0, 0, 0),
                childForceExpandWidth: false);
            rh.childForceExpandHeight = false;
            rh.childAlignment = TextAnchor.MiddleLeft;
            UI.AddLE(remapHost, flexibleWidth: 1.4f, minWidth: 160f * s, preferredHeight: rowH - 8f * s);

            float inputH = rowH - 10f * s;
            string display = RemapAtomUidsChoiceToDisplay(mapped);
            bool suggested = RemapAtomUidsChoiceStillSuggested(original, mapped);
            bool emptyDest = string.IsNullOrEmpty(mapped);
            Color destBgColor = emptyDest
                ? RemapDestUnresolvedBg
                : (suggested ? RemapDestSuggestedBg : RemapDestNormalBg);
            if (emptyDest)
            {
                Image shellImg = shellBg;
                if (shellImg != null) shellImg.color = RemapRowUnresolvedBg;
            }

            // Hand-rolled InputField: text as direct child (CreateTextInput pattern).
            // Do NOT ApplyFont here — Body font already scaled; ApplyFont localScale + stretch
            // clips InputField glyphs under RectMask2D (blank destination).
            GameObject inputGO = new GameObject("Input");
            inputGO.transform.SetParent(remapHost.transform, false);
            Image destBgImg = UI.AddGalleryElementRoundedBg(inputGO, destBgColor);
            if (destBgImg != null) _remapAtomUidsDestBgs[original] = destBgImg;
            UI.AddLE(inputGO, flexibleWidth: 1f, minWidth: 80f * s, minHeight: inputH, preferredHeight: inputH);

            InputField input = inputGO.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.None;

            float padX = 8f * s;
            float padY = 2f * s;
            Text tComp = UI.CreateLabel(
                inputGO, display, font, UI.InputFieldTextColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.stretchAll, name: "Text");
            tComp.fontSize = font;
            tComp.transform.localScale = Vector3.one;
            RectTransform tRt = tComp.rectTransform;
            tRt.offsetMin = new Vector2(padX, padY);
            tRt.offsetMax = new Vector2(-padX, -padY);
            tRt.pivot = new Vector2(0f, 0.5f);

            string phText = suggested
                ? VPBTranslation.T("gallery.import.remap_uids.dest_ph_suggested", "Suggested — pick or type…")
                : VPBTranslation.T("gallery.import.remap_uids.dest_ph", "Pick or type UID…");
            Text ph = UI.CreateLabel(
                inputGO, phText, font, UI.InputFieldPlaceholderColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.stretchAll, name: "Placeholder");
            ph.fontSize = font;
            ph.fontStyle = FontStyle.Italic;
            ph.transform.localScale = Vector3.one;
            RectTransform phRt = ph.rectTransform;
            phRt.offsetMin = new Vector2(padX, padY);
            phRt.offsetMax = new Vector2(-padX, -padY);
            phRt.pivot = new Vector2(0f, 0.5f);

            input.textComponent = tComp;
            input.placeholder = ph;
            input.text = display;
            // Re-assert after InputField assigns (some Unity builds clear mesh until dirty).
            tComp.text = display;
            tComp.color = UI.InputFieldTextColor;
            ph.gameObject.SetActive(string.IsNullOrEmpty(display));

            try { inputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(input); } catch { }

            input.onEndEdit.AddListener(val =>
            {
                OnRemapAtomUidsChoiceEdited(capturedOriginal, val);
            });
            input.onValueChanged.AddListener(val =>
            {
                if (ph != null) ph.gameObject.SetActive(string.IsNullOrEmpty(val));
                _remapAtomUidsChoices[capturedOriginal] = RemapAtomUidsDisplayToStored(val);
                TryAutoSuggestRemapReceiver(capturedOriginal, force: true);
                SyncRemapAtomUidsReceiverInputVisual(capturedOriginal);
                if (_remapAtomUidsImportForceArmed && CountRemapAtomUidsAllGaps() == 0)
                    _remapAtomUidsImportForceArmed = false;
                RefreshRemapAtomUidsUnresolvedUi();
            });
            _remapAtomUidsInputs[original] = input;

            if (suggested)
            {
                var sugHover = inputGO.AddComponent<UIHoverDelegate>();
                sugHover.OnHoverChange += (enter) =>
                {
                    if (enter)
                    {
                        string stored;
                        if (!_remapAtomUidsChoices.TryGetValue(capturedOriginal, out stored))
                            stored = string.Empty;
                        if (RemapAtomUidsChoiceStillSuggested(capturedOriginal, stored))
                        {
                            SetStatus(VPBTranslation.T(
                                "gallery.import.remap_uids.tip_suggested",
                                "Suggested match from live scene"));
                        }
                    }
                    else SetStatus(null);
                };
            }

            // Click field → expand list (combobox pattern); typing still works.
            EventTrigger et = inputGO.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener(_ =>
            {
                if (!string.Equals(_remapAtomUidsOpenPickerFor, capturedOriginal, StringComparison.Ordinal))
                    OpenRemapAtomUidsExpand(capturedOriginal, capturedType, canCreate);
            });
            et.triggers.Add(entry);

            float chevronSz = rowH - 10f * s;
            GameObject pickBtn = RemapAtomUidsSquareIconButton(
                remapHost.transform, chevronSz, "vpb_icons/chevron_down.png",
                new Color(0.22f, 0.38f, 0.52f, 1f),
                () => ToggleRemapAtomUidsExpand(capturedOriginal, capturedType, canCreate));
            if (pickBtn != null)
            {
                pickBtn.name = "ExpandChevron";
                Transform iconTr = pickBtn.transform.Find("Icon");
                Image iconImg = iconTr != null ? iconTr.GetComponent<Image>() : null;
                if (iconImg != null) _remapAtomUidsChevronIcons[original] = iconImg;
            }

            // Receiver column — plugin#N_Class on remapped live atom (Embody slot mismatch).
            bool hasPluginRecv = row.SourcePluginReceivers != null && row.SourcePluginReceivers.Count > 0;
            string recvChoice;
            if (!_remapAtomUidsReceiverChoices.TryGetValue(original, out recvChoice))
                recvChoice = string.Empty;
            bool destOkForRecv = !string.IsNullOrEmpty(mapped) && !RemapAtomUidsIsCreateNewChoice(mapped);
            bool recvEnabled = hasPluginRecv && destOkForRecv;

            GameObject recvHost = new GameObject("ReceiverHost");
            recvHost.transform.SetParent(main.transform, false);
            HorizontalLayoutGroup recvHl = UI.AddHLG(
                recvHost, spacing: 4f * s, padding: UI.Pad(0, 0, 0, 0),
                childForceExpandWidth: false);
            recvHl.childForceExpandHeight = false;
            recvHl.childAlignment = TextAnchor.MiddleLeft;
            UI.AddLE(recvHost, flexibleWidth: 1.1f, minWidth: 120f * s, preferredHeight: rowH - 8f * s);

            Color recvBgColor = !recvEnabled
                ? new Color(0.12f, 0.13f, 0.15f, 1f)
                : (string.IsNullOrEmpty(recvChoice)
                    ? RemapDestUnresolvedBg
                    : (RemapAtomUidsReceiverStillSuggested(original, recvChoice)
                        ? RemapDestSuggestedBg
                        : RemapDestNormalBg));

            GameObject recvInputGO = new GameObject("ReceiverInput");
            recvInputGO.transform.SetParent(recvHost.transform, false);
            Image recvBgImg = UI.AddGalleryElementRoundedBg(recvInputGO, recvBgColor);
            if (recvBgImg != null) _remapAtomUidsReceiverBgs[original] = recvBgImg;
            UI.AddLE(recvInputGO, flexibleWidth: 1f, minWidth: 72f * s, minHeight: inputH, preferredHeight: inputH);

            InputField recvInput = recvInputGO.AddComponent<InputField>();
            recvInput.lineType = InputField.LineType.SingleLine;
            recvInput.transition = Selectable.Transition.None;
            recvInput.interactable = recvEnabled;

            Text recvTxt = UI.CreateLabel(
                recvInputGO, recvChoice ?? string.Empty, font, UI.InputFieldTextColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.stretchAll, name: "Text");
            recvTxt.fontSize = font;
            recvTxt.transform.localScale = Vector3.one;
            RectTransform recvTxtRt = recvTxt.rectTransform;
            recvTxtRt.offsetMin = new Vector2(padX, padY);
            recvTxtRt.offsetMax = new Vector2(-padX, -padY);
            recvTxtRt.pivot = new Vector2(0f, 0.5f);

            string recvPhText = hasPluginRecv
                ? VPBTranslation.T("gallery.import.remap_uids.receiver_ph", "plugin#N_Class…")
                : string.Empty;
            Text recvPh = UI.CreateLabel(
                recvInputGO, recvPhText, font, UI.InputFieldPlaceholderColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.stretchAll, name: "Placeholder");
            recvPh.fontSize = font;
            recvPh.fontStyle = FontStyle.Italic;
            recvPh.transform.localScale = Vector3.one;
            RectTransform recvPhRt = recvPh.rectTransform;
            recvPhRt.offsetMin = new Vector2(padX, padY);
            recvPhRt.offsetMax = new Vector2(-padX, -padY);
            recvPhRt.pivot = new Vector2(0f, 0.5f);

            recvInput.textComponent = recvTxt;
            recvInput.placeholder = recvPh;
            recvInput.text = recvChoice ?? string.Empty;
            recvTxt.text = recvChoice ?? string.Empty;
            recvPh.gameObject.SetActive(string.IsNullOrEmpty(recvChoice) && recvEnabled);

            try { recvInputGO.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(recvInput); } catch { }

            recvInput.onEndEdit.AddListener(val => OnRemapAtomUidsReceiverEdited(capturedOriginal, val));
            recvInput.onValueChanged.AddListener(val =>
            {
                if (recvPh != null) recvPh.gameObject.SetActive(string.IsNullOrEmpty(val) && recvInput.interactable);
                OnRemapAtomUidsReceiverEdited(capturedOriginal, val);
            });
            _remapAtomUidsReceiverInputs[original] = recvInput;

            if (hasPluginRecv)
            {
                EventTrigger recvEt = recvInputGO.AddComponent<EventTrigger>();
                EventTrigger.Entry recvEntry = new EventTrigger.Entry();
                recvEntry.eventID = EventTriggerType.PointerClick;
                recvEntry.callback.AddListener(_ =>
                {
                    if (!recvInput.interactable) return;
                    ToggleRemapAtomUidsReceiverExpand(capturedOriginal);
                });
                recvEt.triggers.Add(recvEntry);

                GameObject recvPickBtn = RemapAtomUidsSquareIconButton(
                    recvHost.transform, chevronSz, "vpb_icons/chevron_down.png",
                    new Color(0.22f, 0.38f, 0.52f, 1f),
                    () =>
                    {
                        if (!recvInput.interactable) return;
                        ToggleRemapAtomUidsReceiverExpand(capturedOriginal);
                    });
                if (recvPickBtn != null) recvPickBtn.name = "ReceiverChevron";
            }

            GameObject expandHost = new GameObject("Expand");
            expandHost.transform.SetParent(shell.transform, false);
            UI.AddImage(expandHost, new Color(0.06f, 0.07f, 0.09f, 1f));
            VerticalLayoutGroup ev = UI.AddVLG(
                expandHost, spacing: 2f * s, padding: UI.Pad(8, 8, 4, 6, s));
            ev.childForceExpandHeight = false;
            ev.childForceExpandWidth = true;
            UI.AddLE(expandHost, flexibleWidth: 1f, minHeight: 0f, preferredHeight: 0f);
            expandHost.SetActive(false);
            _remapAtomUidsExpandHosts[original] = expandHost;
        }

        private void ToggleRemapAtomUidsExpand(string originalUid, string sourceType, bool canCreate)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            if (string.Equals(_remapAtomUidsOpenPickerFor, originalUid, StringComparison.Ordinal)
                && !_remapAtomUidsExpandIsReceiver)
            {
                CollapseRemapAtomUidsExpand();
                return;
            }
            OpenRemapAtomUidsExpand(originalUid, sourceType, canCreate);
        }

        private void ToggleRemapAtomUidsReceiverExpand(string originalUid)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            if (string.Equals(_remapAtomUidsOpenPickerFor, originalUid, StringComparison.Ordinal)
                && _remapAtomUidsExpandIsReceiver)
            {
                CollapseRemapAtomUidsExpand();
                return;
            }
            OpenRemapAtomUidsReceiverExpand(originalUid);
        }

        private void CollapseRemapAtomUidsExpand()
        {
            string prev = _remapAtomUidsOpenPickerFor;
            _remapAtomUidsOpenPickerFor = null;
            _remapAtomUidsExpandIsReceiver = false;
            _remapAtomUidsExpandFilterInput = null;
            _remapAtomUidsExpandFilter = string.Empty;
            _remapAtomUidsExpandSameType.Clear();
            _remapAtomUidsExpandOther.Clear();
            if (string.IsNullOrEmpty(prev)) return;

            GameObject host;
            if (_remapAtomUidsExpandHosts.TryGetValue(prev, out host) && host != null)
            {
                for (int c = host.transform.childCount - 1; c >= 0; c--)
                {
                    try { UnityEngine.Object.Destroy(host.transform.GetChild(c).gameObject); } catch { }
                }
                host.SetActive(false);
                LayoutElement le = host.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = 0f;
            }

            GameObject shell;
            if (_remapAtomUidsRowShells.TryGetValue(prev, out shell) && shell != null)
            {
                float s = _remapAtomUidsChromeScale > 0f ? _remapAtomUidsChromeScale : ChromeScale;
                LayoutElement shellLe = shell.GetComponent<LayoutElement>();
                if (shellLe != null)
                    shellLe.preferredHeight = GalleryUiDesignTokens.ButtonSizeRef * s;
            }

            SyncRemapAtomUidsRowExpandVisual(prev, false);
            SyncRemapAtomUidsRowDestVisual(prev);
            SyncRemapAtomUidsReceiverInputVisual(prev);
        }

        private void OpenRemapAtomUidsExpand(string originalUid, string sourceType, bool canCreate)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            CollapseRemapAtomUidsExpand();
            _remapAtomUidsOpenPickerFor = originalUid;
            _remapAtomUidsExpandIsReceiver = false;
            _remapAtomUidsExpandSourceType = sourceType;
            _remapAtomUidsExpandCanCreate = canCreate;
            _remapAtomUidsExpandFilter = string.Empty;
            _remapAtomUidsExpandSameType.Clear();
            _remapAtomUidsExpandOther.Clear();

            if (SuperController.singleton != null)
            {
                foreach (Atom a in SuperController.singleton.GetAtoms())
                {
                    if (a == null || string.IsNullOrEmpty(a.uid)) continue;
                    if (SceneUtils.IsSystemProtectedAtomId(a.uid) && string.IsNullOrEmpty(sourceType))
                        continue;
                    if (!string.IsNullOrEmpty(sourceType)
                        && string.Equals(a.type, sourceType, StringComparison.Ordinal))
                        _remapAtomUidsExpandSameType.Add(a.uid);
                    else
                        _remapAtomUidsExpandOther.Add(a.uid);
                }
            }
            _remapAtomUidsExpandSameType.Sort(StringComparer.OrdinalIgnoreCase);
            _remapAtomUidsExpandOther.Sort(StringComparer.OrdinalIgnoreCase);

            RebuildRemapAtomUidsExpandOptions();
        }

        private void OpenRemapAtomUidsReceiverExpand(string originalUid)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            string dest;
            if (!_remapAtomUidsChoices.TryGetValue(originalUid, out dest)
                || string.IsNullOrEmpty(dest)
                || RemapAtomUidsIsCreateNewChoice(dest))
                return;

            CollapseRemapAtomUidsExpand();
            _remapAtomUidsOpenPickerFor = originalUid;
            _remapAtomUidsExpandIsReceiver = true;
            _remapAtomUidsExpandCanCreate = false;
            _remapAtomUidsExpandFilter = string.Empty;
            _remapAtomUidsExpandSameType.Clear();
            _remapAtomUidsExpandOther.Clear();

            List<SceneAtomImporter.LivePluginSlot> slots
                = SceneAtomImporter.ListLivePluginSlotsByUid(dest);
            for (int i = 0; i < slots.Count; i++)
            {
                string sid = slots[i].StoreId;
                if (string.IsNullOrEmpty(sid)) continue;
                _remapAtomUidsExpandSameType.Add(sid);
            }
            _remapAtomUidsExpandSameType.Sort(StringComparer.OrdinalIgnoreCase);

            RebuildRemapAtomUidsExpandOptions();
        }

        private void RebuildRemapAtomUidsExpandOptions()
        {
            string originalUid = _remapAtomUidsOpenPickerFor;
            if (string.IsNullOrEmpty(originalUid)) return;

            GameObject expandHost;
            if (!_remapAtomUidsExpandHosts.TryGetValue(originalUid, out expandHost) || expandHost == null)
                return;

            float s = _remapAtomUidsChromeScale > 0f ? _remapAtomUidsChromeScale : ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float rowH = GalleryUiDesignTokens.ButtonSizeRef * s * 0.9f;

            if (_remapAtomUidsExpandIsReceiver)
            {
                RebuildRemapAtomUidsReceiverExpandOptions(expandHost, originalUid, font, s, rowH);
                return;
            }

            string sourceType = _remapAtomUidsExpandSourceType;
            bool canCreate = _remapAtomUidsExpandCanCreate;
            string filter = _remapAtomUidsExpandFilter ?? string.Empty;
            if (!string.IsNullOrEmpty(filter)) filter = filter.Trim();

            // Keep filter field; wipe option rows.
            for (int c = expandHost.transform.childCount - 1; c >= 0; c--)
            {
                Transform ch = expandHost.transform.GetChild(c);
                if (ch == null) continue;
                if (_remapAtomUidsExpandFilterInput != null
                    && ch.gameObject == _remapAtomUidsExpandFilterInput.gameObject)
                    continue;
                try { UnityEngine.Object.Destroy(ch.gameObject); } catch { }
            }

            int candidateCount = _remapAtomUidsExpandSameType.Count + _remapAtomUidsExpandOther.Count;
            bool showFilter = candidateCount >= RemapExpandFilterThreshold;
            if (showFilter && _remapAtomUidsExpandFilterInput == null)
            {
                InputField filterInput = UI.CreateChromeLayoutInputField(
                    expandHost.transform,
                    font,
                    rowH,
                    1f,
                    8f * s,
                    2f * s,
                    new Color(0.12f, 0.14f, 0.18f, 1f),
                    UI.InputFieldPlaceholderColor,
                    VPBTranslation.T("gallery.import.remap_uids.filter_ph", "Filter atoms…"),
                    "ExpandFilter");
                if (filterInput != null)
                {
                    filterInput.transform.SetAsFirstSibling();
                    _remapAtomUidsExpandFilterInput = filterInput;
                    filterInput.onValueChanged.AddListener(val =>
                    {
                        _remapAtomUidsExpandFilter = val ?? string.Empty;
                        RebuildRemapAtomUidsExpandOptions();
                    });
                    try { filterInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>().Initialize(filterInput); }
                    catch { }
                }
            }
            else if (showFilter && _remapAtomUidsExpandFilterInput != null)
            {
                _remapAtomUidsExpandFilterInput.transform.SetAsFirstSibling();
            }

            string current;
            _remapAtomUidsChoices.TryGetValue(originalUid, out current);
            bool createActive = RemapAtomUidsIsCreateNewChoice(current);
            int optionCount = showFilter ? 1 : 0;
            string capturedOrig = originalUid;

            bool createMatchesFilter = string.IsNullOrEmpty(filter)
                || RemapAtomUidsCreateNewLabel().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || filter.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0;

            if (canCreate && createMatchesFilter)
            {
                string createLabel = RemapAtomUidsCreateNewLabel()
                    + VPBTranslation.T("gallery.import.remap_uids.create_new_suffix", "  (import from donor)");
                RemapAtomUidsAddExpandOption(expandHost, createLabel, font, s, rowH, createActive, () =>
                {
                    ApplyRemapAtomUidsPick(capturedOrig, SceneAtomImporter.CreateNewUidSentinel);
                });
                optionCount++;
            }

            int sameShown = 0;
            int otherShown = 0;
            int otherCap = string.IsNullOrEmpty(filter) ? 24 : 64;

            for (int i = 0; i < _remapAtomUidsExpandSameType.Count; i++)
            {
                string uid = _remapAtomUidsExpandSameType[i];
                if (!string.IsNullOrEmpty(filter)
                    && uid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (string.IsNullOrEmpty(sourceType)
                        || sourceType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                bool active = string.Equals(uid, current, StringComparison.Ordinal);
                string label = string.IsNullOrEmpty(sourceType) ? uid : uid + "  [" + sourceType + "]";
                string captured = uid;
                RemapAtomUidsAddExpandOption(expandHost, label, font, s, rowH, active, () =>
                {
                    ApplyRemapAtomUidsPick(capturedOrig, captured);
                });
                optionCount++;
                sameShown++;
            }

            bool anyOtherVisible = false;
            for (int i = 0; i < _remapAtomUidsExpandOther.Count; i++)
            {
                string uid = _remapAtomUidsExpandOther[i];
                Atom live = SuperController.singleton != null
                    ? SuperController.singleton.GetAtomByUid(uid) : null;
                string liveType = live != null ? live.type : null;
                if (!string.IsNullOrEmpty(filter)
                    && uid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (string.IsNullOrEmpty(liveType)
                        || liveType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                anyOtherVisible = true;
                break;
            }

            if (anyOtherVisible && (sameShown > 0 || (canCreate && createMatchesFilter)))
            {
                Text sep = UI.CreateLabel(
                    expandHost,
                    VPBTranslation.T("gallery.import.remap_uids.other_types", "— other types —"),
                    font, new Color(0.55f, 0.57f, 0.60f, 1f),
                    TextAnchor.MiddleLeft, raycastTarget: false, name: "Sep");
                GalleryUiMetrics.ApplyFont(sep, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                UI.AddLE(sep.gameObject, preferredHeight: rowH * 0.65f, flexibleWidth: 1f);
                optionCount++;
            }

            for (int i = 0; i < _remapAtomUidsExpandOther.Count && otherShown < otherCap; i++)
            {
                string uid = _remapAtomUidsExpandOther[i];
                Atom live = SuperController.singleton != null
                    ? SuperController.singleton.GetAtomByUid(uid) : null;
                string liveType = live != null ? live.type : null;
                if (!string.IsNullOrEmpty(filter)
                    && uid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (string.IsNullOrEmpty(liveType)
                        || liveType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                bool active = string.Equals(uid, current, StringComparison.Ordinal);
                string label = live != null ? uid + "  (" + live.type + ")" : uid;
                string captured = uid;
                RemapAtomUidsAddExpandOption(expandHost, label, font, s, rowH, active, () =>
                {
                    ApplyRemapAtomUidsPick(capturedOrig, captured);
                });
                optionCount++;
                otherShown++;
            }

            int contentRows = optionCount - (showFilter ? 1 : 0);
            if (contentRows <= 0)
            {
                string emptyMsg = candidateCount == 0 && !canCreate
                    ? VPBTranslation.T("gallery.import.remap_uids.no_live", "(no live atoms)")
                    : VPBTranslation.T("gallery.import.remap_uids.filter_empty", "(no matches)");
                RemapAtomUidsAddExpandOption(
                    expandHost, emptyMsg, font, s, rowH, false, CollapseRemapAtomUidsExpand);
                optionCount++;
            }

            float expandH = optionCount * (rowH + 2f * s) + 10f * s;
            LayoutElement hostLe = expandHost.GetComponent<LayoutElement>();
            if (hostLe != null) hostLe.preferredHeight = expandH;
            expandHost.SetActive(true);
            SyncRemapAtomUidsRowExpandVisual(originalUid, true);

            GameObject shell;
            if (_remapAtomUidsRowShells.TryGetValue(originalUid, out shell) && shell != null)
            {
                float mainH = GalleryUiDesignTokens.ButtonSizeRef * s;
                LayoutElement shellLe = shell.GetComponent<LayoutElement>();
                if (shellLe != null)
                    shellLe.preferredHeight = mainH + expandH;
            }

            Canvas.ForceUpdateCanvases();
        }

        private void SyncRemapAtomUidsRowExpandVisual(string originalUid, bool expanded)
        {
            Image bg;
            if (_remapAtomUidsRowBgs.TryGetValue(originalUid, out bg) && bg != null)
            {
                if (expanded) bg.color = RemapRowExpandedBg;
                else SyncRemapAtomUidsRowDestVisual(originalUid);
            }

            Image chevron;
            if (_remapAtomUidsChevronIcons.TryGetValue(originalUid, out chevron) && chevron != null)
            {
                try
                {
                    string path = expanded ? "vpb_icons/chevron_up.png" : "vpb_icons/chevron_down.png";
                    Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                    if (spr != null) chevron.sprite = spr;
                }
                catch { }
            }
        }

        private static void RemapAtomUidsAddExpandOption(
            GameObject parent, string label, int font, float s, float rowH, bool active, UnityAction onClick)
        {
            if (parent == null) return;
            GameObject go = UI.CreateChromeLayoutButton(
                parent.transform, 0f, rowH, label, font,
                active ? RemapExpandOptionActiveBg : RemapExpandOptionBg,
                onClick);
            if (go == null) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
            }
            Text t = go.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                RectTransform trt = t.GetComponent<RectTransform>();
                if (trt != null)
                {
                    trt.offsetMin = new Vector2(10f * s, 0f);
                    trt.offsetMax = new Vector2(-8f * s, 0f);
                }
            }
        }

        private void ApplyRemapAtomUidsPick(string originalUid, string liveUid)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            _remapAtomUidsChoices[originalUid] = liveUid ?? string.Empty;
            string display = RemapAtomUidsChoiceToDisplay(liveUid);
            InputField input;
            if (_remapAtomUidsInputs.TryGetValue(originalUid, out input) && input != null)
            {
                input.text = display;
                if (input.textComponent != null)
                {
                    input.textComponent.text = display;
                    input.textComponent.color = UI.InputFieldTextColor;
                    input.textComponent.transform.localScale = Vector3.one;
                }
                if (input.placeholder != null)
                    input.placeholder.gameObject.SetActive(string.IsNullOrEmpty(display));
            }
            TryAutoSuggestRemapReceiver(originalUid, force: true);
            SyncRemapAtomUidsReceiverInputVisual(originalUid);
            if (_remapAtomUidsImportForceArmed && CountRemapAtomUidsAllGaps() == 0)
                _remapAtomUidsImportForceArmed = false;
            CollapseRemapAtomUidsExpand();
            RefreshRemapAtomUidsUnresolvedUi();
        }

        private void ApplyRemapAtomUidsReceiverPick(string originalUid, string storeId)
        {
            if (string.IsNullOrEmpty(originalUid)) return;
            string t = storeId != null ? storeId.Trim() : string.Empty;
            if (string.IsNullOrEmpty(t))
                _remapAtomUidsReceiverChoices.Remove(originalUid);
            else
                _remapAtomUidsReceiverChoices[originalUid] = t;
            SyncRemapAtomUidsReceiverInputVisual(originalUid);
            if (_remapAtomUidsImportForceArmed && CountRemapAtomUidsAllGaps() == 0)
                _remapAtomUidsImportForceArmed = false;
            CollapseRemapAtomUidsExpand();
            RefreshRemapAtomUidsUnresolvedUi();
        }

        private void RebuildRemapAtomUidsReceiverExpandOptions(
            GameObject expandHost, string originalUid, int font, float s, float rowH)
        {
            for (int c = expandHost.transform.childCount - 1; c >= 0; c--)
            {
                try { UnityEngine.Object.Destroy(expandHost.transform.GetChild(c).gameObject); } catch { }
            }

            string current;
            if (!_remapAtomUidsReceiverChoices.TryGetValue(originalUid, out current))
                current = string.Empty;

            SceneAtomImporter.BrokenUidRef row = FindRemapAtomUidsRow(originalUid);
            string dest;
            _remapAtomUidsChoices.TryGetValue(originalUid, out dest);

            int optionCount = 0;
            string capturedOrig = originalUid;

            // Recognition: list every source plugin receiver + auto status (multi-plugin edge).
            if (row.SourcePluginReceivers != null && row.SourcePluginReceivers.Count > 0)
            {
                Text hdr = UI.CreateLabel(
                    expandHost,
                    VPBTranslation.T(
                        "gallery.import.remap_uids.receiver_sources",
                        "Source receivers → live"),
                    font, new Color(0.62f, 0.64f, 0.68f, 1f),
                    TextAnchor.MiddleLeft, raycastTarget: false, name: "RecvHdr");
                hdr.fontSize = font;
                hdr.transform.localScale = Vector3.one;
                UI.AddLE(hdr.gameObject, preferredHeight: rowH * 0.65f, flexibleWidth: 1f);
                optionCount++;

                for (int ri = 0; ri < row.SourcePluginReceivers.Count; ri++)
                {
                    string srcRecv = row.SourcePluginReceivers[ri];
                    if (string.IsNullOrEmpty(srcRecv)) continue;
                    string url = null;
                    if (row.SourcePluginReceiverUrls != null && ri < row.SourcePluginReceiverUrls.Count)
                        url = row.SourcePluginReceiverUrls[ri];
                    string sug = SceneAtomImporter.SuggestLiveReceiverStoreId(dest, srcRecv, url);
                    string line;
                    if (!string.IsNullOrEmpty(sug))
                    {
                        if (string.Equals(sug, srcRecv, StringComparison.Ordinal))
                            line = srcRecv + "  [ok]";
                        else
                            line = srcRecv + " -> " + sug;
                    }
                    else if (ri == 0 && !string.IsNullOrEmpty(current))
                        line = srcRecv + " -> " + current + "  (pick)";
                    else
                        line = srcRecv + "  [!]";

                    Text lineTxt = UI.CreateLabel(
                        expandHost, line, font,
                        string.IsNullOrEmpty(sug) && !(ri == 0 && !string.IsNullOrEmpty(current))
                            ? RemapUnresolvedMuted
                            : new Color(0.78f, 0.80f, 0.84f, 1f),
                        TextAnchor.MiddleLeft, raycastTarget: false, name: "RecvSrc");
                    lineTxt.fontSize = font;
                    lineTxt.transform.localScale = Vector3.one;
                    UI.AddLE(lineTxt.gameObject, preferredHeight: rowH * 0.7f, flexibleWidth: 1f);
                    optionCount++;
                }

                Text pickHdr = UI.CreateLabel(
                    expandHost,
                    VPBTranslation.T(
                        "gallery.import.remap_uids.receiver_pick",
                        "Pick live store for primary:"),
                    font, new Color(0.55f, 0.57f, 0.60f, 1f),
                    TextAnchor.MiddleLeft, raycastTarget: false, name: "RecvPickHdr");
                pickHdr.fontSize = font;
                pickHdr.transform.localScale = Vector3.one;
                UI.AddLE(pickHdr.gameObject, preferredHeight: rowH * 0.6f, flexibleWidth: 1f);
                optionCount++;
            }

            for (int i = 0; i < _remapAtomUidsExpandSameType.Count; i++)
            {
                string sid = _remapAtomUidsExpandSameType[i];
                bool active = string.Equals(sid, current, StringComparison.Ordinal);
                string captured = sid;
                RemapAtomUidsAddExpandOption(expandHost, sid, font, s, rowH, active, () =>
                {
                    ApplyRemapAtomUidsReceiverPick(capturedOrig, captured);
                });
                optionCount++;
            }

            if (_remapAtomUidsExpandSameType.Count == 0)
            {
                RemapAtomUidsAddExpandOption(
                    expandHost,
                    VPBTranslation.T(
                        "gallery.import.remap_uids.receiver_none",
                        "No plugins on destination"),
                    font, s, rowH, false, CollapseRemapAtomUidsExpand);
                optionCount++;
            }

            expandHost.SetActive(true);
            LayoutElement ele = expandHost.GetComponent<LayoutElement>();
            if (ele != null)
                ele.preferredHeight = Mathf.Max(rowH, optionCount * (rowH + 2f * s) + 10f * s);

            GameObject shell;
            if (_remapAtomUidsRowShells.TryGetValue(originalUid, out shell) && shell != null)
            {
                LayoutElement shellLe = shell.GetComponent<LayoutElement>();
                float baseH = GalleryUiDesignTokens.ButtonSizeRef * s;
                if (shellLe != null)
                    shellLe.preferredHeight = baseH + (ele != null ? ele.preferredHeight : 0f);
            }

            SyncRemapAtomUidsRowExpandVisual(originalUid, true);
        }

        private static void RemapAtomUidsColLabel(
            Transform parent, string text, int font, float s, float flex, float height)
        {
            // Same scaled Body size as row text — no ApplyFont (avoids localScale vs layout mismatch).
            Text t = UI.CreateLabel(
                parent.gameObject, text, font,
                new Color(0.90f, 0.91f, 0.93f, 1f),
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false,
                anchorPreset: AnchorPresets.middleLeft,
                size: new Vector2(80f * s, height),
                name: "Col");
            t.fontSize = font;
            t.transform.localScale = Vector3.one;
            LayoutElement le = UI.AddLE(
                t.gameObject,
                flexibleWidth: flex,
                minWidth: 72f * s,
                preferredHeight: height);
            le.minHeight = height;
        }

        /// <summary>
        /// Live UI-scale hotkey path: rebuild float chrome at new ChromeScale, keep choices + geometry.
        /// </summary>
        private void RescaleRemapAtomUidsIfOpen(float chromeScale)
        {
            if (!IsRemapAtomUidsModalOpen) return;
            if (_remapAtomUidsRows == null || _remapAtomUidsOnConfirm == null) return;

            float s = chromeScale > 0f ? chromeScale : ChromeScale;
            if (Mathf.Abs(s - _remapAtomUidsChromeScale) < 0.0005f) return;

            foreach (KeyValuePair<string, InputField> kv in _remapAtomUidsInputs)
            {
                if (kv.Value == null) continue;
                string typed = kv.Value.text != null ? kv.Value.text.Trim() : string.Empty;
                _remapAtomUidsChoices[kv.Key] = RemapAtomUidsDisplayToStored(typed);
            }
            foreach (KeyValuePair<string, InputField> kv in _remapAtomUidsReceiverInputs)
            {
                if (kv.Value == null) continue;
                string typed = kv.Value.text != null ? kv.Value.text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(typed))
                    _remapAtomUidsReceiverChoices.Remove(kv.Key);
                else
                    _remapAtomUidsReceiverChoices[kv.Key] = typed;
            }

            CaptureRemapAtomUidsGeometryToMemory();
            PersistRemapAtomUidsGeometry();
            CollapseRemapAtomUidsExpand();

            if (_remapAtomUidsModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_remapAtomUidsModalRoot); } catch { }
                _remapAtomUidsModalRoot = null;
            }
            _remapAtomUidsPanelRT = null;
            _remapAtomUidsListParent = null;
            _remapAtomUidsUnresolvedText = null;
            _remapAtomUidsImportLabel = null;
            _remapAtomUidsImportBg = null;
            _remapAtomUidsInputs.Clear();
            _remapAtomUidsReceiverInputs.Clear();
            _remapAtomUidsDestBgs.Clear();
            _remapAtomUidsReceiverBgs.Clear();
            _remapAtomUidsRowShells.Clear();
            _remapAtomUidsExpandHosts.Clear();
            _remapAtomUidsRowBgs.Clear();
            _remapAtomUidsChevronIcons.Clear();
            _remapAtomUidsOpenPickerFor = null;

            BuildRemapAtomUidsModal();
        }

        private void LoadRemapAtomUidsGeometryFromConfig()
        {
            _remapAtomUidsSavedPosCenter = null;
            _remapAtomUidsSavedSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (VPBConfig.Instance.GalleryRemapAtomUidsPosSaved)
                {
                    _remapAtomUidsSavedPosCenter = new Vector2(
                        VPBConfig.Instance.GalleryRemapAtomUidsPosX,
                        VPBConfig.Instance.GalleryRemapAtomUidsPosY);
                }
                if (VPBConfig.Instance.GalleryRemapAtomUidsSizeSaved)
                {
                    float w = VPBConfig.Instance.GalleryRemapAtomUidsWidthRef;
                    float h = VPBConfig.Instance.GalleryRemapAtomUidsHeightRef;
                    if (w >= RemapFloatMinWRef && h >= RemapFloatMinHRef)
                    {
                        _remapAtomUidsSavedSizeRef = new Vector2(
                            Mathf.Clamp(w, RemapFloatMinWRef, RemapFloatMaxWRef),
                            Mathf.Clamp(h, RemapFloatMinHRef, RemapFloatMaxHRef));
                    }
                }
            }
            catch { }
        }

        private void CaptureRemapAtomUidsGeometryToMemory()
        {
            if (_remapAtomUidsPanelRT == null) return;
            float s = _remapAtomUidsChromeScale > 0f ? _remapAtomUidsChromeScale : 1f;
            _remapAtomUidsSavedPosCenter = RemapAtomUidsTopLeftToCenter(
                _remapAtomUidsPanelRT.anchoredPosition, _remapAtomUidsPanelRT.sizeDelta);
            _remapAtomUidsSavedSizeRef = new Vector2(
                Mathf.Clamp(_remapAtomUidsPanelRT.sizeDelta.x / s, RemapFloatMinWRef, RemapFloatMaxWRef),
                Mathf.Clamp(_remapAtomUidsPanelRT.sizeDelta.y / s, RemapFloatMinHRef, RemapFloatMaxHRef));
        }

        private void PersistRemapAtomUidsGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                if (_remapAtomUidsSavedPosCenter.HasValue)
                {
                    VPBConfig.Instance.GalleryRemapAtomUidsPosSaved = true;
                    VPBConfig.Instance.GalleryRemapAtomUidsPosX = _remapAtomUidsSavedPosCenter.Value.x;
                    VPBConfig.Instance.GalleryRemapAtomUidsPosY = _remapAtomUidsSavedPosCenter.Value.y;
                }
                if (_remapAtomUidsSavedSizeRef.HasValue)
                {
                    VPBConfig.Instance.GalleryRemapAtomUidsSizeSaved = true;
                    VPBConfig.Instance.GalleryRemapAtomUidsWidthRef = _remapAtomUidsSavedSizeRef.Value.x;
                    VPBConfig.Instance.GalleryRemapAtomUidsHeightRef = _remapAtomUidsSavedSizeRef.Value.y;
                }
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private void OnRemapAtomUidsFloatMoved()
        {
            CaptureRemapAtomUidsGeometryToMemory();
            PersistRemapAtomUidsGeometry();
        }

        private void OnRemapAtomUidsFloatResized()
        {
            CaptureRemapAtomUidsGeometryToMemory();
            PersistRemapAtomUidsGeometry();
        }

        private static Vector2 RemapAtomUidsCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 RemapAtomUidsTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private static void RemapAtomUidsArrowGlyph(Transform parent, float size, float s, int font)
        {
            float sz = Mathf.Max(16f * s, size);
            GameObject go = new GameObject("Arrow");
            go.transform.SetParent(parent, false);
            UI.AddLE(go, minWidth: sz, preferredWidth: sz, preferredHeight: sz);
            try
            {
                Sprite spr = UI.LoadIconSprite("vpb_icons/chevron_right.png", UI.BarIconGlyphTint);
                if (spr != null)
                {
                    Image img = UI.AddImage(go, new Color(0.55f, 0.62f, 0.70f, 1f));
                    img.sprite = spr;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.color = new Color(0.55f, 0.62f, 0.70f, 1f);
                    return;
                }
            }
            catch { }

            Text fallback = UI.CreateLabel(go, "\u2192", font, new Color(0.55f, 0.62f, 0.70f, 1f),
                TextAnchor.MiddleCenter, raycastTarget: false, name: "ArrowText");
            GalleryUiMetrics.ApplyFont(fallback, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
        }

        private static GameObject RemapAtomUidsSquareIconButton(
            Transform parent, float size, string iconPath, Color backdrop, UnityAction onClick)
        {
            GameObject go = UI.CreateUIButton(
                parent.gameObject, size, size, " ", 14, 0, 0, AnchorPresets.middleCenter, onClick);
            if (go == null) return null;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = size;
            le.minHeight = le.preferredHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            Button btn = go.GetComponent<Button>();
            if (btn != null) btn.transition = Selectable.Transition.None;
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            try
            {
                Sprite spr = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                if (spr != null)
                    UI.AddIconToButton(go, spr, Mathf.Max(4f, size * 0.16f), backdrop);
                else
                {
                    Image img = go.GetComponent<Image>();
                    if (img != null) img.color = backdrop;
                }
            }
            catch
            {
                Image img = go.GetComponent<Image>();
                if (img != null) img.color = backdrop;
            }
            return go;
        }

        private static GameObject RemapAtomUidsChromeButton(
            Transform parent, float width, float height, string label, int font, float s,
            Color bg, UnityAction onClick, bool flexibleWidth)
        {
            float w = flexibleWidth ? 0f : width;
            GameObject go = UI.CreateChromeLayoutButton(parent, w, height, label, font, bg, onClick);
            if (flexibleWidth)
            {
                LayoutElement le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minWidth = 72f * s;
                le.minHeight = height;
                le.preferredHeight = height;
            }
            return go;
        }

        private static void SetLayerRecursiveLocal(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursiveLocal(t.GetChild(i).gameObject, layer);
        }
    }
}
