using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetPeerLook
    {
        public const int MaxChangesPerPoll = 8;
        public const float MorphEpsilon = 0.0005f;
        public const int MaxRetryItems = 16;
        public const int MaxRetryPolls = 10;

        static MethodInfo _miSetActiveClothingItem;
        static bool _resolvedSetter;
        static MethodInfo _miSetActiveHairItem;
        static bool _resolvedHairSetter;

        readonly List<string> _retryUid = new List<string>(MaxRetryItems);
        readonly List<bool> _retryOn = new List<bool>(MaxRetryItems);
        readonly List<int> _retryLeft = new List<int>(MaxRetryItems);
        readonly List<string> _pendingUid = new List<string>(MaxChangesPerPoll);
        readonly List<bool> _pendingOn = new List<bool>(MaxChangesPerPoll);
        readonly List<string> _pendingMorphUid = new List<string>(MaxChangesPerPoll);
        readonly List<float> _pendingMorphValue = new List<float>(MaxChangesPerPoll);
        readonly List<string> _hairRetryUid = new List<string>(VpbNetEventLimits.MaxHairItems);
        readonly List<int> _hairRetryLeft = new List<int>(VpbNetEventLimits.MaxHairItems);
        readonly List<string> _morphRetryUid = new List<string>(MaxRetryItems);
        readonly List<float> _morphRetryValue = new List<float>(MaxRetryItems);
        readonly List<int> _morphRetryLeft = new List<int>(MaxRetryItems);

        Atom _atom;
        string _boundUid = string.Empty;
        DAZCharacterSelector _selector;
        DAZClothingItem[] _clothing;
        DAZHairGroup[] _hair;
        Dictionary<string, bool> _wornBaseline;

        // Keep baseline across rebind of the same person.
        Dictionary<string, bool> _keptBaseline;
        string _keptUid = string.Empty;
        List<DAZMorph> _morphs;
        Dictionary<string, float> _morphBaseline;
        int _morphCacheCount = -1;
        Dictionary<string, DAZMorph> _morphByUid;

        bool _syncMorphs;

        public string ExcludeMorphUid;

        // Morph-bank scan is the expensive path — session drives this from their published rules.
        public bool SyncMorphs
        {
            get { return _syncMorphs; }
            set
            {
                if (_syncMorphs == value) return;
                _syncMorphs = value;
                if (!IsBound) return;
                if (value) BindMorphs();
                else DropMorphs();
            }
        }

        public bool IsBound { get { return _atom != null && _selector != null; } }
        public int ClothingTracked { get { return _clothing == null ? 0 : _clothing.Length; } }
        public int ClothingRetryPending { get { return _retryUid.Count; } }
        public int HairTracked { get { return _hair == null ? 0 : _hair.Length; } }
        public int HairRetryPending { get { return _hairRetryUid.Count; } }
        public int MorphsTracked { get { return _morphs == null ? 0 : _morphs.Count; } }
        public int MorphRetryPending { get { return _morphRetryUid.Count; } }

        public bool Bind(Atom atom, bool syncMorphs)
        {
            Unbind();
            if (atom == null) return false;

            DAZCharacterSelector sel = null;
            try { sel = atom.GetComponentInChildren<DAZCharacterSelector>(true); }
            catch { }
            if (sel == null) return false;

            _atom = atom;
            _selector = sel;
            _syncMorphs = syncMorphs;

            string uidNow = null;
            try { uidNow = atom.uid; }
            catch { }
            _boundUid = uidNow ?? string.Empty;

            try { _clothing = sel.clothingItems; }
            catch { _clothing = null; }
            try { _hair = sel.hairItems; }
            catch { _hair = null; }

            bool reused = _keptBaseline != null && _boundUid.Length > 0
                && string.Equals(_keptUid, _boundUid, StringComparison.Ordinal);
            _wornBaseline = reused
                ? _keptBaseline
                : new Dictionary<string, bool>(StringComparer.Ordinal);
            _keptBaseline = null;
            _keptUid = string.Empty;

            int seeded = 0;
            if (_clothing != null)
            {
                for (int i = 0; i < _clothing.Length; i++)
                {
                    DAZClothingItem item = _clothing[i];
                    if (item == null) continue;
                    string uid = SafeUid(item);
                    if (uid == null) continue;
                    // Reuse: seed only unseen items so a change while unbound still reads as a change.
                    if (reused && _wornBaseline.ContainsKey(uid)) continue;
                    bool active = false;
                    try { active = item.active; }
                    catch { }
                    _wornBaseline[uid] = active;
                    seeded++;
                }
            }

            if (reused)
                LogUtil.LogWarning("[VPB.Net] look rebound on " + _boundUid
                    + ": kept the outfit baseline (" + _wornBaseline.Count + " known, "
                    + seeded + " new), so anything changed while it was rebinding still goes out");

            if (_syncMorphs) BindMorphs();
            return true;
        }

        void BindMorphs()
        {
            GenerateDAZMorphsControlUI ui = null;
            try { ui = _selector.morphsControlUI; }
            catch { }
            if (ui == null) return;

            try { _morphs = ui.GetMorphs(); }
            catch { _morphs = null; }
            if (_morphs == null) return;

            _morphBaseline = new Dictionary<string, float>(_morphs.Count, StringComparer.Ordinal);
            for (int i = 0; i < _morphs.Count; i++)
            {
                DAZMorph m = _morphs[i];
                if (m == null) continue;
                string uid = SafeMorphUid(m);
                if (uid == null) continue;
                float v = 0f;
                try { v = m.morphValue; }
                catch { }
                _morphBaseline[uid] = v;
            }
        }

        void DropMorphs()
        {
            _morphs = null;
            _morphBaseline = null;
            _morphByUid = null;
            _morphCacheCount = -1;
            _morphRetryUid.Clear();
            _morphRetryValue.Clear();
            _morphRetryLeft.Clear();
            _pendingMorphUid.Clear();
            _pendingMorphValue.Clear();
        }

        public void Unbind()
        {
            if (_wornBaseline != null && _boundUid.Length > 0)
            {
                _keptBaseline = _wornBaseline;
                _keptUid = _boundUid;
            }

            _atom = null;
            _boundUid = string.Empty;
            _selector = null;
            _clothing = null;
            _hair = null;
            _wornBaseline = null;
            _hairRetryUid.Clear();
            _hairRetryLeft.Clear();
            _morphs = null;
            _morphBaseline = null;
            _morphByUid = null;
            _morphCacheCount = -1;
            _morphRetryUid.Clear();
            _morphRetryValue.Clear();
            _morphRetryLeft.Clear();
            _pendingUid.Clear();
            _pendingOn.Clear();
            _pendingMorphUid.Clear();
            _pendingMorphValue.Clear();
            _retryUid.Clear();
            _retryOn.Clear();
            _retryLeft.Clear();
        }

        public bool IsAlive()
        {
            return _atom != null && _selector != null && _atom.on;
        }

        public int CollectClothingChanges(List<string> ids, List<bool> on)
        {
            ids.Clear();
            on.Clear();
            _pendingUid.Clear();
            _pendingOn.Clear();
            if (_wornBaseline == null) return 0;

            DAZClothingItem[] list = LiveClothing();
            if (list == null) return 0;

            for (int i = 0; i < list.Length && ids.Count < MaxChangesPerPoll; i++)
            {
                DAZClothingItem item = list[i];
                if (item == null) continue;

                bool active = false;
                try { active = item.active; }
                catch { continue; }

                string uid = SafeUid(item);
                if (uid == null) continue;

                bool was;
                if (!_wornBaseline.TryGetValue(uid, out was)) was = false;
                if (active == was) continue;

                _pendingUid.Add(uid);
                _pendingOn.Add(active);
                ids.Add(uid);
                on.Add(active);
            }
            return ids.Count;
        }

        public int CollectActiveClothing(List<string> ids, List<bool> on, int cap)
        {
            ids.Clear();
            on.Clear();

            DAZClothingItem[] list = LiveClothing();
            if (list == null) return 0;

            for (int i = 0; i < list.Length && ids.Count < cap; i++)
            {
                DAZClothingItem item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeUid(item);
                if (uid == null) continue;

                ids.Add(uid);
                on.Add(true);
            }
            return ids.Count;
        }

        // Order-independent XOR of worn uids — incremental diff can miss a rebind/partial poll.
        public int ActiveClothingSignature(out int count)
        {
            count = 0;
            DAZClothingItem[] list = LiveClothing();
            if (list == null) return 0;

            int mixed = 0;
            for (int i = 0; i < list.Length; i++)
            {
                DAZClothingItem item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeUid(item);
                if (uid == null) continue;

                mixed ^= HashUid(uid);
                count++;
            }

            unchecked { return mixed ^ (count * 0x2545F491); }
        }

        static int HashUid(string s)
        {
            unchecked
            {
                int h = -2128831035;
                for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619;
                return h;
            }
        }

        public void CommitClothing(int count)
        {
            if (_wornBaseline == null) return;
            if (count > _pendingUid.Count) count = _pendingUid.Count;
            for (int i = 0; i < count; i++)
            {
                _wornBaseline[_pendingUid[i]] = _pendingOn[i];
            }
        }

        // Fold a wire-landed change into the baseline now — origin is only known at this point.
        public void RebaseClothing(string uid, bool on)
        {
            if (_wornBaseline == null || string.IsNullOrEmpty(uid)) return;
            _wornBaseline[uid] = on;
        }

        // Hair is sent whole each event — no clothing-style baseline to lose across a rebind.
        public int CollectActiveHair(List<string> ids, int cap)
        {
            ids.Clear();
            DAZHairGroup[] list = LiveHair();
            if (list == null) return 0;

            for (int i = 0; i < list.Length && ids.Count < cap; i++)
            {
                DAZHairGroup item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeItemUid(item);
                if (uid == null) continue;
                ids.Add(uid);
            }
            return ids.Count;
        }

        public int ActiveHairSignature(out int count)
        {
            count = 0;
            DAZHairGroup[] list = LiveHair();
            if (list == null) return 0;

            int mixed = 0;
            for (int i = 0; i < list.Length; i++)
            {
                DAZHairGroup item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeItemUid(item);
                if (uid == null) continue;

                mixed ^= HashUid(uid);
                count++;
            }

            unchecked { return mixed ^ (count * 0x27220A95); }
        }

        // The peer's list is the whole truth: wear everything in it, take off everything else.
        public int ApplyHairSet(List<string> ids)
        {
            if (!IsBound) return 0;

            int worn = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ApplyHair(ids[i], true)) worn++;
            }

            DAZHairGroup[] list = LiveHair();
            if (list == null) return worn;

            for (int i = 0; i < list.Length; i++)
            {
                DAZHairGroup item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeItemUid(item);
                if (uid == null) continue;

                bool listed = false;
                for (int j = 0; j < ids.Count; j++)
                {
                    if (!string.Equals(ids[j], uid, StringComparison.Ordinal)) continue;
                    listed = true;
                    break;
                }
                if (!listed && ApplyHair(uid, false))
                    LogUtil.LogWarning("[VPB.Net] took " + uid + " off the peer's avatar");
            }
            return worn;
        }

        public bool ApplyHair(string uid, bool on)
        {
            if (string.IsNullOrEmpty(uid) || !IsBound) return false;

            DAZHairGroup item = FindHair(uid);
            if (item == null && on)
            {
                if (RequestPackage(uid)) item = FindHair(uid);
                if (item == null)
                {
                    QueueHairRetry(uid);
                    return false;
                }
            }
            if (item == null) return false;

            bool already;
            try { already = item.active; }
            catch { already = false; }
            if (already == on) return on;

            MethodInfo setter = ResolveHairSetter(_selector);
            if (setter == null) return false;

            try
            {
                ParameterInfo[] ps = setter.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = item;
                args[1] = on;
                for (int i = 2; i < ps.Length; i++) args[i] = false;
                setter.Invoke(_selector, args);
                LogUtil.LogWarning("[VPB.Net] " + (on ? "put " : "took ") + uid
                    + (on ? " on" : " off") + " the peer's avatar");
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] could not apply hair " + uid + ": " + e.Message);
                return false;
            }
        }

        void QueueHairRetry(string uid)
        {
            for (int i = 0; i < _hairRetryUid.Count; i++)
            {
                if (!string.Equals(_hairRetryUid[i], uid, StringComparison.Ordinal)) continue;
                _hairRetryLeft[i] = MaxRetryPolls;
                return;
            }
            if (_hairRetryUid.Count >= VpbNetEventLimits.MaxHairItems) return;

            _hairRetryUid.Add(uid);
            _hairRetryLeft.Add(MaxRetryPolls);
            LogUtil.LogWarning("[VPB.Net] the peer is wearing hair " + uid
                + " and this machine has no such item yet; requested it and will retry");
        }

        public void RetryPendingHair()
        {
            if (_hairRetryUid.Count == 0 || !IsBound) return;

            for (int i = _hairRetryUid.Count - 1; i >= 0; i--)
            {
                string uid = _hairRetryUid[i];
                if (FindHair(uid) != null)
                {
                    _hairRetryUid.RemoveAt(i);
                    _hairRetryLeft.RemoveAt(i);
                    ApplyHair(uid, true);
                    continue;
                }

                _hairRetryLeft[i] = _hairRetryLeft[i] - 1;
                if (_hairRetryLeft[i] > 0) continue;

                LogUtil.LogWarning("[VPB.Net] gave up applying hair " + uid
                    + "; that package is not installed here, so the peer's avatar is missing it");
                _hairRetryUid.RemoveAt(i);
                _hairRetryLeft.RemoveAt(i);
            }
        }

        DAZHairGroup[] LiveHair()
        {
            DAZHairGroup[] live = null;
            try { live = _selector.hairItems; }
            catch { live = null; }
            if (live != null) _hair = live;
            return _hair;
        }

        DAZHairGroup FindHair(string uid)
        {
            DAZHairGroup[] list = LiveHair();
            if (list == null) return null;

            for (int i = 0; i < list.Length; i++)
            {
                DAZHairGroup h = list[i];
                if (h == null) continue;
                if (!string.Equals(SafeItemUid(h), uid, StringComparison.Ordinal)) continue;
                return h;
            }
            return null;
        }

        static MethodInfo ResolveHairSetter(DAZCharacterSelector sel)
        {
            if (_resolvedHairSetter) return _miSetActiveHairItem;
            _resolvedHairSetter = true;
            if (sel == null) return null;

            try
            {
                MethodInfo[] all = sel.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (m.Name != "SetActiveHairItem") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2) continue;
                    if (ps[0].ParameterType != typeof(DAZHairGroup)) continue;
                    _miSetActiveHairItem = m;
                    break;
                }
            }
            catch { }

            if (_miSetActiveHairItem == null)
                LogUtil.LogWarning("[VPB.Net] DAZCharacterSelector.SetActiveHairItem not found; hair sync is off this session");
            return _miSetActiveHairItem;
        }

        public int CollectMorphChanges(List<string> ids, List<float> values)
        {
            ids.Clear();
            values.Clear();
            _pendingMorphUid.Clear();
            _pendingMorphValue.Clear();
            if (!_syncMorphs || _morphBaseline == null) return 0;

            List<DAZMorph> list = LiveMorphs();
            if (list == null) return 0;

            for (int i = 0; i < list.Count && ids.Count < MaxChangesPerPoll; i++)
            {
                DAZMorph m = list[i];
                if (m == null) continue;

                float v;
                try { v = m.morphValue; }
                catch { continue; }

                string uid = SafeMorphUid(m);
                if (uid == null) continue;
                if (ExcludeMorphUid != null && string.Equals(uid, ExcludeMorphUid, StringComparison.Ordinal))
                {
                    _morphBaseline[uid] = v;
                    continue;
                }

                float was;
                if (!_morphBaseline.TryGetValue(uid, out was))
                {
                    _morphBaseline[uid] = v;
                    continue;
                }

                float d = v - was;
                if (d < 0f) d = -d;
                if (d < MorphEpsilon) continue;

                _pendingMorphUid.Add(uid);
                _pendingMorphValue.Add(v);
                ids.Add(uid);
                values.Add(v);
            }
            return ids.Count;
        }

        public void CommitMorphs(int count)
        {
            if (_morphBaseline == null) return;
            if (count > _pendingMorphUid.Count) count = _pendingMorphUid.Count;
            for (int i = 0; i < count; i++)
            {
                _morphBaseline[_pendingMorphUid[i]] = _pendingMorphValue[i];
            }
        }

        public void RebaseMorph(string uid, float value)
        {
            if (_morphBaseline == null || string.IsNullOrEmpty(uid)) return;
            _morphBaseline[uid] = value;
        }

        public void ApplyState(VpbNetPeerState state)
        {
            if (state == null || !IsBound) return;

            for (int i = 0; i < state.ClothingCount; i++)
            {
                ApplyClothing(state.ClothingId(i), state.ClothingOn(i));
            }

            if (state.ClothingAuthoritative) RemoveUnlisted(state);
            for (int i = 0; i < state.MorphCount; i++)
            {
                ApplyMorph(state.MorphId(i), state.MorphValue(i));
            }
        }

        DAZClothingItem[] LiveClothing()
        {
            DAZClothingItem[] live = null;
            try { live = _selector.clothingItems; }
            catch { live = null; }
            if (live != null) _clothing = live;
            return _clothing;
        }

        DAZClothingItem FindClothing(string uid)
        {
            DAZClothingItem[] list = LiveClothing();
            if (list == null) return null;

            for (int i = 0; i < list.Length; i++)
            {
                DAZClothingItem c = list[i];
                if (c == null) continue;
                if (!string.Equals(SafeUid(c), uid, StringComparison.Ordinal)) continue;
                return c;
            }
            return null;
        }

        static bool RequestPackage(string uid)
        {
            if (uid.IndexOf(':') <= 0) return false;
            try { return VamOnDemandLoader.TryRegisterPackageOnDemandForEntryPath(uid) != null; }
            catch { return false; }
        }

        void QueueRetry(string uid, bool on)
        {
            for (int i = 0; i < _retryUid.Count; i++)
            {
                if (!string.Equals(_retryUid[i], uid, StringComparison.Ordinal)) continue;
                _retryOn[i] = on;
                return;
            }
            if (_retryUid.Count >= MaxRetryItems) return;

            _retryUid.Add(uid);
            _retryOn.Add(on);
            _retryLeft.Add(MaxRetryPolls);
            LogUtil.LogWarning("[VPB.Net] peer is wearing " + uid
                + " and this machine has no such item yet; requested it and will retry");
        }

        public void RetryPendingClothing()
        {
            if (_retryUid.Count == 0 || !IsBound) return;

            for (int i = _retryUid.Count - 1; i >= 0; i--)
            {
                string uid = _retryUid[i];
                DAZClothingItem item = FindClothing(uid);
                if (item != null)
                {
                    bool on = _retryOn[i];
                    DropRetry(i);
                    bool ok = ApplyClothing(uid, on);
                    LogUtil.LogWarning("[VPB.Net] " + uid + " arrived and was "
                        + (ok ? "put on the peer's avatar" : "found but could not be applied"));
                    continue;
                }

                _retryLeft[i] = _retryLeft[i] - 1;
                if (_retryLeft[i] > 0) continue;

                LogUtil.LogWarning("[VPB.Net] gave up applying " + uid
                    + "; that package is not installed here, so the peer's avatar is missing it");
                DropRetry(i);
            }
        }

        void DropRetry(int i)
        {
            _retryUid.RemoveAt(i);
            _retryOn.RemoveAt(i);
            _retryLeft.RemoveAt(i);
        }

        void RemoveUnlisted(VpbNetPeerState state)
        {
            DAZClothingItem[] list = LiveClothing();
            if (list == null) return;

            for (int i = 0; i < list.Length; i++)
            {
                DAZClothingItem item = list[i];
                if (item == null) continue;

                bool active;
                try { active = item.active; }
                catch { continue; }
                if (!active) continue;

                string uid = SafeUid(item);
                if (uid == null) continue;

                bool listed = false;
                for (int j = 0; j < state.ClothingCount; j++)
                {
                    if (!string.Equals(state.ClothingId(j), uid, StringComparison.Ordinal)) continue;
                    listed = true;
                    break;
                }
                if (!listed) ApplyClothing(uid, false);
            }
        }

        public bool ApplyClothing(string uid, bool on)
        {
            if (string.IsNullOrEmpty(uid) || !IsBound) return false;

            DAZClothingItem item = FindClothing(uid);
            if (item == null && on)
            {
                if (RequestPackage(uid)) item = FindClothing(uid);
                if (item == null)
                {
                    QueueRetry(uid, on);
                    return false;
                }
            }
            if (item == null) return false;

            bool already;
            try { already = item.active; }
            catch { already = !on; }
            if (already == on) return true;

            MethodInfo setter = ResolveSetter(_selector);
            if (setter == null) return false;

            try
            {
                ParameterInfo[] ps = setter.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = item;
                args[1] = on;
                for (int i = 2; i < ps.Length; i++) args[i] = false;
                setter.Invoke(_selector, args);
                // Log success — silent apply made "never changed" and "never sent" look identical.
                LogUtil.LogWarning("[VPB.Net] " + (on ? "put " : "took ") + uid
                    + (on ? " on" : " off") + " the peer's avatar");
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] could not apply clothing " + uid + ": " + e.Message);
                return false;
            }
        }

        public bool ApplyMorph(string uid, float value)
        {
            if (string.IsNullOrEmpty(uid) || !IsBound) return false;

            DAZMorph m = ResolveMorph(uid);
            if (m == null)
            {
                if (RequestPackage(uid)) m = ResolveMorph(uid);
                if (m == null)
                {
                    QueueMorphRetry(uid, value);
                    return false;
                }
            }

            try
            {
                m.morphValue = value;
                return true;
            }
            catch { return false; }
        }

        void QueueMorphRetry(string uid, float value)
        {
            for (int i = 0; i < _morphRetryUid.Count; i++)
            {
                if (!string.Equals(_morphRetryUid[i], uid, StringComparison.Ordinal)) continue;
                _morphRetryValue[i] = value;
                return;
            }
            if (_morphRetryUid.Count >= MaxRetryItems) return;

            _morphRetryUid.Add(uid);
            _morphRetryValue.Add(value);
            _morphRetryLeft.Add(MaxRetryPolls);
            LogUtil.LogWarning("[VPB.Net] peer changed morph " + uid
                + " and this machine has no such morph yet; requested it and will retry");
        }

        public void RetryPendingMorphs()
        {
            if (_morphRetryUid.Count == 0 || !IsBound) return;

            for (int i = _morphRetryUid.Count - 1; i >= 0; i--)
            {
                string uid = _morphRetryUid[i];
                DAZMorph m = ResolveMorph(uid);
                if (m != null)
                {
                    float v = _morphRetryValue[i];
                    DropMorphRetry(i);
                    ApplyMorph(uid, v);
                    continue;
                }

                _morphRetryLeft[i] = _morphRetryLeft[i] - 1;
                if (_morphRetryLeft[i] > 0) continue;

                LogUtil.LogWarning("[VPB.Net] gave up applying morph " + uid
                    + "; that morph is not installed here, so the peer's face or body differs");
                DropMorphRetry(i);
            }
        }

        void DropMorphRetry(int i)
        {
            _morphRetryUid.RemoveAt(i);
            _morphRetryValue.RemoveAt(i);
            _morphRetryLeft.RemoveAt(i);
        }

        List<DAZMorph> LiveMorphs()
        {
            GenerateDAZMorphsControlUI ui = null;
            try { ui = _selector.morphsControlUI; }
            catch { }
            if (ui == null) return _morphs;

            List<DAZMorph> all = null;
            try { all = ui.GetMorphs(); }
            catch { all = null; }
            if (all != null) _morphs = all;
            return _morphs;
        }

        DAZMorph ResolveMorph(string uid)
        {
            List<DAZMorph> all = LiveMorphs();
            if (all == null) return null;

            if (_morphByUid == null || all.Count != _morphCacheCount)
            {
                _morphByUid = new Dictionary<string, DAZMorph>(all.Count, StringComparer.Ordinal);
                for (int i = 0; i < all.Count; i++)
                {
                    DAZMorph m = all[i];
                    if (m == null) continue;
                    string id = SafeMorphUid(m);
                    if (id == null || _morphByUid.ContainsKey(id)) continue;
                    _morphByUid[id] = m;
                }
                _morphCacheCount = all.Count;
            }

            DAZMorph found;
            return _morphByUid.TryGetValue(uid, out found) ? found : null;
        }

        static MethodInfo ResolveSetter(DAZCharacterSelector sel)
        {
            if (_resolvedSetter) return _miSetActiveClothingItem;
            _resolvedSetter = true;
            if (sel == null) return null;

            try
            {
                MethodInfo[] all = sel.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (m.Name != "SetActiveClothingItem") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2) continue;
                    if (ps[0].ParameterType != typeof(DAZClothingItem)) continue;
                    _miSetActiveClothingItem = m;
                    break;
                }
            }
            catch { }

            if (_miSetActiveClothingItem == null)
                LogUtil.LogWarning("[VPB.Net] DAZCharacterSelector.SetActiveClothingItem not found; clothing sync is off this session");
            return _miSetActiveClothingItem;
        }

        static string SafeUid(DAZClothingItem item)
        {
            return SafeItemUid(item);
        }

        static string SafeItemUid(DAZDynamicItem item)
        {
            string uid = null;
            try { uid = item.uid; }
            catch { }
            if (string.IsNullOrEmpty(uid)) return null;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid)) return null;
            if (VpbNetEventCodec.IsPluginReference(uid)) return null;
            return uid;
        }

        static string SafeMorphUid(DAZMorph m)
        {
            string uid = null;
            try { uid = m.uid; }
            catch { }
            if (string.IsNullOrEmpty(uid)) return null;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid)) return null;
            if (VpbNetEventCodec.IsPluginReference(uid)) return null;
            return uid;
        }
    }
}
