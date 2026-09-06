using System;

namespace VPB
{
    internal enum VpbBodyRegion : byte
    {
        Head = 0,
        Neck = 1,
        Chest = 2,
        Abdomen = 3,
        Hip = 4,
        Groin = 5,
        Shoulder = 6,
        Forearm = 7,
        Hand = 8,
        Thigh = 9,
        Shin = 10,
        Foot = 11,
        Toe = 12,
        Other = 13,

        Count = 14,
        Unknown = 0xFF,
    }

    internal static class VpbBodyRegions
    {
        internal const int Count = (int)VpbBodyRegion.Count;

        private struct Rule
        {
            internal readonly string Token;
            internal readonly VpbBodyRegion Region;
            internal Rule(string token, VpbBodyRegion region) { Token = token; Region = region; }
        }

        private static readonly Rule[] Rules =
        {
            new Rule("forearm", VpbBodyRegion.Forearm),
            new Rule("collar", VpbBodyRegion.Shoulder),
            new Rule("shldr", VpbBodyRegion.Shoulder),
            new Rule("shoulder", VpbBodyRegion.Shoulder),
            new Rule("upperarm", VpbBodyRegion.Shoulder),

            new Rule("toe", VpbBodyRegion.Toe),
            new Rule("foot", VpbBodyRegion.Foot),
            new Rule("metatarsal", VpbBodyRegion.Foot),
            new Rule("heel", VpbBodyRegion.Foot),
            new Rule("ankle", VpbBodyRegion.Foot),
            new Rule("shin", VpbBodyRegion.Shin),
            new Rule("calf", VpbBodyRegion.Shin),
            new Rule("knee", VpbBodyRegion.Shin),
            new Rule("thigh", VpbBodyRegion.Thigh),

            new Rule("thumb", VpbBodyRegion.Hand),
            new Rule("index", VpbBodyRegion.Hand),
            new Rule("mid", VpbBodyRegion.Hand),
            new Rule("ring", VpbBodyRegion.Hand),
            new Rule("pinky", VpbBodyRegion.Hand),
            new Rule("carpal", VpbBodyRegion.Hand),
            new Rule("finger", VpbBodyRegion.Hand),
            new Rule("hand", VpbBodyRegion.Hand),

            new Rule("penis", VpbBodyRegion.Groin),
            new Rule("testes", VpbBodyRegion.Groin),
            new Rule("testicle", VpbBodyRegion.Groin),
            new Rule("genital", VpbBodyRegion.Groin),
            new Rule("labia", VpbBodyRegion.Groin),
            new Rule("vagina", VpbBodyRegion.Groin),
            new Rule("anus", VpbBodyRegion.Groin),

            new Rule("pectoral", VpbBodyRegion.Chest),
            new Rule("nipple", VpbBodyRegion.Chest),
            new Rule("breast", VpbBodyRegion.Chest),
            new Rule("chest", VpbBodyRegion.Chest),
            new Rule("abdomen", VpbBodyRegion.Abdomen),
            new Rule("belly", VpbBodyRegion.Abdomen),
            new Rule("pelvis", VpbBodyRegion.Hip),
            new Rule("glute", VpbBodyRegion.Hip),
            new Rule("hip", VpbBodyRegion.Hip),

            new Rule("neck", VpbBodyRegion.Neck),
            new Rule("head", VpbBodyRegion.Head),
            new Rule("eye", VpbBodyRegion.Head),
            new Rule("ear", VpbBodyRegion.Head),
            new Rule("jaw", VpbBodyRegion.Head),
            new Rule("teeth", VpbBodyRegion.Head),
            new Rule("tongue", VpbBodyRegion.Head),
            new Rule("lip", VpbBodyRegion.Head),
            new Rule("nose", VpbBodyRegion.Head),
            new Rule("brow", VpbBodyRegion.Head),
            new Rule("skull", VpbBodyRegion.Head),
        };

        internal static VpbBodyRegion FromBoneId(string boneId)
        {
            if (string.IsNullOrEmpty(boneId)) return VpbBodyRegion.Unknown;
            string s = boneId.ToLowerInvariant();
            for (int i = 0; i < Rules.Length; i++)
            {
                if (s.IndexOf(Rules[i].Token, StringComparison.Ordinal) >= 0)
                    return Rules[i].Region;
            }
            return VpbBodyRegion.Other;
        }

    }
}
