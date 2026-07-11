using System;
using System.Collections.Generic;
using System.Globalization;
using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    // Diagnostics for the scene-import pose path. VpbImport captures the source pose JSON + target uid right
    // before it hands the pose to VaM's native PosePresets PresetManager; Ctrl+Shift+P then dumps, side by side,
    // what the SOURCE pose specified for the foot/toe/hand/root controls vs the LIVE applied controller states
    // and toe/foot bone angles. Purpose: find why toes curl up / hands drift after an appearance+pose import.
    public static class PoseImportDiagnostics
    {
        public static JSONClass LastSourcePose;   // the Person atom node handed to the native pose loader
        public static string LastTargetUid;
        public static float LastCapturedTime;

        // Controls we care about for the toe/hand fidelity bug. Substrings are matched case-insensitively so
        // variants (lToeControl, rToeControl, lFootControl, ...) are all captured.
        private static readonly string[] InterestingControlSubstrings =
            { "toe", "foot", "hand", "hip", "pelvis" };
        private static readonly string[] InterestingRootControls = { "control" };

        // Bones whose local angle reveals a curled toe / mis-angled foot.
        private static readonly string[] InterestingBoneSubstrings = { "toe", "foot" };

        public static void CaptureSource(JSONClass poseNode, string targetUid)
        {
            try
            {
                LastSourcePose = poseNode;
                LastTargetUid = targetUid;
                LastCapturedTime = Time.realtimeSinceStartup;
                LogUtil.Log($"[VPB][POSE] captured source pose for target '{targetUid}' "
                    + $"(storables={CountStorables(poseNode)}).");
            }
            catch (Exception ex) { LogUtil.LogWarning("[VPB][POSE] CaptureSource failed: " + ex.Message); }
        }

        public static void Dump(string tag)
        {
            SuperController sc = SuperController.singleton;
            if (sc == null) { LogUtil.Log($"[VPB][POSE][dump:{tag}] no SuperController."); return; }

            Atom person = ResolveTargetPerson(sc);
            if (person == null) { LogUtil.Log($"[VPB][POSE][dump:{tag}] no Person atom found (target uid '{LastTargetUid}')."); return; }

            LogUtil.Log($"[VPB][POSE][dump:{tag}] person='{person.uid}' sourcePose={(LastSourcePose != null ? "present" : "NONE")} "
                + $"capturedAgo={(LastSourcePose != null ? (Time.realtimeSinceStartup - LastCapturedTime).ToString("F1") + "s" : "-")}.");

            DumpSourceStructure(tag);
            DumpControls(tag, person);
            DumpBones(tag, person);
        }

        // Ground-truth dump of the source pose preset so we can see EXACTLY what native VaM was handed:
        // the top-level keys, the full list of storable ids, and the raw serialized JSON of each interesting
        // control storable (so we can confirm whether e.g. rFootControl carries positionState=On or omits it).
        private static void DumpSourceStructure(string tag)
        {
            if (LastSourcePose == null) { LogUtil.Log($"[VPB][POSE][dump:{tag}] SRC (no captured source pose)."); return; }

            System.Text.StringBuilder topKeys = new System.Text.StringBuilder();
            foreach (string k in LastSourcePose.Keys) { if (topKeys.Length > 0) topKeys.Append(","); topKeys.Append(k); }

            JSONArray storables = LastSourcePose["storables"] != null ? LastSourcePose["storables"].AsArray : null;
            int count = storables != null ? storables.Count : 0;
            LogUtil.Log($"[VPB][POSE][dump:{tag}] SRC topKeys=[{topKeys}] storableCount={count}");

            if (storables == null) return;

            System.Text.StringBuilder ids = new System.Text.StringBuilder();
            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i] as JSONClass;
                string id = s != null ? s.GetId() : null;
                if (ids.Length > 0) ids.Append(",");
                ids.Append(id ?? "(null)");
            }
            LogUtil.Log($"[VPB][POSE][dump:{tag}] SRC storableIds=[{ids}]");

            // Raw JSON of each interesting control storable — the definitive answer on what state the pose carries.
            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i] as JSONClass;
                if (s == null) continue;
                string id = s.GetId();
                if (!IsInterestingControl(id)) continue;
                LogUtil.Log($"[VPB][POSE][dump:{tag}] SRC RAW '{id}' = {JsonSerializationUtil.Serialize(s, 256)}");
            }
        }

        private static void DumpControls(string tag, Atom person)
        {
            FreeControllerV3[] fcs = person.freeControllers;
            if (fcs == null) { LogUtil.Log($"[VPB][POSE][dump:{tag}] no freeControllers."); return; }

            int n = 0;
            for (int i = 0; i < fcs.Length; i++)
            {
                FreeControllerV3 fc = fcs[i];
                if (fc == null) continue;
                string name = fc.name;
                if (!IsInterestingControl(name)) continue;
                n++;

                Transform t = fc.transform;
                Vector3 localEul = t.localRotation.eulerAngles;
                string linkName = fc.linkToRB != null ? fc.linkToRB.name : "(none)";

                LogUtil.Log($"[VPB][POSE][dump:{tag}] CTRL '{name}' live: posState={fc.currentPositionState} "
                    + $"rotState={fc.currentRotationState} link={linkName} "
                    + $"worldPos={t.position.ToString("F4")} localEul={localEul.ToString("F2")}");

                JSONClass src = LastSourcePose != null ? LastSourcePose.GetStorable(name) : null;
                if (src == null)
                {
                    LogUtil.Log($"[VPB][POSE][dump:{tag}] CTRL '{name}' src: (ABSENT from source pose)");
                }
                else
                {
                    LogUtil.Log($"[VPB][POSE][dump:{tag}] CTRL '{name}' src: posState={Str(src, "positionState")} "
                        + $"rotState={Str(src, "rotationState")} linkTo={Str(src, "linkTo")} "
                        + $"pos={VecStr(src, "position")} rot={VecStr(src, "rotation")}");
                }
            }
            if (n == 0) LogUtil.Log($"[VPB][POSE][dump:{tag}] no foot/toe/hand/root controls matched.");
        }

        private static void DumpBones(string tag, Atom person)
        {
            DAZBone[] bones = person.GetComponentsInChildren<DAZBone>();
            if (bones == null) return;
            for (int i = 0; i < bones.Length; i++)
            {
                DAZBone b = bones[i];
                if (b == null) continue;
                string name = b.name;
                if (!ContainsAny(name, InterestingBoneSubstrings)) continue;
                Transform t = b.transform;
                LogUtil.Log($"[VPB][POSE][dump:{tag}] BONE '{name}' localEul={t.localRotation.eulerAngles.ToString("F2")} "
                    + $"worldPos={t.position.ToString("F4")}");
            }
        }

        private static Atom ResolveTargetPerson(SuperController sc)
        {
            if (!string.IsNullOrEmpty(LastTargetUid))
            {
                Atom a = sc.GetAtomByUid(LastTargetUid);
                if (a != null && a.type == "Person") return a;
            }
            foreach (Atom a in sc.GetAtoms())
                if (a != null && a.type == "Person") return a;
            return null;
        }

        private static bool IsInterestingControl(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < InterestingRootControls.Length; i++)
                if (string.Equals(name, InterestingRootControls[i], StringComparison.OrdinalIgnoreCase)) return true;
            return ContainsAny(name, InterestingControlSubstrings);
        }

        private static bool ContainsAny(string name, string[] subs)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < subs.Length; i++)
                if (name.IndexOf(subs[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static int CountStorables(JSONClass node)
        {
            JSONArray s = node != null && node["storables"] != null ? node["storables"].AsArray : null;
            return s != null ? s.Count : 0;
        }

        private static string Str(JSONClass o, string key) => o != null && o.HasKey(key) ? o[key].Value : "-";

        private static string VecStr(JSONClass o, string key)
        {
            JSONClass v = o != null && o.HasKey(key) ? o[key].AsObject : null;
            if (v == null) return "-";
            return "(" + Str(v, "x") + "," + Str(v, "y") + "," + Str(v, "z") + ")";
        }
    }
}
