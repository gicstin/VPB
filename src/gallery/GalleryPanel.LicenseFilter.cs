using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        private Coroutine _licenseFilterHydrateCo;
        private int _licenseFilterHydrateGen;

        private bool HasLicenseFilter()
        {
            return !string.IsNullOrEmpty(currentLicenseFilter);
        }

        private void ClearLicenseFilter(bool refresh)
        {
            if (!HasLicenseFilter()) return;
            currentLicenseFilter = "";
            CancelLicenseFilterHydrate();
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

            if (refresh)
            {
                CancelLicenseFilterHydrate();
                _licenseFilterHydrateCo = StartCoroutine(LicenseFilterHydrateThenRefreshCo());
            }
            else
            {
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                SyncBrowseFilterChipChrome();
            }
        }

        private void CancelLicenseFilterHydrate()
        {
            _licenseFilterHydrateGen++;
            if (_licenseFilterHydrateCo == null) return;
            try { StopCoroutine(_licenseFilterHydrateCo); } catch { }
            _licenseFilterHydrateCo = null;
        }

        /// <summary>
        /// Hydrate package licenses once per package (not per grid row), then refresh.
        /// Yields so large libraries do not stall the main thread past Doherty threshold.
        /// </summary>
        private IEnumerator LicenseFilterHydrateThenRefreshCo()
        {
            int gen = ++_licenseFilterHydrateGen;
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.filter.license_loading", "Loading licenses…"),
                    2.5f);
            }
            catch { }

            VarPackage[] snap = null;
            try
            {
                Dictionary<string, VarPackage> byUid = FileManager.PackagesByUid;
                if (byUid != null && byUid.Count > 0)
                {
                    snap = new VarPackage[byUid.Count];
                    int i = 0;
                    foreach (KeyValuePair<string, VarPackage> kv in byUid)
                    {
                        if (gen != _licenseFilterHydrateGen) yield break;
                        snap[i++] = kv.Value;
                    }
                }
            }
            catch { snap = null; }

            if (snap != null)
            {
                const int yieldEvery = 12;
                int n = 0;
                for (int i = 0; i < snap.Length; i++)
                {
                    if (gen != _licenseFilterHydrateGen) yield break;
                    VarPackage pkg = snap[i];
                    if (pkg == null) continue;
                    if (!string.IsNullOrEmpty(pkg.LicenseType)) continue;
                    try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
                    n++;
                    if ((n % yieldEvery) == 0)
                        yield return null;
                }
            }

            if (gen != _licenseFilterHydrateGen) yield break;
            _licenseFilterHydrateCo = null;
            AfterLicenseFilterChanged();
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
