using System;
using System.Collections.Generic;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public static class VpbNetAvatarRoster
    {
        public const int MaxAvatars = 32;
        const float RescanSeconds = 1f;

        static readonly List<string> _uids = new List<string>(MaxAvatars);
        static readonly List<Atom> _atoms = new List<Atom>(MaxAvatars);

        static bool _subscribed;
        static bool _dirty = true;
        static float _nextScan;
        static int _revision;

        public static int Count { get { return _uids.Count; } }
        public static int Revision { get { return _revision; } }

        public static string Uid(int i)
        {
            return i >= 0 && i < _uids.Count ? _uids[i] : null;
        }

        public static Atom AtomAt(int i)
        {
            return i >= 0 && i < _atoms.Count ? _atoms[i] : null;
        }

        public static void Poll()
        {
            EnsureSubscribed();

            float now = Time.realtimeSinceStartup;
            if (!_dirty && now < _nextScan) return;
            _nextScan = now + RescanSeconds;
            _dirty = false;
            Rescan();
        }

        public static void Invalidate()
        {
            _dirty = true;
        }

        public static Atom Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;

            for (int i = 0; i < _uids.Count; i++)
            {
                if (!string.Equals(_uids[i], uid, StringComparison.Ordinal)) continue;
                Atom a = _atoms[i];
                if (a != null) return a;
            }

            return Exists(uid);
        }

        public static Atom Exists(string uid)
        {
            Atom found = AnyAtom(uid);
            if (found == null) return null;
            return SceneUtils.IsPersonLikeAtom(found) ? found : null;
        }

        public static Atom AnyAtom(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;

            SuperController sc = SuperController.singleton;
            if (sc == null) return null;

            try { return sc.GetAtomByUid(uid); }
            catch { return null; }
        }

        public static bool Contains(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            for (int i = 0; i < _uids.Count; i++)
            {
                if (string.Equals(_uids[i], uid, StringComparison.Ordinal)) return true;
            }
            return Exists(uid) != null;
        }

        public static void Shutdown()
        {
            if (_subscribed)
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
                _subscribed = false;
            }

            _uids.Clear();
            _atoms.Clear();
            _dirty = true;
        }

        static void EnsureSubscribed()
        {
            if (_subscribed) return;
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                sc.onAtomAddedHandlers -= OnAtomChanged;
                sc.onAtomAddedHandlers += OnAtomChanged;
                sc.onAtomRemovedHandlers -= OnAtomChanged;
                sc.onAtomRemovedHandlers += OnAtomChanged;
                _subscribed = true;
            }
            catch { }
        }

        static void OnAtomChanged(Atom a)
        {
            _dirty = true;
        }

        static void Rescan()
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            List<Atom> all = null;
            try { all = sc.GetAtoms(); }
            catch { }
            if (all == null) return;

            bool changed = false;
            int kept = 0;

            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (a == null || !IsAvatar(a)) continue;

                string uid = null;
                try { uid = a.uid; }
                catch { }
                if (!VpbNetAvatarAssignment.IsValidUid(uid) || string.IsNullOrEmpty(uid)) continue;

                if (kept >= MaxAvatars) break;

                if (kept < _uids.Count)
                {
                    if (!string.Equals(_uids[kept], uid, StringComparison.Ordinal) || _atoms[kept] != a)
                    {
                        _uids[kept] = uid;
                        _atoms[kept] = a;
                        changed = true;
                    }
                }
                else
                {
                    _uids.Add(uid);
                    _atoms.Add(a);
                    changed = true;
                }
                kept++;
            }

            if (kept < _uids.Count)
            {
                _uids.RemoveRange(kept, _uids.Count - kept);
                _atoms.RemoveRange(kept, _atoms.Count - kept);
                changed = true;
            }

            if (changed) _revision++;
        }

        static bool IsAvatar(Atom a)
        {
            try
            {
                if (!a.on) return false;
                return SceneUtils.IsPersonLikeAtom(a);
            }
            catch { return false; }
        }
    }
}
