using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Spawns a MeshedVR-default-equivalent 3-point <c>InvisibleLight</c> rig relative to a Person.
    /// Warm/cold path only (user-initiated spawn) — not for Update.
    /// Values taken from VaM <c>Saves/scene/MeshedVR/default.json</c> LightBack / LightFrontLeft / LightFrontRight.
    /// </summary>
    public static class VpbThreePointLightSetup
    {
        public const string KeyUid = "VPB_KeyLight";
        public const string FillUid = "VPB_FillLight";
        public const string RimUid = "VPB_RimLight";

        // Local offsets from MeshedVR default (Person at origin).
        private static readonly Vector3 KeyLocal = new Vector3(-0.5187473f, 1.361399f, 0.6727902f);
        private static readonly Vector3 FillLocal = new Vector3(0.29f, 1.833f, 0.6092629f);
        private static readonly Vector3 RimLocal = new Vector3(0.499617f, 1.349904f, -0.7464932f);

        public static bool SceneAlreadyHasLights(SuperController sc)
        {
            if (sc == null) return false;
            List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { return false; }
            if (atoms == null) return false;

            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                string t = null;
                try { t = a.type; } catch { continue; }
                if (string.IsNullOrEmpty(t)) continue;
                if (string.Equals(t, "InvisibleLight", StringComparison.Ordinal)) return true;
                if (string.Equals(t, "Light", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Spawn key / fill / rim when scene has no lights. Clears and fills <paramref name="outUids"/> with live UIDs.
        /// </summary>
        public static IEnumerator SpawnAroundPerson(Atom person, List<string> outUids)
        {
            if (outUids != null) outUids.Clear();

            SuperController sc = SuperController.singleton;
            if (sc == null || person == null) yield break;
            if (SceneAlreadyHasLights(sc)) yield break;

            Transform root = null;
            try
            {
                root = person.mainController != null ? person.mainController.transform : person.transform;
            }
            catch { root = null; }
            if (root == null) yield break;

            // Sequential AddAtomByType — avoid parallel atom-load races on Unity 2018 main thread.
            yield return SpawnOne(
                sc, root, KeyUid, KeyLocal,
                intensity: 1.27175f, range: 7f, spotAngle: 110f, lightType: "Spot",
                hue: 0.6584297f, sat: 0.22f, val: 1f, forcePixel: false, outUids);

            yield return SpawnOne(
                sc, root, FillUid, FillLocal,
                intensity: 0.920134f, range: -1f, spotAngle: -1f, lightType: "Point",
                hue: 0.086f, sat: 0.22f, val: 1f, forcePixel: false, outUids);

            yield return SpawnOne(
                sc, root, RimUid, RimLocal,
                intensity: 2.650301f, range: -1f, spotAngle: -1f, lightType: "Point",
                hue: 0.086f, sat: 0.22f, val: 1f, forcePixel: true, outUids);
        }

        private static IEnumerator SpawnOne(
            SuperController sc,
            Transform personRoot,
            string preferredUid,
            Vector3 localPos,
            float intensity,
            float range,
            float spotAngle,
            string lightType,
            float hue,
            float sat,
            float val,
            bool forcePixel,
            List<string> outUids)
        {
            if (sc == null || personRoot == null || string.IsNullOrEmpty(preferredUid)) yield break;

            try
            {
                Atom existing = sc.GetAtomByUid(preferredUid);
                if (existing != null)
                {
                    PlaceAndConfigure(existing, personRoot, localPos, intensity, range, spotAngle, lightType, hue, sat, val, forcePixel);
                    if (outUids != null) outUids.Add(preferredUid);
                    yield break;
                }
            }
            catch { }

            HashSet<string> before = SnapshotUids(sc);

            IEnumerator addIe = null;
            try { addIe = sc.AddAtomByType("InvisibleLight", preferredUid, false, false, false); }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] 3-point light spawn failed for '" + preferredUid + "': " + ex.Message);
                yield break;
            }

            while (addIe != null && addIe.MoveNext())
                yield return addIe.Current;

            Atom atom = null;
            try { atom = sc.GetAtomByUid(preferredUid); } catch { atom = null; }
            if (atom == null)
                atom = FindNewAtomByType(sc, "InvisibleLight", before);

            if (atom == null)
            {
                LogUtil.LogWarning("[VPB] 3-point light spawn: InvisibleLight '" + preferredUid + "' not found after AddAtomByType.");
                yield break;
            }

            PlaceAndConfigure(atom, personRoot, localPos, intensity, range, spotAngle, lightType, hue, sat, val, forcePixel);

            if (outUids != null)
            {
                string uid = null;
                try { uid = atom.uid; } catch { }
                if (!string.IsNullOrEmpty(uid)) outUids.Add(uid);
            }
        }

        private static void PlaceAndConfigure(
            Atom atom,
            Transform personRoot,
            Vector3 localPos,
            float intensity,
            float range,
            float spotAngle,
            string lightType,
            float hue,
            float sat,
            float val,
            bool forcePixel)
        {
            if (atom == null || personRoot == null) return;

            Vector3 worldPos = personRoot.TransformPoint(localPos);
            Vector3 aim = personRoot.position + personRoot.up * 1.2f;

            try
            {
                if (atom.mainController != null)
                {
                    atom.mainController.transform.position = worldPos;
                    Vector3 dir = aim - worldPos;
                    if (dir.sqrMagnitude > 0.0001f)
                        atom.mainController.transform.rotation = Quaternion.LookRotation(dir.normalized, personRoot.up);
                }
                else if (atom.transform != null)
                {
                    atom.transform.position = worldPos;
                }
            }
            catch { }

            try { atom.SetOn(true); } catch { }

            JSONStorable light = null;
            try { light = atom.GetStorableByID("Light"); } catch { light = null; }
            if (light == null) return;

            try
            {
                var typeCho = light.GetStringChooserJSONParam("type");
                if (typeCho != null && !string.IsNullOrEmpty(lightType))
                    typeCho.val = lightType;
            }
            catch { }

            WriteFloat(light, "intensity", intensity);
            if (range > 0f) WriteFloat(light, "range", range);
            if (spotAngle > 0f) WriteFloat(light, "spotAngle", spotAngle);
            WriteFloat(light, "shadowStrength", 0.1f);
            WriteFloat(light, "pointBias", 0f);

            try
            {
                var color = light.GetColorJSONParam("color");
                if (color != null)
                {
                    HSVColor hsv = new HSVColor();
                    hsv.H = hue;
                    hsv.S = sat;
                    hsv.V = val;
                    color.val = hsv;
                }
            }
            catch { }

            if (forcePixel)
            {
                try
                {
                    var render = light.GetStringChooserJSONParam("renderType");
                    if (render != null) render.val = "ForcePixel";
                }
                catch { }
            }

            // InvisibleLight defaults often leave halo/dust on — quiet for appearance preview.
            WriteBool(light, "showHalo", false);
            WriteBool(light, "showDust", false);
            WriteBool(light, "on", true);
        }

        private static void WriteFloat(JSONStorable storable, string param, float value)
        {
            if (storable == null) return;
            try
            {
                var f = storable.GetFloatJSONParam(param);
                if (f != null) f.val = value;
            }
            catch { }
            try { storable.SetFloatParamValue(param, value); } catch { }
        }

        private static void WriteBool(JSONStorable storable, string param, bool value)
        {
            if (storable == null) return;
            try
            {
                var b = storable.GetBoolJSONParam(param);
                if (b != null) b.val = value;
            }
            catch { }
        }

        private static HashSet<string> SnapshotUids(SuperController sc)
        {
            var set = new HashSet<string>();
            if (sc == null) return set;
            List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { return set; }
            if (atoms == null) return set;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                try
                {
                    string uid = a.uid;
                    if (!string.IsNullOrEmpty(uid)) set.Add(uid);
                }
                catch { }
            }
            return set;
        }

        private static Atom FindNewAtomByType(SuperController sc, string type, HashSet<string> before)
        {
            if (sc == null || string.IsNullOrEmpty(type)) return null;
            List<Atom> atoms = null;
            try { atoms = sc.GetAtoms(); } catch { return null; }
            if (atoms == null) return null;

            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null) continue;
                try
                {
                    if (!string.Equals(a.type, type, StringComparison.Ordinal)) continue;
                    string uid = a.uid;
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (before != null && before.Contains(uid)) continue;
                    return a;
                }
                catch { }
            }
            return null;
        }

        public static void RemoveByUids(IList<string> uids)
        {
            if (uids == null || uids.Count == 0) return;
            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            for (int i = 0; i < uids.Count; i++)
            {
                string uid = uids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                try
                {
                    Atom a = sc.GetAtomByUid(uid);
                    if (a != null) sc.RemoveAtom(a);
                }
                catch { }
            }
        }
    }
}
