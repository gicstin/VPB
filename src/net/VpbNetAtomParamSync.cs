using System;
using System.Collections.Generic;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetAtomParamBatch
    {
        public readonly string[] Uid = new string[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly string[] Storable = new string[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly string[] Name = new string[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly string[] Text = new string[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly byte[] Kind = new byte[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly float[] Number = new float[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly bool[] Switch = new bool[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly float[] H = new float[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly float[] S = new float[VpbNetEventLimits.MaxParamsPerEvent];
        public readonly float[] V = new float[VpbNetEventLimits.MaxParamsPerEvent];

        public int Count;

        public void Clear()
        {
            Count = 0;
        }
    }

    // JSONStorable values only — whitelist refuses plugin/preset/path by name on both sides.
    public sealed class VpbNetAtomParamSync
    {
        public const float HoldOffSeconds = 0.35f;
        public const float RescanSeconds = 5f;
        public const int MaxAtomsBuiltPerPoll = 2;
        public const int MaxParamsPerAtom = 96;
        public const int MaxTrackedParams = 4096;
        public const float FloatEpsilon = 1e-4f;
        public const float ColorEpsilon = 1e-4f;

        sealed class Ref
        {
            public string Storable;
            public string Name;
            public byte Kind;
            public JSONStorableFloat F;
            public JSONStorableBool B;
            public JSONStorableColor C;
            public JSONStorableStringChooser Ch;
            public JSONStorableString T;
            // Skyshop style dropdown is a UIPopup writing skyName, not a JSONStorable.
            public SkyshopLightController Sky;
            public float LastF;
            public bool LastB;
            public float LastH;
            public float LastS;
            public float LastV;
            public string LastText;
            public float HoldUntil;
            // Push scene lighting once after bind; ordinary lamps stay diff-only (same scene JSON).
            public bool Pending;
        }

        sealed class Slot
        {
            public Atom Atom;
            public string Uid;
            public List<Ref> Params;
            public bool Built;
        }

        readonly Dictionary<string, Slot> _byUid = new Dictionary<string, Slot>(64, StringComparer.Ordinal);
        readonly List<Slot> _slots = new List<Slot>(64);
        readonly List<Slot> _build = new List<Slot>(64);
        readonly List<Ref> _sentRef = new List<Ref>(VpbNetEventLimits.MaxParamsPerEvent);

        Atom _localAvatar;
        Atom _remoteAvatar;
        bool _bound;
        bool _listDirty = true;
        float _nextRescan;
        int _tracked;
        int _sent;
        int _applied;
        int _refused;
        int _missing;
        int _scanCursor;

        public bool IsBound { get { return _bound; } }
        public int Watched { get { return _slots.Count; } }
        public int Tracked { get { return _tracked; } }
        public int PendingBuilds { get { return _build.Count; } }
        public int Sent { get { return _sent; } }
        public int Applied { get { return _applied; } }
        public int Refused { get { return _refused; } }
        public int Missing { get { return _missing; } }

        public void Bind(Atom localAvatar, Atom remoteAvatar)
        {
            Unbind();
            _localAvatar = localAvatar;
            _remoteAvatar = remoteAvatar;
            _bound = true;
            _listDirty = true;
            _nextRescan = 0f;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                sc.onAtomAddedHandlers -= OnAtomChanged;
                sc.onAtomAddedHandlers += OnAtomChanged;
                sc.onAtomRemovedHandlers -= OnAtomChanged;
                sc.onAtomRemovedHandlers += OnAtomChanged;
            }
            catch { }
        }

        public void Unbind()
        {
            if (_bound)
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    try
                    {
                        sc.onAtomAddedHandlers -= OnAtomChanged;
                        sc.onAtomRemovedHandlers -= OnAtomChanged;
                    }
                    catch { }
                }
            }

            _bound = false;
            _localAvatar = null;
            _remoteAvatar = null;
            _byUid.Clear();
            _slots.Clear();
            _build.Clear();
            _sentRef.Clear();
            _tracked = 0;
            _scanCursor = 0;
            _listDirty = true;
        }

        void OnAtomChanged(Atom a)
        {
            _listDirty = true;
        }

        public void RefreshNow()
        {
            _listDirty = true;
            RefreshSlots();
        }

        void RefreshSlots()
        {
            float now = Time.realtimeSinceStartup;
            if (!_listDirty && now < _nextRescan) return;
            _listDirty = false;
            _nextRescan = now + RescanSeconds;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            List<Atom> all = null;
            try { all = sc.GetAtoms(); }
            catch { }
            if (all == null) return;

            _slots.Clear();

            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (VpbNetPropSync.ParamExclusion(a, _localAvatar, _remoteAvatar) != VpbNetPropExclusion.None)
                    continue;

                string uid = null;
                try { uid = a.uid; }
                catch { }
                if (uid == null || !VpbNetPropFrame.IsSendableUid(uid)) continue;

                Slot s;
                if (!_byUid.TryGetValue(uid, out s) || s.Atom != a)
                {
                    s = new Slot();
                    s.Atom = a;
                    s.Uid = uid;
                    s.Params = new List<Ref>(16);
                    s.Built = false;
                    _byUid[uid] = s;
                    _build.Add(s);
                }

                _slots.Add(s);
            }

            PruneDeadSlots();
        }

        void PruneDeadSlots()
        {
            if (_byUid.Count <= _slots.Count) return;

            for (int i = _build.Count - 1; i >= 0; i--)
            {
                if (_build[i].Atom != null) continue;
                _build.RemoveAt(i);
            }

            // Rebuild the dict — cannot walk it while writing (dead slots).
            _tracked = 0;
            Dictionary<string, Slot> keep = new Dictionary<string, Slot>(_slots.Count + 8, StringComparer.Ordinal);
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                keep[s.Uid] = s;
                _tracked += s.Params.Count;
            }
            _byUid.Clear();
            foreach (KeyValuePair<string, Slot> kv in keep) _byUid[kv.Key] = kv.Value;
        }

        void DrainBuilds()
        {
            int budget = MaxAtomsBuiltPerPoll;
            while (budget > 0 && _build.Count > 0)
            {
                Slot s = _build[_build.Count - 1];
                _build.RemoveAt(_build.Count - 1);
                budget--;
                BuildSlot(s);
            }
        }

        void BuildSlot(Slot s)
        {
            if (s == null || s.Built) return;
            s.Built = true;
            s.Params.Clear();

            Atom a = s.Atom;
            if (a == null) return;
            if (_tracked >= MaxTrackedParams) return;

            List<string> ids = null;
            try { ids = a.GetStorableIDs(); }
            catch { }
            if (ids == null) return;

            bool core = VpbNetStorableWhitelist.IsCoreControlUid(s.Uid);
            for (int i = 0; i < ids.Count && s.Params.Count < MaxParamsPerAtom; i++)
            {
                string id = ids[i];
                if (id == null) continue;
                if (VpbNetStorableWhitelist.IsDeniedParamStorable(id)) continue;

                JSONStorable js = null;
                try { js = a.GetStorableByID(id); }
                catch { }
                if (js == null) continue;

                bool lighting = IsLighting(js, id);
                if (core && !lighting) continue;

                AddFloats(s, js, id);
                AddBools(s, js, id);
                AddColors(s, js, id);
                AddChoosers(s, js, id);
                AddTexts(s, js, id);
                AddUrls(s, js, id);
                AddSkyProps(s, js, id);
            }

            if (core)
            {
                for (int i = 0; i < s.Params.Count; i++) s.Params[i].Pending = true;
                string first = s.Params.Count > 0 ? s.Params[0].Storable : null;
                LogUtil.LogWarning("[VPB.Net] scene lighting on " + s.Uid + ": "
                    + s.Params.Count + " values tracked"
                    + (first != null ? " (first storable " + first + ")" : " - sky and exposure stay local"));
            }

            _tracked += s.Params.Count;
        }

        static bool IsLighting(JSONStorable js, string id)
        {
            if (VpbNetStorableWhitelist.IsSceneLightingStorable(id)) return true;
            try { return js is SkyshopLightController; }
            catch { return false; }
        }

        bool Accepts(Slot s, JSONStorable js, string id, string name)
        {
            if (name == null) return false;
            if (s.Params.Count >= MaxParamsPerAtom) return false;
            if (!ParamAllowed(s.Uid, js, id, name)) return false;
            try
            {
                if (js.IsUrlJSONParam(name) && !IsLighting(js, id))
                    return false;
            }
            catch { }
            return true;
        }

        static bool ParamAllowed(string uid, JSONStorable js, string id, string name)
        {
            if (VpbNetStorableWhitelist.IsAllowedAtomParam(uid, id, name)) return true;
            bool sky = false;
            try { sky = js is SkyshopLightController; }
            catch { }
            if (!sky) return false;
            return VpbNetStorableWhitelist.IsAllowedAtomParam(uid, "GlobalLighting", name);
        }

        void AddFloats(Slot s, JSONStorable js, string id)
        {
            List<string> names = null;
            try { names = js.GetFloatParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableFloat p = null;
                try { p = js.GetFloatJSONParam(n); }
                catch { }
                if (p == null) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Float;
                r.F = p;
                try { r.LastF = p.val; }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddBools(Slot s, JSONStorable js, string id)
        {
            List<string> names = null;
            try { names = js.GetBoolParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableBool p = null;
                try { p = js.GetBoolJSONParam(n); }
                catch { }
                if (p == null) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Bool;
                r.B = p;
                try { r.LastB = p.val; }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddColors(Slot s, JSONStorable js, string id)
        {
            List<string> names = null;
            try { names = js.GetColorParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableColor p = null;
                try { p = js.GetColorJSONParam(n); }
                catch { }
                if (p == null) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Color;
                r.C = p;
                try
                {
                    HSVColor c = p.val;
                    r.LastH = c.H;
                    r.LastS = c.S;
                    r.LastV = c.V;
                }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddChoosers(Slot s, JSONStorable js, string id)
        {
            List<string> names = null;
            try { names = js.GetStringChooserParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableStringChooser p = null;
                try { p = js.GetStringChooserJSONParam(n); }
                catch { }
                if (p == null) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Chooser;
                r.Ch = p;
                try { r.LastText = p.val; }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddTexts(Slot s, JSONStorable js, string id)
        {
            List<string> names = null;
            try { names = js.GetStringParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableString p = null;
                try { p = js.GetStringJSONParam(n); }
                catch { }
                if (p == null) continue;
                if (p is JSONStorableUrl) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Text;
                r.T = p;
                try { r.LastText = p.val; }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddUrls(Slot s, JSONStorable js, string id)
        {
            if (!IsLighting(js, id)) return;

            List<string> names = null;
            try { names = js.GetUrlParamNames(); }
            catch { }
            if (names == null) return;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (!Accepts(s, js, id, n)) continue;

                JSONStorableUrl p = null;
                try { p = js.GetUrlJSONParam(n); }
                catch { }
                if (p == null) continue;

                Ref r = new Ref();
                r.Storable = id;
                r.Name = n;
                r.Kind = VpbNetAtomParamKind.Text;
                r.T = p;
                try { r.LastText = p.val; }
                catch { continue; }
                s.Params.Add(r);
            }
        }

        void AddSkyProps(Slot s, JSONStorable js, string id)
        {
            SkyshopLightController sky = js as SkyshopLightController;
            if (sky == null) return;

            if (Accepts(s, js, id, "skyName"))
            {
                Ref r = new Ref();
                r.Storable = id;
                r.Name = "skyName";
                r.Kind = VpbNetAtomParamKind.Chooser;
                r.Sky = sky;
                try { r.LastText = sky.skyName ?? string.Empty; }
                catch { r.LastText = string.Empty; }
                s.Params.Add(r);
            }

            if (Accepts(s, js, id, "url"))
            {
                Ref r = new Ref();
                r.Storable = id;
                r.Name = "url";
                r.Kind = VpbNetAtomParamKind.Text;
                r.Sky = sky;
                try { r.LastText = sky.url ?? string.Empty; }
                catch { r.LastText = string.Empty; }
                s.Params.Add(r);
            }
        }

        // Ring walk — restarting at atom 0 starves everything behind a dragged first lamp.
        public int Collect(VpbNetAtomParamBatch batch, int cap)
        {
            batch.Clear();
            _sentRef.Clear();
            if (!_bound) return 0;

            RefreshSlots();
            DrainBuilds();

            if (cap <= 0 || _slots.Count == 0) return 0;
            if (IsSceneLoading()) return 0;

            float now = Time.realtimeSinceStartup;
            if (_scanCursor >= _slots.Count) _scanCursor = 0;
            int start = _scanCursor;

            for (int step = 0; step < _slots.Count && batch.Count < cap; step++)
            {
                int at = start + step;
                if (at >= _slots.Count) at -= _slots.Count;

                Slot s = _slots[at];
                if (s == null || s.Atom == null || !s.Built) continue;

                for (int i = 0; i < s.Params.Count && batch.Count < cap; i++)
                {
                    Ref r = s.Params[i];
                    if (now < r.HoldUntil) continue;
                    if (!Changed(r, batch, s.Uid)) continue;
                    _sentRef.Add(r);
                    _scanCursor = at;
                }
            }

            return batch.Count;
        }

        bool Changed(Ref r, VpbNetAtomParamBatch batch, string uid)
        {
            int n = batch.Count;

            if (r.Kind == VpbNetAtomParamKind.Float)
            {
                if (r.F == null) return false;
                float v;
                try { v = r.F.val; }
                catch { return false; }
                if (float.IsNaN(v) || float.IsInfinity(v)) return false;

                float tol = FloatEpsilon;
                float scale = Math.Abs(r.LastF) * FloatEpsilon;
                if (scale > tol) tol = scale;
                if (!r.Pending && Math.Abs(v - r.LastF) <= tol) return false;

                batch.Number[n] = v;
            }
            else if (r.Kind == VpbNetAtomParamKind.Bool)
            {
                if (r.B == null) return false;
                bool v;
                try { v = r.B.val; }
                catch { return false; }
                if (!r.Pending && v == r.LastB) return false;

                batch.Switch[n] = v;
            }
            else if (r.Kind == VpbNetAtomParamKind.Color)
            {
                if (r.C == null) return false;
                HSVColor c;
                try { c = r.C.val; }
                catch { return false; }
                if (float.IsNaN(c.H) || float.IsNaN(c.S) || float.IsNaN(c.V)) return false;
                if (!r.Pending
                    && Math.Abs(c.H - r.LastH) <= ColorEpsilon
                    && Math.Abs(c.S - r.LastS) <= ColorEpsilon
                    && Math.Abs(c.V - r.LastV) <= ColorEpsilon) return false;

                batch.H[n] = c.H;
                batch.S[n] = c.S;
                batch.V[n] = c.V;
            }
            else
            {
                string v = null;
                try
                {
                    if (r.Sky != null) v = ReadSkyProp(r);
                    else v = r.Kind == VpbNetAtomParamKind.Chooser ? r.Ch.val : r.T.val;
                }
                catch { return false; }
                if (v == null) v = string.Empty;
                if (!r.Pending
                    && string.Equals(v, r.LastText ?? string.Empty, StringComparison.Ordinal))
                    return false;

                bool skyFile = (r.Sky != null && string.Equals(r.Name, "url", StringComparison.Ordinal))
                    || r.T is JSONStorableUrl;
                if (skyFile && v.Length == 0)
                {
                    if (r.Pending) r.Pending = false;
                    if (r.LastText == null || r.LastText.Length == 0) return false;
                    batch.Text[n] = v;
                }
                else
                {
                    if (v.Length != 0)
                    {
                        if (skyFile)
                        {
                            if (!VpbNetStorableWhitelist.IsAllowedSkyRef(v))
                            {
                                r.LastText = v;
                                return false;
                            }
                        }
                        else if (!VpbNetStorableWhitelist.IsAllowedStringValue(v))
                        {
                            r.LastText = v;
                            return false;
                        }
                    }
                    batch.Text[n] = v;
                }
            }

            batch.Uid[n] = uid;
            batch.Storable[n] = r.Storable;
            batch.Name[n] = r.Name;
            batch.Kind[n] = r.Kind;
            batch.Count = n + 1;
            return true;
        }

        public void Commit(VpbNetAtomParamBatch batch, int count)
        {
            if (count > _sentRef.Count) count = _sentRef.Count;
            for (int i = 0; i < count && i < batch.Count; i++)
            {
                Ref r = _sentRef[i];
                if (r.Kind == VpbNetAtomParamKind.Float) r.LastF = batch.Number[i];
                else if (r.Kind == VpbNetAtomParamKind.Bool) r.LastB = batch.Switch[i];
                else if (r.Kind == VpbNetAtomParamKind.Color)
                {
                    r.LastH = batch.H[i];
                    r.LastS = batch.S[i];
                    r.LastV = batch.V[i];
                }
                else r.LastText = batch.Text[i];
                r.Pending = false;
                _sent++;
            }
            _sentRef.Clear();
        }

        // Baseline before apply — otherwise this side's observe is handed back as our change.
        public bool Apply(string uid, string storableId, string name, byte kind,
            float number, bool flag, float h, float s, float v, string text)
        {
            if (!_bound) return false;
            if (!VpbNetAtomParamKind.IsKnown(kind)) return false;

            Atom a = Find(uid);
            if (a == null)
            {
                _missing++;
                return false;
            }

            VpbNetStorableVerdict verdict = VpbNetStorableWhitelist.CheckAtomParam(uid, storableId, name);
            if (verdict != VpbNetStorableVerdict.Allowed)
            {
                JSONStorable probe = null;
                try { probe = a.GetStorableByID(storableId); }
                catch { }
                bool isSky = false;
                try { isSky = probe is SkyshopLightController; }
                catch { }
                if (isSky)
                    verdict = VpbNetStorableWhitelist.CheckAtomParam(uid, "GlobalLighting", name);
            }
            if (verdict != VpbNetStorableVerdict.Allowed)
            {
                _refused++;
                if (_refused <= 8)
                    LogUtil.LogWarning("[VPB.Net] refused a peer setting on " + uid + ": "
                        + VpbNetStorableWhitelist.Explain(verdict, storableId, name));
                return false;
            }
            if (VpbNetPropSync.ParamExclusion(a, _localAvatar, _remoteAvatar) != VpbNetPropExclusion.None)
            {
                _refused++;
                return false;
            }

            JSONStorable js = null;
            try { js = a.GetStorableByID(storableId); }
            catch { }
            if (js == null)
            {
                _missing++;
                if (_missing <= 8)
                    LogUtil.LogWarning("[VPB.Net] the peer changed " + storableId + "/" + name
                        + " on " + uid + ", but this side's " + uid + " has no " + storableId
                        + " - that setting stays as it is here");
                return false;
            }

            Slot slot = SlotFor(uid, a);
            Ref r = slot == null ? null : FindRef(slot, storableId, name, kind);

            bool ok = false;
            SkyshopLightController sky = js as SkyshopLightController;
            if (sky != null && (kind == VpbNetAtomParamKind.Chooser || kind == VpbNetAtomParamKind.Text)
                && string.Equals(name, "skyName", StringComparison.Ordinal))
                ok = ApplySkyName(sky, text, r);
            else if (sky != null && kind == VpbNetAtomParamKind.Text
                && string.Equals(name, "url", StringComparison.Ordinal))
                ok = ApplySkyUrl(sky, text, r);
            else if (kind == VpbNetAtomParamKind.Float) ok = ApplyFloat(js, name, number, r);
            else if (kind == VpbNetAtomParamKind.Bool) ok = ApplyBool(js, name, flag, r);
            else if (kind == VpbNetAtomParamKind.Color) ok = ApplyColor(js, name, h, s, v, r);
            else if (kind == VpbNetAtomParamKind.Chooser) ok = ApplyChooser(js, name, text, r);
            else ok = ApplyText(js, name, text, r);

            if (!ok)
            {
                _missing++;
                return false;
            }

            if (r != null)
            {
                r.HoldUntil = Time.realtimeSinceStartup + HoldOffSeconds;
                r.Pending = false;
            }
            _applied++;
            return true;
        }

        bool ApplyFloat(JSONStorable js, string name, float value, Ref r)
        {
            JSONStorableFloat p = null;
            try { p = js.GetFloatJSONParam(name); }
            catch { }
            if (p == null) return false;

            if (r != null) r.LastF = value;
            try { p.val = value; }
            catch { return false; }
            if (r != null)
            {
                try { r.LastF = p.val; }
                catch { }
            }
            return true;
        }

        bool ApplyBool(JSONStorable js, string name, bool value, Ref r)
        {
            JSONStorableBool p = null;
            try { p = js.GetBoolJSONParam(name); }
            catch { }
            if (p == null) return false;

            if (r != null) r.LastB = value;
            try { p.val = value; }
            catch { return false; }
            return true;
        }

        bool ApplyColor(JSONStorable js, string name, float h, float s, float v, Ref r)
        {
            JSONStorableColor p = null;
            try { p = js.GetColorJSONParam(name); }
            catch { }
            if (p == null) return false;

            if (r != null)
            {
                r.LastH = h;
                r.LastS = s;
                r.LastV = v;
            }

            HSVColor c;
            c.H = h;
            c.S = s;
            c.V = v;
            try { p.val = c; }
            catch { return false; }

            if (r != null)
            {
                try
                {
                    HSVColor now = p.val;
                    r.LastH = now.H;
                    r.LastS = now.S;
                    r.LastV = now.V;
                }
                catch { }
            }
            return true;
        }

        // Chooser is a list: a value not on it leaves both sides never agreeing again.
        bool ApplyChooser(JSONStorable js, string name, string value, Ref r)
        {
            JSONStorableStringChooser p = null;
            try { p = js.GetStringChooserJSONParam(name); }
            catch { }
            if (p == null) return false;
            if (value == null) return false;

            List<string> choices = null;
            try { choices = p.choices; }
            catch { }
            if (choices == null) return false;

            bool known = false;
            for (int i = 0; i < choices.Count; i++)
            {
                if (!string.Equals(choices[i], value, StringComparison.Ordinal)) continue;
                known = true;
                break;
            }
            if (!known)
            {
                _refused++;
                if (_refused <= 8)
                    LogUtil.LogWarning("[VPB.Net] the peer set " + name + " to \"" + value
                        + "\", which is not one of the choices this build offers; it was ignored");
                return false;
            }

            if (r != null) r.LastText = value;
            try { p.val = value; }
            catch { return false; }
            return true;
        }

        bool ApplyText(JSONStorable js, string name, string value, Ref r)
        {
            if (value == null) return false;

            bool url = false;
            try { url = js.IsUrlJSONParam(name); }
            catch { return false; }
            if (url) return ApplyUrl(js, name, value, r);

            if (value.Length != 0 && !VpbNetStorableWhitelist.IsAllowedStringValue(value)) return false;

            JSONStorableString p = null;
            try { p = js.GetStringJSONParam(name); }
            catch { }
            if (p == null) return false;
            if (p is JSONStorableUrl) return false;

            if (r != null) r.LastText = value;
            try { p.val = value; }
            catch { return false; }
            return true;
        }

        bool ApplyUrl(JSONStorable js, string name, string value, Ref r)
        {
            if (value.Length != 0 && !VpbNetStorableWhitelist.IsAllowedSkyRef(value)) return false;

            JSONStorableUrl p = null;
            try { p = js.GetUrlJSONParam(name); }
            catch { }
            if (p == null) return false;

            if (r != null) r.LastText = value;
            try { p.val = value; }
            catch { return false; }
            if (r != null)
            {
                try { r.LastText = p.val; }
                catch { }
            }
            return true;
        }

        static string ReadSkyProp(Ref r)
        {
            if (string.Equals(r.Name, "url", StringComparison.Ordinal))
                return r.Sky.url ?? string.Empty;
            return r.Sky.skyName ?? string.Empty;
        }

        bool ApplySkyName(SkyshopLightController sky, string value, Ref r)
        {
            if (value == null || value.Length == 0) return false;
            if (!VpbNetStorableWhitelist.IsAllowedStringValue(value)) return false;
            if (!SkyNameKnown(sky, value))
            {
                _refused++;
                if (_refused <= 8)
                    LogUtil.LogWarning("[VPB.Net] the peer set skyName to \"" + value
                        + "\", which is not one of the skies this build offers; it was ignored");
                return false;
            }

            if (r != null) r.LastText = value;
            try { sky.skyName = value; }
            catch { return false; }
            return true;
        }

        bool ApplySkyUrl(SkyshopLightController sky, string value, Ref r)
        {
            if (value == null) return false;
            if (value.Length != 0 && !VpbNetStorableWhitelist.IsAllowedSkyRef(value)) return false;

            if (r != null) r.LastText = value;
            try { sky.url = value; }
            catch { return false; }
            return true;
        }

        static bool SkyNameKnown(SkyshopLightController sky, string name)
        {
            mset.Sky[] skies = null;
            try { skies = sky.skies; }
            catch { return false; }
            if (skies == null) return false;
            for (int i = 0; i < skies.Length; i++)
            {
                mset.Sky one = skies[i];
                if (one == null) continue;
                string n = null;
                try { n = one.name; }
                catch { continue; }
                if (string.Equals(n, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        Slot SlotFor(string uid, Atom a)
        {
            Slot s;
            if (_byUid.TryGetValue(uid, out s) && s.Atom == a)
            {
                if (!s.Built) BuildSlot(s);
                return s;
            }

            s = new Slot();
            s.Atom = a;
            s.Uid = uid;
            s.Params = new List<Ref>(16);
            s.Built = false;
            _byUid[uid] = s;
            BuildSlot(s);
            _listDirty = true;
            return s;
        }

        static Ref FindRef(Slot s, string storableId, string name, byte kind)
        {
            for (int i = 0; i < s.Params.Count; i++)
            {
                Ref r = s.Params[i];
                if (r.Kind != kind) continue;
                if (!string.Equals(r.Storable, storableId, StringComparison.Ordinal)) continue;
                if (!string.Equals(r.Name, name, StringComparison.Ordinal)) continue;
                return r;
            }
            return null;
        }

        static Atom Find(string uid)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;
            try { return sc.GetAtomByUid(uid); }
            catch { return null; }
        }

        static bool IsSceneLoading()
        {
            try { return LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { return false; }
        }
    }
}
