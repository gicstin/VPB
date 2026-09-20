using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal static class OutlinerParamCatalog
    {
        internal const int MaxParamsPerStorable = 48;
        internal const int MaxCollectedPerStorable = 256;
        internal const string AtomStorableId = "atom";

        internal const string PluginStorableId = "PluginManager";

        static readonly string[] PersonAllow =
        {
            PluginStorableId, "control", AtomStorableId, "geometry", "CollisionTrigger"
        };

        static readonly string[] LightAllow = { PluginStorableId, "Light", AtomStorableId, "control" };
        static readonly string[] AudioAllow = { PluginStorableId, "AudioSource", AtomStorableId, "control" };
        static readonly string[] CameraAllow = { PluginStorableId, "CameraControl", AtomStorableId, "control" };
        static readonly string[] CuaAllow =
        {
            PluginStorableId, "asset", "CustomUnityAsset", AtomStorableId, "control"
        };
        static readonly string[] DefaultAllow = { PluginStorableId, "control", AtomStorableId };

        static readonly string[] RedundantStorables = { "rescaleObject", "scale" };

        static readonly string[] HeaderOwnedAtomParams = { "on", "hidden", "collisionEnabled" };

        static readonly string[] ActionMirrorPrefixes = { "Toggle", "Set", "Turn" };

        static readonly string[] ActionMirrorSuffixes = { "", "On", "Off", "True", "False", "Toggle" };

        internal static bool HasEditableParams(List<OutlinerParamDescriptor> list)
        {
            if (list == null || list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                OutlinerParamDescriptor d = list[i];
                if (d == null) continue;
                if (d.Kind == OutlinerParamKind.Disabled) continue;
                if (!string.IsNullOrEmpty(d.DisabledReason)) continue;
                return true;
            }
            return false;
        }

        static readonly string[] NoiseExact =
        {
            "lastRelativeVelocity",
            "recentMaxVolume",
            "resetPhysicsProgress",
            "keepParamLocksWhenPuttingBackInPool",
            "physicsMorphUid",
            "physicsMouthOpenMorphUid",
            "physicsVisemeName",
            "physicsVisemeWeight"
        };

        internal static bool IsNoiseParam(string paramId)
        {
            if (string.IsNullOrEmpty(paramId)) return true;
            if (IsPresetSlotParam(paramId)) return true;
            if (StartsWithOrdinal(paramId, "CopyToClipboard")) return true;
            if (StartsWithOrdinal(paramId, "LoadFromClipboard")) return true;
            if (StartsWithOrdinal(paramId, "PasteFrom")) return true;
            for (int i = 0; i < NoiseExact.Length; i++)
            {
                if (string.Equals(NoiseExact[i], paramId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static bool IsHeaderOwnedParam(string storableId, string paramId)
        {
            if (string.IsNullOrEmpty(paramId)) return false;
            if (!string.Equals(storableId, AtomStorableId, StringComparison.OrdinalIgnoreCase)) return false;
            for (int i = 0; i < HeaderOwnedAtomParams.Length; i++)
            {
                if (string.Equals(HeaderOwnedAtomParams[i], paramId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool IsHiddenParam(string storableId, string paramId)
        {
            return IsNoiseParam(paramId) || IsHeaderOwnedParam(storableId, paramId);
        }

        internal static bool ActionMirrorsParam(string actionId, string paramId)
        {
            if (string.IsNullOrEmpty(actionId) || string.IsNullOrEmpty(paramId)) return false;
            if (actionId.Length <= paramId.Length) return false;
            for (int p = 0; p <= ActionMirrorPrefixes.Length; p++)
            {
                string pre = p == 0 ? "" : ActionMirrorPrefixes[p - 1];
                if (pre.Length > 0 && !StartsWithOrdinal(actionId, pre)) continue;
                int tail = actionId.Length - pre.Length - paramId.Length;
                if (tail < 0) continue;
                if (string.Compare(actionId, pre.Length, paramId, 0, paramId.Length,
                        StringComparison.OrdinalIgnoreCase) != 0) continue;
                for (int s = 0; s < ActionMirrorSuffixes.Length; s++)
                {
                    string suf = ActionMirrorSuffixes[s];
                    if (suf.Length != tail) continue;
                    if (tail == 0) return pre.Length > 0;
                    if (string.Compare(actionId, actionId.Length - tail, suf, 0, tail,
                            StringComparison.OrdinalIgnoreCase) == 0) return true;
                }
            }
            return false;
        }

        internal static bool IsPresetSlotParam(string paramId)
        {
            if (string.IsNullOrEmpty(paramId)) return false;
            if (StartsWithOrdinal(paramId, "SaveToStore")) return true;
            if (!StartsWithOrdinal(paramId, "Restore")) return false;
            if (paramId.IndexOf("FromStore", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (paramId.EndsWith("FromDefaults", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool IsRedundantStorable(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return false;
            for (int i = 0; i < RedundantStorables.Length; i++)
            {
                if (string.Equals(RedundantStorables[i], storableId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool EnumeratesFloatParams(JSONStorable st, string storableId)
        {
            if (st is DAZCharacterSelector) return false;
            if (string.IsNullOrEmpty(storableId)) return true;
            if (string.Equals(storableId, "geometry", StringComparison.OrdinalIgnoreCase)) return false;
            if (storableId.IndexOf("morph", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        static bool StartsWithOrdinal(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] AllowlistForType(string atomType)
        {
            if (SceneUtils.IsPersonLikeAtomType(atomType)) return PersonAllow;
            if (SceneUtils.IsLightAtomType(atomType)) return LightAllow;
            if (SceneUtils.IsSoundAtomType(atomType)) return AudioAllow;
            if (string.Equals(atomType, "WindowCamera", StringComparison.Ordinal)
                || string.Equals(atomType, "CustomUnityAsset", StringComparison.Ordinal))
            {
                if (string.Equals(atomType, "CustomUnityAsset", StringComparison.Ordinal))
                    return CuaAllow;
                return CameraAllow;
            }
            return DefaultAllow;
        }

        internal static List<OutlinerParamDescriptor> FromNameLists(
            string storableId,
            string[] floatNames,
            string[] boolNames,
            string[] stringNames,
            string[] chooserNames,
            string[] colorNames,
            string[] urlNames,
            string[] actionNames,
            bool throwOnAccess)
        {
            var list = new List<OutlinerParamDescriptor>(16);
            if (throwOnAccess)
            {
                var bad = new OutlinerParamDescriptor();
                bad.StorableId = storableId ?? "";
                bad.ParamId = "";
                bad.Kind = OutlinerParamKind.Disabled;
                bad.Label = storableId ?? "";
                bad.DisabledReason = "Storable threw on access";
                list.Add(bad);
                return list;
            }

            AddNames(list, storableId, floatNames, OutlinerParamKind.Float);
            AddNames(list, storableId, boolNames, OutlinerParamKind.Bool);
            AddNames(list, storableId, stringNames, OutlinerParamKind.String);
            AddNames(list, storableId, chooserNames, OutlinerParamKind.StringChooser);
            AddNames(list, storableId, colorNames, OutlinerParamKind.Color);
            AddNames(list, storableId, urlNames, OutlinerParamKind.Url);
            AddNames(list, storableId, actionNames, OutlinerParamKind.Action);
            Prioritize(list, storableId);
            return list;
        }

        static void AddNames(
            List<OutlinerParamDescriptor> list,
            string storableId,
            string[] names,
            OutlinerParamKind kind)
        {
            if (names == null) return;
            for (int i = 0; i < names.Length; i++)
            {
                OutlinerParamDescriptor d = Make(storableId, names[i], kind);
                if (d != null) list.Add(d);
            }
        }

        static OutlinerParamDescriptor Make(string storableId, string paramId, OutlinerParamKind kind)
        {
            if (string.IsNullOrEmpty(paramId)) return null;
            if (IsHiddenParam(storableId, paramId)) return null;
            var d = new OutlinerParamDescriptor();
            d.StorableId = storableId ?? "";
            d.ParamId = paramId;
            d.Kind = kind;
            d.Label = OutlinerParamLabels.Humanize(paramId);
            return d;
        }

        internal static void Prioritize(List<OutlinerParamDescriptor> list, string storableId)
        {
            if (list == null || list.Count == 0) return;
            DropDuplicateForms(list);
            for (int i = 0; i < list.Count; i++)
            {
                OutlinerParamDescriptor d = list[i];
                if (d == null) continue;
                d.Order = OutlinerParamPriority.Rank(storableId, d.ParamId, d.Kind, i);
            }
            OutlinerParamPriority.Sort(list);
            if (list.Count > MaxParamsPerStorable)
                list.RemoveRange(MaxParamsPerStorable, list.Count - MaxParamsPerStorable);
        }

        internal static void DropDuplicateForms(List<OutlinerParamDescriptor> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                OutlinerParamDescriptor d = list[i];
                if (d == null)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (DuplicatesEarlierEntry(list, i, d)) list.RemoveAt(i);
            }
        }

        static bool DuplicatesEarlierEntry(List<OutlinerParamDescriptor> list, int index,
            OutlinerParamDescriptor d)
        {
            for (int j = 0; j < index; j++)
            {
                OutlinerParamDescriptor other = list[j];
                if (other == null) continue;
                if (string.Equals(other.ParamId, d.ParamId, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (d.Kind != OutlinerParamKind.Action) continue;
                if (other.Kind == OutlinerParamKind.Action) continue;
                if (ActionMirrorsParam(d.ParamId, other.ParamId)) return true;
            }
            return false;
        }

        internal static bool HasVisibleParams(JSONStorable st, string storableId)
        {
            if (st == null) return false;
            if (IsRedundantStorable(storableId)) return false;
            if (EnumeratesFloatParams(st, storableId)
                && HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.Float))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.Bool))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.StringChooser))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.Color))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.Url))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.String))) return true;
            if (HasVisibleName(storableId, SafeNames(st, OutlinerParamKind.Action))) return true;
            return false;
        }

        static List<string> SafeNames(JSONStorable st, OutlinerParamKind kind)
        {
            try
            {
                switch (kind)
                {
                    case OutlinerParamKind.Float: return st.GetFloatParamNames();
                    case OutlinerParamKind.Bool: return st.GetBoolParamNames();
                    case OutlinerParamKind.String: return st.GetStringParamNames();
                    case OutlinerParamKind.StringChooser: return st.GetStringChooserParamNames();
                    case OutlinerParamKind.Color: return st.GetColorParamNames();
                    case OutlinerParamKind.Url: return st.GetUrlParamNames();
                    case OutlinerParamKind.Action: return st.GetActionNames();
                }
            }
            catch { }
            return null;
        }

        static bool HasVisibleName(string storableId, List<string> names)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (!IsHiddenParam(storableId, names[i])) return true;
            }
            return false;
        }

        internal static OutlinerParamDescriptor DescribeNamed(JSONStorable st, string storableId, string paramId)
        {
            if (st == null || string.IsNullOrEmpty(paramId) || IsNoiseParam(paramId)) return null;
            string sid = storableId ?? "";
            try
            {
                JSONStorableFloat fl = st.GetFloatJSONParam(paramId);
                if (fl != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.Float;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    d.Min = fl.min;
                    d.Max = fl.max;
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableBool bo = st.GetBoolJSONParam(paramId);
                if (bo != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.Bool;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableStringChooser ch = st.GetStringChooserJSONParam(paramId);
                if (ch != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.StringChooser;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    if (ch.choices != null)
                    {
                        d.Choices = new string[ch.choices.Count];
                        for (int c = 0; c < ch.choices.Count; c++)
                            d.Choices[c] = ch.choices[c];
                    }
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableString str = st.GetStringJSONParam(paramId);
                if (str != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.String;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableColor col = st.GetColorJSONParam(paramId);
                if (col != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.Color;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableUrl url = st.GetUrlJSONParam(paramId);
                if (url != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.Url;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    return d;
                }
            }
            catch { }
            try
            {
                JSONStorableAction act = st.GetAction(paramId);
                if (act != null)
                {
                    var d = new OutlinerParamDescriptor();
                    d.StorableId = sid;
                    d.ParamId = paramId;
                    d.Kind = OutlinerParamKind.Action;
                    d.Label = OutlinerParamLabels.Humanize(paramId);
                    return d;
                }
            }
            catch { }
            return null;
        }

        internal static List<OutlinerParamDescriptor> FromStorable(JSONStorable st, string storableId)
        {
            var list = new List<OutlinerParamDescriptor>(16);
            if (st == null)
            {
                var bad = new OutlinerParamDescriptor();
                bad.StorableId = storableId ?? "";
                bad.Kind = OutlinerParamKind.Disabled;
                bad.DisabledReason = "Missing storable";
                list.Add(bad);
                return list;
            }

            try
            {
                if (EnumeratesFloatParams(st, storableId))
                    AddFromFloats(list, st, storableId);
                AddFromBools(list, st, storableId);
                AddFromStrings(list, st, storableId);
                AddFromChoosers(list, st, storableId);
                AddFromColors(list, st, storableId);
                AddFromUrls(list, st, storableId);
                AddFromActions(list, st, storableId);
                Prioritize(list, storableId);
            }
            catch (Exception)
            {
                list.Clear();
                var bad = new OutlinerParamDescriptor();
                bad.StorableId = storableId ?? "";
                bad.Kind = OutlinerParamKind.Disabled;
                bad.DisabledReason = "Storable threw on access";
                list.Add(bad);
            }
            return list;
        }

        static void AddFromFloats(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetFloatParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableFloat p = null;
                try { p = st.GetFloatJSONParam(n); } catch { p = null; }
                if (p == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.Float;
                d.Label = OutlinerParamLabels.Humanize(n);
                d.Min = p.min;
                d.Max = p.max;
                list.Add(d);
            }
        }

        static void AddFromBools(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetBoolParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableBool p = null;
                try { p = st.GetBoolJSONParam(n); } catch { p = null; }
                if (p == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.Bool;
                d.Label = OutlinerParamLabels.Humanize(n);
                list.Add(d);
            }
        }

        static void AddFromStrings(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetStringParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableString p = null;
                try { p = st.GetStringJSONParam(n); } catch { p = null; }
                if (p == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.String;
                d.Label = OutlinerParamLabels.Humanize(n);
                list.Add(d);
            }
        }

        static void AddFromChoosers(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetStringChooserParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableStringChooser ch = null;
                try { ch = st.GetStringChooserJSONParam(n); } catch { ch = null; }
                if (ch == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.StringChooser;
                d.Label = OutlinerParamLabels.Humanize(n);
                if (ch.choices != null)
                {
                    d.Choices = new string[ch.choices.Count];
                    for (int c = 0; c < ch.choices.Count; c++)
                        d.Choices[c] = ch.choices[c];
                }
                list.Add(d);
            }
        }

        static void AddFromColors(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetColorParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableColor p = null;
                try { p = st.GetColorJSONParam(n); } catch { p = null; }
                if (p == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.Color;
                d.Label = OutlinerParamLabels.Humanize(n);
                list.Add(d);
            }
        }

        static void AddFromUrls(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetUrlParamNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableUrl p = null;
                try { p = st.GetUrlJSONParam(n); } catch { p = null; }
                if (p == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.Url;
                d.Label = OutlinerParamLabels.Humanize(n);
                list.Add(d);
            }
        }

        static void AddFromActions(List<OutlinerParamDescriptor> list, JSONStorable st, string storableId)
        {
            List<string> names = null;
            try { names = st.GetActionNames(); } catch { }
            if (names == null) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (list.Count >= MaxCollectedPerStorable) return;
                string n = names[i];
                if (string.IsNullOrEmpty(n) || IsHiddenParam(storableId, n)) continue;
                JSONStorableAction act = null;
                try { act = st.GetAction(n); } catch { act = null; }
                if (act == null || act.actionCallback == null) continue;
                var d = new OutlinerParamDescriptor();
                d.StorableId = storableId;
                d.ParamId = n;
                d.Kind = OutlinerParamKind.Action;
                d.Label = OutlinerParamLabels.Humanize(n);
                list.Add(d);
            }
        }
    }
}
