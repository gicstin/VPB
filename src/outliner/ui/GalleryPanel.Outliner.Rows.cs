using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        void AddOutlinerParamRowFromQualified(GameObject parent, Atom atom, string qualified, float s)
        {
            if (string.IsNullOrEmpty(qualified) || atom == null) return;
            int slash = qualified.IndexOf('/');
            if (slash <= 0) return;
            string sid = qualified.Substring(0, slash);
            string pid = qualified.Substring(slash + 1);
            JSONStorable st = null;
            try { st = OutlinerEdits.ResolveStorable(atom, sid); } catch { }
            OutlinerParamDescriptor d = OutlinerParamCatalog.DescribeNamed(st, sid, pid);
            if (d != null) AddOutlinerParamRow(parent, atom, d, s);
        }

        static string OutlinerParamCaption(OutlinerParamDescriptor d)
        {
            if (d == null) return "";
            if (!string.IsNullOrEmpty(d.Label)) return d.Label;
            return d.ParamId ?? "";
        }

        static void LayoutOutlinerFlexLabel(Text label, float s, float h)
        {
            if (label == null) return;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(120f * s, h);
            LayoutElement le = label.GetComponent<LayoutElement>();
            if (le == null) le = label.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 48f * s;
            le.preferredWidth = 120f * s;
            le.flexibleWidth = 1f;
            le.minHeight = h;
            le.preferredHeight = h;
            le.flexibleHeight = 0f;
            ClipOutlinerText(label);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void AddOutlinerParamRow(GameObject parent, Atom atom, OutlinerParamDescriptor d, float s)
        {
            if (d == null || parent == null) return;
            float h = OutlinerRowHeight();
            GameObject row = new GameObject("Param_" + d.ParamId);
            row.transform.SetParent(parent.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.ControlGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            string type = "";
            try { type = atom.type; } catch { }
            bool pinned = OutlinerPins.IsPinned(_outlinerPinMap, type, d.QualifiedId);
            float pinSz = OutlinerSlotH(s);
            GameObject pin = UI.CreateFloatChromeIconButton(
                row.transform, pinSz,
                "pin",
                pinned ? GalleryUiColorTokens.ActiveOn : GalleryUiColorTokens.RowIdle,
                () => ToggleOutlinerPin(type, d.QualifiedId));
            if (pin != null)
            {
                UI.AddLE(pin, preferredWidth: pinSz,
                    preferredHeight: pinSz,
                    minWidth: pinSz,
                    minHeight: pinSz,
                    flexibleWidth: 0f, flexibleHeight: 0f);
                AddTooltipPlain(pin, atom.uid + "/" + d.StorableId + "/" + d.ParamId);
            }

            string caption = OutlinerParamCaption(d);
            Text label = UI.CreateLabel(row, caption, GalleryUiDesignTokens.FontBodyRef,
                string.IsNullOrEmpty(d.DisabledReason) ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, richText: false, anchorPreset: AnchorPresets.middleLeft,
                size: new Vector2(120f * s, h), name: "ParamLabel");
            GalleryUiMetrics.ApplyFont(label, GalleryUiDesignTokens.FontBodyRef, s);
            LayoutOutlinerFlexLabel(label, s, h);
            AddTooltipPlain(label.gameObject, caption + "\n" + d.QualifiedId);

            if (!string.IsNullOrEmpty(d.DisabledReason))
            {
                Text why = UI.CreateLabel(row, d.DisabledReason, GalleryUiDesignTokens.FontBodyRef,
                    GalleryUiColorTokens.TextDim, TextAnchor.MiddleRight,
                    HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, raycastTarget: false);
                UI.AddLE(why.gameObject, preferredWidth: 80f * s);
                ClipOutlinerText(why);
                return;
            }

            bool vr = OutlinerVr();
            switch (d.Kind)
            {
                case OutlinerParamKind.Float:
                    if (vr) AddOutlinerFloatStepper(row, atom, d, s);
                    else AddOutlinerFloatSlider(row, atom, d, s);
                    break;
                case OutlinerParamKind.Bool:
                    AddOutlinerBoolToggle(row, atom, d, s);
                    break;
                case OutlinerParamKind.String:
                case OutlinerParamKind.Url:
                    AddOutlinerStringField(row, atom, d, s);
                    break;
                case OutlinerParamKind.StringChooser:
                    AddOutlinerChooser(row, atom, d, s);
                    break;
                case OutlinerParamKind.Color:
                    AddOutlinerColorSwatch(row, atom, d, s);
                    break;
                case OutlinerParamKind.Action:
                    AddOutlinerActionButton(row, atom, d, s);
                    break;
            }
        }

        void ToggleOutlinerPin(string atomType, string qualified)
        {
            if (_outlinerPinMap == null)
                _outlinerPinMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            bool now = !OutlinerPins.IsPinned(_outlinerPinMap, atomType, qualified);
            OutlinerPins.SetPinned(_outlinerPinMap, atomType, qualified, now);
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.OutlinerPinsJson = OutlinerPins.Serialize(_outlinerPinMap);
                VPBConfig.Instance.Save();
            }
            InvalidateOutlinerCards();
            RebuildOutlinerInspector();
        }

        void AddOutlinerFloatSlider(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            JSONStorableFloat p = OutlinerEdits.GetFloat(atom, d.StorableId, d.ParamId);
            if (p == null) return;
            float val = p.val;
            float min = p.min;
            float max = p.max;
            if (Mathf.Approximately(min, max)) { min = val - 1f; max = val + 1f; }

            float rowH = OutlinerSlotH(s);
            float handleW = GalleryUiDesignTokens.OutlinerSliderHandleSizeRef * s;
            float trackEndPad = handleW * 0.5f;

            Slider sliderRef = null;
            Text valueRef = null;
            bool[] echoGuard = new bool[1];
            float step = OutlinerFloatStep(p);
            AddOutlinerRepeatStepButton(row, "−",
                VPBTranslation.T("outliner.param.step_down", "Decrease (hold to repeat)"),
                () => StepOutlinerFloatInline(atom, d, -step, sliderRef, valueRef, echoGuard), s);

            GameObject host = new GameObject("Slider");
            host.transform.SetParent(row.transform, false);
            UI.AddLE(host, flexibleWidth: 1.4f, preferredHeight: rowH, minHeight: rowH,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s,
                flexibleHeight: 0f);
            Slider slider = host.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject hitbox = new GameObject("Hitbox");
            hitbox.transform.SetParent(host.transform, false);
            Image hitImg = UI.AddImage(hitbox, new Color(1f, 1f, 1f, 0f));
            if (hitImg != null) hitImg.raycastTarget = true;
            RectTransform hitRT = hitbox.GetComponent<RectTransform>();
            hitRT.anchorMin = Vector2.zero;
            hitRT.anchorMax = Vector2.one;
            hitRT.sizeDelta = Vector2.zero;
            hitRT.offsetMin = Vector2.zero;
            hitRT.offsetMax = Vector2.zero;

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(host.transform, false);
            Image bgImg = UI.AddImage(bg, GalleryUiColorTokens.SurfaceMid);
            if (bgImg != null) bgImg.raycastTarget = false;
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.28f);
            bgRT.anchorMax = new Vector2(1f, 0.72f);
            bgRT.offsetMin = new Vector2(trackEndPad, 0f);
            bgRT.offsetMax = new Vector2(-trackEndPad, 0f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(host.transform, false);
            RectTransform faRT = fillArea.AddComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.28f);
            faRT.anchorMax = new Vector2(1f, 0.72f);
            faRT.offsetMin = new Vector2(trackEndPad, 0f);
            faRT.offsetMax = new Vector2(-trackEndPad, 0f);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = UI.AddImage(fill, GalleryUiColorTokens.AccentSelected);
            if (fillImg != null) fillImg.raycastTarget = false;
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.sizeDelta = Vector2.zero;
            slider.fillRect = fillRT;

            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(host.transform, false);
            RectTransform haRT = handleArea.AddComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(trackEndPad, 0f);
            haRT.offsetMax = new Vector2(-trackEndPad, 0f);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image himg = UI.AddImage(handle, GalleryUiColorTokens.TextPrimary);
            if (himg != null) himg.raycastTarget = true;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0f, 0f);
            handleRT.anchorMax = new Vector2(0f, 1f);
            handleRT.pivot = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(handleW, 0f);
            slider.handleRect = handleRT;
            slider.targetGraphic = himg;
            slider.value = val;

            Text valTxt = UI.CreateLabel(row, val.ToString("0.##"), GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextMuted, TextAnchor.MiddleCenter);
            ApplyOutlinerScaledFont(valTxt, GalleryUiDesignTokens.FontBodyRef, s);
            UI.AddLE(valTxt.gameObject, preferredWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s,
                preferredHeight: rowH, minHeight: rowH);

            sliderRef = slider;
            valueRef = valTxt;
            AddOutlinerRepeatStepButton(row, "+",
                VPBTranslation.T("outliner.param.step_up", "Increase (hold to repeat)"),
                () => StepOutlinerFloatInline(atom, d, step, sliderRef, valueRef, echoGuard), s);

            string key = atom.uid + "|" + d.StorableId + "|" + d.ParamId;
            slider.onValueChanged.AddListener(v =>
            {
                if (echoGuard[0]) return;
                _outlinerDragParamKey = key;
                _outlinerLastFocused = true;
                float before = p.val;
                if (!OutlinerEdits.WriteFloat(atom, d.StorableId, d.ParamId, v))
                {
                    ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                        "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                    return;
                }
                MirrorOutlinerParamToGroup(atom, d);
                _outlinerUndo.Push(key, d.Label, FormatOutlinerFloat(before), FormatOutlinerFloat(v));
                if (valTxt != null) valTxt.text = v.ToString("0.##");
            });
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        static float OutlinerFloatStep(JSONStorableFloat p)
        {
            if (p == null) return 0.01f;
            float range = p.max - p.min;
            if (range <= 0f || float.IsInfinity(range) || float.IsNaN(range)) return 0.01f;
            float step = range * 0.01f;
            return step < 1e-4f ? 1e-4f : step;
        }

        void AddOutlinerRepeatStepButton(GameObject row, string caption, string tip,
            System.Action act, float s)
        {
            float h = OutlinerSlotH(s);
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, h, h, caption,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle,
                () => { if (act != null) act(); });
            if (btn == null) return;
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            Button b = btn.GetComponent<Button>();
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            OutlinerUseInwardHoverRim(btn);
            OutlinerRepeatButton rpt = btn.AddComponent<OutlinerRepeatButton>();
            rpt.OnRepeat = act;
            AddTooltipPlain(btn, tip);
            Text t = btn.GetComponentInChildren<Text>();
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(t);
        }

        void StepOutlinerFloatInline(Atom atom, OutlinerParamDescriptor d, float delta,
            Slider slider, Text valTxt, bool[] echoGuard)
        {
            JSONStorableFloat p = OutlinerEdits.GetFloat(atom, d.StorableId, d.ParamId);
            if (p == null) return;
            float before = p.val;
            float next = Mathf.Clamp(before + delta, p.min, p.max);
            if (next == before) return;
            if (!OutlinerEdits.WriteFloat(atom, d.StorableId, d.ParamId, next))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                    "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                return;
            }
            MirrorOutlinerParamToGroup(atom, d);
            _outlinerUndo.Push(atom.uid + "|" + d.StorableId + "|" + d.ParamId, d.Label,
                FormatOutlinerFloat(before), FormatOutlinerFloat(next));
            _outlinerLastFocused = true;
            if (slider != null && echoGuard != null)
            {
                echoGuard[0] = true;
                try { slider.value = next; }
                finally { echoGuard[0] = false; }
            }
            if (valTxt != null) valTxt.text = next.ToString("0.##");
            InvalidateOutlinerParamCard(d.StorableId);
        }

        void AddOutlinerFloatStepper(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            JSONStorableFloat p = OutlinerEdits.GetFloat(atom, d.StorableId, d.ParamId);
            if (p == null) return;
            float val = p.val;
            float step = Mathf.Max(0.01f, (d.Max - d.Min) * 0.05f);
            AddOutlinerStepBtn(row, "−−", () => NudgeOutlinerFloat(atom, d, -step * 5f), s);
            AddOutlinerStepBtn(row, "−", () => NudgeOutlinerFloat(atom, d, -step), s);
            Text vtxt = UI.CreateLabel(row, val.ToString("0.##"), GalleryUiDesignTokens.FontBodyRef,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleCenter);
            ApplyOutlinerScaledFont(vtxt, GalleryUiDesignTokens.FontBodyRef, s);
            UI.AddLE(vtxt.gameObject, preferredWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s);
            AddOutlinerStepBtn(row, "+", () => NudgeOutlinerFloat(atom, d, step), s);
            AddOutlinerStepBtn(row, "++", () => NudgeOutlinerFloat(atom, d, step * 5f), s);
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        void AddOutlinerStepBtn(GameObject row, string label, UnityEngine.Events.UnityAction act, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateUIButton(row, h, h, label, GalleryUiDesignTokens.FontBodyRef,
                0f, 0f, AnchorPresets.middleCenter, act);
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h);
        }

        void NudgeOutlinerFloat(Atom atom, OutlinerParamDescriptor d, float delta)
        {
            JSONStorableFloat p = OutlinerEdits.GetFloat(atom, d.StorableId, d.ParamId);
            if (p == null) { return; }
            float before = p.val;
            float next = Mathf.Clamp(before + delta, p.min, p.max);
            string key = atom.uid + "|" + d.StorableId + "|" + d.ParamId;
            OutlinerEdits.WriteFloat(atom, d.StorableId, d.ParamId, next);
            MirrorOutlinerParamToGroup(atom, d);
            _outlinerUndo.Push(key, d.Label, FormatOutlinerFloat(before), FormatOutlinerFloat(next));
            _outlinerLastFocused = true;
            InvalidateOutlinerParamCard(d.StorableId);
            RebuildOutlinerInspector();
        }

        void AddOutlinerBoolToggle(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            bool val = false;
            OutlinerEdits.TryReadBool(atom, d.StorableId, d.ParamId, out val);
            float h = OutlinerSlotH(s);
            float w = h * 2f;
            bool captured = val;
            string caption = captured
                ? VPBTranslation.T("outliner.param.on", "On")
                : VPBTranslation.T("outliner.param.off", "Off");
            Color well = captured ? GalleryUiColorTokens.ActiveOn : GalleryUiColorTokens.RowIdle;
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, w, h, caption,
                GalleryUiDesignTokens.FontBodyRef, well, () =>
                {
                    string key = atom.uid + "|" + d.StorableId + "|" + d.ParamId;
                    bool next = !captured;
                    if (!OutlinerEdits.WriteBool(atom, d.StorableId, d.ParamId, next))
                    {
                        ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                            "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                        return;
                    }
                    MirrorOutlinerParamToGroup(atom, d);
                    _outlinerUndo.Push(key, d.Label, captured ? "1" : "0", next ? "1" : "0");
                    _outlinerLastFocused = true;
                    InvalidateOutlinerParamCard(d.StorableId);
                    RebuildOutlinerInspector();
                });
            UI.AddLE(btn, preferredWidth: w, preferredHeight: h, minWidth: w, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            Button b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            OutlinerUseInwardHoverRim(btn);
            AddTooltipPlain(btn, d.QualifiedId);
            Text bt = btn != null ? btn.GetComponentInChildren<Text>() : null;
            ApplyOutlinerScaledFont(bt, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(bt);
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        void AddOutlinerStringField(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            string val = "";
            try
            {
                JSONStorable st = OutlinerEdits.ResolveStorable(atom, d.StorableId);
                JSONStorableString p = st != null ? st.GetStringJSONParam(d.ParamId) : null;
                if (p != null) val = p.val;
            }
            catch { }
            GameObject fieldGO = UI.CreateTextInput(row, 80f, OutlinerSlotH(s), val,
                GalleryUiDesignTokens.FontBodyRef, 0f, 0f, AnchorPresets.middleRight, null);
            UI.AddLE(fieldGO, flexibleWidth: 1.2f, preferredHeight: OutlinerSlotH(s));
            InputField inf = fieldGO.GetComponent<InputField>();
            if (inf != null)
            {
                inf.text = val;
                StyleOutlinerInputField(inf);
                ApplyOutlinerScaledFont(inf.textComponent, GalleryUiDesignTokens.FontBodyRef, s);
                ApplyOutlinerScaledFont(inf.placeholder as Text, GalleryUiDesignTokens.FontBodyRef, s);
                inf.onEndEdit.AddListener(v =>
                {
                    string key = atom.uid + "|" + d.StorableId + "|" + d.ParamId;
                    OutlinerEdits.WriteString(atom, d.StorableId, d.ParamId, v);
                    MirrorOutlinerParamToGroup(atom, d);
                    _outlinerUndo.Push(key, d.Label, val, v ?? "");
                    _outlinerLastFocused = true;
                });
            }
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        void AddOutlinerChooser(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            List<string> opts = new List<string>();
            if (d.Choices != null)
            {
                for (int i = 0; i < d.Choices.Length; i++) opts.Add(d.Choices[i]);
            }
            if (opts.Count == 0) opts.Add("");
            int idx = 0;
            try
            {
                JSONStorable st = OutlinerEdits.ResolveStorable(atom, d.StorableId);
                JSONStorableStringChooser ch = st != null ? st.GetStringChooserJSONParam(d.ParamId) : null;
                if (ch != null)
                {
                    for (int i = 0; i < opts.Count; i++)
                    {
                        if (string.Equals(opts[i], ch.val, StringComparison.Ordinal))
                        {
                            idx = i;
                            break;
                        }
                    }
                }
            }
            catch { }
            float h = OutlinerSlotH(s);
            string cur = (idx >= 0 && idx < opts.Count) ? opts[idx] : "";
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, 0f, h, cur,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle, () =>
                {
                    int next = (idx + 1) % opts.Count;
                    string key = atom.uid + "|" + d.StorableId + "|" + d.ParamId;
                    if (!OutlinerEdits.WriteChooser(atom, d.StorableId, d.ParamId, opts[next]))
                    {
                        ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                            "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                        return;
                    }
                    MirrorOutlinerParamToGroup(atom, d);
                    _outlinerUndo.Push(key, d.Label, cur, opts[next]);
                    _outlinerLastFocused = true;
                    InvalidateOutlinerParamCard(d.StorableId);
                    RebuildOutlinerInspector();
                });
            UI.AddLE(btn, flexibleWidth: 1f, preferredHeight: h, minHeight: h,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s);
            Text dt = btn != null ? btn.GetComponentInChildren<Text>() : null;
            ApplyOutlinerScaledFont(dt, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(dt);
            OutlinerUseInwardHoverRim(btn);
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        void AddOutlinerColorSwatch(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            float h = OutlinerSlotH(s);
            HSVColor hsv = new HSVColor();
            bool ok = OutlinerEdits.TryReadColor(atom, d.StorableId, d.ParamId, out hsv);
            Color rgb = ok ? Color.HSVToRGB(hsv.H, hsv.S, hsv.V) : GalleryUiColorTokens.RowIdle;
            GameObject sw = UI.CreateUIButton(row, h, h, "", GalleryUiDesignTokens.FontBodyRef,
                0f, 0f, AnchorPresets.middleCenter, null);
            UI.AddLE(sw, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            Image img = sw.GetComponent<Image>();
            if (img != null)
            {
                img.color = rgb;
                img.raycastTarget = false;
            }
            Button sb = sw.GetComponent<Button>();
            if (sb != null) sb.interactable = false;
            if (!ok) return;
            AddOutlinerAxisField(row, hsv.H, nv =>
            {
                hsv.H = Mathf.Clamp01(nv);
                OutlinerEdits.WriteColor(atom, d.StorableId, d.ParamId, hsv);
                MirrorOutlinerParamToGroup(atom, d);
                if (img != null) img.color = Color.HSVToRGB(hsv.H, hsv.S, hsv.V);
            }, s, h);
            AddOutlinerAxisField(row, hsv.S, nv =>
            {
                hsv.S = Mathf.Clamp01(nv);
                OutlinerEdits.WriteColor(atom, d.StorableId, d.ParamId, hsv);
                MirrorOutlinerParamToGroup(atom, d);
                if (img != null) img.color = Color.HSVToRGB(hsv.H, hsv.S, hsv.V);
            }, s, h);
            AddOutlinerAxisField(row, hsv.V, nv =>
            {
                hsv.V = Mathf.Clamp01(nv);
                OutlinerEdits.WriteColor(atom, d.StorableId, d.ParamId, hsv);
                MirrorOutlinerParamToGroup(atom, d);
                if (img != null) img.color = Color.HSVToRGB(hsv.H, hsv.S, hsv.V);
            }, s, h);
            AddOutlinerResetButton(row, () => ResetOutlinerParam(atom, d), s);
        }

        GameObject AddOutlinerResetButton(GameObject row, UnityEngine.Events.UnityAction act, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateFloatChromeIconButton(row.transform, h, "reset",
                GalleryUiColorTokens.ChromeIconWell, act);
            if (btn == null) return null;
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            AddTooltipPlain(btn, VPBTranslation.T("outliner.reset_default", "Reset to default"));
            return btn;
        }

        void ResetOutlinerParam(Atom atom, OutlinerParamDescriptor d)
        {
            if (atom == null || d == null) return;
            bool ok = false;
            if (d.Kind == OutlinerParamKind.Float)
                ok = OutlinerEdits.ResetFloat(atom, d.StorableId, d.ParamId);
            else if (d.Kind == OutlinerParamKind.Bool)
                ok = OutlinerEdits.ResetBool(atom, d.StorableId, d.ParamId);
            else if (d.Kind == OutlinerParamKind.String || d.Kind == OutlinerParamKind.Url)
                ok = OutlinerEdits.ResetString(atom, d.StorableId, d.ParamId);
            else if (d.Kind == OutlinerParamKind.StringChooser)
                ok = OutlinerEdits.ResetChooser(atom, d.StorableId, d.ParamId);
            else if (d.Kind == OutlinerParamKind.Color)
                ok = OutlinerEdits.ResetColor(atom, d.StorableId, d.ParamId);
            if (ok) MirrorOutlinerParamToGroup(atom, d);
            if (!ok)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                    "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                return;
            }
            _outlinerLastFocused = true;
            InvalidateOutlinerParamCard(d.StorableId);
            RebuildOutlinerInspector();
        }

        void AddOutlinerActionButton(GameObject row, Atom atom, OutlinerParamDescriptor d, float s)
        {
            float h = OutlinerSlotH(s);
            float w = h * 2f;
            string caption = VPBTranslation.T("outliner.param.click", "Click");
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, w, h, caption,
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.RowIdle, () =>
                {
                    if (!OutlinerEdits.InvokeAction(atom, d.StorableId, d.ParamId))
                    {
                        ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                            "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                        return;
                    }
                    _outlinerLastFocused = true;
                    if (OutlinerPlugins.IsCreatePluginAction(d.StorableId, d.ParamId))
                        OpenOutlinerPluginWorkflow(atom);
                });
            UI.AddLE(btn, preferredWidth: w, preferredHeight: h, minWidth: w, minHeight: h,
                flexibleWidth: 0f, flexibleHeight: 0f);
            Button b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            OutlinerUseInwardHoverRim(btn);
            AddTooltipPlain(btn, d.QualifiedId);
            Text bt = btn != null ? btn.GetComponentInChildren<Text>() : null;
            ClipOutlinerText(bt);
            ApplyOutlinerScaledFont(bt, GalleryUiDesignTokens.FontBodyRef, s);
        }

        void ToggleOutlinerOn(Atom atom, bool on)
        {
            bool before = true;
            try { before = atom.on; } catch { }
            OutlinerEdits.SetOn(atom, on);
            MirrorOutlinerFactToGroup(atom, "on", on);
            _outlinerUndo.Push(atom.uid + "|on", "On", before ? "1" : "0", on ? "1" : "0");
        }

        void ToggleOutlinerHidden(Atom atom, bool hidden)
        {
            bool before = false;
            try { before = atom.hidden; } catch { }
            OutlinerEdits.SetHidden(atom, hidden);
            MirrorOutlinerFactToGroup(atom, "hidden", hidden);
            _outlinerUndo.Push(atom.uid + "|hidden", "Hidden", before ? "1" : "0", hidden ? "1" : "0");
        }

        void ToggleOutlinerCollision(Atom atom, bool on)
        {
            bool before = true;
            try { before = atom.collisionEnabled; } catch { }
            OutlinerEdits.SetCollision(atom, on);
            MirrorOutlinerFactToGroup(atom, "collision", on);
            _outlinerUndo.Push(atom.uid + "|collision", "Collision", before ? "1" : "0", on ? "1" : "0");
        }

        void CommitOutlinerRename(Atom atom, string next)
        {
            if (atom == null || string.IsNullOrEmpty(next)) return;
            string before = "";
            try { before = atom.uid; } catch { }
            if (string.Equals(before, next, StringComparison.Ordinal)) return;
            if (!OutlinerEdits.Rename(atom, next))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.rename.in_use", "That name is already used."), 2f);
                return;
            }
            _outlinerUndo.Push(before + "|rename", "Rename", before, next);
            _outlinerSelection.SelectOnly(next);
            QueueOutlinerRebuild();
        }

        void ApplyOutlinerUndoRecord(OutlinerUndoRecord rec, bool undo)
        {
            if (rec == null) return;
            if (TryApplyOutlinerBatchOnUndo(rec, undo)) return;
            string payload = undo ? rec.Before : rec.After;
            string[] parts = rec.Key.Split('|');
            if (parts.Length < 2) return;
            string uid = parts[0];
            if (string.Equals(parts[1], "delete", StringComparison.Ordinal))
            {
                if (undo) StartCoroutine(RestoreOutlinerDeletedAtom(rec.Before));
                else OutlinerEdits.Delete(OutlinerEdits.GetAtom(uid));
                QueueOutlinerRebuild();
                return;
            }
            Atom atom = OutlinerEdits.GetAtom(uid);
            if (atom == null && parts.Length >= 2 && string.Equals(parts[1], "rename", StringComparison.Ordinal))
                atom = OutlinerEdits.GetAtom(undo ? rec.After : rec.Before);
            if (atom == null) return;
            if (parts.Length == 2)
            {
                string fact = parts[1];
                bool bit = payload == "1";
                if (fact == "on") OutlinerEdits.SetOn(atom, bit);
                else if (fact == "hidden") OutlinerEdits.SetHidden(atom, bit);
                else if (fact == "collision") OutlinerEdits.SetCollision(atom, bit);
                else if (fact == "rename") OutlinerEdits.Rename(atom, payload);
                else if (fact == "parent")
                {
                    Atom p = string.IsNullOrEmpty(payload) ? null : OutlinerEdits.GetAtom(payload);
                    OutlinerEdits.SetParent(atom, p);
                }
                QueueOutlinerRebuild();
                return;
            }
            if (parts.Length >= 3)
            {
                string sid = parts[1];
                string pid = parts[2];
                if (string.Equals(sid, "xform", StringComparison.Ordinal))
                {
                    Vector3 v;
                    if (TryParseOutlinerVector3(payload, out v))
                    {
                        if (string.Equals(pid, "pos", StringComparison.Ordinal))
                            OutlinerEdits.WriteMainControllerPosition(atom, v);
                        else if (string.Equals(pid, "rot", StringComparison.Ordinal))
                        {
                            OutlinerEdits.WriteMainControllerRotation(atom, v);
                            StoreOutlinerUiEuler(atom, v);
                        }
                    }
                    RefreshOutlinerTransformFields(atom);
                    RebuildOutlinerInspector();
                    return;
                }
                bool wasBool;
                float f;
                if ((payload == "0" || payload == "1")
                    && OutlinerEdits.TryReadBool(atom, sid, pid, out wasBool))
                    OutlinerEdits.WriteBool(atom, sid, pid, payload == "1");
                else if (TryParseOutlinerFloat(payload, out f)
                    && OutlinerEdits.GetFloat(atom, sid, pid) != null)
                    OutlinerEdits.WriteFloat(atom, sid, pid, f);
                else
                    OutlinerEdits.WriteString(atom, sid, pid, payload);
                if (OutlinerLookPreview.IsWornParam(sid, pid)) QueueOutlinerLookRefresh();
                InvalidateOutlinerParamCard(sid);
            }
            RebuildOutlinerInspector();
        }

        IEnumerator RestoreOutlinerDeletedAtom(string json)
        {
            yield return StartCoroutine(OutlinerEdits.RestoreAtomRoutine(json));
            QueueOutlinerRebuild();
            RebuildOutlinerInspector();
        }

        internal static string FormatOutlinerVector3(Vector3 value)
        {
            return "(" + FormatOutlinerFloat(value.x) + ", " + FormatOutlinerFloat(value.y) + ", " + FormatOutlinerFloat(value.z) + ")";
        }

        internal static string FormatOutlinerFloat(float value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static bool TryParseOutlinerFloat(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            return float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        internal static bool TryParseOutlinerVector3(string text, out Vector3 value)
        {
            value = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            int open = text.IndexOf('(');
            int close = text.LastIndexOf(')');
            string body = open >= 0 && close > open
                ? text.Substring(open + 1, close - open - 1)
                : text;
            string[] bits = body.Split(',');
            if (bits.Length < 3) return false;
            float x, y, z;
            if (!TryParseOutlinerFloat(bits[0], out x)) return false;
            if (!TryParseOutlinerFloat(bits[1], out y)) return false;
            if (!TryParseOutlinerFloat(bits[2], out z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }
    }
}
