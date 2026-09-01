using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetCapability
    {
        public const uint Events = 1u << 0;
        public const uint Keyframe = 1u << 1;
        public const uint Fingers = 1u << 2;
        public const uint Eyes = 1u << 3;
        public const uint Jaw = 1u << 4;
        public const uint Props = 1u << 5;
        public const uint Voice = 1u << 6;
        public const uint Contract = 1u << 7;
        public const uint Triggers = 1u << 8;
        public const uint Atoms = 1u << 9;
        public const uint Params = 1u << 10;

        // Peer without Rules is older — use LegacyPeer, not this bit.
        public const uint Rules = 1u << 11;

        // Peer without Content still gets the old contract/missing-list path.
        public const uint Content = 1u << 12;

        public const uint Local = Events | Keyframe | Contract | Rules | Content;

        public const uint FidelityTier = Fingers | Eyes | Jaw;

        public static uint LocalWith(bool fingers, bool eyes, bool jaw)
        {
            return LocalWith(fingers, eyes, jaw, false);
        }

        public static uint LocalWith(bool fingers, bool eyes, bool jaw, bool triggers)
        {
            return LocalWith(fingers, eyes, jaw, triggers, false, false);
        }

        public static uint LocalWith(bool fingers, bool eyes, bool jaw, bool triggers, bool props, bool atoms)
        {
            return LocalWith(fingers, eyes, jaw, triggers, props, atoms, false);
        }

        public static uint LocalWith(bool fingers, bool eyes, bool jaw, bool triggers, bool props,
            bool atoms, bool parameters)
        {
            uint bits = Local;
            if (fingers) bits |= Fingers;
            if (eyes) bits |= Eyes;
            if (jaw) bits |= Jaw;
            if (triggers) bits |= Triggers;
            if (props) bits |= Props;
            if (atoms) bits |= Atoms;
            if (parameters) bits |= Params;
            return bits;
        }

        public static uint Intersect(uint mine, uint theirs)
        {
            return mine & theirs;
        }

        public static void Describe(StringBuilder sb, uint bits)
        {
            if (sb == null) return;
            if (bits == 0)
            {
                sb.Append("none");
                return;
            }
            bool any = false;
            Append(sb, bits, Events, "events", ref any);
            Append(sb, bits, Keyframe, "keyframe", ref any);
            Append(sb, bits, Fingers, "fingers", ref any);
            Append(sb, bits, Eyes, "eyes", ref any);
            Append(sb, bits, Jaw, "jaw", ref any);
            Append(sb, bits, Props, "props", ref any);
            Append(sb, bits, Voice, "voice", ref any);
            Append(sb, bits, Contract, "contract", ref any);
            Append(sb, bits, Triggers, "triggers", ref any);
            Append(sb, bits, Atoms, "atoms", ref any);
            Append(sb, bits, Params, "params", ref any);
            Append(sb, bits, Rules, "rules", ref any);
            Append(sb, bits, Content, "content", ref any);
        }

        static void Append(StringBuilder sb, uint bits, uint bit, string name, ref bool any)
        {
            if ((bits & bit) == 0) return;
            if (any) sb.Append('+');
            sb.Append(name);
            any = true;
        }
    }

    public static class VpbNetRigId
    {
        public const ushort None = 0;

        public const ushort VamPerson17 = 1;

        public static string Name(ushort id)
        {
            switch (id)
            {
                case None: return "none";
                case VamPerson17: return "vam1-person-17";
            }
            return "rig#" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public enum VpbNetRigCompat
    {
        Ok = 0,
        NoDescriptor = 1,
        RigMismatch = 2,
        CountMismatch = 3,
        LayoutMismatch = 4,
        PoseVersionMismatch = 5
    }

    public struct VpbNetRigDescriptor
    {
        public ushort RigId;
        public byte PoseProtoVersion;
        public byte ControllerCount;
        public uint LayoutHash;
        public uint Capabilities;

        public bool IsPresent { get { return RigId != VpbNetRigId.None && ControllerCount > 0; } }
    }

    public static class VpbNetRig
    {
        public const int WireBytes = 8;

        public static uint ComputeLayoutHash(string[] names)
        {
            if (names == null) return 0u;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < names.Length; i++)
                {
                    string n = names[i];
                    if (n != null)
                    {
                        for (int c = 0; c < n.Length; c++)
                        {
                            h ^= (byte)n[c];
                            h *= 16777619u;
                        }
                    }
                    h ^= 0x1F;
                    h *= 16777619u;
                }
                return h;
            }
        }

        public static VpbNetRigDescriptor Describe(ushort rigId, string[] names, uint capabilities)
        {
            VpbNetRigDescriptor d = new VpbNetRigDescriptor();
            d.RigId = rigId;
            d.PoseProtoVersion = VpbPose.ProtoVersion;
            d.ControllerCount = (byte)(names == null ? 0 : (names.Length > 255 ? 255 : names.Length));
            d.LayoutHash = ComputeLayoutHash(names);
            d.Capabilities = capabilities;
            return d;
        }

        public static VpbNetRigCompat Check(VpbNetRigDescriptor mine, VpbNetRigDescriptor theirs)
        {
            if (!theirs.IsPresent) return VpbNetRigCompat.NoDescriptor;
            if (mine.RigId != theirs.RigId) return VpbNetRigCompat.RigMismatch;
            if (mine.PoseProtoVersion != theirs.PoseProtoVersion) return VpbNetRigCompat.PoseVersionMismatch;
            if (mine.ControllerCount != theirs.ControllerCount) return VpbNetRigCompat.CountMismatch;
            if (mine.LayoutHash != theirs.LayoutHash) return VpbNetRigCompat.LayoutMismatch;
            return VpbNetRigCompat.Ok;
        }

        public static bool IsFatal(VpbNetRigCompat c)
        {
            return c != VpbNetRigCompat.Ok && c != VpbNetRigCompat.NoDescriptor;
        }

        public static string Explain(VpbNetRigCompat c, VpbNetRigDescriptor mine, VpbNetRigDescriptor theirs)
        {
            switch (c)
            {
                case VpbNetRigCompat.Ok:
                    return "rig " + VpbNetRigId.Name(mine.RigId) + " matches";
                case VpbNetRigCompat.NoDescriptor:
                    return "peer did not send a rig descriptor; assuming " + VpbNetRigId.Name(mine.RigId);
                case VpbNetRigCompat.RigMismatch:
                    return "that peer is running a different body type ("
                        + VpbNetRigId.Name(theirs.RigId) + " against your " + VpbNetRigId.Name(mine.RigId)
                        + "). They cannot share a session.";
                case VpbNetRigCompat.PoseVersionMismatch:
                    return "that peer speaks pose format v" + theirs.PoseProtoVersion
                        + " and you speak v" + mine.PoseProtoVersion
                        + ". Update whichever VPB is older.";
                case VpbNetRigCompat.CountMismatch:
                    return "that peer sends " + theirs.ControllerCount + " controllers and you expect "
                        + mine.ControllerCount + ". Update whichever VPB is older.";
                case VpbNetRigCompat.LayoutMismatch:
                    return "that peer's controller layout differs from yours at the same count ("
                        + mine.ControllerCount + "). Update whichever VPB is older.";
            }
            return "rig check failed";
        }

        public static void Write(VpbNetEventWriter w, VpbNetRigDescriptor d)
        {
            if (w == null) return;
            w.WriteU16(d.RigId);
            w.WriteByte(d.PoseProtoVersion);
            w.WriteByte(d.ControllerCount);
            w.WriteU32(d.LayoutHash);
        }

        public static VpbNetRigDescriptor Read(VpbNetEventReader r, uint capabilities)
        {
            VpbNetRigDescriptor d = new VpbNetRigDescriptor();
            if (r == null || r.Remaining < WireBytes) return d;

            d.RigId = (ushort)r.ReadU16();
            d.PoseProtoVersion = r.ReadByte();
            d.ControllerCount = r.ReadByte();
            d.LayoutHash = r.ReadU32();
            d.Capabilities = capabilities;
            if (r.Failed)
            {
                d.RigId = VpbNetRigId.None;
                d.ControllerCount = 0;
            }
            return d;
        }
    }
}
