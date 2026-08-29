using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetKeyframeSelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(4096);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== full-state keyframe self-test =====");

            StateRoundTrip(log, ref pass, ref fail);
            FragmentReassembly(log, ref pass, ref fail);
            OutOfOrderAndDuplicates(log, ref pass, ref fail);
            Supersede(log, ref pass, ref fail);
            Timeout(log, ref pass, ref fail);
            HostileFragments(log, ref pass, ref fail);
            ValidationOnCompletion(log, ref pass, ref fail);
            EventWatermark(log, ref pass, ref fail);
            ClothingAuthority(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 state      full state round-trips through a keyframe : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 fragments  reassembles out of order, dedupes         : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 resync     newer supersedes, stale refused, times out: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 security   caps and plugin refs refused on assembly  : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end keyframe self-test =====");
            return fail == 0;
        }

        static VpbNetPeerState BuildState(int clothing, int morphs)
        {
            VpbNetPeerState s = new VpbNetPeerState();
            s.PeerId = 4242;
            s.SetExpression("Creator.Pack.1:/Custom/Expressions/Smile.json", 2);

            byte[] pose = new byte[VpbPose.FrameBytes];
            float[] floats = new float[VpbPose.PoseFloats];
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                floats[p] = 0.1f * i;
                floats[p + 1] = 1.2f;
                floats[p + 2] = -0.3f * i;
                floats[p + 6] = 1f;
            }
            int clamped;
            int n = VpbPose.WriteFrame(pose, 0, VpbPose.FlagKeyframe, 4242, 9, 1234,
                floats, VpbPose.ControllerCount, null, 0, 0, out clamped);
            if (n > 0) s.SetPose(pose, 0, n);

            for (int i = 0; i < clothing; i++)
                s.SetClothing("Creator.Pack.1:/Custom/Clothing/Female/Item" + i + ".vam", (i & 1) == 0, (uint)(10 + i));
            for (int i = 0; i < morphs; i++)
                s.SetMorph("Creator.Pack.1:/Custom/Morphs/female/morph" + i + ".vmi", -2f + i * 0.13f, (uint)(100 + i));

            return s;
        }

        static void StateRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState src = BuildState(6, 10);
            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = src.Write(blob);

            VpbNetPeerState dst = new VpbNetPeerState();
            VpbNetKeyframeReject r = dst.Read(blob, 0, n);

            bool basics = r == VpbNetKeyframeReject.None
                && dst.PeerId == 4242
                && dst.Expression == src.Expression
                && dst.ClothingCount == 6
                && dst.MorphCount == 10
                && dst.HavePose
                && dst.EventSeq == src.EventSeq;

            double worstMorph = 0.0;
            bool idsOk = true;
            for (int i = 0; i < src.MorphCount && basics; i++)
            {
                if (dst.MorphId(i) != src.MorphId(i)) idsOk = false;
                double e = Math.Abs(dst.MorphValue(i) - src.MorphValue(i));
                if (e > worstMorph) worstMorph = e;
            }
            bool clothOk = true;
            for (int i = 0; i < src.ClothingCount && basics; i++)
            {
                if (dst.ClothingId(i) != src.ClothingId(i) || dst.ClothingOn(i) != src.ClothingOn(i)) clothOk = false;
            }

            byte[] poseA = new byte[VpbPose.FrameBytes];
            byte[] poseB = new byte[VpbPose.FrameBytes];
            src.CopyPose(poseA);
            dst.CopyPose(poseB);
            bool poseSame = true;
            for (int i = 0; i < VpbPose.FrameBytes; i++)
            {
                if (poseA[i] != poseB[i]) { poseSame = false; break; }
            }

            Check(log, ref pass, ref fail,
                basics && idsOk && clothOk && poseSame && worstMorph < 0.0002,
                "full state round-trips in " + n + " B: pose byte-identical, 6 clothing, 10 morphs (worst "
                    + F(worstMorph, 6) + "), expression, event watermark",
                "state round-trip failed: reject=" + r + " basics=" + basics + " ids=" + idsOk
                    + " cloth=" + clothOk + " pose=" + poseSame + " morphErr=" + F(worstMorph, 6));
        }

        static void FragmentReassembly(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState src = BuildState(VpbNetEventLimits.MaxClothingItems, VpbNetEventLimits.MaxMorphs);
            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = src.Write(blob);

            int count = VpbNetKeyframeAssembler.FragmentCount(n);
            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];

            for (int i = 0; i < count; i++)
            {
                int fn = VpbNetKeyframeAssembler.WriteFragment(frag, blob, n, 1, i);
                asm.Offer(frag, 0, fn, 0.0);
            }

            byte[] outBuf = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int got = asm.Take(outBuf);

            VpbNetPeerState dst = new VpbNetPeerState();
            VpbNetKeyframeReject r = got > 0 ? dst.Read(outBuf, 0, got) : VpbNetKeyframeReject.Incomplete;

            Check(log, ref pass, ref fail, count > 1,
                "a worst-case keyframe (" + VpbNetEventLimits.MaxClothingItems + " clothing + "
                    + VpbNetEventLimits.MaxMorphs + " morphs) is " + n + " B = " + count
                    + " fragments, so fragmentation is mandatory not optional",
                "worst-case keyframe fits one datagram (" + n + " B) - the fragmentation path would be untested");

            Check(log, ref pass, ref fail,
                got == n && r == VpbNetKeyframeReject.None
                    && dst.ClothingCount == src.ClothingCount && dst.MorphCount == src.MorphCount,
                "reassembles to a byte-exact " + got + " B blob and decodes ("
                    + dst.ClothingCount + " clothing, " + dst.MorphCount + " morphs)",
                "reassembly failed: got=" + got + "/" + n + " reject=" + r);
        }

        static void OutOfOrderAndDuplicates(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState src = BuildState(20, 30);
            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = src.Write(blob);
            int count = VpbNetKeyframeAssembler.FragmentCount(n);

            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];

            for (int i = count - 1; i >= 0; i--)
            {
                int fn = VpbNetKeyframeAssembler.WriteFragment(frag, blob, n, 5, i);
                asm.Offer(frag, 0, fn, 0.0);
            }

            int dupFn = VpbNetKeyframeAssembler.WriteFragment(frag, blob, n, 5, 0);
            VpbNetKeyframeReject dup = asm.Offer(frag, 0, dupFn, 0.0);

            byte[] outBuf = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int got = asm.Take(outBuf);

            bool same = got == n;
            for (int i = 0; i < got && same; i++)
            {
                if (outBuf[i] != blob[i]) same = false;
            }

            Check(log, ref pass, ref fail, same && dup == VpbNetKeyframeReject.Duplicate && asm.Duplicates == 1,
                "fragments arriving fully reversed still reassemble byte-exact, and a repeat is refused ("
                    + count + " fragments)",
                "out-of-order reassembly failed: got=" + got + "/" + n + " same=" + same + " dup=" + dup);
        }

        static void Supersede(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState a = BuildState(20, 30);
            VpbNetPeerState b = BuildState(4, 4);
            byte[] blobA = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            byte[] blobB = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int na = a.Write(blobA);
            int nb = b.Write(blobB);

            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];

            int fn = VpbNetKeyframeAssembler.WriteFragment(frag, blobA, na, 7, 0);
            asm.Offer(frag, 0, fn, 0.0);
            bool partial = asm.Active && !asm.IsComplete;

            int countB = VpbNetKeyframeAssembler.FragmentCount(nb);
            for (int i = 0; i < countB; i++)
            {
                fn = VpbNetKeyframeAssembler.WriteFragment(frag, blobB, nb, 8, i);
                asm.Offer(frag, 0, fn, 0.0);
            }

            byte[] outBuf = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int got = asm.Take(outBuf);
            VpbNetPeerState dst = new VpbNetPeerState();
            VpbNetKeyframeReject r = got > 0 ? dst.Read(outBuf, 0, got) : VpbNetKeyframeReject.Incomplete;

            VpbNetKeyframeAssembler stale = new VpbNetKeyframeAssembler();
            fn = VpbNetKeyframeAssembler.WriteFragment(frag, blobB, nb, 9, 0);
            stale.Offer(frag, 0, fn, 0.0);
            fn = VpbNetKeyframeAssembler.WriteFragment(frag, blobA, na, 3, 0);
            VpbNetKeyframeReject staleReject = stale.Offer(frag, 0, fn, 0.0);

            Check(log, ref pass, ref fail,
                partial && got == nb && r == VpbNetKeyframeReject.None && dst.ClothingCount == 4
                    && asm.Superseded == 1,
                "a newer generation abandons a half-assembled older one (" + asm.Superseded
                    + " superseded) - a resync must never deliver stale state",
                "supersede failed: partial=" + partial + " got=" + got + "/" + nb + " reject=" + r
                    + " superseded=" + asm.Superseded);

            Check(log, ref pass, ref fail, staleReject == VpbNetKeyframeReject.Stale,
                "an older generation arriving late is refused, not merged into the current one",
                "a stale generation was accepted: " + staleReject);
        }

        static void Timeout(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState s = BuildState(20, 30);
            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = s.Write(blob);

            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];
            int fn = VpbNetKeyframeAssembler.WriteFragment(frag, blob, n, 1, 0);
            asm.Offer(frag, 0, fn, 1000.0);

            asm.Tick(1000.0 + VpbNetKeyframeAssembler.ReassemblyTimeoutMs - 1.0);
            bool stillActive = asm.Active;

            asm.Tick(1000.0 + VpbNetKeyframeAssembler.ReassemblyTimeoutMs + 1.0);
            bool cleared = !asm.Active && asm.TimedOut == 1;

            Check(log, ref pass, ref fail, stillActive && cleared,
                "an abandoned partial keyframe times out after "
                    + F(VpbNetKeyframeAssembler.ReassemblyTimeoutMs / 1000.0, 0)
                    + "s and frees the buffer - a peer cannot pin memory with endless partials",
                "reassembly timeout wrong: active@t-1=" + stillActive + " cleared=" + cleared);
        }

        static void HostileFragments(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];

            frag[0] = VpbNetKeyframeAssembler.ProtoVersion;
            VpbIpc.WriteU16(frag, 2, 1);
            frag[4] = 250;
            frag[5] = 200;
            VpbIpc.WriteU16(frag, 6, 8);
            VpbNetKeyframeReject bigCount = asm.Offer(frag, 0, VpbNetKeyframeAssembler.FragmentHeader + 8, 0.0);

            frag[4] = 5;
            frag[5] = 3;
            VpbNetKeyframeReject badIndex = asm.Offer(frag, 0, VpbNetKeyframeAssembler.FragmentHeader + 8, 0.0);

            frag[4] = 0;
            frag[5] = 3;
            VpbIpc.WriteU16(frag, 6, 8);
            VpbNetKeyframeReject badStride = asm.Offer(frag, 0, VpbNetKeyframeAssembler.FragmentHeader + 8, 0.0);

            frag[4] = 0;
            frag[5] = 1;
            VpbIpc.WriteU16(frag, 6, 900);
            VpbNetKeyframeReject lying = asm.Offer(frag, 0, VpbNetKeyframeAssembler.FragmentHeader + 8, 0.0);

            frag[0] = 99;
            VpbNetKeyframeReject badVer = asm.Offer(frag, 0, VpbNetKeyframeAssembler.FragmentHeader + 8, 0.0);

            Check(log, ref pass, ref fail,
                bigCount == VpbNetKeyframeReject.BadCount
                    && badIndex == VpbNetKeyframeReject.BadIndex
                    && badStride == VpbNetKeyframeReject.BadStride
                    && lying == VpbNetKeyframeReject.Truncated
                    && badVer == VpbNetKeyframeReject.BadVersion
                    && !asm.Active,
                "hostile fragments refused: count>" + VpbNetKeyframeAssembler.MaxFragments
                    + ", index>=count, short non-final fragment, lying length, bad version - and none started an assembly",
                "hostile fragment accepted: count=" + bigCount + " index=" + badIndex + " stride=" + badStride
                    + " lying=" + lying + " ver=" + badVer + " active=" + asm.Active);
        }

        static void ValidationOnCompletion(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState src = BuildState(3, 3);
            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = src.Write(blob);

            int at = FindOrdinal(blob, n, "Item0.vam");
            bool patched = false;
            if (at >= 0)
            {
                byte[] evil = Encoding.UTF8.GetBytes("aaaaaa.cs");
                for (int i = 0; i < evil.Length; i++) blob[at + i] = evil[i];
                patched = true;
            }

            VpbNetPeerState dst = new VpbNetPeerState();
            VpbNetKeyframeReject r = dst.Read(blob, 0, n);

            VpbNetPeerState over = new VpbNetPeerState();
            byte[] blob2 = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n2 = BuildState(2, 2).Write(blob2);
            int countAt = VpbNetPeerState.HeaderSize + VpbPose.FrameBytes;
            countAt += 1 + blob2[countAt];
            blob2[countAt] = (byte)(VpbNetEventLimits.MaxClothingItems + 1);
            VpbNetKeyframeReject overR = over.Read(blob2, 0, n2);

            Check(log, ref pass, ref fail, patched && r == VpbNetKeyframeReject.BadPayload,
                "a plugin reference smuggled inside a reassembled keyframe is refused (" + r
                    + ") - the same rule as the EVENT channel, applied after reassembly not per fragment",
                "keyframe validation missed a plugin reference: patched=" + patched + " reject=" + r);

            Check(log, ref pass, ref fail, overR == VpbNetKeyframeReject.BadCount,
                "a keyframe claiming more than " + VpbNetEventLimits.MaxClothingItems
                    + " clothing items is refused (" + overR + ")",
                "keyframe count cap not enforced: " + overR);
        }

        static void EventWatermark(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState s = new VpbNetPeerState();
            s.SetExpression("Creator.Pack.1:/Custom/Expressions/Smile.json", 5);
            s.SetClothing("Creator.Pack.1:/Custom/Clothing/Female/A.vam", true, 9);

            bool stale5 = s.IsStaleEvent(5);
            bool stale9 = s.IsStaleEvent(9);
            bool fresh10 = !s.IsStaleEvent(10);

            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = s.Write(blob);
            VpbNetPeerState dst = new VpbNetPeerState();
            dst.Read(blob, 0, n);

            bool carried = dst.EventSeq == 9 && dst.IsStaleEvent(9) && !dst.IsStaleEvent(10);

            Check(log, ref pass, ref fail, stale5 && stale9 && fresh10 && carried,
                "the keyframe carries an event watermark (seq " + dst.EventSeq
                    + "), so events already folded into it are discarded and later ones still apply",
                "watermark wrong: stale5=" + stale5 + " stale9=" + stale9 + " fresh10=" + fresh10
                    + " carried=" + carried);

            StaleKeyframePeek(log, ref pass, ref fail);
        }

        // Read() replaces whole state including watermark.
        static void StaleKeyframePeek(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState old = new VpbNetPeerState();
            old.SetClothing("Creator.Pack.1:/Custom/Clothing/Female/A.vam", true, 4);

            byte[] blob = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = old.Write(blob);

            uint peeked;
            bool ok = VpbNetPeerState.TryPeekEventSeq(blob, 0, n, out peeked);

            VpbNetPeerState live = new VpbNetPeerState();
            live.SetClothing("Creator.Pack.1:/Custom/Clothing/Female/B.vam", true, 7);

            bool older = peeked < live.EventSeq;

            uint junk;
            bool shortRefused = !VpbNetPeerState.TryPeekEventSeq(blob, 0, 3, out junk);
            bool nullRefused = !VpbNetPeerState.TryPeekEventSeq(null, 0, n, out junk);

            live.Read(blob, 0, n);
            bool rewinds = live.EventSeq == 4;

            Check(log, ref pass, ref fail, ok && peeked == 4 && older && shortRefused && nullRefused && rewinds,
                "a keyframe's age can be read (seq " + peeked + ") before it overwrites live state,"
                    + " which is the only thing standing between a late keyframe and an undone change",
                "peek wrong: ok=" + ok + " peeked=" + peeked + " older=" + older
                    + " shortRefused=" + shortRefused + " nullRefused=" + nullRefused
                    + " rewinds=" + rewinds);
        }

        static int FindOrdinal(byte[] buf, int len, string needle)
        {
            byte[] n = Encoding.UTF8.GetBytes(needle);
            for (int i = 0; i + n.Length <= len; i++)
            {
                bool hit = true;
                for (int k = 0; k < n.Length; k++)
                {
                    if (buf[i + k] != n[k]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }

        static string F(double v, int decimals)
        {
            return v.ToString("F" + decimals.ToString());
        }

        static void ClothingAuthority(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetPeerState a = new VpbNetPeerState();
            a.SetClothing("Creator.Pack.1:/Custom/Clothing/top.vam", true, 4);
            a.SetClothing("Creator.Pack.1:/Custom/Clothing/skirt.vam", true, 4);
            a.ClothingAuthoritative = true;

            byte[] buf = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int n = a.Write(buf);
            VpbNetPeerState b = new VpbNetPeerState();
            Check(log, ref pass, ref fail,
                n > 0 && b.Read(buf, 0, n) == VpbNetKeyframeReject.None
                    && b.ClothingAuthoritative && b.ClothingCount == 2,
                "an authoritative outfit survives the wire with its flag",
                "the authoritative flag or the outfit did not survive");

            a.ClothingAuthoritative = false;
            n = a.Write(buf);
            VpbNetPeerState c = new VpbNetPeerState();
            Check(log, ref pass, ref fail,
                n > 0 && c.Read(buf, 0, n) == VpbNetKeyframeReject.None && !c.ClothingAuthoritative,
                "a partial outfit stays marked partial",
                "a partial outfit read back as authoritative");

            VpbNetPeerState d = new VpbNetPeerState();
            d.ClothingAuthoritative = true;
            d.SetPose(PoseFrame(), 0, VpbPose.FrameBytes);
            n = d.Write(buf);
            VpbNetPeerState e = new VpbNetPeerState();
            Check(log, ref pass, ref fail,
                n > 0 && e.Read(buf, 0, n) == VpbNetKeyframeReject.None
                    && e.HavePose && e.ClothingAuthoritative,
                "have-pose and clothing authority coexist in one flag byte",
                "the two header flags collided");

            VpbNetPeerState f = new VpbNetPeerState();
            f.SetClothing("Creator.Pack.1:/Custom/Clothing/top.vam", true, 1);
            f.ClothingAuthoritative = true;
            f.ClearClothing();
            Check(log, ref pass, ref fail,
                f.ClothingCount == 0 && !f.ClothingAuthoritative,
                "clearing the outfit also drops the claim of authority",
                "ClearClothing left a stale authority claim");
        }

        static byte[] PoseFrame()
        {
            byte[] frame = new byte[VpbPose.FrameBytes];
            frame[0] = VpbPose.ProtoVersion;
            frame[1] = VpbPose.PackFlags(0, VpbPose.ControllerCount);
            return frame;
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
