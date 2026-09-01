using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public enum VpbNetPropExclusion
    {
        None = 0,
        Gone = 1,
        Person = 2,
        InSubScene = 3,
        PlayerLocal = 4
    }

    public sealed class VpbNetPropSync
    {
        public const float PositionEpsilon = 0.001f;
        public const float RotationEpsilonDeg = 0.25f;
        public const float HoldOffSeconds = 0.35f;
        public const int MaxLifecycleQueue = 16;
        public const float SubSceneResolveSeconds = 10f;
        public const float SubScenePlaceSeconds = 5f;

        // Past this, drop the echo so a load that never landed is not handed back as a local change.
        public const float SubSceneEchoHoldSeconds = 12f;
        public const int MaxSubSceneRetryPolls = 150;
        public const int MaxSubSceneQueue = 8;

        struct Baseline
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public float HoldUntil;
        }

        // Absorb the specific echo (want/was), not a blanket ignore.
        struct Echo
        {
            public string Want;
            public string Was;
            public float Until;
        }

        readonly List<Atom> _watched = new List<Atom>(64);
        readonly List<string> _watchedUid = new List<string>(64);
        readonly List<Transform> _watchedT = new List<Transform>(64);
        readonly Dictionary<string, Baseline> _baseline = new Dictionary<string, Baseline>(64, StringComparer.Ordinal);
        readonly List<int> _sentIndex = new List<int>(VpbNetPropLimits.MaxAtomsPerFrame);

        readonly List<string> _addUid = new List<string>(MaxLifecycleQueue);
        readonly List<string> _addType = new List<string>(MaxLifecycleQueue);
        readonly List<float> _addDeadline = new List<float>(MaxLifecycleQueue);
        readonly List<string> _removeUid = new List<string>(MaxLifecycleQueue);
        readonly HashSet<string> _remoteCreated = new HashSet<string>(StringComparer.Ordinal);

        readonly List<Atom> _subAtoms = new List<Atom>(8);
        readonly List<string> _subAtomUid = new List<string>(8);
        readonly Dictionary<string, string> _subBaseline = new Dictionary<string, string>(8, StringComparer.Ordinal);
        readonly Dictionary<string, Echo> _subEcho = new Dictionary<string, Echo>(8, StringComparer.Ordinal);
        readonly List<string> _sentSubUid = new List<string>(VpbNetEventLimits.MaxSubScenesPerEvent);
        readonly List<string> _sentSubRef = new List<string>(VpbNetEventLimits.MaxSubScenesPerEvent);
        readonly List<string> _subRetryUid = new List<string>(MaxSubSceneQueue);
        readonly List<string> _subRetryRef = new List<string>(MaxSubSceneQueue);
        readonly List<int> _subRetryLeft = new List<int>(MaxSubSceneQueue);

        static MethodInfo _miLoadSubSceneWithPath;
        static bool _resolvedLoader;

        Atom _localAvatar;
        Atom _remoteAvatar;
        bool _bound;
        bool _listDirty = true;
        bool _lifecycleOn;
        int _applied;
        int _created;
        int _removed;
        int _refused;
        int _dropped;
        int _suppressedAdds;
        int _suppressedRemoves;
        int _skippedPerson;
        int _skippedInSubScene;
        int _skippedPlayerLocal;
        int _pruned;
        int _subSent;
        int _subApplied;
        int _subSkipped;
        int _subRefused;
        int _subSuppressed;
        bool _warnedLifecycleOff;
        bool _warnedSubLifecycleOff;
        bool _warnedQueueFull;
        bool _announceExisting = true;
        bool _subSeeded;

        public bool IsBound { get { return _bound; } }
        public bool AnnounceExisting { get { return _announceExisting; } set { _announceExisting = value; } }
        public bool LifecycleOn { get { return _lifecycleOn; } }
        public int Applied { get { return _applied; } }
        public int Created { get { return _created; } }
        public int Removed { get { return _removed; } }
        public int Refused { get { return _refused; } }
        public int Dropped { get { return _dropped; } }
        public int SuppressedAdds { get { return _suppressedAdds; } }
        public int SuppressedRemoves { get { return _suppressedRemoves; } }
        public int SkippedPerson { get { return _skippedPerson; } }
        public int SkippedInSubScene { get { return _skippedInSubScene; } }
        public int SkippedPlayerLocal { get { return _skippedPlayerLocal; } }
        public int Pruned { get { return _pruned; } }
        public int SubSceneSent { get { return _subSent; } }
        public int SubSceneApplied { get { return _subApplied; } }
        public int SubSceneSkipped { get { return _subSkipped; } }
        public int SubSceneRefused { get { return _subRefused; } }
        public int SubSceneSuppressed { get { return _subSuppressed; } }
        public int SubScenesWatched { get { return _subAtoms.Count; } }
        public int SubScenePendingApplies { get { return _subRetryUid.Count; } }

        public string DescribeExclusions()
        {
            return _skippedPerson + " persons, " + _skippedInSubScene
                + " inside a subscene, " + _skippedPlayerLocal + " player-local";
        }
        public int Watched { get { return _watched.Count; } }
        public int PendingAdds { get { return _addUid.Count; } }
        public int PendingRemoves { get { return _removeUid.Count; } }

        public void Bind(Atom localAvatar, Atom remoteAvatar, bool lifecycle)
        {
            Unbind();
            _localAvatar = localAvatar;
            _remoteAvatar = remoteAvatar;
            _lifecycleOn = lifecycle;
            _warnedLifecycleOff = false;
            _listDirty = true;
            _bound = true;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                sc.onAtomAddedHandlers -= OnAtomAdded;
                sc.onAtomAddedHandlers += OnAtomAdded;
                sc.onAtomRemovedHandlers -= OnAtomRemoved;
                sc.onAtomRemovedHandlers += OnAtomRemoved;
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
                        sc.onAtomAddedHandlers -= OnAtomAdded;
                        sc.onAtomRemovedHandlers -= OnAtomRemoved;
                    }
                    catch { }
                }
            }

            _bound = false;
            _lifecycleOn = false;
            _localAvatar = null;
            _remoteAvatar = null;
            _watched.Clear();
            _watchedUid.Clear();
            _watchedT.Clear();
            _baseline.Clear();
            _sentIndex.Clear();
            _addUid.Clear();
            _addType.Clear();
            _addDeadline.Clear();
            _removeUid.Clear();
            _remoteCreated.Clear();
            _subAtoms.Clear();
            _subAtomUid.Clear();
            _subBaseline.Clear();
            _subEcho.Clear();
            _sentSubUid.Clear();
            _sentSubRef.Clear();
            _subRetryUid.Clear();
            _subRetryRef.Clear();
            _subRetryLeft.Clear();
            _warnedQueueFull = false;
            _warnedSubLifecycleOff = false;
            _subSeeded = false;
            _listDirty = true;
        }

        void OnAtomAdded(Atom a)
        {
            _listDirty = true;
            if (!_bound || a == null) return;
            if (IsSceneLoading()) return;

            string uid = SafeUid(a);
            if (uid == null) return;
            if (_remoteCreated.Contains(uid)) return;
            if (!IsSyncable(a)) return;

            string type = null;
            try { type = a.type; }
            catch { }

            // Log here: a silent return made "feature off" look identical to a bug.
            if (!_lifecycleOn)
            {
                _suppressedAdds++;
                WarnLifecycleOff("added " + uid + " (" + type + ")");
                return;
            }

            if (!VpbNetStorableWhitelist.IsAllowedAtom(uid, type))
            {
                LogUtil.LogWarning("[VPB.Net] not sharing " + uid + " (" + type + "): "
                    + VpbNetStorableWhitelist.Explain(
                        VpbNetStorableWhitelist.CheckAtom(uid, type), type, type));
                return;
            }
            if (_addUid.Count >= MaxLifecycleQueue)
            {
                _dropped++;
                WarnQueueFull(uid);
                return;
            }

            LogUtil.LogWarning("[VPB.Net] you added " + uid + " (" + type
                + "); queued for the other side");

            _addUid.Add(uid);
            _addType.Add(type);
            _addDeadline.Add(Time.realtimeSinceStartup + SubSceneResolveSeconds);
        }

        void OnAtomRemoved(Atom a)
        {
            _listDirty = true;
            if (!_bound || a == null) return;
            if (IsSceneLoading()) return;

            string uid = SafeUid(a);
            if (uid == null) return;
            if (!_lifecycleOn)
            {
                if (IsSyncable(a))
                {
                    _suppressedRemoves++;
                    WarnLifecycleOff("removed " + uid);
                }
                return;
            }
            if (_remoteCreated.Remove(uid)) return;
            if (_removeUid.Count >= MaxLifecycleQueue) return;
            if (_removeUid.Contains(uid)) return;
            _removeUid.Add(uid);
        }

        // Resolve storePath here, not at add — UI creates an empty SubScene atom first.
        public int TakeAdds(List<string> uids, List<string> types, List<string> refs,
            List<Vector3> positions, List<Quaternion> rotations, int cap)
        {
            uids.Clear();
            types.Clear();
            refs.Clear();
            positions.Clear();
            rotations.Clear();

            PruneAdds();

            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _addUid.Count && uids.Count < cap; i++)
            {
                string uid = _addUid[i];
                Atom a = Find(uid);
                string reference = a == null ? string.Empty : SubSceneReference(a);
                bool isSubScene = a != null && HasSubScene(a);

                if (isSubScene && reference.Length == 0 && now < _addDeadline[i]) break;
                if (isSubScene && reference.Length == 0)
                    LogUtil.LogWarning("[VPB.Net] " + uid
                        + " is a subscene with nothing loaded into it yet; the other side gets an empty one");

                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                Transform t = a == null ? null : ControlOf(a);
                if (t != null)
                {
                    pos = t.position;
                    rot = t.rotation;
                }

                uids.Add(uid);
                types.Add(_addType[i]);
                refs.Add(reference);
                positions.Add(pos);
                rotations.Add(rot);
            }
            return uids.Count;
        }

        void PruneAdds()
        {
            for (int i = _addUid.Count - 1; i >= 0; i--)
            {
                string uid = _addUid[i];
                Atom a = Find(uid);
                if (a == null)
                {
                    if (Time.realtimeSinceStartup < _addDeadline[i]) continue;
                    DropAdd(i);
                    continue;
                }
                if (IsSyncable(a)) continue;

                DropAdd(i);
                _pruned++;
            }
        }

        void DropAdd(int i)
        {
            _addUid.RemoveAt(i);
            _addType.RemoveAt(i);
            _addDeadline.RemoveAt(i);
        }

        public void CommitAdds(int count)
        {
            if (count > _addUid.Count) count = _addUid.Count;
            _addUid.RemoveRange(0, count);
            _addType.RemoveRange(0, count);
            _addDeadline.RemoveRange(0, count);
        }

        void WarnQueueFull(string uid)
        {
            if (_warnedQueueFull) return;
            _warnedQueueFull = true;
            LogUtil.LogWarning("[VPB.Net] the add queue is full (" + MaxLifecycleQueue
                + " waiting), so " + uid + " was NOT sent to the other side."
                + " Whatever is at the head of that queue is not resolving.");
        }

        void WarnLifecycleOff(string what)
        {
            if (_warnedLifecycleOff) return;
            _warnedLifecycleOff = true;
            LogUtil.LogWarning("[VPB.Net] you " + what
                + " but adding and deleting objects is not being shared this session."
                + " Press \"Load objects they add\" under Rules on BOTH"
                + " machines - it takes effect without a reconnect.");
        }

        static bool HasSubScene(Atom a)
        {
            try { return a.subSceneComponent != null; }
            catch { return false; }
        }

        public int TakeRemoves(List<string> uids, int cap)
        {
            uids.Clear();
            int n = _removeUid.Count;
            if (n > cap) n = cap;
            for (int i = 0; i < n; i++) uids.Add(_removeUid[i]);
            return n;
        }

        public void CommitRemoves(int count)
        {
            if (count > _removeUid.Count) count = _removeUid.Count;
            _removeUid.RemoveRange(0, count);
        }

        // Watch storePath, not creation — Load SubScene into an existing atom used to send nothing.
        public int CollectSubSceneChanges(List<string> uids, List<string> refs, int cap)
        {
            uids.Clear();
            refs.Clear();
            _sentSubUid.Clear();
            _sentSubRef.Clear();
            if (!_bound) return 0;

            RefreshWatched();
            if (_subAtoms.Count == 0) return 0;

            float now = Time.realtimeSinceStartup;
            bool seeding = !_subSeeded;
            _subSeeded = true;

            for (int i = 0; i < _subAtoms.Count; i++)
            {
                Atom a = _subAtoms[i];
                if (a == null) continue;

                string uid = _subAtomUid[i];
                string path = RawSubScenePath(a);

                Echo echo;
                if (_subEcho.TryGetValue(uid, out echo))
                {
                    if (SameSubSceneFile(path, echo.Want))
                    {
                        _subEcho.Remove(uid);
                        _subBaseline[uid] = path;
                        continue;
                    }
                    if (now >= echo.Until)
                    {
                        // Echo expired without matching Want — take what is there, do not echo the peer's load back.
                        _subEcho.Remove(uid);
                        _subBaseline[uid] = path;
                        continue;
                    }
                    if (path.Length == 0
                        || string.Equals(path, echo.Was, StringComparison.Ordinal))
                        continue;

                    // Player loaded over the peer's pick — local change, travels.
                    _subEcho.Remove(uid);
                }

                string known;
                bool first = !_subBaseline.TryGetValue(uid, out known);
                if (!first && SameSubSceneFile(known, path)) continue;

                if (!_lifecycleOn)
                {
                    _subBaseline[uid] = path;
                    _subSuppressed++;
                    WarnSubSceneLifecycleOff(uid);
                    continue;
                }

                if (path.Length == 0)
                {
                    _subBaseline[uid] = path;
                    LogUtil.LogWarning("[VPB.Net] " + uid
                        + " is a subscene with nothing loaded into it; nothing to send for it yet");
                    continue;
                }

                if (first && seeding && !_announceExisting)
                {
                    _subBaseline[uid] = path;
                    LogUtil.LogWarning("[VPB.Net] " + uid + " already held " + path
                        + " when the session started; the host's copy decides that slot, so this"
                        + " side is not offering its own and the host is not asked about it");
                    continue;
                }

                VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckSubSceneRef(path);
                if (v != VpbNetStorableVerdict.Allowed)
                {
                    _subBaseline[uid] = path;
                    _subSkipped++;
                    LogUtil.LogWarning("[VPB.Net] not telling the other side about subscene \""
                        + path + "\" in " + uid + ": "
                        + VpbNetStorableWhitelist.Explain(v, path, path)
                        + " - their copy stays as it is");
                    continue;
                }

                if (uids.Count >= cap) continue;

                LogUtil.LogWarning(first
                    ? "[VPB.Net] " + uid + " holds " + path
                        + "; telling the other side, which loads it from its own library only if"
                        + " what it has there is different"
                    : "[VPB.Net] you loaded " + path + " into " + uid
                        + "; sending it so the other side loads the same thing from its own library");

                uids.Add(uid);
                refs.Add(path);
                _sentSubUid.Add(uid);
                _sentSubRef.Add(path);
            }

            return uids.Count;
        }

        public void CommitSubSceneChanges(int count)
        {
            if (count > _sentSubUid.Count) count = _sentSubUid.Count;
            for (int i = 0; i < count; i++)
            {
                _subBaseline[_sentSubUid[i]] = _sentSubRef[i];
                _subSent++;
            }
            _sentSubUid.Clear();
            _sentSubRef.Clear();
        }

        public bool AbsorbSameSubSceneRef(string uid, string reference)
        {
            if (!_bound || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(reference))
                return false;

            Atom a = Find(uid);
            if (a == null || SubSceneOf(a) == null) return false;

            string have = RawSubScenePath(a);
            if (have.Length == 0 || !SameSubSceneFile(have, reference)) return false;

            _subBaseline[uid] = have;
            _subEcho.Remove(uid);
            _subSkipped++;
            return true;
        }

        public bool ApplySubSceneRef(string uid, string reference)
        {
            if (!_lifecycleOn)
            {
                _subSuppressed++;
                WarnSubSceneLifecycleOff(uid);
                return false;
            }
            if (!VpbNetPropFrame.IsSendableUid(uid)) return false;

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckSubSceneRef(reference);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                _subRefused++;
                LogUtil.LogWarning("[VPB.Net] refused a peer subscene reference: " + reference
                    + " (" + VpbNetStorableWhitelist.Explain(v, reference, reference) + ")");
                return false;
            }

            Atom a = Find(uid);
            if (a == null)
            {
                QueueSubSceneRetry(uid, reference);
                return false;
            }

            SubScene sub = SubSceneOf(a);
            if (sub == null)
            {
                _subRefused++;
                LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                    + " but this side's " + uid + " is not a subscene, so nothing was loaded");
                return false;
            }

            // Record before the load so our watcher never hands this back.
            _subBaseline[uid] = reference;

            string have = RawSubScenePath(a);
            if (SameSubSceneFile(have, reference))
            {
                _subBaseline[uid] = have;
                _subEcho.Remove(uid);
                _subSkipped++;
                LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                    + "; this side already has that same subscene" + (string.Equals(have, reference,
                        StringComparison.Ordinal) ? string.Empty : " as \"" + have + "\"")
                    + ", so nothing was reloaded");
                return false;
            }

            LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                + " (this side had " + (have.Length == 0 ? "nothing" : have) + ")");
            ExpectSubScene(uid, reference, have);
            LoadSubScene(a, uid, reference);
            _subApplied++;
            return true;
        }

        void ExpectSubScene(string uid, string want, string was)
        {
            Echo e;
            e.Want = want;
            e.Was = was ?? string.Empty;
            e.Until = Time.realtimeSinceStartup + SubSceneEchoHoldSeconds;
            _subEcho[uid] = e;
        }

        void QueueSubSceneRetry(string uid, string reference)
        {
            for (int i = 0; i < _subRetryUid.Count; i++)
            {
                if (!string.Equals(_subRetryUid[i], uid, StringComparison.Ordinal)) continue;
                _subRetryRef[i] = reference;
                _subRetryLeft[i] = MaxSubSceneRetryPolls;
                return;
            }
            if (_subRetryUid.Count >= MaxSubSceneQueue)
            {
                _subRefused++;
                LogUtil.LogWarning("[VPB.Net] too many subscene loads are waiting for atoms that"
                    + " do not exist here; " + uid + " was dropped");
                return;
            }

            _subRetryUid.Add(uid);
            _subRetryRef.Add(reference);
            _subRetryLeft.Add(MaxSubSceneRetryPolls);
            LogUtil.LogWarning("[VPB.Net] the peer loaded " + reference + " into " + uid
                + ", which does not exist here yet; holding it until that atom appears");
        }

        public void RetryPendingSubScenes()
        {
            if (_subRetryUid.Count == 0) return;

            for (int i = _subRetryUid.Count - 1; i >= 0; i--)
            {
                string uid = _subRetryUid[i];
                if (Find(uid) != null)
                {
                    string reference = _subRetryRef[i];
                    DropSubSceneRetry(i);
                    ApplySubSceneRef(uid, reference);
                    continue;
                }

                _subRetryLeft[i] = _subRetryLeft[i] - 1;
                if (_subRetryLeft[i] > 0) continue;

                LogUtil.LogWarning("[VPB.Net] gave up loading " + _subRetryRef[i] + " into " + uid
                    + "; no atom by that name ever appeared on this side");
                _subRefused++;
                DropSubSceneRetry(i);
            }
        }

        void DropSubSceneRetry(int i)
        {
            _subRetryUid.RemoveAt(i);
            _subRetryRef.RemoveAt(i);
            _subRetryLeft.RemoveAt(i);
        }

        void WarnSubSceneLifecycleOff(string uid)
        {
            if (_warnedSubLifecycleOff) return;
            _warnedSubLifecycleOff = true;
            LogUtil.LogWarning("[VPB.Net] the subscene in " + uid
                + " changed, but subscenes are not being shared this session."
                + " Press \"Add/Del\" on the session panel on BOTH machines - it says"
                + " \"Add/Del: shared\" once both sides agree.");
        }

        public int Collect(VpbNetPropFrame frame)
        {
            frame.Clear();
            _sentIndex.Clear();
            if (!_bound) return 0;

            RefreshWatched();
            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < _watched.Count; i++)
            {
                if (frame.Count >= VpbNetPropLimits.MaxAtomsPerFrame) break;

                Transform t = _watchedT[i];
                Atom a = _watched[i];
                if (t == null || a == null) continue;

                bool on;
                try { on = a.on; }
                catch { continue; }
                if (!on) continue;

                string uid = _watchedUid[i];
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;

                Baseline b;
                if (!_baseline.TryGetValue(uid, out b))
                {
                    b.Pos = pos;
                    b.Rot = rot;
                    b.HoldUntil = 0f;
                    _baseline[uid] = b;
                    continue;
                }

                if (now < b.HoldUntil) continue;
                if (!Moved(b, pos, rot)) continue;
                if (!frame.Add(uid, pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w)) continue;
                _sentIndex.Add(i);
            }

            return frame.Count;
        }

        public void Commit(VpbNetPropFrame frame)
        {
            for (int i = 0; i < _sentIndex.Count && i < frame.Count; i++)
            {
                int at = _sentIndex[i];
                Transform t = _watchedT[at];
                if (t == null) continue;

                Baseline b;
                b.Pos = t.position;
                b.Rot = t.rotation;
                b.HoldUntil = 0f;
                _baseline[_watchedUid[at]] = b;
            }
            _sentIndex.Clear();
        }

        public int Apply(VpbNetPropFrame frame)
        {
            if (!_bound || frame == null) return 0;

            int n = 0;
            float hold = Time.realtimeSinceStartup + HoldOffSeconds;

            for (int i = 0; i < frame.Count; i++)
            {
                string uid = frame.Uid(i);
                Atom a = Find(uid);
                if (a == null) continue;
                if (!IsSyncable(a)) continue;

                Transform t = ControlOf(a);
                if (t == null) continue;

                Vector3 pos;
                pos.x = frame.PosX(i);
                pos.y = frame.PosY(i);
                pos.z = frame.PosZ(i);

                Quaternion rot;
                rot.x = frame.RotX(i);
                rot.y = frame.RotY(i);
                rot.z = frame.RotZ(i);
                rot.w = frame.RotW(i);

                try
                {
                    t.position = pos;
                    t.rotation = rot;
                }
                catch { continue; }

                Rigidbody rb = BodyOf(a);
                if (rb != null)
                {
                    try
                    {
                        rb.position = pos;
                        rb.rotation = rot;
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    catch { }
                }

                Baseline b;
                b.Pos = pos;
                b.Rot = rot;
                b.HoldUntil = hold;
                _baseline[uid] = b;

                n++;
                _applied++;
            }

            return n;
        }

        public bool ApplyAdd(string uid, string type, string subSceneRef, Vector3 pos, Quaternion rot)
        {
            if (!_lifecycleOn) return false;

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckAtom(uid, type);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a peer atom: "
                    + VpbNetStorableWhitelist.Explain(v, type, type));
                return false;
            }

            if (!string.IsNullOrEmpty(subSceneRef)
                && !VpbNetStorableWhitelist.IsAllowedSubSceneRef(subSceneRef))
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a peer subscene reference: " + subSceneRef);
                return false;
            }

            if (Find(uid) != null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] the peer added " + uid
                    + " but this side already has an atom by that name, so it was not created here."
                    + " Rename one of them and add it again.");
                return false;
            }

            SuperController sc = SuperController.singleton;
            if (sc == null) return false;

            _remoteCreated.Add(uid);
            try { sc.StartCoroutine(CreateCo(sc, uid, type, subSceneRef, pos, rot)); }
            catch (Exception e)
            {
                _remoteCreated.Remove(uid);
                LogUtil.LogWarning("[VPB.Net] could not create " + uid + ": " + e.Message);
                return false;
            }
            return true;
        }

        IEnumerator CreateCo(SuperController sc, string uid, string type, string subSceneRef,
            Vector3 pos, Quaternion rot)
        {
            IEnumerator add = null;
            try { add = sc.AddAtomByType(type, uid, false, false, false); }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] AddAtomByType(" + type + ") failed: " + e.Message);
            }
            if (add != null) yield return sc.StartCoroutine(add);

            Atom created = Find(uid);
            if (created == null)
            {
                _remoteCreated.Remove(uid);
                LogUtil.LogWarning("[VPB.Net] the peer added " + uid + " of type " + type
                    + ", which this VaM does not have; that object is missing on this side");
                yield break;
            }

            try { created.SetOn(true); }
            catch { }
            _created++;
            _listDirty = true;

            Place(created, uid, pos, rot);

            if (string.IsNullOrEmpty(subSceneRef))
            {
                LogUtil.LogWarning("[VPB.Net] the peer added " + uid + " (" + type + ")");
                yield break;
            }

            yield return null;
            _subBaseline[uid] = subSceneRef;
            ExpectSubScene(uid, subSceneRef, RawSubScenePath(created));
            LoadSubScene(created, uid, subSceneRef);

            float until = Time.realtimeSinceStartup + SubScenePlaceSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;

                Atom still = Find(uid);
                if (still == null) yield break;

                Transform t = ControlOf(still);
                if (t == null) continue;
                if (!Drifted(t, pos, rot)) continue;

                Place(still, uid, pos, rot);
            }
        }

        void Place(Atom a, string uid, Vector3 pos, Quaternion rot)
        {
            Transform t = ControlOf(a);
            if (t == null) return;

            try
            {
                t.position = pos;
                t.rotation = rot;
            }
            catch { return; }

            Rigidbody rb = BodyOf(a);
            if (rb != null)
            {
                try
                {
                    rb.position = pos;
                    rb.rotation = rot;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }

            Baseline b;
            b.Pos = pos;
            b.Rot = rot;
            b.HoldUntil = Time.realtimeSinceStartup + HoldOffSeconds;
            _baseline[uid] = b;
        }

        void LoadSubScene(Atom atom, string uid, string reference)
        {
            SubScene sub = null;
            try { sub = atom.subSceneComponent; }
            catch { }
            if (sub == null)
            {
                LogUtil.LogWarning("[VPB.Net] " + uid + " is not a subscene, so " + reference + " was not loaded");
                return;
            }

            MethodInfo loader = ResolveSubSceneLoader(sub);
            if (loader == null)
            {
                LogUtil.LogWarning("[VPB.Net] this VaM has no SubScene.LoadSubSceneWithPath;"
                    + " " + uid + " was created empty");
                return;
            }

            LogUtil.LogWarning("[VPB.Net] the peer added subscene " + uid
                + " and this side is loading " + reference
                + " from its own library - anything that subscene carries, including plugins, runs here");

            try { loader.Invoke(sub, new object[] { reference }); }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] could not load " + reference + " into " + uid + ": " + e.Message
                    + " - that package is probably not installed here");
            }
        }

        public bool ApplyRemove(string uid)
        {
            if (!_lifecycleOn) return false;
            if (!VpbNetPropFrame.IsSendableUid(uid)) return false;

            Atom a = Find(uid);
            if (a == null) return false;
            if (!IsSyncable(a)) return false;

            SuperController sc = SuperController.singleton;
            if (sc == null) return false;

            _remoteCreated.Remove(uid);
            _baseline.Remove(uid);
            try
            {
                sc.RemoveAtom(a);
                _removed++;
                _listDirty = true;
                LogUtil.LogWarning("[VPB.Net] the peer removed " + uid);
                return true;
            }
            catch (Exception e)
            {
                LogUtil.LogWarning("[VPB.Net] could not remove " + uid + ": " + e.Message);
                return false;
            }
        }

        public void RefreshNow()
        {
            _listDirty = true;
            RefreshWatched();
        }

        void RefreshWatched()
        {
            if (!_listDirty) return;
            _listDirty = false;

            _watched.Clear();
            _watchedUid.Clear();
            _watchedT.Clear();
            _subAtoms.Clear();
            _subAtomUid.Clear();
            _skippedPerson = 0;
            _skippedInSubScene = 0;
            _skippedPlayerLocal = 0;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            List<Atom> all = null;
            try { all = sc.GetAtoms(); }
            catch { }
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (a == null || !IsSyncable(a, true)) continue;

                string uid = SafeUid(a);
                if (uid == null) continue;

                if (HasSubScene(a) && _subAtoms.Count < MaxSubSceneQueue * 2)
                {
                    _subAtoms.Add(a);
                    _subAtomUid.Add(uid);
                }

                Transform t = ControlOf(a);
                if (t == null) continue;

                _watched.Add(a);
                _watchedUid.Add(uid);
                _watchedT.Add(t);
                if (_watched.Count >= VpbNetPropLimits.MaxTracked) break;
            }
        }

        bool IsSyncable(Atom a)
        {
            return IsSyncable(a, false);
        }

        bool IsSyncable(Atom a, bool count)
        {
            VpbNetPropExclusion why = Exclusion(a, _localAvatar, _remoteAvatar);
            if (why == VpbNetPropExclusion.None) return true;
            if (!count) return false;

            if (why == VpbNetPropExclusion.Person) _skippedPerson++;
            else if (why == VpbNetPropExclusion.InSubScene) _skippedInSubScene++;
            else if (why == VpbNetPropExclusion.PlayerLocal) _skippedPlayerLocal++;
            return false;
        }

        // Shared exclusion for every sharing kind — param sync must not drift from position sync.
        public static VpbNetPropExclusion Exclusion(Atom a, Atom localAvatar, Atom remoteAvatar)
        {
            if (a == null) return VpbNetPropExclusion.Gone;
            if (a == localAvatar || a == remoteAvatar) return VpbNetPropExclusion.Person;

            try
            {
                if (SceneUtils.IsPersonLikeAtom(a)) return VpbNetPropExclusion.Person;
                if (a.containingSubScene != null
                    || VpbNetStorableWhitelist.IsSubSceneContentUid(a.uid))
                    return VpbNetPropExclusion.InSubScene;
                if (VpbNetStorableWhitelist.IsDeniedAtomType(a.type))
                    return VpbNetPropExclusion.PlayerLocal;
            }
            catch { return VpbNetPropExclusion.Gone; }

            return VpbNetPropExclusion.None;
        }

        // Position sync refuses CoreControl (their camera). Settings names it — Skyshop lives there.
        public static VpbNetPropExclusion ParamExclusion(Atom a, Atom localAvatar, Atom remoteAvatar)
        {
            if (a == null) return VpbNetPropExclusion.Gone;
            if (a == localAvatar || a == remoteAvatar) return VpbNetPropExclusion.Person;

            try
            {
                if (SceneUtils.IsPersonLikeAtom(a)) return VpbNetPropExclusion.Person;
                string uid = a.uid;
                if (a.containingSubScene != null
                    || VpbNetStorableWhitelist.IsSubSceneContentUid(uid))
                    return VpbNetPropExclusion.InSubScene;
                if (VpbNetStorableWhitelist.IsSceneLightingHost(uid, a.type))
                    return VpbNetPropExclusion.None;
                if (VpbNetStorableWhitelist.IsDeniedAtomType(a.type))
                    return VpbNetPropExclusion.PlayerLocal;
            }
            catch { return VpbNetPropExclusion.Gone; }

            return VpbNetPropExclusion.None;
        }

        static bool Drifted(Transform t, Vector3 pos, Quaternion rot)
        {
            if ((t.position - pos).sqrMagnitude > PositionEpsilon * PositionEpsilon) return true;
            return Quaternion.Angle(t.rotation, rot) > RotationEpsilonDeg;
        }

        static bool Moved(Baseline b, Vector3 pos, Quaternion rot)
        {
            if ((pos - b.Pos).sqrMagnitude > PositionEpsilon * PositionEpsilon) return true;
            return Quaternion.Angle(b.Rot, rot) > RotationEpsilonDeg;
        }

        static Transform ControlOf(Atom a)
        {
            try
            {
                FreeControllerV3 fc = a.mainController;
                if (fc != null)
                {
                    Transform c = fc.control;
                    return c != null ? c : fc.transform;
                }
                return a.transform;
            }
            catch { return null; }
        }

        static Rigidbody BodyOf(Atom a)
        {
            try
            {
                FreeControllerV3 fc = a.mainController;
                return fc == null ? null : fc.followWhenOffRB;
            }
            catch { return null; }
        }

        static Atom Find(string uid)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return null;
            try { return sc.GetAtomByUid(uid); }
            catch { return null; }
        }

        static string SafeUid(Atom a)
        {
            string uid = null;
            try { uid = a.uid; }
            catch { }
            return VpbNetPropFrame.IsSendableUid(uid) ? uid : null;
        }

        static SubScene SubSceneOf(Atom a)
        {
            try { return a.subSceneComponent; }
            catch { return null; }
        }

        static string RawSubScenePath(Atom a)
        {
            SubScene sub = SubSceneOf(a);
            if (sub == null) return string.Empty;

            string path = null;
            try
            {
                JSONStorableUrl url = sub.GetUrlJSONParam("storePath");
                if (url != null) path = url.val;
            }
            catch { }

            return string.IsNullOrEmpty(path) ? string.Empty : path;
        }

        static int SubScenePathStart(string s)
        {
            int i = 0;
            int c = s.IndexOf(':');
            if (c >= 0) i = c + 1;
            while (i < s.Length && (s[i] == '/' || s[i] == '\\')) i++;
            return i;
        }

        public static bool SameSubSceneFile(string a, string b)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (a.Length == 0 || b.Length == 0) return false;

            int i = SubScenePathStart(a);
            int j = SubScenePathStart(b);
            if (a.Length - i != b.Length - j) return false;

            while (i < a.Length)
            {
                char ca = a[i++];
                char cb = b[j++];
                if (ca == '\\') ca = '/';
                if (cb == '\\') cb = '/';
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
                if (ca != cb) return false;
            }
            return true;
        }

        static string SubSceneReference(Atom a)
        {
            string path = RawSubScenePath(a);
            if (path.Length == 0) return string.Empty;

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckSubSceneRef(path);
            if (v == VpbNetStorableVerdict.Allowed) return path;

            LogUtil.LogWarning("[VPB.Net] not sending the contents of subscene \"" + path
                + "\": " + VpbNetStorableWhitelist.Explain(v, path, path)
                + " - the other side will get an empty subscene");
            return string.Empty;
        }

        static MethodInfo ResolveSubSceneLoader(SubScene sub)
        {
            if (_resolvedLoader) return _miLoadSubSceneWithPath;
            _resolvedLoader = true;
            if (sub == null) return null;

            try
            {
                _miLoadSubSceneWithPath = sub.GetType().GetMethod("LoadSubSceneWithPath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new Type[] { typeof(string) }, null);
            }
            catch { _miLoadSubSceneWithPath = null; }

            return _miLoadSubSceneWithPath;
        }

        static bool IsSceneLoading()
        {
            try { return LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { return false; }
        }
    }
}
