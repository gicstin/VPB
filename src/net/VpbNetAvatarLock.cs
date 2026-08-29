using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public sealed class VpbNetAvatarLock
    {
        sealed class Held
        {
            public Atom Atom;
            public FreeControllerV3[] Controllers;
            public bool[] GrabPosition;
            public bool[] GrabRotation;
            public bool[] InteractableInPlay;
            public bool[] Possessable;
        }

        readonly Dictionary<string, Held> _held = new Dictionary<string, Held>(8, StringComparer.Ordinal);
        readonly List<string> _scratch = new List<string>(8);

        bool _enabled = true;

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!_enabled) ReleaseAll();
            }
        }

        public int LockedCount { get { return _held.Count; } }

        // Alone, everything stays grabbable. Locks only when there is someone to arbitrate with.
        public void Apply(string myUid, bool active)
        {
            if (!_enabled || !active)
            {
                ReleaseAll();
                return;
            }

            if (myUid == null) myUid = string.Empty;

            for (int i = 0; i < VpbNetAvatarRoster.Count; i++)
            {
                string uid = VpbNetAvatarRoster.Uid(i);
                if (string.IsNullOrEmpty(uid)) continue;

                if (string.Equals(uid, myUid, StringComparison.Ordinal)) Release(uid);
                else Lock(uid, VpbNetAvatarRoster.AtomAt(i));
            }

            _scratch.Clear();
            foreach (KeyValuePair<string, Held> kv in _held)
            {
                if (!VpbNetAvatarRoster.Contains(kv.Key)) _scratch.Add(kv.Key);
            }
            for (int i = 0; i < _scratch.Count; i++) Release(_scratch[i]);
            _scratch.Clear();
        }

        void Lock(string uid, Atom atom)
        {
            if (atom == null) return;

            Held prior;
            if (_held.TryGetValue(uid, out prior))
            {
                if (prior.Atom == atom) return;
                Release(uid);
            }

            FreeControllerV3[] all = null;
            try { all = atom.freeControllers; }
            catch { }
            if (all == null || all.Length == 0) return;

            Held h = new Held();
            h.Atom = atom;
            h.Controllers = all;
            h.GrabPosition = new bool[all.Length];
            h.GrabRotation = new bool[all.Length];
            h.InteractableInPlay = new bool[all.Length];
            h.Possessable = new bool[all.Length];

            for (int i = 0; i < all.Length; i++)
            {
                FreeControllerV3 fc = all[i];
                if (fc == null) continue;
                try
                {
                    h.GrabPosition[i] = fc.canGrabPosition;
                    h.GrabRotation[i] = fc.canGrabRotation;
                    h.InteractableInPlay[i] = fc.interactableInPlayMode;
                    h.Possessable[i] = fc.possessable;

                    DropPossession(fc);

                    fc.canGrabPosition = false;
                    fc.canGrabRotation = false;
                    fc.interactableInPlayMode = false;
                    fc.possessable = false;
                }
                catch { }
            }

            _held[uid] = h;
            LogUtil.LogWarning("[VPB.Net] " + uid + " is not yours to move; its controls are locked and it cannot be possessed here");
        }

        static void DropPossession(FreeControllerV3 fc)
        {
            bool on = false;
            try { on = fc.possessed || fc.startedPossess; }
            catch { }
            if (!on) return;

            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try { sc.ClearPossess(false, fc); }
            catch { }
        }

        void Release(string uid)
        {
            Held h;
            if (!_held.TryGetValue(uid, out h)) return;
            _held.Remove(uid);
            Restore(h);
            LogUtil.LogWarning("[VPB.Net] " + uid + " is yours again; its controls are unlocked");
        }

        public void ReleaseAll()
        {
            if (_held.Count == 0) return;

            foreach (KeyValuePair<string, Held> kv in _held) Restore(kv.Value);
            _held.Clear();
        }

        static void Restore(Held h)
        {
            if (h == null || h.Controllers == null) return;
            for (int i = 0; i < h.Controllers.Length; i++)
            {
                FreeControllerV3 fc = h.Controllers[i];
                if (fc == null) continue;
                try
                {
                    fc.canGrabPosition = h.GrabPosition[i];
                    fc.canGrabRotation = h.GrabRotation[i];
                    fc.interactableInPlayMode = h.InteractableInPlay[i];
                    fc.possessable = h.Possessable[i];
                }
                catch { }
            }
        }
    }
}
