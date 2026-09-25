using System;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private enum TryOnKind
        {
            None,
            Clothing,
            Hair,
            Skin,
            Morphs,
            Appearance,
            Pose,
            Plugins
        }

        private bool _tryOnActive;
        private string _tryOnAtomUid;
        private JSONClass _tryOnBaseline;   // snapshot before the candidate was applied
        private JSONClass _tryOnCandidate;
        private bool _tryOnTouchedPhysical;
        private bool _tryOnComparing;
        private string _tryOnCurrentName;

        private GameObject _tryOnBarGO;
        private Text _tryOnLabel;

        private bool TryOnIsEnabled()
        {
            try { return VPBConfig.Instance != null && VPBConfig.Instance.TryOnModeEnabled; }
            catch { return false; }
        }

        private TryOnKind TryOnClassify(FileEntry file)
        {
            if (file == null) return TryOnKind.None;

            string pathLower = (file.Path ?? "").ToLowerInvariant();
            string category = CurrentCategoryTitle ?? "";
            string categoryLower = category.ToLowerInvariant();

            // Excluded from Try-On (full-scene / object-spawning / cache-only operations).
            if (pathLower.EndsWith(".var", StringComparison.Ordinal)) return TryOnKind.None;
            if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene")) return TryOnKind.None;
            if (pathLower.Contains("/assets/") || pathLower.Contains("\\assets\\") || pathLower.EndsWith(".assetbundle", StringComparison.Ordinal) || pathLower.EndsWith(".unity3d", StringComparison.Ordinal)) return TryOnKind.None;
            bool isScene = pathLower.EndsWith(".json", StringComparison.Ordinal) && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || category.Contains("Scene"));
            if (isScene) return TryOnKind.None;

            if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing")) return TryOnKind.Clothing;
            if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair")) return TryOnKind.Hair;
            if (pathLower.Contains("/skin/") || pathLower.Contains("\\skin\\") || category.Contains("Skin")) return TryOnKind.Skin;
            if (pathLower.Contains("/morphs/") || pathLower.Contains("\\morphs\\") || category.Contains("Morphs")) return TryOnKind.Morphs;
            if (pathLower.Contains("/appearance/") || pathLower.Contains("\\appearance\\") || category.Contains("Appearance")) return TryOnKind.Appearance;

            bool isPluginScript =
                (pathLower.Contains("/custom/scripts/") || pathLower.Contains("\\custom\\scripts\\"))
                && (pathLower.EndsWith(".cs", StringComparison.Ordinal) || pathLower.EndsWith(".cslist", StringComparison.Ordinal) || pathLower.EndsWith(".dll", StringComparison.Ordinal));
            bool isPluginPreset =
                pathLower.Contains("/custom/atom/person/plugins/") ||
                pathLower.Contains("\\custom\\atom\\person\\plugins\\") ||
                pathLower.Contains("/custom/pluginpresets/") ||
                pathLower.Contains("\\custom\\pluginpresets\\") ||
                (pathLower.EndsWith(".vap", StringComparison.Ordinal) && (categoryLower.Contains("person plugins") || categoryLower.Contains("plugin preset") || categoryLower.Contains("plugins")));
            if (isPluginScript || isPluginPreset) return TryOnKind.Plugins;

            if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose")) return TryOnKind.Pose;

            return TryOnKind.None;
        }

        private bool TryOnInterceptApply(FileEntry file)
        {
            if (!TryOnIsEnabled()) return false;

            TryOnKind kind = TryOnClassify(file);
            if (kind == TryOnKind.None) return false;

            Atom target = GetBestTargetAtom();
            if (target == null)
            {
                return false;
            }

            try
            {
                string autoKeptName = null;
                if (_tryOnActive)
                {
                    autoKeptName = _tryOnCurrentName;
                    try { TryOnKeep(); } catch { }
                }

                bool started = TryOnBeginSessionWithFile(file, kind, target);
                TryOnNotifyAutoKept(autoKeptName);
                return started;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TryOnInterceptApply error: " + ex);
                try { TryOnEndSession(false); } catch { }
                return false;
            }
        }

        private void TryOnNotifyAutoKept(string keptName)
        {
            if (string.IsNullOrEmpty(keptName)) return;
            try
            {
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T(
                            "gallery.tryon.auto_kept",
                            "Kept '{0}' — Undo restores the previous look."),
                        keptName),
                    2f);
            }
            catch { }
        }

        private bool TryOnBeginSessionWithFile(FileEntry file, TryOnKind kind, Atom target)
        {
            _tryOnBaseline = TryOnCaptureState(target);
            if (_tryOnBaseline == null) return false;
            try { ExitOtherStickyToolModes(StickyToolMode.TryOn); } catch { }
            _tryOnAtomUid = target.uid;
            _tryOnTouchedPhysical = false;
            _tryOnActive = true;

            _tryOnCandidate = null;
            _tryOnComparing = false;
            if (kind == TryOnKind.Pose || kind == TryOnKind.Plugins)
                _tryOnTouchedPhysical = true;

            _tryOnCurrentName = TryOnNiceName(file);

            ExecuteAutoActionForFile(file);

            TryOnShowBar();
            TryOnUpdateLabel();
            try { RefreshModeAmbientChrome(); } catch { }
            return true;
        }

        private static string TryOnNiceName(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                string p = file.Uid;
                if (string.IsNullOrEmpty(p)) p = file.Path;
                if (string.IsNullOrEmpty(p)) return "";
                p = p.Replace('\\', '/');
                int slash = p.LastIndexOf('/');
                if (slash >= 0 && slash < p.Length - 1) p = p.Substring(slash + 1);
                int dot = p.LastIndexOf('.');
                if (dot > 0) p = p.Substring(0, dot);
                return p;
            }
            catch { return ""; }
        }

        private JSONClass TryOnCaptureState(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                JSONArray arr = new JSONArray();
                atom.Store(arr, true, true);
                if (arr.Count == 0) return null;
                return arr[0].AsObject;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TryOnCaptureState error: " + ex);
                return null;
            }
        }

        private void TryOnRestoreState(JSONClass state, bool restorePhysical)
        {
            if (state == null) return;
            Atom atom = null;
            try { atom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(_tryOnAtomUid) : null; }
            catch { atom = null; }
            if (atom == null) return;

            try
            {
                atom.Restore(state, restorePhysical, true, false, null, false, false, true, false);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TryOnRestoreState error: " + ex);
            }
        }

        private void TryOnKeep()
        {
            if (!_tryOnActive) return;

            if (_tryOnComparing && _tryOnCandidate != null)
                TryOnRestoreState(_tryOnCandidate, _tryOnTouchedPhysical);
            _tryOnComparing = false;

            JSONClass baseline = _tryOnBaseline;
            string uid = _tryOnAtomUid;
            bool phys = _tryOnTouchedPhysical;
            if (baseline != null && !string.IsNullOrEmpty(uid))
            {
                PushUndo(() =>
                {
                    try
                    {
                        Atom a = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(uid) : null;
                        if (a != null) a.Restore(baseline, phys, true, false, null, false, false, true, false);
                    }
                    catch { }
                }, VPBTranslation.T("gallery.undo.tryon_keep", "Try-On keep"));
            }

            TryOnEndSession(false);
        }

        private void TryOnRevert()
        {
            if (!_tryOnActive) return;
            _tryOnComparing = false;
            TryOnRestoreState(_tryOnBaseline, _tryOnTouchedPhysical);
            TryOnEndSession(false);
        }

        internal void TryOnCompareBegin()
        {
            if (!_tryOnActive || _tryOnComparing) return;
            // Capture the candidate now (the atom is currently showing it) so the peek is race-free.
            if (_tryOnCandidate == null)
            {
                Atom atom = null;
                try { atom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(_tryOnAtomUid) : null; }
                catch { atom = null; }
                if (atom != null) _tryOnCandidate = TryOnCaptureState(atom);
            }
            _tryOnComparing = true;
            TryOnRestoreState(_tryOnBaseline, _tryOnTouchedPhysical);
            TryOnUpdateLabel();
        }

        internal void TryOnCompareEnd()
        {
            if (!_tryOnActive || !_tryOnComparing) return;
            _tryOnComparing = false;
            if (_tryOnCandidate != null)
                TryOnRestoreState(_tryOnCandidate, _tryOnTouchedPhysical);
            TryOnUpdateLabel();
        }

        // Called right before a full scene load replaces everything: drop the session.
        internal void TryOnAbandonForSceneLoad()
        {
            bool wasActive = _tryOnActive;
            if (_tryOnActive) TryOnEndSession(false);
            if (wasActive)
            {
                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.tryon.abandoned_scene_load",
                            "Try-On discarded — scene load replaced preview."),
                        2f);
                }
                catch { }
            }
        }

        private void TryOnEndSession(bool revertFirst)
        {
            if (revertFirst && _tryOnActive)
            {
                try { TryOnRestoreState(_tryOnBaseline, _tryOnTouchedPhysical); } catch { }
            }
            _tryOnActive = false;
            _tryOnAtomUid = null;
            _tryOnBaseline = null;
            _tryOnCandidate = null;
            _tryOnTouchedPhysical = false;
            _tryOnComparing = false;
            _tryOnCurrentName = null;
            TryOnHideBar();
            try { RefreshModeAmbientChrome(); } catch { }
            try { ResetArmedApplySemanticsIfIdle(toast: false); } catch { }
        }

        private void TryOnShowBar()
        {
            TryOnEnsureBar();
            if (_tryOnBarGO != null)
            {
                TryOnLayoutBar();
                _tryOnBarGO.SetActive(true);
                _tryOnBarGO.transform.SetAsLastSibling();
            }
            try { RefreshModeAmbientChrome(); } catch { }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.tryon.entered",
                        "Try-On — Compare / Revert / Keep. Browsing on keeps it (Undo reverts)."),
                    1.75f);
            }
            catch { }
        }

        private float TryOnRowHeight(float s)
        {
            float h = tboxInfoRowHeight;
            float min = 40f * s;
            if (h < min) h = min;
            return h;
        }

        // Extra height the toolbox must reserve at its top for the active Try-On bar (row + gap).
        private float TryOnToolboxReservedHeight()
        {
            if (!_tryOnActive || _tryOnBarGO == null) return 0f;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            return TryOnRowHeight(s) + TboxBtnRowGapScaled();
        }

        private void TryOnLayoutBar()
        {
            if (_tryOnBarGO == null) return;
            RectTransform rt = _tryOnBarGO.GetComponent<RectTransform>();
            if (rt == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            float rowH = TryOnRowHeight(s);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(-(16f * s), rowH);
        }

        private void TryOnHideBar()
        {
            if (_tryOnBarGO != null) _tryOnBarGO.SetActive(false);
        }

        private void TryOnUpdateLabel()
        {
            if (_tryOnLabel == null) return;
            string name = string.IsNullOrEmpty(_tryOnCurrentName) ? "preset" : _tryOnCurrentName;
            _tryOnLabel.text = _tryOnComparing
                ? "Try-On: showing ORIGINAL  (" + name + ") — release Compare to return"
                : "Try-On: " + name + " — Keep · Revert · Esc discards · moving on keeps it";
        }

        private void TryOnEnsureBar()
        {
            if (_tryOnBarGO != null) return;
            EnsureTboxUI();
            GameObject parent = tbox != null ? tbox : backgroundBoxGO;
            if (parent == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            GameObject bar = UI.CreateChildRT(parent, "VPB_TryOnBar", AnchorPresets.hStretchTop, new Vector2(-(16f * s), TryOnRowHeight(s)));
            _tryOnBarGO = bar;
            Image barBg = UI.AddImage(bar, new Color(0.08f, 0.08f, 0.10f, 0.96f));

            HorizontalLayoutGroup row = UI.AddHLG(bar, spacing: UI.GapControl(s), padding: UI.PadHV(GalleryUiDesignTokens.GroupGapRef, GalleryUiDesignTokens.ControlGapRef, s), childForceExpandWidth: false, childForceExpandHeight: true);

            Text label = UI.CreateLabel(bar, "", GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef), new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, name: "Label");
            LayoutElement labelLE = UI.AddLE(label.gameObject, flexibleWidth: 1f);
            _tryOnLabel = label;

            GameObject compareGO = TryOnCreateButton(bar, "Compare", new Color(0.20f, 0.24f, 0.30f, 1f), 120f * s, s, null);
            TryOnCompareHandler handler = compareGO.AddComponent<TryOnCompareHandler>();
            handler.Panel = this;

            TryOnCreateButton(bar, "Revert", new Color(0.34f, 0.16f, 0.16f, 1f), 110f * s, s, TryOnRevert);

            TryOnCreateButton(bar, "Keep", new Color(0.16f, 0.32f, 0.18f, 1f), 110f * s, s, TryOnKeep);

            bar.SetActive(false);
        }

        private GameObject TryOnCreateButton(GameObject parent, string text, Color bg, float width, float scale, Action onClick)
        {
            GameObject go = new GameObject("Button_" + text);
            go.transform.SetParent(parent.transform, false);
            Image img = UI.AddGalleryElementRoundedBg(go, bg);
            img.raycastTarget = true;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UI.ConfigButtonFlat(btn, applyColors: true);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            UI.EnsureFloatChromeHoverBorder(go, inward: true);

            LayoutElement le = UI.AddLE(go, minWidth: width, preferredWidth: width, flexibleWidth: 0f);

            UI.CreateLabel(go, text, GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, scale, GalleryUiDesignTokens.FontMinRef), new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, name: "Text");

            return go;
        }
    }

    internal class TryOnCompareHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public GalleryPanel Panel;
        private bool _held;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Panel == null) return;
            _held = true;
            try { Panel.TryOnCompareBegin(); } catch { }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndPeek();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            EndPeek();
        }

        private void EndPeek()
        {
            if (Panel == null || !_held) return;
            _held = false;
            try { Panel.TryOnCompareEnd(); } catch { }
        }
    }
}
