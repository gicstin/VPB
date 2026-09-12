using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const int UpdaterVersionCollapsedCount = 12;

        private bool _updaterVersionListOpen;
        private bool _updaterVersionShowAll;

        private void AppendUpdaterVersionSettings(List<InternalSettingDefinition> defs, VpbUpdaterService updater)
        {
            var catalog = updater.ReleaseCatalog;
            if (catalog == null || catalog.IsEmpty)
            {
                AppendUpdaterVersionUnavailableRows(defs, updater);
                return;
            }

            defs.Add(new InternalSettingDefinition
            {
                Key = "updater.version.state",
                GroupKey = "updater",
                Label = VPBTranslation.T("settings.updater.version", "Version"),
                Tooltip = VPBTranslation.T("settings.tip.updater.version",
                    "Which VPB build this install follows. Pin an earlier build to compare behaviour against it; a pinned build never auto-updates until you return to latest."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                GetString = () => DescribeUpdaterVersionState(updater)
            });

            defs.Add(new InternalSettingDefinition
            {
                Key = "updater.version.browse",
                GroupKey = "updater",
                Label = _updaterVersionListOpen
                    ? VPBTranslation.T("settings.updater.version_hide", "Hide earlier builds")
                    : string.Format(
                        VPBTranslation.T("settings.updater.version_browse", "Choose an earlier build ({0} on {1})"),
                        catalog.Releases.Length,
                        updater.Config.Branch ?? VpbUpdateConfig.DefaultBranch),
                Tooltip = string.Format(
                    VPBTranslation.T("settings.tip.updater.version_browse",
                        "The {0} builds published on {1} that you can roll back to. Each branch publishes its own list. Older builds are not offered: {2} moved the plugin into a single folder and the patcher only migrates forward."),
                    catalog.Releases.Length,
                    updater.Config.Branch ?? VpbUpdateConfig.DefaultBranch,
                    catalog.MinRollbackVersion),
                ControlType = InternalSettingControlType.Button,
                OnAction = () =>
                {
                    _updaterVersionListOpen = !_updaterVersionListOpen;
                    if (!_updaterVersionListOpen) _updaterVersionShowAll = false;
                    InvalidateInternalSettingsDefsCache();
                    RefreshInternalSettingsListRows(true);
                }
            });

            if (updater.IsPinned) AppendUpdaterUnpinRow(defs, updater);

            if (!_updaterVersionListOpen) return;

            var releases = catalog.Releases;
            int shown = releases.Length;
            if (!_updaterVersionShowAll && shown > UpdaterVersionCollapsedCount)
                shown = UpdaterVersionCollapsedCount;

            for (int i = 0; i < shown; i++)
            {
                VpbRelease release = releases[i];
                bool isCurrent = string.Equals(release.Version, PluginVersionInfo.Version, StringComparison.Ordinal);
                bool isPinned = updater.IsPinned
                    && string.Equals(release.Version, updater.PinnedVersion, StringComparison.Ordinal);

                defs.Add(new InternalSettingDefinition
                {
                    Key = "updater.version.pick." + release.Version,
                    GroupKey = "updater",
                    Label = FormatUpdaterReleaseLabel(release, isCurrent, isPinned),
                    Tooltip = BuildUpdaterReleaseTooltip(release),
                    ControlType = InternalSettingControlType.Button,
                    ActionEnabled = () => !isPinned,
                    OnAction = () =>
                    {
                        string refused = updater.PinToRelease(release);
                        if (refused != null)
                        {
                            ShowTemporaryStatus(refused, 6f);
                            return;
                        }

                        _updaterVersionListOpen = false;

                        string risk = VpbUpdaterService.DescribeSchemaRisk(release.Schema);
                        ShowTemporaryStatus(
                            string.Format(
                                VPBTranslation.T("settings.updater.pinned_status",
                                    "Pinned to {0}. Check for updates, then restart VaM."),
                                release.Version)
                            + (risk == null ? "" : "  -  " + risk),
                            risk == null ? 6f : 10f);

                        InvalidateInternalSettingsDefsCache();
                        RefreshInternalSettingsListRows(true);
                    }
                });
            }

            if (releases.Length <= UpdaterVersionCollapsedCount) return;

            defs.Add(new InternalSettingDefinition
            {
                Key = "updater.version.more",
                GroupKey = "updater",
                Label = _updaterVersionShowAll
                    ? string.Format(
                        VPBTranslation.T("settings.updater.version_show_fewer", "Show only the {0} newest"),
                        UpdaterVersionCollapsedCount)
                    : string.Format(
                        VPBTranslation.T("settings.updater.version_show_all", "Show all {0} builds ({1} older)"),
                        releases.Length, releases.Length - UpdaterVersionCollapsedCount),
                Tooltip = VPBTranslation.T("settings.tip.updater.version_show_all",
                    "The newest builds are listed first because they are what you usually want. Expand for the full published history on this branch."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () =>
                {
                    _updaterVersionShowAll = !_updaterVersionShowAll;
                    InvalidateInternalSettingsDefsCache();
                    RefreshInternalSettingsListRows(true);
                }
            });
        }

        private void AppendUpdaterVersionUnavailableRows(List<InternalSettingDefinition> defs, VpbUpdaterService updater)
        {
            _updaterVersionListOpen = false;
            _updaterVersionShowAll = false;

            string branch = updater.Config.Branch ?? VpbUpdateConfig.DefaultBranch;
            bool fetching = updater.ReleaseCatalogState == VpbCatalogState.Fetching
                || updater.ReleaseCatalogState == VpbCatalogState.Unknown;

            defs.Add(new InternalSettingDefinition
            {
                Key = "updater.version.state",
                GroupKey = "updater",
                Label = VPBTranslation.T("settings.updater.version", "Version"),
                Tooltip = fetching
                    ? VPBTranslation.T("settings.tip.updater.version_loading",
                        "The list of builds is published per branch. It is being fetched for the branch you selected.")
                    : VPBTranslation.T("settings.tip.updater.version_unavailable",
                        "This branch does not publish a list of builds, or it could not be reached. Updating still works normally - only rolling back to an earlier build needs the list."),
                ControlType = InternalSettingControlType.ReadOnlyText,
                GetString = () => updater.IsPinned
                    ? DescribeUpdaterVersionState(updater)
                    : (fetching
                        ? string.Format(
                            VPBTranslation.T("settings.updater.version_loading", "Loading builds for {0}..."),
                            branch)
                        : string.Format(
                            VPBTranslation.T("settings.updater.version_unavailable",
                                "No build list published on {0} - rollback unavailable"),
                            branch))
            });

            if (!fetching)
            {
                defs.Add(new InternalSettingDefinition
                {
                    Key = "updater.version.retry",
                    GroupKey = "updater",
                    Label = VPBTranslation.T("settings.updater.version_retry", "Retry loading builds"),
                    Tooltip = VPBTranslation.T("settings.tip.updater.version_retry",
                        "Ask GitHub again for this branch's list of builds. Useful after a dropped connection; it will not help if the branch simply has none."),
                    ControlType = InternalSettingControlType.Button,
                    OnAction = () =>
                    {
                        updater.FetchReleasesAsync();
                        InvalidateInternalSettingsDefsCache();
                        RefreshInternalSettingsListRows(true);
                    }
                });
            }

            if (updater.IsPinned) AppendUpdaterUnpinRow(defs, updater);
        }

        private void AppendUpdaterUnpinRow(List<InternalSettingDefinition> defs, VpbUpdaterService updater)
        {
            bool stranded = updater.PinnedRefMissing;

            defs.Add(new InternalSettingDefinition
            {
                Key = "updater.version.latest",
                GroupKey = "updater",
                Label = stranded
                    ? string.Format(
                        VPBTranslation.T("settings.updater.version_unpin_stranded", "Pinned build {0} is gone - return to latest"),
                        updater.PinnedVersion ?? "?")
                    : string.Format(
                        VPBTranslation.T("settings.updater.version_unpin", "Return to latest ({0})"),
                        updater.Config.Branch ?? VpbUpdateConfig.DefaultBranch),
                Tooltip = stranded
                    ? VPBTranslation.T("settings.tip.updater.version_unpin_stranded",
                        "The build you pinned is no longer published, so update checks against it fail. Returning to latest is the way out; your installed files are untouched until you check for updates.")
                    : VPBTranslation.T("settings.tip.updater.version_unpin",
                        "Stop following the pinned build and track the update branch again. Check for updates afterwards to pull it."),
                ControlType = InternalSettingControlType.Button,
                OnAction = () =>
                {
                    updater.UnpinToLatest();
                    _updaterVersionListOpen = false;
                    ShowTemporaryStatus(VPBTranslation.T("settings.updater.unpinned",
                        "Following latest again. Check for updates to pull it."), 5f);
                    InvalidateInternalSettingsDefsCache();
                    RefreshInternalSettingsListRows(true);
                }
            });
        }

        private static string DescribeUpdaterVersionState(VpbUpdaterService updater)
        {
            if (updater.IsPinned && updater.PinnedRefMissing)
            {
                return string.Format(
                    VPBTranslation.T("settings.updater.state_pinned_gone",
                        "Pinned to {0}, which is no longer published"),
                    updater.PinnedVersion ?? "?");
            }

            if (updater.IsPinned)
            {
                string pinned = updater.PinnedVersion ?? "?";
                string text = string.Format(
                    VPBTranslation.T("settings.updater.state_pinned", "Pinned to {0} (no auto-updates)"), pinned);

                if (!updater.PinnedReleaseIsListed())
                {
                    text += "  -  " + VPBTranslation.T("settings.updater.state_pinned_unlisted",
                        "no longer in the release list, but still installable");
                }

                string risk = VpbUpdaterService.DescribeSchemaRisk(updater.Config.PinnedSchema);
                return risk == null ? text : text + "  -  " + risk;
            }

            string state = string.Format(
                VPBTranslation.T("settings.updater.state_following", "Following {0} ({1})"),
                updater.Config.Branch ?? VpbUpdateConfig.DefaultBranch,
                PluginVersionInfo.Version);

            var catalog = updater.ReleaseCatalog;
            if (catalog != null)
            {
                VpbRelease running = catalog.Find(PluginVersionInfo.Version);
                if (running != null)
                {
                    string age = FormatUpdaterAge(running);
                    if (age.Length > 0) state += "  -  " + age;
                }
            }

            return state;
        }

        private static string FormatUpdaterAge(VpbRelease release)
        {
            int days = release.DaysAgo;
            if (days < 0) return "";
            if (days == 0) return VPBTranslation.T("settings.updater.age_today", "today");
            if (days == 1) return VPBTranslation.T("settings.updater.age_yesterday", "yesterday");
            return string.Format(VPBTranslation.T("settings.updater.age_days", "{0}d ago"), days);
        }

        private static string FormatUpdaterReleaseLabel(VpbRelease release, bool isCurrent, bool isPinned)
        {
            string label = release.Version;

            string date = release.DatePart;
            string age = FormatUpdaterAge(release);
            if (date.Length > 0)
            {
                label += "   " + date;
                if (age.Length > 0) label += "  (" + age + ")";
            }
            else if (age.Length > 0)
            {
                label += "   " + age;
            }

            if (isPinned) return label + "   " + VPBTranslation.T("settings.updater.tag_pinned", "- pinned");
            if (isCurrent) return label + "   " + VPBTranslation.T("settings.updater.tag_running", "- running now");
            return label;
        }

        private static string BuildUpdaterReleaseTooltip(VpbRelease release)
        {
            string notes = release.Notes;
            if (string.IsNullOrEmpty(notes))
                notes = VPBTranslation.T("settings.updater.no_notes", "No release notes.");

            string schema = release.Schema > 0
                ? string.Format(VPBTranslation.T("settings.updater.schema_line", "Database schema {0}."), release.Schema)
                : VPBTranslation.T("settings.updater.schema_unknown", "Database schema unknown for this build.");

            string risk = VpbUpdaterService.DescribeSchemaRisk(release.Schema);

            string when = release.DatePart;
            string age = FormatUpdaterAge(release);
            if (when.Length > 0 && age.Length > 0) when += "  (" + age + ")";
            else if (when.Length == 0) when = age;
            if (when.Length > 0) when = string.Format(
                VPBTranslation.T("settings.updater.released_line", "Released {0}."), when) + "\n";

            return notes + "\n\n" + when + schema + (risk == null ? "" : "\n" + risk) + "\n\n"
                + string.Format(VPBTranslation.T("settings.updater.pick_hint", "Pin {0} and check for updates to apply it."),
                    release.Version);
        }
    }
}
