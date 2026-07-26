using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Assigned when the footer plugin-info button row is built. That build path is not wired, so
        // the field stays null and the hover/chrome handlers null-guard and no-op (dormant, not dead).
#pragma warning disable 0649
        private GameObject footerPluginInfoBtn;
        private Image footerPluginInfoBtnImage;
#pragma warning restore 0649
        private bool _footerPluginInfoHovering;
        private int _footerPluginInfoTooltipKey = int.MinValue;
        private float _footerPluginInfoLastUpdateCheckUnscaled = -999f;
        private const float FooterPluginInfoUpdateCheckCooldownSec = 60f;

        private static string BuildFooterPluginInfoHoverText()
        {
            var sb = new StringBuilder(160);
            sb.Append("VPB ");
            sb.Append(PluginVersionInfo.Version);

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
                    else if (updater.IsBusy)
                    {
                        sb.Append(" | ");
                        sb.Append(updater.StatusMessage ?? VPBTranslation.T("settings.updater.checking", "Checking..."));
                    }
                    else if (updater.Status == VpbUpdateStatus.UpToDate)
                    {
                        sb.Append(" | ");
                        sb.Append(updater.StatusMessage ?? VPBTranslation.T("settings.updater.up_to_date", "Up to date"));
                    }
                    else if (updater.Status == VpbUpdateStatus.Error && !string.IsNullOrEmpty(updater.StatusMessage))
                    {
                        sb.Append(" | ");
                        sb.Append(updater.StatusMessage);
                    }
                    else if (updater.Status == VpbUpdateStatus.Staged)
                    {
                        sb.Append(" | ");
                        sb.Append(VPBTranslation.T("gallery.footer.info.update_staged", "Update staged — restart VaM to apply"));
                    }
                }
            }
            catch { }

            sb.Append(" — ");
            sb.Append(VPBTranslation.T("gallery.footer.info.click_hint", "Click: Auto-Updater settings"));
            sb.Append(" | ");
            sb.Append(VPBTranslation.T("gallery.footer.info.right_click_hint", "Right-click: Check for updates (1/min)"));
            return sb.ToString();
        }

        // Rich hover tooltip for the title-bar Settings gear — diagnostics that help triage a user's
        // screenshot: plugin version, baked build date + age, VR/Desktop mode, loaded var-package count,
        // updater status, and the on-disk path of the loaded DLL. Build date + path expose a stale/duplicate
        // VPB.dll; VR/Desktop and package count narrow down environment-specific reports.
        private string BuildPluginInfoTooltip()
        {
            var sb = new StringBuilder(220);
            sb.Append("VPB ");
            sb.Append(PluginVersionInfo.Version);

            // Baked build date (UTC, date only): confirms the loaded build is actually recent without
            // leaking a precise timestamp/timezone. Shows age in days so a stale DLL is obvious.
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
                int pkgs = FileManager.GetPackages().Count;
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

        private void FooterPluginInfoRefreshChrome()
        {
            if (footerPluginInfoBtnImage == null) return;
            bool highlight = false;
            try
            {
                var u = VamHookPlugin.singleton != null ? VamHookPlugin.singleton.Updater : null;
                highlight = u != null && (u.HasPendingUpdate || u.Status == VpbUpdateStatus.Staged);
            }
            catch { }
            footerPluginInfoBtnImage.color = highlight
                ? new Color(0.55f, 0.38f, 0.18f, 1f)
                : UI.IconButtonBackdrop;
        }

        private void FooterPluginInfoUpdateHoverTooltip()
        {
            if (!_footerPluginInfoHovering || footerPluginInfoBtn == null) return;
            StopCo(ref temporaryStatusCoroutine);
            temporaryStatusMsg = BuildFooterPluginInfoHoverText();
            temporaryStatusOwner = footerPluginInfoBtn;
        }

        private void FooterPluginInfoPollHoverTooltip()
        {
            if (!_footerPluginInfoHovering || footerPluginInfoBtn == null) return;
            int key = PluginVersionInfo.Version.GetHashCode();
            try
            {
                var u = VamHookPlugin.singleton != null ? VamHookPlugin.singleton.Updater : null;
                if (u != null)
                    key = unchecked(key * 31 + (int)u.Status + (u.HasPendingUpdate ? 1 : 0) + (u.IsBusy ? 2 : 0));
            }
            catch { }
            if (key == _footerPluginInfoTooltipKey) return;
            _footerPluginInfoTooltipKey = key;
            FooterPluginInfoUpdateHoverTooltip();
        }

        private void RegisterFooterPluginInfoHover(GameObject go)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) =>
            {
                if (enter) hoverCount++;
                else hoverCount--;
                if (hoverCount < 0) hoverCount = 0;
                _footerPluginInfoHovering = enter;
                if (enter)
                {
                    FooterPluginInfoRefreshChrome();
                    _footerPluginInfoTooltipKey = int.MinValue;
                    FooterPluginInfoUpdateHoverTooltip();
                }
                else
                {
                    _footerPluginInfoTooltipKey = int.MinValue;
                    FooterPluginInfoRefreshChrome();
                    if (temporaryStatusOwner == go)
                    {
                        temporaryStatusMsg = null;
                        temporaryStatusOwner = null;
                    }
                }
            };
        }

        private void FooterPluginInfoOpenSettings()
        {
            try { OpenSettingsGroup("updater"); }
            catch (System.Exception ex) { LogUtil.LogError("[VPB] FooterPluginInfoOpenSettings: " + ex.Message); }
        }

        private void FooterPluginInfoCheckUpdateOnRightClick()
        {
            try
            {
                var updater = VamHookPlugin.singleton != null ? VamHookPlugin.singleton.Updater : null;
                if (updater == null)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.footer.info.updater_unavailable", "Updater unavailable."), 2f);
                    return;
                }
                if (updater.IsBusy)
                {
                    ShowTemporaryStatus(
                        updater.StatusMessage ?? VPBTranslation.T("settings.updater.checking", "Checking..."),
                        2f);
                    return;
                }
                if (updater.HasPendingUpdate)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.footer.info.update_already_pending", "Update already staged or available."),
                        2.5f);
                    return;
                }
                float elapsed = Time.unscaledTime - _footerPluginInfoLastUpdateCheckUnscaled;
                if (elapsed < FooterPluginInfoUpdateCheckCooldownSec)
                {
                    int waitSec = Mathf.CeilToInt(FooterPluginInfoUpdateCheckCooldownSec - elapsed);
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("gallery.footer.info.update_check_cooldown", "Update check cooldown — wait {0}s."),
                            waitSec),
                        2.5f);
                    return;
                }
                _footerPluginInfoLastUpdateCheckUnscaled = Time.unscaledTime;
                updater.CheckForUpdateAsync();
                FooterPluginInfoRefreshChrome();
                if (_footerPluginInfoHovering)
                {
                    _footerPluginInfoTooltipKey = int.MinValue;
                    FooterPluginInfoUpdateHoverTooltip();
                }
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.footer.info.update_check_started", "Checking for VPB updates..."),
                    2f);
            }
            catch (System.Exception ex)
            {
                LogUtil.LogError("[VPB] FooterPluginInfoCheckUpdateOnRightClick: " + ex.Message);
            }
        }
    }
}
