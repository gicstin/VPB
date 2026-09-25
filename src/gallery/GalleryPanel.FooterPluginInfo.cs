using System.Text;

namespace VPB
{
    public partial class GalleryPanel
    {
        private string BuildPluginInfoTooltip()
        {
            var sb = new StringBuilder(220);
            sb.Append("VPB ");
            sb.Append(PluginVersionInfo.Version);

            // Baked build date (UTC, date only): confirms the loaded build is actually recent without leaking a precise timestamp/timezone.
            try
            {
                sb.Append(" | ");
                sb.Append(VPBTranslation.T("gallery.plugininfo.built", "Built"));
                sb.Append(' ');
                sb.Append(PluginVersionInfo.BuildDate);
                System.DateTime built;
                if (System.DateTime.TryParseExact(PluginVersionInfo.BuildDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out built))
                {
                    int days = (System.DateTime.UtcNow.Date - built.Date).Days;
                    if (days >= 0)
                    {
                        sb.Append(" (");
                        sb.Append(days);
                        sb.Append(VPBTranslation.T("gallery.plugininfo.days_ago", "d ago"));
                        sb.Append(')');
                    }
                }
            }
            catch { }

            // VR vs Desktop: UI/interaction bugs differ per mode, so this is key triage from a screenshot.
            try
            {
                sb.Append(" | ");
                sb.Append(VPB.src.util.XrUtils.IsVrActive()
                    ? VPBTranslation.T("gallery.plugininfo.mode_vr", "VR")
                    : VPBTranslation.T("gallery.plugininfo.mode_desktop", "Desktop"));
            }
            catch { }

            string loc = null;
            try { loc = typeof(GalleryPanel).Assembly.Location; } catch { }

            try
            {
                int pkgs = FileManager.GetPackageCount();
                sb.Append(" | ");
                sb.Append(VPBTranslation.T("gallery.plugininfo.packages", "Packages"));
                sb.Append(": ");
                sb.Append(pkgs);
            }
            catch { }

            try
            {
                var updater = VamHookPlugin.singleton != null ? VamHookPlugin.singleton.Updater : null;
                if (updater != null)
                {
                    if (updater.HasPendingUpdate)
                    {
                        sb.Append(" | ");
                        sb.Append(VPBTranslation.T("gallery.footer.info.update_available", "Update available"));
                        if (!string.IsNullOrEmpty(updater.AvailableVersion))
                        {
                            sb.Append(": ");
                            sb.Append(updater.AvailableVersion);
                        }
                    }
                    else if (updater.Status == VpbUpdateStatus.Staged)
                    {
                        sb.Append(" | ");
                        sb.Append(VPBTranslation.T("gallery.footer.info.update_staged", "Update staged — restart VaM to apply"));
                    }
                    else if (updater.Status == VpbUpdateStatus.UpToDate)
                    {
                        sb.Append(" | ");
                        sb.Append(updater.StatusMessage ?? VPBTranslation.T("settings.updater.up_to_date", "Up to date"));
                    }

                    if (updater.IsPinned)
                    {
                        sb.Append(" | ");
                        sb.Append(VPBTranslation.T("gallery.plugininfo.pinned", "Pinned"));
                        sb.Append(' ');
                        sb.Append(updater.PinnedVersion ?? "?");
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(loc))
            {
                sb.Append(" | DLL: ");
                sb.Append(loc);
            }

            sb.Append(" | ");
            sb.Append(VPBTranslation.T("gallery.tooltip.open_settings", "Settings"));
            return sb.ToString();
        }
    }
}
