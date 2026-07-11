using System;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    // Try-On Mode: apply a preset live but non-destructively. A floating bar lets the
    // user Compare (hold to peek at the original), Revert, or Keep. Works identically in
    // desktop (overlay canvas) and VR (worldspace canvas) because the bar lives on the
    // same gallery canvas the user already points at.
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

        // Session state
        private bool _tryOnActive;
        private string _tryOnAtomUid;
        private JSONClass _tryOnBaseline;   // snapshot before the candidate was applied
        private JSONClass _tryOnCandidate;  // snapshot of the applied candidate (lazy, for Compare)
        private bool _tryOnTouchedPhysical; // cumulative: did any tried preset move the body?
        private bool _tryOnComparing;
        private string _tryOnCurrentName;

        // UI
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
            if (pathLower.EndsWith(".var")) return TryOnKind.None;
            if (pathLower.Contains("/subscene/") || pathLower.Contains("\\subscene\\") || category.Contains("SubScene")) return TryOnKind.None;
            if (pathLower.Contains("/assets/") || pathLower.Contains("\\assets\\") || pathLower.EndsWith(".assetbundle") || pathLower.EndsWith(".unity3d")) return TryOnKind.None;
            bool isScene = pathLower.EndsWith(".json") && (pathLower.Contains("/scene/") || pathLower.Contains("\\scene\\") || pathLower.Contains("saves/scene") || category.Contains("Scene"));
            if (isScene) return TryOnKind.None;

            if (pathLower.Contains("/clothing/") || pathLower.Contains("\\clothing\\") || category.Contains("Clothing")) return TryOnKind.Clothing;
            if (pathLower.Contains("/hair/") || pathLower.Contains("\\hair\\") || category.Contains("Hair")) return TryOnKind.Hair;
            if (pathLower.Contains("/skin/") || pathLower.Contains("\\skin\\") || category.Contains("Skin")) return TryOnKind.Skin;
            if (pathLower.Contains("/morphs/") || pathLower.Contains("\\morphs\\") || category.Contains("Morphs")) return TryOnKind.Morphs;
            if (pathLower.Contains("/appearance/") || pathLower.Contains("\\appearance\\") || category.Contains("Appearance")) return TryOnKind.Appearance;

            bool isPluginPreset =
                pathLower.Contains("/custom/atom/person/plugins/") ||
                pathLower.Contains("\\custom\\atom\\person\\plugins\\") ||
                (pathLower.EndsWith(".vap") && (categoryLower.Contains("person plugins") || categoryLower.Contains("plugin preset")));
            if (isPluginPreset) return TryOnKind.Plugins;

            if (pathLower.Contains("/pose/") || pathLower.Contains("\\pose\\") || pathLower.Contains("/person/") || pathLower.Contains("\\person\\") || category.Contains("Pose")) return TryOnKind.Pose;

            return TryOnKind.None;
        }

        /// <summary>
        /// Routes an apply through Try-On Mode. Returns true if the apply was handled here
        /// (the caller must then skip its own apply). Returns false to fall through to the
        /// normal one-shot apply path.
        /// </summary>
        private bool TryOnInterceptApply(FileEntry file)
        {
            if (!TryOnIsEnabled()) return false;

            TryOnKind kind = TryOnClassify(file);
            if (kind == TryOnKind.None) return false;

            Atom target = GetBestTargetAtom();
            if (target == null)
            {
                // No valid target: let the normal path log the "select a Person" hint.
                return false;
            }

            try
            {
                // Changing the interaction away from the current try-on (applying a different
                // preset/item, on this atom or another) auto-accepts it: commit the candidate and
                // push its undo before starting a fresh session. The new session's baseline is the
                // just-accepted look, so Revert returns to it rather than discarding it.
                if (_tryOnActive) TryOnKeep();

                _tryOnBaseline = TryOnCaptureState(target);
                if (_tryOnBaseline == null) return false; // capture failed, fall through
                _tryOnAtomUid = target.uid;
                _tryOnTouchedPhysical = false;
                _tryOnActive = true;

                // Candidate snapshot is captured lazily on first Compare.
                _tryOnCandidate = null;
                _tryOnComparing = false;
                if (kind == TryOnKind.Pose || kind == TryOnKind.Plugins)
                    _tryOnTouchedPhysical = true;

                _tryOnCurrentName = TryOnNiceName(file);

                // Apply the candidate live.
                ExecuteAutoActionForFile(file);

                TryOnShowBar();
                TryOnUpdateLabel();
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] TryOnInterceptApply error: " + ex);
                // Best-effort: tear down a half-built session rather than leave a stuck bar.
                try { TryOnEndSession(false); } catch { }
                return false;
            }
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
                // restoreCore:false keeps on/collision/parenting flags; setMissingToDefault:true
                // cleanly removes storables the candidate added that the baseline never had.
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

            // Make sure we are showing the candidate, not a held Compare peek.
            if (_tryOnComparing && _tryOnCandidate != null)
                TryOnRestoreState(_tryOnCandidate, _tryOnTouchedPhysical);
            _tryOnComparing = false;

            // Preserve the ability to undo the committed change.
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
                });
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

        // Called right before a full scene load replaces everything: drop the session silently.
        internal void TryOnAbandonForSceneLoad()
        {
            if (_tryOnActive) TryOnEndSession(false);
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
        }

        // ---- UI ----

        private void TryOnShowBar()
        {
            TryOnEnsureBar();
            if (_tryOnBarGO != null)
            {
                TryOnLayoutBar();
                _tryOnBarGO.SetActive(true);
                _tryOnBarGO.transform.SetAsLastSibling();
            }
        }

        // Height (in toolbox units) of the Try-On row. Matches a standard toolbox row so the
        // bar reads as part of the toolbox stack.
        private float TryOnRowHeight(float s)
        {
            float h = tboxInfoRowHeight;
            float min = 40f * s;
            if (h < min) h = min;
            return h;
        }

        // Extra height the toolbox must reserve at its top for the active Try-On bar (row +
        // gap). Zero when no session is active. Called from the toolbox height calc so the
        // toolbox grows to include the bar instead of the bar floating over the buttons.
        private float TryOnToolboxReservedHeight()
        {
            if (!_tryOnActive || _tryOnBarGO == null) return 0f;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            return TryOnRowHeight(s) + tboxBtnRowGap;
        }

        // Pin the bar to the top edge of the toolbox interior, filling the reserved row.
        // The toolbox top grows (TryOnToolboxReservedHeight) to make room, so the bar sits
        // above the action buttons rather than over them. Re-run each layout pass so it
        // tracks resize, toolbox expansion, and scale changes.
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
                ? "Try-On: showing ORIGINAL  (" + name + ")"
                : "Try-On: " + name;
        }

        private void TryOnEnsureBar()
        {
            if (_tryOnBarGO != null) return;
            // The bar lives INSIDE the toolbox so it participates in the toolbox layout and
            // the toolbox grows to include it (see TryOnToolboxReservedHeight / TryOnLayoutBar).
            EnsureTboxUI();
            GameObject parent = tbox != null ? tbox : backgroundBoxGO;
            if (parent == null) return;

            GameObject bar = new GameObject("VPB_TryOnBar");
            _tryOnBarGO = bar;
            bar.transform.SetParent(parent.transform, false);
            Image barBg = bar.AddComponent<Image>();
            barBg.color = new Color(0.08f, 0.08f, 0.10f, 0.96f);
            barBg.raycastTarget = true;

            // Pinned to the top edge of the toolbox interior (set fully in TryOnLayoutBar).
            // Scales with the rest of the chrome.
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            RectTransform rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-(16f * s), TryOnRowHeight(s));

            HorizontalLayoutGroup row = bar.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(8f * s), Mathf.RoundToInt(8f * s));
            row.spacing = 8f * s;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            // Label (takes the remaining width).
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(bar.transform, false);
            Text label = labelGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontStyle = FontStyle.Normal;
            label.fontSize = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef);
            label.color = new Color(1f, 1f, 1f, 0.92f);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutElement labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1f;
            _tryOnLabel = label;

            // Compare (hold to peek at the original).
            GameObject compareGO = TryOnCreateButton(bar, "Compare", new Color(0.20f, 0.24f, 0.30f, 1f), 120f * s, s, null);
            TryOnCompareHandler handler = compareGO.AddComponent<TryOnCompareHandler>();
            handler.Panel = this;

            // Revert (discard the candidate, restore the original).
            TryOnCreateButton(bar, "Revert", new Color(0.34f, 0.16f, 0.16f, 1f), 110f * s, s, TryOnRevert);

            // Keep (commit the candidate, end the session).
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
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = cb;
            btn.transition = Selectable.Transition.ColorTint;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.flexibleWidth = 0f;

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontStyle = FontStyle.Normal;
            t.fontSize = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, scale, GalleryUiDesignTokens.FontMinRef);
            t.color = new Color(1f, 1f, 1f, 0.95f);
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            return go;
        }
    }

    /// <summary>
    /// Hold-to-peek handler for the Try-On Compare button. Pointer down restores the original;
    /// pointer up (or pointer exit) restores the candidate. Works for mouse (desktop) and the
    /// VR pointer trigger because both raise the same Unity pointer events on the gallery canvas.
    /// </summary>
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
