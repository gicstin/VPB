using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject _forceLatestExclModalRoot;
        private Transform _forceLatestExclListParent;
        private Transform _forceLatestExclMatchesParent;
        private Text _forceLatestExclListHdrText;
        private Text _forceLatestExclMatchesHdrText;
        private InputField _forceLatestExclInput;
        private List<string> _forceLatestExclInstalledGroups;
        private bool _forceLatestExclChanged;

        private const int ForceLatestExclMaxMatches = 40;
        private const int ForceLatestExclMinQueryChars = 2;

        public void ShowForceLatestExclusionsModal()
        {
            if (backgroundBoxGO == null) return;
            if (_forceLatestExclModalRoot != null)
            {
                _forceLatestExclModalRoot.SetActive(true);
                RebuildForceLatestExclusionRows();
                return;
            }
            _forceLatestExclChanged = false;
            _forceLatestExclInstalledGroups = ReadInstalledPackageGroupsSorted();
            BuildForceLatestExclusionsModal();
        }

        private void HideForceLatestExclusionsModal()
        {
            if (_forceLatestExclModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_forceLatestExclModalRoot); } catch { }
                _forceLatestExclModalRoot = null;
            }
            _forceLatestExclListParent = null;
            _forceLatestExclMatchesParent = null;
            _forceLatestExclListHdrText = null;
            _forceLatestExclMatchesHdrText = null;
            _forceLatestExclInput = null;
            _forceLatestExclInstalledGroups = null;

            if (_forceLatestExclChanged)
            {
                _forceLatestExclChanged = false;
                try { FileManager.LogForceLatestPolicyState("excluded packages changed"); } catch { }
                try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
            }
            try
            {
                if (IsSettingsPanelOpen())
                {
                    InvalidateInternalSettingsDefsCache();
                    RefreshInternalSettingsListRows(true);
                }
            }
            catch { }
        }

        private static List<string> ReadInstalledPackageGroupsSorted()
        {
            var groups = new List<string>(256);
            try { FileManager.CopyPackageGroupIds(groups); } catch { }
            groups.Sort(StringComparer.OrdinalIgnoreCase);
            return groups;
        }

        private void BuildForceLatestExclusionsModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;

            GameObject panel;
            _forceLatestExclModalRoot = UI.CreateModalChrome(
                backgroundBoxGO, "VPB_ForceLatestExclusionsModal", 760f * s, 700f * s,
                new Color(0.06f, 0.06f, 0.08f, 1f), HideForceLatestExclusionsModal, out panel);

            UI.AddVLG(panel, spacing: UI.GapControl(s), padding: UI.PadDialog(s));

            GameObject header = new GameObject("HeaderRow");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.spacing = 8f * s;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            UI.AddLE(header, minHeight: 48f * s, preferredHeight: 48f * s);

            Text title = UI.CreateEmphasisTitleLabel(header, VPBTranslation.T("settings.pkg_versions.exclusions_title", "Excluded packages"), headerFont);
            UI.AddLE(title.gameObject, flexibleWidth: 1f);
            UI.CreateChromeLayoutButton(header.transform, 100f * s, 40f * s, VPBTranslation.T("hook.close", "Close"), bodyFont, new Color(0.44f, 0.36f, 0.20f, 1f), HideForceLatestExclusionsModal);

            Text help = UI.CreateLabel(panel, VPBTranslation.T("settings.pkg_versions.exclusions_help",
                "These packages always load the exact version a scene or preset asks for, even with Always use newest installed version on."),
                bodyFont, new Color(0.82f, 0.84f, 0.88f, 1f), TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, name: "Help");
            UI.AddLE(help.gameObject, minHeight: 48f * s);

            GameObject scrollGO = UI.CreateVScrollableContent(panel, new Color(0.08f, 0.08f, 0.1f, 1f), AnchorPresets.stretchAll, 0f, 280f * s, Vector2.zero, 10f * s, 3f * s, false);
            UI.AddLE(scrollGO, minHeight: 240f * s, flexibleHeight: 1f);
            Transform content = scrollGO.transform.Find("Viewport/Content");
            if (content == null) return;

            VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.padding = new RectOffset(Mathf.RoundToInt(8f * s), Mathf.RoundToInt(8f * s), Mathf.RoundToInt(6f * s), Mathf.RoundToInt(8f * s));
                contentVlg.spacing = 10f * s;
            }

            _forceLatestExclListHdrText = ScanWlCreateSectionHeader(content, VPBTranslation.T("settings.pkg_versions.exclusions_list", "Excluded"), 0, bodyFont, s);
            GameObject list = new GameObject("ExcludedList");
            list.transform.SetParent(content, false);
            UI.AddVLG(list, spacing: UI.GapTight(s));
            _forceLatestExclListParent = list.transform;

            _forceLatestExclMatchesHdrText = ScanWlCreateSectionHeader(content, VPBTranslation.T("settings.pkg_versions.exclusions_matches", "Installed packages matching your search"), 0, bodyFont, s);
            GameObject matches = new GameObject("MatchesList");
            matches.transform.SetParent(content, false);
            UI.AddVLG(matches, spacing: UI.GapTight(s));
            _forceLatestExclMatchesParent = matches.transform;

            GameObject addFooter = new GameObject("AddFooter");
            addFooter.transform.SetParent(panel.transform, false);
            UI.AddImage(addFooter, new Color(0.09f, 0.09f, 0.12f, 1f));
            UI.AddVLG(addFooter, spacing: UI.GapGroup(s), padding: UI.PadGroup(s));

            ScanWlCreateAddBlock(
                addFooter.transform,
                VPBTranslation.T("settings.pkg_versions.exclusions_add", "Search installed packages, or type Creator.Package and press Add"),
                VPBTranslation.T("settings.pkg_versions.exclusions_add_ph", "Creator.Package"),
                bodyFont,
                s,
                out _forceLatestExclInput,
                () =>
                {
                    string raw = _forceLatestExclInput != null ? _forceLatestExclInput.text : null;
                    if (AddForceLatestExclusion(raw))
                    {
                        if (_forceLatestExclInput != null) _forceLatestExclInput.text = "";
                    }
                    else if (!string.IsNullOrEmpty(raw) && raw.Trim().Length > 0)
                    {
                        ShowTemporaryStatus(VPBTranslation.T("settings.pkg_versions.exclusions_invalid",
                            "Not a package name. Use Creator.Package, e.g. MacGruber.Life."), 4f);
                    }
                    RebuildForceLatestExclusionRows();
                });

            if (_forceLatestExclInput != null)
                _forceLatestExclInput.onValueChanged.AddListener(_ => RebuildForceLatestMatchRows());

            RebuildForceLatestExclusionRows();
        }

        private bool AddForceLatestExclusion(string raw)
        {
            string group = ForceLatestDependencyPolicy.NormalizeExclusionEntry(raw);
            if (string.IsNullOrEmpty(group)) return false;
            try
            {
                if (DependencyWhitelistManager.Instance.IsWhitelisted(group)) return true;
                DependencyWhitelistManager.Instance.SetWhitelisted(group, true, true);
                _forceLatestExclChanged = true;
                try { FileManager.NotifyForceLatestPolicyChanged(null); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Could not exclude " + group + " from always-newest versions: " + ex.Message);
                return false;
            }
        }

        private void RemoveForceLatestExclusion(string group)
        {
            if (string.IsNullOrEmpty(group)) return;
            try
            {
                DependencyWhitelistManager.Instance.SetWhitelisted(group, false, true);
                _forceLatestExclChanged = true;
                try { FileManager.NotifyForceLatestPolicyChanged(null); } catch { }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Could not remove " + group + " from the always-newest exclusions: " + ex.Message);
            }
        }

        private void RebuildForceLatestExclusionRows()
        {
            if (_forceLatestExclListParent == null) return;
            UI.DestroyAllChildren(_forceLatestExclListParent);

            float s = ChromeScale;
            int bodyFont = new GalleryModalTypography(s).Body;

            var excluded = new List<string>();
            try { excluded.AddRange(DependencyWhitelistManager.Instance.GetWhitelistedPackageGroups()); }
            catch { }
            excluded.Sort(StringComparer.OrdinalIgnoreCase);

            ScanWlUpdateSectionHeader(_forceLatestExclListHdrText, VPBTranslation.T("settings.pkg_versions.exclusions_list", "Excluded"), excluded.Count);

            if (excluded.Count == 0)
            {
                ScanWlAddPlaceholderRow(_forceLatestExclListParent, VPBTranslation.T("settings.pkg_versions.exclusions_none",
                    "Nothing excluded - every package uses its newest installed version."), bodyFont, s);
            }
            else
            {
                for (int i = 0; i < excluded.Count; i++)
                {
                    string group = excluded[i];
                    string label = DescribeForceLatestGroup(group);
                    GameObject btn = UI.CreateRemovableStripeRow(
                        _forceLatestExclListParent, label, bodyFont, ScanWlRowHeightScale * s, ScanWlRemoveBtnWidthScale * s, UI.GapTight(s),
                        UI.GapControl(s), UI.PadFloatFooter(s), (i & 1) == 1,
                        VPBTranslation.T("hook.remove", "Remove"), () =>
                        {
                            RemoveForceLatestExclusion(group);
                            RebuildForceLatestExclusionRows();
                        });
                    if (btn != null)
                    {
                        try { AddTooltipPlain(btn, VPBTranslation.T("settings.pkg_versions.exclusions_remove_tip", "Let this package use its newest installed version again")); }
                        catch { }
                    }
                }
            }

            RebuildForceLatestMatchRows();
        }

        private void RebuildForceLatestMatchRows()
        {
            if (_forceLatestExclMatchesParent == null) return;
            UI.DestroyAllChildren(_forceLatestExclMatchesParent);

            float s = ChromeScale;
            int bodyFont = new GalleryModalTypography(s).Body;
            string query = _forceLatestExclInput != null ? (_forceLatestExclInput.text ?? "").Trim() : "";

            if (query.Length < ForceLatestExclMinQueryChars)
            {
                ScanWlUpdateSectionHeader(_forceLatestExclMatchesHdrText, VPBTranslation.T("settings.pkg_versions.exclusions_matches", "Installed packages matching your search"), 0);
                ScanWlAddPlaceholderRow(_forceLatestExclMatchesParent, VPBTranslation.T("settings.pkg_versions.exclusions_type_more",
                    "Type at least two letters of a creator or package name below."), bodyFont, s);
                return;
            }

            var matches = new List<string>(ForceLatestExclMaxMatches);
            int total = 0;
            List<string> groups = _forceLatestExclInstalledGroups;
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    string g = groups[i];
                    if (string.IsNullOrEmpty(g) || g.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool already = false;
                    try { already = DependencyWhitelistManager.Instance.IsWhitelisted(g); } catch { }
                    if (already) continue;
                    total++;
                    if (matches.Count < ForceLatestExclMaxMatches) matches.Add(g);
                }
            }

            ScanWlUpdateSectionHeader(_forceLatestExclMatchesHdrText, VPBTranslation.T("settings.pkg_versions.exclusions_matches", "Installed packages matching your search"), total);

            if (matches.Count == 0)
            {
                ScanWlAddPlaceholderRow(_forceLatestExclMatchesParent, VPBTranslation.T("settings.pkg_versions.exclusions_no_match",
                    "No installed package matches. You can still add the exact name with Add."), bodyFont, s);
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                string group = matches[i];
                AddForceLatestMatchRow(group, bodyFont, s, (i & 1) == 1);
            }

            if (total > matches.Count)
            {
                ScanWlAddPlaceholderRow(_forceLatestExclMatchesParent, string.Format(VPBTranslation.T("settings.pkg_versions.exclusions_more",
                    "{0} more - keep typing to narrow the list."), total - matches.Count), bodyFont, s);
            }
        }

        private void AddForceLatestMatchRow(string group, int fontSize, float s, bool altStripe)
        {
            float rowH = ScanWlRowHeightScale * s;
            GameObject row = new GameObject("MatchRow");
            row.transform.SetParent(_forceLatestExclMatchesParent, false);
            UI.AddImage(row, altStripe ? new Color(0.11f, 0.11f, 0.14f, 1f) : new Color(0.09f, 0.09f, 0.11f, 1f));
            UI.AddHLG(row, spacing: UI.GapControl(s), padding: UI.PadFloatFooter(s), childForceExpandWidth: false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text label = UI.CreateLabel(row, DescribeForceLatestGroup(group), fontSize, new Color(0.92f, 0.92f, 0.94f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Label");
            UI.AddLE(label.gameObject, minWidth: 0f, flexibleWidth: 1f);

            UI.CreateChromeLayoutButton(row.transform, ScanWlRemoveBtnWidthScale * s, rowH - UI.GapTight(s),
                VPBTranslation.T("settings.pkg_versions.exclude", "Exclude"), fontSize, new Color(0.22f, 0.42f, 0.58f, 1f), () =>
                {
                    AddForceLatestExclusion(group);
                    RebuildForceLatestExclusionRows();
                });
        }

        private static string DescribeForceLatestGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return "";
            try
            {
                VarPackageGroup g = FileManager.GetPackageGroup(group);
                VarPackage newest = g != null ? g.NewestPackage : null;
                if (newest != null)
                    return group + "   " + string.Format(VPBTranslation.T("settings.pkg_versions.newest_installed", "(newest installed: {0})"), newest.Version);
                return group + "   " + VPBTranslation.T("settings.pkg_versions.not_installed", "(not installed)");
            }
            catch
            {
                return group;
            }
        }
    }
}
