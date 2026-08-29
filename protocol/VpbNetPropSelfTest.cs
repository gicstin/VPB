using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetPropSelfTest
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

            Line(log, "===== prop + atom sync self-test =====");
            Line(log, "layout: header=" + VpbNetPropLimits.HeaderSize
                + " fixed/atom=" + VpbNetPropLimits.FixedPerAtom
                + " cap=" + VpbNetPropLimits.MaxAtomsPerFrame + " atoms");

            RoundTrip(log, ref pass, ref fail);
            Precision(log, ref pass, ref fail);
            Hostile(log, ref pass, ref fail);
            Budget(log, ref pass, ref fail);
            AtomGate(log, ref pass, ref fail);
            SubSceneGate(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 transform  a prop's place round-trips within a millimetre : " + V(fail));
            Line(log, "EXIT 2/4 malformed  a bad uid, count or float is refused whole, never half-applied : " + V(fail));
            Line(log, "EXIT 3/4 atom gate  no type that executes code, and no atom that is a player : " + V(fail));
            Line(log, "EXIT 4/4 subscene   a .json under VaM's own roots, package or loose, and nothing else : " + V(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end prop + atom sync self-test =====");
            return fail == 0;
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[VpbIpc.MaxDataPayload];
            VpbNetPropFrame tx = new VpbNetPropFrame();
            VpbNetPropFrame rx = new VpbNetPropFrame();

            Check(log, ref pass, ref fail, "an empty frame is never written, so silence stays silence",
                tx.Write(buf, 1) < 0);

            tx.Add("Box#1", 1.5f, 0.25f, -3f, 0f, 0f, 0f, 1f);
            tx.Add("Creator.Pack.1:/Lamp", -12f, 2f, 40f, 0f, 0.7071068f, 0f, 0.7071068f);
            int n = tx.Write(buf, 77);
            Check(log, ref pass, ref fail, "two atoms encode into " + n + " B",
                n > 0 && n <= VpbIpc.MaxDataPayload);

            Check(log, ref pass, ref fail, "the frame decodes with both atoms and its sequence",
                rx.Read(buf, 0, n) == VpbNetPropReject.None && rx.Count == 2 && rx.Seq == 77);

            Check(log, ref pass, ref fail, "uids survive, including a package-qualified one",
                rx.Uid(0) == "Box#1" && rx.Uid(1) == "Creator.Pack.1:/Lamp");

            Check(log, ref pass, ref fail,
                "positions are exact f32, so a prop parked at a round number stays there",
                rx.PosX(0) == 1.5f && rx.PosY(0) == 0.25f && rx.PosZ(0) == -3f
                && rx.PosX(1) == -12f && rx.PosZ(1) == 40f);

            Check(log, ref pass, ref fail,
                "a prop 40 m out is carried exactly, unlike a bone, which is why props are not pose-quantized",
                rx.PosZ(1) == 40f);

            Check(log, ref pass, ref fail, "Clear empties the frame so a stale atom cannot be re-applied",
                Cleared(rx));
        }

        static bool Cleared(VpbNetPropFrame f)
        {
            f.Clear();
            return f.Count == 0 && f.Seq == 0 && f.Uid(0) == null;
        }

        static void Precision(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[VpbIpc.MaxDataPayload];
            VpbNetPropFrame tx = new VpbNetPropFrame();
            VpbNetPropFrame rx = new VpbNetPropFrame();

            Random rng = new Random(11);
            float worstMm = 0f;
            float worstDeg = 0f;

            for (int k = 0; k < 256; k++)
            {
                tx.Clear();
                float px = (float)(rng.NextDouble() * 200.0 - 100.0);
                float py = (float)(rng.NextDouble() * 20.0 - 5.0);
                float pz = (float)(rng.NextDouble() * 200.0 - 100.0);
                float qx, qy, qz, qw;
                RandomUnitQuat(rng, out qx, out qy, out qz, out qw);
                tx.Add("Prop", px, py, pz, qx, qy, qz, qw);

                int n = tx.Write(buf, (uint)k);
                if (n <= 0 || rx.Read(buf, 0, n) != VpbNetPropReject.None || rx.Count != 1)
                {
                    worstMm = 9999f;
                    break;
                }

                float dx = (px - rx.PosX(0)) * 1000f;
                float dy = (py - rx.PosY(0)) * 1000f;
                float dz = (pz - rx.PosZ(0)) * 1000f;
                float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > worstMm) worstMm = d;

                float a = QuatAngleDeg(qx, qy, qz, qw, rx.RotX(0), rx.RotY(0), rx.RotZ(0), rx.RotW(0));
                if (a > worstDeg) worstDeg = a;
            }

            Check(log, ref pass, ref fail,
                "position is bit-exact over 100 m (" + worstMm.ToString("0.000") + " mm)", worstMm == 0f);
            Check(log, ref pass, ref fail,
                "rotation shares the pose quat codec: worst " + worstDeg.ToString("0.0000") + " deg",
                worstDeg <= 0.25f);
        }

        static void Hostile(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[VpbIpc.MaxDataPayload];
            VpbNetPropFrame tx = new VpbNetPropFrame();
            VpbNetPropFrame rx = new VpbNetPropFrame();

            tx.Add("Box", 1f, 1f, 1f, 0f, 0f, 0f, 1f);
            int n = tx.Write(buf, 5);

            Check(log, ref pass, ref fail, "a truncated frame is refused",
                rx.Read(buf, 0, n - 1) != VpbNetPropReject.None && rx.Count == 0);
            Check(log, ref pass, ref fail, "trailing bytes are refused rather than ignored",
                rx.Read(buf, 0, n) == VpbNetPropReject.None
                && Read(rx, buf, n + 1) == VpbNetPropReject.Truncated);

            buf[0] = 99;
            Check(log, ref pass, ref fail, "an unknown prop protocol version is refused",
                rx.Read(buf, 0, n) == VpbNetPropReject.BadVersion);
            buf[0] = VpbNetPropLimits.ProtoVersion;

            buf[1] = (byte)(VpbNetPropLimits.MaxAtomsPerFrame + 1);
            Check(log, ref pass, ref fail, "a count past the cap is refused before any uid is read",
                rx.Read(buf, 0, n) == VpbNetPropReject.CountCap && rx.Count == 0);
            buf[1] = 1;

            Check(log, ref pass, ref fail, "a traversal uid is refused on the wire, not just on apply",
                !VpbNetPropFrame.IsSendableUid("../../evil")
                && !VpbNetPropFrame.IsSendableUid("C:/Windows")
                && !VpbNetPropFrame.IsSendableUid("evil.cslist")
                && !VpbNetPropFrame.IsSendableUid(null)
                && !VpbNetPropFrame.IsSendableUid("")
                && !VpbNetPropFrame.IsSendableUid(new string('u', VpbNetPropLimits.MaxUidChars + 1)));

            tx.Clear();
            Check(log, ref pass, ref fail, "a refused uid is never queued for sending",
                !tx.Add("../../evil", 0f, 0f, 0f, 0f, 0f, 0f, 1f) && tx.Count == 0);

            tx.Clear();
            Check(log, ref pass, ref fail, "NaN in a position is refused on read, so a prop cannot be sent to nowhere",
                WriteRawFloat(buf, tx, float.NaN) && rx.Read(buf, 0, RawLen(buf, tx)) == VpbNetPropReject.BadValue);

            tx.Clear();
            for (int i = 0; i < VpbNetPropLimits.MaxAtomsPerFrame + 2; i++)
                tx.Add("Prop" + i, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
            Check(log, ref pass, ref fail,
                "the sender stops at " + VpbNetPropLimits.MaxAtomsPerFrame + " atoms rather than overflowing a datagram",
                tx.Count == VpbNetPropLimits.MaxAtomsPerFrame);
        }

        static VpbNetPropReject Read(VpbNetPropFrame f, byte[] buf, int len)
        {
            return f.Read(buf, 0, len);
        }

        static bool WriteRawFloat(byte[] buf, VpbNetPropFrame tx, float v)
        {
            tx.Clear();
            tx.Add("Box", 1f, 1f, 1f, 0f, 0f, 0f, 1f);
            int n = tx.Write(buf, 1);
            if (n <= 0) return false;
            int at = VpbNetPropLimits.HeaderSize + 1 + 3;
            uint bits = v.Equals(float.NaN) ? 0x7FC00000u : 0x7F800000u;
            VpbIpc.WriteU32(buf, at, bits);
            return true;
        }

        static int RawLen(byte[] buf, VpbNetPropFrame tx)
        {
            return VpbNetPropLimits.HeaderSize + 1 + 3 + 16;
        }

        static void Budget(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[VpbIpc.MaxDataPayload];
            VpbNetPropFrame tx = new VpbNetPropFrame();

            string longUid = new string('u', VpbNetPropLimits.MaxUidChars);
            for (int i = 0; i < VpbNetPropLimits.MaxAtomsPerFrame; i++)
                tx.Add(longUid.Substring(0, VpbNetPropLimits.MaxUidChars - 2) + i.ToString("00"),
                    0f, 0f, 0f, 0f, 0f, 0f, 1f);
            int n = tx.Write(buf, 1);

            Check(log, ref pass, ref fail,
                "a full frame of maximum-length uids still fits one datagram (" + n + " B of "
                    + VpbIpc.MaxDataPayload + ")",
                n > 0 && n <= VpbIpc.MaxDataPayload);

            double kbps = n * 15.0 * 8.0 / 1000.0;
            Check(log, ref pass, ref fail,
                "eight props dragged at once at 15 Hz costs " + kbps.ToString("0.0")
                    + " kbit/s, and a still scene costs nothing because only changes are sent",
                kbps <= 150.0);
        }

        static void AtomGate(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "an ordinary prop type is allowed",
                VpbNetStorableWhitelist.IsAllowedAtom("Box#1", "Box")
                && VpbNetStorableWhitelist.IsAllowedAtom("Lamp", "InvisibleLight")
                && VpbNetStorableWhitelist.IsAllowedAtom("Room", "SubScene"));

            Check(log, ref pass, ref fail,
                "CustomUnityAsset is refused: an asset bundle is code, and no peer gets to run code here",
                VpbNetStorableWhitelist.CheckAtom("X", "CustomUnityAsset") == VpbNetStorableVerdict.DeniedName
                && VpbNetStorableWhitelist.CheckAtom("X", "customunityasset") == VpbNetStorableVerdict.DeniedName
                && VpbNetStorableWhitelist.IsDeniedAtomType("CustomUnityAsset"));

            Check(log, ref pass, ref fail,
                "Person is refused: avatars belong to the pose path, not to atom creation",
                VpbNetStorableWhitelist.CheckAtom("X", "Person") == VpbNetStorableVerdict.DeniedName
                && VpbNetStorableWhitelist.IsDeniedAtomType("Person"));

            Check(log, ref pass, ref fail, "a plugin-named type is refused",
                VpbNetStorableWhitelist.CheckAtom("X", "PluginBox") == VpbNetStorableVerdict.DeniedName
                && VpbNetStorableWhitelist.CheckAtom("X", "evil.cs") == VpbNetStorableVerdict.PluginReference);

            Check(log, ref pass, ref fail, "a traversal or drive letter in an atom uid is refused",
                VpbNetStorableWhitelist.CheckAtom("../../X", "Box") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtom("C:/Windows", "Box") == VpbNetStorableVerdict.BadIdentifier);

            Check(log, ref pass, ref fail, "null, empty and over-long are refused",
                VpbNetStorableWhitelist.CheckAtom(null, "Box") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtom("X", null) == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtom("", "Box") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtom("X", "") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckAtom("X",
                    new string('t', VpbNetStorableLimits.MaxStorableChars + 1)) == VpbNetStorableVerdict.Oversize);

            Check(log, ref pass, ref fail,
                "an unknown type is left to VaM's own catalog rather than guessed at here",
                VpbNetStorableWhitelist.IsAllowedAtom("X", "SomeTypeThisBuildHasNeverHeardOf")
                && !VpbNetStorableWhitelist.IsDeniedAtomType("Box"));

            Check(log, ref pass, ref fail,
                "CoreControl is refused: it IS the player's navigation rig, so syncing it drags the other person's camera",
                VpbNetStorableWhitelist.IsPlayerLocalAtomType("CoreControl")
                && VpbNetStorableWhitelist.IsDeniedAtomType("CoreControl")
                && VpbNetStorableWhitelist.CheckAtom("CoreControl", "CoreControl")
                    == VpbNetStorableVerdict.DeniedName);

            Check(log, ref pass, ref fail,
                "the other three per-player atoms every scene carries are refused too",
                VpbNetStorableWhitelist.IsDeniedAtomType("WindowCamera")
                && VpbNetStorableWhitelist.IsDeniedAtomType("VRController")
                && VpbNetStorableWhitelist.IsDeniedAtomType("PlayerNavigationPanel"));

            Check(log, ref pass, ref fail,
                "case is not a way past the player-local list",
                VpbNetStorableWhitelist.IsDeniedAtomType("corecontrol")
                && VpbNetStorableWhitelist.IsDeniedAtomType("windowcamera"));

            Check(log, ref pass, ref fail,
                "ordinary scene furniture is still movable - the list names players, not props",
                !VpbNetStorableWhitelist.IsPlayerLocalAtomType("InvisibleLight")
                && !VpbNetStorableWhitelist.IsPlayerLocalAtomType("Wall")
                && !VpbNetStorableWhitelist.IsPlayerLocalAtomType("SubScene")
                && VpbNetStorableWhitelist.IsAllowedAtom("Lamp", "InvisibleLight"));

            Check(log, ref pass, ref fail,
                "atom creation did not widen the storable door either",
                !VpbNetStorableWhitelist.IsKnownStorable("Box")
                && !VpbNetStorableWhitelist.IsKnownStorable("SubScene"));

            Check(log, ref pass, ref fail,
                "a subscene's own contents never travel as loose atoms - VaM names them"
                    + " \"<subscene>/<atom>\" at creation, before it parents them",
                VpbNetStorableWhitelist.IsSubSceneContentUid("Room/Lamp")
                && VpbNetStorableWhitelist.CheckAtom("Room/Lamp", "InvisibleLight")
                    == VpbNetStorableVerdict.DeniedName
                && VpbNetStorableWhitelist.CheckAtom("Room/Nested/Lamp", "InvisibleLight")
                    == VpbNetStorableVerdict.DeniedName);

            Check(log, ref pass, ref fail,
                "the subscene root itself is still shareable - it is the thing that carries them",
                !VpbNetStorableWhitelist.IsSubSceneContentUid("Room")
                && VpbNetStorableWhitelist.IsAllowedAtom("Room", "SubScene"));
        }

        static void SubSceneGate(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a package-qualified subscene json is allowed",
                VpbNetStorableWhitelist.IsAllowedSubSceneRef("Creator.Pack.1:/Custom/SubScene/Room.json"));

            Check(log, ref pass, ref fail,
                "a loose subscene under Custom/ is allowed - VaM stores most of them that way",
                VpbNetStorableWhitelist.IsAllowedSubSceneRef("Custom/SubScene/Anonymous/Lights/Setup.json")
                && VpbNetStorableWhitelist.IsAllowedSubSceneRef("Saves/scene/Room.json"));

            Check(log, ref pass, ref fail,
                "a reference outside VaM's own content roots is refused, package-qualified or not",
                VpbNetStorableWhitelist.CheckSubSceneRef("Elsewhere/Room.json")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSubSceneRef("Creator.Pack.1:/Elsewhere/Room.json")
                    == VpbNetStorableVerdict.BadIdentifier);

            Check(log, ref pass, ref fail, "a drive letter is refused",
                VpbNetStorableWhitelist.CheckSubSceneRef("C:/Windows/evil.json")
                    == VpbNetStorableVerdict.BadIdentifier);

            Check(log, ref pass, ref fail, "a traversal is refused",
                VpbNetStorableWhitelist.CheckSubSceneRef("Creator.Pack.1:/../../evil.json")
                    == VpbNetStorableVerdict.BadIdentifier);

            Check(log, ref pass, ref fail,
                "anything that is not a .json is refused, so a subscene reference can never name a script or a bundle",
                VpbNetStorableWhitelist.CheckSubSceneRef("Creator.Pack.1:/Custom/evil.cslist")
                    == VpbNetStorableVerdict.PluginReference
                && VpbNetStorableWhitelist.CheckSubSceneRef("Creator.Pack.1:/Custom/evil.assetbundle")
                    == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSubSceneRef("Creator.Pack.1:/Custom/evil.vac")
                    == VpbNetStorableVerdict.BadIdentifier);

            Check(log, ref pass, ref fail, "null, empty and over-long are refused",
                VpbNetStorableWhitelist.CheckSubSceneRef(null) == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSubSceneRef("") == VpbNetStorableVerdict.BadIdentifier
                && VpbNetStorableWhitelist.CheckSubSceneRef(
                    new string('r', VpbNetStorableLimits.MaxStringValueChars + 1))
                    == VpbNetStorableVerdict.Oversize);
        }

        static void RandomUnitQuat(Random rng, out float x, out float y, out float z, out float w)
        {
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble() * 2.0 * Math.PI;
            double u3 = rng.NextDouble() * 2.0 * Math.PI;
            double sq1 = Math.Sqrt(1.0 - u1);
            double sq2 = Math.Sqrt(u1);
            x = (float)(sq1 * Math.Sin(u2));
            y = (float)(sq1 * Math.Cos(u2));
            z = (float)(sq2 * Math.Sin(u3));
            w = (float)(sq2 * Math.Cos(u3));
        }

        static float QuatAngleDeg(float ax, float ay, float az, float aw, float bx, float by, float bz, float bw)
        {
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f) dot = -dot;
            if (dot > 1f) dot = 1f;
            return (float)(2.0 * Math.Acos(dot) * (180.0 / Math.PI));
        }

        static string V(int fail)
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
