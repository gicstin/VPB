using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject _hubFetchModalRoot;
        private Action<bool> _hubFetchAnswer;

        private const int HubFetchModalMaxRows = 40;
        private const int HubFetchMinVisibleRows = 2;
        private const int HubFetchMaxVisibleRows = 7;
        private const float HubFetchModalWidthRef = 560f;
        private const float HubFetchSizeColumnRef = 96f;

        public bool IsHubFetchConfirmOpen
        {
            get { return _hubFetchModalRoot != null; }
        }

        public bool CanHostHubFetchModal
        {
            get { return IsVisible && backgroundBoxGO != null; }
        }

        public void RequestHubFetchConfirm(VpbHubDependencyFetcher.FetchPlan plan, Action<bool> answer)
        {
            if (plan == null || plan.IsEmpty)
            {
                if (answer != null) answer(false);
                return;
            }
            if (backgroundBoxGO == null)
            {
                if (answer != null) answer(!plan.OverBudget);
                return;
            }

            AnswerHubFetchConfirm(false);
            _hubFetchAnswer = answer;
            BuildHubFetchConfirmModal(plan);
        }

        private void AnswerHubFetchConfirm(bool proceed)
        {
            Action<bool> cb = _hubFetchAnswer;
            _hubFetchAnswer = null;
            if (cb != null)
            {
                try { cb(proceed); }
                catch (Exception ex) { LogUtil.LogError("[VPB] hub fetch answer failed: " + ex.Message); }
            }
        }

        private void CloseHubFetchConfirmModal(bool proceed)
        {
            if (_hubFetchModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_hubFetchModalRoot); }
                catch { }
                _hubFetchModalRoot = null;
            }
            AnswerHubFetchConfirm(proceed);
        }

        private void HideHubFetchConfirmModal()
        {
            CloseHubFetchConfirmModal(false);
        }

        private void BuildHubFetchConfirmModal(VpbHubDependencyFetcher.FetchPlan plan)
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;
            int captionFont = type.Caption;

            float rowH = GalleryUiDesignTokens.ControlSlotHeightRef * s;
            float pad = GalleryUiDesignTokens.DialogPadRef * s;
            float gap = UI.GapControl(s);

            int listed = Mathf.Min(plan.Names.Count, HubFetchModalMaxRows)
                + (plan.Names.Count > HubFetchModalMaxRows ? 1 : 0)
                + (plan.NotOnHub.Count > 0 ? plan.NotOnHub.Count + 1 : 0);
            int listRows = Mathf.Clamp(listed, HubFetchMinVisibleRows, HubFetchMaxVisibleRows);
            float listH = listRows * rowH + GalleryUiDesignTokens.ControlGapRef * 2f * s;

            float titleH = GalleryUiDesignTokens.ControlSlotHeightRef * s;
            float summaryH = rowH;
            int warningCount = (plan.OverBudget ? 1 : 0) + (plan.HasUnknownSizes ? 1 : 0);
            float warnH = warningCount > 0 ? rowH * 1.25f * warningCount : 0f;
            float panelH = pad * 2f + titleH + summaryH + listH + warnH + rowH + gap * (warningCount > 0 ? 4f : 3f);

            GameObject panel;
            _hubFetchModalRoot = UI.CreateModalChrome(
                backgroundBoxGO, "VPB_HubFetchConfirm", HubFetchModalWidthRef * s, panelH,
                UI.ModalPanel, HideHubFetchConfirmModal, out panel);

            UI.AddVLG(panel, spacing: gap, padding: UI.PadDialog(s));

            Text title = UI.CreateEmphasisTitleLabel(panel, HubFetchTitle(plan), headerFont);
            UI.AddLE(title.gameObject, minHeight: titleH, preferredHeight: titleH, flexibleHeight: 0f);

            Text summary = UI.CreateLabel(panel, HubFetchSummary(plan),
                bodyFont, UI.TextMuted, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, name: "Summary");
            UI.AddLE(summary.gameObject, minHeight: summaryH, preferredHeight: summaryH, flexibleHeight: 0f);

            GameObject scrollGO = UI.CreateVScrollableContent(panel, UI.ChromeDarker,
                AnchorPresets.stretchAll, 0f, listH, Vector2.zero,
                GalleryUiDesignTokens.RegionGapRef * s, GalleryUiDesignTokens.HairGapRef * s, false);
            UI.AddLE(scrollGO, minHeight: listH, preferredHeight: listH, flexibleHeight: 1f);
            Transform content = scrollGO.transform.Find("Viewport/Content");
            if (content != null)
            {
                VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
                if (contentVlg != null)
                {
                    contentVlg.padding = UI.PadHV(
                        GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.TightGapRef, s);
                    contentVlg.spacing = GalleryUiDesignTokens.HairGapRef * s;
                    contentVlg.childForceExpandHeight = false;
                }

                int shown = Mathf.Min(plan.Names.Count, HubFetchModalMaxRows);
                for (int i = 0; i < shown; i++)
                    HubFetchAddRow(content, plan.Names[i], HubFetchSizeLabel(plan.Sizes[i]),
                        UI.TextPrimary, UI.TextMuted, bodyFont, rowH, s, i % 2 == 1);

                if (plan.Names.Count > shown)
                    HubFetchAddRow(content, string.Format(
                        VPBTranslation.T("gallery.hubfetch.confirm.more", "…and {0} more"),
                        plan.Names.Count - shown), "", UI.TextDim, UI.TextDim, bodyFont, rowH, s, shown % 2 == 1);

                if (plan.NotOnHub.Count > 0)
                {
                    HubFetchAddRow(content, VPBTranslation.T(
                        "gallery.hubfetch.confirm.not_on_hub_header", "Not available on the Hub"),
                        "", GalleryUiColorTokens.FamilyAmberText, UI.TextDim, captionFont, rowH, s, false);

                    for (int i = 0; i < plan.NotOnHub.Count; i++)
                        HubFetchAddRow(content, plan.NotOnHub[i], "",
                            UI.TextMuted, UI.TextDim, bodyFont, rowH, s, i % 2 == 0);
                }
            }

            if (warningCount > 0)
            {
                Text warn = UI.CreateLabel(panel, HubFetchWarning(plan),
                    captionFont, GalleryUiColorTokens.FamilyAmberText, TextAnchor.UpperLeft,
                    HorizontalWrapMode.Wrap, name: "BudgetWarn");
                UI.AddLE(warn.gameObject, minHeight: warnH, preferredHeight: warnH, flexibleHeight: 0f);
            }

            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(panel.transform, false);
            UI.AddHLG(btnRow, spacing: gap, childForceExpandWidth: true, childForceExpandHeight: false);
            UI.AddLE(btnRow, minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0f);

            GameObject skip = UI.CreateChromeLayoutButton(btnRow.transform, 0f, rowH,
                plan.Names.Count == 0
                    ? VPBTranslation.T("gallery.hubfetch.confirm.cancel", "Cancel")
                    : VPBTranslation.T("gallery.hubfetch.confirm.skip", "Continue without them"),
                bodyFont, UI.ChromeMid, () => CloseHubFetchConfirmModal(false));
            AddDynamicTooltip(skip, () => VPBTranslation.T("gallery.hubfetch.confirm.skip_tip",
                plan.Names.Count == 0
                    ? "Close this list without downloading anything."
                    : "Load now. Anything the missing packages provide will be absent from the scene."));

            GameObject download = UI.CreateChromeLayoutButton(btnRow.transform, 0f, rowH,
                HubFetchDownloadLabel(plan),
                bodyFont, UI.AccentGreen, () => CloseHubFetchConfirmModal(true));
            AddDynamicTooltip(download, () => VPBTranslation.T("gallery.hubfetch.confirm.download_tip",
                plan.Names.Count == 0
                    ? "Close this list. These packages are not available from the Hub."
                    : "Fetch these from the Hub, plus anything they turn out to need, then load."));
        }

        private static string HubFetchSummary(VpbHubDependencyFetcher.FetchPlan plan)
        {
            if (plan.NotOnHub.Count == 0)
            {
                return string.Format(
                    plan.Names.Count == 1
                        ? VPBTranslation.T("gallery.hubfetch.confirm.summary_one",
                            "1 package available — {1} to download from the Hub.")
                        : VPBTranslation.T("gallery.hubfetch.confirm.summary",
                            "{0} packages available — {1} to download from the Hub."),
                    plan.Names.Count, VpbHubDependencyFetcher.FormatBytes(plan.TotalBytes));
            }

            return string.Format(
                VPBTranslation.T("gallery.hubfetch.confirm.summary_with_unavailable",
                    "{0} packages available — {1} to download. {2} unavailable."),
                plan.Names.Count, VpbHubDependencyFetcher.FormatBytes(plan.TotalBytes), plan.NotOnHub.Count);
        }

        private static string HubFetchTitle(VpbHubDependencyFetcher.FetchPlan plan)
        {
            return plan.Names.Count == 0
                ? VPBTranslation.T("gallery.hubfetch.confirm.unavailable_title", "Missing packages unavailable")
                : VPBTranslation.T("gallery.hubfetch.confirm.title", "Download missing packages?");
        }

        private static string HubFetchSizeLabel(long bytes)
        {
            return bytes > 0L
                ? VpbHubDependencyFetcher.FormatBytes(bytes)
                : VPBTranslation.T("gallery.hubfetch.confirm.size_unknown", "Size unknown");
        }

        private static string HubFetchWarning(VpbHubDependencyFetcher.FetchPlan plan)
        {
            if (plan.OverBudget && plan.HasUnknownSizes)
            {
                return VpbHubDependencyFetcher.OverBudgetMessage(plan) + "\n"
                    + VpbHubDependencyFetcher.UnknownSizeMessage(plan);
            }
            return plan.OverBudget
                ? VpbHubDependencyFetcher.OverBudgetMessage(plan)
                : VpbHubDependencyFetcher.UnknownSizeMessage(plan);
        }

        private static string HubFetchDownloadLabel(VpbHubDependencyFetcher.FetchPlan plan)
        {
            if (plan.Names.Count == 0)
                return VPBTranslation.T("gallery.hubfetch.confirm.continue", "Continue");
            if (plan.OverBudget)
                return VPBTranslation.T("gallery.hubfetch.confirm.download_anyway", "Download anyway");
            return string.Format(
                plan.Names.Count == 1
                    ? VPBTranslation.T("gallery.hubfetch.confirm.download_one", "Download 1 package")
                    : VPBTranslation.T("gallery.hubfetch.confirm.download", "Download {0} packages"),
                plan.Names.Count);
        }

        private static void HubFetchAddRow(
            Transform parent, string label, string trailing,
            Color color, Color trailingColor, int font, float rowH, float s, bool stripe)
        {
            GameObject row = new GameObject("Row");
            row.transform.SetParent(parent, false);
            if (stripe) UI.AddImage(row, UI.ChromeDark);
            UI.AddHLG(row, spacing: UI.GapControl(s),
                padding: UI.PadHV(GalleryUiDesignTokens.TightGapRef, 0f, s),
                childForceExpandWidth: false, childForceExpandHeight: false);
            UI.AddLE(row, minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0f);

            Text name = UI.CreateLabel(row, label ?? "", font, color, TextAnchor.MiddleLeft,
                HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate, name: "Name");
            UI.AddLE(name.gameObject, flexibleWidth: 1f);

            if (!string.IsNullOrEmpty(trailing))
            {
                Text size = UI.CreateLabel(row, trailing, font, trailingColor, TextAnchor.MiddleRight,
                    HorizontalWrapMode.Overflow, name: "Size");
                UI.AddLE(size.gameObject,
                    minWidth: HubFetchSizeColumnRef * s, preferredWidth: HubFetchSizeColumnRef * s,
                    flexibleWidth: 0f);
            }
        }

        private void CloseHubFetchConfirmModalIfAbandoned()
        {
            if (_hubFetchModalRoot == null) return;
            if (VpbHubDependencyFetcher.Busy) return;
            CloseHubFetchConfirmModal(false);
        }

        public void FetchMissingDependenciesForSelection()
        {
            FileEntry file = null;
            if (selectedFiles != null && selectedFiles.Count > 0) file = selectedFiles[0];
            FetchMissingDependencies(file);
        }

        public void FetchMissingDependencies(FileEntry file)
        {
            if (file == null)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.hubfetch.status.nothing", "Nothing selected."));
                return;
            }
            if (VpbHubDependencyFetcher.Busy)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.hubfetch.fail.busy", "Another Hub download is already running."));
                return;
            }

            List<string> missing = null;
            try { missing = GallerySortManager.GetMissingDependencyIds(file); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] FetchMissingDependencies scan failed: " + ex);
                missing = null;
            }

            if (missing == null || missing.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T(
                    "gallery.hubdep.status.no_missing", "No missing dependencies."));
                return;
            }

            string reason;
            if (!VpbHubDependencyFetcher.HubAvailable(out reason))
            {
                ShowTemporaryStatus(reason, 4f);
                return;
            }

            try { StartCoroutine(FetchMissingDependenciesRoutine(file, missing)); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] FetchMissingDependencies could not start: " + ex);
            }
        }

        private IEnumerator FetchMissingDependenciesRoutine(FileEntry file, List<string> missing)
        {
            string title = string.Format(
                VPBTranslation.T("gallery.hubfetch.banner_for", "Fetching packages for {0}"),
                file != null ? (file.Name ?? "item") : "item");

            VpbHubDependencyFetcher.FetchResult result = null;
            yield return VpbHubDependencyFetcher.Fetch(
                missing,
                title,
                RequestHubFetchConfirm,
                r => result = r);

            ReportHubFetchResult(result);

            if (result != null && result.InstalledAny)
            {
                try
                {
                    ResetMissingDepsCacheFor(file);
                    Gallery.RefreshVisiblePanelRowVisuals();
                }
                catch { }
            }
        }

        private void ReportHubFetchResult(VpbHubDependencyFetcher.FetchResult result)
        {
            if (result == null) return;

            switch (result.Outcome)
            {
                case VpbHubDependencyFetcher.Outcome.NothingToDo:
                    ShowTemporaryStatus(VPBTranslation.T(
                        "gallery.hubdep.status.no_missing", "No missing dependencies."));
                    break;
                case VpbHubDependencyFetcher.Outcome.Complete:
                    ShowTemporaryStatus(string.Format(
                        result.Installed == 1
                            ? VPBTranslation.T("gallery.hubfetch.status.done_one", "Downloaded 1 package — {1}.")
                            : VPBTranslation.T("gallery.hubfetch.status.done", "Downloaded {0} packages — {1}."),
                        result.Installed, VpbHubDependencyFetcher.FormatBytes(result.Bytes)), 4f);
                    break;
                case VpbHubDependencyFetcher.Outcome.Partial:
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("gallery.hubfetch.status.partial",
                            "Downloaded {0} package(s); {1} could not be fetched."),
                        result.Installed, result.NotOnHub.Count + result.Unresolved.Count), 5f);
                    break;
                case VpbHubDependencyFetcher.Outcome.Declined:
                    if (!string.IsNullOrEmpty(result.FailReason))
                        ShowTemporaryStatus(result.FailReason, 3f);
                    break;
                default:
                    ShowTemporaryStatus(string.IsNullOrEmpty(result.FailReason)
                        ? VPBTranslation.T("gallery.hubfetch.fail.download",
                            "One or more downloads failed — see the log.")
                        : result.FailReason, 5f);
                    break;
            }
        }

        private static void ResetMissingDepsCacheFor(FileEntry file)
        {
            VarPackage pkg = null;
            if (file is VarFileEntry vfe) pkg = vfe.Package;
            else if (file is PackageListEntry ple) pkg = ple.Package;
            if (pkg != null) pkg.MissingDepsCount = -1;
        }
    }
}
