using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Strip Scene possessable + person rename modes (Session Plugins parity).
    /// Options persist in VPBConfig; applied on confirm / after scene rebuild.
    /// </summary>
    public partial class GalleryPanel
    {
        /// <summary>Person rename policy applied on Strip confirm (kept Persons).</summary>
        private enum StripKeepPersonRenameMode
        {
            Off = 0,
            ActorNumber = 1,
            PrefixGender = 2,
            RenameGender = 3,
        }

        private bool _stripKeepRemovePossessable = true;
        private bool _stripKeepAddPossessableMale = true;
        private bool _stripKeepAddPossessableFemale = false;
        private StripKeepPersonRenameMode _stripKeepPersonRenameMode = StripKeepPersonRenameMode.Off;

        private Toggle _stripKeepPossClearToggle;
        private Toggle _stripKeepPossMaleToggle;
        private Toggle _stripKeepPossFemaleToggle;
        private Text _stripKeepRenameModeBtnLabel;
        private GameObject _stripKeepPossOptionsHost;

        private void StripKeepPossessOptionsResetUiRefs()
        {
            _stripKeepPossClearToggle = null;
            _stripKeepPossMaleToggle = null;
            _stripKeepPossFemaleToggle = null;
            _stripKeepRenameModeBtnLabel = null;
            _stripKeepPossOptionsHost = null;
        }

        private void LoadStripKeepPossessOptionsFromConfig()
        {
            _stripKeepRemovePossessable = true;
            _stripKeepAddPossessableMale = true;
            _stripKeepAddPossessableFemale = false;
            _stripKeepPersonRenameMode = StripKeepPersonRenameMode.Off;
            try
            {
                if (VPBConfig.Instance == null) return;
                _stripKeepRemovePossessable = VPBConfig.Instance.CreatorStripRemovePossessable;
                _stripKeepAddPossessableMale = VPBConfig.Instance.CreatorStripAddPossessableMale;
                _stripKeepAddPossessableFemale = VPBConfig.Instance.CreatorStripAddPossessableFemale;
                int rm = VPBConfig.Instance.CreatorStripPersonRenameMode;
                if (rm < 0) rm = 0;
                if (rm > 3) rm = 3;
                _stripKeepPersonRenameMode = (StripKeepPersonRenameMode)rm;
            }
            catch { }
        }

        private void PersistStripKeepPossessOptionsToConfig()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.CreatorStripRemovePossessable = _stripKeepRemovePossessable;
                VPBConfig.Instance.CreatorStripAddPossessableMale = _stripKeepAddPossessableMale;
                VPBConfig.Instance.CreatorStripAddPossessableFemale = _stripKeepAddPossessableFemale;
                VPBConfig.Instance.CreatorStripPersonRenameMode = (int)_stripKeepPersonRenameMode;
                VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        private bool StripKeepPossessOptionsActive()
        {
            return _stripKeepRemovePossessable
                || _stripKeepAddPossessableMale
                || _stripKeepAddPossessableFemale;
        }

        private void StripKeepWritePossessOptionsToJson(JSONClass jc)
        {
            if (jc == null) return;
            jc["possClear"].AsBool = _stripKeepRemovePossessable;
            jc["possMale"].AsBool = _stripKeepAddPossessableMale;
            jc["possFemale"].AsBool = _stripKeepAddPossessableFemale;
            jc["renameMode"].AsInt = (int)_stripKeepPersonRenameMode;
        }

        private void StripKeepReadPossessOptionsFromJson(JSONClass jc)
        {
            if (jc == null) return;
            if (jc["possClear"] != null)
                _stripKeepRemovePossessable = jc["possClear"].AsBool;
            if (jc["possMale"] != null)
                _stripKeepAddPossessableMale = jc["possMale"].AsBool;
            if (jc["possFemale"] != null)
                _stripKeepAddPossessableFemale = jc["possFemale"].AsBool;
            if (jc["renameMode"] != null)
            {
                int rm = jc["renameMode"].AsInt;
                if (rm < 0) rm = 0;
                if (rm > 3) rm = 3;
                _stripKeepPersonRenameMode = (StripKeepPersonRenameMode)rm;
            }
            PersistStripKeepPossessOptionsToConfig();
            RefreshStripKeepPossessOptionsChrome();
        }

        private string StripKeepPersonRenameModeLabel(StripKeepPersonRenameMode mode)
        {
            switch (mode)
            {
                case StripKeepPersonRenameMode.ActorNumber: return "Rename:Actor#";
                case StripKeepPersonRenameMode.PrefixGender: return "Rename:Prefix";
                case StripKeepPersonRenameMode.RenameGender: return "Rename:Gender";
                default: return "Rename:Off";
            }
        }

        private void StripKeepCyclePersonRenameMode()
        {
            int next = ((int)_stripKeepPersonRenameMode + 1) % 4;
            _stripKeepPersonRenameMode = (StripKeepPersonRenameMode)next;
            PersistStripKeepPossessOptionsToConfig();
            RefreshStripKeepRenameModeButtonLabel();

            // Preview renames immediately so list shows outcome (recognition).
            if (_stripKeepPersonRenameMode == StripKeepPersonRenameMode.Off)
            {
                // Leave existing manual renames; only clear ones we can re-derive? Keep manual.
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.creator.strip_rename_mode_off", "Person rename mode off."),
                    1.25f);
            }
            else
            {
                StripKeepApplyPersonRenameModeToSelection(overwriteExisting: true);
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T(
                            "gallery.creator.strip_rename_mode_set",
                            "Person rename → {0}"),
                        StripKeepPersonRenameModeLabel(_stripKeepPersonRenameMode)),
                    1.5f);
            }
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
        }

        /// <summary>
        /// Apply current rename mode to kept Persons using live scene gender.
        /// </summary>
        private void StripKeepApplyPersonRenameModeToSelection(bool overwriteExisting)
        {
            if (_stripKeepPersonRenameMode == StripKeepPersonRenameMode.Off) return;

            SuperController sc = SuperController.singleton;
            Dictionary<string, Atom> byUid = new Dictionary<string, Atom>(StringComparer.Ordinal);
            if (sc != null)
            {
                List<Atom> all = null;
                try { all = sc.GetAtoms(); } catch { all = null; }
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Atom a = all[i];
                        if (a == null) continue;
                        string uid = null;
                        try { uid = a.uid; } catch { uid = null; }
                        if (string.IsNullOrEmpty(uid)) continue;
                        byUid[uid] = a;
                    }
                }
            }

            if (_stripKeepPersonRenameMode == StripKeepPersonRenameMode.ActorNumber)
            {
                StripKeepBulkRenameSelectedPersonsInternal(overwriteExisting);
                return;
            }

            int maleN = 0;
            int femaleN = 0;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Synthetic) continue;
                if (it.Kind != CreatorStripKeepKind.Persons) continue;
                if (!_stripKeepSelectedUids.Contains(it.Uid)) continue;
                if (!overwriteExisting
                    && _stripKeepRenames.ContainsKey(it.Uid)
                    && !string.Equals(_stripKeepRenames[it.Uid], it.Uid, StringComparison.Ordinal))
                    continue;

                Atom atom = null;
                byUid.TryGetValue(it.Uid, out atom);
                bool isMale = false;
                bool isFemale = false;
                if (atom != null)
                {
                    try { isMale = AtomGenderUtils.IsMale(atom); } catch { isMale = false; }
                    try { isFemale = AtomGenderUtils.IsFemale(atom); } catch { isFemale = false; }
                }
                // Fallback: name heuristic when live gender unavailable.
                if (!isMale && !isFemale)
                {
                    string probe = it.Name ?? it.Uid ?? "";
                    if (probe.StartsWith("Futa", StringComparison.OrdinalIgnoreCase)
                        || probe.StartsWith("Male", StringComparison.OrdinalIgnoreCase)
                        || probe.StartsWith("M_", StringComparison.OrdinalIgnoreCase))
                        isMale = true;
                    else if (probe.StartsWith("Female", StringComparison.OrdinalIgnoreCase)
                        || probe.StartsWith("F_", StringComparison.OrdinalIgnoreCase))
                        isFemale = true;
                }
                if (!isMale && !isFemale) continue; // unknown — leave name alone

                string newName;
                if (_stripKeepPersonRenameMode == StripKeepPersonRenameMode.PrefixGender)
                {
                    string baseName = it.Name;
                    if (string.IsNullOrEmpty(baseName)) baseName = it.Uid;
                    // Strip prior gender prefix to avoid M_M_ stacking.
                    if (baseName.StartsWith("M_", StringComparison.Ordinal)
                        || baseName.StartsWith("F_", StringComparison.Ordinal))
                        baseName = baseName.Substring(2);
                    string prefix = isMale ? "M_" : "F_";
                    newName = prefix + baseName;
                }
                else
                {
                    // RenameGender: Male / Male2 / Female / Female2…
                    if (isMale)
                    {
                        maleN++;
                        newName = maleN <= 1 ? "Male" : ("Male" + maleN.ToString());
                    }
                    else
                    {
                        femaleN++;
                        newName = femaleN <= 1 ? "Female" : ("Female" + femaleN.ToString());
                    }
                }

                while (StripKeepBulkNameTaken(newName, it.Uid))
                {
                    if (_stripKeepPersonRenameMode == StripKeepPersonRenameMode.PrefixGender)
                        newName = newName + "_";
                    else if (isMale)
                    {
                        maleN++;
                        newName = "Male" + maleN.ToString();
                    }
                    else
                    {
                        femaleN++;
                        newName = "Female" + femaleN.ToString();
                    }
                }

                if (string.Equals(newName, it.Uid, StringComparison.Ordinal))
                    _stripKeepRenames.Remove(it.Uid);
                else
                    _stripKeepRenames[it.Uid] = newName;
            }
        }

        /// <summary>Actor# rename used by mode + legacy Actor# chip.</summary>
        private void StripKeepBulkRenameSelectedPersonsInternal(bool overwriteExisting)
        {
            int n = 0;
            for (int i = 0; i < _stripKeepItems.Count; i++)
            {
                StripKeepItem it = _stripKeepItems[i];
                if (it.Kind != CreatorStripKeepKind.Persons) continue;
                if (!_stripKeepSelectedUids.Contains(it.Uid)) continue;
                if (!overwriteExisting
                    && _stripKeepRenames.ContainsKey(it.Uid)
                    && !string.Equals(_stripKeepRenames[it.Uid], it.Uid, StringComparison.Ordinal))
                    continue;

                n++;
                string newName = "Actor" + n.ToString();
                while (StripKeepBulkNameTaken(newName, it.Uid))
                {
                    n++;
                    newName = "Actor" + n.ToString();
                }
                if (string.Equals(newName, it.Uid, StringComparison.Ordinal))
                    _stripKeepRenames.Remove(it.Uid);
                else
                    _stripKeepRenames[it.Uid] = newName;
            }
        }

        /// <summary>
        /// Possession-ready preset: Persons only + clear possess + gender head/hands + Prefix rename.
        /// </summary>
        private void StripKeepApplyPossessionReadyPreset()
        {
            _stripKeepRemovePossessable = true;
            _stripKeepAddPossessableMale = true;
            _stripKeepAddPossessableFemale = true;
            _stripKeepPersonRenameMode = StripKeepPersonRenameMode.PrefixGender;
            PersistStripKeepPossessOptionsToConfig();

            _stripKeepMoreToolsExpanded = true;
            try { ApplyStripKeepMoreToolsVisibility(); } catch { }
            try { RefreshStripKeepMoreToggleLabel(); } catch { }

            StripKeepApplyPreset(CreatorStripKeepKind.Persons);
            StripKeepApplyPersonRenameModeToSelection(overwriteExisting: true);
            RefreshStripKeepPossessOptionsChrome();
            RebuildStripKeepToggleRows();
            RefreshStripKeepSummaryAndConfirm();
            ShowTemporaryStatus(
                VPBTranslation.T(
                    "gallery.creator.strip_preset_possess_ready",
                    "Possession-ready: Persons + clear possess + head/hands + Prefix rename."),
                2f);
        }

        private void BuildStripKeepPossessOptionsSection(Transform parent, float btnH, int font, float s)
        {
            _stripKeepPossOptionsHost = new GameObject("PossessOptions");
            _stripKeepPossOptionsHost.transform.SetParent(parent, false);
            VerticalLayoutGroup v = UI.AddVLG(_stripKeepPossOptionsHost, spacing: UI.GapHair(s), padding: UI.Pad(0, 0, 0, 0));
            v.childForceExpandHeight = false;
            UI.AddLE(_stripKeepPossOptionsHost, flexibleWidth: 1f, flexibleHeight: 0f);

            // Row 1: possessable toggles
            ScrollRect possScroll;
            GameObject possHostGo;
            Transform possContent = StripKeepCreateHScrollChipStrip(
                _stripKeepPossOptionsHost.transform, "PossessRow", btnH * 0.85f, s,
                out possScroll, out possHostGo);

            _stripKeepPossClearToggle = StripKeepBuildOptionToggle(
                possContent, btnH * 0.8f, font, s,
                VPBTranslation.T("gallery.creator.strip_poss_clear", "Clear possess"),
                _stripKeepRemovePossessable,
                (on) =>
                {
                    if (_stripKeepSuppressToggleEvents) return;
                    _stripKeepRemovePossessable = on;
                    PersistStripKeepPossessOptionsToConfig();
                    RefreshStripKeepSummaryAndConfirm();
                },
                VPBTranslation.T(
                    "gallery.creator.strip_poss_clear_tip",
                    "Remove all possessable flags on kept Persons after strip."));

            _stripKeepPossMaleToggle = StripKeepBuildOptionToggle(
                possContent, btnH * 0.8f, font, s,
                VPBTranslation.T("gallery.creator.strip_poss_male", "+Male possess"),
                _stripKeepAddPossessableMale,
                (on) =>
                {
                    if (_stripKeepSuppressToggleEvents) return;
                    _stripKeepAddPossessableMale = on;
                    PersistStripKeepPossessOptionsToConfig();
                    RefreshStripKeepSummaryAndConfirm();
                },
                VPBTranslation.T(
                    "gallery.creator.strip_poss_male_tip",
                    "After clear: enable head + hands possessable on male Persons."));

            _stripKeepPossFemaleToggle = StripKeepBuildOptionToggle(
                possContent, btnH * 0.8f, font, s,
                VPBTranslation.T("gallery.creator.strip_poss_female", "+Female possess"),
                _stripKeepAddPossessableFemale,
                (on) =>
                {
                    if (_stripKeepSuppressToggleEvents) return;
                    _stripKeepAddPossessableFemale = on;
                    PersistStripKeepPossessOptionsToConfig();
                    RefreshStripKeepSummaryAndConfirm();
                },
                VPBTranslation.T(
                    "gallery.creator.strip_poss_female_tip",
                    "After clear: enable head + hands possessable on female Persons."));

            // Rename mode cycle
            string renLbl = StripKeepPersonRenameModeLabel(_stripKeepPersonRenameMode);
            GameObject renGo = StripKeepChromeButton(
                possContent, StripKeepChipWidth(renLbl, font, s), btnH * 0.8f,
                renLbl, font, s,
                new Color(0.30f, 0.26f, 0.18f, 1f), StripKeepCyclePersonRenameMode);
            if (renGo != null)
            {
                _stripKeepRenameModeBtnLabel = renGo.GetComponentInChildren<Text>();
                AddTooltipPlain(renGo,
                    VPBTranslation.T(
                        "gallery.creator.strip_rename_mode_tip",
                        "Cycle: Off → Actor# → Prefix M_/F_ → Male/Female names. Applies to kept Persons."));
            }
        }

        private Toggle StripKeepBuildOptionToggle(
            Transform parent, float h, int font, float s,
            string label, bool isOn, UnityEngine.Events.UnityAction<bool> onChanged, string tip)
        {
            GameObject host = new GameObject("Opt_" + label);
            host.transform.SetParent(parent, false);
            float w = StripKeepChipWidth(label, font, s) + 28f * s;
            UI.AddLE(host, minWidth: w, preferredWidth: w, minHeight: h, preferredHeight: h, flexibleWidth: 0f);
            UI.AddImage(host, new Color(0.16f, 0.17f, 0.20f, 1f));

            _stripKeepSuppressToggleEvents = true;
            GameObject toggleGo = UI.CreateUIToggle(
                host, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll, onChanged);
            StripKeepFillInParent(toggleGo);
            Toggle t = toggleGo.GetComponent<Toggle>();
            if (t != null) t.isOn = isOn;
            _stripKeepSuppressToggleEvents = false;

            Text tl = toggleGo.GetComponentInChildren<Text>();
            if (tl != null)
            {
                GalleryUiMetrics.ApplyFont(tl, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                tl.raycastTarget = false;
                StripKeepClampText(tl);
                RectTransform lrt = tl.GetComponent<RectTransform>();
                if (lrt != null)
                {
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(28f * s, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
            }
            if (!string.IsNullOrEmpty(tip))
                AddTooltipPlain(host, tip);
            return t;
        }

        private void RefreshStripKeepPossessOptionsChrome()
        {
            _stripKeepSuppressToggleEvents = true;
            try
            {
                if (_stripKeepPossClearToggle != null
                    && _stripKeepPossClearToggle.isOn != _stripKeepRemovePossessable)
                    _stripKeepPossClearToggle.isOn = _stripKeepRemovePossessable;
                if (_stripKeepPossMaleToggle != null
                    && _stripKeepPossMaleToggle.isOn != _stripKeepAddPossessableMale)
                    _stripKeepPossMaleToggle.isOn = _stripKeepAddPossessableMale;
                if (_stripKeepPossFemaleToggle != null
                    && _stripKeepPossFemaleToggle.isOn != _stripKeepAddPossessableFemale)
                    _stripKeepPossFemaleToggle.isOn = _stripKeepAddPossessableFemale;
            }
            catch { }
            _stripKeepSuppressToggleEvents = false;
            RefreshStripKeepRenameModeButtonLabel();
        }

        private void RefreshStripKeepRenameModeButtonLabel()
        {
            if (_stripKeepRenameModeBtnLabel == null) return;
            string label = StripKeepPersonRenameModeLabel(_stripKeepPersonRenameMode);
            _stripKeepRenameModeBtnLabel.text = label;
            LayoutElement le = _stripKeepRenameModeBtnLabel.transform.parent != null
                ? _stripKeepRenameModeBtnLabel.transform.parent.GetComponent<LayoutElement>()
                : null;
            if (le != null)
            {
                float s = ChromeScale;
                int font = new GalleryModalTypography(s).Body;
                float w = StripKeepChipWidth(label, font, s);
                le.minWidth = w;
                le.preferredWidth = w;
            }
        }

        /// <summary>Append possess/rename summary fragment when options active.</summary>
        private string StripKeepPossessSummarySuffix()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(48);
            if (_stripKeepRemovePossessable)
                sb.Append("  ·  Clear possess");
            if (_stripKeepAddPossessableMale && _stripKeepAddPossessableFemale)
                sb.Append("  ·  +M/F hands");
            else if (_stripKeepAddPossessableMale)
                sb.Append("  ·  +Male hands");
            else if (_stripKeepAddPossessableFemale)
                sb.Append("  ·  +Female hands");
            if (_stripKeepPersonRenameMode != StripKeepPersonRenameMode.Off)
                sb.Append("  ·  ").Append(StripKeepPersonRenameModeLabel(_stripKeepPersonRenameMode));
            return sb.ToString();
        }
    }
}
