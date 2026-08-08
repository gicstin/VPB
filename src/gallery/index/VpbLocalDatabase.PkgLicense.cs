using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    /// <summary>
    /// Persist <c>meta.json</c> licenseType on <c>pkg.license</c> for gallery SQL filters.
    /// Avoids main-thread ZIP hydrate when browsing by license.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        const string PkgLicenseBackfillMetaKey = "pkg_license_backfill_v1";

        /// <summary>Trim; drop empty / literal "null".</summary>
        internal static string NormalizePkgLicense(string license)
        {
            if (string.IsNullOrEmpty(license)) return "";
            string t = license.Trim();
            if (t.Length == 0) return "";
            if (string.Equals(t, "null", StringComparison.OrdinalIgnoreCase)) return "";
            return t;
        }

        /// <summary>Appends AND equality (COLLATE NOCASE); one bind placeholder when filter non-empty.</summary>
        internal static void AppendPkgLicenseFilterSql(StringBuilder sb, string pkgAlias, string licenseFilter)
        {
            if (sb == null) return;
            string lic = NormalizePkgLicense(licenseFilter);
            if (lic.Length == 0) return;
            string a = string.IsNullOrEmpty(pkgAlias) ? "p" : pkgAlias;
            sb.Append(" AND ifnull(").Append(a).Append(".license,'') = ? COLLATE NOCASE");
        }

        internal static string BuildPkgLicenseFilterFragment(string pkgAlias, string licenseFilter)
        {
            string lic = NormalizePkgLicense(licenseFilter);
            if (lic.Length == 0) return "";
            var sb = new StringBuilder(64);
            AppendPkgLicenseFilterSql(sb, pkgAlias, lic);
            return sb.ToString();
        }

        static void EnsurePkgLicenseSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN license TEXT;");
            try
            {
                // NOCASE index matches filter COLLATE so equality can seek.
                conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_pkg_license ON pkg(license COLLATE NOCASE);");
            }
            catch { }
        }

        static bool PkgHasLicenseColumn(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            using (var st = conn.Prepare("SELECT 1 FROM pragma_table_info('pkg') WHERE name='license' LIMIT 1;"))
            {
                return st.Step() == VpbSqlite3.SqliteRow;
            }
        }

        static string ResolveLicenseForInsert(VarPackage pkg)
        {
            if (pkg == null) return "";
            string lic = "";
            try { lic = NormalizePkgLicense(pkg.LicenseType); } catch { lic = ""; }
            if (lic.Length > 0) return lic;
            try
            {
                pkg.TryEnsureMetaJsonLiteFields();
                lic = NormalizePkgLicense(pkg.LicenseType);
            }
            catch { lic = ""; }
            return lic ?? "";
        }

        /// <summary>
        /// One-shot: fill empty <c>pkg.license</c> from live <see cref="VarPackage"/> (may open ZIP).
        /// Call from ThreadPool / index rebuild — never main-thread VR frame.
        /// </summary>
        internal static bool TryBackfillPkgLicensesFromLivePackagesIfNeeded()
        {
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsurePkgLicenseSchema(conn);
                    if (!string.IsNullOrEmpty(MetaGet(conn, PkgLicenseBackfillMetaKey)))
                        return true;
                    return BackfillPkgLicensesFromLivePackages(conn);
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] pkg license backfill failed (retry later): " + ex.Message); } catch { }
                return false;
            }
        }

        /// <summary>After full index rebuild that stamps license on every pkg row.</summary>
        static void MarkPkgLicenseBackfillComplete(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try { MetaSet(conn, PkgLicenseBackfillMetaKey, "1"); } catch { }
        }

        static bool BackfillPkgLicensesFromLivePackages(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            Dictionary<string, VarPackage> byUid = null;
            try { byUid = FileManager.PackagesByUid; } catch { byUid = null; }
            if (byUid == null || byUid.Count == 0)
            {
                // No packages yet — leave sentinel unset so a later inventory can fill.
                return false;
            }

            int updated = 0;
            int examined = 0;
            try
            {
                conn.ExecUtf8("BEGIN IMMEDIATE;");
                using (var up = conn.Prepare("UPDATE pkg SET license=? WHERE uid=? AND (license IS NULL OR license='')"))
                {
                    foreach (KeyValuePair<string, VarPackage> kv in byUid)
                    {
                        VarPackage pkg = kv.Value;
                        if (pkg == null) continue;
                        string uid = pkg.Uid ?? kv.Key ?? "";
                        if (uid.Length == 0) continue;
                        examined++;
                        string lic = ResolveLicenseForInsert(pkg);
                        // Always stamp (empty clears unknown) so we do not re-open ZIP next launch.
                        up.BindText(1, lic ?? "");
                        up.BindText(2, uid);
                        up.Step();
                        up.Reset();
                        if (!string.IsNullOrEmpty(lic)) updated++;
                    }
                }
                MetaSet(conn, PkgLicenseBackfillMetaKey, "1");
                conn.ExecUtf8("COMMIT;");
                try
                {
                    LogUtil.Log("[VPB.DB] pkg license backfill OK examined=" + examined + " withLicense=" + updated);
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                try { LogUtil.LogWarning("[VPB.DB] pkg license backfill aborted: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
