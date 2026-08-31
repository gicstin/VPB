using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const string SettingsDefaultGroupKey = "appearance";
        private const string SettingsHeaderRowKeyPrefix = "#hdr:";

        private readonly Dictionary<string, int> _settingsMatchCountByGroup =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _settingsFloatScrollToGroup;
        private GameObject _settingsFloatModifiedBtn;
        private Image _settingsFloatModifiedBtnBg;
        private Text _settingsFloatModifiedBtnText;
        private GameObject _settingsFloatEmptyGo;
        private Text _settingsFloatEmptyText;
        private GameObject _settingsFloatEmptyClearBtn;
        private GameObject _settingsFloatRevertBtn;
        private GameObject _settingsFloatCloseBtn;

        private static string SettingsSubGroupLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            switch (key)
            {
                case "visuals": return VPBTranslation.T("settings.section.visuals", "Look");
                case "hover": return VPBTranslation.T("settings.section.hover", "Hover preview");
                case "grid": return VPBTranslation.T("settings.section.grid", "Grid");
                case "scan_wl_border": return VPBTranslation.T("settings.section.scan_wl", "Scan whitelist");
                case "scan_wl_temp": return VPBTranslation.T("settings.section.scan_wl_temp", "Temporary whitelist");
                case "follow": return VPBTranslation.T("settings.section.follow", "Follow");
                case "desktop": return VPBTranslation.T("settings.section.desktop", "Desktop");
                case "lists": return VPBTranslation.T("settings.section.lists", "Lists");
                case "cat_general": return VPBTranslation.T("settings.section.cat_general", "Categories");
                case "tags": return VPBTranslation.T("settings.section.tags", "Tags");
                case "search": return VPBTranslation.T("settings.section.search", "Search");
                case "options": return VPBTranslation.T("settings.section.options", "Options");
                case "visibility": return VPBTranslation.T("settings.section.visibility", "Visibility");
                case "keys_rules": return VPBTranslation.T("settings.section.keys_rules", "When shortcuts are active");
                case "plugin_hotkeys": return VPBTranslation.T("settings.section.keys_global", "Global (work anywhere)");
                case "keys_chrome": return VPBTranslation.T("settings.section.keys_chrome", "Gallery window");
                case "keys_browse": return VPBTranslation.T("settings.section.keys_browse", "Browse & search");
                case "keys_selection": return VPBTranslation.T("settings.section.keys_selection", "Selection");
                case "keys_tools": return VPBTranslation.T("settings.section.keys_tools", "Scene tools");
                case "keys_world": return VPBTranslation.T("settings.section.keys_world", "World navigation");
                case "plugin_quickmenu": return VPBTranslation.T("settings.section.quickmenu", "Quick menu");
                case "plugin_zstd": return VPBTranslation.T("settings.section.zstd", "Texture cache");
                case "plugin_scan_whitelist": return VPBTranslation.T("settings.section.scan_whitelist", "Scan whitelist");
                case "helpers": return VPBTranslation.T("settings.section.helpers", "Helpers");
                case "updater": return VPBTranslation.T("settings.section.updater", "Updater");
                case "ba_migration": return VPBTranslation.T("settings.section.ba_migration", "BrowserAssistant");
                default: return "";
            }
        }

        private static string SettingsSectionKey(InternalSettingDefinition def)
        {
            if (def == null) return "";
            if (def.Key != null && def.Key.StartsWith("scanWlTemp", StringComparison.Ordinal))
                return "scan_wl_temp";
            return def.SubGroupKey ?? "";
        }

        private static bool SettingsDefMatchesFinder(InternalSettingDefinition def, string f)
        {
            if (def == null) return false;
            if (string.IsNullOrEmpty(f)) return true;
            if (!string.IsNullOrEmpty(def.Label) && def.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(def.Tooltip) && def.Tooltip.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(def.Key) && def.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (def.ControlType == InternalSettingControlType.Hotkey && def.GetString != null)
            {
                try
                {
                    string bound = def.GetString();
                    if (!string.IsNullOrEmpty(bound) && bound.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private bool SettingsDefPassesViewFilters(InternalSettingDefinition def, string f, bool searching)
        {
            if (def == null) return false;
            try
            {
                if (def.RowVisible != null && !def.RowVisible()) return false;
            }
            catch { }
            if (!searching
                && !string.Equals(currentSettingsGroup, def.GroupKey, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!SettingsDefMatchesFinder(def, f)) return false;
            if (settingsModifiedOnly)
            {
                if (def.ControlType == InternalSettingControlType.Button) return false;
                if (SettingsRowIsAtDefault(def)) return false;
            }
            return true;
        }

        private void FillSettingsMatchCounts(string f)
        {
            _settingsMatchCountByGroup.Clear();
            var defs = GetInternalSettingDefinitionsCached();
            for (int i = 0; i < defs.Count; i++)
            {
                InternalSettingDefinition def = defs[i];
                if (def == null || string.IsNullOrEmpty(def.GroupKey)) continue;
                try
                {
                    if (def.RowVisible != null && !def.RowVisible()) continue;
                }
                catch { }
                if (!SettingsDefMatchesFinder(def, f)) continue;
                if (settingsModifiedOnly)
                {
                    if (def.ControlType == InternalSettingControlType.Button) continue;
                    if (SettingsRowIsAtDefault(def)) continue;
                }
                int n;
                _settingsMatchCountByGroup.TryGetValue(def.GroupKey, out n);
                _settingsMatchCountByGroup[def.GroupKey] = n + 1;
            }
        }

        private int SettingsMatchCountForGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return 0;
            int n;
            return _settingsMatchCountByGroup.TryGetValue(group, out n) ? n : 0;
        }

        private int SettingsMatchCountOtherGroups(string exceptGroup)
        {
            int sum = 0;
            foreach (var kv in _settingsMatchCountByGroup)
            {
                if (string.Equals(kv.Key, exceptGroup, StringComparison.OrdinalIgnoreCase)) continue;
                sum += kv.Value;
            }
            return sum;
        }

        private bool SettingsFinderIsActive()
        {
            string f = (CanonicalSettingsSideSearchText() ?? "").Trim();
            return !string.IsNullOrEmpty(f);
        }

        private bool _settingsGroupExplicit;

        /// <summary>Resolve display group. Never "all" — last-used or Appearance.</summary>
        private void SetActiveSettingsGroup(string key)
        {
            if (string.IsNullOrEmpty(key)
                || string.Equals(key, "all", StringComparison.OrdinalIgnoreCase))
            {
                currentSettingsGroup = SettingsDefaultGroupKey;
                return;
            }
            foreach (var row in SettingsGroupStructure)
            {
                if (row == null || row.Length == 0) continue;
                if (string.Equals(row[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    currentSettingsGroup = row[0];
                    PersistSettingsLastGroup();
                    return;
                }
            }
            string group;
            if (SettingsFineToGroup().TryGetValue(key, out group) && !string.IsNullOrEmpty(group))
            {
                currentSettingsGroup = group;
                PersistSettingsLastGroup();
                return;
            }
            currentSettingsGroup = SettingsDefaultGroupKey;
        }

        private void ResolveSettingsLastGroupOnOpen()
        {
            if (_settingsGroupExplicit)
            {
                _settingsGroupExplicit = false;
                EnsureCurrentSettingsGroupIsVisibleTab();
                PersistSettingsLastGroup();
                return;
            }
            string want = currentSettingsGroup;
            try
            {
                if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.GallerySettingsLastGroup))
                    want = VPBConfig.Instance.GallerySettingsLastGroup;
            }
            catch { }
            SetActiveSettingsGroup(want);
            EnsureCurrentSettingsGroupIsVisibleTab();
        }

        private void EnsureCurrentSettingsGroupIsVisibleTab()
        {
            List<SettingsGroupTab> tabs = GetSettingsGroupTabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                SettingsGroupTab g = tabs[i];
                if (g != null && string.Equals(currentSettingsGroup, g.Key, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            currentSettingsGroup = SettingsDefaultGroupKey;
        }

        private void PersistSettingsLastGroup()
        {
            if (string.IsNullOrEmpty(currentSettingsGroup)
                || string.Equals(currentSettingsGroup, "all", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (string.Equals(VPBConfig.Instance.GallerySettingsLastGroup, currentSettingsGroup, StringComparison.OrdinalIgnoreCase))
                    return;
                VPBConfig.Instance.GallerySettingsLastGroup = currentSettingsGroup;
            }
            catch { return; }
            try { ScheduleQuickFiltersConfigSave(); } catch { }
        }

        private InternalSettingRowEntry MakeSettingsHeaderRow(string sectionKey, string groupKey, string label, bool categoryHeader)
        {
            var row = new InternalSettingRowEntry(SettingsHeaderRowKeyPrefix + sectionKey, groupKey, label ?? "");
            row.IsHeader = true;
            row.IsCategoryHeader = categoryHeader;
            return row;
        }

        private void RebuildSettingsFloatSidebar(int font, float s, float chromeSz)
        {
            if (_settingsFloatGroupTabsParent == null) return;
            for (int c = _settingsFloatGroupTabsParent.childCount - 1; c >= 0; c--)
            {
                try
                {
                    GameObject go = _settingsFloatGroupTabsParent.GetChild(c).gameObject;
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
                catch { }
            }

            EnsureCurrentSettingsGroupIsVisibleTab();
            string f = (CanonicalSettingsSideSearchText() ?? "").Trim();
            bool searching = !string.IsNullOrEmpty(f);
            bool showCounts = searching || settingsModifiedOnly;
            FillSettingsMatchCounts(f);

            List<SettingsGroupTab> tabs = GetSettingsGroupTabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                SettingsGroupTab g = tabs[i];
                if (g == null) continue;
                AddSettingsSidebarRow(g.Key, g.Label, g.Icon, font, s, chromeSz, showCounts);
            }

            try { UI.ApplyFloatRootHoverPolicy(_settingsFloatGroupTabsParent.gameObject); } catch { }
        }

        private void AddSettingsSidebarRow(
            string key, string label, string iconRole, int font, float s, float chromeSz, bool showCounts)
        {
            bool active = string.Equals(currentSettingsGroup, key, StringComparison.OrdinalIgnoreCase);
            Color bg = active ? SettingsFloatGroupActive : SettingsFloatGroupInactive;
            int count = SettingsMatchCountForGroup(key);
            string shown = label ?? key;
            if (showCounts) shown = shown + "  " + count.ToString();

            string capturedKey = key;
            GameObject btn = UI.CreateChromeLayoutButton(
                _settingsFloatGroupTabsParent, 0f, chromeSz, shown, font, bg,
                () => OnSettingsSidebarGroupClicked(capturedKey));
            if (btn == null) return;

            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 48f * s;
            le.preferredHeight = chromeSz;
            le.minHeight = chromeSz;
            ApplySettingsGroupChipIcon(btn, iconRole, s);

            Text chipTxt = btn.GetComponentInChildren<Text>(true);
            if (chipTxt != null)
            {
                chipTxt.alignment = TextAnchor.MiddleLeft;
                chipTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
                chipTxt.verticalOverflow = VerticalWrapMode.Truncate;
                LayoutElement tle = chipTxt.GetComponent<LayoutElement>();
                if (tle != null)
                {
                    tle.flexibleWidth = 1f;
                    tle.preferredWidth = 0f;
                }
                if (showCounts && count <= 0 && !active)
                    chipTxt.color = GalleryUiColorTokens.TextDim;
            }
        }

        private void OnSettingsSidebarGroupClicked(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            try { CancelPluginHotkeyCapture(false); } catch { }
            bool searching = SettingsFinderIsActive();
            currentSettingsGroup = key;
            PersistSettingsLastGroup();
            if (searching)
                _settingsFloatScrollToGroup = key;
            RefreshInternalSettingsListRows(false);
        }

        private void ToggleSettingsModifiedOnly()
        {
            settingsModifiedOnly = !settingsModifiedOnly;
            SyncSettingsModifiedOnlyButton();
            RefreshInternalSettingsListRows(true);
        }

        private void SyncSettingsModifiedOnlyButton()
        {
            if (_settingsFloatModifiedBtnBg != null)
            {
                _settingsFloatModifiedBtnBg.color = settingsModifiedOnly
                    ? SettingsFloatGroupActive
                    : GalleryUiColorTokens.ChromeIconWell;
            }
            if (_settingsFloatModifiedBtnText != null)
            {
                _settingsFloatModifiedBtnText.color = settingsModifiedOnly
                    ? Color.white
                    : GalleryUiColorTokens.TextDim;
            }
        }

        private void SyncSettingsFloatEmptyState(int rowCount, float s, int font)
        {
            if (_settingsFloatEmptyGo == null) return;
            bool empty = rowCount <= 0;
            if (_settingsFloatEmptyGo.activeSelf != empty)
                _settingsFloatEmptyGo.SetActive(empty);
            if (!empty) return;

            string f = (CanonicalSettingsSideSearchText() ?? "").Trim();
            string msg;
            if (!string.IsNullOrEmpty(f))
            {
                msg = VPBTranslation.T("settings.empty.no_match", "No settings match") + " \"" + f + "\"";
            }
            else if (settingsModifiedOnly)
            {
                msg = VPBTranslation.T("settings.empty.no_modified", "No modified settings in this category");
                int other = SettingsMatchCountOtherGroups(currentSettingsGroup);
                if (other > 0)
                    msg = msg + "\n" + other.ToString() + " "
                        + VPBTranslation.T("settings.empty.other_modified", "modified in other categories");
            }
            else
            {
                msg = VPBTranslation.T("settings.empty.none", "No settings in this category");
            }
            if (_settingsFloatEmptyText != null)
            {
                _settingsFloatEmptyText.text = msg;
                GalleryUiMetrics.ApplyFont(_settingsFloatEmptyText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            if (_settingsFloatEmptyClearBtn != null)
            {
                bool showClear = !string.IsNullOrEmpty(f) || settingsModifiedOnly;
                if (_settingsFloatEmptyClearBtn.activeSelf != showClear)
                    _settingsFloatEmptyClearBtn.SetActive(showClear);
                Text t = _settingsFloatEmptyClearBtn.GetComponentInChildren<Text>(true);
                if (t != null)
                {
                    t.text = !string.IsNullOrEmpty(f)
                        ? VPBTranslation.T("settings.empty.clear", "Clear search")
                        : VPBTranslation.T("settings.empty.show_all", "Show all");
                }
            }
        }

        private void ClearSettingsFinderAndModified()
        {
            SetSettingsFloatFilterTextWithoutNotify("");
            settingsFilter = "";
            settingsModifiedOnly = false;
            SyncSettingsModifiedOnlyButton();
            RefreshInternalSettingsListRows(true);
        }

        /// <summary>After rows built: jump to first row of <see cref="_settingsFloatScrollToGroup"/>.</summary>
        private void ApplySettingsFloatScrollToGroup()
        {
            string want = _settingsFloatScrollToGroup;
            _settingsFloatScrollToGroup = null;
            if (string.IsNullOrEmpty(want) || _settingsFloatScrollRect == null) return;
            int n = _settingsFloatRowQueue.Count;
            if (n <= 0) return;
            int idx = -1;
            for (int i = 0; i < n; i++)
            {
                InternalSettingRowEntry row = _settingsFloatRowQueue[i];
                if (row == null) continue;
                if (!string.Equals(row.GroupKey, want, StringComparison.OrdinalIgnoreCase)) continue;
                idx = i;
                break;
            }
            if (idx < 0) return;

            float spacing = _settingsFloatRowsSpacing;
            float pad = SettingsFloatRowsPadRef * (_settingsFloatRowsScale > 0f ? _settingsFloatRowsScale : 1f);
            float y = pad;
            for (int i = 0; i < idx && i < _settingsFloatRowHeights.Count; i++)
                y += _settingsFloatRowHeights[i] + spacing;

            float rowsH = 0f;
            for (int i = 0; i < _settingsFloatRowHeights.Count; i++)
                rowsH += _settingsFloatRowHeights[i];
            float totalH = rowsH + spacing * Mathf.Max(0, n - 1) + pad * 2f;
            float viewH = SettingsFloatViewportHeight(_settingsFloatRowsScale);
            float range = Mathf.Max(0f, totalH - viewH);
            if (range <= 1f)
            {
                _settingsFloatScrollRect.verticalNormalizedPosition = 1f;
                return;
            }
            _settingsFloatScrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - (y / range));
        }
    }
}
