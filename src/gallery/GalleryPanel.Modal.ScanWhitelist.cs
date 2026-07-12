using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GameObject _scanWlModalRoot;
        private Transform _scanWlFoldersParent;
        private Transform _scanWlUidsParent;
        private Text _scanWlFoldersHdrText;
        private Text _scanWlUidsHdrText;
        private InputField _scanWlNewFolderInput;
        private InputField _scanWlNewUidInput;
        private GameObject _scanWlDisableConfirmRoot;
        private GameObject _scanWlEmptyWarnGo;

        private const float ScanWlRemoveBtnWidthScale = 92f;
        private const float ScanWlRowHeightScale = 34f;

        private static void RefreshGalleryAfterScanWhitelistChange()
        {
            try { Gallery.RefreshVisiblePanelRowVisuals(); } catch { }
        }

        public void ShowScanWhitelistEditorModal()
        {
            if (backgroundBoxGO == null) return;
            HideScanWhitelistDisableConfirmModal();
            if (_scanWlModalRoot != null)
            {
                _scanWlModalRoot.SetActive(true);
                RebuildScanWhitelistModalRows();
                return;
            }
            BuildScanWhitelistEditorModal();
        }

        public void ShowScanWhitelistDisableConfirmModal()
        {
            if (backgroundBoxGO == null) return;
            if (_scanWlDisableConfirmRoot != null)
            {
                _scanWlDisableConfirmRoot.SetActive(true);
                return;
            }
            BuildScanWhitelistDisableConfirmModal();
        }

        private void HideScanWhitelistEditorModal()
        {
            HideScanWhitelistDisableConfirmModal();
            if (_scanWlModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_scanWlModalRoot); } catch { }
                _scanWlModalRoot = null;
            }
            _scanWlFoldersParent = null;
            _scanWlUidsParent = null;
            _scanWlFoldersHdrText = null;
            _scanWlUidsHdrText = null;
            _scanWlNewFolderInput = null;
            _scanWlNewUidInput = null;
            _scanWlEmptyWarnGo = null;
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

        private void HideScanWhitelistDisableConfirmModal()
        {
            if (_scanWlDisableConfirmRoot != null)
            {
                try { UnityEngine.Object.Destroy(_scanWlDisableConfirmRoot); } catch { }
                _scanWlDisableConfirmRoot = null;
            }
        }

        private void BuildScanWhitelistEditorModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;

            _scanWlModalRoot = new GameObject("VPB_ScanWhitelistModal");
            _scanWlModalRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRt = _scanWlModalRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image dim = UI.AddImage(_scanWlModalRoot, new Color(0f, 0f, 0f, 0.72f));
            Button dimBtn = _scanWlModalRoot.AddComponent<Button>();
            UI.ConfigButtonFlat(dimBtn);
            dimBtn.onClick.AddListener(HideScanWhitelistEditorModal);

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_scanWlModalRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(760f * s, 660f * s);
            Image pbg = UI.AddImage(panel, new Color(0.06f, 0.06f, 0.08f, 1f));

            VerticalLayoutGroup v = UI.AddVLG(panel, spacing: 8f * s, padding: UI.Pad(14, 14, 14, 14, s));

            GameObject header = new GameObject("HeaderRow");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.spacing = 8f * s;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            LayoutElement hle = UI.AddLE(header, minHeight: 48f * s, preferredHeight: 48f * s);

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(header.transform, false);
            Text title = titleGo.AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            title.color = Color.white;
            title.alignment = TextAnchor.MiddleLeft;
            title.text = VPBTranslation.T("hook.settings.scan_whitelist.window_title", "VaM Scan Whitelist");
            try { VPBUiFont.ApplyTo(title); } catch { }
            GalleryUiMetrics.ApplyEmphasisTitle(title, headerFont);
            LayoutElement tle = UI.AddLE(titleGo, flexibleWidth: 1f);

            ScanWlCreateHeaderButton(header.transform, 100f * s, 40f * s, VPBTranslation.T("hook.close", "Close"), bodyFont, new Color(0.44f, 0.36f, 0.20f, 1f), HideScanWhitelistEditorModal);

            _scanWlEmptyWarnGo = new GameObject("EmptyWarn");
            _scanWlEmptyWarnGo.transform.SetParent(panel.transform, false);
            Text warnT = _scanWlEmptyWarnGo.AddComponent<Text>();
            warnT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            warnT.fontSize = bodyFont;
            warnT.color = new Color(1f, 0.75f, 0.4f, 1f);
            warnT.horizontalOverflow = HorizontalWrapMode.Wrap;
            warnT.text = VPBTranslation.T("hook.settings.scan_whitelist.empty_warning", "⚠ Warning: whitelist is enabled but empty — all packages will be excluded from VaM's scan!");
            try { VPBUiFont.ApplyTo(warnT); } catch { }
            LayoutElement warnLe = UI.AddLE(_scanWlEmptyWarnGo, minHeight: 44f * s);
            _scanWlEmptyWarnGo.SetActive(false);

            GameObject enableRow = new GameObject("EnableRow");
            enableRow.transform.SetParent(panel.transform, false);
            Image enableBg = UI.AddImage(enableRow, new Color(0.1f, 0.11f, 0.14f, 1f));
            HorizontalLayoutGroup erh = UI.AddHLG(enableRow, spacing: 10f * s, padding: UI.Pad(8, 8, 6, 6, s), childForceExpandWidth: false);
            LayoutElement erLe = UI.AddLE(enableRow, minHeight: 44f * s, preferredHeight: 44f * s);

            bool enabled = false;
            try { enabled = ScanWhitelistManager.Instance.IsEnabled; } catch { }
            ScanWlCreateHeaderButton(enableRow.transform, 40f * s, 32f * s, enabled ? "✓" : " ", bodyFont, new Color(0.2f, 0.35f, 0.5f, 1f), () =>
            {
                try
                {
                    if (ScanWhitelistManager.Instance.IsEnabled)
                        ShowScanWhitelistDisableConfirmModal();
                    else
                    {
                        ScanWhitelistManager.Instance.SetEnabled(true);
                        ScanWhitelistManager.Instance.Save();
                        RebuildScanWhitelistModalRows();
                        RefreshGalleryAfterScanWhitelistChange();
                        if (IsSettingsPanelOpen()) RefreshInternalSettingsListRows(true);
                    }
                }
                catch { }
            });

            Text el = UI.CreateLabel(enableRow, VPBTranslation.T("hook.settings.scan_whitelist.enable_short", "Enable scan whitelist"), bodyFont, Color.white, TextAnchor.MiddleLeft, name: "EnableLabel");
            LayoutElement ell = UI.AddLE(el.gameObject, flexibleWidth: 1f);

            GameObject scrollGO = UI.CreateVScrollableContent(panel, new Color(0.08f, 0.08f, 0.1f, 1f), AnchorPresets.stretchAll, 0f, 280f * s, Vector2.zero, 10f * s, 3f * s, false);
            LayoutElement scLe = UI.AddLE(scrollGO, minHeight: 260f * s, flexibleHeight: 1f);
            Transform content = scrollGO.transform.Find("Viewport/Content");
            if (content == null) return;

            VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.padding = new RectOffset(Mathf.RoundToInt(8f * s), Mathf.RoundToInt(8f * s), Mathf.RoundToInt(6f * s), Mathf.RoundToInt(8f * s));
                contentVlg.spacing = 10f * s;
            }

            _scanWlFoldersHdrText = ScanWlCreateSectionHeader(content, VPBTranslation.T("hook.settings.scan_whitelist.folders", "Whitelisted Folders"), 0, bodyFont, s);
            GameObject foldersList = new GameObject("FoldersList");
            foldersList.transform.SetParent(content, false);
            VerticalLayoutGroup fvg = UI.AddVLG(foldersList, spacing: 3f * s);
            _scanWlFoldersParent = foldersList.transform;

            _scanWlUidsHdrText = ScanWlCreateSectionHeader(content, VPBTranslation.T("hook.settings.scan_whitelist.uids", "Per-Package UID Overrides"), 0, bodyFont, s);
            GameObject uidsList = new GameObject("UidsList");
            uidsList.transform.SetParent(content, false);
            VerticalLayoutGroup uvg = UI.AddVLG(uidsList, spacing: 3f * s);
            _scanWlUidsParent = uidsList.transform;

            GameObject addFooter = new GameObject("AddFooter");
            addFooter.transform.SetParent(panel.transform, false);
            Image addFooterBg = UI.AddImage(addFooter, new Color(0.09f, 0.09f, 0.12f, 1f));
            VerticalLayoutGroup addFooterV = UI.AddVLG(addFooter, spacing: 10f * s, padding: UI.Pad(10, 10, 10, 10, s));

            ScanWlCreateAddBlock(
                addFooter.transform,
                VPBTranslation.T("hook.settings.scan_whitelist.add_folder", "Add folder (e.g. AddonPackages/CreatorName)"),
                VPBTranslation.T("hook.settings.scan_whitelist.add_folder_ph", "AddonPackages/CreatorName"),
                bodyFont,
                s,
                out _scanWlNewFolderInput,
                () =>
                {
                    try
                    {
                        string folder = _scanWlNewFolderInput != null ? (_scanWlNewFolderInput.text ?? "").Trim() : "";
                        if (string.IsNullOrEmpty(folder)) return;
                        if (ScanWhitelistManager.Instance.AddFolder(folder))
                        {
                            ScanWhitelistManager.Instance.Save();
                            if (_scanWlNewFolderInput != null) _scanWlNewFolderInput.text = "";
                            RebuildScanWhitelistModalRows();
                            RefreshGalleryAfterScanWhitelistChange();
                        }
                    }
                    catch { }
                });

            ScanWlCreateAddBlock(
                addFooter.transform,
                VPBTranslation.T("hook.settings.scan_whitelist.add_uid", "Add package UID override"),
                VPBTranslation.T("hook.settings.scan_whitelist.add_uid_ph", "Creator.PackageName.latest"),
                bodyFont,
                s,
                out _scanWlNewUidInput,
                () =>
                {
                    try
                    {
                        string uid = _scanWlNewUidInput != null ? (_scanWlNewUidInput.text ?? "").Trim() : "";
                        if (string.IsNullOrEmpty(uid)) return;
                        if (ScanWhitelistManager.Instance.AddUidOverride(uid))
                        {
                            ScanWhitelistManager.Instance.Save();
                            if (_scanWlNewUidInput != null) _scanWlNewUidInput.text = "";
                            RebuildScanWhitelistModalRows();
                            RefreshGalleryAfterScanWhitelistChange();
                        }
                    }
                    catch { }
                });

            RebuildScanWhitelistModalRows();
        }

        private void BuildScanWhitelistDisableConfirmModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int headerFont = type.Title;
            int bodyFont = type.Body;

            Transform parent = _scanWlModalRoot != null ? _scanWlModalRoot.transform : backgroundBoxGO.transform;

            _scanWlDisableConfirmRoot = new GameObject("VPB_ScanWhitelistDisableConfirm");
            _scanWlDisableConfirmRoot.transform.SetParent(parent, false);
            RectTransform rootRt = _scanWlDisableConfirmRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image dim = UI.AddImage(_scanWlDisableConfirmRoot, new Color(0f, 0f, 0f, 0.55f));

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_scanWlDisableConfirmRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(520f * s, 280f * s);
            Image pbg = UI.AddImage(panel, new Color(0.08f, 0.08f, 0.1f, 1f));

            VerticalLayoutGroup v = panel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(16f * s), Mathf.RoundToInt(16f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s));
            v.spacing = 10f * s;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(panel.transform, false);
            Text title = titleGo.AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            title.color = Color.white;
            title.text = VPBTranslation.T("hook.settings.scan_whitelist.disable_confirm.title", "Disable scan whitelist?");
            try { VPBUiFont.ApplyTo(title); } catch { }
            GalleryUiMetrics.ApplyEmphasisTitle(title, headerFont);

            Text body = UI.CreateLabel(panel, VPBTranslation.T(
                "hook.settings.scan_whitelist.disable_confirm.body",
                "Enable VaM scan whitelist to improve startup time. This “hides” most packages from VaM so it won’t rescan them every start.\n\nDisabling will make VaM startup much slower (full rescan)."), bodyFont, new Color(0.88f, 0.88f, 0.9f, 1f), TextAnchor.UpperLeft, verticalWrap: VerticalWrapMode.Overflow, name: "Body");
            LayoutElement bodyLe = UI.AddLE(body.gameObject, minHeight: 100f * s, flexibleHeight: 1f);

            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup brh = btnRow.AddComponent<HorizontalLayoutGroup>();
            brh.spacing = 10f * s;
            brh.childControlWidth = true;
            brh.childControlHeight = true;
            brh.childForceExpandWidth = true;
            LayoutElement brLe = UI.AddLE(btnRow, minHeight: 40f * s);

            ScanWlCreateHeaderButton(btnRow.transform, 0f, 38f * s, VPBTranslation.T("hook.cancel", "Cancel"), bodyFont, new Color(0.35f, 0.35f, 0.38f, 1f), HideScanWhitelistDisableConfirmModal);
            ScanWlCreateHeaderButton(btnRow.transform, 0f, 38f * s, VPBTranslation.T("hook.settings.scan_whitelist.disable_confirm.disable", "Disable"), bodyFont, new Color(0.55f, 0.28f, 0.28f, 1f), () =>
            {
                try
                {
                    ScanWhitelistManager.Instance.SetEnabled(false);
                    ScanWhitelistManager.Instance.Save();
                }
                catch { }
                HideScanWhitelistDisableConfirmModal();
                RebuildScanWhitelistModalRows();
                RefreshGalleryAfterScanWhitelistChange();
                try
                {
                    if (IsSettingsPanelOpen())
                    {
                        InvalidateInternalSettingsDefsCache();
                        RefreshInternalSettingsListRows(true);
                    }
                }
                catch { }
            });
        }

        private void RebuildScanWhitelistModalRows()
        {
            if (_scanWlFoldersParent == null || _scanWlUidsParent == null) return;

            ScanWlDestroyChildren(_scanWlFoldersParent);
            ScanWlDestroyChildren(_scanWlUidsParent);

            float s = ChromeScale;
            int bodyFont = new GalleryModalTypography(s).Body;

            try
            {
                if (_scanWlEmptyWarnGo != null)
                    _scanWlEmptyWarnGo.SetActive(ScanWhitelistManager.Instance.IsEnabledButEmpty());
            }
            catch
            {
                if (_scanWlEmptyWarnGo != null) _scanWlEmptyWarnGo.SetActive(false);
            }

            List<string> folders = null;
            var uidSet = new List<string>();
            try
            {
                folders = ScanWhitelistManager.Instance.GetWhitelistedFolders();
                var rawUids = ScanWhitelistManager.Instance.GetIncludedPackageUids();
                if (rawUids != null)
                {
                    foreach (string uid in rawUids)
                    {
                        if (!string.IsNullOrEmpty(uid))
                            uidSet.Add(uid);
                    }
                }
            }
            catch { }

            int folderCount = folders != null ? folders.Count : 0;
            ScanWlUpdateSectionHeader(_scanWlFoldersHdrText, VPBTranslation.T("hook.settings.scan_whitelist.folders", "Whitelisted Folders"), folderCount);

            if (folderCount == 0)
                ScanWlAddPlaceholderRow(_scanWlFoldersParent, VPBTranslation.T("hook.settings.scan_whitelist.none_folders", "No folders — add one below."), bodyFont, s);
            else
            {
                for (int i = 0; i < folders.Count; i++)
                {
                    string path = folders[i];
                    bool alt = (i & 1) == 1;
                    ScanWlAddRemovableRow(this, _scanWlFoldersParent, path, bodyFont, s, alt, () =>
                    {
                        try
                        {
                            ScanWhitelistManager.Instance.RemoveFolder(path);
                            ScanWhitelistManager.Instance.Save();
                            RebuildScanWhitelistModalRows();
                            RefreshGalleryAfterScanWhitelistChange();
                        }
                        catch { }
                    });
                }
            }

            ScanWlUpdateSectionHeader(_scanWlUidsHdrText, VPBTranslation.T("hook.settings.scan_whitelist.uids", "Per-Package UID Overrides"), uidSet.Count);

            if (uidSet.Count == 0)
                ScanWlAddPlaceholderRow(_scanWlUidsParent, VPBTranslation.T("hook.settings.scan_whitelist.none_uids", "No UID overrides — add one below."), bodyFont, s);
            else
            {
                for (int i = 0; i < uidSet.Count; i++)
                {
                    string uid = uidSet[i];
                    bool alt = (i & 1) == 1;
                    ScanWlAddRemovableRow(this, _scanWlUidsParent, uid, bodyFont, s, alt, () =>
                    {
                        try
                        {
                            ScanWhitelistManager.Instance.RemoveUidOverride(uid);
                            ScanWhitelistManager.Instance.Save();
                            RebuildScanWhitelistModalRows();
                            RefreshGalleryAfterScanWhitelistChange();
                        }
                        catch { }
                    });
                }
            }
        }

        private static Text ScanWlCreateSectionHeader(Transform parent, string title, int count, int fontSize, float s)
        {
            Text t = UI.CreateLabel(parent.gameObject, null, fontSize, new Color(0.82f, 0.88f, 1f, 1f), TextAnchor.MiddleLeft, name: "SectionHeader");
            ScanWlUpdateSectionHeader(t, title, count);
            LayoutElement le = UI.AddLE(t.gameObject, minHeight: 26f * s, preferredHeight: 26f * s);
            return t;
        }

        private static void ScanWlUpdateSectionHeader(Text header, string title, int count)
        {
            if (header == null) return;
            string t = title ?? "";
            header.text = count > 0 ? t + "  (" + count + ")" : t;
        }

        private static void ScanWlCreateAddBlock(Transform parent, string label, string placeholder, int fontSize, float s, out InputField input, UnityAction onAdd)
        {
            GameObject block = UI.CreateChildRT(parent.gameObject, "AddBlock");
            UI.AddVLG(block, spacing: 6f * s);

            Text lbl = UI.CreateLabel(block, label, fontSize, new Color(0.78f, 0.8f, 0.84f, 1f), TextAnchor.MiddleLeft, name: "Label");
            LayoutElement lblLe = UI.AddLE(lbl.gameObject, minHeight: 20f * s);

            GameObject row = UI.CreateChildRT(block, "Row");
            UI.AddHLG(row, spacing: 8f * s, childForceExpandWidth: false);
            LayoutElement rowLe = UI.AddLE(row, minHeight: 36f * s, preferredHeight: 36f * s);

            input = ScanWlCreateInputField(row.transform, fontSize, s, 1f, placeholder);
            float addBtnW = ScanWlRemoveBtnWidthScale * s;
            ScanWlCreateHeaderButton(row.transform, addBtnW, 34f * s, VPBTranslation.T("hook.add", "Add"), fontSize, new Color(0.22f, 0.42f, 0.58f, 1f), onAdd);
        }

        private static void ScanWlAddPlaceholderRow(Transform parent, string text, int fontSize, float s)
        {
            Text t = UI.CreateLabel(parent.gameObject, text, fontSize, UI.TextDim, name: "Placeholder");
            LayoutElement le = UI.AddLE(t.gameObject, minHeight: 22f * s);
        }

        private static void ScanWlAddRemovableRow(GalleryPanel panel, Transform parent, string label, int fontSize, float s, bool altStripe, UnityAction onRemove)
        {
            float rowH = ScanWlRowHeightScale * s;
            float removeW = ScanWlRemoveBtnWidthScale * s;

            GameObject row = new GameObject("Row");
            row.transform.SetParent(parent, false);
            Image rowBg = UI.AddGalleryElementRoundedBg(row, altStripe ? new Color(0.11f, 0.11f, 0.14f, 1f) : new Color(0.09f, 0.09f, 0.11f, 1f));
            HorizontalLayoutGroup h = UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(8, 6, 4, 4, s), childForceExpandWidth: false);
            LayoutElement rle = UI.AddLE(row, minHeight: rowH, preferredHeight: rowH);

            Text lt = UI.CreateLabel(row, label ?? "", fontSize, new Color(0.92f, 0.92f, 0.94f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, name: "Label");
            LayoutElement lle = UI.AddLE(lt.gameObject, minWidth: 0f, flexibleWidth: 1f);

            GameObject removeBtn = ScanWlCreateHeaderButton(row.transform, removeW, rowH - 6f * s, VPBTranslation.T("hook.remove", "Remove"),
                fontSize, new Color(0.52f, 0.28f, 0.28f, 1f), onRemove);
            if (panel != null)
            {
                try
                {
                    panel.AddTooltipPlain(removeBtn, VPBTranslation.T("hook.settings.scan_whitelist.remove_tip", "Remove this entry from the whitelist"));
                }
                catch { }
            }
        }

        private static InputField ScanWlCreateInputField(Transform parent, int fontSize, float s, float flexWidth, string placeholderText = null)
        {
            GameObject go = new GameObject("Input");
            go.transform.SetParent(parent, false);
            Image bg = UI.AddGalleryElementRoundedBg(go, new Color(0.12f, 0.12f, 0.14f, 1f));
            LayoutElement le = UI.AddLE(go, minHeight: 34f * s, preferredHeight: 34f * s, flexibleWidth: flexWidth);

            GameObject ta = new GameObject("TextArea");
            ta.transform.SetParent(go.transform, false);
            RectTransform taRt = ta.AddComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(6f * s, 2f * s);
            taRt.offsetMax = new Vector2(-6f * s, -2f * s);

            Text phT = UI.CreateLabel(ta, placeholderText ?? "", fontSize, new Color(0.45f, 0.45f, 0.48f, 1f), name: "Placeholder");

            Text tcT = UI.CreateLabel(ta, "", fontSize, Color.white, name: "Text");

            InputField input = go.AddComponent<InputField>();
            input.textComponent = tcT;
            input.placeholder = phT;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static GameObject ScanWlCreateHeaderButton(Transform parent, float width, float height, string label, int fontSize, Color bg, UnityAction onClick)
        {
            GameObject go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            Image img = UI.AddGalleryElementRoundedBg(go, bg);
            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            UIHoverBorder hb = go.AddComponent<UIHoverBorder>();
            try { hb.ApplyBorderSettings(); } catch { }

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                le.minWidth = le.preferredWidth = width;
                le.flexibleWidth = 0f;
            }
            else
            {
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
            }
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;

            UI.CreateLabel(go, label ?? "", fontSize, Color.white, TextAnchor.MiddleCenter, name: "Text");
            return go;
        }

        private static void ScanWlDestroyChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null) continue;
                try { UnityEngine.Object.Destroy(ch.gameObject); } catch { }
            }
        }
    }
}
