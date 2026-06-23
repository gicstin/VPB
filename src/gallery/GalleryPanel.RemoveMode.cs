using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    // Remove Item Mode: a hover-to-remove tool. While active, the item the user points at in the
    // 3D scene is faded to half opacity and a "Click to remove X" hint is shown; clicking removes it.
    // Clothing/hair worn on a Person are removed via the character selector (they are not atoms);
    // everything else (CUA, lights, mirrors, persons, ...) is removed as a whole atom. Desktop uses
    // the monitor camera + mouse; VR uses the controller pointer. Clicking the rail button again
    // exits; so does Esc / cancel. Every removal pushes an Undo (the gallery's existing Undo button
    // reverts it).
    //
    // Picking is render-accurate: VaM clothing/hair render through Graphics.DrawMesh and expose no
    // Renderer or usable bounds, so we raycast the actual garment mesh triangles (see
    // TryRaycastGarmentMesh) to find exactly the garment drawn under the cursor. Pointing at bare
    // skin pierces no garment and a click removes the owning Person atom instead.
    public partial class GalleryPanel
    {
        private enum RemoveTargetKind { None, ClothingItem, HairItem, Atom }

        private sealed class RemoveTarget
        {
            public RemoveTargetKind kind;
            public DAZDynamicItem item;            // clothing / hair sub-item
            public DAZCharacterSelector selector;  // owner of the sub-item
            public Atom atom;                      // whole-atom target
            public GameObject highlightRoot;       // subtree to fade
            public UnityEngine.Object identity;    // item or atom, for change detection

            public string DisplayName()
            {
                try
                {
                    if (kind == RemoveTargetKind.ClothingItem || kind == RemoveTargetKind.HairItem)
                    {
                        if (item != null)
                        {
                            if (!string.IsNullOrEmpty(item.displayName)) return item.displayName;
                            if (!string.IsNullOrEmpty(item.uid)) return item.uid;
                        }
                        return "item";
                    }
                    if (kind == RemoveTargetKind.Atom && atom != null)
                        return atom.uid + " (" + atom.type + ")";
                }
                catch { }
                return "item";
            }

            // Richer label for the floating pointer popup: "Remove <type>: <name>".
            public string PopupLabel()
            {
                try
                {
                    if (kind == RemoveTargetKind.ClothingItem) return "Remove clothing: " + DisplayName();
                    if (kind == RemoveTargetKind.HairItem) return "Remove hair: " + DisplayName();
                    if (kind == RemoveTargetKind.Atom && atom != null) return "Remove " + atom.type + ": " + atom.uid;
                }
                catch { }
                return "Remove " + DisplayName();
            }
        }

        // Reversible half-transparency highlight. VaM skin/clothing/hair fade through the
        // MaterialOptions "Alpha Adjust" storable (the proper _AlphaAdjust knob, range -1..1);
        // generic props with real Renderers fade through a per-renderer MaterialPropertyBlock.
        // Both are restored exactly on clear, so nothing is mutated permanently.
        private sealed class RemoveModeHighlight
        {
            private static readonly int AlphaAdjustId = Shader.PropertyToID("_AlphaAdjust");

            private readonly List<JSONStorableFloat> _floats = new List<JSONStorableFloat>();
            private readonly List<float> _floatSaved = new List<float>();
            private readonly List<Renderer> _renderers = new List<Renderer>();
            private readonly List<MaterialPropertyBlock> _blockSaved = new List<MaterialPropertyBlock>();

            public void Apply(GameObject root, float alphaAdjust, bool includeMaterialOptions)
            {
                Clear();
                if (root == null) return;

                MaterialOptions[] mos = null;
                // MaterialOptions fade is only used for individual clothing/hair items (scoped to
                // that item's subtree). It is intentionally skipped for whole-atom targets so that
                // hovering a Person does NOT mass-fade every garment it wears.
                if (includeMaterialOptions)
                {
                    try { mos = root.GetComponentsInChildren<MaterialOptions>(false); } catch { mos = null; }
                }
                if (mos != null)
                {
                    for (int i = 0; i < mos.Length; i++)
                    {
                        MaterialOptions mo = mos[i];
                        if (mo == null) continue;
                        JSONStorableFloat f = null;
                        try { f = mo.GetFloatJSONParam("Alpha Adjust"); } catch { }
                        if (f == null) continue;
                        _floats.Add(f);
                        _floatSaved.Add(f.val);
                        try { f.val = alphaAdjust; } catch { }
                    }
                }

                Renderer[] rs;
                try { rs = root.GetComponentsInChildren<Renderer>(false); } catch { rs = null; }
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        Renderer r = rs[i];
                        if (r == null || !r.enabled) continue;
                        if (!r.gameObject.activeInHierarchy) continue;
                        MaterialPropertyBlock saved = new MaterialPropertyBlock();
                        try { r.GetPropertyBlock(saved); } catch { continue; }
                        MaterialPropertyBlock mod = new MaterialPropertyBlock();
                        try { r.GetPropertyBlock(mod); } catch { }
                        try { mod.SetFloat(AlphaAdjustId, alphaAdjust); } catch { }
                        try { r.SetPropertyBlock(mod); } catch { continue; }
                        _renderers.Add(r);
                        _blockSaved.Add(saved);
                    }
                }
            }

            public void Clear()
            {
                for (int i = 0; i < _floats.Count; i++)
                {
                    JSONStorableFloat f = _floats[i];
                    if (f == null) continue;
                    try { f.val = _floatSaved[i]; } catch { }
                }
                _floats.Clear();
                _floatSaved.Clear();

                for (int i = 0; i < _renderers.Count; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null) continue;
                    try { r.SetPropertyBlock(_blockSaved[i]); } catch { }
                }
                _renderers.Clear();
                _blockSaved.Clear();
            }
        }

        // Negative = more transparent on VaM shaders. -0.5 reads as roughly half opacity.
        private const float RemoveModeAlphaAdjust = -0.5f;

        // When the nearest surface is bare skin but a clothing/hair collider sits within this
        // distance behind it, prefer removing the clothing (matches "remove the item, not the
        // person" intent for garments that hug the body).
        private const float RemoveModeItemBias = 0.12f;

        // Only the owner panel runs the per-frame scene logic, so left/right rails of the same
        // instance (or any stray duplicate) never double-process.
        private static GalleryPanel _removeModeOwner;

        private bool _removeModeActive;
        private RemoveModeHighlight _removeHighlight;
        private UnityEngine.Object _removeHighlightedIdentity;

        // Baked world-space geometry for one garment wrap, captured once per remove-mode session.
        // Remove mode freezes all animation + physics on enter, so the garment verts are static for
        // the whole session; recomputing them (UpdateVerts), re-reading them, and rebuilding their
        // AABB every frame was the dominant per-frame cost. We bake them once and only re-run the
        // (ray-dependent) triangle intersection each frame.
        private sealed class WrapGeom
        {
            public DAZDynamicItem item;
            public Vector3[] verts;
            public int[] tris;
            public Vector3 min;
            public Vector3 max;
        }

        // Per body atom: its baked garment geometry. Built lazily on first hover and reused every
        // frame while the scene is frozen; cleared on enter/exit and after any removal so a changed
        // garment set is re-baked exactly once.
        private readonly Dictionary<Atom, List<WrapGeom>> _wrapGeomCache = new Dictionary<Atom, List<WrapGeom>>();

        // Side buttons (for square-chrome sizing) + their outline and icon image (recolored by state).
        private GameObject rightRemoveModeSideBtn;
        private GameObject leftRemoveModeSideBtn;
        private Outline rightRemoveModeBtnOutline;
        private Outline leftRemoveModeBtnOutline;
        private Image rightRemoveModeBtnIconImage;
        private Image leftRemoveModeBtnIconImage;

        // Rail button look: red backdrop always; outline + icon glyph go white when idle and magenta
        // when the mode is active.
        private static readonly Color RemoveModeRailBackdrop = new Color(0.62f, 0.16f, 0.16f, 1f);
        private static readonly Color RemoveModeOutlineIdle = Color.white;
        private static readonly Color RemoveModeOutlineActive = new Color(1f, 0.2f, 0.9f, 1f);

        // Remembers the user's freeze-animation toggle so entering remove mode can pause all
        // animation + sound while active and restore the prior state on exit.
        private bool _removePrevFreeze;

        // Tracks whether we currently own the on-screen help text, so we only clear what we set.
        private bool _removeHelpShown;

        // Floating "Remove <type>: <name>" popup that follows the desktop pointer while hovering.
        private GameObject _removePopupGO;
        private RectTransform _removePopupRT;
        private Text _removePopupText;

        internal void ToggleRemoveMode()
        {
            if (_removeModeActive) RemoveModeExit();
            else RemoveModeEnter();
        }

        private void RemoveModeEnter()
        {
            _removeModeActive = true;
            _removeModeOwner = this;
            if (_removeHighlight == null) _removeHighlight = new RemoveModeHighlight();
            _removeHighlightedIdentity = null;
            _wrapGeomCache.Clear();
            RemoveModeFreezeAnimation(true);
            RemoveModeUpdateButtonVisual();
        }

        private void RemoveModeExit()
        {
            _removeModeActive = false;
            if (ReferenceEquals(_removeModeOwner, this)) _removeModeOwner = null;
            _wrapGeomCache.Clear();
            RemoveModeClearHighlight();
            RemoveModeClearHelp();
            RemoveModeHidePopup();
            RemoveModeFreezeAnimation(false);
            RemoveModeUpdateButtonVisual();
        }

        private void RemoveModeUpdateButtonVisual()
        {
            Color c = _removeModeActive ? RemoveModeOutlineActive : RemoveModeOutlineIdle;
            try { if (rightRemoveModeBtnOutline != null) rightRemoveModeBtnOutline.effectColor = c; } catch { }
            try { if (leftRemoveModeBtnOutline != null) leftRemoveModeBtnOutline.effectColor = c; } catch { }
            try { if (rightRemoveModeBtnIconImage != null) rightRemoveModeBtnIconImage.color = c; } catch { }
            try { if (leftRemoveModeBtnIconImage != null) leftRemoveModeBtnIconImage.color = c; } catch { }
        }

        // Pauses all animation + sound while remove mode is active (VaM's built-in freeze, the same
        // one behind the "Pause Animations and Sounds" toggle) and restores the prior state on exit.
        private void RemoveModeFreezeAnimation(bool freeze)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            if (freeze)
            {
                try { _removePrevFreeze = (sc.freezeAnimationToggle != null) ? sc.freezeAnimationToggle.isOn : sc.freezeAnimation; }
                catch { _removePrevFreeze = false; }
                try { sc.SetFreezeAnimation(true); } catch { }
            }
            else
            {
                try { sc.SetFreezeAnimation(_removePrevFreeze); } catch { }
            }
        }

        // Adds/styles the rail button outline (idle white). Called from button construction (Init).
        private Outline RemoveModeAddRailOutline(GameObject btn)
        {
            if (btn == null) return null;
            Outline o = btn.GetComponent<Outline>();
            if (o == null) o = btn.AddComponent<Outline>();
            o.effectColor = RemoveModeOutlineIdle;
            o.effectDistance = new Vector2(2f, -2f);
            o.useGraphicAlpha = false;
            return o;
        }

        // Shows "Click to remove X" on VaM's on-screen help HUD while a target is hovered.
        private void RemoveModeSetHelp(string text)
        {
            try { SuperController.singleton.helpText = text; _removeHelpShown = true; } catch { }
        }

        private void RemoveModeClearHelp()
        {
            if (!_removeHelpShown) return;
            try { SuperController.singleton.helpText = string.Empty; } catch { }
            _removeHelpShown = false;
        }

        // Lazily builds a standalone screen-space-overlay canvas holding the floating pointer popup.
        private void RemoveModeEnsurePopup()
        {
            if (_removePopupGO != null) return;
            try
            {
                GameObject canvasGO = new GameObject("VPB_RemovePopup");
                Canvas c = canvasGO.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 32760; // above the gallery and most HUDs
                canvasGO.AddComponent<CanvasScaler>();

                GameObject panel = new GameObject("Panel");
                panel.transform.SetParent(canvasGO.transform, false);
                Image bg = panel.AddComponent<Image>();
                bg.color = new Color(0.09f, 0.09f, 0.16f, 0.95f);
                bg.raycastTarget = false;
                Outline ol = panel.AddComponent<Outline>();
                ol.effectColor = new Color(0.92f, 0.26f, 0.26f, 0.9f);
                ol.effectDistance = new Vector2(1.5f, -1.5f);
                RectTransform prt = panel.GetComponent<RectTransform>();
                prt.anchorMin = prt.anchorMax = Vector2.zero; // bottom-left origin = Input.mousePosition space
                prt.pivot = new Vector2(0f, 1f);              // top-left corner pinned to the cursor

                GameObject txtGO = new GameObject("Text");
                txtGO.transform.SetParent(panel.transform, false);
                Text txt = txtGO.AddComponent<Text>();
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = 26;
                txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
                RectTransform trt = txtGO.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(12f, 8f);
                trt.offsetMax = new Vector2(-12f, -8f);

                _removePopupGO = canvasGO;
                _removePopupRT = prt;
                _removePopupText = txt;
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] RemoveMode popup create error: " + ex); _removePopupGO = null; }
        }

        // Shows the popup with the given label and positions it next to the desktop cursor (clamped).
        private void RemoveModeShowPopup(string label)
        {
            RemoveModeEnsurePopup();
            if (_removePopupGO == null || _removePopupText == null || _removePopupRT == null) return;
            if (!_removePopupGO.activeSelf) _removePopupGO.SetActive(true);
            if (_removePopupText.text != label) _removePopupText.text = label;

            float w = _removePopupText.preferredWidth + 24f;
            float h = _removePopupText.preferredHeight + 16f;
            _removePopupRT.sizeDelta = new Vector2(w, h);

            Vector3 m = Input.mousePosition;
            float px = m.x + 18f;
            float py = m.y - 18f;
            if (px + w > Screen.width) px = m.x - 18f - w; // flip to the cursor's left near the right edge
            if (px < 0f) px = 0f;
            if (py < h) py = h;                            // keep fully on screen vertically
            if (py > Screen.height) py = Screen.height;
            _removePopupRT.anchoredPosition = new Vector2(px, py);
        }

        private void RemoveModeHidePopup()
        {
            if (_removePopupGO != null && _removePopupGO.activeSelf) _removePopupGO.SetActive(false);
        }

        private void RemoveModeDestroyPopup()
        {
            if (_removePopupGO != null) { try { Destroy(_removePopupGO); } catch { } }
            _removePopupGO = null;
            _removePopupRT = null;
            _removePopupText = null;
        }

        // Per-frame entry point, called from Update() while the gallery canvas is enabled.
        private void RemoveModeUpdate()
        {
            if (!_removeModeActive) return;
            if (!ReferenceEquals(_removeModeOwner, this)) return;
            if (SuperController.singleton == null) return;

            try { if (SuperController.singleton.GetCancel()) { RemoveModeExit(); return; } }
            catch { }

            bool desktop = true;
            try { desktop = SuperController.singleton.IsMonitorOnly; } catch { desktop = true; }

            // Right-click anywhere also ends remove mode (desktop).
            if (desktop)
            {
                bool rdown = false; try { rdown = Input.GetMouseButtonDown(1); } catch { }
                if (rdown) { RemoveModeExit(); return; }
            }

            // Don't fight the gallery UI: when the mouse is over the gallery window on desktop,
            // clear any highlight and skip scene picking so hovering the panel is harmless.
            if (desktop)
            {
                try { if (IsPointerInsideGalleryWindowRect()) { RemoveModeClearHighlight(); RemoveModeClearHelp(); RemoveModeHidePopup(); return; } }
                catch { }
            }

            RemoveTarget target = null;
            bool clickThisFrame = false;
            try { target = RemoveModeResolve(desktop, out clickThisFrame); }
            catch (Exception ex) { LogUtil.LogError("[VPB] RemoveModeResolve error: " + ex); target = null; clickThisFrame = false; }

            // Update the half-transparency highlight + help hint only when the target identity changes.
            UnityEngine.Object newIdentity = target != null ? target.identity : null;
            bool identityChanged = !ReferenceEquals(newIdentity, _removeHighlightedIdentity);
            if (identityChanged)
            {
                RemoveModeClearHighlight();
                if (target != null && target.highlightRoot != null)
                {
                    // MaterialOptions "Alpha Adjust" fade is scoped to a single clothing/hair item's
                    // subtree. For whole-atom (e.g. Person) targets we MUST skip it, otherwise it
                    // mass-fades every garment the person wears instead of just the hovered item.
                    bool fadeMat = target.kind == RemoveTargetKind.ClothingItem
                                   || target.kind == RemoveTargetKind.HairItem;
                    try { _removeHighlight.Apply(target.highlightRoot, RemoveModeAlphaAdjust, fadeMat); } catch { }
                }
                _removeHighlightedIdentity = newIdentity;

                if (target != null) RemoveModeSetHelp("Click to remove " + target.DisplayName());
                else RemoveModeClearHelp();
            }

            // Floating popup follows the desktop pointer every frame while a target is held.
            if (desktop && target != null) RemoveModeShowPopup(target.PopupLabel());
            else RemoveModeHidePopup();

            if (clickThisFrame && target != null)
            {
                // Restore the look before mutating the scene so a removed-but-recreated (undo)
                // item never carries a stale property block.
                RemoveModeClearHighlight();
                RemoveModeClearHelp();
                RemoveModeHidePopup();
                try { RemoveModeExecuteRemoval(target); }
                catch (Exception ex) { LogUtil.LogError("[VPB] RemoveModeExecuteRemoval error: " + ex); }
            }
        }

        // Resolves the best target under the pointer. Desktop: a single ray from the monitor
        // camera. VR: the nearer of the two controller rays. Also reports whether the matching
        // select input fired this frame.
        private RemoveTarget RemoveModeResolve(bool desktop, out bool clickThisFrame)
        {
            clickThisFrame = false;
            SuperController sc = SuperController.singleton;

            if (desktop)
            {
                Camera cam = sc.MonitorCenterCamera != null ? sc.MonitorCenterCamera : Camera.main;
                if (cam == null) return null;
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                RemoveTarget t = ResolveTargetForRay(ray);
                // GetMouseSelect() alone proved unreliable here (clicks never registered), so also
                // accept a raw left-mouse-down. The gallery-window guard above already prevents this
                // from firing while the pointer is over the panel.
                bool sel = false; try { sel = sc.GetMouseSelect(); } catch { }
                bool down = false; try { down = Input.GetMouseButtonDown(0); } catch { }
                clickThisFrame = sel || down;
                return t;
            }

            RemoveTarget best = null;
            float bestScore = float.MaxValue;
            bool bestIsRight = false;

            Transform right = null, left = null;
            try { right = sc.touchObjectRight; } catch { }
            try { left = sc.touchObjectLeft; } catch { }

            if (right != null)
            {
                RemoveTarget tR = ResolveTargetForRay(new Ray(right.position, right.forward), out float sR);
                if (tR != null && sR < bestScore) { best = tR; bestScore = sR; bestIsRight = true; }
            }
            if (left != null)
            {
                RemoveTarget tL = ResolveTargetForRay(new Ray(left.position, left.forward), out float sL);
                if (tL != null && sL < bestScore) { best = tL; bestScore = sL; bestIsRight = false; }
            }

            if (best != null)
            {
                try { clickThisFrame = bestIsRight ? sc.GetRightSelect() : sc.GetLeftSelect(); }
                catch { clickThisFrame = false; }
            }
            return best;
        }

        private RemoveTarget ResolveTargetForRay(Ray ray)
        {
            return ResolveTargetForRay(ray, out _);
        }

        // Core picker. Two signals are combined:
        //  1. Collider hits that resolve (parent walk) to an active DAZDynamicItem — the fast path
        //     for items that own colliders (rare).
        //  2. Garment mesh raycast — VaM clothing/hair render via Graphics.DrawMesh with NO collider,
        //     so a collider hit lands on the body and resolves to the Person. We then raycast the
        //     actual garment mesh triangles (TryRaycastGarmentMesh) to pick the garment drawn under
        //     the cursor; if none is pierced the whole atom (Person) is the target.
        private RemoveTarget ResolveTargetForRay(Ray ray, out float score)
        {
            score = float.MaxValue;

            DAZDynamicItem bestDyn = null;
            float bestDynDist = float.MaxValue;
            Atom bestAtom = null;
            float bestAtomDist = float.MaxValue;

            try
            {
                int mask = ~(1 << 5); // everything except the UI layer
                RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, mask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null) continue;
                    float d = hits[i].distance;

                    DAZDynamicItem dyn = null;
                    try { dyn = col.GetComponentInParent<DAZDynamicItem>(); } catch { }
                    bool dynUsable = false;
                    if (dyn != null)
                    {
                        try { dynUsable = dyn.active && dyn.characterSelector != null; } catch { dynUsable = false; }
                    }

                    if (dynUsable)
                    {
                        if (d < bestDynDist) { bestDynDist = d; bestDyn = dyn; }
                    }
                    else
                    {
                        Atom hitAtom = null;
                        try { hitAtom = col.GetComponentInParent<Atom>(); } catch { }
                        if (hitAtom != null && d < bestAtomDist) { bestAtomDist = d; bestAtom = hitAtom; }
                    }
                }
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] RemoveMode raycast error: " + ex); }

            // Fast path: an item that owns colliders and sits in front of (or close behind) the body.
            if (bestDyn != null && bestDynDist <= bestAtomDist + RemoveModeItemBias)
                return MakeItemTarget(bestDyn, bestDynDist, out score);

            // Body hit: try to resolve which garment the line of sight passes through.
            if (bestAtom != null)
            {
                DAZDynamicItem garment = TryRaycastGarmentMesh(ray, bestAtom);
                if (garment != null)
                    return MakeItemTarget(garment, bestAtomDist, out score);

                score = bestAtomDist;
                return new RemoveTarget
                {
                    kind = RemoveTargetKind.Atom,
                    atom = bestAtom,
                    identity = bestAtom,
                    highlightRoot = bestAtom.gameObject
                };
            }

            return null;
        }

        private RemoveTarget MakeItemTarget(DAZDynamicItem dyn, float dist, out float score)
        {
            DAZCharacterSelector sel = null;
            try { sel = dyn.characterSelector; } catch { }
            score = dist;
            return new RemoveTarget
            {
                kind = (dyn is DAZHairGroup) ? RemoveTargetKind.HairItem : RemoveTargetKind.ClothingItem,
                item = dyn,
                selector = sel,
                identity = dyn,
                highlightRoot = dyn.gameObject
            };
        }

        // Resolves which garment is under the cursor by raycasting the ACTUAL rendered garment
        // geometry. In play mode VaM draws each clothing/hair DAZSkinWrap via Graphics.DrawMesh at an
        // identity matrix (DAZSkinWrap.DrawMesh, ref/vam line 1404), so wrap.Mesh.vertices are the
        // live WORLD-SPACE vertices and the mesh triangles index them directly. We intersect the
        // cursor ray against those triangles (Moller-Trumbore) and pick the garment whose surface the
        // ray pierces FIRST (smallest t) — exactly the garment drawn at that pixel. Position- and
        // depth-correct by construction: pointing at bare skin (head, etc.) pierces no garment, and
        // an outer layer is hit before the inner layer it sits on.
        private DAZDynamicItem TryRaycastGarmentMesh(Ray ray, Atom bodyAtom)
        {
            Vector3 o = ray.origin;
            Vector3 d = ray.direction.normalized;

            List<WrapGeom> geoms = GetGarmentGeom(bodyAtom);
            if (geoms == null || geoms.Count == 0) return null;

            DAZDynamicItem best = null;
            float bestT = float.MaxValue;

            for (int g = 0; g < geoms.Count; g++)
            {
                WrapGeom geom = geoms[g];
                if (geom == null || geom.verts == null || geom.tris == null) continue;

                // A garment removed (or undone) mid-session changes which items are live; skip any
                // baked geometry whose item is no longer active rather than re-bake every frame.
                bool active = false;
                try { active = geom.item != null && geom.item.active; } catch { active = false; }
                if (!active) continue;

                if (!RayHitsAabb(o, d, geom.min, geom.max)) continue;

                if (RayMeshNearest(o, d, geom.verts, geom.tris, out float t) && t < bestT)
                {
                    bestT = t;
                    best = geom.item;
                }
            }

            return best;
        }

        // Bakes (and caches) the world-space garment geometry for a body atom. Remove mode freezes
        // animation + physics, so this is computed once and reused every frame; the expensive
        // skinning recompute (UpdateVerts), vertex read, and AABB build run here instead of per
        // frame. The triangle topology is captured alongside the verts and the AABB is precomputed
        // for the per-frame ray fast-reject.
        private List<WrapGeom> GetGarmentGeom(Atom bodyAtom)
        {
            List<WrapGeom> geoms;
            if (_wrapGeomCache.TryGetValue(bodyAtom, out geoms) && geoms != null) return geoms;

            geoms = new List<WrapGeom>();
            _wrapGeomCache[bodyAtom] = geoms;

            DAZSkinWrap[] wraps;
            try { wraps = bodyAtom.GetComponentsInChildren<DAZSkinWrap>(false); } catch { wraps = null; }
            if (wraps == null || wraps.Length == 0) return geoms;

            for (int w = 0; w < wraps.Length; w++)
            {
                DAZSkinWrap wrap = wraps[w];
                if (wrap == null) continue;

                DAZDynamicItem item = null;
                try { item = wrap.GetComponentInParent<DAZDynamicItem>(); } catch { }
                if (item == null) continue;

                bool ok = wrap.gameObject.activeInHierarchy;
                try { ok = ok && item.active && item.characterSelector != null; } catch { ok = false; }
                if (!ok) continue;

                // VaM skins clothing on the GPU, so the CPU-side mesh.vertices are stale bind-pose
                // geometry (sits nowhere near the on-screen garment). UpdateVerts recomputes the
                // garment verts from the LIVE body skin (rawSkinnedVerts) and writes them into the
                // mesh — same math VaM uses in CPU-skinning mode, harmless to the GPU render path.
                try { wrap.UpdateVerts(); } catch { }

                Mesh m = null;
                try { m = wrap.Mesh; } catch { }
                if (m == null) continue;

                int[] tris = null;
                try { tris = m.triangles; } catch { }
                if (tris == null || tris.Length < 3) continue;

                Vector3[] verts = null;
                try { verts = m.vertices; } catch { }
                if (verts == null || verts.Length < 3) continue;

                // Build the AABB once from the (now live, world-space) verts; mesh.bounds is junk in
                // play mode so we cannot use it.
                Vector3 mn = verts[0], mx = mn;
                for (int i = 1; i < verts.Length; i++)
                {
                    Vector3 p = verts[i];
                    if (p.x < mn.x) mn.x = p.x; else if (p.x > mx.x) mx.x = p.x;
                    if (p.y < mn.y) mn.y = p.y; else if (p.y > mx.y) mx.y = p.y;
                    if (p.z < mn.z) mn.z = p.z; else if (p.z > mx.z) mx.z = p.z;
                }

                geoms.Add(new WrapGeom { item = item, verts = verts, tris = tris, min = mn, max = mx });
            }

            return geoms;
        }

        // Slab ray/AABB test; d is normalized, hits accepted on the forward half-line [0, +inf).
        private static bool RayHitsAabb(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx)
        {
            float tmin = 0f, tmax = float.MaxValue;
            for (int a = 0; a < 3; a++)
            {
                float od = a == 0 ? o.x : (a == 1 ? o.y : o.z);
                float dd = a == 0 ? d.x : (a == 1 ? d.y : d.z);
                float lo = a == 0 ? mn.x : (a == 1 ? mn.y : mn.z);
                float hi = a == 0 ? mx.x : (a == 1 ? mx.y : mx.z);
                if (Mathf.Abs(dd) < 1e-8f)
                {
                    if (od < lo || od > hi) return false;
                }
                else
                {
                    float inv = 1f / dd;
                    float t1 = (lo - od) * inv;
                    float t2 = (hi - od) * inv;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    if (t1 > tmin) tmin = t1;
                    if (t2 < tmax) tmax = t2;
                    if (tmin > tmax) return false;
                }
            }
            return true;
        }

        // Nearest double-sided ray/triangle hit over a mesh (Moller-Trumbore). t is in world units.
        private static bool RayMeshNearest(Vector3 o, Vector3 d, Vector3[] verts, int[] tris, out float bestT)
        {
            bestT = float.MaxValue;
            bool found = false;
            int vcount = verts.Length;
            const float EPS = 1e-7f;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vcount || i1 >= vcount || i2 >= vcount) continue;
                Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];
                Vector3 e1 = v1 - v0;
                Vector3 e2 = v2 - v0;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -EPS && det < EPS) continue;
                float inv = 1f / det;
                Vector3 tv = o - v0;
                float u = Vector3.Dot(tv, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(tv, e1);
                float vv = Vector3.Dot(d, q) * inv;
                if (vv < 0f || u + vv > 1f) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > EPS && t < bestT) { bestT = t; found = true; }
            }
            return found;
        }

        private void RemoveModeClearHighlight()
        {
            try { if (_removeHighlight != null) _removeHighlight.Clear(); } catch { }
            _removeHighlightedIdentity = null;
        }

        private void RemoveModeExecuteRemoval(RemoveTarget target)
        {
            if (target == null) return;

            // Any removal changes which garments/atoms are live, so drop the baked geometry; the
            // next hover re-bakes the new set exactly once.
            _wrapGeomCache.Clear();

            if (target.kind == RemoveTargetKind.ClothingItem && target.selector != null)
            {
                DAZClothingItem ci = target.item as DAZClothingItem;
                DAZCharacterSelector sel = target.selector;
                if (ci == null || sel == null) return;
                sel.SetActiveClothingItem(ci, false);
                PushUndo(() => { try { if (sel != null && ci != null) sel.SetActiveClothingItem(ci, true); } catch { } });
                return;
            }

            if (target.kind == RemoveTargetKind.HairItem && target.selector != null)
            {
                DAZHairGroup hi = target.item as DAZHairGroup;
                DAZCharacterSelector sel = target.selector;
                if (hi == null || sel == null) return;
                sel.SetActiveHairItem(hi, false);
                PushUndo(() => { try { if (sel != null && hi != null) sel.SetActiveHairItem(hi, true); } catch { } });
                return;
            }

            if (target.kind == RemoveTargetKind.Atom && target.atom != null)
            {
                Atom atom = target.atom;
                try { PushUndoSnapshotForAtomRemoval(atom); } catch { }
                try { SuperController.singleton.RemoveAtom(atom); }
                catch (Exception ex) { LogUtil.LogError("[VPB] RemoveAtom failed: " + ex); }
            }
        }
    }
}
