using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Plugins float header destinations: Session + one chip per Person.
    /// Click → Edit mode + open VaM Plugins UI. Drop → load onto that host (session or person).
    /// </summary>
    public partial class GalleryPanel
    {

        private GameObject _pluginsFloatDestHost;
        private RectTransform _pluginsFloatDestContentRT;
        private GameObject _pluginsFloatSessionDestBtn;
        private Image _pluginsFloatSessionDestBg;
        private readonly List<GameObject> _pluginsFloatPersonDestPool = new List<GameObject>(8);
        private readonly List<string> _pluginsFloatPersonDestUids = new List<string>(8);
        private UIDraggableItem _pluginsFloatDropBridge;
        private PluginsFloatDestKind _pluginsFloatDropHoverKind = PluginsFloatDestKind.None;
        private string _pluginsFloatDropHoverPersonUid;

        private enum PluginsFloatDestKind
        {
            None = 0,
            Session = 1,
            Person = 2
        }

        private void BuildPluginsFloatDestBar(GameObject titleBar, float s, int font, float chromeSz)
        {
            if (titleBar == null) return;
            // Scrollable chip strip in title bar (between title and window controls).
            // Height matches collapse/close (chromeSz). No host RectMask2D — that was clipping
            // UIHoverBorder outward rim + chip fill. Viewport mask only, expanded vertically by
            // borderSize into title-bar pad so highlight has room while horizontal scroll still clips.
            _pluginsFloatDestHost = UI.CreateChildRT(titleBar, "DestHost", AnchorPresets.middleCenter,
                new Vector2(120f * s, chromeSz), Vector2.zero);
            UI.AddLE(_pluginsFloatDestHost, flexibleWidth: 1.4f, minWidth: 100f * s,
                minHeight: chromeSz, preferredHeight: chromeSz, flexibleHeight: 0f);

            ScrollRect sr = _pluginsFloatDestHost.AddComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = VpbScrollTuning.Sensitivity(20f, 1f);
            sr.horizontalScrollbar = null;
            sr.verticalScrollbar = null;

            GameObject vp = UI.CreateChildRT(_pluginsFloatDestHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = vp.GetComponent<RectTransform>();
            float rimPad = GalleryUiDesignTokens.ControlRimGutterRef * s;
            if (vpRt != null)
            {
                // Expand mask so outward hover rim is not clipped (incl. Session chip on left).
                vpRt.offsetMin = new Vector2(-rimPad, -rimPad);
                vpRt.offsetMax = new Vector2(rimPad, rimPad);
            }
            if (vp.GetComponent<RectMask2D>() == null)
                vp.AddComponent<RectMask2D>();
            sr.viewport = vpRt;

            GameObject content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _pluginsFloatDestContentRT = content.AddComponent<RectTransform>();
            _pluginsFloatDestContentRT.anchorMin = new Vector2(0f, 0.5f);
            _pluginsFloatDestContentRT.anchorMax = new Vector2(0f, 0.5f);
            _pluginsFloatDestContentRT.pivot = new Vector2(0f, 0.5f);
            // Nudge content right so first chip rim sits inside expanded viewport.
            _pluginsFloatDestContentRT.anchoredPosition = new Vector2(rimPad, 0f);
            _pluginsFloatDestContentRT.sizeDelta = new Vector2(0f, chromeSz);
            HorizontalLayoutGroup hlg = content.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f * s;
            int rimPx = Mathf.Max(1, Mathf.RoundToInt(rimPad));
            hlg.padding = new RectOffset(rimPx, rimPx, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _pluginsFloatDestContentRT;

            _pluginsFloatSessionDestBtn = CreatePluginsFloatDestChip(
                content.transform, s, font, chromeSz,
                VPBTranslation.T("gallery.plugins.dest_session", "Session"),
                "Session",
                GalleryUiColorTokens.ActiveOn,
                OpenPluginsFloatSessionUi);
            if (_pluginsFloatSessionDestBtn != null)
            {
                _pluginsFloatSessionDestBg = _pluginsFloatSessionDestBtn.GetComponent<Image>();
                AddTooltip(_pluginsFloatSessionDestBtn, "gallery.tooltip.plugins_dest_session",
                    "Click: open VaM Session Plugins. Drop: load as session plugin (also void).");
            }
        }

        private GameObject CreatePluginsFloatDestChip(
            Transform parent, float s, int font, float height,
            string label, string name, Color bgColor, UnityEngine.Events.UnityAction onClick)
        {
            if (parent == null) return null;
            float minW = 64f * s;
            // Same chrome button path as other float header controls (exact chromeSz height).
            GameObject go = UI.CreateChromeLayoutButton(
                parent, minW, height, label, font, bgColor, onClick);
            if (go == null) return null;
            go.name = name;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = minW;
            le.preferredWidth = minW;
            le.flexibleWidth = 0f;
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            Image bg = go.GetComponent<Image>();
            if (bg != null) bg.color = bgColor;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.alignment = TextAnchor.MiddleCenter;
                // Preferred width from text — expand chip for long person names (cap).
                float prefer = Mathf.Clamp(EstimatePluginsFloatChipWidth(label, s), minW, 120f * s);
                le.preferredWidth = prefer;
                le.minWidth = Mathf.Min(minW, prefer);
            }
            var meta = go.AddComponent<PluginsFloatDestChipMeta>();
            meta.background = bg;
            meta.label = t;
            meta.isPerson = false;
            return go;
        }

        private static float EstimatePluginsFloatChipWidth(string label, float s)
        {
            int n = string.IsNullOrEmpty(label) ? 0 : label.Length;
            return (18f + n * 7.2f) * s;
        }

        private void DestroyPluginsFloatDestBar()
        {
            _pluginsFloatDestHost = null;
            _pluginsFloatDestContentRT = null;
            _pluginsFloatSessionDestBtn = null;
            _pluginsFloatSessionDestBg = null;
            _pluginsFloatPersonDestPool.Clear();
            _pluginsFloatPersonDestUids.Clear();
            _pluginsFloatDropBridge = null;
            _pluginsFloatDropHoverKind = PluginsFloatDestKind.None;
            _pluginsFloatDropHoverPersonUid = null;
        }

        private void RefreshPluginsFloatDestButtons()
        {
            if (_pluginsFloatDestContentRT == null) return;
            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : ChromeScale;
            if (s <= 0.01f) s = 1f;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Caption > 0 ? type.Caption : type.Body;

            var persons = new List<Atom>(8);
            try
            {
                if (SuperController.singleton != null)
                {
                    foreach (Atom a in SuperController.singleton.GetAtoms())
                    {
                        if (a == null) continue;
                        if (!SceneUtils.IsPersonLikeAtom(a)) continue;
                        persons.Add(a);
                    }
                }
            }
            catch { }

            // Stable order by uid for muscle memory.
            persons.Sort((x, y) =>
            {
                string xu = "";
                string yu = "";
                try { xu = x.uid ?? ""; } catch { }
                try { yu = y.uid ?? ""; } catch { }
                return string.Compare(xu, yu, StringComparison.OrdinalIgnoreCase);
            });

            EnsurePluginsFloatPersonDestPool(persons.Count, s, font, chromeSz);

            _pluginsFloatPersonDestUids.Clear();
            for (int i = 0; i < _pluginsFloatPersonDestPool.Count; i++)
            {
                GameObject go = _pluginsFloatPersonDestPool[i];
                if (go == null) continue;
                if (i >= persons.Count)
                {
                    go.SetActive(false);
                    continue;
                }

                Atom person = persons[i];
                string uid = null;
                string display = null;
                try { uid = person.uid; } catch { uid = null; }
                try { display = person.name; } catch { display = null; }
                if (string.IsNullOrEmpty(display)) display = uid ?? "Person";
                _pluginsFloatPersonDestUids.Add(uid ?? "");

                go.SetActive(true);
                PluginsFloatDestChipMeta meta = go.GetComponent<PluginsFloatDestChipMeta>();
                if (meta != null)
                {
                    meta.isPerson = true;
                    meta.personUid = uid;
                    if (meta.label != null) meta.label.text = display;
                    LayoutElement le = go.GetComponent<LayoutElement>();
                    if (le != null)
                    {
                        float prefer = Mathf.Clamp(EstimatePluginsFloatChipWidth(display, s), 64f * s, 120f * s);
                        le.preferredWidth = prefer;
                        le.minWidth = 56f * s;
                        le.minHeight = le.preferredHeight = chromeSz;
                        le.flexibleHeight = 0f;
                    }
                }

                Button btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    string captureUid = uid;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OpenPluginsFloatPersonUi(captureUid));
                }
                AddTooltipPlain(go,
                    VPBTranslation.T("gallery.tooltip.plugins_dest_person", "Click: open Person Plugins. Drop: load onto this person.")
                    + "\n" + display);
            }

            ApplyPluginsFloatDestHoverVisuals();
        }

        private void EnsurePluginsFloatPersonDestPool(int needed, float s, int font, float height)
        {
            if (_pluginsFloatDestContentRT == null) return;
            while (_pluginsFloatPersonDestPool.Count < needed)
            {
                GameObject go = CreatePluginsFloatDestChip(
                    _pluginsFloatDestContentRT, s, font, height, "Person", "PersonDest",
                    GalleryUiColorTokens.SurfaceMid, null);
                if (go == null) break;
                _pluginsFloatPersonDestPool.Add(go);
            }
        }

        /// <summary>
        /// VaM Plugins UI / selection links need Edit mode; Play blocks the path.
        /// Setter runs SyncGameMode (+ optional auto-freeze).
        /// </summary>
        private void EnsurePluginsFloatEditMode()
        {
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                if (sc.gameMode != SuperController.GameMode.Edit)
                    sc.gameMode = SuperController.GameMode.Edit;
            }
            catch { }
        }

        /// <summary>
        /// Open VaM main HUD on correct anchor: controller menu in VR, monitor when
        /// MonitorRigActive / desktop. Never ShowMainHUDMonitor — that pins left monitor in VR.
        /// </summary>
        private void ShowPluginsFloatVamMainHud()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try { sc.ShowMainHUDAuto(); }
            catch
            {
                // Fallback if Auto missing on older builds.
                try
                {
                    bool forceMonitor = false;
                    try { forceMonitor = sc.IsMonitorOnly || sc.IsMonitorRigActive; } catch { }
                    sc.ShowMainHUD(true, forceMonitor);
                }
                catch { }
            }
        }

        private void OpenPluginsFloatSessionUi()
        {
            try
            {
                if (SuperController.singleton == null) return;
                EnsurePluginsFloatEditMode();
                ShowPluginsFloatVamMainHud();
                SuperController.singleton.activeUI = SuperController.ActiveUI.MainMenu;
                if (SuperController.singleton.mainMenuTabSelector != null)
                    SuperController.singleton.mainMenuTabSelector.SetActiveTab("TabSessionPlugins");
                else
                {
                    try { SuperController.singleton.SetMainMenuTab("TabSessionPlugins"); }
                    catch { }
                }
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.plugins.opened_session_ui", "Opened Session Plugins"),
                    1.4f);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.PluginsFloat] open session UI failed: " + ex.Message); } catch { }
            }
        }

        private void OpenPluginsFloatPersonUi(string personUid)
        {
            if (string.IsNullOrEmpty(personUid)) return;
            try
            {
                if (SuperController.singleton == null) return;
                Atom atom = SuperController.singleton.GetAtomByUid(personUid);
                if (atom == null || !SceneUtils.IsPersonLikeAtom(atom))
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.plugins.person_missing", "Person not found."),
                        1.8f);
                    RefreshPluginsFloatDestButtons();
                    return;
                }
                EnsurePluginsFloatEditMode();
                if (atom.mainController != null)
                    SuperController.singleton.SelectController(atom.mainController, false, false, true);
                ShowPluginsFloatVamMainHud();
                StartCoroutine(PluginsFloatWaitOpenPersonPluginsTab(atom));
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.PluginsFloat] open person UI failed: " + ex.Message); } catch { }
            }
        }

        private IEnumerator PluginsFloatWaitOpenPersonPluginsTab(Atom atom)
        {
            float deadline = Time.unscaledTime + 1f;
            while (Time.unscaledTime < deadline && atom != null)
            {
                yield return null;
                UITabSelector selector = null;
                try { selector = atom.gameObject.GetComponentInChildren<UITabSelector>(true); }
                catch { selector = null; }
                if (selector == null) continue;
                try { selector.SetActiveTab("Plugins"); } catch { }
                try
                {
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("gallery.plugins.opened_person_ui", "Opened Plugins · {0}"),
                            atom.name ?? atom.uid),
                        1.4f);
                }
                catch { }
                yield break;
            }
        }

        private PluginsFloatDestKind ProbePluginsFloatDest(
            PointerEventData eventData, out string personUid, out string personLabel)
        {
            personUid = null;
            personLabel = null;
            // Header chips stay usable while collapsed (footer tree chrome hidden).
            if (!IsPluginsFloatOpen() || eventData == null)
                return PluginsFloatDestKind.None;

            Camera cam = eventData.pressEventCamera;
            if (cam == null) cam = eventData.enterEventCamera;
            Vector2 screen = eventData.position;

            // Person chips first (more specific).
            for (int i = 0; i < _pluginsFloatPersonDestPool.Count; i++)
            {
                GameObject go = _pluginsFloatPersonDestPool[i];
                if (go == null || !go.activeInHierarchy) continue;
                RectTransform rt = go.transform as RectTransform;
                if (rt == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam))
                    continue;
                PluginsFloatDestChipMeta meta = go.GetComponent<PluginsFloatDestChipMeta>();
                if (meta == null || string.IsNullOrEmpty(meta.personUid)) continue;
                personUid = meta.personUid;
                personLabel = meta.label != null ? meta.label.text : meta.personUid;
                return PluginsFloatDestKind.Person;
            }

            if (_pluginsFloatSessionDestBtn != null)
            {
                RectTransform rt = _pluginsFloatSessionDestBtn.transform as RectTransform;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam))
                    return PluginsFloatDestKind.Session;
            }

            return PluginsFloatDestKind.None;
        }

        internal string DescribePluginsFloatSessionDrop(PointerEventData eventData, string pluginName)
        {
            bool replaceUnused;
            return DescribePluginsFloatSessionDrop(eventData, pluginName, out replaceUnused);
        }

        /// <summary>Ghost/status while over header Session / Person chips. out = person chip.</summary>
        internal string DescribePluginsFloatSessionDrop(
            PointerEventData eventData, string pluginName, out bool isPersonChip)
        {
            isPersonChip = false;
            string personUid;
            string personLabel;
            PluginsFloatDestKind kind = ProbePluginsFloatDest(eventData, out personUid, out personLabel);
            if (kind == PluginsFloatDestKind.Session)
                return "Add " + pluginName + " · session";
            if (kind == PluginsFloatDestKind.Person)
            {
                isPersonChip = true;
                string who = !string.IsNullOrEmpty(personLabel) ? personLabel : personUid;
                return "Adding " + pluginName + " to " + who;
            }
            return null;
        }

        internal void SetPluginsFloatSessionDropHover(PointerEventData eventData)
        {
            string personUid;
            string personLabel;
            PluginsFloatDestKind kind = ProbePluginsFloatDest(eventData, out personUid, out personLabel);
            if (kind == _pluginsFloatDropHoverKind
                && string.Equals(_pluginsFloatDropHoverPersonUid, personUid, StringComparison.OrdinalIgnoreCase))
                return;
            _pluginsFloatDropHoverKind = kind;
            _pluginsFloatDropHoverPersonUid = personUid;
            ApplyPluginsFloatDestHoverVisuals();
        }

        internal void ClearPluginsFloatSessionDropHover()
        {
            if (_pluginsFloatDropHoverKind == PluginsFloatDestKind.None
                && string.IsNullOrEmpty(_pluginsFloatDropHoverPersonUid))
                return;
            _pluginsFloatDropHoverKind = PluginsFloatDestKind.None;
            _pluginsFloatDropHoverPersonUid = null;
            ApplyPluginsFloatDestHoverVisuals();
        }

        private void ApplyPluginsFloatDestHoverVisuals()
        {
            if (_pluginsFloatSessionDestBg != null)
            {
                bool hot = _pluginsFloatDropHoverKind == PluginsFloatDestKind.Session;
                _pluginsFloatSessionDestBg.color = hot
                    ? Color.Lerp(GalleryUiColorTokens.ActiveOn, Color.white, 0.28f)
                    : GalleryUiColorTokens.ActiveOn;
            }

            for (int i = 0; i < _pluginsFloatPersonDestPool.Count; i++)
            {
                GameObject go = _pluginsFloatPersonDestPool[i];
                if (go == null || !go.activeSelf) continue;
                PluginsFloatDestChipMeta meta = go.GetComponent<PluginsFloatDestChipMeta>();
                if (meta == null || meta.background == null) continue;
                bool hot = _pluginsFloatDropHoverKind == PluginsFloatDestKind.Person
                    && !string.IsNullOrEmpty(_pluginsFloatDropHoverPersonUid)
                    && string.Equals(meta.personUid, _pluginsFloatDropHoverPersonUid, StringComparison.OrdinalIgnoreCase);
                meta.background.color = hot
                    ? Color.Lerp(GalleryUiColorTokens.AccentSelected, Color.white, 0.2f)
                    : GalleryUiColorTokens.SurfaceMid;
            }
        }

        /// <summary>Header Session/Person chip drop. True = handled.</summary>
        internal bool TryConsumePluginsFloatSessionDrop(PointerEventData eventData, FileEntry entry)
        {
            if (entry == null || eventData == null) return false;
            string personUid;
            string personLabel;
            PluginsFloatDestKind kind = ProbePluginsFloatDest(eventData, out personUid, out personLabel);
            ClearPluginsFloatSessionDropHover();
            if (kind == PluginsFloatDestKind.None) return false;

            UIDraggableItem bridge = GetOrCreatePluginsFloatDropBridge();
            if (bridge == null) return false;
            bridge.FileEntry = entry;
            bridge.Panel = this;

            if (kind == PluginsFloatDestKind.Session)
            {
                bridge.LoadPluginsAsSession();
                return true;
            }

            if (kind == PluginsFloatDestKind.Person && !string.IsNullOrEmpty(personUid))
            {
                Atom atom = null;
                try { atom = SuperController.singleton.GetAtomByUid(personUid); } catch { atom = null; }
                if (atom == null || !SceneUtils.IsPersonLikeAtom(atom))
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.plugins.person_missing", "Person not found."),
                        1.8f);
                    RefreshPluginsFloatDestButtons();
                    return true;
                }
                bridge.LoadPlugins(atom);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Person atoms changed (scene load, add, remove). Refresh Session/Person dest chips
        /// while Plugins float is open — including float opened before any Person existed.
        /// </summary>
        internal void NotifyPluginsFloatSceneTargetsChanged()
        {
            if (!IsPluginsFloatOpen()) return;
            try { RefreshPluginsFloatDestButtons(); } catch { }
        }

        /// <summary>After session-plugin load; dest strip may need a pass.</summary>
        internal void NotifyPluginsFloatSessionPluginsChanged()
        {
            NotifyPluginsFloatSceneTargetsChanged();
        }

        private UIDraggableItem GetOrCreatePluginsFloatDropBridge()
        {
            if (_pluginsFloatDropBridge != null) return _pluginsFloatDropBridge;
            if (_pluginsFloatRoot == null) return null;
            GameObject host = new GameObject("PluginsFloatDropBridge");
            host.transform.SetParent(_pluginsFloatRoot.transform, false);
            host.SetActive(false);
            _pluginsFloatDropBridge = host.AddComponent<UIDraggableItem>();
            _pluginsFloatDropBridge.Panel = this;
            return _pluginsFloatDropBridge;
        }
    }

    internal sealed class PluginsFloatDestChipMeta : MonoBehaviour
    {
        public bool isPerson;
        public string personUid;
        public Image background;
        public Text label;
    }
}
