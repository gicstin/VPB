using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>Two-person pose cast picker. Own canvas so it works with no gallery pane.</summary>
    public static class VpbDualPoseModal
    {
        const int OverlaySortingOrder = 5200;
        const int WorldSortingOrder = GalleryPanel.DockBaseSortingOrder + 1100;
        static readonly Vector2 WorldSizePx = new Vector2(1200f, 800f);
        const float WorldDistanceMeters = 1.1f;

        const float PanelWidthRef = 620f;

        static GameObject _overlayGO;
        static Canvas _overlayCanvas;
        static GameObject _root;
        static GameObject _body;

        static VpbDualPoseFile _file;
        static readonly List<Atom> _people = new List<Atom>(8);
        static Atom _castA;
        static Atom _castB;
        static bool _anchor;
        static bool _remember;

        // Held until scene change so repeat applies don't re-ask.
        static readonly SessionChoice _sessionChoice = new SessionChoice();
        static readonly LocalChoice _localChoice = new LocalChoice();

        sealed class SessionChoice
        {
            public bool Held;
            public bool ByGender;
            public bool Male;
            public int Role;
        }

        sealed class LocalChoice
        {
            public bool Held;
            public bool ByGender;
            /// <summary>By gender: the female's uid. Otherwise: whoever took the first half.</summary>
            public string First;
            public string Second;
        }

        public static bool IsOpen { get { return _root != null; } }

        public static bool HasRememberedChoice
        {
            get { return _sessionChoice.Held || _localChoice.Held; }
        }

        /// <summary>Ask again. Scene change and the settings row call this.</summary>
        public static void ForgetRememberedChoice()
        {
            if (!HasRememberedChoice) return;
            _sessionChoice.Held = false;
            _localChoice.Held = false;
            LogUtil.LogWarning("[VPB] two-person poses will ask who takes which half again.");
        }

        public static void Show(VpbDualPoseFile file, Atom prefer)
        {
            if (file == null) return;

            Close();

            if (TryApplyRemembered(file)) return;

            _file = file;
            _anchor = AnchorPreference;
            _remember = false;

            VpbDualPose.CollectPeople(_people);
            VpbDualPose.SuggestCast(file, _people, prefer, out _castA, out _castB);

            Build();
        }

        /// <summary>Reuse only if gender hint / cast still matches this file and scene.</summary>
        static bool TryApplyRemembered(VpbDualPoseFile file)
        {
            bool anchored = AnchorPreference;

            if (VpbNetAvatarGuard.Active)
            {
                int role;
                if (!TryRememberedRole(file, out role)) return false;
                if (VpbNetDualPoseRelay.MyAvatar() == null) return false;
                if (!VpbNetDualPoseRelay.PeerIsRiding || !VpbNetDualPoseRelay.PeerWouldTakeAHalf) return false;

                string why;
                if (!ApplySessionRole(file, role, anchored, out why))
                {
                    LogUtil.LogWarning("[VPB] two-person pose not applied: "
                        + (why ?? "the other side could not be told"));
                    return false;
                }
                SayApplied(string.Format(
                    VPBTranslation.T("gallery.dual_pose.auto_session",
                        "Two-person pose applied — you are {0}. Not asking again this session."),
                    VpbDualPose.RoleLabel(file, role)));
                return true;
            }

            Atom a, b;
            if (!TryRememberedCast(file, out a, out b)) return false;

            ApplyLocalCast(file, a, b, anchored);
            SayApplied(string.Format(
                VPBTranslation.T("gallery.dual_pose.auto_local",
                    "Two-person pose applied to {0} and {1}. Not asking again this session."),
                a.uid, b.uid));
            return true;
        }

        static bool TryRememberedRole(VpbDualPoseFile file, out int role)
        {
            role = -1;
            if (!_sessionChoice.Held) return false;

            if (_sessionChoice.ByGender)
            {
                if (file.Ambiguous) return false;
                role = file.RoleOfGender(_sessionChoice.Male);
                return role >= 0;
            }

            if (!file.Ambiguous) return false;
            role = _sessionChoice.Role;
            return role == 0 || role == 1;
        }

        static bool TryRememberedCast(VpbDualPoseFile file, out Atom first, out Atom second)
        {
            first = null;
            second = null;
            if (!_localChoice.Held) return false;

            Atom one = FindPerson(_localChoice.First);
            Atom two = FindPerson(_localChoice.Second);
            if (one == null || two == null || one == two) return false;

            if (!_localChoice.ByGender)
            {
                if (!file.Ambiguous) return false;
                first = one;
                second = two;
                return true;
            }

            if (file.Ambiguous) return false;
            int female = file.RoleOfGender(false);
            if (female < 0) return false;

            // "one" is the female.
            if (female == 0) { first = one; second = two; }
            else { first = two; second = one; }
            return true;
        }

        static Atom FindPerson(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;

            Atom a = null;
            try { a = sc.GetAtomByUid(uid); }
            catch { return null; }
            if (a == null) return null;

            try { return a.on && SceneUtils.IsPersonLikeAtom(a) ? a : null; }
            catch { return null; }
        }

        static void RememberSession(VpbDualPoseFile file, int role)
        {
            _sessionChoice.Held = true;
            _sessionChoice.ByGender = !file.Ambiguous;
            _sessionChoice.Male = !file.Ambiguous && file.Role(role) != null && file.Role(role).IsMale;
            _sessionChoice.Role = role;
        }

        static void RememberLocal(VpbDualPoseFile file, Atom a, Atom b)
        {
            _localChoice.Held = true;
            _localChoice.ByGender = !file.Ambiguous;
            if (!file.Ambiguous)
            {
                // Female-then-male so a reversed file still maps.
                int female = file.RoleOfGender(false);
                _localChoice.First = (female == 0 ? a : b).uid;
                _localChoice.Second = (female == 0 ? b : a).uid;
                return;
            }
            _localChoice.First = a.uid;
            _localChoice.Second = b.uid;
        }

        static void SayApplied(string text)
        {
            LogUtil.LogWarning("[VPB] " + text);
            try { ShowStatus(text); }
            catch { }
        }

        public static void Close()
        {
            if (_root != null)
            {
                try { UnityEngine.Object.Destroy(_root); }
                catch { }
            }
            _root = null;
            _body = null;
            _file = null;
            _castA = null;
            _castB = null;
            _people.Clear();
            if (_overlayGO != null) _overlayGO.SetActive(false);
        }

        /// <summary>Gallery teardown: VR overlay leaves Gallery's transform, so it is freed here.</summary>
        public static void DestroyOverlay()
        {
            Close();
            if (_overlayGO != null)
            {
                try { UnityEngine.Object.Destroy(_overlayGO); }
                catch { }
            }
            _overlayGO = null;
            _overlayCanvas = null;
        }

        static bool AnchorPreference
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    return s != null && s.PoseDualAnchorAtMe != null && s.PoseDualAnchorAtMe.Value;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    Settings s = Settings.Instance;
                    if (s == null || s.PoseDualAnchorAtMe == null) return;
                    if (s.PoseDualAnchorAtMe.Value == value) return;
                    s.PoseDualAnchorAtMe.Value = value;
                    Settings.SaveConfig();
                }
                catch { }
            }
        }

        static GameObject EnsureOverlay()
        {
            if (_overlayGO != null)
            {
                _overlayGO.SetActive(true);
                ApplyRenderMode();
                return _overlayGO;
            }

            Transform parent = null;
            try
            {
                if (Gallery.singleton != null) parent = Gallery.singleton.transform;
                else if (VamHookPlugin.singleton != null) parent = VamHookPlugin.singleton.transform;
            }
            catch { }

            GameObject go = new GameObject("VPB_DualPoseOverlay");
            go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false);

            Canvas c = go.AddComponent<Canvas>();
            c.pixelPerfect = false;
            c.overrideSorting = true;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            _overlayGO = go;
            _overlayCanvas = c;
            // Render mode before VaM binds the canvas.
            ApplyRenderMode();

            go.AddComponent<GraphicRaycaster>();
            try
            {
                if (SuperController.singleton != null) SuperController.singleton.AddCanvas(c);
            }
            catch { }

            go.AddComponent<VpbDualPoseModalTick>();
            return go;
        }

        static void ApplyRenderMode()
        {
            GameObject go = _overlayGO;
            Canvas c = _overlayCanvas;
            if (go == null || c == null) return;

            bool vr = false;
            try { vr = XrUtils.IsVrActive(); }
            catch { vr = false; }

            if (!vr)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = OverlaySortingOrder;
                c.worldCamera = null;
                go.transform.localScale = Vector3.one;
                return;
            }

            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = WorldSortingOrder;

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null && rt.sizeDelta != WorldSizePx)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = WorldSizePx;
            }

            try { if (Camera.main != null) c.worldCamera = Camera.main; }
            catch { }

            int layerBefore = go.layer;
            VpbWorldSpaceUiScale.AttachToPlayerUiSpace(go.transform);
            if (go.layer != layerBefore) SetLayerRecursive(go, go.layer);
        }

        static void PlaceInFrontOfPlayer()
        {
            GameObject go = _overlayGO;
            Canvas c = _overlayCanvas;
            if (go == null || c == null || c.renderMode != RenderMode.WorldSpace) return;

            Transform camTf = null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.centerCameraTarget != null) camTf = sc.centerCameraTarget.transform;
            }
            catch { }
            if (camTf == null && Camera.main != null) camTf = Camera.main.transform;
            if (camTf == null) return;

            Vector3 forward = camTf.forward * WorldDistanceMeters;
            Transform tf = go.transform;
            tf.position = camTf.position + forward;
            tf.rotation = Quaternion.LookRotation(forward, Vector3.up);
            VpbWorldSpaceUiScale.ApplyMetersPerPixelLocalScale(tf);
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        internal static void Tick()
        {
            if (_root == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        static float Scale
        {
            get
            {
                try
                {
                    float s = GalleryUiMetrics.Resolve(true).ChromeScale;
                    if (s > 0.01f) return s;
                }
                catch { }
                return 1f;
            }
        }

        static void Build()
        {
            GameObject host = EnsureOverlay();
            if (host == null)
            {
                LogUtil.LogWarning("[VPB] two-person pose: no surface to ask on, so nothing was applied.");
                _file = null;
                return;
            }

            float s = Scale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;

            _root = UI.CreateChildRT(host, "VPB_DualPoseModal", AnchorPresets.stretchAll);
            SetLayerRecursive(_root, host.layer);

            UI.CreateDimBlocker(_root, "Dim", Close);

            GameObject panel = UI.CreateChildRT(
                _root, "Panel", AnchorPresets.middleCenter,
                new Vector2(PanelWidthRef * s, 0f), Vector2.zero);
            UI.AddImage(panel, new Color(0.07f, 0.08f, 0.10f, 1f));
            UI.AddVLG(panel, spacing: UI.GapControl(s), padding: UI.PadDialog(s));
            ContentSizeFitter fit = panel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text title = UI.CreateEmphasisTitleLabel(
                panel,
                VPBTranslation.T("gallery.dual_pose.title", "Two-person pose"),
                type.Title);
            GalleryUiMetrics.ApplyFont(title, GalleryUiDesignTokens.FontTitleRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(title.gameObject, flexibleWidth: 1f, preferredHeight: GalleryUiDesignTokens.ButtonSizeRef * s);

            Text name = UI.CreateLabel(
                panel, _file.DisplayName, font, GalleryUiColorTokens.TextDim,
                TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, name: "PoseName");
            GalleryUiMetrics.ApplyFont(name, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(name.gameObject, flexibleWidth: 1f, preferredHeight: GalleryUiDesignTokens.ButtonSizeRef * s);

            _body = UI.CreateChildRT(panel, "Body", AnchorPresets.stretchAll);
            UI.AddVLG(_body, spacing: UI.GapControl(s), padding: UI.Pad(0, 0, 0, 0));

            RebuildBody();
            PlaceInFrontOfPlayer();
        }

        static void RebuildBody()
        {
            if (_body == null || _file == null) return;

            // Detach first — Destroy is end-of-frame, doomed row still lays out.
            Transform parent = _body.transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject old = parent.GetChild(i).gameObject;
                try
                {
                    old.transform.SetParent(null, false);
                    UnityEngine.Object.Destroy(old);
                }
                catch { }
            }

            float s = Scale;
            int font = new GalleryModalTypography(s).Body;
            float btnH = GalleryUiDesignTokens.ButtonSizeRef * s;

            if (VpbNetAvatarGuard.Active) BuildSessionBody(s, font, btnH);
            else BuildLocalBody(s, font, btnH);
        }

        static void BuildSessionBody(float s, int font, float btnH)
        {
            Atom mine = VpbNetDualPoseRelay.MyAvatar();
            string peer = VpbNetDualPoseRelay.PeerLabel();

            if (mine == null)
            {
                Hint(s, font, VPBTranslation.T("gallery.dual_pose.spectating",
                    "You are spectating, so there is no body of yours to pose. Claim an avatar on the session panel first."));
                Footer(s, font, btnH, null, null);
                return;
            }

            if (!VpbNetDualPoseRelay.PeerIsRiding)
            {
                Hint(s, font, string.Format(
                    VPBTranslation.T("gallery.dual_pose.peer_spectating",
                        "{0} is not riding an avatar, so only your half of this pose can be applied. Their half will not happen anywhere."),
                    peer));
                AnchorToggle(s, font, btnH, VPBTranslation.T("gallery.dual_pose.anchor_me", "Put the pose where I am standing"));
                Footer(s, font, btnH,
                    VPBTranslation.T("gallery.dual_pose.apply_mine", "Apply my half only"),
                    ApplyMineOnly);
                return;
            }

            if (!VpbNetDualPoseRelay.PeerWouldTakeAHalf)
            {
                Hint(s, font, VPBTranslation.T("gallery.dual_pose.sync_off",
                    "Their session rules do not let you start a two-person pose on them, so only your half can be applied."));
                AnchorToggle(s, font, btnH, VPBTranslation.T("gallery.dual_pose.anchor_me", "Put the pose where I am standing"));
                Footer(s, font, btnH,
                    VPBTranslation.T("gallery.dual_pose.apply_mine", "Apply my half only"),
                    ApplyMineOnly);
                return;
            }

            Hint(s, font, VPBTranslation.T("gallery.dual_pose.which_one",
                "This pose is made for two. Pick the one you want to be — the other half goes to them."));

            // Gender match is a hint, not a lock — still show both role cards.
            int suits = -1;
            if (!_file.Ambiguous)
            {
                bool male = false;
                try { male = AtomGenderUtils.IsMale(mine); }
                catch { }
                suits = _file.RoleOfGender(male);
            }

            GameObject row = UI.CreateChildRT(_body, "Roles", AnchorPresets.stretchAll);
            UI.AddHLG(row, spacing: UI.GapControl(s), padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: true);
            float cardH = btnH * 3f;
            UI.AddLE(row, minHeight: cardH, preferredHeight: cardH);

            AddRoleCard(row.transform, 0, s, font, cardH, peer, suits);
            AddRoleCard(row.transform, 1, s, font, cardH, peer, suits);

            AnchorToggle(s, font, btnH, VPBTranslation.T("gallery.dual_pose.anchor_me", "Put the pose where I am standing"));
            RememberToggle(s, font, btnH);
            Footer(s, font, btnH, null, null);
        }

        static void AddRoleCard(Transform parent, int role, float s, int font, float height, string peer, int suits)
        {
            int theirs = role == 0 ? 1 : 0;
            string label = string.Format(
                VPBTranslation.T("gallery.dual_pose.card", "I am {0}\n{1} is {2}"),
                VpbDualPose.RoleLabel(_file, role),
                peer,
                VpbDualPose.RoleLabel(_file, theirs));
            label += "\n" + (role == suits
                ? VPBTranslation.T("gallery.dual_pose.suits", "suits your avatar")
                : " ");

            int captured = role;
            GameObject go = UI.CreateChromeLayoutButton(
                parent, 0f, height, label, font,
                role == 0 ? new Color(0.24f, 0.40f, 0.54f, 1f) : new Color(0.22f, 0.48f, 0.34f, 1f),
                () => ConfirmSession(captured));
            UI.AddLE(go, flexibleWidth: 1f, minHeight: height, preferredHeight: height);

            Text t = go.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }
        }

        static void ConfirmSession(int myRole)
        {
            VpbDualPoseFile file = _file;
            if (file == null) return;

            bool remember = _remember;
            bool anchored = _anchor;

            string why;
            if (!ApplySessionRole(file, myRole, anchored, out why))
            {
                LogUtil.LogWarning("[VPB] two-person pose not applied: " + (why ?? "the other side could not be told"));
                try
                {
                    ShowStatus(string.Format(
                        VPBTranslation.T("gallery.dual_pose.not_sent",
                            "Two-person pose not applied — {0}."),
                        why ?? "the other side could not be told"));
                }
                catch { }
                Close();
                return;
            }

            if (remember) RememberSession(file, myRole);
            Close();
        }

        /// <summary>Send peer half first; if that fails, apply nothing locally.</summary>
        static bool ApplySessionRole(VpbDualPoseFile file, int myRole, bool anchored, out string why)
        {
            why = null;

            Atom mine = VpbNetDualPoseRelay.MyAvatar();
            if (mine == null)
            {
                why = "you are no longer riding an avatar";
                return false;
            }

            VpbDualPose.Anchor anchor = anchored
                ? VpbDualPose.AnchorAt(file, myRole, mine)
                : VpbDualPose.Anchor.None;

            if (!VpbNetDualPoseRelay.Send(file, myRole, anchor, out why)) return false;

            VpbDualPose.Apply(mine, file.Role(myRole), anchor, "you applied a two-person pose");
            try { VpbNetPresence.MarkLocalPoseJump(); }
            catch { }
            return true;
        }

        static void ApplyMineOnly()
        {
            VpbDualPoseFile file = _file;
            if (file == null) return;

            Atom mine = VpbNetDualPoseRelay.MyAvatar();
            if (mine == null)
            {
                Close();
                return;
            }

            int role = 0;
            if (!file.Ambiguous)
            {
                bool male = false;
                try { male = AtomGenderUtils.IsMale(mine); }
                catch { }
                int wanted = file.RoleOfGender(male);
                if (wanted >= 0) role = wanted;
            }

            VpbDualPose.Anchor anchor = _anchor
                ? VpbDualPose.AnchorAt(file, role, mine)
                : VpbDualPose.Anchor.None;

            VpbDualPose.Apply(mine, file.Role(role), anchor, "you applied half a two-person pose");
            try { VpbNetPresence.MarkLocalPoseJump(); }
            catch { }
            Close();
        }

        static void BuildLocalBody(float s, int font, float btnH)
        {
            if (_people.Count < 2)
            {
                Hint(s, font, VPBTranslation.T("gallery.dual_pose.need_two",
                    "This pose needs two people in the scene. Add a second Person and try again."));
                Footer(s, font, btnH, null, null);
                return;
            }

            Hint(s, font, VPBTranslation.T("gallery.dual_pose.cast",
                "This pose is made for two. Pick who takes each half — two people, one each."));

            AddCastRow(s, font, btnH, 0);
            AddCastRow(s, font, btnH, 1);

            GameObject swapRow = UI.CreateChildRT(_body, "SwapRow", AnchorPresets.stretchAll);
            UI.AddHLG(swapRow, spacing: UI.GapControl(s), padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: false);
            UI.AddLE(swapRow, minHeight: btnH, preferredHeight: btnH);
            GameObject swap = UI.CreateChromeLayoutButton(
                swapRow.transform, 140f * s, btnH,
                VPBTranslation.T("gallery.dual_pose.swap", "Swap the two"), font,
                new Color(0.28f, 0.28f, 0.32f, 1f), SwapCast);
            ApplyButtonFont(swap, s);

            string anchorName = _castA != null ? _castA.uid : "the first";
            AnchorToggle(s, font, btnH, string.Format(
                VPBTranslation.T("gallery.dual_pose.anchor_local", "Put the pose where {0} is standing"),
                anchorName));
            RememberToggle(s, font, btnH);

            Footer(s, font, btnH,
                VPBTranslation.T("gallery.dual_pose.apply", "Apply to both"),
                ConfirmLocal);
        }

        static void AddCastRow(float s, int font, float btnH, int role)
        {
            GameObject row = UI.CreateChildRT(_body, "Cast" + role, AnchorPresets.stretchAll);
            UI.AddHLG(row, spacing: UI.GapControl(s), padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: false);
            UI.AddLE(row, minHeight: btnH, preferredHeight: btnH);

            Text label = UI.CreateLabel(
                row, VpbDualPose.RoleLabel(_file, role), font, Color.white,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Wrap, name: "RoleLabel");
            GalleryUiMetrics.ApplyFont(label, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(label.gameObject, minWidth: 130f * s, preferredWidth: 130f * s);

            Atom cast = role == 0 ? _castA : _castB;
            string who = cast != null
                ? cast.uid
                : VPBTranslation.T("gallery.dual_pose.nobody", "nobody yet");

            int captured = role;
            GameObject btn = UI.CreateChromeLayoutButton(
                row.transform, 0f, btnH, who, font,
                new Color(0.22f, 0.26f, 0.32f, 1f), () => CycleCast(captured));
            UI.AddLE(btn, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);
            ApplyButtonFont(btn, s);
        }

        static void ApplyButtonFont(GameObject go, float s)
        {
            Text t = go != null ? go.GetComponentInChildren<Text>() : null;
            if (t != null)
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
        }

        /// <summary>Cycle one half to the next person, never onto the other half's.</summary>
        static void CycleCast(int role)
        {
            if (_people.Count == 0) return;

            Atom current = role == 0 ? _castA : _castB;
            Atom other = role == 0 ? _castB : _castA;

            int start = _people.IndexOf(current);
            for (int step = 1; step <= _people.Count; step++)
            {
                Atom next = _people[(start + step + _people.Count) % _people.Count];
                if (next == null || next == other) continue;
                if (role == 0) _castA = next;
                else _castB = next;
                RebuildBody();
                return;
            }
        }

        static void SwapCast()
        {
            Atom t = _castA;
            _castA = _castB;
            _castB = t;
            RebuildBody();
        }

        static void ConfirmLocal()
        {
            VpbDualPoseFile file = _file;
            if (file == null) return;

            if (_castA == null || _castB == null || _castA == _castB)
            {
                LogUtil.LogWarning("[VPB] two-person pose: pick two different people first.");
                return;
            }

            Atom a = _castA;
            Atom b = _castB;
            bool anchored = _anchor;
            bool remember = _remember;
            Close();

            ApplyLocalCast(file, a, b, anchored);
            if (remember) RememberLocal(file, a, b);
        }

        static void ApplyLocalCast(VpbDualPoseFile file, Atom a, Atom b, bool anchored)
        {
            // One transform from first half; per-body anchors pull the pose apart.
            VpbDualPose.Anchor anchor = anchored
                ? VpbDualPose.AnchorAt(file, 0, a)
                : VpbDualPose.Anchor.None;

            VpbDualPose.Apply(a, file.A, anchor, "a two-person pose was applied");
            VpbDualPose.Apply(b, file.B, anchor, "a two-person pose was applied");
        }

        static void Hint(float s, int font, string text)
        {
            Text t = UI.CreateLabel(
                _body, text, font, new Color(0.75f, 0.75f, 0.78f, 1f),
                TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, name: "Hint");
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            // Height from wrapped Text; no preferredHeight of our own.
            UI.AddLE(t.gameObject, flexibleWidth: 1f, minHeight: GalleryUiDesignTokens.ButtonSizeRef * s);
        }

        static void AnchorToggle(float s, int font, float btnH, string label)
        {
            GameObject row = UI.CreateChildRT(_body, "AnchorRow", AnchorPresets.stretchAll);
            UI.AddLE(row, minHeight: btnH, preferredHeight: btnH);

            GameObject toggleGo = UI.CreateUIToggle(
                row, 0, btnH, label, font, 0, 0, AnchorPresets.stretchAll, OnAnchorChanged);
            Toggle toggle = toggleGo.GetComponent<Toggle>();
            if (toggle != null) toggle.isOn = _anchor;
            ApplyButtonFont(toggleGo, s);
            UI.AddLE(toggleGo, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);
        }

        static void OnAnchorChanged(bool on)
        {
            if (_anchor == on) return;
            _anchor = on;
            AnchorPreference = on;
        }

        /// <summary>Reuse this answer for later poses this session; not persisted.</summary>
        static void RememberToggle(float s, int font, float btnH)
        {
            GameObject row = UI.CreateChildRT(_body, "RememberRow", AnchorPresets.stretchAll);
            UI.AddLE(row, minHeight: btnH, preferredHeight: btnH);

            GameObject toggleGo = UI.CreateUIToggle(
                row, 0, btnH,
                VPBTranslation.T("gallery.dual_pose.remember", "Don't ask again this session"),
                font, 0, 0, AnchorPresets.stretchAll, OnRememberChanged);
            Toggle toggle = toggleGo.GetComponent<Toggle>();
            if (toggle != null) toggle.isOn = _remember;
            ApplyButtonFont(toggleGo, s);
            UI.AddLE(toggleGo, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);
        }

        static void OnRememberChanged(bool on)
        {
            _remember = on;
        }

        static void Footer(float s, int font, float btnH, string confirmLabel, UnityAction confirm)
        {
            GameObject footer = UI.CreateChildRT(_body, "Footer", AnchorPresets.stretchAll);
            UI.AddHLG(footer, spacing: UI.GapGroup(s), padding: UI.Pad(0, 0, 0, 0), childForceExpandWidth: true);
            UI.AddLE(footer, minHeight: btnH, preferredHeight: btnH);

            GameObject cancel = UI.CreateChromeLayoutButton(
                footer.transform, 0f, btnH,
                VPBTranslation.T("gallery.dual_pose.cancel", "Cancel"), font,
                new Color(0.35f, 0.28f, 0.22f, 1f), Close);
            UI.AddLE(cancel, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);
            ApplyButtonFont(cancel, s);

            if (confirm == null || string.IsNullOrEmpty(confirmLabel)) return;

            GameObject ok = UI.CreateChromeLayoutButton(
                footer.transform, 0f, btnH, confirmLabel, font,
                new Color(0.22f, 0.52f, 0.30f, 1f), confirm);
            UI.AddLE(ok, flexibleWidth: 1f, minHeight: btnH, preferredHeight: btnH);
            ApplyButtonFont(ok, s);
        }

        static void ShowStatus(string text)
        {
            Gallery g = Gallery.singleton;
            if (g == null || g.Panels == null) return;
            for (int i = 0; i < g.Panels.Count; i++)
            {
                GalleryPanel p = g.Panels[i];
                if (p == null || !p.IsVisible) continue;
                p.ShowTemporaryStatus(text, 5f);
                return;
            }
        }
    }

    internal sealed class VpbDualPoseModalTick : MonoBehaviour
    {
        void Update()
        {
            try { VpbDualPoseModal.Tick(); }
            catch { }
        }
    }
}
