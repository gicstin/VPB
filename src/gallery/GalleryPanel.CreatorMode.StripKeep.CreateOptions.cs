using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Strip Scene post-rebuild create options: Default 3P Lights XOR Import SubScene.
    /// Shown when no lights kept. Path + last mode persist in VPBConfig (recognition).
    /// </summary>
    public partial class GalleryPanel
    {
        /// <summary>Synthetic uid: spawn SubScene atom + load path after strip rebuild.</summary>
        private const string StripKeepSyntheticImportSubSceneUid = "VPB_SYNTH_IMPORT_SUBSCENE";

        /// <summary>0=none, 1=3P, 2=SubScene — last create-fill choice for auto-seed.</summary>
        private enum StripKeepCreateFillMode
        {
            None = 0,
            Default3P = 1,
            ImportSubScene = 2,
        }

        private GameObject _stripKeepCreateOptionsHost;
        private GameObject _stripKeepImportSubSceneHost;
        private Toggle _stripKeepImportSubSceneToggle;
        private Text _stripKeepImportSubScenePathLabel;
        private string _stripKeepDefaultSubScenePath = "";
        private StripKeepCreateFillMode _stripKeepCreateFillMode = StripKeepCreateFillMode.Default3P;

        private void StripKeepCreateOptionsResetUiRefs()
        {
            _stripKeepCreateOptionsHost = null;
            _stripKeepImportSubSceneHost = null;
            _stripKeepImportSubSceneToggle = null;
            _stripKeepImportSubScenePathLabel = null;
            _stripKeepDefault3PHost = null;
            _stripKeepDefault3PToggle = null;
        }

        private void LoadStripKeepCreateOptionsFromConfig()
        {
            _stripKeepDefaultSubScenePath = "";
            _stripKeepCreateFillMode = StripKeepCreateFillMode.Default3P;
            try
            {
                if (VPBConfig.Instance == null) return;
                string p = VPBConfig.Instance.CreatorStripDefaultSubScenePath;
                _stripKeepDefaultSubScenePath = p != null ? p.Trim() : "";
                int m = VPBConfig.Instance.CreatorStripCreateFillMode;
                if (m < 0) m = 0;
                if (m > 2) m = 2;
                _stripKeepCreateFillMode = (StripKeepCreateFillMode)m;
            }
            catch { }
        }

        private void PersistStripKeepCreateOptionsToConfig()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.CreatorStripDefaultSubScenePath =
                    _stripKeepDefaultSubScenePath != null ? _stripKeepDefaultSubScenePath : "";
                VPBConfig.Instance.CreatorStripCreateFillMode = (int)_stripKeepCreateFillMode;
                VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        private void StripKeepWriteCreateOptionsToJson(JSONClass jc)
        {
            if (jc == null) return;
            jc["createFill"].AsInt = (int)_stripKeepCreateFillMode;
            jc["subScenePath"] = _stripKeepDefaultSubScenePath != null
                ? _stripKeepDefaultSubScenePath
                : "";
        }

        private void StripKeepReadCreateOptionsFromJson(JSONClass jc)
        {
            if (jc == null) return;
            if (jc["subScenePath"] != null)
            {
                string p = jc["subScenePath"].Value;
                _stripKeepDefaultSubScenePath = p != null ? p.Trim() : "";
            }
            if (jc["createFill"] != null)
            {
                int m = jc["createFill"].AsInt;
                if (m < 0) m = 0;
                if (m > 2) m = 2;
                _stripKeepCreateFillMode = (StripKeepCreateFillMode)m;
            }
            PersistStripKeepCreateOptionsToConfig();
            RefreshStripKeepCreateOptionsChrome();
        }

        private static string StripKeepSubSceneDisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string name = Path.GetFileName(path.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return path;
        }

        private static bool StripKeepSubScenePathExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (FileManager.FileExists(path)) return true;
            }
            catch { }
            try
            {
                string local = path.Replace('/', Path.DirectorySeparatorChar);
                if (File.Exists(local)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// External create-options block — pinned above atom list. Visible when lights stripped.
        /// Two exclusive fills: Default 3P XOR Import SubScene (Hick: one path).
        /// </summary>
        private void BuildStripKeepCreateOptionsSection(Transform parent, float btnH, int font, float s)
        {
            _stripKeepCreateOptionsHost = new GameObject("CreateOptions");
            _stripKeepCreateOptionsHost.transform.SetParent(parent, false);
            VerticalLayoutGroup v = UI.AddVLG(
                _stripKeepCreateOptionsHost, spacing: 2f * s, padding: UI.Pad(0, 0, 0, 0));
            v.childForceExpandHeight = false;
            v.childControlHeight = true;
            UI.AddImage(_stripKeepCreateOptionsHost, GalleryUiColorTokens.SurfaceDarker);
            UI.AddLE(_stripKeepCreateOptionsHost, flexibleWidth: 1f, flexibleHeight: 0f);

            BuildStripKeepDefault3PRow(_stripKeepCreateOptionsHost.transform, btnH, font, s);
            BuildStripKeepImportSubSceneRow(_stripKeepCreateOptionsHost.transform, btnH, font, s);
            RefreshStripKeepCreateOptionsChrome();
        }

        private void BuildStripKeepImportSubSceneRow(Transform parent, float btnH, int font, float s)
        {
            _stripKeepImportSubSceneHost = new GameObject("ImportSubSceneRow");
            _stripKeepImportSubSceneHost.transform.SetParent(parent, false);
            UI.AddLE(
                _stripKeepImportSubSceneHost,
                minHeight: btnH, preferredHeight: btnH,
                flexibleWidth: 1f, flexibleHeight: 0f);
            UI.AddImage(_stripKeepImportSubSceneHost, GalleryUiColorTokens.SurfacePanel);
            UI.AddHLG(
                _stripKeepImportSubSceneHost, spacing: 4f * s, padding: UI.Pad(4, 4, 2, 2, s),
                childForceExpandWidth: false, childForceExpandHeight: false,
                childControlWidth: true, childControlHeight: true,
                childAlignment: TextAnchor.MiddleLeft);

            GameObject toggleHost = new GameObject("ToggleHost");
            toggleHost.transform.SetParent(_stripKeepImportSubSceneHost.transform, false);
            if (toggleHost.GetComponent<RectTransform>() == null)
                toggleHost.AddComponent<RectTransform>();
            UI.AddLE(
                toggleHost,
                flexibleWidth: 0.55f, minWidth: 90f * s,
                minHeight: btnH, preferredHeight: btnH, flexibleHeight: 0f);

            string label = VPBTranslation.T(
                "gallery.creator.strip_import_subscene",
                "Import SubScene");
            _stripKeepSuppressToggleEvents = true;
            GameObject toggleGo = UI.CreateUIToggle(
                toggleHost, 0, 0, label, font, 0, 0, AnchorPresets.stretchAll,
                OnStripKeepImportSubSceneToggle);
            StripKeepFillInParent(toggleGo);
            _stripKeepImportSubSceneToggle = toggleGo.GetComponent<Toggle>();
            if (_stripKeepImportSubSceneToggle != null)
            {
                _stripKeepImportSubSceneToggle.isOn =
                    _stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid);
            }
            _stripKeepSuppressToggleEvents = false;

            Text toggleLabel = toggleGo.GetComponentInChildren<Text>();
            if (toggleLabel != null)
            {
                GalleryUiMetrics.ApplyFont(
                    toggleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                toggleLabel.raycastTarget = false;
                StripKeepClampText(toggleLabel);
                toggleLabel.color = GalleryUiColorTokens.TextMuted;
                RectTransform lrt = toggleLabel.GetComponent<RectTransform>();
                if (lrt != null)
                {
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(35f, 0f);
                    lrt.offsetMax = new Vector2(-4f * s, 0f);
                }
            }
            if (toggleHost.GetComponent<RectMask2D>() == null)
                toggleHost.AddComponent<RectMask2D>();

            GameObject pathHost = new GameObject("PathHost");
            pathHost.transform.SetParent(_stripKeepImportSubSceneHost.transform, false);
            UI.AddLE(
                pathHost,
                flexibleWidth: 1f, minWidth: 48f * s,
                minHeight: btnH * 0.85f, preferredHeight: btnH * 0.85f, flexibleHeight: 0f);
            UI.AddImage(pathHost, GalleryUiColorTokens.SurfaceDarker);
            if (pathHost.GetComponent<RectMask2D>() == null)
                pathHost.AddComponent<RectMask2D>();

            _stripKeepImportSubScenePathLabel = UI.CreateLabel(
                pathHost,
                "",
                font,
                GalleryUiColorTokens.TextMuted,
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "PathLabel");
            GalleryUiMetrics.ApplyFont(
                _stripKeepImportSubScenePathLabel,
                GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            StripKeepClampText(_stripKeepImportSubScenePathLabel);
            StripKeepFillInParent(_stripKeepImportSubScenePathLabel.gameObject);
            RectTransform pathRt = _stripKeepImportSubScenePathLabel.GetComponent<RectTransform>();
            if (pathRt != null)
            {
                pathRt.offsetMin = new Vector2(6f * s, 0f);
                pathRt.offsetMax = new Vector2(-4f * s, 0f);
            }

            float chipH = btnH * 0.85f;
            GameObject browseGo = StripKeepChromeButton(
                _stripKeepImportSubSceneHost.transform, 0f, chipH,
                VPBTranslation.T("gallery.creator.strip_subscene_pick", "Pick"),
                font, s,
                GalleryUiColorTokens.SurfaceMid,
                OnStripKeepBrowseDefaultSubSceneClicked);
            AddTooltipPlain(
                browseGo,
                VPBTranslation.T(
                    "gallery.creator.strip_subscene_pick_tip",
                    "Open gallery SubScenes category and pick default file. Confirm Pick to use, Cancel Pick to abort."));

            GameObject clearGo = StripKeepChromeButton(
                _stripKeepImportSubSceneHost.transform, 0f, chipH,
                VPBTranslation.T("gallery.creator.strip_subscene_clear", "×"),
                font, s,
                GalleryUiColorTokens.AccentDanger,
                OnStripKeepClearDefaultSubSceneClicked);
            AddTooltipPlain(
                clearGo,
                VPBTranslation.T(
                    "gallery.creator.strip_subscene_clear_tip",
                    "Clear saved SubScene path."));

            AddTooltipPlain(
                _stripKeepImportSubSceneHost,
                VPBTranslation.T(
                    "gallery.creator.strip_import_subscene_tip",
                    "After strip: spawn SubScene atom and load this file. Exclusive with Default 3P Lights. Pick from gallery SubScenes."));

            RefreshStripKeepImportSubScenePathLabel();
        }

        private void RefreshStripKeepImportSubScenePathLabel()
        {
            if (_stripKeepImportSubScenePathLabel == null) return;
            if (string.IsNullOrEmpty(_stripKeepDefaultSubScenePath))
            {
                _stripKeepImportSubScenePathLabel.text = VPBTranslation.T(
                    "gallery.creator.strip_subscene_none",
                    "(none — Pick)");
                _stripKeepImportSubScenePathLabel.color = GalleryUiColorTokens.TextDim;
            }
            else
            {
                _stripKeepImportSubScenePathLabel.text =
                    StripKeepSubSceneDisplayName(_stripKeepDefaultSubScenePath);
                bool exists = StripKeepSubScenePathExists(_stripKeepDefaultSubScenePath);
                _stripKeepImportSubScenePathLabel.color = exists
                    ? GalleryUiColorTokens.TextMuted
                    : GalleryUiColorTokens.AccentDangerStrong;
            }

            if (_stripKeepImportSubSceneHost != null
                && !string.IsNullOrEmpty(_stripKeepDefaultSubScenePath))
            {
                AddTooltipPlain(
                    _stripKeepImportSubSceneHost,
                    VPBTranslation.T(
                        "gallery.creator.strip_import_subscene_tip",
                        "After strip: spawn SubScene atom and load this file. Exclusive with Default 3P Lights.")
                    + "\n" + _stripKeepDefaultSubScenePath);
            }
        }

        private void OnStripKeepImportSubSceneToggle(bool on)
        {
            if (_stripKeepSuppressToggleEvents) return;
            StripKeepClearSoftConfirm();
            if (on)
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                _stripKeepSelectedUids.Add(StripKeepSyntheticImportSubSceneUid);
                _stripKeepCreateFillMode = StripKeepCreateFillMode.ImportSubScene;
                PersistStripKeepCreateOptionsToConfig();
                RefreshStripKeepDefault3PToggleOnly();
                if (string.IsNullOrEmpty(_stripKeepDefaultSubScenePath))
                    StripKeepStartSubScenePickMode();
            }
            else
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
                if (!_stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid))
                    _stripKeepCreateFillMode = StripKeepCreateFillMode.None;
                PersistStripKeepCreateOptionsToConfig();
            }
            RefreshStripKeepSummaryAndConfirm();
        }

        private void OnStripKeepBrowseDefaultSubSceneClicked()
        {
            StripKeepStartSubScenePickMode();
        }

        private void OnStripKeepClearDefaultSubSceneClicked()
        {
            StripKeepClearSoftConfirm();
            _stripKeepDefaultSubScenePath = "";
            _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
            if (_stripKeepCreateFillMode == StripKeepCreateFillMode.ImportSubScene)
                _stripKeepCreateFillMode = StripKeepCreateFillMode.None;
            PersistStripKeepCreateOptionsToConfig();
            RefreshStripKeepCreateOptionsChrome();
            RefreshStripKeepSummaryAndConfirm();
            ShowTemporaryStatus(
                VPBTranslation.T("gallery.creator.strip_subscene_cleared", "Default SubScene cleared."),
                1.25f);
        }

        private void RefreshStripKeepDefault3PToggleOnly()
        {
            if (_stripKeepDefault3PToggle == null) return;
            bool want = _stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid);
            if (_stripKeepDefault3PToggle.isOn == want) return;
            _stripKeepSuppressToggleEvents = true;
            _stripKeepDefault3PToggle.isOn = want;
            _stripKeepSuppressToggleEvents = false;
        }

        private void RefreshStripKeepImportSubSceneToggleOnly()
        {
            if (_stripKeepImportSubSceneToggle == null) return;
            bool want = _stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid);
            if (_stripKeepImportSubSceneToggle.isOn == want) return;
            _stripKeepSuppressToggleEvents = true;
            _stripKeepImportSubSceneToggle.isOn = want;
            _stripKeepSuppressToggleEvents = false;
        }

        private void RefreshStripKeepCreateOptionsChrome()
        {
            if (_stripKeepCreateOptionsHost == null)
            {
                // Legacy single-row rebuild path.
                RefreshStripKeepDefault3PChromeLegacy();
                return;
            }

            bool show = StripKeepShouldShowDefault3P();
            bool wasActive = _stripKeepCreateOptionsHost.activeSelf;
            if (!show)
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
                if (wasActive)
                {
                    _stripKeepCreateOptionsHost.SetActive(false);
                    ScheduleStripKeepSnugPanelHeight();
                }
                return;
            }

            if (!wasActive)
            {
                _stripKeepCreateOptionsHost.SetActive(true);
                ScheduleStripKeepSnugPanelHeight();
            }

            RefreshStripKeepDefault3PToggleOnly();
            RefreshStripKeepImportSubSceneToggleOnly();
            RefreshStripKeepImportSubScenePathLabel();
        }

        /// <summary>Fallback if create-options host missing (partial rebuild).</summary>
        private void RefreshStripKeepDefault3PChromeLegacy()
        {
            if (_stripKeepDefault3PHost == null) return;

            bool show = StripKeepShouldShowDefault3P();
            bool wasActive = _stripKeepDefault3PHost.activeSelf;
            if (!show)
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
                if (wasActive)
                {
                    _stripKeepDefault3PHost.SetActive(false);
                    ScheduleStripKeepSnugPanelHeight();
                }
                return;
            }

            if (!wasActive)
            {
                _stripKeepDefault3PHost.SetActive(true);
                ScheduleStripKeepSnugPanelHeight();
            }

            RefreshStripKeepDefault3PToggleOnly();
            RefreshStripKeepImportSubSceneToggleOnly();
            RefreshStripKeepImportSubScenePathLabel();
        }

        /// <summary>
        /// Auto-pick create fill when Lights kept in mask but scene has none.
        /// Prefer last mode (SubScene if path set, else 3P).
        /// </summary>
        private void StripKeepSeedCreateFillWhenNoLights()
        {
            if (!StripKeepShouldShowDefault3P()) return;
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid)
                || _stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
                return;

            if (_stripKeepCreateFillMode == StripKeepCreateFillMode.ImportSubScene
                && !string.IsNullOrEmpty(_stripKeepDefaultSubScenePath))
            {
                _stripKeepSelectedUids.Add(StripKeepSyntheticImportSubSceneUid);
                return;
            }

            // Default / remembered 3P / SubScene with empty path → offer 3P.
            _stripKeepSelectedUids.Add(StripKeepSyntheticDefault3PUid);
            if (_stripKeepCreateFillMode == StripKeepCreateFillMode.None)
                _stripKeepCreateFillMode = StripKeepCreateFillMode.Default3P;
        }

        private string StripKeepCreateOptionsSummarySuffix()
        {
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
            {
                string name = StripKeepSubSceneDisplayName(_stripKeepDefaultSubScenePath);
                if (string.IsNullOrEmpty(name)) name = "?";
                return "  ·  +" + VPBTranslation.T(
                    "gallery.creator.strip_summary_subscene",
                    "SubScene") + " " + name;
            }
            if (_stripKeepSelectedUids.Contains(StripKeepSyntheticDefault3PUid))
            {
                return "  ·  +" + VPBTranslation.T(
                    "gallery.creator.strip_summary_3p",
                    "3P");
            }
            return "";
        }

        private bool StripKeepValidateCreateOptionsForConfirm(out string errorMsg)
        {
            errorMsg = null;
            if (!_stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
                return true;
            if (string.IsNullOrEmpty(_stripKeepDefaultSubScenePath))
            {
                errorMsg = VPBTranslation.T(
                    "gallery.creator.strip_subscene_need_path",
                    "Import SubScene selected — Pick a SubScene from gallery first.");
                return false;
            }
            if (!StripKeepSubScenePathExists(_stripKeepDefaultSubScenePath))
            {
                errorMsg = VPBTranslation.T(
                    "gallery.creator.strip_subscene_missing",
                    "Default SubScene file not found — Pick again.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// After strip rebuild: EnsureInstalled → AddAtom SubScene → LoadSubSceneWithPath.
        /// Uses MoveNext spawn loop (Unity 2018 nested IEnumerator-safe) + preferred uid.
        /// Warm path only.
        /// </summary>
        private IEnumerator CreatorStripImportDefaultSubSceneRoutine(string path)
        {
            if (string.IsNullOrEmpty(path)) yield break;
            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;

            string normalized = path;
            try { normalized = UI.NormalizePath(path); }
            catch { normalized = path; }

            // Install / resolve packages referenced by SubScene before spawn (cold/warm).
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_ensure",
                        "Resolving SubScene packages…"),
                    1.5f);
            }
            catch { }
            bool installed = false;
            try { installed = SceneLoadingUtils.EnsureInstalled(normalized); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Creator Strip EnsureInstalled: " + ex.Message);
                installed = false;
            }
            if (!installed)
            {
                LogUtil.LogWarning("[VPB] Creator Strip SubScene EnsureInstalled returned false for "
                    + normalized + " — continuing load attempt.");
            }
            yield return null;

            // Settle after strip rebuild before spawning.
            float settleDeadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < settleDeadline)
            {
                bool loading = false;
                try { if (sc.isLoading) loading = true; } catch { }
                try { if (!loading && LogUtil.IsSceneLoading()) loading = true; } catch { }
                if (!loading) break;
                yield return null;
            }
            yield return null;
            yield return new WaitForEndOfFrame();

            const string preferredUid = "VPB_StripImportSubScene";
            HashSet<string> before = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                List<Atom> atoms = sc.GetAtoms();
                if (atoms != null)
                {
                    for (int i = 0; i < atoms.Count; i++)
                    {
                        Atom a = atoms[i];
                        if (a == null) continue;
                        string uid = null;
                        try { uid = a.uid; } catch { uid = null; }
                        if (!string.IsNullOrEmpty(uid)) before.Add(uid);
                    }
                }
            }
            catch { }

            // Prefer stable uid; reuse if strip left one from prior import.
            Atom subSceneAtom = null;
            try { subSceneAtom = sc.GetAtomByUid(preferredUid); } catch { subSceneAtom = null; }
            if (subSceneAtom != null && !SceneUtils.IsSubSceneAtom(subSceneAtom))
                subSceneAtom = null;

            if (subSceneAtom == null)
            {
                IEnumerator addIe = null;
                try
                {
                    // false,false,false — avoid UI select side-effects right after strip rebuild.
                    addIe = sc.AddAtomByType("SubScene", preferredUid, false, false, false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Creator Strip AddAtomByType(SubScene) failed: " + ex.Message);
                    yield break;
                }

                // Explicit MoveNext — do not rely on nested yield return IEnumerator (Unity 2018 Mono).
                while (addIe != null && addIe.MoveNext())
                    yield return addIe.Current;

                yield return null;
                yield return new WaitForEndOfFrame();

                float findDeadline = Time.realtimeSinceStartup + 30f;
                while (Time.realtimeSinceStartup < findDeadline && subSceneAtom == null)
                {
                    try { subSceneAtom = sc.GetAtomByUid(preferredUid); } catch { subSceneAtom = null; }
                    if (subSceneAtom != null && !SceneUtils.IsSubSceneAtom(subSceneAtom))
                        subSceneAtom = null;
                    if (subSceneAtom == null)
                        subSceneAtom = StripKeepFindNewSubSceneAtom(sc, before);
                    if (subSceneAtom != null) break;

                    bool loading = false;
                    try { if (sc.isLoading) loading = true; } catch { }
                    if (!loading)
                    {
                        subSceneAtom = StripKeepFindNewSubSceneAtom(sc, before);
                        if (subSceneAtom != null) break;
                    }
                    yield return null;
                }
            }

            if (subSceneAtom == null)
            {
                LogUtil.LogError("[VPB] Creator Strip: could not find new SubScene atom after AddAtomByType"
                    + " path=" + normalized);
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_spawn_fail",
                        "Strip done, but SubScene atom failed to spawn."),
                    3f);
                yield break;
            }

            SubScene sub = null;
            try { sub = subSceneAtom.GetComponentInChildren<SubScene>(true); }
            catch { sub = null; }
            if (sub == null)
            {
                // Component may appear a frame late — yield outside try/catch (CS1626).
                yield return null;
                yield return new WaitForEndOfFrame();
                try { sub = subSceneAtom.GetComponentInChildren<SubScene>(true); }
                catch { sub = null; }
            }
            if (sub == null)
            {
                LogUtil.LogError("[VPB] Creator Strip: SubScene component missing on "
                    + subSceneAtom.uid);
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_comp_fail",
                        "SubScene atom spawned but component missing."),
                    3f);
                yield break;
            }

            try
            {
                MethodInfo loadMethod = StripKeepResolveLoadSubSceneWithPath();
                if (loadMethod == null)
                {
                    LogUtil.LogError("[VPB] Creator Strip: LoadSubSceneWithPath not found");
                    yield break;
                }
                LogUtil.LogWarning("[VPB] Creator Strip import SubScene path=" + normalized
                    + " atom=" + subSceneAtom.uid);
                loadMethod.Invoke(sub, new object[] { normalized });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Creator Strip SubScene import failed: " + ex.Message);
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_load_fail",
                        "SubScene import failed — see log."),
                    3f);
                yield break;
            }

            // Wait for nested load / package resolve.
            float loadDeadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < loadDeadline)
            {
                bool loading = false;
                try { if (sc.isLoading) loading = true; } catch { }
                try { if (!loading && LogUtil.IsSceneLoading()) loading = true; } catch { }
                if (!loading) break;
                yield return null;
            }
            yield return null;
            yield return new WaitForEndOfFrame();

            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.EndSceneLoad();
            }
            catch { }
        }

        private static Atom StripKeepFindNewSubSceneAtom(SuperController sc, HashSet<string> before)
        {
            if (sc == null) return null;
            List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { return null; }
            if (atoms == null) return null;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                if (!SceneUtils.IsSubSceneAtom(a)) continue;
                string uid = null;
                try { uid = a.uid; } catch { uid = null; }
                if (string.IsNullOrEmpty(uid)) continue;
                if (before != null && before.Contains(uid)) continue;
                return a;
            }
            return null;
        }

        private static MethodInfo s_LoadSubSceneWithPathMethod;
        private static bool s_LoadSubSceneWithPathResolved;

        private static MethodInfo StripKeepResolveLoadSubSceneWithPath()
        {
            if (s_LoadSubSceneWithPathResolved) return s_LoadSubSceneWithPathMethod;
            s_LoadSubSceneWithPathResolved = true;
            try
            {
                s_LoadSubSceneWithPathMethod = typeof(SubScene).GetMethod(
                    "LoadSubSceneWithPath",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { s_LoadSubSceneWithPathMethod = null; }
            return s_LoadSubSceneWithPathMethod;
        }
    }
}
