using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void AppendPackageVersionSettingDefinitions(List<InternalSettingDefinition> defs)
        {
            if (defs == null) return;

            var forceLatest = new InternalSettingDefinition
            {
                Key = "pkg_versions.forceLatestAll",
                GroupKey = "pkg_versions",
                Label = VPBTranslation.T("settings.pkg_versions.force_latest_all", "Always use newest installed version"),
                Tooltip = VPBTranslation.T("settings.tip.pkg_versions.force_latest_all",
                    "When a scene, preset or package asks for a specific version of another package (Creator.Package.12), load the newest version you have installed instead. "
                    + "Never downgrades: if the requested version is newer than anything installed, it is reported missing and fetched from the Hub as before. "
                    + "The requested version is kept when the newest one does not contain the file, and for items opened from that same package. "
                    + "Takes effect on the next load."),
                ControlType = InternalSettingControlType.Toggle,
                GetBool = () => FileManager.IsForceLatestAllEnabled(),
                SetBool = v => SetForceLatestAllDependencies(v)
            };
            forceLatest.SetDefault(false);
            defs.Add(forceLatest);

            int excludedCount = 0;
            try { excludedCount = DependencyWhitelistManager.Instance.GetWhitelistedPackageGroups().Count; }
            catch { excludedCount = 0; }

            defs.Add(new InternalSettingDefinition
            {
                Key = "pkg_versions.exclusions",
                GroupKey = "pkg_versions",
                Label = string.Format(VPBTranslation.T("settings.pkg_versions.exclusions", "Excluded packages ({0})"), excludedCount),
                Tooltip = VPBTranslation.T("settings.tip.pkg_versions.exclusions",
                    "Packages that always load the exact version a scene asks for. Use this for plugins or assets whose newer versions behave differently."),
                ControlType = InternalSettingControlType.Button,
                ActionLabel = () => VPBTranslation.T("settings.row.manage", "MANAGE"),
                OnAction = () => ShowForceLatestExclusionsModal(),
                RowVisible = () => FileManager.IsForceLatestAllEnabled()
            });
        }

        private void SetForceLatestAllDependencies(bool enabled)
        {
            Settings s = Settings.Instance;
            if (s == null || s.ForceLatestAllDependencies == null) return;
            if (s.ForceLatestAllDependencies.Value == enabled) return;

            s.ForceLatestAllDependencies.Value = enabled;
            bool clearedExact = false;
            if (enabled && s.ForceExactPackageVersions != null && s.ForceExactPackageVersions.Value)
            {
                s.ForceExactPackageVersions.Value = false;
                clearedExact = true;
            }
            try { Settings.SaveConfig(); } catch { }

            string reason = enabled ? "turned on in Settings" : "turned off in Settings";
            if (clearedExact) reason += "; ForceExactPackageVersions turned off because it would block it";
            ApplyPackageVersionPolicyChange(reason);

            if (clearedExact)
            {
                ShowTemporaryStatus(VPBTranslation.T("settings.pkg_versions.exact_cleared",
                    "Exact package versions (VPB.cfg) was turned off: it would stop newest versions from being used."), 5f);
            }
        }

        private void RestorePackageVersionSettings(bool forceLatestAll, bool forceExact)
        {
            Settings s = Settings.Instance;
            if (s == null) return;
            bool changed = false;
            try
            {
                if (s.ForceLatestAllDependencies != null && s.ForceLatestAllDependencies.Value != forceLatestAll)
                {
                    s.ForceLatestAllDependencies.Value = forceLatestAll;
                    changed = true;
                }
                if (s.ForceExactPackageVersions != null && s.ForceExactPackageVersions.Value != forceExact)
                {
                    s.ForceExactPackageVersions.Value = forceExact;
                    changed = true;
                }
            }
            catch { }
            if (!changed) return;
            try { Settings.SaveConfig(); } catch { }
            try { FileManager.NotifyForceLatestPolicyChanged("settings reverted"); } catch { }
            try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
        }

        private void ApplyPackageVersionPolicyChange(string reason)
        {
            try { FileManager.NotifyForceLatestPolicyChanged(reason); } catch { }
            try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
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
    }
}
