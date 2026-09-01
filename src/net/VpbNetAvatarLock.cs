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

        readonly Dictionary<string, Held> _held = new Dictionary<string, Held>(4, StringComparer.Ordinal);
        readonly List<string> _scratch = new List<string>(4);

        bool _enabled = true;
        string _lastMine = string.Empty;
        string _lastTheirs = string.Empty;
        bool _lastActive;

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

        // Only the seat the other player rides is locked. Unclaimed Persons stay the user's to move.
        public void Apply(string myUid, string peerUid, bool active)
        {
            if (myUid == null) myUid = string.Empty;
            if (peerUid == null) peerUid = string.Empty;

            if (!_enabled || !active)
            {
                ReleaseAll();
                return;
            }

            if (_lastActive
                && string.Equals(myUid, _lastMine, StringComparison.Ordinal)
                && string.Equals(peerUid, _lastTheirs, StringComparison.Ordinal)
                && HeldIsCurrent(peerUid))
                return;

            _lastMine = myUid;
            _lastTheirs = peerUid;
            _lastActive = true;

            _scratch.Clear();
            foreach (KeyValuePair<string, Held> kv in _held)
            {
                if (!string.Equals(kv.Key, peerUid, StringComparison.Ordinal)) _scratch.Add(kv.Key);
            }
            for (int i = 0; i < _scratch.Count; i++) Release(_scratch[i]);
            _scratch.Clear();

            if (peerUid.Length == 0 || string.Equals(peerUid, myUid, StringComparison.Ordinal)) return;
            Lock(peerUid, VpbNetAvatarRoster.Find(peerUid));
        }

        bool HeldIsCurrent(string peerUid)
        {
            if (peerUid.Length == 0) return _held.Count == 0;
            if (_held.Count != 1) return false;

            Held h;
            if (!_held.TryGetValue(peerUid, out h)) return false;
            return h != null && h.Atom != null;
        }

        void Forget()
        {
            _lastMine = string.Empty;
            _lastTheirs = string.Empty;
            _lastActive = false;
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

            bool dropped = false;
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

                    if (DropPossession(fc)) dropped = true;

                    fc.canGrabPosition = false;
                    fc.canGrabRotation = false;
                    fc.interactableInPlayMode = false;
                    fc.possessable = false;
                }
                catch { }
            }

            _held[uid] = h;

            if (dropped)
                LogUtil.LogWarning("[VPB.Net] you were possessing " + uid
                    + ", and the other player has just taken it, so your possession was let go."
                    + " Nobody else in this scene is locked - possess one of them instead.");
            else
                LogUtil.LogWarning("[VPB.Net] " + uid
                    + " is the person the other player is riding; its controls are locked here."
                    + " Everyone else in this scene stays yours to move.");
        }

        static bool DropPossession(FreeControllerV3 fc)
        {
            bool on = false;
            try { on = fc.possessed || fc.startedPossess; }
            catch { }
            if (!on) return false;

            SuperController sc = SuperController.singleton;
            if (sc == null) return false;
            try { sc.ClearPossess(false, fc); }
            catch { return false; }
            return true;
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
            Forget();
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
