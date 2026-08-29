using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetEventSelfTest
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

            Line(log, "===== EVENT channel self-test =====");

            RoundTrip(log, ref pass, ref fail);
            Identifiers(log, ref pass, ref fail);
            PluginRefusal(log, ref pass, ref fail);
            Caps(log, ref pass, ref fail);
            Malformed(log, ref pass, ref fail);
            Ordering(log, ref pass, ref fail);
            RateLimit(log, ref pass, ref fail);
            UnknownType(log, ref pass, ref fail);
            BusyRoundTrip(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 codec      every event type round-trips           : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 security   paths, traversal and plugin refs refused: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 caps       size, count and rate limits enforced   : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 ordering   in-order release, duplicates refused   : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end EVENT channel self-test =====");
            return fail == 0;
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventWriter w = new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize);
            VpbNetEventReader r = new VpbNetEventReader();

            w.Begin(VpbNetEventType.Join, 7);
            w.WriteU16(42);
            w.WriteU32(0x0000000Fu);
            int n = w.End();

            bool joinOk = n > 0 && r.Begin(w.Buffer, 0, n) && r.Type == VpbNetEventType.Join && r.Seq == 7;
            int peer = joinOk ? r.ReadU16() : -1;
            uint caps = joinOk ? r.ReadU32() : 0u;
            Check(log, ref pass, ref fail,
                joinOk && peer == 42 && caps == 0x0Fu && !r.Failed && r.Remaining == 0,
                "Join round-trips (" + n + " B: peer, capability bitset - no player handle on the wire)",
                "Join round-trip failed: peer=" + peer + " caps=" + caps);

            w.Begin(VpbNetEventType.Clothing, 8);
            w.WriteByte(2);
            w.WriteString("Creator.Pack.1:/Custom/Clothing/Female/Top", VpbNetEventLimits.MaxIdentifier);
            w.WriteByte(1);
            w.WriteString("Creator.Pack.1:/Custom/Clothing/Female/Skirt", VpbNetEventLimits.MaxIdentifier);
            w.WriteByte(0);
            n = w.End();

            bool clothOk = n > 0 && r.Begin(w.Buffer, 0, n) && r.Type == VpbNetEventType.Clothing;
            int count = clothOk ? r.ReadCount(VpbNetEventLimits.MaxClothingItems) : -1;
            string id0 = clothOk ? r.ReadIdentifier() : null;
            byte on0 = clothOk ? r.ReadByte() : (byte)0;
            string id1 = clothOk ? r.ReadIdentifier() : null;
            byte on1 = clothOk ? r.ReadByte() : (byte)9;
            Check(log, ref pass, ref fail,
                clothOk && count == 2 && id0 != null && on0 == 1 && id1 != null && on1 == 0
                    && !r.Failed && r.Remaining == 0,
                "Clothing round-trips with package-qualified uids (" + n + " B, 2 items)",
                "Clothing round-trip failed: count=" + count + " reject=" + r.Reject);

            w.Begin(VpbNetEventType.Morphs, 9);
            w.WriteByte(3);
            float[] vals = { -1.75f, 0f, 3.25f };
            for (int i = 0; i < 3; i++)
            {
                w.WriteString("morph.id." + i, VpbNetEventLimits.MaxIdentifier);
                w.WriteI16(VpbNetEventCodec.QuantizeMorph(vals[i]));
            }
            n = w.End();

            bool morphOk = n > 0 && r.Begin(w.Buffer, 0, n) && r.Type == VpbNetEventType.Morphs;
            int mc = morphOk ? r.ReadCount(VpbNetEventLimits.MaxMorphs) : -1;
            double worst = 0.0;
            for (int i = 0; i < 3 && morphOk; i++)
            {
                r.ReadIdentifier();
                float got = VpbNetEventCodec.DequantizeMorph(r.ReadI16());
                double e = Math.Abs(got - vals[i]);
                if (e > worst) worst = e;
            }
            Check(log, ref pass, ref fail, morphOk && mc == 3 && !r.Failed && worst < 0.0002,
                "Morphs round-trip, worst quantization error " + F(worst, 6) + " over +/-"
                    + VpbNetEventLimits.MorphRange,
                "Morphs round-trip failed: count=" + mc + " worst=" + F(worst, 6) + " reject=" + r.Reject);

            w.Begin(VpbNetEventType.Chat, 10);
            w.WriteString("hello there - how's it going? 100%", VpbNetEventLimits.MaxChat);
            n = w.End();
            bool chatOk = n > 0 && r.Begin(w.Buffer, 0, n);
            string text = chatOk ? r.ReadText(VpbNetEventLimits.MaxChat) : null;
            Check(log, ref pass, ref fail, chatOk && text == "hello there - how's it going? 100%",
                "Chat round-trips punctuation and spaces without identifier rules",
                "Chat round-trip failed: " + (text == null ? "null (" + r.Reject + ")" : text));

            w.Begin(VpbNetEventType.AtomParam, 11);
            w.WriteByte(3);
            w.WriteString("Lamp", VpbNetEventLimits.MaxIdentifier);
            w.WriteString("Light", VpbNetStorableLimits.MaxStorableChars);
            w.WriteString("intensity", VpbNetStorableLimits.MaxParamChars);
            w.WriteByte(VpbNetAtomParamKind.Float);
            w.WriteF32(1.25f);
            w.WriteString("Lamp", VpbNetEventLimits.MaxIdentifier);
            w.WriteString("Light", VpbNetStorableLimits.MaxStorableChars);
            w.WriteString("on", VpbNetStorableLimits.MaxParamChars);
            w.WriteByte(VpbNetAtomParamKind.Bool);
            w.WriteByte(1);
            w.WriteString("Lamp", VpbNetEventLimits.MaxIdentifier);
            w.WriteString("Light", VpbNetStorableLimits.MaxStorableChars);
            w.WriteString("color", VpbNetStorableLimits.MaxParamChars);
            w.WriteByte(VpbNetAtomParamKind.Color);
            w.WriteF32(0.3f);
            w.WriteF32(0.5f);
            w.WriteF32(0.8f);
            n = w.End();

            bool paramOk = n > 0 && r.Begin(w.Buffer, 0, n) && r.Type == VpbNetEventType.AtomParam;
            int pc = paramOk ? r.ReadCount(VpbNetEventLimits.MaxParamsPerEvent) : -1;
            string pUid = paramOk ? r.ReadIdentifier() : null;
            string pStore = paramOk ? r.ReadIdentifier(VpbNetStorableLimits.MaxStorableChars) : null;
            string pName = paramOk ? r.ReadIdentifier(VpbNetStorableLimits.MaxParamChars) : null;
            byte pKind = paramOk ? r.ReadByte() : (byte)0;
            float pNum = paramOk ? r.ReadF32() : 0f;
            r.ReadIdentifier();
            r.ReadIdentifier(VpbNetStorableLimits.MaxStorableChars);
            r.ReadIdentifier(VpbNetStorableLimits.MaxParamChars);
            byte bKind = paramOk ? r.ReadByte() : (byte)0;
            byte bFlag = paramOk ? r.ReadByte() : (byte)0;
            r.ReadIdentifier();
            r.ReadIdentifier(VpbNetStorableLimits.MaxStorableChars);
            r.ReadIdentifier(VpbNetStorableLimits.MaxParamChars);
            byte cKind = paramOk ? r.ReadByte() : (byte)0;
            float cH = paramOk ? r.ReadF32() : 0f;
            float cS = paramOk ? r.ReadF32() : 0f;
            float cV = paramOk ? r.ReadF32() : 0f;
            Check(log, ref pass, ref fail,
                paramOk && pc == 3 && pUid == "Lamp" && pStore == "Light" && pName == "intensity"
                    && pKind == VpbNetAtomParamKind.Float && Math.Abs(pNum - 1.25f) < 1e-6
                    && bKind == VpbNetAtomParamKind.Bool && bFlag == 1
                    && cKind == VpbNetAtomParamKind.Color
                    && Math.Abs(cH - 0.3f) < 1e-6 && Math.Abs(cS - 0.5f) < 1e-6 && Math.Abs(cV - 0.8f) < 1e-6
                    && !r.Failed && r.Remaining == 0,
                "AtomParam round-trips a number, a switch and a colour (" + n + " B, 3 values)",
                "AtomParam round-trip failed: count=" + pc + " reject=" + r.Reject
                    + " remaining=" + r.Remaining);
        }

        static void Identifiers(StringBuilder log, ref int pass, ref int fail)
        {
            string[] good =
            {
                "Creator.Pack.1:/Custom/Clothing/Female/Top.vam",
                "Custom/Atom/Person/Morphs/female/thing.vmi",
                "simple_id-42",
                "a:b/c.d"
            };
            string[] bad =
            {
                "../../../Windows/System32/config",
                "/etc/passwd",
                "C:\\Windows\\System32\\drivers",
                "C:/Windows/System32/drivers",
                "Custom\\Clothing\\thing",
                "has\u0000null",
                "wild*card",
                "double//slash",
                ""
            };

            int goodOk = 0;
            for (int i = 0; i < good.Length; i++)
            {
                if (VpbNetEventCodec.IsSafeIdentifier(good[i])) goodOk++;
            }

            int badBlocked = 0;
            for (int i = 0; i < bad.Length; i++)
            {
                if (!VpbNetEventCodec.IsSafeIdentifier(bad[i])) badBlocked++;
            }

            Check(log, ref pass, ref fail, goodOk == good.Length,
                "legitimate package-qualified uids accepted (" + goodOk + "/" + good.Length + ")",
                "legitimate uids rejected: only " + goodOk + "/" + good.Length + " accepted");
            Check(log, ref pass, ref fail, badBlocked == bad.Length,
                "traversal, absolute paths, drive letters, backslashes, nulls and wildcards all refused ("
                    + badBlocked + "/" + bad.Length + ")",
                "hostile identifiers accepted: only " + badBlocked + "/" + bad.Length + " blocked");
        }

        static void PluginRefusal(StringBuilder log, ref int pass, ref int fail)
        {
            string[] plugins =
            {
                "Creator.Pack.1:/Custom/Scripts/evil.cs",
                "Creator.Pack.1:/Custom/Scripts/evil.CSLIST",
                "Creator.Pack.1:/Custom/Scripts/evil.dll",
                "thing.dvar",
                "Creator.Pack.1:/Custom/Scripts/evil.cs.",
                "Creator.Pack.1:/Custom/Scripts/evil.cs   "
            };

            int blocked = 0;
            for (int i = 0; i < plugins.Length; i++)
            {
                if (VpbNetEventCodec.IsPluginReference(plugins[i])) blocked++;
            }

            VpbNetEventWriter w = new VpbNetEventWriter(256);
            VpbNetEventReader r = new VpbNetEventReader();
            w.Begin(VpbNetEventType.Clothing, 1);
            w.WriteByte(1);
            w.WriteString("Creator.Pack.1:/Custom/Scripts/evil.cs", VpbNetEventLimits.MaxIdentifier);
            w.WriteByte(1);
            int n = w.End();
            r.Begin(w.Buffer, 0, n);
            r.ReadCount(VpbNetEventLimits.MaxClothingItems);
            string got = r.ReadIdentifier();

            Check(log, ref pass, ref fail,
                blocked == plugins.Length && got == null && r.Reject == VpbNetEventReject.PluginReference,
                "plugin references refused at decode (" + blocked + "/" + plugins.Length
                    + " suffixes, reader reports " + r.Reject + ") - rule 8.1, this is RCE on the receiver",
                "plugin reference slipped through: blocked " + blocked + "/" + plugins.Length
                    + ", reader gave " + (got ?? "null") + " reject=" + r.Reject);
        }

        static void Caps(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventWriter w = new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize);
            VpbNetEventReader r = new VpbNetEventReader();

            w.Begin(VpbNetEventType.Expression, 1);
            w.WriteString(new string('x', VpbNetEventLimits.MaxIdentifier + 1), VpbNetEventLimits.MaxIdentifier);
            int over = w.End();

            w.Begin(VpbNetEventType.Morphs, 2);
            w.WriteByte((byte)(VpbNetEventLimits.MaxMorphs + 1));
            int n = w.End();
            r.Begin(w.Buffer, 0, n);
            int count = r.ReadCount(VpbNetEventLimits.MaxMorphs);

            VpbNetEventWriter big = new VpbNetEventWriter(4096);
            big.Begin(VpbNetEventType.Chat, 3);
            for (int i = 0; i < 40; i++) big.WriteString(new string('a', 200), 200);
            int oversize = big.End();

            Check(log, ref pass, ref fail, over < 0,
                "an identifier over " + VpbNetEventLimits.MaxIdentifier + " chars cannot be written",
                "oversize identifier was encoded (" + over + " B)");
            Check(log, ref pass, ref fail, count == 0 && r.Reject == VpbNetEventReject.CountCap,
                "a morph count over " + VpbNetEventLimits.MaxMorphs + " is refused at decode",
                "morph count cap not enforced: count=" + count + " reject=" + r.Reject);

            w.Begin(VpbNetEventType.AtomParam, 4);
            w.WriteByte((byte)(VpbNetEventLimits.MaxParamsPerEvent + 1));
            n = w.End();
            r.Begin(w.Buffer, 0, n);
            int paramCount = r.ReadCount(VpbNetEventLimits.MaxParamsPerEvent);
            Check(log, ref pass, ref fail, paramCount == 0 && r.Reject == VpbNetEventReject.CountCap,
                "an object-settings count over " + VpbNetEventLimits.MaxParamsPerEvent + " is refused at decode",
                "AtomParam count cap not enforced: count=" + paramCount + " reject=" + r.Reject);
            Check(log, ref pass, ref fail, oversize < 0,
                "a payload over " + VpbNetEventLimits.MaxPayload + " B cannot be emitted",
                "oversize payload was encoded (" + oversize + " B)");
        }

        static void Malformed(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventWriter w = new VpbNetEventWriter(256);
            VpbNetEventReader r = new VpbNetEventReader();

            w.Begin(VpbNetEventType.Expression, 1);
            w.WriteString("bob", VpbNetEventLimits.MaxIdentifier);
            int n = w.End();

            bool shortOk = !r.Begin(w.Buffer, 0, n - 1) && r.Reject == VpbNetEventReject.Truncated;

            w.Buffer[0] = 99;
            bool verOk = !r.Begin(w.Buffer, 0, n) && r.Reject == VpbNetEventReject.BadVersion;
            w.Buffer[0] = VpbNetEventCodec.ProtoVersion;

            VpbIpc.WriteU16(w.Buffer, 2, VpbNetEventLimits.MaxPayload + 1);
            bool sizeOk = !r.Begin(w.Buffer, 0, n) && r.Reject == VpbNetEventReject.Oversize;
            VpbIpc.WriteU16(w.Buffer, 2, n - VpbNetEventCodec.HeaderSize);

            w.Begin(VpbNetEventType.Expression, 2);
            w.WriteString("bad\u0007bell", VpbNetEventLimits.MaxIdentifier);
            n = w.End();
            r.Begin(w.Buffer, 0, n);
            string ctrl = r.ReadText(VpbNetEventLimits.MaxIdentifier);
            bool ctrlOk = ctrl == null && r.Reject == VpbNetEventReject.BadString;

            Check(log, ref pass, ref fail, shortOk && verOk && sizeOk && ctrlOk,
                "malformed frames refused: truncation, bad version, lying length field, control characters",
                "malformed frame accepted: short=" + shortOk + " ver=" + verOk
                    + " size=" + sizeOk + " ctrl=" + ctrlOk);
        }

        static void Ordering(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventQueue q = new VpbNetEventQueue(256);
            VpbNetEventWriter w = new VpbNetEventWriter(256);
            byte[] dst = new byte[256];

            uint[] arrival = { 1, 3, 2, 5, 4, 6 };
            double now = 0.0;
            for (int i = 0; i < arrival.Length; i++)
            {
                w.Begin(VpbNetEventType.Expression, arrival[i]);
                w.WriteString("n" + arrival[i], VpbNetEventLimits.MaxIdentifier);
                int n = w.End();
                q.Offer(w.Buffer, 0, n, arrival[i], now);
            }

            bool ordered = true;
            uint expect = 1;
            uint seq;
            int releases = 0;
            while (q.TryRelease(dst, out seq) > 0)
            {
                if (seq != expect) ordered = false;
                expect++;
                releases++;
            }

            w.Begin(VpbNetEventType.Expression, 3);
            w.WriteString("dup", VpbNetEventLimits.MaxIdentifier);
            int dn = w.End();
            VpbNetEventReject dupReject = q.Offer(w.Buffer, 0, dn, 3, now);

            Check(log, ref pass, ref fail, ordered && releases == 6,
                "reordered events release strictly in sequence (" + releases + "/6, out-of-order held not dropped)",
                "ordering broken: released " + releases + "/6 ordered=" + ordered);
            Check(log, ref pass, ref fail,
                dupReject == VpbNetEventReject.Duplicate && q.Duplicates == 1,
                "a replayed event is refused (" + dupReject + ")",
                "replayed event accepted: " + dupReject);

            VpbNetEventQueue gap = new VpbNetEventQueue(256);
            w.Begin(VpbNetEventType.Expression, 2);
            w.WriteString("second", VpbNetEventLimits.MaxIdentifier);
            int gn = w.End();
            gap.Offer(w.Buffer, 0, gn, 2, now);
            int got = gap.TryRelease(dst, out seq);

            Check(log, ref pass, ref fail, got == 0 && gap.Held == 1,
                "a gap holds the stream instead of applying out of order (held " + gap.Held + ")",
                "a gap released early: got " + got + " held " + gap.Held);
        }

        static void RateLimit(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventQueue q = new VpbNetEventQueue(256);
            VpbNetEventWriter w = new VpbNetEventWriter(256);
            byte[] dst = new byte[256];

            int limited = 0;
            uint seq = 1;
            for (int i = 0; i < 200; i++)
            {
                w.Begin(VpbNetEventType.Expression, seq);
                w.WriteString("spam", VpbNetEventLimits.MaxIdentifier);
                int n = w.End();
                if (q.Offer(w.Buffer, 0, n, seq, 0.0) == VpbNetEventReject.RateLimited) limited++;
                else seq++;

                uint rel;
                while (q.TryRelease(dst, out rel) > 0) { }
            }

            int accepted = 200 - limited;

            VpbNetEventQueue q2 = new VpbNetEventQueue(256);
            uint s2 = 1;
            int acceptedOverTime = 0;
            for (int i = 0; i < 200; i++)
            {
                w.Begin(VpbNetEventType.Expression, s2);
                w.WriteString("ok", VpbNetEventLimits.MaxIdentifier);
                int n = w.End();
                if (q2.Offer(w.Buffer, 0, n, s2, i * 100.0) == VpbNetEventReject.None)
                {
                    acceptedOverTime++;
                    s2++;
                }
                uint rel;
                while (q2.TryRelease(dst, out rel) > 0) { }
            }

            Check(log, ref pass, ref fail,
                accepted == VpbNetEventLimits.MaxEventsPerSecond && limited == 200 - accepted,
                "a burst is capped at " + VpbNetEventLimits.MaxEventsPerSecond + "/s ("
                    + accepted + " accepted, " + limited + " dropped)",
                "rate limit not enforced: accepted " + accepted + " of 200 in one second");
            Check(log, ref pass, ref fail, acceptedOverTime == 200,
                "a legitimate 10/s stream is never limited (" + acceptedOverTime + "/200 over 20s)",
                "legitimate traffic was rate limited: " + acceptedOverTime + "/200");
        }

        static void UnknownType(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventWriter w = new VpbNetEventWriter(256);
            VpbNetEventReader r = new VpbNetEventReader();

            w.Begin(200, 1);
            w.WriteString("future", 64);
            int n = w.End();

            bool parsed = r.Begin(w.Buffer, 0, n);

            Check(log, ref pass, ref fail, parsed && r.Type == 200 && !r.Failed,
                "an unknown event type parses its envelope so a v1 peer can skip it, not disconnect",
                "unknown event type broke the envelope: parsed=" + parsed + " reject=" + r.Reject);
        }

        static void BusyRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEventWriter w = new VpbNetEventWriter(256);
            VpbNetEventReader r = new VpbNetEventReader();

            w.Begin(VpbNetEventType.Busy, 7);
            w.WriteByte(1);
            w.WriteU16(45);
            w.WriteByte(VpbNetBusyKind.Appearance);
            int n = w.End();

            bool parsed = r.Begin(w.Buffer, 0, n);
            byte begin = r.ReadByte();
            int seconds = r.ReadU16();
            byte kind = r.ReadByte();

            bool ok = parsed && !r.Failed && r.Type == VpbNetEventType.Busy && r.Seq == 7
                && begin == 1 && seconds == 45 && kind == VpbNetBusyKind.Appearance;

            Check(log, ref pass, ref fail, ok,
                "a busy notice round-trips its flag, its expected seconds and its kind in " + n + " bytes",
                "busy notice did not round-trip: parsed=" + parsed + " failed=" + r.Failed
                    + " begin=" + begin + " seconds=" + seconds + " kind=" + kind);

            bool known = string.Equals(VpbNetBusyKind.Describe(VpbNetBusyKind.Appearance),
                "loading a look", StringComparison.Ordinal);
            bool unknownDegrades = string.Equals(VpbNetBusyKind.Describe(200),
                "loading content", StringComparison.Ordinal);

            Check(log, ref pass, ref fail, known && unknownDegrades,
                "the busy reason is a code the receiver translates, and an unrecognised code falls"
                    + " back to the generic phrase rather than being refused",
                "busy kind mapping wrong: known=" + known + " unknownDegrades=" + unknownDegrades);

            // Must fit one datagram with spare.
            Check(log, ref pass, ref fail, n > 0 && n <= 128,
                "a busy notice is small enough to burst (" + n + " bytes)",
                "busy notice is too large to burst: " + n + " bytes");
        }

        static string F(double v, int decimals)
        {
            return v.ToString("F" + decimals.ToString());
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
