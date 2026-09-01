using System;
using System.Text;

namespace VpbNet
{
    // One-way grant — never intersect with peer.
    public static class VpbNetRuleDomain
    {
        public const byte Pose = 0;
        public const byte DualPose = 1;
        public const byte Clothing = 2;
        public const byte Hair = 3;
        public const byte Morphs = 4;
        public const byte Look = 5;
        public const byte Skin = 6;
        public const byte Scene = 7;
        public const byte Objects = 8;
        public const byte Params = 9;
        public const byte Triggers = 10;
        public const byte AvatarClaim = 11;

        // Fetch consent, not in-scene action.
        public const byte Content = 12;

        public const int Count = 13;

        public static bool IsKnown(byte d)
        {
            return d < Count;
        }

        public static string Name(byte d)
        {
            switch (d)
            {
                case Pose: return "pose";
                case DualPose: return "two-person pose";
                case Clothing: return "clothing";
                case Hair: return "hair";
                case Morphs: return "morphs";
                case Look: return "look";
                case Skin: return "skin";
                case Scene: return "scene";
                case Objects: return "objects";
                case Params: return "object settings";
                case Triggers: return "triggers";
                case AvatarClaim: return "avatar claim";
                case Content: return "missing content";
            }
            return "domain#" + d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Unknown action → refuse, no permissive fallback.
        public static bool FromPresetAction(string action, out byte domain)
        {
            domain = 0;
            if (action == null) return false;
            if (string.Equals(action, "LoadClothing", StringComparison.Ordinal)) { domain = Clothing; return true; }
            if (string.Equals(action, "LoadHair", StringComparison.Ordinal)) { domain = Hair; return true; }
            if (string.Equals(action, "LoadSkin", StringComparison.Ordinal)) { domain = Skin; return true; }
            if (string.Equals(action, "LoadMorphs", StringComparison.Ordinal)) { domain = Morphs; return true; }
            if (string.Equals(action, "LoadPose", StringComparison.Ordinal)) { domain = Pose; return true; }
            if (string.Equals(action, "LoadAppearance", StringComparison.Ordinal)) { domain = Look; return true; }
            return false;
        }
    }

    // Mirror = their body; Control = mine.
    public static class VpbNetRuleAxis
    {
        public const byte Mirror = 0;
        public const byte Control = 1;

        public const int Count = 2;

        public static string Name(byte a)
        {
            return a == Control ? "control" : "mirror";
        }
    }

    public static class VpbNetRuleLevel
    {
        public const byte Blocked = 0;
        public const byte Ask = 1;
        public const byte Allowed = 2;

        // Unknown bits → Blocked.
        public static byte Sanitize(byte v)
        {
            return v == Ask || v == Allowed ? v : Blocked;
        }

        public static string Name(byte v)
        {
            switch (Sanitize(v))
            {
                case Allowed: return "allowed";
                case Ask: return "ask";
            }
            return "blocked";
        }
    }

    public static class VpbNetRulePreset
    {
        public const byte LockedDown = 0;
        public const byte WatchTogether = 1;
        public const byte FullTrust = 2;
        public const byte Custom = 3;

        public static string Name(byte p)
        {
            switch (p)
            {
                case LockedDown: return "locked down";
                case WatchTogether: return "watch together";
                case FullTrust: return "full trust";
            }
            return "custom";
        }
    }

    // 24 two-bit lanes in Lo/Hi.
    public struct VpbNetRuleTable
    {
        public const byte TableVersion = 1;
        public const int WireBytes = 13;
        public const int LaneCount = 32;

        public uint Lo;
        public uint Hi;
        public uint Revision;

        public static VpbNetRuleTable DenyAll()
        {
            return Normalize(new VpbNetRuleTable());
        }

        // Missing table → old mirror-only grant.
        public static VpbNetRuleTable LegacyPeer()
        {
            return Normalize(new VpbNetRuleTable());
        }

        public static VpbNetRuleTable FromPreset(byte preset)
        {
            VpbNetRuleTable t = new VpbNetRuleTable();
            if (preset == VpbNetRulePreset.LockedDown) return Normalize(t);

            if (preset == VpbNetRulePreset.FullTrust)
            {
                for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
                    t.Set(d, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
                return Normalize(t);
            }

            t.Set(VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.AvatarClaim, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Content, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            t.Set(VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            return Normalize(t);
        }

        // Copy parent answer into child lanes older peers read.
        public static VpbNetRuleTable Normalize(VpbNetRuleTable t)
        {
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (HasAxis(d, VpbNetRuleAxis.Mirror))
                    t.Set(d, VpbNetRuleAxis.Mirror, VpbNetRuleLevel.Allowed);

                byte parent = CoveredBy(d);
                if (parent != NoParent)
                    t.Set(d, VpbNetRuleAxis.Control, t.Get(parent, VpbNetRuleAxis.Control));
            }
            return t;
        }

        public static byte MatchPreset(VpbNetRuleTable t)
        {
            if (SameLanes(t, FromPreset(VpbNetRulePreset.LockedDown))) return VpbNetRulePreset.LockedDown;
            if (SameLanes(t, FromPreset(VpbNetRulePreset.WatchTogether))) return VpbNetRulePreset.WatchTogether;
            if (SameLanes(t, FromPreset(VpbNetRulePreset.FullTrust))) return VpbNetRulePreset.FullTrust;
            return VpbNetRulePreset.Custom;
        }

        public static bool SameLanes(VpbNetRuleTable a, VpbNetRuleTable b)
        {
            return a.Lo == b.Lo && a.Hi == b.Hi;
        }

        // World domains: control only.
        public static bool HasAxis(byte domain, byte axis)
        {
            if (!VpbNetRuleDomain.IsKnown(domain)) return false;
            if (axis == VpbNetRuleAxis.Control) return true;
            if (axis != VpbNetRuleAxis.Mirror) return false;

            switch (domain)
            {
                case VpbNetRuleDomain.Pose:
                case VpbNetRuleDomain.Clothing:
                case VpbNetRuleDomain.Hair:
                case VpbNetRuleDomain.Morphs:
                case VpbNetRuleDomain.Look:
                case VpbNetRuleDomain.Skin:
                    return true;
            }
            return false;
        }

        public const byte NoParent = 255;

        // Parent owns children both ways.
        public static byte CoveredBy(byte domain)
        {
            switch (domain)
            {
                case VpbNetRuleDomain.Clothing:
                case VpbNetRuleDomain.Hair:
                case VpbNetRuleDomain.Morphs:
                case VpbNetRuleDomain.Skin:
                    return VpbNetRuleDomain.Look;
                case VpbNetRuleDomain.Params:
                case VpbNetRuleDomain.Triggers:
                    return VpbNetRuleDomain.Objects;
            }
            return NoParent;
        }

        // Parent lane for prompts/writes; enforcement uses Effective.
        public static byte Answerable(byte domain)
        {
            byte parent = CoveredBy(domain);
            return parent == NoParent ? domain : parent;
        }

        // Mirror and covered children are not player questions.
        public static bool IsEditable(byte domain, byte axis)
        {
            if (!HasAxis(domain, axis)) return false;
            if (axis == VpbNetRuleAxis.Mirror) return false;
            return CoveredBy(domain) == NoParent;
        }

        public byte Get(byte domain, byte axis)
        {
            int lane = Lane(domain, axis);
            if (lane < 0) return VpbNetRuleLevel.Blocked;
            uint word = lane < 16 ? Lo : Hi;
            int shift = (lane & 15) * 2;
            return VpbNetRuleLevel.Sanitize((byte)((word >> shift) & 3u));
        }

        public void Set(byte domain, byte axis, byte level)
        {
            int lane = Lane(domain, axis);
            if (lane < 0) return;
            uint v = VpbNetRuleLevel.Sanitize(level);
            int shift = (lane & 15) * 2;
            uint mask = 3u << shift;
            if (lane < 16) Lo = (Lo & ~mask) | (v << shift);
            else Hi = (Hi & ~mask) | (v << shift);
        }

        // Use this, not Get — parent cover + mirror always on.
        public byte Effective(byte domain, byte axis)
        {
            if (!HasAxis(domain, axis)) return VpbNetRuleLevel.Blocked;
            if (axis == VpbNetRuleAxis.Mirror) return VpbNetRuleLevel.Allowed;
            return Get(Answerable(domain), axis);
        }

        static int Lane(byte domain, byte axis)
        {
            if (!VpbNetRuleDomain.IsKnown(domain)) return -1;
            if (axis >= VpbNetRuleAxis.Count) return -1;
            int lane = domain * VpbNetRuleAxis.Count + axis;
            return lane < LaneCount ? lane : -1;
        }

        public static void Write(VpbNetEventWriter w, VpbNetRuleTable t)
        {
            if (w == null) return;
            w.WriteByte(TableVersion);
            w.WriteU32(t.Lo);
            w.WriteU32(t.Hi);
            w.WriteU32(t.Revision);
        }

        // Unknown version → refuse whole.
        public static bool Read(VpbNetEventReader r, out VpbNetRuleTable t)
        {
            t = new VpbNetRuleTable();
            if (r == null) return false;

            byte ver = r.ReadByte();
            uint lo = r.ReadU32();
            uint hi = r.ReadU32();
            uint rev = r.ReadU32();
            if (r.Failed || ver != TableVersion) return false;

            t.Lo = lo;
            t.Hi = hi;
            t.Revision = rev;
            return true;
        }

        public static void Describe(StringBuilder sb, VpbNetRuleTable t)
        {
            if (sb == null) return;
            bool any = false;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (CoveredBy(d) != NoParent) continue;
                byte level = t.Effective(d, VpbNetRuleAxis.Control);
                if (level == VpbNetRuleLevel.Blocked) continue;
                if (any) sb.Append(", ");
                sb.Append(VpbNetRuleDomain.Name(d));
                sb.Append(' ');
                sb.Append(VpbNetRuleLevel.Name(level));
                any = true;
            }
            if (!any) sb.Append("nothing of yours can be changed by them");
        }
    }
}
