using System;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public delegate bool VpbNetDualPoseSend(
        string reference, int senderRole, int receiverRole, VpbDualPose.Anchor anchor);

    /// <summary>Each side applies its half locally; file named, never sent.</summary>
    public static class VpbNetDualPoseRelay
    {
        static VpbNetDualPoseSend _sender;
        static int _sent;
        static int _applied;
        static int _refused;

        public static int Sent { get { return _sent; } }
        public static int Applied { get { return _applied; } }
        public static int Refused { get { return _refused; } }

        // Advisory: their table re-checks. Hide the split if they will drop it.
        public static bool PeerWouldTakeAHalf
        {
            get
            {
                try { return VpbNetRulebook.PeerWouldAccept(VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control); }
                catch { return false; }
            }
        }

        public static bool PeerIsRiding
        {
            get
            {
                try { return !string.IsNullOrEmpty(VpbNetPresence.PeerAvatar); }
                catch { return false; }
            }
        }

        public static Atom MyAvatar()
        {
            try { return VpbNetAvatarGuard.MyAvatar(); }
            catch { return null; }
        }

        public static string PeerLabel()
        {
            string who = null;
            try { who = VpbNetPresence.PeerName; }
            catch { }
            if (string.IsNullOrEmpty(who)) who = VPBTranslation.T("gallery.dual_pose.peer", "the other player");
            return who;
        }

        public static void SetSender(VpbNetDualPoseSend sender)
        {
            _sender = sender;
        }

        public static void ResetCounters()
        {
            _sent = 0;
            _applied = 0;
            _refused = 0;
        }

        /// <summary>False if the datagram did not go out — do not apply an unmatched half.</summary>
        public static bool Send(VpbDualPoseFile file, int myRole, VpbDualPose.Anchor anchor, out string why)
        {
            why = null;
            if (file == null)
            {
                why = "there is no pose to send";
                return false;
            }
            if (_sender == null || !VpbNetAvatarGuard.Active)
            {
                why = "you are not in a session";
                return false;
            }
            if (!PeerWouldTakeAHalf)
            {
                why = "their session rules do not let you start a two-person pose on them";
                return false;
            }

            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckPresetRef(file.Reference);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                why = VpbNetStorableWhitelist.Explain(v, file.Reference, file.Reference);
                return false;
            }

            int peerRole = myRole == 0 ? 1 : 0;
            bool ok = false;
            try { ok = _sender(file.Reference, myRole, peerRole, anchor); }
            catch { ok = false; }
            if (!ok)
            {
                why = "the message did not fit one datagram";
                return false;
            }

            _sent++;
            LogUtil.LogWarning("[VPB.Net] two-person pose " + file.Reference
                + ": you take " + VpbDualPose.RoleLabel(file, myRole)
                + ", " + PeerLabel() + " takes " + VpbDualPose.RoleLabel(file, peerRole));
            return true;
        }

        /// <summary>Lands on the avatar this side already rides.</summary>
        public static bool Apply(string reference, int myRole, VpbDualPose.Anchor anchor)
        {
            VpbNetStorableVerdict v = VpbNetStorableWhitelist.CheckPresetRef(reference);
            if (v != VpbNetStorableVerdict.Allowed)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a two-person pose: " + reference + " - "
                    + VpbNetStorableWhitelist.Explain(v, reference, reference));
                return false;
            }

            Atom mine = MyAvatar();
            if (mine == null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] the other player applied the two-person pose "
                    + reference + " but you are not riding an avatar, so there is no body here"
                    + " to take the other half.");
                return false;
            }

            FileEntry entry = VpbNetPresetRelay.ResolveEntry(reference);
            if (entry == null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] the other player applied the two-person pose "
                    + reference + " but that file is not installed here, so only their half"
                    + " of it happened.");
                return false;
            }

            // Busy before restore — blocked thread cannot ping; peer would assume the worst.
            VpbNetBusy.Begin(VpbNetBusyKind.DualPose, VpbNetPresetRelay.BusySeconds("LoadPose"));
            try
            {
                return ApplyHalf(reference, myRole, anchor, mine, entry);
            }
            finally
            {
                VpbNetBusy.End();
            }
        }

        static bool ApplyHalf(string reference, int myRole, VpbDualPose.Anchor anchor, Atom mine, FileEntry entry)
        {
            string why;
            VpbDualPoseFile file = VpbDualPose.Load(reference, entry, out why);
            if (file == null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] the other player applied the two-person pose "
                    + reference + " but this machine's copy " + (why ?? "could not be read"));
                return false;
            }

            VpbDualPoseRole role = file.Role(myRole);
            if (role == null)
            {
                _refused++;
                LogUtil.LogWarning("[VPB.Net] refused a two-person pose: it named half "
                    + myRole + ", which does not exist");
                return false;
            }

            int stripped = StripPlugins(role.Data);

            LogUtil.LogWarning("[VPB.Net] the other player applied " + reference
                + "; taking " + VpbDualPose.RoleLabel(file, myRole) + " onto " + mine.uid
                + (stripped > 0
                    ? " (" + stripped + " storable(s) naming a plugin dropped first - a pose the"
                        + " other player picked never runs code here)"
                    : string.Empty));

            if (!VpbDualPose.Apply(mine, role, anchor, "the other player applied a two-person pose"))
            {
                _refused++;
                return false;
            }

            // Sampler did not ask for this jump — peer must keyframe, not interpolate across the room.
            try { VpbNetPresence.MarkLocalPoseJump(); }
            catch { }

            _applied++;
            return true;
        }

        /// <summary>Strip plugin storables before Restore.</summary>
        static int StripPlugins(JSONClass data)
        {
            if (data == null) return 0;

            JSONArray storables = data["storables"] as JSONArray;
            if (storables == null) return 0;

            int dropped = 0;
            for (int i = storables.Count - 1; i >= 0; i--)
            {
                JSONClass s = storables[i] as JSONClass;
                if (s == null) continue;

                bool named = false;
                try { named = VpbNetPresetRelay.NamesPlugin(s, 0); }
                catch { named = true; }
                if (!named) continue;

                try
                {
                    storables.Remove(i);
                    dropped++;
                }
                catch { }
            }
            return dropped;
        }
    }
}
