using System;
using System.Collections.Generic;
using System.Text;

namespace VpbNet
{
    public static class VpbNetOfferSelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(16384);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== content offer self-test =====");

            OfferRoundTrip(log, ref pass, ref fail);
            OfferRefusals(log, ref pass, ref fail);
            OfferFitsAnEvent(log, ref pass, ref fail);
            StatusRoundTrip(log, ref pass, ref fail);
            StatusUnknownPhase(log, ref pass, ref fail);
            StatusProgress(log, ref pass, ref fail);
            StatusText(log, ref pass, ref fail);
            PhaseRules(log, ref pass, ref fail);
            ManifestRoundTrip(log, ref pass, ref fail);
            ManifestCapacity(log, ref pass, ref fail);
            ManifestRefusals(log, ref pass, ref fail);
            ManifestFitsFragments(log, ref pass, ref fail);
            PlanScenePackageIsStrict(log, ref pass, ref fail);
            PlanDepsAcceptFamily(log, ref pass, ref fail);
            PlanWithNoCatalog(log, ref pass, ref fail);
            SizeText(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/5 offer      a scene identity survives the wire inside one event  : " + Verdict(fail));
            Line(log, "EXIT 2/5 status     an unknown phase is never mistaken for a settled one : " + Verdict(fail));
            Line(log, "EXIT 3/5 manifest   always fits one fragment generation, or is truncated : " + Verdict(fail));
            Line(log, "EXIT 4/5 plan       the scene package needs an exact match, deps do not  : " + Verdict(fail));
            Line(log, "EXIT 5/5 safety     no path, uid or plugin reference gets in unchecked   : " + Verdict(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end content offer self-test =====");
            return fail == 0;
        }

        static void OfferRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetOfferInfo o = new VpbNetOfferInfo();
            o.Clear();
            o.OfferId = 0x0BADF00D;
            o.Flags = VpbNetOfferInfo.FlagFromPackage;
            o.ScenePath = "Creator.ScenePack.2:/Saves/scene/night club loft.json";
            o.PackageUid = "Creator.ScenePack.2";
            o.PackageHash = 0xDEADBEEF;
            o.Title = "Night Club Loft";
            o.ManifestGen = 7;
            o.ManifestCount = 42;
            o.TotalKiB = 491520;

            VpbNetEventWriter w = NewWriter();
            w.Begin(VpbNetEventType.SceneOffer, 11);
            o.Write(w);
            int n = w.End();
            Check(log, ref pass, ref fail, "an offer writes", n > 0);

            VpbNetOfferInfo d;
            Check(log, ref pass, ref fail, "an offer reads back", ReadOffer(w.Buffer, n, out d));
            Check(log, ref pass, ref fail, "offer id survives", d.OfferId == o.OfferId);
            Check(log, ref pass, ref fail, "scene path survives", d.ScenePath == o.ScenePath);
            Check(log, ref pass, ref fail, "package uid survives", d.PackageUid == o.PackageUid);
            Check(log, ref pass, ref fail, "package hash survives", d.PackageHash == o.PackageHash);
            Check(log, ref pass, ref fail, "title survives", d.Title == o.Title);
            Check(log, ref pass, ref fail, "manifest generation survives", d.ManifestGen == o.ManifestGen);
            Check(log, ref pass, ref fail, "manifest count survives", d.ManifestCount == o.ManifestCount);
            Check(log, ref pass, ref fail, "size hint survives", d.TotalKiB == o.TotalKiB);
            Check(log, ref pass, ref fail, "the from-package flag survives", d.FromPackage);
            Check(log, ref pass, ref fail, "edit mode was not invented", !d.EditMode);
            Check(log, ref pass, ref fail, "a decoded offer is present", d.IsPresent);
        }

        static void OfferRefusals(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetOfferInfo bad;

            Check(log, ref pass, ref fail, "offer id zero is refused",
                !ReadOffer(BuildOffer(0, "Creator.Pack.1:/Saves/scene/a.json", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "a plugin file as a scene path is refused",
                !ReadOffer(BuildOffer(1, "Creator.Pack.1:/Custom/Scripts/x.cs", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "a traversing scene path is refused",
                !ReadOffer(BuildOffer(1, "Creator.Pack.1:/Saves/../../secret.json", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "an absolute scene path is refused",
                !ReadOffer(BuildOffer(1, "/Saves/scene/a.json", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "a windows drive scene path is refused",
                !ReadOffer(BuildOffer(1, "C:/Saves/scene/a.json", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "a backslash scene path is refused",
                !ReadOffer(BuildOffer(1, "Creator.Pack.1:\\Saves\\a.json", "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "an empty scene path is refused",
                !ReadOffer(BuildOffer(1, string.Empty, "Creator.Pack.1", "t", 0, 0), out bad));

            Check(log, ref pass, ref fail, "a manifest count above the cap is refused",
                !ReadOffer(BuildOffer(1, "Creator.Pack.1:/Saves/scene/a.json", "Creator.Pack.1", "t", 0,
                    VpbNetManifestLimits.MaxEntries + 1), out bad));

            Check(log, ref pass, ref fail, "a loose scene with no package is accepted",
                ReadOffer(BuildOffer(1, "Saves/scene/mine.json", string.Empty, "Mine", 0, 0), out bad)
                && bad.PackageUid.Length == 0);
        }

        static void OfferFitsAnEvent(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetOfferInfo o = new VpbNetOfferInfo();
            o.Clear();
            o.OfferId = uint.MaxValue;
            o.ScenePath = Repeat('p', VpbNetOfferLimits.MaxScenePath);
            o.PackageUid = Repeat('u', VpbNetOfferLimits.MaxPackageUid);
            o.Title = Repeat('t', VpbNetOfferLimits.MaxTitle);
            o.PackageHash = uint.MaxValue;
            o.ManifestGen = 0xFFFF;
            o.ManifestCount = VpbNetManifestLimits.MaxEntries;
            o.TotalKiB = uint.MaxValue;

            VpbNetEventWriter w = NewWriter();
            w.Begin(VpbNetEventType.SceneOffer, 1);
            o.Write(w);
            int n = w.End();
            Check(log, ref pass, ref fail, "the largest legal offer still fits one event payload", n > 0);
        }

        static void StatusRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContentStatus s = new VpbNetContentStatus();
            s.Clear();
            s.OfferId = 0x12345678;
            s.Phase = VpbNetContentPhase.Fetching;
            s.Fail = VpbNetContentFail.None;
            s.Have = 3;
            s.Need = 7;
            s.DoneKiB = 219136;
            s.TotalKiB = 491520;
            s.Current = "AcidBubbles.Timeline.14";

            VpbNetEventWriter w = NewWriter();
            w.Begin(VpbNetEventType.ContentState, 5);
            s.Write(w);
            int n = w.End();
            Check(log, ref pass, ref fail, "a status writes", n > 0);

            VpbNetContentStatus d;
            Check(log, ref pass, ref fail, "a status reads back", ReadStatus(w.Buffer, n, out d));
            Check(log, ref pass, ref fail, "every status field survives", d.SameAs(s));
        }

        static void StatusUnknownPhase(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContentStatus s = new VpbNetContentStatus();
            s.Clear();
            s.OfferId = 9;
            s.Phase = 200;

            VpbNetEventWriter w = NewWriter();
            w.Begin(VpbNetEventType.ContentState, 1);
            s.Write(w);
            int n = w.End();

            VpbNetContentStatus d;
            Check(log, ref pass, ref fail, "a phase from a later build decodes", ReadStatus(w.Buffer, n, out d));
            Check(log, ref pass, ref fail, "it decodes as unknown, not as itself",
                d.Phase == VpbNetContentPhase.Unknown);
            Check(log, ref pass, ref fail, "and unknown is never settled",
                !VpbNetContentPhase.IsSettled(d.Phase));
            Check(log, ref pass, ref fail, "and unknown never loads",
                !VpbNetContentPhase.CanLoad(d.Phase));
        }

        static void StatusProgress(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetContentStatus s = new VpbNetContentStatus();
            s.Clear();
            s.Phase = VpbNetContentPhase.Fetching;
            s.DoneKiB = 50;
            s.TotalKiB = 100;
            Check(log, ref pass, ref fail, "bytes drive the bar when they are known",
                s.Fraction01 > 0.49f && s.Fraction01 < 0.51f);

            s.DoneKiB = 500;
            Check(log, ref pass, ref fail, "a bar cannot run past full", s.Fraction01 <= 1f);

            s.DoneKiB = 0;
            s.TotalKiB = 0;
            s.Have = 1;
            s.Need = 4;
            Check(log, ref pass, ref fail, "counts drive the bar when bytes are not known",
                s.Fraction01 > 0.24f && s.Fraction01 < 0.26f);

            s.Phase = VpbNetContentPhase.Degraded;
            Check(log, ref pass, ref fail, "a loadable side reads as full even when short",
                s.Fraction01 >= 1f);

            s.Phase = VpbNetContentPhase.Unknown;
            s.Have = 0;
            s.Need = 0;
            Check(log, ref pass, ref fail, "nothing known is an empty bar, not a divide by zero",
                s.Fraction01 == 0f);
        }

        static void StatusText(StringBuilder log, ref int pass, ref int fail)
        {
            StringBuilder sb = new StringBuilder(128);
            VpbNetContentStatus s = new VpbNetContentStatus();

            for (byte p = 0; p < VpbNetContentPhase.Count; p++)
            {
                s.Clear();
                s.Phase = p;
                s.Have = 2;
                s.Need = 5;
                s.TotalKiB = 4096;
                s.DoneKiB = 1024;
                sb.Length = 0;
                s.Describe(sb);
                Check(log, ref pass, ref fail,
                    "phase " + VpbNetContentPhase.Name(p) + " renders a sentence", sb.Length > 0);
            }

            s.Clear();
            s.Phase = VpbNetContentPhase.Failed;
            s.Fail = VpbNetContentFail.NotOnHub;
            sb.Length = 0;
            s.Describe(sb);
            Check(log, ref pass, ref fail, "a failure names its reason",
                sb.ToString().IndexOf("Hub", StringComparison.Ordinal) >= 0);
        }

        static void PhaseRules(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "ready is settled and loadable",
                VpbNetContentPhase.IsSettled(VpbNetContentPhase.Ready)
                && VpbNetContentPhase.CanLoad(VpbNetContentPhase.Ready));
            Check(log, ref pass, ref fail, "degraded is settled and still loadable",
                VpbNetContentPhase.IsSettled(VpbNetContentPhase.Degraded)
                && VpbNetContentPhase.CanLoad(VpbNetContentPhase.Degraded));
            Check(log, ref pass, ref fail, "failed is settled but never loadable",
                VpbNetContentPhase.IsSettled(VpbNetContentPhase.Failed)
                && !VpbNetContentPhase.CanLoad(VpbNetContentPhase.Failed));
            Check(log, ref pass, ref fail, "refused is settled but never loadable",
                VpbNetContentPhase.IsSettled(VpbNetContentPhase.Refused)
                && !VpbNetContentPhase.CanLoad(VpbNetContentPhase.Refused));
            Check(log, ref pass, ref fail, "fetching is neither settled nor loadable",
                !VpbNetContentPhase.IsSettled(VpbNetContentPhase.Fetching)
                && !VpbNetContentPhase.CanLoad(VpbNetContentPhase.Fetching));
            Check(log, ref pass, ref fail, "waiting on an answer is not settled",
                !VpbNetContentPhase.IsSettled(VpbNetContentPhase.Waiting));
            Check(log, ref pass, ref fail, "loading the scene is not settled, loadable or stoppable",
                !VpbNetContentPhase.IsSettled(VpbNetContentPhase.Loading)
                && !VpbNetContentPhase.CanLoad(VpbNetContentPhase.Loading)
                && !VpbNetContentPhase.IsWorking(VpbNetContentPhase.Loading));
        }

        static void ManifestRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetManifest m = new VpbNetManifest();
            m.SetGeneration(9);
            m.AddKiB(1024);
            m.AddKiB(2048);

            Check(log, ref pass, ref fail, "manifest entries are added",
                m.TryAdd("Creator.ScenePack.2", VpbNetContractRole.Scene)
                && m.TryAdd("MeshedVR.Assets.1", VpbNetContractRole.Look)
                && m.TryAdd("Someone.Hair.4", VpbNetContractRole.Look));

            Check(log, ref pass, ref fail, "a repeat entry merges its role rather than growing the list",
                m.TryAdd("Someone.Hair.4", VpbNetContractRole.Scene) && m.Count == 3);

            byte[] buf = new byte[VpbNetManifestLimits.MaxBytes];
            int n = m.Write(buf);
            Check(log, ref pass, ref fail, "manifest writes and reports its length", n > 0 && n == m.WireBytes);

            VpbNetManifest d = new VpbNetManifest();
            Check(log, ref pass, ref fail, "manifest reads back",
                d.Read(buf, 0, n) == VpbNetContractReject.None);
            Check(log, ref pass, ref fail, "count survives", d.Count == m.Count);
            Check(log, ref pass, ref fail, "generation survives", d.Generation == 9);
            Check(log, ref pass, ref fail, "size total survives", d.TotalKiB == 3072u);
            Check(log, ref pass, ref fail, "uids survive in order",
                d.Uid(0) == "Creator.ScenePack.2" && d.Uid(2) == "Someone.Hair.4");
            Check(log, ref pass, ref fail, "the merged role survives",
                (d.Role(2) & VpbNetContractRole.Scene) != 0 && (d.Role(2) & VpbNetContractRole.Look) != 0);
            Check(log, ref pass, ref fail, "nothing was truncated", !d.Truncated);
        }

        static void ManifestCapacity(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetManifest m = new VpbNetManifest();
            int added = 0;
            for (int i = 0; i < VpbNetManifestLimits.MaxEntries + 200; i++)
            {
                if (m.TryAdd("Creator.Filler" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".1",
                    VpbNetContractRole.Look)) added++;
            }

            Check(log, ref pass, ref fail, "a manifest stops adding rather than overflowing",
                m.Count <= VpbNetManifestLimits.MaxEntries);
            Check(log, ref pass, ref fail, "and says so", m.Truncated && m.Omitted > 0);
            Check(log, ref pass, ref fail, "and only counted what it kept", added == m.Count);

            byte[] buf = new byte[VpbNetManifestLimits.MaxBytes];
            int n = m.Write(buf);
            Check(log, ref pass, ref fail, "a full manifest still writes", n > 0);

            VpbNetManifest d = new VpbNetManifest();
            Check(log, ref pass, ref fail, "a full manifest still reads",
                d.Read(buf, 0, n) == VpbNetContractReject.None);
            Check(log, ref pass, ref fail, "the truncation flag survives the wire", d.Truncated);
        }

        static void ManifestRefusals(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetManifest m = new VpbNetManifest();
            Check(log, ref pass, ref fail, "a plugin file is never a manifest entry",
                !m.TryAdd("Creator.Pack.1/Custom/Scripts/thing.cslist", VpbNetContractRole.Look));
            Check(log, ref pass, ref fail, "a traversing uid is refused",
                !m.TryAdd("../../etc/passwd", VpbNetContractRole.Look));
            Check(log, ref pass, ref fail, "a roleless entry is refused",
                !m.TryAdd("Creator.Pack.1", 0));
            Check(log, ref pass, ref fail, "an empty uid is refused",
                !m.TryAdd(string.Empty, VpbNetContractRole.Look));
            Check(log, ref pass, ref fail, "an over-long uid is refused",
                !m.TryAdd(Repeat('x', VpbNetManifestLimits.MaxUidChars + 1), VpbNetContractRole.Look));

            byte[] buf = new byte[64];
            VpbNetManifest d = new VpbNetManifest();
            Check(log, ref pass, ref fail, "a short buffer is truncated, not read",
                d.Read(buf, 0, 4) == VpbNetContractReject.Truncated);

            buf[0] = 99;
            Check(log, ref pass, ref fail, "a manifest version this build does not know is refused",
                d.Read(buf, 0, VpbNetManifest.HeaderSize) == VpbNetContractReject.BadVersion);

            VpbNetManifest dup = new VpbNetManifest();
            dup.TryAdd("Creator.Pack.1", VpbNetContractRole.Look);
            dup.TryAdd("Creator.Other.1", VpbNetContractRole.Look);
            byte[] wire = new byte[VpbNetManifestLimits.MaxBytes];
            int n = dup.Write(wire);
            int second = VpbNetManifest.HeaderSize + 1 + wire[VpbNetManifest.HeaderSize] + 1;
            wire[second] = wire[VpbNetManifest.HeaderSize];
            for (int i = 0; i < wire[second]; i++)
                wire[second + 1 + i] = wire[VpbNetManifest.HeaderSize + 1 + i];
            Check(log, ref pass, ref fail, "the same package listed twice is refused",
                d.Read(wire, 0, n) == VpbNetContractReject.Duplicate);
        }

        static void ManifestFitsFragments(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "the manifest cap fits one fragment generation",
                VpbNetManifestLimits.MaxBytes <= VpbNetKeyframeAssembler.MaxKeyframeBytes);

            VpbNetManifest m = new VpbNetManifest();
            for (int i = 0; i < VpbNetManifestLimits.MaxEntries; i++)
                m.TryAdd(Repeat('a', 60) + i.ToString(System.Globalization.CultureInfo.InvariantCulture), VpbNetContractRole.Look);

            byte[] whole = new byte[VpbNetManifestLimits.MaxBytes];
            int n = m.Write(whole);
            Check(log, ref pass, ref fail, "the biggest manifest this build makes still writes", n > 0);
            Check(log, ref pass, ref fail, "and fragments inside the assembler's limit",
                VpbNetKeyframeAssembler.FragmentCount(n) <= VpbNetKeyframeAssembler.MaxFragments);

            VpbNetKeyframeAssembler asm = new VpbNetKeyframeAssembler();
            byte[] frag = new byte[VpbIpc.MaxDataPayload];
            int count = VpbNetKeyframeAssembler.FragmentCount(n);
            bool sent = true;
            for (int i = 0; i < count; i++)
            {
                int f = VpbNetKeyframeAssembler.WriteFragment(frag, whole, n, 3, i);
                if (f <= 0) { sent = false; break; }
                asm.Offer(frag, 0, f, 0.0);
            }
            Check(log, ref pass, ref fail, "every fragment writes", sent);
            Check(log, ref pass, ref fail, "and the whole thing reassembles", asm.IsComplete);

            byte[] taken = new byte[VpbNetKeyframeAssembler.MaxKeyframeBytes];
            int got = asm.Take(taken);
            VpbNetManifest d = new VpbNetManifest();
            Check(log, ref pass, ref fail, "and survives the round trip through fragments",
                got == n && d.Read(taken, 0, got) == VpbNetContractReject.None && d.Count == m.Count);
        }

        static void PlanScenePackageIsStrict(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Creator.ScenePack.2", 1);

            VpbNetManifest m = new VpbNetManifest();
            m.TryAdd("Creator.ScenePack.3", VpbNetContractRole.Scene);

            VpbNetContentPlan plan = new VpbNetContentPlan();
            plan.Build(m, cat);
            Check(log, ref pass, ref fail, "an older scene package does not satisfy a newer one",
                plan.Count == 1 && plan.Wanted(0) == "Creator.ScenePack.3");

            cat.Install("Creator.ScenePack.3", 2);
            plan.Build(m, cat);
            Check(log, ref pass, ref fail, "the exact scene package satisfies it",
                plan.NeedsNothing && plan.Present == 1);
        }

        static void PlanDepsAcceptFamily(StringBuilder log, ref int pass, ref int fail)
        {
            FakeCatalog cat = new FakeCatalog();
            cat.Install("Someone.Hair.9", 1);

            VpbNetManifest m = new VpbNetManifest();
            m.TryAdd("Someone.Hair.4", VpbNetContractRole.Look);
            m.TryAdd("Nobody.Missing.1", VpbNetContractRole.Look);

            VpbNetContentPlan plan = new VpbNetContentPlan();
            plan.Build(m, cat);

            Check(log, ref pass, ref fail, "a different version of a dependency is not downloaded again",
                plan.Count == 1 && plan.Wanted(0) == "Nobody.Missing.1");
            Check(log, ref pass, ref fail, "but the drift is counted", plan.Drifted == 1);
            Check(log, ref pass, ref fail, "and everything was looked at", plan.Checked == 2);

            plan.Clear();
            Check(log, ref pass, ref fail, "a cleared plan wants nothing",
                plan.NeedsNothing && plan.Checked == 0);
        }

        static void PlanWithNoCatalog(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetManifest m = new VpbNetManifest();
            m.TryAdd("Creator.A.1", VpbNetContractRole.Scene);
            m.TryAdd("Creator.B.1", VpbNetContractRole.Look);

            VpbNetContentPlan plan = new VpbNetContentPlan();
            plan.Build(m, null);
            Check(log, ref pass, ref fail, "with no library to ask, everything is assumed missing",
                plan.Count == 2);

            plan.Clear();
            plan.AddSeed("Creator.Seed.1", VpbNetContractRole.Scene);
            plan.AddSeed("Creator.Seed.1", VpbNetContractRole.Look);
            Check(log, ref pass, ref fail, "a seed added twice is one entry with both roles",
                plan.Count == 1
                && (plan.Role(0) & VpbNetContractRole.Scene) != 0
                && (plan.Role(0) & VpbNetContractRole.Look) != 0);
        }

        static void SizeText(StringBuilder log, ref int pass, ref int fail)
        {
            StringBuilder sb = new StringBuilder(32);

            sb.Length = 0;
            VpbNetContentStatus.AppendSize(sb, 512);
            Check(log, ref pass, ref fail, "small sizes read in KB", sb.ToString() == "512 KB");

            sb.Length = 0;
            VpbNetContentStatus.AppendSize(sb, 1536);
            Check(log, ref pass, ref fail, "megabytes get one decimal", sb.ToString() == "1.5 MB");

            sb.Length = 0;
            VpbNetContentStatus.AppendSize(sb, 1024 * 1024 * 2);
            Check(log, ref pass, ref fail, "gigabytes read in GB", sb.ToString() == "2 GB");

            sb.Length = 0;
            VpbNetContentStatus.AppendSize(sb, 0);
            Check(log, ref pass, ref fail, "zero is still a sentence", sb.ToString() == "0 KB");
        }

        static VpbNetEventWriter NewWriter()
        {
            return new VpbNetEventWriter(VpbNetEventLimits.MaxPayload + VpbNetEventCodec.HeaderSize);
        }

        static byte[] BuildOffer(uint id, string path, string pkg, string title, uint hash, int count)
        {
            VpbNetOfferInfo o = new VpbNetOfferInfo();
            o.Clear();
            o.OfferId = id;
            o.ScenePath = path;
            o.PackageUid = pkg;
            o.Title = title;
            o.PackageHash = hash;
            o.ManifestCount = count;

            VpbNetEventWriter w = NewWriter();
            w.Begin(VpbNetEventType.SceneOffer, 1);
            o.Write(w);
            int n = w.End();
            if (n <= 0) return new byte[0];

            byte[] copy = new byte[n];
            Buffer.BlockCopy(w.Buffer, 0, copy, 0, n);
            return copy;
        }

        static bool ReadOffer(byte[] wire, out VpbNetOfferInfo o)
        {
            return ReadOffer(wire, wire == null ? 0 : wire.Length, out o);
        }

        static bool ReadOffer(byte[] wire, int len, out VpbNetOfferInfo o)
        {
            o = new VpbNetOfferInfo();
            o.Clear();
            if (wire == null || len <= 0) return false;

            VpbNetEventReader r = new VpbNetEventReader();
            if (!r.Begin(wire, 0, len)) return false;
            if (r.Type != VpbNetEventType.SceneOffer) return false;
            return VpbNetOfferInfo.TryRead(r, out o);
        }

        static bool ReadStatus(byte[] wire, int len, out VpbNetContentStatus s)
        {
            s = new VpbNetContentStatus();
            s.Clear();
            if (wire == null || len <= 0) return false;

            VpbNetEventReader r = new VpbNetEventReader();
            if (!r.Begin(wire, 0, len)) return false;
            if (r.Type != VpbNetEventType.ContentState) return false;
            return VpbNetContentStatus.TryRead(r, out s);
        }

        static string Repeat(char c, int n)
        {
            return new string(c, n);
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
