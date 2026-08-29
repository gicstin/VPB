using System;
using System.Collections.Generic;
using System.Text;

namespace VpbNet
{
    public static class VpbNetContractSelfTest
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

            Line(log, "===== content contract self-test =====");

            UidSplitting(log, ref pass, ref fail);
            RoundTrip(log, ref pass, ref fail);
            RoleMerge(log, ref pass, ref fail);
            Capacity(log, ref pass, ref fail);
            Refusals(log, ref pass, ref fail);
            CompareMatch(log, ref pass, ref fail);
            CompareIncomplete(log, ref pass, ref fail);
            CompareApproximated(log, ref pass, ref fail);
            SeverityNeverFalls(log, ref pass, ref fail);
            IssueOverflow(log, ref pass, ref fail);
            HonestUnknowns(log, ref pass, ref fail);
            Messages(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 exchange   scene + resolved set + hashes survive the wire : " + Verdict(fail));
            Line(log, "EXIT 2/4 severity   a missing SCENE package is louder, not a refusal : " + Verdict(fail));
            Line(log, "EXIT 3/4 degrading  no verdict of any kind ever refuses a session   : " + Verdict(fail));
            Line(log, "EXIT 4/4 honesty    truncated or unknown never reports a match     : " + Verdict(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end content contract self-test =====");
            return fail == 0;
        }

        static void UidSplitting(StringBuilder log, ref int pass, ref int fail)
        {
            string f, v;
            Check(log, ref pass, ref fail, "Creator.Name.3 splits into family and version",
                VpbNetContractUid.TrySplit("Creator.Name.3", out f, out v) && f == "Creator.Name" && v == "3");
            Check(log, ref pass, ref fail, "Creator.Name.latest splits and reads as latest",
                VpbNetContractUid.TrySplit("Creator.Name.latest", out f, out v)
                && f == "Creator.Name" && VpbNetContractUid.IsLatest(v));
            Check(log, ref pass, ref fail, "LATEST is case-insensitive",
                VpbNetContractUid.TrySplit("Creator.Name.LATEST", out f, out v) && VpbNetContractUid.IsLatest(v));
            Check(log, ref pass, ref fail, "a non-version tail is not a version",
                !VpbNetContractUid.TrySplit("Creator.Name.beta", out f, out v));
            Check(log, ref pass, ref fail, "a uid with no dot has no version",
                !VpbNetContractUid.TrySplit("Creator", out f, out v));
            Check(log, ref pass, ref fail, "a trailing dot is not a version",
                !VpbNetContractUid.TrySplit("Creator.Name.", out f, out v));
            Check(log, ref pass, ref fail, "a leading dot is not a family",
                !VpbNetContractUid.TrySplit(".3", out f, out v));
            Check(log, ref pass, ref fail, "an unsplit uid reports itself as the family",
                !VpbNetContractUid.TrySplit("Creator.Name.beta", out f, out v) && f == "Creator.Name.beta");
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContract c = new VpbNetContract();
            Check(log, ref pass, ref fail, "scene identity is accepted",
                c.SetScene("Creator.ScenePack.2:/Saves/scene/party.json", 0xDEADBEEF));
            Check(log, ref pass, ref fail, "scene entries are added",
                c.TryAdd("Creator.ScenePack.2", 0x11111111, VpbNetContractRole.Scene)
                && c.TryAdd("MeshedVR.Assets.1", 0x22222222, VpbNetContractRole.Scene)
                && c.TryAdd("Someone.Hair.4", 0x33333333, VpbNetContractRole.Look));

            byte[] buf = new byte[VpbNetContractLimits.MaxContractBytes];
            int n = c.Write(buf);
            Check(log, ref pass, ref fail, "write succeeds and reports its length", n > 0 && n == c.WireBytes);

            VpbNetContract d = new VpbNetContract();
            Check(log, ref pass, ref fail, "read accepts what write produced",
                d.Read(buf, 0, n) == VpbNetContractReject.None);
            Check(log, ref pass, ref fail, "scene uid survives", d.SceneUid == c.SceneUid);
            Check(log, ref pass, ref fail, "scene hash survives", d.SceneHash == c.SceneHash);
            Check(log, ref pass, ref fail, "count survives", d.Count == 3);
            Check(log, ref pass, ref fail, "not marked truncated", !d.Truncated);

            bool same = true;
            for (int i = 0; i < d.Count; i++)
            {
                if (d.Uid(i) != c.Uid(i) || d.Hash(i) != c.Hash(i) || d.Role(i) != c.Role(i)) same = false;
            }
            Check(log, ref pass, ref fail, "every entry survives uid, hash and role", same);

            byte[] offset = new byte[n + 64];
            Buffer.BlockCopy(buf, 0, offset, 17, n);
            VpbNetContract e = new VpbNetContract();
            Check(log, ref pass, ref fail, "read honours a non-zero offset",
                e.Read(offset, 17, n) == VpbNetContractReject.None && e.Count == 3 && e.SceneUid == c.SceneUid);

            VpbNetContract empty = new VpbNetContract();
            int en = empty.Write(buf);
            VpbNetContract emptyBack = new VpbNetContract();
            Check(log, ref pass, ref fail, "an empty contract round-trips",
                en > 0 && emptyBack.Read(buf, 0, en) == VpbNetContractReject.None
                && emptyBack.Count == 0 && !emptyBack.HasScene);
        }

        static void RoleMerge(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContract c = new VpbNetContract();
            c.TryAdd("Creator.Shared.1", 0xAA, VpbNetContractRole.Look);
            c.TryAdd("Creator.Shared.1", 0, VpbNetContractRole.Scene);

            Check(log, ref pass, ref fail, "a package needed twice is listed once", c.Count == 1);
            Check(log, ref pass, ref fail, "both roles are kept",
                c.Role(0) == (VpbNetContractRole.Scene | VpbNetContractRole.Look));
            Check(log, ref pass, ref fail, "a known hash is not overwritten by an unknown one", c.Hash(0) == 0xAA);
            Check(log, ref pass, ref fail, "case does not create a second entry",
                c.TryAdd("creator.shared.1", 0, VpbNetContractRole.Look) && c.Count == 1);
            Check(log, ref pass, ref fail, "a roleless entry is refused",
                !c.TryAdd("Creator.Other.1", 0, 0));

            VpbNetContract m = new VpbNetContract();
            m.TryAdd("Creator.Outfit.latest", 0, VpbNetContractRole.Scene);
            m.TryAdd("Creator.Outfit.1", 0x55, VpbNetContractRole.Look);
            Check(log, ref pass, ref fail, ".latest and a concrete version are one package",
                m.Count == 1);
            Check(log, ref pass, ref fail, "the concrete version is the one kept",
                m.Uid(0) == "Creator.Outfit.1" && m.Hash(0) == 0x55);
            Check(log, ref pass, ref fail, "the merged entry keeps both roles",
                m.Role(0) == (VpbNetContractRole.Scene | VpbNetContractRole.Look));

            VpbNetContract m2 = new VpbNetContract();
            m2.TryAdd("Creator.Outfit.1", 0x55, VpbNetContractRole.Look);
            m2.TryAdd("Creator.Outfit.latest", 0, VpbNetContractRole.Scene);
            Check(log, ref pass, ref fail, "the merge works in either arrival order",
                m2.Count == 1 && m2.Uid(0) == "Creator.Outfit.1" && m2.Hash(0) == 0x55
                && m2.Role(0) == (VpbNetContractRole.Scene | VpbNetContractRole.Look));

            byte[] mbuf = new byte[VpbNetContractLimits.MaxContractBytes];
            int mn = m2.Write(mbuf);
            VpbNetContract mback = new VpbNetContract();
            Check(log, ref pass, ref fail, "a merged contract still reports its own length",
                mn > 0 && mn == m2.WireBytes && mback.Read(mbuf, 0, mn) == VpbNetContractReject.None
                && mback.Count == 1);

            VpbNetContract m3 = new VpbNetContract();
            m3.TryAdd("Creator.Outfit.1", 0, VpbNetContractRole.Look);
            m3.TryAdd("Creator.Outfit.2", 0, VpbNetContractRole.Look);
            Check(log, ref pass, ref fail, "two real versions of one family stay two entries",
                m3.Count == 2);
        }

        static void Capacity(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a contract fits the fragment assembler",
                VpbNetContractLimits.MaxContractBytes <= VpbNetKeyframeAssembler.MaxKeyframeBytes);

            VpbNetContract real = new VpbNetContract();
            real.SetScene("Creator.ScenePack.2:/Saves/scene/party.json", 1);
            for (int i = 0; i < VpbNetContractLimits.MaxDependencies; i++)
            {
                real.TryAdd("Creator" + i + ".Package" + i + "." + (i % 9 + 1), (uint)(i * 2654435761u), VpbNetContractRole.Scene);
            }
            byte[] buf = new byte[VpbNetContractLimits.MaxContractBytes];
            int n = real.Write(buf);
            Check(log, ref pass, ref fail, "a full realistic dependency set fits",
                real.Count == VpbNetContractLimits.MaxDependencies && !real.Truncated && n > 0);
            Line(log, "  note " + VpbNetContractLimits.MaxDependencies + " realistic packages = " + n + " B in "
                + VpbNetKeyframeAssembler.FragmentCount(n) + " fragments");

            VpbNetContract over = new VpbNetContract();
            for (int i = 0; i < VpbNetContractLimits.MaxDependencies + 8; i++)
            {
                over.TryAdd("Creator" + i + ".Package." + i, 0, VpbNetContractRole.Look);
            }
            Check(log, ref pass, ref fail, "past the count cap the contract stops and says so",
                over.Count == VpbNetContractLimits.MaxDependencies && over.Truncated && over.Omitted == 8);

            VpbNetContract wide = new VpbNetContract();
            string pad = new string('\u00e9', 80);
            int added = 0;
            for (int i = 0; i < VpbNetContractLimits.MaxDependencies; i++)
            {
                string uid = pad + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".Package.1";
                if (wide.TryAdd(uid, 0, VpbNetContractRole.Look)) added++;
            }
            Check(log, ref pass, ref fail, "the byte cap is reachable and also marks truncation",
                added < VpbNetContractLimits.MaxDependencies && wide.Truncated && wide.Omitted > 0);
            Check(log, ref pass, ref fail, "a truncated contract still writes what it has",
                wide.Write(buf) > 0);

            VpbNetContract back = new VpbNetContract();
            int wn = wide.Write(buf);
            Check(log, ref pass, ref fail, "the truncated flag survives the wire",
                back.Read(buf, 0, wn) == VpbNetContractReject.None && back.Truncated);
        }

        static void Refusals(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContract c = new VpbNetContract();
            Check(log, ref pass, ref fail, "a plugin reference is refused as a dependency",
                !c.TryAdd("Creator.Pack.1:/Custom/Scripts/evil.cs", 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "a trailing-dot plugin reference is refused",
                !c.TryAdd("Creator.Pack.1:/Custom/Scripts/evil.cs.", 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "a traversal is refused",
                !c.TryAdd("../../Windows/System32", 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "a drive path is refused",
                !c.TryAdd("C:/Windows/System32", 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "a backslash path is refused",
                !c.TryAdd("C:\\Windows", 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "an over-long uid is refused",
                !c.TryAdd(new string('x', VpbNetContractLimits.MaxUidChars + 1), 0, VpbNetContractRole.Scene));
            Check(log, ref pass, ref fail, "a package-qualified uid is still accepted",
                c.TryAdd("Creator.Pack.1:/Custom/Clothing/thing.vam", 0, VpbNetContractRole.Look));
            Check(log, ref pass, ref fail, "a plugin reference is refused as the scene",
                !c.SetScene("Creator.Pack.1:/Custom/Scripts/evil.cslist", 0));
            Check(log, ref pass, ref fail, "the scene cannot be set after entries exist",
                !c.SetScene("Creator.Pack.1:/Saves/scene/x.json", 0));

            VpbNetContract src = new VpbNetContract();
            src.SetScene("Creator.Pack.1:/Saves/scene/x.json", 7);
            src.TryAdd("Creator.Pack.1", 5, VpbNetContractRole.Scene);
            byte[] buf = new byte[VpbNetContractLimits.MaxContractBytes];
            int n = src.Write(buf);

            VpbNetContract d = new VpbNetContract();
            byte keep = buf[0];
            buf[0] = 99;
            Check(log, ref pass, ref fail, "a different format version is named, not guessed",
                d.Read(buf, 0, n) == VpbNetContractReject.BadVersion);
            buf[0] = keep;

            Check(log, ref pass, ref fail, "a short buffer is refused",
                d.Read(buf, 0, VpbNetContract.HeaderSize - 1) == VpbNetContractReject.Truncated);
            Check(log, ref pass, ref fail, "a cut-off payload is refused",
                d.Read(buf, 0, n - 3) == VpbNetContractReject.Truncated);
            Check(log, ref pass, ref fail, "a null buffer is refused",
                d.Read(null, 0, n) == VpbNetContractReject.Truncated);

            byte[] bad = new byte[n];
            Buffer.BlockCopy(buf, 0, bad, 0, n);
            VpbIpc.WriteU16(bad, 2, VpbNetContractLimits.MaxDependencies + 1);
            Check(log, ref pass, ref fail, "an impossible count is refused before any allocation",
                d.Read(bad, 0, n) == VpbNetContractReject.BadCount);

            Buffer.BlockCopy(buf, 0, bad, 0, n);
            bad[n - 1] = 0;
            Check(log, ref pass, ref fail, "an entry with no role is refused",
                d.Read(bad, 0, n) == VpbNetContractReject.BadRole);

            Buffer.BlockCopy(buf, 0, bad, 0, n);
            bad[1] = 0;
            Check(log, ref pass, ref fail, "a scene flag that disagrees with the payload is refused",
                d.Read(bad, 0, n) == VpbNetContractReject.BadIdentifier);

            VpbNetContract dup = new VpbNetContract();
            dup.TryAdd("Creator.PackA.1", 1, VpbNetContractRole.Scene);
            dup.TryAdd("Creator.PackB.1", 1, VpbNetContractRole.Scene);
            int dn = dup.Write(buf);
            int second = FindSecondEntryUid(buf, dn);
            if (second > 0) buf[second + 12] = (byte)'a';
            Check(log, ref pass, ref fail, "the same package listed twice is refused, case aside",
                second > 0 && d.Read(buf, 0, dn) == VpbNetContractReject.Duplicate);

            Check(log, ref pass, ref fail, "an oversize claim is refused",
                d.Read(new byte[VpbNetContractLimits.MaxContractBytes + 64], 0,
                    VpbNetContractLimits.MaxContractBytes + 1) == VpbNetContractReject.Oversize);
        }

        static void CompareMatch(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Creator.ScenePack.2", 0x11111111);
            cat.Install("MeshedVR.Assets.1", 0x22222222);
            cat.Install("Someone.Hair.4", 0x33333333);

            VpbNetContract c = Contract(
                "Creator.ScenePack.2", 0x11111111, VpbNetContractRole.Scene,
                "MeshedVR.Assets.1", 0x22222222, VpbNetContractRole.Scene,
                "Someone.Hair.4", 0x33333333, VpbNetContractRole.Look);

            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "an identical library matches",
                VpbNetContractCheck.Compare(c, cat, r) == VpbNetContractVerdict.Match);
            Check(log, ref pass, ref fail, "a match reports no issues", r.IssueCount == 0);
            Check(log, ref pass, ref fail, "a match reports what it checked", r.Checked == 3);

            FakeCatalog blind = new FakeCatalog();
            blind.Install("Creator.ScenePack.2", 0);
            blind.Install("MeshedVR.Assets.1", 0);
            blind.Install("Someone.Hair.4", 0);
            VpbNetContractReport r2 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "an unknown local hash never invents content drift",
                VpbNetContractCheck.Compare(c, blind, r2) == VpbNetContractVerdict.Match);

            VpbNetContract noHash = Contract("Creator.ScenePack.2", 0, VpbNetContractRole.Scene);
            VpbNetContractReport r3 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "an unknown remote hash never invents content drift",
                VpbNetContractCheck.Compare(noHash, cat, r3) == VpbNetContractVerdict.Match);
        }

        static void CompareIncomplete(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("MeshedVR.Assets.1", 0x22222222);

            VpbNetContract c = Contract(
                "Creator.ScenePack.2", 0x11111111, VpbNetContractRole.Scene,
                "MeshedVR.Assets.1", 0x22222222, VpbNetContractRole.Scene);

            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a missing scene package reports incomplete, never a refusal",
                VpbNetContractCheck.Compare(c, cat, r) == VpbNetContractVerdict.Incomplete);
            Check(log, ref pass, ref fail, "the incomplete package is named exactly once",
                r.IssueCount == 1 && r.Uid(0) == "Creator.ScenePack.2");
            Check(log, ref pass, ref fail, "the incomplete issue is a missing package",
                r.Kind(0) == VpbNetContractIssueKind.MissingPackage);
            Check(log, ref pass, ref fail, "the critical count is separate from the issue count",
                r.CriticalCount == 1);
            Check(log, ref pass, ref fail, "the report says what is missing, not that it failed",
                r.Describe(0).IndexOf("Creator.ScenePack.2", StringComparison.Ordinal) >= 0);

            VpbNetContract both = Contract(
                "Creator.ScenePack.2", 0, (byte)(VpbNetContractRole.Scene | VpbNetContractRole.Look));
            VpbNetContractReport r2 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a package needed by both takes the scene half's severity",
                VpbNetContractCheck.Compare(both, cat, r2) == VpbNetContractVerdict.Incomplete);
        }

        static void CompareApproximated(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Creator.ScenePack.2", 0x11111111);
            cat.Install("Someone.Hair.3", 0x99999999);
            cat.Install("Third.Skin.1", 0x44444444);

            VpbNetContract drift = Contract(
                "Creator.ScenePack.2", 0x11111111, VpbNetContractRole.Scene,
                "Someone.Hair.4", 0x33333333, VpbNetContractRole.Look);
            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a different version of an installed package approximates",
                VpbNetContractCheck.Compare(drift, cat, r) == VpbNetContractVerdict.Approximated);
            Check(log, ref pass, ref fail, "the drift names the version actually installed",
                r.IssueCount == 1 && r.Kind(0) == VpbNetContractIssueKind.VersionDrift
                && r.LocalUid(0) == "Someone.Hair.3");

            VpbNetContract selfMade = Contract(
                "Creator.ScenePack.2", 0x11111111, VpbNetContractRole.Scene,
                "Nobody.HomeMadeOutfit.1", 0x55555555, VpbNetContractRole.Look);
            VpbNetContractReport r2 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a look-only package nobody can get stays approximated",
                VpbNetContractCheck.Compare(selfMade, cat, r2) == VpbNetContractVerdict.Approximated);
            Check(log, ref pass, ref fail, "nothing about a look-only gap is counted as critical",
                r2.CriticalCount == 0);

            VpbNetContract content = Contract("Third.Skin.1", 0x77777777, VpbNetContractRole.Look);
            VpbNetContractReport r3 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "the same version with different contents approximates",
                VpbNetContractCheck.Compare(content, cat, r3) == VpbNetContractVerdict.Approximated
                && r3.Kind(0) == VpbNetContractIssueKind.ContentDrift);

            VpbNetContract latest = Contract("Someone.Hair.latest", 0x33333333, VpbNetContractRole.Look);
            VpbNetContractReport r4 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a .latest reference is not a disagreement",
                VpbNetContractCheck.Compare(latest, cat, r4) == VpbNetContractVerdict.Match);

            VpbNetContract sceneDrift = Contract("Creator.ScenePack.3", 0, VpbNetContractRole.Scene);
            VpbNetContractReport r5 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a scene package at another version warns only",
                VpbNetContractCheck.Compare(sceneDrift, cat, r5) == VpbNetContractVerdict.Approximated);
        }

        static void SeverityNeverFalls(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Ok.Package.1", 0x1);

            VpbNetContract c = Contract(
                "Gone.Package.1", 0, VpbNetContractRole.Scene,
                "Ok.Package.1", 0x1, VpbNetContractRole.Scene);
            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a later clean entry cannot lower the verdict",
                VpbNetContractCheck.Compare(c, cat, r) == VpbNetContractVerdict.Incomplete);

            Check(log, ref pass, ref fail, "severity of a missing scene package is incomplete",
                VpbNetContractReport.SeverityOf(VpbNetContractIssueKind.MissingPackage, VpbNetContractRole.Scene)
                    == VpbNetContractVerdict.Incomplete);
            Check(log, ref pass, ref fail, "severity of a missing look package is approximated",
                VpbNetContractReport.SeverityOf(VpbNetContractIssueKind.MissingPackage, VpbNetContractRole.Look)
                    == VpbNetContractVerdict.Approximated);
            Check(log, ref pass, ref fail, "severity of any drift is approximated",
                VpbNetContractReport.SeverityOf(VpbNetContractIssueKind.VersionDrift, VpbNetContractRole.Scene)
                    == VpbNetContractVerdict.Approximated
                && VpbNetContractReport.SeverityOf(VpbNetContractIssueKind.ContentDrift, VpbNetContractRole.Scene)
                    == VpbNetContractVerdict.Approximated);

            VpbNetContractReport reused = new VpbNetContractReport();
            VpbNetContractCheck.Compare(c, cat, reused);
            VpbNetContract clean = Contract("Ok.Package.1", 0x1, VpbNetContractRole.Scene);
            Check(log, ref pass, ref fail, "a reused report does not carry the previous verdict",
                VpbNetContractCheck.Compare(clean, cat, reused) == VpbNetContractVerdict.Match
                && reused.IssueCount == 0 && reused.CriticalCount == 0);
        }

        static void IssueOverflow(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            VpbNetContract c = new VpbNetContract();
            int want = VpbNetContractLimits.MaxIssues + 11;
            for (int i = 0; i < want; i++) c.TryAdd("Gone" + i + ".Package.1", 0, VpbNetContractRole.Scene);

            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "more issues than the list holds still reports incomplete",
                VpbNetContractCheck.Compare(c, cat, r) == VpbNetContractVerdict.Incomplete);
            Check(log, ref pass, ref fail, "the listed issues stop at the cap",
                r.IssueCount == VpbNetContractLimits.MaxIssues);
            Check(log, ref pass, ref fail, "the overflow is counted rather than hidden",
                r.OverflowedIssues == want - VpbNetContractLimits.MaxIssues);
            Check(log, ref pass, ref fail, "the critical count is the true total, not the listed one",
                r.CriticalCount == want);
        }

        static void HonestUnknowns(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Ok.Package.1", 0x1);

            VpbNetContract t = Contract("Ok.Package.1", 0x1, VpbNetContractRole.Scene);
            for (int i = 0; i < VpbNetContractLimits.MaxDependencies + 4; i++)
            {
                t.TryAdd("Filler" + i + ".Package.1", 0, VpbNetContractRole.Look);
            }
            Check(log, ref pass, ref fail, "the overflowing source contract is marked truncated", t.Truncated);

            FakeCatalog all = new FakeCatalog();
            for (int i = 0; i < VpbNetContractLimits.MaxDependencies; i++) all.Install("Filler" + i + ".Package.1", 0);
            all.Install("Ok.Package.1", 0x1);

            VpbNetContractReport r = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "a truncated list never reports a clean match",
                VpbNetContractCheck.Compare(t, all, r) == VpbNetContractVerdict.Approximated);
            Check(log, ref pass, ref fail, "the truncation is the reported issue",
                r.IssueCount == 1 && r.Kind(0) == VpbNetContractIssueKind.ListTruncated);

            VpbNetContractReport r2 = new VpbNetContractReport();
            VpbNetContract clean = Contract("Ok.Package.1", 0x1, VpbNetContractRole.Scene);
            Check(log, ref pass, ref fail, "no catalog never reports a clean match either",
                VpbNetContractCheck.Compare(clean, null, r2) == VpbNetContractVerdict.Approximated);

            VpbNetContractReport r3 = new VpbNetContractReport();
            Check(log, ref pass, ref fail, "no contract at all is a match, not a crash",
                VpbNetContractCheck.Compare(null, cat, r3) == VpbNetContractVerdict.Match);
        }

        static void Messages(StringBuilder log, ref int pass, ref int fail)
        {
            bool allNamed = true;
            for (int i = 0; i <= (int)VpbNetContractReject.Duplicate; i++)
            {
                string s = VpbNetContract.Explain((VpbNetContractReject)i);
                if (string.IsNullOrEmpty(s) || s.Length < 2) allNamed = false;
                if (s.IndexOf(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal) >= 0 && i > 1) allNamed = false;
            }
            Check(log, ref pass, ref fail, "every refusal has prose, never a bare code", allNamed);

            FakeCatalog cat = new FakeCatalog();
            cat.Install("Ok.Package.1", 0x1);

            VpbNetContractReport match = new VpbNetContractReport();
            VpbNetContractCheck.Compare(Contract("Ok.Package.1", 0x1, VpbNetContractRole.Scene), cat, match);
            Check(log, ref pass, ref fail, "a match summary names how much was checked",
                match.Summary().IndexOf("1", StringComparison.Ordinal) >= 0);

            VpbNetContractReport incomplete = new VpbNetContractReport();
            VpbNetContractCheck.Compare(Contract("Gone.Package.1", 0, VpbNetContractRole.Scene), cat, incomplete);
            string bs = incomplete.Summary();
            Check(log, ref pass, ref fail, "an incomplete summary says the join still happens",
                bs.IndexOf("joining anyway", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(log, ref pass, ref fail, "a single missing scene package reads in the singular",
                bs.IndexOf("package the scene needs is", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "no summary ever tells the user to try again",
                bs.IndexOf("try again", StringComparison.OrdinalIgnoreCase) < 0);

            VpbNetContractReport approx = new VpbNetContractReport();
            VpbNetContractCheck.Compare(Contract("Gone.Package.1", 0, VpbNetContractRole.Look), cat, approx);
            Check(log, ref pass, ref fail, "an approximated summary says the join still happens",
                approx.Summary().IndexOf("approximated", StringComparison.Ordinal) >= 0);

            Check(log, ref pass, ref fail, "roles read as words",
                VpbNetContractRole.Name(VpbNetContractRole.Scene) == "scene"
                && VpbNetContractRole.Name(VpbNetContractRole.Look) == "look"
                && VpbNetContractRole.Name((byte)(VpbNetContractRole.Scene | VpbNetContractRole.Look)) == "scene+look");
        }

        static VpbNetContract Contract(params object[] triples)
        {
            VpbNetContract c = new VpbNetContract();
            for (int i = 0; i + 2 < triples.Length; i += 3)
            {
                c.TryAdd((string)triples[i], Convert.ToUInt32(triples[i + 1]), Convert.ToByte(triples[i + 2]));
            }
            return c;
        }

        static int FindSecondEntryUid(byte[] buf, int len)
        {
            int o = VpbNetContract.HeaderSize;
            if (o >= len) return -1;
            o += 1 + buf[o];
            if (o >= len) return -1;
            o += 1 + buf[o] + 5;
            if (o >= len) return -1;
            return o + 1;
        }

        sealed class FakeCatalog : IVpbNetContractCatalog
        {
            readonly Dictionary<string, uint> _exact = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, string> _family = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void Install(string uid, uint hash)
            {
                _exact[uid] = hash;
                string f, v;
                if (VpbNetContractUid.TrySplit(uid, out f, out v)) _family[f] = uid;
            }

            public bool TryResolveExact(string uid, out uint contentHash)
            {
                return _exact.TryGetValue(uid, out contentHash);
            }

            public bool TryResolveFamily(string family, out string installedUid)
            {
                return _family.TryGetValue(family, out installedUid);
            }
        }

        static string Verdict(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok   " + what);
            }
            else
            {
                fail++;
                Line(log, "  FAIL " + what);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            log.Append(s);
            log.Append('\n');
        }
    }
}
