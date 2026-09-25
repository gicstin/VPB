using VPB.src.util;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    internal static class AppearancePresetSuppress
    {
        // After look apply, SubScene / browse-sync can re-fire PosePresets (often empty name).
        static float _posePreserveUntil;
        static string _posePreserveAtomName;
        static List<JSONClass> _posePreserveSnap;

        public static void BeginPosePreserve(Atom person, List<JSONClass> snap, float seconds = 12f)
        {
            _posePreserveAtomName = person != null ? person.name : null;
            _posePreserveSnap = snap;
            _posePreserveUntil = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
        }

        public static bool IsPosePreserveActive(string atomName)
        {
            if (string.IsNullOrEmpty(_posePreserveAtomName)) return false;
            if (Time.realtimeSinceStartup > _posePreserveUntil) return false;
            if (string.IsNullOrEmpty(atomName)) return false;
            return string.Equals(atomName, _posePreserveAtomName, System.StringComparison.Ordinal);
        }

        public static bool ShouldSkipPosePresetAutoLoad(string atomName, string storableId, string presetName)
        {
            if (!string.Equals(storableId, "PosePresets", System.StringComparison.OrdinalIgnoreCase))
                return false;
            if (!IsPosePreserveActive(atomName)) return false;
            return string.IsNullOrEmpty(presetName);
        }

        public static int TryRestorePreservedPose(Atom person)
        {
            if (person == null || _posePreserveSnap == null || _posePreserveSnap.Count == 0) return 0;
            if (!IsPosePreserveActive(person.name)) return 0;
            return RestoreLivePoseStorables(person, _posePreserveSnap);
        }

        public static int TryRestorePreservedPoseAny()
        {
            if (string.IsNullOrEmpty(_posePreserveAtomName)) return 0;
            if (Time.realtimeSinceStartup > _posePreserveUntil) return 0;
            if (_posePreserveSnap == null || _posePreserveSnap.Count == 0) return 0;
            // Throttle — Boy1/Girl1 pose spam would otherwise RestoreFromJSON every ~1s for minutes.
            if (Time.realtimeSinceStartup - _lastRestoreAnyAt < 0.35f) return 0;
            _lastRestoreAnyAt = Time.realtimeSinceStartup;

            Atom person = null;
            try
            {
                if (SuperController.singleton != null)
                    person = SuperController.singleton.GetAtomByUid(_posePreserveAtomName);
            }
            catch { }
            if (person == null)
            {
                try
                {
                    var atoms = SuperController.singleton != null ? SuperController.singleton.GetAtoms() : null;
                    if (atoms != null)
                    {
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            Atom a = atoms[i];
                            if (a != null && string.Equals(a.name, _posePreserveAtomName, System.StringComparison.Ordinal))
                            {
                                person = a;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
            if (person == null) return 0;
            return RestoreLivePoseStorables(person, _posePreserveSnap);
        }

        static float _lastRestoreAnyAt;

        public static bool PatchScaleToTargetCurrent(JSONClass presetJson, Atom targetAtom)
        {
            if (presetJson == null || targetAtom == null) return false;
            var storables = presetJson["storables"] as JSONArray;
            if (storables == null) return false;

            var rescaleStorable = targetAtom.GetStorableByID("rescaleObject");
            if (rescaleStorable == null) return false;
            var scaleParam = rescaleStorable.GetFloatJSONParam("scale");
            if (scaleParam == null) return false;

            foreach (JSONNode n in storables)
            {
                var jc = n as JSONClass;
                if (jc != null && jc["id"] != null && jc["id"].Value == "rescaleObject")
                {
                    jc["scale"].AsFloat = scaleParam.val;
                    return true;
                }
            }
            return false;
        }

        public static List<JSONClass> CaptureLivePoseStorables(Atom person)
        {
            var list = new List<JSONClass>(64);
            if (person == null) return list;

            FreeControllerV3[] fcs = null;
            try { fcs = person.freeControllers; } catch { return list; }
            if (fcs == null) return list;

            for (int i = 0; i < fcs.Length; i++)
            {
                FreeControllerV3 fc = fcs[i];
                if (fc == null) continue;
                try
                {
                    JSONClass jc = fc.GetJSON();
                    if (jc == null) continue;
                    if (jc["id"] == null || string.IsNullOrEmpty(jc["id"].Value))
                        jc["id"] = fc.name;
                    list.Add(jc);
                }
                catch { }
            }
            return list;
        }

        public static int RestoreLivePoseStorables(Atom person, List<JSONClass> snap)
        {
            if (person == null || snap == null || snap.Count == 0) return 0;
            int ok = 0;
            for (int i = 0; i < snap.Count; i++)
            {
                JSONClass jc = snap[i];
                if (jc == null || jc["id"] == null) continue;
                string id = jc["id"].Value;
                if (string.IsNullOrEmpty(id)) continue;
                try
                {
                    JSONStorable st = person.GetStorableByID(id);
                    if (st == null) continue;
                    st.RestoreFromJSON(jc);
                    ok++;
                }
                catch { }
            }
            return ok;
        }

        public static int StripPoseStorables(JSONClass presetJson)
        {
            if (presetJson == null) return 0;
            JSONArray storables = presetJson["storables"] as JSONArray;
            if (storables == null) return 0;
            int removed = 0;
            for (int i = storables.Count - 1; i >= 0; i--)
            {
                JSONClass s = storables[i] as JSONClass;
                if (s == null || s["id"] == null) continue;
                if (!IsPoseDrivingStorableId(s["id"].Value)) continue;
                storables.Remove(i);
                removed++;
            }
            ClearAppearanceIncludePoseFlags(presetJson);
            return removed;
        }

        public static void ClearAppearanceIncludePoseFlags(JSONClass presetJson)
        {
            if (presetJson == null) return;
            JSONArray storables = presetJson["storables"] as JSONArray;
            if (storables == null) return;
            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i] as JSONClass;
                if (s == null || s["id"] == null) continue;
                if (!string.Equals(s["id"].Value, "AppearancePresets", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (s.HasKey("includePhysical")) s["includePhysical"] = new JSONData(false);
                if (s.HasKey("includePose")) s["includePose"] = new JSONData(false);
                break;
            }
        }

        public static bool IsPoseDrivingStorableId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (string.Equals(id, "PosePresets", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(id, "control", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (id.EndsWith("WrapControl", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (id.EndsWith("Control", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(id, "EyelidControl", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(id, "EyeTargetControl", System.StringComparison.OrdinalIgnoreCase)
                && id.IndexOf("Breast", System.StringComparison.OrdinalIgnoreCase) < 0
                && id.IndexOf("Glute", System.StringComparison.OrdinalIgnoreCase) < 0
                && id.IndexOf("Anatomy", System.StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            return false;
        }

        /// <summary>Light deferred re-apply (hooks handle SubScene pose spam).</summary>
        public static IEnumerator RestoreLivePoseDeferred(Atom person, List<JSONClass> snap, int frames = 3)
        {
            if (person == null || snap == null || snap.Count == 0) yield break;
            int max = frames > 0 ? frames : 3;
            for (int f = 0; f < max; f++)
            {
                yield return null;
                if (person == null) yield break;
                int n = RestoreLivePoseStorables(person, snap);
                if (f == 0 || f == max - 1)
                {
                    try
                    {
                        if (VPBLogger.Verbose || Settings.Instance?.LogVerboseUi?.Value == true) LogUtil.Log("[VPB] Appearance: restored live pose controllers=" + n
                            + " frame=" + f + "/" + (max - 1));
                    }
                    catch { }
                }
            }
        }
    }
}
