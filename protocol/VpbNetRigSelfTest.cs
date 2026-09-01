using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRigSelfTest
    {
        static readonly string[] Vam17 =
        {
            "control", "hipControl", "pelvisControl", "chestControl", "headControl",
            "rHandControl", "lHandControl", "rFootControl", "lFootControl",
            "rElbowControl", "lElbowControl", "rKneeControl", "lKneeControl",
            "rThighControl", "lThighControl", "rArmControl", "lArmControl",
        };

        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(8192);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== rig descriptor + capability self-test =====");

            HashStability(log, ref pass, ref fail);
            HashSensitivity(log, ref pass, ref fail);
            Compatibility(log, ref pass, ref fail);
            Capabilities(log, ref pass, ref fail);
            RoundTrip(log, ref pass, ref fail);
            Explanations(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 negotiated  controller count travels on the wire and is checked : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/3 legible     every mismatch names a cause and a fix              : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/3 compatible  a peer with no descriptor still joins               : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, fail == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Line(log, "===== end rig self-test =====");
            return fail == 0;
        }

        static void HashStability(StringBuilder log, ref int pass, ref int fail)
        {
            uint a = VpbNetRig.ComputeLayoutHash(Vam17);
            uint b = VpbNetRig.ComputeLayoutHash(Vam17);

            Check(log, ref pass, ref fail, a == b && a != 0u,
                "layout hash is deterministic and non-zero for the VaM 17-controller set (0x" + a.ToString("X8") + ")",
                "layout hash unstable or zero: " + a.ToString("X8") + " vs " + b.ToString("X8"));

            Check(log, ref pass, ref fail, VpbNetRig.ComputeLayoutHash(null) == 0u,
                "a null name array hashes to 0, which reads as no descriptor rather than a false match",
                "null name array did not hash to 0");
        }

        static void HashSensitivity(StringBuilder log, ref int pass, ref int fail)
        {
            uint baseline = VpbNetRig.ComputeLayoutHash(Vam17);

            string[] renamed = (string[])Vam17.Clone();
            renamed[4] = "HeadControl";
            Check(log, ref pass, ref fail, VpbNetRig.ComputeLayoutHash(renamed) != baseline,
                "a single-character case change in one controller name changes the layout hash",
                "renaming headControl -> HeadControl did not change the hash");

            string[] reordered = (string[])Vam17.Clone();
            string t = reordered[5];
            reordered[5] = reordered[6];
            reordered[6] = t;
            Check(log, ref pass, ref fail, VpbNetRig.ComputeLayoutHash(reordered) != baseline,
                "swapping rHandControl and lHandControl changes the hash, so a reorder cannot pass as a match",
                "a reordered controller set produced the same hash - a swapped-limb session would connect");

            string[] shorter = new string[Vam17.Length - 1];
            Array.Copy(Vam17, shorter, shorter.Length);
            Check(log, ref pass, ref fail, VpbNetRig.ComputeLayoutHash(shorter) != baseline,
                "dropping a controller changes the hash",
                "a shortened controller set produced the same hash");

            uint x = VpbNetRig.ComputeLayoutHash(new string[] { "ab", "c" });
            uint y = VpbNetRig.ComputeLayoutHash(new string[] { "a", "bc" });
            Check(log, ref pass, ref fail, x != y,
                "names are delimited in the hash, so {ab,c} and {a,bc} do not collide",
                "name boundaries are not hashed: {ab,c} and {a,bc} both gave 0x" + x.ToString("X8"));
        }

        static void Compatibility(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRigDescriptor mine = VpbNetRig.Describe(VpbNetRigId.VamPerson17, Vam17, VpbNetCapability.Local);
            VpbNetRigDescriptor same = VpbNetRig.Describe(VpbNetRigId.VamPerson17, Vam17, VpbNetCapability.Local);

            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, same) == VpbNetRigCompat.Ok,
                "two identical builds are compatible",
                "identical builds were rejected: " + VpbNetRig.Check(mine, same));

            Check(log, ref pass, ref fail, mine.ControllerCount == VpbPose.ControllerCount,
                "the advertised controller count is the codec's count (" + VpbPose.ControllerCount + "), not a second hard-coded literal",
                "descriptor count " + mine.ControllerCount + " does not match codec count " + VpbPose.ControllerCount);

            VpbNetRigDescriptor otherRig = mine;
            otherRig.RigId = 2;
            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, otherRig) == VpbNetRigCompat.RigMismatch,
                "a different rig id is refused as a rig mismatch",
                "a foreign rig id was not refused");

            VpbNetRigDescriptor otherCount = mine;
            otherCount.ControllerCount = 22;
            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, otherCount) == VpbNetRigCompat.CountMismatch,
                "a 22-controller peer is refused against a 17-controller peer instead of decoding a short frame",
                "a controller-count mismatch was accepted");

            VpbNetRigDescriptor otherLayout = mine;
            otherLayout.LayoutHash = mine.LayoutHash ^ 0x5A5A5A5Au;
            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, otherLayout) == VpbNetRigCompat.LayoutMismatch,
                "same rig and same count but a different layout is still refused",
                "a layout mismatch at matching count was accepted");

            VpbNetRigDescriptor otherProto = mine;
            otherProto.PoseProtoVersion = (byte)(VpbPose.ProtoVersion + 1);
            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, otherProto) == VpbNetRigCompat.PoseVersionMismatch,
                "a newer pose protocol version is refused before it can be decoded",
                "a pose protocol mismatch was accepted");

            VpbNetRigDescriptor absent = new VpbNetRigDescriptor();
            VpbNetRigCompat c = VpbNetRig.Check(mine, absent);
            Check(log, ref pass, ref fail, c == VpbNetRigCompat.NoDescriptor && !VpbNetRig.IsFatal(c),
                "a peer that sends no descriptor is allowed to join, because the only rig it could have is the one both builds hard-coded",
                "an absent descriptor was treated as fatal, which would break peers built before this field");

            Check(log, ref pass, ref fail,
                VpbNetRig.IsFatal(VpbNetRigCompat.RigMismatch)
                && VpbNetRig.IsFatal(VpbNetRigCompat.CountMismatch)
                && VpbNetRig.IsFatal(VpbNetRigCompat.LayoutMismatch)
                && VpbNetRig.IsFatal(VpbNetRigCompat.PoseVersionMismatch)
                && !VpbNetRig.IsFatal(VpbNetRigCompat.Ok),
                "every real mismatch is fatal and nothing else is",
                "the fatal classification is wrong");
        }

        static void Capabilities(StringBuilder log, ref int pass, ref int fail)
        {
            uint mine = VpbNetCapability.Local;
            uint theirs = VpbNetCapability.Events | VpbNetCapability.Fingers;
            uint both = VpbNetCapability.Intersect(mine, theirs);

            Check(log, ref pass, ref fail, both == VpbNetCapability.Events,
                "capabilities resolve to the intersection, so a peer advertising fingers against a build without them runs at events only",
                "capability intersection was " + both + ", expected " + VpbNetCapability.Events);

            Check(log, ref pass, ref fail,
                (VpbNetCapability.Local & VpbNetCapability.Events) != 0
                && (VpbNetCapability.Local & VpbNetCapability.Keyframe) != 0
                && (VpbNetCapability.Local & VpbNetCapability.Contract) != 0
                && (VpbNetCapability.Local & VpbNetCapability.Fingers) == 0,
                "the base set is what every session gets: events, keyframe and contract, with no fidelity tier",
                "the local capability set does not match what is implemented");

            Check(log, ref pass, ref fail,
                VpbNetCapability.LocalWith(false, false, false) == VpbNetCapability.Local,
                "a peer with the fidelity tier switched off advertises exactly the base set",
                "LocalWith(false,false,false) added bits it should not have");

            uint tier = VpbNetCapability.LocalWith(true, true, true);
            Check(log, ref pass, ref fail,
                (tier & VpbNetCapability.Local) == VpbNetCapability.Local
                && (tier & VpbNetCapability.FidelityTier) == VpbNetCapability.FidelityTier
                && (tier & VpbNetCapability.Props) == 0
                && (tier & VpbNetCapability.Voice) == 0
                && (tier & VpbNetCapability.Triggers) == 0,
                "the fidelity tier is additive: it never drops a base bit and never claims props, voice or triggers",
                "LocalWith(true,true,true) produced " + tier);

            Check(log, ref pass, ref fail,
                VpbNetCapability.LocalWith(false, false, false, true)
                    == (VpbNetCapability.Local | VpbNetCapability.Triggers),
                "trigger relay is claimed on its own, independently of the fidelity tier",
                "LocalWith(...,true) did not claim exactly the trigger bit");

            Check(log, ref pass, ref fail,
                VpbNetCapability.LocalWith(false, false, false, false, false, false, true)
                    == (VpbNetCapability.Local | VpbNetCapability.Params)
                && (VpbNetCapability.LocalWith(false, false, false, false, true, true)
                    & VpbNetCapability.Params) == 0,
                "object settings are claimed on their own, and the six-argument LocalWith does not silently promise them",
                "LocalWith(...,parameters) did not claim exactly the params bit");

            StringBuilder paramsDesc = new StringBuilder(80);
            VpbNetCapability.Describe(paramsDesc,
                VpbNetCapability.Local | VpbNetCapability.Params);
            Check(log, ref pass, ref fail, paramsDesc.ToString() == "events+keyframe+contract+params+rules+content",
                "capability text names params when that bit is set",
                "params bit rendered as \"" + paramsDesc.ToString() + "\"");

            Check(log, ref pass, ref fail,
                VpbNetCapability.LocalWith(true, false, false) == (VpbNetCapability.Local | VpbNetCapability.Fingers)
                && VpbNetCapability.LocalWith(false, true, false) == (VpbNetCapability.Local | VpbNetCapability.Eyes)
                && VpbNetCapability.LocalWith(false, false, true) == (VpbNetCapability.Local | VpbNetCapability.Jaw),
                "each half of the tier is claimed on its own, so a rig that only resolved eyes does not promise fingers",
                "the fidelity bits are not independently claimable");

            Check(log, ref pass, ref fail,
                VpbNetCapability.Intersect(VpbNetCapability.LocalWith(true, true, true), VpbNetCapability.Local)
                    == VpbNetCapability.Local,
                "a tier peer meeting a base peer runs at the base set, with no fidelity on the wire",
                "the tier survived an intersection with a base peer");

            StringBuilder sb = new StringBuilder(64);
            VpbNetCapability.Describe(sb, VpbNetCapability.Local);
            string desc = sb.ToString();
            Check(log, ref pass, ref fail, desc == "events+keyframe+contract+rules+content",
                "capability text renders as \"" + desc + "\"",
                "capability text was \"" + desc + "\", expected \"events+keyframe+contract+rules+content\"");

            sb.Length = 0;
            VpbNetCapability.Describe(sb, 0u);
            Check(log, ref pass, ref fail, sb.ToString() == "none",
                "an empty capability set renders as \"none\" rather than an empty string",
                "empty capability set rendered as \"" + sb.ToString() + "\"");
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRigDescriptor mine = VpbNetRig.Describe(VpbNetRigId.VamPerson17, Vam17, VpbNetCapability.Local);

            VpbNetEventWriter w = new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + 64);
            w.Begin(VpbNetEventType.Join, 7);
            w.WriteU16(3);
            w.WriteU32(mine.Capabilities);
            VpbNetRig.Write(w, mine);
            int n = w.End();

            Check(log, ref pass, ref fail, n > 0,
                "a join carrying capabilities and a rig descriptor fits the event payload cap (" + n + " B of " + VpbNetEventLimits.MaxPayload + ")",
                "join with a rig descriptor did not encode");

            VpbNetEventReader r = new VpbNetEventReader();
            bool began = r.Begin(w.Buffer, 0, n);
            int peerId = r.ReadU16();
            uint caps = r.ReadU32();
            VpbNetRigDescriptor got = VpbNetRig.Read(r, caps);

            Check(log, ref pass, ref fail,
                began && !r.Failed && peerId == 3 && caps == mine.Capabilities
                && got.RigId == mine.RigId && got.ControllerCount == mine.ControllerCount
                && got.LayoutHash == mine.LayoutHash && got.PoseProtoVersion == mine.PoseProtoVersion,
                "join round-trips every field byte-exact",
                "join round-trip lost a field: began=" + began + " failed=" + r.Failed
                    + " peer=" + peerId + " caps=" + caps
                    + " rig=" + got.RigId + " count=" + got.ControllerCount);

            Check(log, ref pass, ref fail, VpbNetRig.Check(mine, got) == VpbNetRigCompat.Ok,
                "a decoded descriptor compares equal to the one that produced it",
                "a decoded descriptor did not match its source");

            VpbNetEventWriter old = new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + 64);
            old.Begin(VpbNetEventType.Join, 8);
            old.WriteU16(3);
            old.WriteU32(0);
            int oldLen = old.End();

            VpbNetEventReader r2 = new VpbNetEventReader();
            r2.Begin(old.Buffer, 0, oldLen);
            r2.ReadU16();
            uint oldCaps = r2.ReadU32();
            bool cleanBeforeRig = !r2.Failed;
            VpbNetRigDescriptor none = VpbNetRig.Read(r2, oldCaps);

            Check(log, ref pass, ref fail,
                cleanBeforeRig && !none.IsPresent,
                "a pre-descriptor join parses its own fields cleanly and reports no descriptor",
                "a short join was misread: cleanBefore=" + cleanBeforeRig + " present=" + none.IsPresent);

            VpbNetEventReader r3 = new VpbNetEventReader();
            bool ok3 = r3.Begin(w.Buffer, 0, n);
            r3.ReadU16();
            uint caps3 = r3.ReadU32();
            Check(log, ref pass, ref fail, ok3 && !r3.Failed && caps3 == mine.Capabilities,
                "an older two-field parser reads a descriptor-carrying join without failing on the trailing bytes",
                "trailing descriptor bytes broke an older parser");
        }

        static void Explanations(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRigDescriptor mine = VpbNetRig.Describe(VpbNetRigId.VamPerson17, Vam17, VpbNetCapability.Local);
            VpbNetRigDescriptor theirs = mine;
            theirs.ControllerCount = 22;

            VpbNetRigCompat[] all =
            {
                VpbNetRigCompat.Ok, VpbNetRigCompat.NoDescriptor, VpbNetRigCompat.RigMismatch,
                VpbNetRigCompat.CountMismatch, VpbNetRigCompat.LayoutMismatch, VpbNetRigCompat.PoseVersionMismatch
            };

            int empty = 0;
            int coded = 0;
            for (int i = 0; i < all.Length; i++)
            {
                string s = VpbNetRig.Explain(all[i], mine, theirs);
                if (string.IsNullOrEmpty(s)) empty++;
                if (s != null && s.IndexOf("rig check failed", StringComparison.Ordinal) >= 0) coded++;
            }

            Check(log, ref pass, ref fail, empty == 0 && coded == 0,
                "every compatibility result has its own user-facing sentence, with no fallback text reachable",
                "compatibility text is missing for some results: empty=" + empty + " fallback=" + coded);

            string fix = VpbNetRig.Explain(VpbNetRigCompat.CountMismatch, mine, theirs);
            Check(log, ref pass, ref fail,
                fix.IndexOf("22", StringComparison.Ordinal) >= 0
                && fix.IndexOf("17", StringComparison.Ordinal) >= 0
                && fix.IndexOf("Update", StringComparison.Ordinal) >= 0,
                "a count mismatch names both counts and the fix: \"" + fix + "\"",
                "count-mismatch text is not actionable: \"" + fix + "\"");

            Check(log, ref pass, ref fail, VpbNetRigId.Name(VpbNetRigId.VamPerson17) == "vam1-person-17",
                "the VaM rig has a stable printable name",
                "rig name was \"" + VpbNetRigId.Name(VpbNetRigId.VamPerson17) + "\"");
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, bool ok, string passText, string failText)
        {
            if (ok)
            {
                pass++;
                Line(log, "PASS  " + passText);
            }
            else
            {
                fail++;
                Line(log, "FAIL  " + failText);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            if (log == null) return;
            log.Append(s);
            log.Append('\n');
        }
    }
}
