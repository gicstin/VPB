using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        static readonly float[] OutlinerMoveSteps = { 0.001f, 0.01f, 0.1f, 0.5f };
        static readonly float[] OutlinerRotateSteps = { 1f, 5f, 15f, 45f };

        const float OutlinerSpringFieldInterval = 1f / 15f;

        const string OutlinerPosFormat = "0.000";
        const string OutlinerRotFormat = "0.0";
        const string OutlinerScaleFormat = "0.000";

        readonly InputField[] _outlinerPosFields = new InputField[3];
        readonly Image[] _outlinerAxisLockImgs = new Image[6];
        GameObject _outlinerLockFreeChip;
        Text _outlinerLockNote;
        readonly InputField[] _outlinerRotFields = new InputField[3];
        InputField _outlinerScaleField;
        int _outlinerSpringHeldCount;
        Atom _outlinerSpringAtom;
        OutlinerSpringAxis.Kind _outlinerSpringKind;
        int _outlinerSpringAxis;
        bool _outlinerSpringActive;
        bool _outlinerSpringBlocked;
        bool _outlinerSpringLocal;
        readonly List<Atom> _outlinerSpringPhysicsHeld = new List<Atom>(8);
        double _outlinerSpringOffset;
        Vector3 _outlinerSpringBasePos;
        Vector3 _outlinerSpringBaseDir;
        Quaternion _outlinerSpringBaseRot = Quaternion.identity;
        Vector3 _outlinerSpringBaseUiEuler;
        float _outlinerSpringBaseScale;
        float _outlinerSpringScaleMin;
        float _outlinerSpringScaleMax;
        string _outlinerSpringScaleStorable;
        float _outlinerSpringNextFieldTime;
        Vector3 _outlinerXformSyncPos;
        Vector3 _outlinerXformSyncEuler;
        bool _outlinerXformSyncSet;
        Vector3 _outlinerXformEuler;
        string _outlinerXformEulerUid;
        Vector3 _outlinerXformClipPos;
        Quaternion _outlinerXformClipRot = Quaternion.identity;
        float _outlinerXformClipScale = 1f;
        bool _outlinerXformClipHasPos;
        bool _outlinerXformClipHasRot;
        bool _outlinerXformClipHasScale;
        string _outlinerXformClipSource = "";
        readonly OutlinerGroupEdit _outlinerGroup = new OutlinerGroupEdit();
        bool _outlinerGroupSpringActive;

        static string OutlinerXformPartName(OutlinerPasteMode part)
        {
            if (part == OutlinerPasteMode.Rotation) return VPBTranslation.T("outliner.rotation", "Rotation");
            if (part == OutlinerPasteMode.Scale) return VPBTranslation.T("outliner.scale", "Scale");
            return VPBTranslation.T("outliner.position", "Position");
        }

        bool OutlinerXformClipHas(OutlinerPasteMode part)
        {
            if (part == OutlinerPasteMode.Rotation) return _outlinerXformClipHasRot;
            if (part == OutlinerPasteMode.Scale) return _outlinerXformClipHasScale;
            if (part == OutlinerPasteMode.PositionAndRotation)
                return _outlinerXformClipHasPos || _outlinerXformClipHasRot;
            return _outlinerXformClipHasPos;
        }

        bool BeginOutlinerGroupEdit(Atom primary)
        {
            _outlinerGroup.Clear();
            if (!OutlinerGroupLinked()) return false;
            _outlinerSelection.CopyUids(_outlinerEditUids);
            return _outlinerGroup.Capture(primary, _outlinerEditUids);
        }

        void ApplyOutlinerGroupFromPrimary(Atom primary)
        {
            Transform t = OutlinerEdits.ControlTransform(primary);
            if (t == null) return;
            _outlinerGroup.ApplyFromPrimary(t.position, t.rotation);
        }

        static float OutlinerMoveStep()
        {
            return VPBConfig.Instance == null
                ? 0.1f
                : VPBConfig.ClampOutlinerMoveStep(VPBConfig.Instance.OutlinerMoveStep);
        }

        static float OutlinerRotateStep()
        {
            return VPBConfig.Instance == null
                ? 15f
                : VPBConfig.ClampOutlinerRotateStep(VPBConfig.Instance.OutlinerRotateStep);
        }

        bool OutlinerLocalSpace()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.OutlinerLocalSpace;
        }

        void ClearOutlinerTransformFieldRefs()
        {
            for (int i = 0; i < 3; i++)
            {
                _outlinerPosFields[i] = null;
                _outlinerRotFields[i] = null;
            }
            _outlinerScaleField = null;
            for (int i = 0; i < _outlinerAxisLockImgs.Length; i++) _outlinerAxisLockImgs[i] = null;
            _outlinerLockFreeChip = null;
            _outlinerLockNote = null;
            _outlinerSpringHeldCount = 0;
            CloseOutlinerSpringDrive();
            _outlinerXformSyncSet = false;
        }

        readonly int[] _outlinerXformSigTerms = new int[9];

        int OutlinerTransformCardSignature(Atom atom)
        {
            int[] v = _outlinerXformSigTerms;
            v[0] = OutlinerLock.IsLocked(atom) ? 1 : 0;
            int axes = 0;
            for (int a = 0; a < 3; a++)
            {
                if (OutlinerLock.IsPositionAxisLocked(atom, a)) axes |= 1 << a;
                if (OutlinerLock.IsRotationAxisLocked(atom, a)) axes |= 1 << (a + 3);
            }
            v[1] = axes;
            v[2] = OutlinerLocalSpace() ? 1 : 0;
            v[3] = _outlinerSelection.Count;
            v[4] = OutlinerLinkedEditActive() ? 1 : 0;
            int clip = 0;
            if (_outlinerXformClipHasPos) clip |= 1;
            if (_outlinerXformClipHasRot) clip |= 2;
            if (_outlinerXformClipHasScale) clip |= 4;
            v[5] = clip;
            v[6] = Mathf.RoundToInt(OutlinerMoveStep() * 10000f);
            v[7] = Mathf.RoundToInt(OutlinerRotateStep() * 100f);
            v[8] = VPBConfig.Instance != null && VPBConfig.Instance.OutlinerZUpAxes ? 1 : 0;
            int sig = 17;
            for (int i = 0; i < v.Length; i++) sig = sig * 31 + v[i];
            return sig;
        }

        bool OutlinerLinkedEditActive()
        {
            return OutlinerGroupLinked();
        }

        void BuildOutlinerTransformCard(Atom atom, float s)
        {
            string type = "";
            try { type = atom != null ? atom.type : ""; } catch { type = ""; }
            if (atom == null || atom.mainController == null || !OutlinerInspectorShows("transform", type))
            {
                ClearOutlinerTransformFieldRefs();
                return;
            }

            string cardTitle = VPBTranslation.T("outliner.transform", "Transform");
            if (OutlinerGroupLinked())
                cardTitle += "  ·  " + _outlinerSelection.Count
                    + " " + VPBTranslation.T("outliner.link.short", "linked");
            GameObject card = BeginOutlinerCard(OutlinerTransformCardId, cardTitle, true, s,
                () => ResetOutlinerTransformCard(atom), OutlinerTransformCardSignature(atom));
            if (_outlinerCardReused) return;
            ClearOutlinerTransformFieldRefs();
            if (!OutlinerCardIsOpen("transform", true))
            {
                UI.AddLE(CreateOutlinerPlaceholder(card, s),
                    preferredHeight: GalleryUiDesignTokens.HairGapRef * s);
                return;
            }

            bool locked = OutlinerLock.IsLocked(atom);
            AddOutlinerLockRow(card, atom, locked, s);
            if (locked)
            {
                AddOutlinerSectionCaption(card, VPBTranslation.T("outliner.position", "Position"), null, s);
                for (int axis = 0; axis < 3; axis++)
                    AddOutlinerLockedAxisRow(card, atom, axis, OutlinerSpringAxis.Kind.Position, s);
                AddOutlinerSectionCaption(card, VPBTranslation.T("outliner.rotation", "Rotation"), null, s);
                for (int axis = 0; axis < 3; axis++)
                    AddOutlinerLockedAxisRow(card, atom, axis, OutlinerSpringAxis.Kind.Rotation, s);
                return;
            }

            AddOutlinerSpaceRow(card, atom, s);

            GameObject posBar = BeginOutlinerXformSection(card,
                VPBTranslation.T("outliner.position", "Position"), s);
            AddOutlinerStepChips(posBar, OutlinerMoveSteps, OutlinerMoveStep(), "0.###",
                VPBTranslation.T("outliner.step.move_tip", "Step each − and + moves by, in metres."), v =>
                {
                    if (VPBConfig.Instance == null) return;
                    VPBConfig.Instance.OutlinerMoveStep = VPBConfig.ClampOutlinerMoveStep(v);
                    VPBConfig.Instance.Save();
                }, s);
            AddOutlinerXformSectionTools(posBar, atom, OutlinerPasteMode.Position,
                () => ResetOutlinerSection(atom, true), s);
            for (int axis = 0; axis < 3; axis++)
                AddOutlinerPositionAxisRow(card, atom, axis, s);

            GameObject rotBar = BeginOutlinerXformSection(card,
                VPBTranslation.T("outliner.rotation", "Rotation"), s);
            AddOutlinerStepChips(rotBar, OutlinerRotateSteps, OutlinerRotateStep(), "0.#",
                VPBTranslation.T("outliner.step.rotate_tip", "Step each − and + turns by, in degrees."), v =>
                {
                    if (VPBConfig.Instance == null) return;
                    VPBConfig.Instance.OutlinerRotateStep = VPBConfig.ClampOutlinerRotateStep(v);
                    VPBConfig.Instance.Save();
                }, s);
            AddOutlinerXformSectionTools(rotBar, atom, OutlinerPasteMode.Rotation,
                () => ResetOutlinerSection(atom, false), s);
            for (int axis = 0; axis < 3; axis++)
                AddOutlinerRotationAxisRow(card, atom, axis, s);

            AddOutlinerScaleRow(card, atom, s);

            GameObject acts = AddOutlinerActionStrip(card, s);
            AddOutlinerXformBtn(acts, VPBTranslation.T("outliner.align_view", "To view"),
                () => AlignOutlinerToView(atom), s);
            AddOutlinerXformBtn(acts, VPBTranslation.T("outliner.drop_floor", "Drop"),
                () => DropOutlinerToFloor(atom), s);
            AddOutlinerXformBtn(acts, VPBTranslation.T("outliner.face_view", "Face me"),
                () => FaceOutlinerToView(atom), s);
            AddOutlinerXformBtn(acts, VPBTranslation.T("outliner.xform.snap", "Snap"),
                () => SnapOutlinerRotation(atom), s);

            BuildOutlinerAlignSection(card, atom, s);
        }

        GameObject AddOutlinerActionStrip(GameObject card, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject row = new GameObject("XformActs");
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            return row;
        }

        void AddOutlinerLockRow(GameObject card, Atom atom, bool locked, float s)
        {
            if (!OutlinerLock.Supports(atom)) return;
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject row = new GameObject("XformLock");
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            int axisLocks = OutlinerLock.AxisLockCount(atom);
            AddOutlinerFieldCaption(row, VPBTranslation.T("outliner.lock", "Lock"), s, h);
            _outlinerLockFreeChip = AddOutlinerChoiceChip(row,
                VPBTranslation.T("outliner.lock.off", "Free"),
                !locked && axisLocks == 0, () => SetOutlinerControlLock(atom, false), s, h);
            AddOutlinerChoiceChip(row, VPBTranslation.T("outliner.lock.on", "All"), locked,
                () => SetOutlinerControlLock(atom, true), s, h);

            Text t = UI.CreateLabel(row, OutlinerLockNoteText(locked, axisLocks),
                GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            UI.AddLE(t.gameObject, flexibleWidth: 1f, preferredHeight: h, minHeight: h);
            _outlinerLockNote = t;
        }

        static string OutlinerLockNoteText(bool locked, int axisLocks)
        {
            if (locked) return VPBTranslation.T("outliner.lock.note", "Position and rotation are held");
            if (axisLocks > 0)
                return axisLocks + " " + VPBTranslation.T("outliner.lock.axes_held", "axes held");
            return "";
        }

        void SetOutlinerChoiceChipState(GameObject chip, bool on)
        {
            if (chip == null) return;
            Image img = chip.GetComponent<Image>();
            if (img != null)
                img.color = on ? GalleryUiColorTokens.ActiveSelected : GalleryUiColorTokens.RowIdle;
            Text label = chip.GetComponentInChildren<Text>();
            if (label != null)
                label.color = on ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextMuted;
        }

        bool RefreshOutlinerAxisLocksInPlace(Atom atom)
        {
            if (atom == null || _outlinerLockNote == null) return false;
            if (OutlinerLock.IsLocked(atom)) return false;
            for (int i = 0; i < _outlinerAxisLockImgs.Length; i++)
            {
                if (_outlinerAxisLockImgs[i] == null) return false;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                int world = OutlinerWorldAxis(axis);
                _outlinerAxisLockImgs[axis].color = OutlinerLock.IsPositionAxisLocked(atom, world)
                    ? GalleryUiColorTokens.ActiveOn
                    : GalleryUiColorTokens.ChromeIconWell;
                _outlinerAxisLockImgs[axis + 3].color = OutlinerLock.IsRotationAxisLocked(atom, world)
                    ? GalleryUiColorTokens.ActiveOn
                    : GalleryUiColorTokens.ChromeIconWell;
            }
            int axisLocks = OutlinerLock.AxisLockCount(atom);
            _outlinerLockNote.text = OutlinerLockNoteText(false, axisLocks);
            SetOutlinerChoiceChipState(_outlinerLockFreeChip, axisLocks == 0);
            return true;
        }

        void AddOutlinerAxisLockToggle(GameObject row, Atom atom, int world,
            OutlinerSpringAxis.Kind kind, bool locked, float s)
        {
            if (!OutlinerLock.Supports(atom)) return;
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            Color well = locked ? GalleryUiColorTokens.ActiveOn : GalleryUiColorTokens.ChromeIconWell;
            GameObject btn = UI.CreateFloatChromeIconButton(row.transform, h, "lock", well,
                () => SetOutlinerAxisLock(atom, world, kind, !OutlinerAxisLockedNow(atom, world, kind)));
            if (btn == null) return;
            int slot = OutlinerUiAxisSlot(world) + (kind == OutlinerSpringAxis.Kind.Rotation ? 3 : 0);
            if (slot >= 0 && slot < _outlinerAxisLockImgs.Length)
                _outlinerAxisLockImgs[slot] = btn.GetComponent<Image>();
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(btn, locked
                ? VPBTranslation.T("outliner.lock.axis_off", "Release this axis")
                : VPBTranslation.T("outliner.lock.axis_on", "Hold this axis"));
        }

        void SetOutlinerAxisLock(Atom atom, int world, OutlinerSpringAxis.Kind kind, bool locked)
        {
            if (atom == null) return;
            bool ok = kind == OutlinerSpringAxis.Kind.Rotation
                ? OutlinerLock.SetRotationAxisLocked(atom, world, locked)
                : OutlinerLock.SetPositionAxisLocked(atom, world, locked);
            if (OutlinerGroupLinked())
            {
                CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
                for (int i = 1; i < _outlinerEditAtoms.Count; i++)
                {
                    if (kind == OutlinerSpringAxis.Kind.Rotation)
                        OutlinerLock.SetRotationAxisLocked(_outlinerEditAtoms[i], world, locked);
                    else
                        OutlinerLock.SetPositionAxisLocked(_outlinerEditAtoms[i], world, locked);
                }
            }
            if (!ok)
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            _outlinerLastFocused = true;
            if (RefreshOutlinerAxisLocksInPlace(atom)) return;
            RebuildOutlinerInspector();
        }

        static bool OutlinerAxisLockedNow(Atom atom, int world, OutlinerSpringAxis.Kind kind)
        {
            return kind == OutlinerSpringAxis.Kind.Rotation
                ? OutlinerLock.IsRotationAxisLocked(atom, world)
                : OutlinerLock.IsPositionAxisLocked(atom, world);
        }

        int OutlinerUiAxisSlot(int world)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (OutlinerWorldAxis(axis) == world) return axis;
            }
            return 0;
        }

        void SetOutlinerControlLock(Atom atom, bool locked)
        {
            if (atom == null) return;
            bool wasLocked = OutlinerLock.IsLocked(atom);
            bool hadAxisLocks = OutlinerLock.AxisLockCount(atom) > 0;
            if (wasLocked == locked && !(hadAxisLocks && !locked)) return;
            bool ok = wasLocked == locked || OutlinerLock.SetLocked(atom, locked);
            if (!locked) ok = OutlinerLock.ClearAxisLocks(atom) && ok;
            if (OutlinerGroupLinked())
            {
                CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
                for (int i = 1; i < _outlinerEditAtoms.Count; i++)
                {
                    OutlinerLock.SetLocked(_outlinerEditAtoms[i], locked);
                    if (!locked) OutlinerLock.ClearAxisLocks(_outlinerEditAtoms[i]);
                }
            }
            if (!ok)
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            _outlinerLastFocused = true;
            RebuildOutlinerInspector();
        }

        void AddOutlinerLockedAxisRow(GameObject card, Atom atom, int axis,
            OutlinerSpringAxis.Kind kind, float s)
        {
            float h = OutlinerSlotH(s);
            int world = OutlinerWorldAxis(axis);
            GameObject row = BeginOutlinerNudgeRow(card, axis,
                (kind == OutlinerSpringAxis.Kind.Position ? "Pos_" : "Rot_") + axis, kind, s, h);
            float val = kind == OutlinerSpringAxis.Kind.Position
                ? OutlinerReadPositionAxis(atom, world)
                : OutlinerReadRotationAxis(atom, world);
            string format = kind == OutlinerSpringAxis.Kind.Position ? OutlinerPosFormat : OutlinerRotFormat;
            InputField inf = AddOutlinerNumberField(row, val, format, v => { }, s, h);
            if (inf != null)
            {
                inf.interactable = false;
                inf.readOnly = true;
            }
            if (kind == OutlinerSpringAxis.Kind.Position) _outlinerPosFields[world] = inf;
            else _outlinerRotFields[world] = inf;
        }

        void AddOutlinerSpaceRow(GameObject card, Atom atom, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject row = new GameObject("XformSpace");
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            AddOutlinerFieldCaption(row, VPBTranslation.T("outliner.space", "Axes"), s, h);
            bool local = OutlinerLocalSpace();
            AddOutlinerChoiceChip(row, VPBTranslation.T("outliner.space.world", "World"), !local,
                () => SetOutlinerLocalSpace(false), s, h);
            AddOutlinerChoiceChip(row, VPBTranslation.T("outliner.space.local", "Local"), local,
                () => SetOutlinerLocalSpace(true), s, h);
        }

        void SetOutlinerLocalSpace(bool local)
        {
            if (VPBConfig.Instance == null) return;
            if (VPBConfig.Instance.OutlinerLocalSpace == local) return;
            VPBConfig.Instance.OutlinerLocalSpace = local;
            VPBConfig.Instance.Save();
            RebuildOutlinerInspector();
        }

        void AddOutlinerStepChips(GameObject row, float[] steps, float current,
            string format, string tip, System.Action<float> set, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            for (int i = 0; i < steps.Length; i++)
            {
                float step = steps[i];
                bool on = Mathf.Abs(step - current) < step * 0.01f;
                GameObject chip = AddOutlinerChoiceChip(row, step.ToString(format), on,
                    () => set(step), s, h);
                AddTooltipPlain(chip, tip);
            }
        }

        GameObject AddOutlinerChoiceChip(GameObject row, string label, bool on,
            UnityEngine.Events.UnityAction act, float s, float h)
        {
            Color well = on ? GalleryUiColorTokens.ActiveSelected : GalleryUiColorTokens.RowIdle;
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, 0f, h, label,
                GalleryUiDesignTokens.FontCaptionRef, well, act);
            UI.AddLE(btn, flexibleWidth: 1f, preferredHeight: h, minHeight: h,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * s);
            Text t = btn != null ? btn.GetComponentInChildren<Text>() : null;
            if (t != null) t.color = on ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextMuted;
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            Button b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            OutlinerUseInwardHoverRim(btn);
            return btn;
        }

        GameObject BeginOutlinerXformSection(GameObject card, string label, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject row = new GameObject("XformSection_" + label);
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text t = UI.CreateLabel(row, label, GalleryUiDesignTokens.FontCaptionRef,
                GalleryUiColorTokens.TextMuted, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            UI.AddLE(t.gameObject,
                preferredWidth: GalleryUiDesignTokens.OutlinerAxisNameWidthRef * s,
                minWidth: GalleryUiDesignTokens.OutlinerAxisNameWidthRef * 0.6f * s,
                preferredHeight: h, minHeight: h, flexibleWidth: 0f, flexibleHeight: 0f);
            return row;
        }

        void AddOutlinerSectionCaption(GameObject card, string label,
            UnityEngine.Events.UnityAction reset, float s)
        {
            GameObject row = BeginOutlinerXformSection(card, label, s);
            if (reset == null) return;
            AddOutlinerXformSpacer(row, s);
            AddOutlinerXformSectionIcon(row, "reset", GalleryUiColorTokens.ChromeIconWell,
                VPBTranslation.T("outliner.xform.reset_section", "Reset these values"), reset, s);
        }

        void AddOutlinerXformSpacer(GameObject row, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject go = new GameObject("Gap");
            go.transform.SetParent(row.transform, false);
            UI.AddLE(go, flexibleWidth: 1f, preferredHeight: h, minHeight: h, flexibleHeight: 0f);
        }

        GameObject AddOutlinerXformSectionIcon(GameObject row, string icon, Color well, string tip,
            UnityEngine.Events.UnityAction act, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateFloatChromeIconButton(row.transform, h, icon, well, act);
            if (btn == null) return null;
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(btn, tip);
            return btn;
        }

        void AddOutlinerXformSectionTools(GameObject row, Atom atom, OutlinerPasteMode part,
            UnityEngine.Events.UnityAction reset, float s)
        {
            AddOutlinerXformSpacer(row, s);
            string what = OutlinerXformPartName(part);
            AddOutlinerXformSectionIcon(row, "copy", GalleryUiColorTokens.ChromeIconWell,
                VPBTranslation.T("outliner.xform.copy_part", "Copy the ") + what.ToLower()
                    + VPBTranslation.T("outliner.xform.copy_part_2", " of this atom"),
                () => CopyOutlinerTransformPart(atom, part), s);
            bool armed = OutlinerXformClipHas(part);
            GameObject paste = AddOutlinerXformSectionIcon(row, "paste",
                armed ? GalleryUiColorTokens.ChromeIconWell : GalleryUiColorTokens.RowIdle,
                armed
                    ? VPBTranslation.T("outliner.xform.paste_part", "Paste the copied ") + what.ToLower()
                        + " (" + _outlinerXformClipSource + ")"
                    : VPBTranslation.T("outliner.xform.paste_part_empty", "Copy a ") + what.ToLower()
                        + VPBTranslation.T("outliner.xform.paste_part_empty_2", " from some atom first"),
                () => PasteOutlinerTransform(atom, part), s);
            if (!armed && paste != null)
            {
                CanvasGroup cg = paste.GetComponent<CanvasGroup>();
                if (cg == null) cg = paste.AddComponent<CanvasGroup>();
                cg.alpha = OutlinerDisabledIconAlpha;
            }
            if (reset != null)
                AddOutlinerXformSectionIcon(row, "reset", GalleryUiColorTokens.ChromeIconWell,
                    VPBTranslation.T("outliner.xform.reset_section", "Reset these values"), reset, s);
        }

        void AddOutlinerFieldCaption(GameObject row, string label, float s, float h)
        {
            float w = GalleryUiDesignTokens.OutlinerAxisNameWidthRef * s;
            Text t = UI.CreateLabel(row, label, GalleryUiDesignTokens.FontCaptionRef,
                GalleryUiColorTokens.TextMuted, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            UI.AddLE(t.gameObject, preferredWidth: w, minWidth: w, preferredHeight: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
        }

        static bool OutlinerZUpAxes()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.OutlinerZUpAxes;
        }

        static int OutlinerWorldAxis(int row)
        {
            if (row == 0 || !OutlinerZUpAxes()) return row;
            return row == 1 ? 2 : 1;
        }

        static string OutlinerAxisLabel(int row)
        {
            if (row == 0) return VPBTranslation.T("outliner.axis.x", "X");
            if (row == 1) return VPBTranslation.T("outliner.axis.y", "Y");
            return VPBTranslation.T("outliner.axis.z", "Z");
        }

        static Color OutlinerAxisTint(int row)
        {
            if (row == 0) return GalleryUiColorTokens.AxisX;
            if (row == 1) return GalleryUiColorTokens.AxisY;
            return GalleryUiColorTokens.AxisZ;
        }

        static string OutlinerAxisIconRole(int row, OutlinerSpringAxis.Kind kind)
        {
            if (kind == OutlinerSpringAxis.Kind.Rotation) return "angle";
            int world = OutlinerWorldAxis(row);
            if (world == 0) return "axis_lr";
            return world == 1 ? "axis_ud" : "axis_depth";
        }

        static string OutlinerAxisIconTip(int row, OutlinerSpringAxis.Kind kind)
        {
            int world = OutlinerWorldAxis(row);
            if (kind == OutlinerSpringAxis.Kind.Rotation)
            {
                if (world == 0) return VPBTranslation.T("outliner.axis.tip.pitch", "Turn around X");
                return world == 1
                    ? VPBTranslation.T("outliner.axis.tip.yaw", "Turn around Y")
                    : VPBTranslation.T("outliner.axis.tip.roll", "Turn around Z");
            }
            if (world == 0) return VPBTranslation.T("outliner.axis.tip.lr", "Left / right");
            return world == 1
                ? VPBTranslation.T("outliner.axis.tip.ud", "Up / down")
                : VPBTranslation.T("outliner.axis.tip.depth", "Forward / back");
        }

        void AddOutlinerAxisIcon(GameObject row, int rowAxis, OutlinerSpringAxis.Kind kind, float s, float h)
        {
            float w = GalleryUiDesignTokens.OutlinerAxisIconSizeRef * s;
            GameObject go = new GameObject("AxisIcon");
            go.transform.SetParent(row.transform, false);
            Image img = go.AddComponent<Image>();
            img.preserveAspect = true;
            Sprite spr = null;
            try { spr = UI.LoadIconSprite(OutlinerAxisIconRole(rowAxis, kind), OutlinerAxisTint(rowAxis)); }
            catch { spr = null; }
            if (spr != null) UI.SetIconSprite(img, spr);
            else img.enabled = false;
            if (kind == OutlinerSpringAxis.Kind.Rotation)
            {
                int world = OutlinerWorldAxis(rowAxis);
                float z = world == 0 ? 0f : (world == 1 ? 90f : -90f);
                go.transform.localEulerAngles = new Vector3(0f, 0f, z);
            }
            UI.AddLE(go, preferredWidth: w, minWidth: w, preferredHeight: w, minHeight: w,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(go, OutlinerAxisIconTip(rowAxis, kind));
        }

        GameObject BeginOutlinerNudgeRow(GameObject card, int axis, string name,
            OutlinerSpringAxis.Kind kind, float s, float h)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.HairGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            float w = GalleryUiDesignTokens.OutlinerAxisNameWidthRef * 0.5f * s;
            Text t = UI.CreateLabel(row, OutlinerAxisLabel(axis), GalleryUiDesignTokens.FontBodyRef,
                OutlinerAxisTint(axis), TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontBodyRef, s);
            UI.AddLE(t.gameObject, preferredWidth: w, minWidth: w, preferredHeight: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddOutlinerAxisIcon(row, axis, kind, s, h);
            return row;
        }

        void AddOutlinerNudgeButton(GameObject row, string label, string tip,
            UnityEngine.Events.UnityAction act, float s, float h)
        {
            float w = GalleryUiDesignTokens.OutlinerNudgeButtonWidthRef * s;
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, w, h, label,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle, act);
            UI.AddLE(btn, preferredWidth: w, minWidth: w, preferredHeight: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            Button b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            Text t = btn != null ? btn.GetComponentInChildren<Text>() : null;
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontBodyRef, s);
            OutlinerUseInwardHoverRim(btn);
            AddTooltipPlain(btn, tip);
        }

        InputField AddOutlinerNumberField(GameObject row, float val, string format,
            System.Action<float> set, float s, float h)
        {
            float w = GalleryUiDesignTokens.OutlinerAxisValueWidthRef * s;
            GameObject fieldGO = UI.CreateTextInput(row, w, h,
                val.ToString(format), GalleryUiDesignTokens.FontBodyRef, 0f, 0f,
                AnchorPresets.middleCenter, null);
            UI.AddLE(fieldGO, flexibleWidth: 0f, preferredWidth: w, minWidth: w,
                preferredHeight: h, minHeight: h);
            InputField inf = fieldGO.GetComponent<InputField>();
            if (inf == null) return null;
            inf.text = val.ToString(format);
            inf.contentType = InputField.ContentType.DecimalNumber;
            StyleOutlinerInputField(inf);
            ApplyOutlinerScaledFont(inf.textComponent, GalleryUiDesignTokens.FontBodyRef, s);
            ApplyOutlinerScaledFont(inf.placeholder as Text, GalleryUiDesignTokens.FontBodyRef, s);
            inf.onEndEdit.AddListener(txt =>
            {
                float f;
                if (float.TryParse(txt, out f)) set(f);
            });
            AddTooltipPlain(fieldGO, VPBTranslation.T("outliner.xform.type_value", "Type an exact value"));
            return inf;
        }

        OutlinerSpringAxis AddOutlinerSpringTrack(GameObject row, Atom atom, int world,
            OutlinerSpringAxis.Kind kind, float s, float h)
        {
            float handleW = GalleryUiDesignTokens.OutlinerSliderHandleSizeRef * s;
            float minW = GalleryUiDesignTokens.OutlinerSpringTrackMinWidthRef * s;
            GameObject host = new GameObject("Spring");
            host.transform.SetParent(row.transform, false);
            UI.AddLE(host, flexibleWidth: 1f, preferredHeight: h, minHeight: h, minWidth: minW,
                flexibleHeight: 0f);
            UI.AddGalleryElementRoundedBg(host, GalleryUiColorTokens.SurfaceMid, true);
            UIHoverBorder hb = host.AddComponent<UIHoverBorder>();
            hb.inward = true;
            try { hb.ApplyBorderSettings(); } catch { }

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(host.transform, false);
            Image fillImg = UI.AddImage(fillGO, GalleryUiColorTokens.ActiveOn, false);
            RectTransform fillRt = fillGO.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0.5f, 0.32f);
            fillRt.anchorMax = new Vector2(0.5f, 0.68f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.sizeDelta = new Vector2(0f, 0f);
            fillRt.anchoredPosition = Vector2.zero;
            fillImg.enabled = false;

            GameObject tickGO = new GameObject("Rest");
            tickGO.transform.SetParent(host.transform, false);
            UI.AddImage(tickGO, GalleryUiColorTokens.TextDim, false);
            RectTransform tickRt = tickGO.GetComponent<RectTransform>();
            float tickW = GalleryUiDesignTokens.TightGapRef * s;
            tickRt.anchorMin = new Vector2(0.5f, 0.22f);
            tickRt.anchorMax = new Vector2(0.5f, 0.78f);
            tickRt.pivot = new Vector2(0.5f, 0.5f);
            tickRt.sizeDelta = new Vector2(tickW, 0f);
            tickRt.anchoredPosition = Vector2.zero;

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(host.transform, false);
            Image handleImg = UI.AddGalleryElementRoundedBg(handleGO, GalleryUiColorTokens.ActiveSelected, false);
            RectTransform handleRt = handleGO.GetComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0.5f, 0.5f);
            handleRt.anchorMax = new Vector2(0.5f, 0.5f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            float handleH = Mathf.Max(GalleryUiDesignTokens.ButtonSizeRef * s,
                h - GalleryUiDesignTokens.ControlRimGutterRef * 2f * s);
            handleRt.sizeDelta = new Vector2(handleW, handleH);
            handleRt.anchoredPosition = Vector2.zero;

            OutlinerSpringAxis spring = host.AddComponent<OutlinerSpringAxis>();
            spring.Wire(handleRt, fillRt, handleImg, fillImg, handleW,
                GalleryUiColorTokens.ActiveSelected, GalleryUiColorTokens.ActiveOn);
            spring.OnDriveBegin = () => BeginOutlinerSpringDrive(atom, world, kind);
            spring.OnDriveTick = TickOutlinerSpringDrive;
            spring.OnDriveEnd = EndOutlinerSpringDrive;

            string tip = kind == OutlinerSpringAxis.Kind.Rotation
                ? VPBTranslation.T("outliner.spring.rot",
                    "Drag from center — farther turns faster. Release recenters.")
                : (kind == OutlinerSpringAxis.Kind.Scale
                    ? VPBTranslation.T("outliner.spring.scale",
                        "Drag from center — farther scales faster. Release recenters.")
                    : VPBTranslation.T("outliner.spring.pos",
                        "Drag from center — farther moves faster. Release recenters."));
            AddTooltipPlain(host, tip);
            return spring;
        }

        void BeginOutlinerSpringDrive(Atom atom, int axis, OutlinerSpringAxis.Kind kind)
        {
            if (_outlinerSpringActive) CloseOutlinerSpringDrive();
            _outlinerSpringHeldCount++;
            _outlinerLastFocused = true;
            _outlinerSpringActive = false;
            _outlinerSpringBlocked = false;
            _outlinerSpringOffset = 0.0;
            _outlinerSpringNextFieldTime = 0f;
            if (atom == null || atom.mainController == null) return;

            _outlinerSpringAtom = atom;
            _outlinerSpringKind = kind;
            _outlinerSpringAxis = axis;
            _outlinerSpringLocal = OutlinerLocalSpace();

            if (kind == OutlinerSpringAxis.Kind.Position)
            {
                _outlinerSpringBasePos = OutlinerEdits.ControlTransform(atom).position;
                _outlinerSpringBaseDir = OutlinerMoveAxis(atom, axis, _outlinerSpringLocal);
            }
            else if (kind == OutlinerSpringAxis.Kind.Rotation)
            {
                _outlinerSpringBaseRot = OutlinerEdits.ControlTransform(atom).rotation;
                _outlinerSpringBaseUiEuler = OutlinerUiEuler(atom);
            }
            else
            {
                JSONStorableFloat p = OutlinerScaleParam(atom);
                if (p == null) return;
                _outlinerSpringBaseScale = p.val;
                _outlinerSpringScaleMin = p.min;
                _outlinerSpringScaleMax = p.max;
                _outlinerSpringScaleStorable = OutlinerEdits.ScaleStorableId(atom);
            }
            _outlinerGroupSpringActive = BeginOutlinerGroupEdit(atom);
            if (kind == OutlinerSpringAxis.Kind.Scale) HoldOutlinerSpringPhysics(atom);
            _outlinerSpringActive = true;
            PulseOutlinerTargets();
        }

        void HoldOutlinerSpringPhysics(Atom primary)
        {
            _outlinerSpringPhysicsHeld.Clear();
            CollectOutlinerEditAtoms(primary, _outlinerEditAtoms);
            for (int i = 0; i < _outlinerEditAtoms.Count; i++)
            {
                Atom a = _outlinerEditAtoms[i];
                JSONStorableBool frozen = OutlinerEdits.FreezePhysicsParam(a);
                if (frozen == null || frozen.val) continue;
                if (OutlinerEdits.SetPhysicsFrozen(a, true)) _outlinerSpringPhysicsHeld.Add(a);
            }
        }

        void ReleaseOutlinerSpringPhysics()
        {
            for (int i = 0; i < _outlinerSpringPhysicsHeld.Count; i++)
            {
                Atom a = _outlinerSpringPhysicsHeld[i];
                if (a != null) OutlinerEdits.SetPhysicsFrozen(a, false);
            }
            _outlinerSpringPhysicsHeld.Clear();
        }

        void TickOutlinerSpringDrive(float rate, float dt)
        {
            if (!_outlinerSpringActive || _outlinerSpringBlocked) return;
            Atom atom = _outlinerSpringAtom;
            if (atom == null || atom.mainController == null) return;

            _outlinerSpringOffset += OutlinerSpringAxis.SpeedFor(_outlinerSpringKind, rate) * dt;
            int axis = _outlinerSpringAxis;
            float offset = (float)_outlinerSpringOffset;
            bool showField = Time.unscaledTime >= _outlinerSpringNextFieldTime;

            if (_outlinerSpringKind == OutlinerSpringAxis.Kind.Position)
            {
                Vector3 next = _outlinerSpringBasePos + _outlinerSpringBaseDir * offset;
                if (!OutlinerEdits.WriteMainControllerPosition(atom, next))
                {
                    BlockOutlinerSpringDrive();
                    return;
                }
                if (_outlinerGroupSpringActive) ApplyOutlinerGroupFromPrimary(atom);
                if (showField)
                    SetOutlinerFieldText(_outlinerPosFields[axis],
                        axis == 0 ? next.x : (axis == 1 ? next.y : next.z), OutlinerPosFormat);
            }
            else if (_outlinerSpringKind == OutlinerSpringAxis.Kind.Rotation)
            {
                Vector3 unit = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
                Quaternion spin = Quaternion.AngleAxis(offset, unit);
                Quaternion next = _outlinerSpringLocal
                    ? _outlinerSpringBaseRot * spin
                    : spin * _outlinerSpringBaseRot;
                if (!OutlinerEdits.WriteMainControllerRotation(atom, next.eulerAngles))
                {
                    BlockOutlinerSpringDrive();
                    return;
                }
                if (_outlinerGroupSpringActive) ApplyOutlinerGroupFromPrimary(atom);
                if (showField)
                {
                    float shown = axis == 0
                        ? _outlinerSpringBaseUiEuler.x
                        : (axis == 1 ? _outlinerSpringBaseUiEuler.y : _outlinerSpringBaseUiEuler.z);
                    SetOutlinerFieldText(_outlinerRotFields[axis], OutlinerWrap360(shown + offset), OutlinerRotFormat);
                }
            }
            else
            {
                float next = Mathf.Clamp(_outlinerSpringBaseScale + offset,
                    _outlinerSpringScaleMin, _outlinerSpringScaleMax);
                _outlinerSpringOffset = next - _outlinerSpringBaseScale;
                if (!OutlinerEdits.WriteFloat(atom, _outlinerSpringScaleStorable, "scale", next))
                {
                    BlockOutlinerSpringDrive();
                    return;
                }
                if (_outlinerGroupSpringActive && _outlinerSpringBaseScale > 0f)
                    _outlinerGroup.ApplyScaleRatio(next / _outlinerSpringBaseScale);
                if (showField) SetOutlinerFieldText(_outlinerScaleField, next, OutlinerScaleFormat);
            }

            if (showField)
                _outlinerSpringNextFieldTime = Time.unscaledTime + OutlinerSpringFieldInterval;
        }

        static float OutlinerWrap360(float deg)
        {
            deg = deg % 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        void BlockOutlinerSpringDrive()
        {
            _outlinerSpringBlocked = true;
            ShowOutlinerWriteBlocked();
        }

        void EndOutlinerSpringDrive()
        {
            if (_outlinerSpringHeldCount > 0) _outlinerSpringHeldCount--;
            CloseOutlinerSpringDrive();
        }

        void CloseOutlinerSpringDrive()
        {
            ReleaseOutlinerSpringPhysics();
            if (!_outlinerSpringActive)
            {
                _outlinerSpringAtom = null;
                _outlinerGroupSpringActive = false;
                _outlinerGroup.Clear();
                return;
            }
            Atom atom = _outlinerSpringAtom;
            _outlinerSpringActive = false;
            _outlinerSpringAtom = null;
            _outlinerGroupSpringActive = false;
            _outlinerGroup.Clear();
            if (atom == null || atom.mainController == null) return;

            if (_outlinerSpringKind == OutlinerSpringAxis.Kind.Position)
            {
                Vector3 now = OutlinerEdits.ControlTransform(atom).position;
                if ((now - _outlinerSpringBasePos).sqrMagnitude > 1e-12f)
                    _outlinerUndo.Push(atom.uid + "|xform|pos", "Position",
                        _outlinerSpringBasePos.ToString(), now.ToString());
            }
            else if (_outlinerSpringKind == OutlinerSpringAxis.Kind.Rotation)
            {
                Vector3 now = OutlinerEdits.ControlTransform(atom).rotation.eulerAngles;
                StoreOutlinerUiEuler(atom, now);
                if ((now - _outlinerSpringBaseUiEuler).sqrMagnitude > 1e-6f)
                    _outlinerUndo.Push(atom.uid + "|xform|rot", "Rotation",
                        _outlinerSpringBaseUiEuler.ToString(), now.ToString());
            }
            else
            {
                JSONStorableFloat p = OutlinerScaleParam(atom);
                if (p != null && Mathf.Abs(p.val - _outlinerSpringBaseScale) > 1e-6f)
                    _outlinerUndo.Push(atom.uid + "|" + _outlinerSpringScaleStorable + "|scale", "Scale",
                        _outlinerSpringBaseScale.ToString("0.###"), p.val.ToString("0.###"));
            }
            RefreshOutlinerTransformFields(atom);
        }

        void AddOutlinerPositionAxisRow(GameObject card, Atom atom, int axis, float s)
        {
            float h = OutlinerSlotH(s);
            int world = OutlinerWorldAxis(axis);
            GameObject row = BeginOutlinerNudgeRow(card, axis, "Pos_" + axis,
                OutlinerSpringAxis.Kind.Position, s, h);
            bool held = OutlinerLock.IsPositionAxisLocked(atom, world);
            float val = OutlinerReadPositionAxis(atom, world);
            if (held)
            {
                _outlinerPosFields[world] = AddOutlinerReadOnlyField(row, val, OutlinerPosFormat, s, h);
            }
            else
            {
                string tipMinus = VPBTranslation.T("outliner.nudge.minus", "Move back by the step");
                string tipPlus = VPBTranslation.T("outliner.nudge.plus", "Move forward by the step");
                AddOutlinerNudgeButton(row, "−", tipMinus, () => NudgeOutlinerPosition(atom, world, -1f), s, h);
                AddOutlinerSpringTrack(row, atom, world, OutlinerSpringAxis.Kind.Position, s, h);
                AddOutlinerNudgeButton(row, "+", tipPlus, () => NudgeOutlinerPosition(atom, world, 1f), s, h);
                _outlinerPosFields[world] = AddOutlinerNumberField(row, val, OutlinerPosFormat,
                    v => SetOutlinerPositionAxis(atom, world, v), s, h);
                AddOutlinerResetButton(row, () => SetOutlinerPositionAxis(atom, world, 0f), s);
            }
            AddOutlinerAxisLockToggle(row, atom, world, OutlinerSpringAxis.Kind.Position, held, s);
        }

        InputField AddOutlinerReadOnlyField(GameObject row, float val, string format, float s, float h)
        {
            GameObject spacer = new GameObject("HeldGap");
            spacer.transform.SetParent(row.transform, false);
            UI.AddLE(spacer, flexibleWidth: 1f, preferredHeight: h, minHeight: h, flexibleHeight: 0f);
            InputField inf = AddOutlinerNumberField(row, val, format, v => { }, s, h);
            if (inf != null)
            {
                inf.interactable = false;
                inf.readOnly = true;
            }
            return inf;
        }

        void AddOutlinerRotationAxisRow(GameObject card, Atom atom, int axis, float s)
        {
            float h = OutlinerSlotH(s);
            int world = OutlinerWorldAxis(axis);
            GameObject row = BeginOutlinerNudgeRow(card, axis, "Rot_" + axis,
                OutlinerSpringAxis.Kind.Rotation, s, h);
            bool held = OutlinerLock.IsRotationAxisLocked(atom, world);
            float val = OutlinerReadRotationAxis(atom, world);
            if (held)
            {
                _outlinerRotFields[world] = AddOutlinerReadOnlyField(row, val, OutlinerRotFormat, s, h);
            }
            else
            {
                string tipMinus = VPBTranslation.T("outliner.turn.minus", "Turn back by the step");
                string tipPlus = VPBTranslation.T("outliner.turn.plus", "Turn forward by the step");
                AddOutlinerNudgeButton(row, "−", tipMinus, () => NudgeOutlinerRotation(atom, world, -1f), s, h);
                AddOutlinerSpringTrack(row, atom, world, OutlinerSpringAxis.Kind.Rotation, s, h);
                AddOutlinerNudgeButton(row, "+", tipPlus, () => NudgeOutlinerRotation(atom, world, 1f), s, h);
                _outlinerRotFields[world] = AddOutlinerNumberField(row, val, OutlinerRotFormat,
                    v => SetOutlinerRotationAxis(atom, world, v), s, h);
                AddOutlinerResetButton(row, () => SetOutlinerRotationAxis(atom, world, 0f), s);
            }
            AddOutlinerAxisLockToggle(row, atom, world, OutlinerSpringAxis.Kind.Rotation, held, s);
        }

        void AddOutlinerScaleRow(GameObject card, Atom atom, float s)
        {
            JSONStorableFloat p = OutlinerScaleParam(atom);
            if (p == null) return;
            GameObject bar = BeginOutlinerXformSection(card, VPBTranslation.T("outliner.scale", "Scale"), s);
            AddOutlinerXformSectionTools(bar, atom, OutlinerPasteMode.Scale,
                () => WriteOutlinerScale(atom, 1f), s);
            float h = OutlinerSlotH(s);
            GameObject row = new GameObject("Scale");
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.HairGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            AddOutlinerFieldCaption(row, VPBTranslation.T("outliner.scale.short", "S"), s, h);
            AddOutlinerNudgeButton(row, "−", VPBTranslation.T("outliner.scale.down", "Smaller"),
                () => NudgeOutlinerScale(atom, -1f), s, h);
            AddOutlinerSpringTrack(row, atom, 0, OutlinerSpringAxis.Kind.Scale, s, h);
            AddOutlinerNudgeButton(row, "+", VPBTranslation.T("outliner.scale.up", "Bigger"),
                () => NudgeOutlinerScale(atom, 1f), s, h);
            _outlinerScaleField = AddOutlinerNumberField(row, p.val, OutlinerScaleFormat,
                v => WriteOutlinerScale(atom, v), s, h);
            AddOutlinerResetButton(row, () => WriteOutlinerScale(atom, 1f), s);
        }

        static JSONStorableFloat OutlinerScaleParam(Atom atom)
        {
            JSONStorableFloat p = OutlinerEdits.GetFloat(atom, "scale", "scale");
            if (p != null) return p;
            return OutlinerEdits.GetFloat(atom, "rescaleObject", "scale");
        }

        void NudgeOutlinerScale(Atom atom, float sign)
        {
            JSONStorableFloat p = OutlinerScaleParam(atom);
            if (p == null) return;
            WriteOutlinerScale(atom, OutlinerSpringAxis.NudgeToStep(p.val, 0.02f, sign));
        }

        void WriteOutlinerScale(Atom atom, float value)
        {
            JSONStorableFloat p = OutlinerScaleParam(atom);
            if (p == null) { return; }
            string storable = OutlinerEdits.ScaleStorableId(atom);
            float before = p.val;
            float next = Mathf.Clamp(value, p.min, p.max);
            bool linked = BeginOutlinerGroupEdit(atom);
            if (!OutlinerEdits.WriteFloat(atom, storable, "scale", next))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            if (linked && before > 0f) _outlinerGroup.ApplyScaleRatio(next / before);
            PulseOutlinerTargets();
            _outlinerUndo.Push(atom.uid + "|" + storable + "|scale", "Scale",
                before.ToString("0.###"), next.ToString("0.###"));
            _outlinerLastFocused = true;
            SetOutlinerFieldText(_outlinerScaleField, next, OutlinerScaleFormat);
        }

        void ShowOutlinerWriteBlocked()
        {
            string reason = OutlinerEdits.LastWriteBlockReason;
            OutlinerEdits.LastWriteBlockReason = null;
            if (!string.IsNullOrEmpty(reason))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked_state",
                    "Cannot edit this: the control's ") + reason + ".", 3f);
                return;
            }
            ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
        }

        static float OutlinerReadPositionAxis(Atom atom, int axis)
        {
            if (atom == null || atom.mainController == null) return 0f;
            Vector3 p = OutlinerEdits.ControlTransform(atom).position;
            return axis == 0 ? p.x : (axis == 1 ? p.y : p.z);
        }

        Vector3 OutlinerUiEuler(Atom atom)
        {
            if (atom == null || atom.mainController == null) return Vector3.zero;
            Quaternion live = OutlinerEdits.ControlTransform(atom).rotation;
            if (_outlinerXformEulerUid == atom.uid
                && Quaternion.Angle(Quaternion.Euler(_outlinerXformEuler), live) < 0.05f)
                return _outlinerXformEuler;
            return live.eulerAngles;
        }

        void StoreOutlinerUiEuler(Atom atom, Vector3 euler)
        {
            if (atom == null) return;
            _outlinerXformEulerUid = atom.uid;
            _outlinerXformEuler = euler;
        }

        float OutlinerReadRotationAxis(Atom atom, int axis)
        {
            Vector3 e = OutlinerUiEuler(atom);
            return axis == 0 ? e.x : (axis == 1 ? e.y : e.z);
        }

        static Vector3 OutlinerMoveAxis(Atom atom, int axis, bool local)
        {
            if (!local || atom == null || atom.mainController == null)
                return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
            Transform t = OutlinerEdits.ControlTransform(atom);
            if (t == null)
                return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
            return axis == 0 ? t.right : (axis == 1 ? t.up : t.forward);
        }

        void NudgeOutlinerPosition(Atom atom, int axis, float sign)
        {
            if (atom == null || atom.mainController == null) return;
            float cur = OutlinerReadPositionAxis(atom, axis);
            SetOutlinerPositionAxis(atom, axis, OutlinerSpringAxis.NudgeToStep(cur, OutlinerMoveStep(), sign));
        }

        void SetOutlinerPositionAxis(Atom atom, int axis, float value)
        {
            if (atom == null || atom.mainController == null) return;
            Vector3 before = OutlinerEdits.ControlTransform(atom).position;
            Vector3 next = before;
            if (axis == 0) next.x = value;
            else if (axis == 1) next.y = value;
            else next.z = value;
            CommitOutlinerPosition(atom, before, next);
        }

        void CommitOutlinerPosition(Atom atom, Vector3 before, Vector3 next)
        {
            bool linked = BeginOutlinerGroupEdit(atom);
            if (!OutlinerEdits.WriteMainControllerPosition(atom, next))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            if (linked) ApplyOutlinerGroupFromPrimary(atom);
            PulseOutlinerTargets();
            _outlinerUndo.Push(atom.uid + "|xform|pos", "Position", before.ToString(), next.ToString());
            _outlinerLastFocused = true;
            RefreshOutlinerTransformFields(atom);
        }

        void NudgeOutlinerRotation(Atom atom, int axis, float sign)
        {
            if (atom == null || atom.mainController == null) return;
            float cur = OutlinerReadRotationAxis(atom, axis);
            SetOutlinerRotationAxis(atom, axis, OutlinerSpringAxis.NudgeToStep(cur, OutlinerRotateStep(), sign));
        }

        void SetOutlinerRotationAxis(Atom atom, int axis, float value)
        {
            if (atom == null || atom.mainController == null) return;
            Vector3 before = OutlinerUiEuler(atom);
            Vector3 next = before;
            if (axis == 0) next.x = value;
            else if (axis == 1) next.y = value;
            else next.z = value;
            CommitOutlinerRotation(atom, before, next);
        }

        void CommitOutlinerRotation(Atom atom, Vector3 beforeEuler, Vector3 nextEuler)
        {
            bool linked = BeginOutlinerGroupEdit(atom);
            if (!OutlinerEdits.WriteMainControllerRotation(atom, nextEuler))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            if (linked) ApplyOutlinerGroupFromPrimary(atom);
            StoreOutlinerUiEuler(atom, nextEuler);
            PulseOutlinerTargets();
            _outlinerUndo.Push(atom.uid + "|xform|rot", "Rotation",
                beforeEuler.ToString(), nextEuler.ToString());
            _outlinerLastFocused = true;
            RefreshOutlinerTransformFields(atom);
        }

        void SyncOutlinerTransformFieldsFromScene()
        {
            if (_outlinerSpringHeldCount > 0) return;
            if (_outlinerPosFields[0] == null) return;
            Atom atom = OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
            if (atom == null || atom.mainController == null) return;
            Vector3 p = OutlinerEdits.ControlTransform(atom).position;
            Vector3 e = OutlinerEdits.ControlTransform(atom).rotation.eulerAngles;
            if (_outlinerXformSyncSet
                && (p - _outlinerXformSyncPos).sqrMagnitude < 1e-8f
                && (e - _outlinerXformSyncEuler).sqrMagnitude < 1e-6f)
                return;
            RefreshOutlinerTransformFields(atom);
        }

        void RefreshOutlinerTransformFields(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            if (!OutlinerIsPrimaryAtom(atom)) return;
            Vector3 p = OutlinerEdits.ControlTransform(atom).position;
            Vector3 e = OutlinerEdits.ControlTransform(atom).rotation.eulerAngles;
            _outlinerXformSyncPos = p;
            _outlinerXformSyncEuler = e;
            _outlinerXformSyncSet = true;
            e = OutlinerUiEuler(atom);
            SetOutlinerFieldText(_outlinerPosFields[0], p.x, OutlinerPosFormat);
            SetOutlinerFieldText(_outlinerPosFields[1], p.y, OutlinerPosFormat);
            SetOutlinerFieldText(_outlinerPosFields[2], p.z, OutlinerPosFormat);
            SetOutlinerFieldText(_outlinerRotFields[0], e.x, OutlinerRotFormat);
            SetOutlinerFieldText(_outlinerRotFields[1], e.y, OutlinerRotFormat);
            SetOutlinerFieldText(_outlinerRotFields[2], e.z, OutlinerRotFormat);
            JSONStorableFloat sp = OutlinerScaleParam(atom);
            if (sp != null) SetOutlinerFieldText(_outlinerScaleField, sp.val, OutlinerScaleFormat);
        }

        static void SetOutlinerFieldText(InputField field, float value, string format)
        {
            if (field == null) return;
            if (field.isFocused) return;
            string next = value.ToString(format);
            if (string.Equals(field.text, next, StringComparison.Ordinal)) return;
            field.text = next;
        }

        void AlignOutlinerToView(Atom atom)
        {
            Camera cam = Camera.main;
            if (cam == null || atom == null || atom.mainController == null) return;
            Vector3 before = OutlinerEdits.ControlTransform(atom).position;
            CommitOutlinerPosition(atom, before, cam.transform.position + cam.transform.forward * 1.2f);
        }

        void DropOutlinerToFloor(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            Vector3 before = OutlinerEdits.ControlTransform(atom).position;
            Vector3 next = before;
            next.y = 0f;
            CommitOutlinerPosition(atom, before, next);
        }

        void FaceOutlinerToView(Atom atom)
        {
            Camera cam = Camera.main;
            if (cam == null || atom == null || atom.mainController == null) return;
            Vector3 self = OutlinerEdits.ControlTransform(atom).position;
            Vector3 toCam = cam.transform.position - self;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) return;
            Vector3 before = OutlinerEdits.ControlTransform(atom).rotation.eulerAngles;
            CommitOutlinerRotation(atom, before, Quaternion.LookRotation(toCam.normalized, Vector3.up).eulerAngles);
        }

        void SnapOutlinerRotation(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            float step = OutlinerRotateStep();
            if (step <= 0f) return;
            Vector3 before = OutlinerUiEuler(atom);
            Vector3 next = new Vector3(
                Mathf.Round(before.x / step) * step,
                Mathf.Round(before.y / step) * step,
                Mathf.Round(before.z / step) * step);
            CommitOutlinerRotation(atom, before, next);
        }

        void ResetOutlinerPosition(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            CommitOutlinerPosition(atom, OutlinerEdits.ControlTransform(atom).position, Vector3.zero);
        }

        void ResetOutlinerRotation(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            CommitOutlinerRotation(atom, OutlinerEdits.ControlTransform(atom).rotation.eulerAngles, Vector3.zero);
        }

        void ResetOutlinerTransform(Atom atom)
        {
            ResetOutlinerPosition(atom);
            ResetOutlinerRotation(atom);
        }

        GameObject AddOutlinerXformBtn(GameObject row, string label, UnityEngine.Events.UnityAction act, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, 0f, h, label,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle, act);
            UI.AddLE(btn, flexibleWidth: 1f, preferredHeight: h, minHeight: h, minWidth: h);
            Text t = btn != null ? btn.GetComponentInChildren<Text>() : null;
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            OutlinerUseInwardHoverRim(btn);
            return btn;
        }

        void AddOutlinerAxisField(GameObject row, float val, System.Action<float> set, float s, float h)
        {
            AddOutlinerNumberField(row, val, "0.##", set, s, h);
        }
    }
}
