using System;

namespace VpbNet
{
    public static class VpbNetAvatarClaimKind
    {
        public const byte Request = 0;
        public const byte State = 1;
        public const byte Deny = 2;
    }

    public static class VpbNetClaimDeny
    {
        public const byte Blocked = 0;
        public const byte Taken = 1;
        public const byte Vetoed = 2;
        public const byte BadIdentifier = 3;
        public const byte Missing = 4;
        public const byte Count = 5;

        public static string Explain(byte reason, string uid)
        {
            string who = string.IsNullOrEmpty(uid) ? "that Person" : uid;
            switch (reason)
            {
                case Blocked:
                    return "They do not let a visitor ride a Person in their scene.";
                case Taken:
                    return who + " is already taken by the other player.";
                case Vetoed:
                    return "They said no to " + who + ".";
                case BadIdentifier:
                    return "Their build will not accept that name for an avatar.";
                case Missing:
                    return who + " is not in their scene.";
            }
            return "They refused " + who + ".";
        }
    }

    public enum VpbNetClaimResult
    {
        Granted = 0,
        Released = 1,
        Taken = 2,
        Unchanged = 3,
        BadIdentifier = 4
    }

    public sealed class VpbNetAvatarAssignment
    {
        public const int MaxUidChars = 96;

        public const int SeatA = 0;
        public const int SeatB = 1;
        public const int SeatCount = 2;
        public const int Unseated = -1;

        readonly string[] _seats = new string[SeatCount];
        uint _generation;

        public VpbNetAvatarAssignment()
        {
            for (int i = 0; i < SeatCount; i++) _seats[i] = string.Empty;
        }

        public uint Generation { get { return _generation; } }

        public static bool IsSeat(int seat) { return seat >= 0 && seat < SeatCount; }
        public static int OtherSeat(int seat)
        {
            if (!IsSeat(seat)) return Unseated;
            return seat == SeatA ? SeatB : SeatA;
        }
        public static string SeatName(int seat)
        {
            return seat == SeatA ? "A" : (seat == SeatB ? "B" : "?");
        }

        public string SeatUid(int seat) { return IsSeat(seat) ? _seats[seat] : string.Empty; }
        public bool IsSpectator(int seat) { return SeatUid(seat).Length == 0; }

        public void Reset()
        {
            for (int i = 0; i < SeatCount; i++) _seats[i] = string.Empty;
            _generation = 0;
        }

        public static bool IsValidUid(string uid)
        {
            if (uid == null) return false;
            if (uid.Length == 0) return true;
            if (uid.Length > MaxUidChars) return false;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid)) return false;
            return !VpbNetEventCodec.IsPluginReference(uid);
        }

        public bool IsFree(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            for (int i = 0; i < SeatCount; i++)
            {
                if (string.Equals(_seats[i], uid, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public string SoleFreeUid(string[] uids, int count)
        {
            if (uids == null) return null;
            if (count > uids.Length) count = uids.Length;

            string only = null;
            for (int i = 0; i < count; i++)
            {
                if (!IsFree(uids[i])) continue;
                if (only != null) return null;
                only = uids[i];
            }
            return only;
        }

        public bool IsClaimedByAnotherSeat(int seat, string uid)
        {
            if (uid == null || uid.Length == 0) return false;
            for (int i = 0; i < SeatCount; i++)
            {
                if (i == seat) continue;
                if (string.Equals(_seats[i], uid, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public VpbNetClaimResult Arbitrate(int seat, string uid)
        {
            if (!IsSeat(seat) || !IsValidUid(uid)) return VpbNetClaimResult.BadIdentifier;

            if (string.Equals(_seats[seat], uid, StringComparison.Ordinal))
                return VpbNetClaimResult.Unchanged;

            if (IsClaimedByAnotherSeat(seat, uid)) return VpbNetClaimResult.Taken;

            _seats[seat] = uid;
            _generation++;

            return uid.Length == 0 ? VpbNetClaimResult.Released : VpbNetClaimResult.Granted;
        }

        public bool AcceptState(uint generation, string uidA, string uidB)
        {
            if (!IsValidUid(uidA) || !IsValidUid(uidB)) return false;
            if (uidA.Length > 0 && string.Equals(uidA, uidB, StringComparison.Ordinal)) return false;
            if (_generation != 0 && (int)(generation - _generation) < 0) return false;

            _seats[SeatA] = uidA;
            _seats[SeatB] = uidB;
            _generation = generation;
            return true;
        }

        public void ClearSeat(int seat)
        {
            if (!IsSeat(seat) || _seats[seat].Length == 0) return;
            _seats[seat] = string.Empty;
            _generation++;
        }

        public static string Explain(VpbNetClaimResult r, string uid)
        {
            switch (r)
            {
                case VpbNetClaimResult.Granted:
                    return "riding " + uid;
                case VpbNetClaimResult.Released:
                    return "spectating";
                case VpbNetClaimResult.Taken:
                    return uid + " is already taken by the other player";
                case VpbNetClaimResult.Unchanged:
                    return "already riding " + (string.IsNullOrEmpty(uid) ? "nobody" : uid);
                case VpbNetClaimResult.BadIdentifier:
                    return "that is not a name this build will accept for an avatar";
            }
            return "the claim was refused";
        }
    }
}
