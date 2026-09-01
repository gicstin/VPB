using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRulesSelfTest
    {
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

            Line(log, "===== session rules self-test =====");

            Lanes(log, ref pass, ref fail);
            FailsClosed(log, ref pass, ref fail);
            Axes(log, ref pass, ref fail);
            Cover(log, ref pass, ref fail);
            Presets(log, ref pass, ref fail);
            Legacy(log, ref pass, ref fail);
            Asymmetry(log, ref pass, ref fail);
            RoundTrip(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 one-way     each side rules bind only its own machine       : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/3 closed      anything unknown decodes as blocked             : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/3 compatible  a peer with no table keeps the old mirror only  : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, fail == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Line(log, "===== end session rules self-test =====");
            return fail == 0;
        }

        // Packing bug silently grants a refused permission.
        static void Lanes(StringBuilder log, ref int pass, ref int fail)
        {
            bool ok = true;
            int checkedLanes = 0;

            for (byte d = 0; d < VpbNetRuleDomain.Count && ok; d++)
            {
                for (byte a = 0; a < VpbNetRuleAxis.Count && ok; a++)
                {
                    VpbNetRuleTable t = new VpbNetRuleTable();
                    t.Set(d, a, VpbNetRuleLevel.Allowed);
                    checkedLanes++;

                    if (t.Get(d, a) != VpbNetRuleLevel.Allowed) { ok = false; break; }

                    for (byte od = 0; od < VpbNetRuleDomain.Count && ok; od++)
                    {
                        for (byte oa = 0; oa < VpbNetRuleAxis.Count && ok; oa++)
                        {
                            if (od == d && oa == a) continue;
                            if (t.Get(od, oa) != VpbNetRuleLevel.Blocked) ok = false;
                        }
                    }
                }
            }

            Check(log, ref pass, ref fail, ok && checkedLanes == VpbNetRuleDomain.Count * VpbNetRuleAxis.Count,
                "all " + checkedLanes + " lanes pack and read back independently",
                "a lane bled into another one, or only " + checkedLanes + " were reachable");

            VpbNetRuleTable levels = new VpbNetRuleTable();
            levels.Set(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            levels.Set(VpbNetRuleDomain.Hair, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            Check(log, ref pass, ref fail,
                levels.Get(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Ask
                && levels.Get(VpbNetRuleDomain.Hair, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed,
                "the three levels survive a round trip through the packing",
                "a level changed value on the way through the packing");
        }

        static void FailsClosed(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                VpbNetRuleLevel.Sanitize(3) == VpbNetRuleLevel.Blocked
                && VpbNetRuleLevel.Sanitize(200) == VpbNetRuleLevel.Blocked,
                "an unassigned level decodes as blocked",
                "an unassigned level decoded as something other than blocked");

            // Unknown level bits must read as refusal, not grant.
            VpbNetRuleTable future = new VpbNetRuleTable();
            future.Lo = 0xFFFFFFFFu;
            future.Hi = 0xFFFFFFFFu;

            bool anyGranted = false;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (future.Effective(d, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked)
                    anyGranted = true;
            }
            Check(log, ref pass, ref fail, !anyGranted,
                "a table full of levels from a later build grants no control at all",
                "a level this build does not know granted control");

            VpbNetRuleTable deny = VpbNetRuleTable.DenyAll();
            bool denied = true;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (deny.Effective(d, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked) denied = false;
            }
            Check(log, ref pass, ref fail, denied,
                "DenyAll denies every control domain",
                "DenyAll left something reachable");

            Check(log, ref pass, ref fail,
                deny.Effective(200, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked
                && deny.Effective(VpbNetRuleDomain.Clothing, 200) == VpbNetRuleLevel.Blocked,
                "a domain or axis out of range answers blocked instead of throwing",
                "an out of range domain or axis did not answer blocked");
        }

        static void Axes(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                !VpbNetRuleTable.HasAxis(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Mirror)
                && !VpbNetRuleTable.HasAxis(VpbNetRuleDomain.Triggers, VpbNetRuleAxis.Mirror)
                && VpbNetRuleTable.HasAxis(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control),
                "world domains carry a control axis and no mirror axis",
                "a world domain offered a mirror axis that means nothing");

            VpbNetRuleTable deny = VpbNetRuleTable.DenyAll();
            bool mirrorFixed = true;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (!VpbNetRuleTable.HasAxis(d, VpbNetRuleAxis.Mirror)) continue;
                if (deny.Effective(d, VpbNetRuleAxis.Mirror) != VpbNetRuleLevel.Allowed) mirrorFixed = false;
                if (VpbNetRuleTable.IsEditable(d, VpbNetRuleAxis.Mirror)) mirrorFixed = false;
            }
            Check(log, ref pass, ref fail, mirrorFixed,
                "every mirror lane is fixed on and not editable, even on a deny-all table",
                "a mirror lane could be switched off, which is a session where they are nobody");

            Check(log, ref pass, ref fail,
                deny.Effective(VpbNetRuleDomain.Pose, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked
                && VpbNetRuleTable.IsEditable(VpbNetRuleDomain.Pose, VpbNetRuleAxis.Control),
                "pose control is a separate, editable, deniable rule",
                "pose control was tied to pose mirror");
        }

        static void Cover(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Clothing) == VpbNetRuleDomain.Look
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Hair) == VpbNetRuleDomain.Look
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Skin) == VpbNetRuleDomain.Look
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Morphs) == VpbNetRuleDomain.Look
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Params) == VpbNetRuleDomain.Objects
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Triggers) == VpbNetRuleDomain.Objects,
                "look owns clothing, hair, skin and morphs; objects owns params and triggers",
                "the cover map pointed a child at the wrong parent");

            bool parentsFree =
                VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Look) == VpbNetRuleTable.NoParent
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Objects) == VpbNetRuleTable.NoParent
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Pose) == VpbNetRuleTable.NoParent
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Scene) == VpbNetRuleTable.NoParent
                && VpbNetRuleTable.CoveredBy(VpbNetRuleDomain.Content) == VpbNetRuleTable.NoParent;
            Check(log, ref pass, ref fail, parentsFree,
                "a parent domain is answered on its own and has no parent above it",
                "a parent domain was itself covered, which would loop or hide a real question");

            // A child lane is never the answer, so a stale one from an older table cannot grant.
            VpbNetRuleTable t = new VpbNetRuleTable();
            t.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            bool lifted =
                t.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed
                && t.Effective(VpbNetRuleDomain.Hair, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed
                && t.Effective(VpbNetRuleDomain.Skin, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed
                && t.Effective(VpbNetRuleDomain.Morphs, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed;
            Check(log, ref pass, ref fail, lifted,
                "allowing look allows clothing, hair, skin and morphs",
                "allowing look did not carry to a domain it owns");

            Check(log, ref pass, ref fail,
                t.Effective(VpbNetRuleDomain.Pose, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked
                && t.Effective(VpbNetRuleDomain.Scene, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked
                && t.Effective(VpbNetRuleDomain.Params, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked,
                "look control does not leak into pose, scene or the object rows",
                "look control leaked into a domain it does not own");

            t.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            t.Set(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            Check(log, ref pass, ref fail,
                t.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Ask
                && !VpbNetRuleTable.IsEditable(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control),
                "a child lane written above its parent still answers with the parent",
                "a child lane out-voted the parent the player actually answered");

            VpbNetRuleTable norm = VpbNetRuleTable.Normalize(t);
            Check(log, ref pass, ref fail,
                norm.Get(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Ask
                && norm.Get(VpbNetRuleDomain.Triggers, VpbNetRuleAxis.Control)
                    == norm.Get(VpbNetRuleDomain.Objects, VpbNetRuleAxis.Control),
                "normalise writes the parent answer into the raw child lanes an older peer reads",
                "a child lane on the wire disagreed with the parent that owns it");

            StringBuilder sb = new StringBuilder(128);
            VpbNetRuleTable described = new VpbNetRuleTable();
            described.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            VpbNetRuleTable.Describe(sb, VpbNetRuleTable.Normalize(described));
            string text = sb.ToString();
            Check(log, ref pass, ref fail,
                text.IndexOf("look") >= 0 && text.IndexOf("clothing") < 0,
                "describe names look and skips the parts look already owns",
                "describe listed a covered part as if it were a separate grant: " + text);
        }

        static void Presets(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRuleTable watch = VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);

            bool bodySafe =
                watch.Effective(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Allowed
                && watch.Effective(VpbNetRuleDomain.Morphs, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Allowed
                && watch.Effective(VpbNetRuleDomain.Skin, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Allowed
                && watch.Effective(VpbNetRuleDomain.DualPose, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Allowed
                && watch.Effective(VpbNetRuleDomain.Pose, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Blocked;
            Check(log, ref pass, ref fail, bodySafe,
                "the default preset lets nobody silently rewrite your body - every body row prompts or refuses",
                "the default preset granted silent control of something on your own body");

            bool mirrors =
                watch.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Mirror) == VpbNetRuleLevel.Allowed
                && watch.Effective(VpbNetRuleDomain.Look, VpbNetRuleAxis.Mirror) == VpbNetRuleLevel.Allowed;
            Check(log, ref pass, ref fail, mirrors,
                "the default preset still mirrors what they do to themselves",
                "the default preset broke ordinary session sync");

            Check(log, ref pass, ref fail,
                watch.Effective(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Ask
                && watch.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Ask,
                "the default preset lets a friend dress you after you say yes to the prompt",
                "the default preset made dressing each other impossible without a rules edit");

            Check(log, ref pass, ref fail,
                watch.Effective(VpbNetRuleDomain.Content, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed,
                "the default preset fetches the packages a shared scene needs",
                "the default preset would stall a join on a missing-package prompt");

            VpbNetRuleTable locked = VpbNetRuleTable.FromPreset(VpbNetRulePreset.LockedDown);
            bool noControl = true;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (locked.Effective(d, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked) noControl = false;
            }
            Check(log, ref pass, ref fail, noControl,
                "locked down grants no control of anything",
                "locked down granted control of something");

            VpbNetRuleTable full = VpbNetRuleTable.FromPreset(VpbNetRulePreset.FullTrust);
            bool everything = true;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (full.Effective(d, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Allowed) everything = false;
            }
            Check(log, ref pass, ref fail, everything,
                "full trust allows every control domain",
                "full trust left a control domain closed");

            Check(log, ref pass, ref fail,
                VpbNetRuleTable.MatchPreset(locked) == VpbNetRulePreset.LockedDown
                && VpbNetRuleTable.MatchPreset(watch) == VpbNetRulePreset.WatchTogether
                && VpbNetRuleTable.MatchPreset(full) == VpbNetRulePreset.FullTrust,
                "each preset recognises itself",
                "a preset did not round trip through MatchPreset");

            VpbNetRuleTable tweaked = watch;
            tweaked.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);
            Check(log, ref pass, ref fail,
                VpbNetRuleTable.MatchPreset(tweaked) == VpbNetRulePreset.Custom,
                "one changed rule reads as custom rather than still claiming a preset",
                "a modified table still reported itself as a stock preset");
        }

        static void Legacy(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRuleTable old = VpbNetRuleTable.LegacyPeer();

            bool noControl = true;
            for (byte d = 0; d < VpbNetRuleDomain.Count; d++)
            {
                if (old.Effective(d, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked) noControl = false;
            }
            Check(log, ref pass, ref fail, noControl,
                "a peer with no table is granted no control of anything",
                "a peer that never published a table was granted control");

            Check(log, ref pass, ref fail,
                old.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Mirror) == VpbNetRuleLevel.Allowed
                && old.Effective(VpbNetRuleDomain.Morphs, VpbNetRuleAxis.Mirror) == VpbNetRuleLevel.Allowed,
                "a peer with no table still gets the mirror older builds always had",
                "an older peer lost ordinary session sync");

            Check(log, ref pass, ref fail,
                (VpbNetCapability.Local & VpbNetCapability.Rules) != 0,
                "this build advertises that it honours a rule table",
                "this build does not advertise the rules capability, so peers will treat it as legacy");
        }

        // Each table binds only the machine that published it.
        static void Asymmetry(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRuleTable host = VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);
            host.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Allowed);

            VpbNetRuleTable guest = VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);
            guest.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Blocked);

            bool guestMayDressHost =
                host.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) == VpbNetRuleLevel.Allowed;
            bool hostMayDressGuest =
                guest.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Control) != VpbNetRuleLevel.Blocked;

            Check(log, ref pass, ref fail, guestMayDressHost && !hostMayDressGuest,
                "host allows, guest refuses: the guest may dress the host and the host may not dress the guest",
                "the two sides answers were forced to agree, which is the bug this replaces");

            Check(log, ref pass, ref fail,
                guest.Effective(VpbNetRuleDomain.Clothing, VpbNetRuleAxis.Mirror) == VpbNetRuleLevel.Allowed,
                "a side that refuses control still mirrors normally",
                "refusing control also switched off ordinary mirroring");
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRuleTable src = VpbNetRuleTable.FromPreset(VpbNetRulePreset.WatchTogether);
            src.Set(VpbNetRuleDomain.Look, VpbNetRuleAxis.Control, VpbNetRuleLevel.Ask);
            src.Revision = 7;

            VpbNetEventWriter w = new VpbNetEventWriter(256);
            w.Begin(VpbNetEventType.Rules, 42);
            VpbNetRuleTable.Write(w, src);
            int len = w.End();

            Check(log, ref pass, ref fail,
                len == VpbNetEventCodec.HeaderSize + VpbNetRuleTable.WireBytes,
                "a rule table is " + VpbNetRuleTable.WireBytes + " bytes on the wire",
                "a rule table wrote " + (len - VpbNetEventCodec.HeaderSize) + " bytes, expected "
                    + VpbNetRuleTable.WireBytes);

            VpbNetEventReader r = new VpbNetEventReader();
            VpbNetRuleTable dst = new VpbNetRuleTable();
            bool read = false;
            if (r.Begin(w.Buffer, 0, len)) read = VpbNetRuleTable.Read(r, out dst);

            Check(log, ref pass, ref fail,
                read && r.Type == VpbNetEventType.Rules && r.Seq == 42
                && dst.Lo == src.Lo && dst.Hi == src.Hi && dst.Revision == 7,
                "a rule table survives the wire unchanged",
                "a rule table changed crossing the wire");

            // Unknown table version refused whole — no partial read.
            byte[] bad = new byte[len];
            Buffer.BlockCopy(w.Buffer, 0, bad, 0, len);
            bad[VpbNetEventCodec.HeaderSize] = 99;

            VpbNetEventReader r2 = new VpbNetEventReader();
            VpbNetRuleTable ignored;
            bool refused = r2.Begin(bad, 0, len) && !VpbNetRuleTable.Read(r2, out ignored);
            Check(log, ref pass, ref fail, refused,
                "a table version this build does not know is refused whole",
                "a table with an unknown version was read anyway");
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
