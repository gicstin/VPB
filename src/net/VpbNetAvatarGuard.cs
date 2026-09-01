using System;
using UnityEngine;

namespace VPB
{
    public static class VpbNetAvatarGuard
    {
        public static bool Active
        {
            get
            {
                try { return VpbNetRuntime.IsEnabled && VpbNetPresence.IsActive && VpbNetPresence.PeerUp; }
                catch { return false; }
            }
        }

        public static bool IsPeerAvatar(Atom a)
        {
            if (a == null) return false;
            if (!Active) return false;

            string uid = null;
            try { uid = a.uid; }
            catch { return false; }

            try { return VpbNetPresence.IsPeers(uid); }
            catch { return false; }
        }

        public static Atom MyAvatar()
        {
            if (!Active) return null;

            string uid = null;
            try { uid = VpbNetPresence.MyAvatar; }
            catch { }
            if (string.IsNullOrEmpty(uid)) return null;

            try { return VpbNetAvatarRoster.Find(uid); }
            catch { return null; }
        }

        public static bool IsMyAvatar(Atom a)
        {
            if (a == null) return false;
            if (!Active) return false;

            string uid = null;
            try { uid = a.uid; }
            catch { return false; }

            try { return VpbNetPresence.IsMine(uid); }
            catch { return false; }
        }

        public static string PeerAvatarUid()
        {
            if (!Active) return null;
            try
            {
                string uid = VpbNetPresence.PeerAvatar;
                return string.IsNullOrEmpty(uid) ? null : uid;
            }
            catch { return null; }
        }
    }
}
