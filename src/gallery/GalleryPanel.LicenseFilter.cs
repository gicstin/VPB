using System;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>VaM PackageBuilder license chooser order (recognition over recall).</summary>
        private static readonly string[] KnownPackageLicenseTypes =
        {
            "PC",
            "PC EA",
            "Questionable",
            "FC",
            "CC BY",
            "CC BY-SA",
            "CC BY-ND",
            "CC BY-NC",
            "CC BY-NC-SA",
            "CC BY-NC-ND"
        };

        private bool HasLicenseFilter()
        {
            return !string.IsNullOrEmpty(currentLicenseFilter);
        }

        private void ClearLicenseFilter(bool refresh)
        {
            if (!HasLicenseFilter()) return;
            currentLicenseFilter = "";
            if (refresh) AfterLicenseFilterChanged();
            else
            {
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                SyncBrowseFilterChipChrome();
            }
        }

        /// <summary>
        /// Arm or toggle off license type filter. Re-pick same type clears.
        /// Forces Source → All (license is .var meta only).
        /// Filter runs in SQLite via <c>pkg.license</c> (no main-thread ZIP hydrate).
        /// </summary>
        private void SetLicenseFilter(string license, bool refresh)
        {
            if (string.IsNullOrEmpty(license))
            {
                ClearLicenseFilter(refresh);
                return;
            }

            string trimmed = license.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                ClearLicenseFilter(refresh);
                return;
            }

            if (string.Equals(currentLicenseFilter, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                ClearLicenseFilter(refresh);
                return;
            }

            currentLicenseFilter = trimmed;

            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                currentGlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.GlobalSourceFilter = VPBConfig.GlobalSourceFilterValue.All;
                    try { VPBConfig.Instance.Save(); } catch { }
                }
            }

            if (refresh) AfterLicenseFilterChanged();
            else
            {
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                SyncBrowseFilterChipChrome();
            }
        }

        private void AfterLicenseFilterChanged()
        {
            try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
            try { RefreshFilesAndTabs(); } catch { RefreshFiles(true); }
            SyncBrowseFilterChipChrome();
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
            {
                try { RebuildGlobalSourceFilterMenuOptions(); } catch { }
            }
        }

        private bool PassesLicenseFilter(FileEntry entry)
        {
            if (!HasLicenseFilter()) return true;
            // SQL path already narrowed cat_mem/pkg rows — skip per-row meta ZIP.
            if (_fileListHadSqlLicenseFilter) return true;
            if (entry == null) return false;

            string lic = ResolveLicenseTypeForFilter(entry);
            if (string.IsNullOrEmpty(lic)) return false;
            return string.Equals(lic, currentLicenseFilter, StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveLicenseTypeForFilter(FileEntry entry)
        {
            VarPackage pkg;
            string label;
            if (!TryGetPackageFromEntry(entry, out pkg, out label) || pkg == null)
                return null;

            if (string.IsNullOrEmpty(pkg.LicenseType))
            {
                try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
            }

            string lic = pkg.LicenseType;
            if (string.IsNullOrEmpty(lic)) return null;
            if (string.Equals(lic, "null", StringComparison.OrdinalIgnoreCase)) return null;
            return lic.Trim();
        }

        private string ResolveBrowseLicenseFilterLabel()
        {
            if (!HasLicenseFilter())
                return VPBTranslation.T("gallery.filter.license", "License");
            return VPBTranslation.T("gallery.filter.license", "License") + ": " + currentLicenseFilter;
        }
    }
}
