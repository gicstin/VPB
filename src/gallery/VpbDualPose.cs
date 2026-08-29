using System;
using System.Collections.Generic;
using MVR.FileManagement;
using SimpleJSON;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    /// <summary>One of the two bodies a two-person pose describes.</summary>
    public sealed class VpbDualPoseRole
    {
        public int Index;
        public string AtomId;
        public JSONClass Data;
        public bool IsMale;
        public bool GenderKnown;
        public bool HaveRoot;
        public Vector3 RootPos;
        public float RootYaw;
    }

    /// <summary>Exactly two roles; Person1/Person2 wins, extras ignored.</summary>
    public sealed class VpbDualPoseFile
    {
        public string Reference;
        public string DisplayName;
        public VpbDualPoseRole A;
        public VpbDualPoseRole B;

        public VpbDualPoseRole Role(int i)
        {
            if (i == 0) return A;
            if (i == 1) return B;
            return null;
        }

        /// <summary>True when the file does not say the two bodies are different genders.</summary>
        public bool Ambiguous
        {
            get
            {
                if (A == null || B == null) return true;
                if (!A.GenderKnown || !B.GenderKnown) return true;
                return A.IsMale == B.IsMale;
            }
        }

        /// <summary>Role index of the male (or female) half, -1 when the file does not say.</summary>
        public int RoleOfGender(bool male)
        {
            if (Ambiguous) return -1;
            if (A.IsMale == male) return 0;
            if (B.IsMale == male) return 1;
            return -1;
        }
    }

    /// <summary>Load, name, apply. Raw world coords keep the pair; optional shared Anchor relocates it.</summary>
    public static class VpbDualPose
    {
        public struct Anchor
        {
            public bool Active;
            public Vector3 Pivot;
            public Vector3 Position;
            public float Yaw;

            public static Anchor None { get { return new Anchor(); } }
        }

        public static bool LooksDual(JSONClass node)
        {
            if (node == null) return false;
            if (node.HasKey("PeopleCount") && node["PeopleCount"].AsInt >= 2) return true;
            if (node.HasKey("Person2")) return true;
            return CountPersonAtoms(node["atoms"] as JSONArray) >= 2;
        }

        static int CountPersonAtoms(JSONArray atoms)
        {
            if (atoms == null) return 0;
            int n = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (IsPersonAtomNode(atoms[i] as JSONClass)) n++;
            }
            return n;
        }

        static bool IsPersonAtomNode(JSONClass a)
        {
            if (a == null || !a.HasKey("type")) return false;
            try { return SceneUtils.IsPersonLikeAtomType(a["type"].Value); }
            catch { return false; }
        }

        /// <summary>Reduce the named file to two roles. <paramref name="why"/> is player-facing on fail.</summary>
        public static VpbDualPoseFile Load(string reference, FileEntry entry, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(reference))
            {
                why = "no pose file was named";
                return null;
            }

            JSONNode node = null;
            try { node = UI.LoadJSONWithFallback(UI.NormalizePath(reference), entry); }
            catch { node = null; }
            if (node == null)
            {
                why = "that pose file could not be read";
                return null;
            }

            return Parse(reference, node.AsObject, out why);
        }

        public static VpbDualPoseFile Parse(string reference, JSONClass node, out string why)
        {
            why = null;
            if (node == null)
            {
                why = "that pose file could not be read";
                return null;
            }

            JSONArray atoms = node["atoms"] as JSONArray;
            if (atoms == null || atoms.Count == 0)
            {
                why = "that pose file does not hold whole people, so it cannot be split in two";
                return null;
            }

            JSONClass first = null;
            JSONClass second = null;

            string p1 = node.HasKey("Person1") ? node["Person1"].Value : null;
            string p2 = node.HasKey("Person2") ? node["Person2"].Value : null;
            if (!string.IsNullOrEmpty(p1) && !string.IsNullOrEmpty(p2))
            {
                first = FindAtomById(atoms, p1);
                second = FindAtomById(atoms, p2);
            }

            // Missing/stale Person1/Person2: first two people in the file.
            if (first == null || second == null)
            {
                first = null;
                second = null;
                for (int i = 0; i < atoms.Count; i++)
                {
                    JSONClass a = atoms[i] as JSONClass;
                    if (!IsPersonAtomNode(a)) continue;
                    if (first == null) first = a;
                    else if (second == null) { second = a; break; }
                }
            }

            if (first == null || second == null)
            {
                why = "that pose only describes one person";
                return null;
            }

            VpbDualPoseFile file = new VpbDualPoseFile();
            file.Reference = reference;
            file.DisplayName = LeafName(reference);
            file.A = BuildRole(0, first);
            file.B = BuildRole(1, second);
            return file;
        }

        static JSONClass FindAtomById(JSONArray atoms, string id)
        {
            for (int i = 0; i < atoms.Count; i++)
            {
                JSONClass a = atoms[i] as JSONClass;
                if (a == null || !a.HasKey("id")) continue;
                if (string.Equals(a["id"].Value, id, StringComparison.Ordinal)) return a;
            }
            return null;
        }

        static VpbDualPoseRole BuildRole(int index, JSONClass data)
        {
            VpbDualPoseRole r = new VpbDualPoseRole();
            r.Index = index;
            r.Data = data;
            r.AtomId = data.HasKey("id") ? data["id"].Value : ("Person " + (index + 1));

            bool male;
            r.GenderKnown = TryReadGender(data, r.AtomId, out male);
            r.IsMale = male;

            Vector3 pos;
            float yaw;
            r.HaveRoot = TryReadRoot(data, out pos, out yaw);
            r.RootPos = pos;
            r.RootYaw = yaw;
            return r;
        }

        // Gender from geometry only; "female" before "male" (substring).
        static bool TryReadGender(JSONClass data, string atomId, out bool male)
        {
            male = false;
            JSONArray st = data["storables"] as JSONArray;
            if (st != null)
            {
                for (int i = 0; i < st.Count; i++)
                {
                    JSONClass s = st[i] as JSONClass;
                    if (s == null || !s.HasKey("id")) continue;
                    if (!string.Equals(s["id"].Value, "geometry", StringComparison.Ordinal)) continue;
                    if (!s.HasKey("character")) break;

                    string c = s["character"].Value;
                    if (string.IsNullOrEmpty(c)) break;
                    if (c.StartsWith("Male", StringComparison.OrdinalIgnoreCase)) { male = true; return true; }
                    if (c.StartsWith("Female", StringComparison.OrdinalIgnoreCase)) { male = false; return true; }
                    break;
                }
            }

            if (string.IsNullOrEmpty(atomId)) return false;
            if (atomId.IndexOf("female", StringComparison.OrdinalIgnoreCase) >= 0) { male = false; return true; }
            if (atomId.IndexOf("woman", StringComparison.OrdinalIgnoreCase) >= 0) { male = false; return true; }
            if (atomId.IndexOf("male", StringComparison.OrdinalIgnoreCase) >= 0) { male = true; return true; }
            if (atomId.IndexOf("man", StringComparison.OrdinalIgnoreCase) >= 0) { male = true; return true; }
            return false;
        }

        static bool TryReadRoot(JSONClass data, out Vector3 pos, out float yaw)
        {
            JSONArray st = data["storables"] as JSONArray;
            if (st != null)
            {
                for (int i = 0; i < st.Count; i++)
                {
                    JSONClass s = st[i] as JSONClass;
                    if (s == null || !s.HasKey("id")) continue;
                    if (!string.Equals(s["id"].Value, "control", StringComparison.Ordinal)) continue;
                    if (TryReadPlacement(s, out pos, out yaw)) return true;
                    break;
                }
            }
            return TryReadPlacement(data, out pos, out yaw);
        }

        static bool TryReadPlacement(JSONClass c, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;
            if (c == null || !c.HasKey("position")) return false;

            JSONNode p = c["position"];
            pos = new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat);
            if (c.HasKey("rotation")) yaw = c["rotation"]["y"].AsFloat;
            return true;
        }

        /// <summary>Package uid if present, else display path.</summary>
        public static string EntryReference(FileEntry entry)
        {
            if (entry == null) return null;

            string uid = null;
            try { uid = entry.Uid; }
            catch { }
            if (!string.IsNullOrEmpty(uid)) return uid.Replace('\\', '/');

            string path = null;
            try { path = entry.Path; }
            catch { }
            return string.IsNullOrEmpty(path) ? null : path.Replace('\\', '/');
        }

        static string LeafName(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return "";
            string s = reference.Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < s.Length) s = s.Substring(slash + 1);
            int dot = s.LastIndexOf('.');
            return dot > 0 ? s.Substring(0, dot) : s;
        }

        public static string RoleLabel(VpbDualPoseFile file, int index)
        {
            VpbDualPoseRole r = file != null ? file.Role(index) : null;
            if (r == null) return "?";
            if (file.Ambiguous)
            {
                return index == 0
                    ? VPBTranslation.T("gallery.dual_pose.role_a", "Person A")
                    : VPBTranslation.T("gallery.dual_pose.role_b", "Person B");
            }
            return r.IsMale
                ? VPBTranslation.T("gallery.dual_pose.role_male", "the male")
                : VPBTranslation.T("gallery.dual_pose.role_female", "the female");
        }

        /// <summary>Shared transform: keep <paramref name="mine"/> in place, partner follows.</summary>
        public static Anchor AnchorAt(VpbDualPoseFile file, int myRole, Atom mine)
        {
            Anchor a = Anchor.None;
            VpbDualPoseRole r = file != null ? file.Role(myRole) : null;
            if (r == null || !r.HaveRoot || mine == null) return a;

            Transform t = RootTransform(mine);
            if (t == null) return a;

            a.Active = true;
            a.Pivot = r.RootPos;
            a.Position = t.position;
            a.Yaw = Mathf.DeltaAngle(r.RootYaw, t.eulerAngles.y);
            return a;
        }

        public static Transform RootTransform(Atom atom)
        {
            if (atom == null) return null;
            try
            {
                if (atom.mainController != null) return atom.mainController.transform;
            }
            catch { }
            try { return atom.transform; }
            catch { return null; }
        }

        /// <summary>Physical restore of one half; look is untouched.</summary>
        public static bool Apply(Atom atom, VpbDualPoseRole role, Anchor anchor, string why)
        {
            if (atom == null || role == null || role.Data == null) return false;

            JSONClass data = role.Data;
            if (anchor.Active) Reposition(data, anchor);

            // Jump lands colliders inside geometry; hold only while a session is ticking.
            try
            {
                if (VpbNetPresence.IsActive)
                    VpbNetCollisionGuard.Suspend(atom, VpbNetCollisionGuard.JumpFrames, why);
            }
            catch { }

            try
            {
                atom.PreRestore(true, false);
                atom.RestoreTransform(data);
                atom.Restore(data, true, false, false);
                atom.LateRestore(data, true, false, false);
                atom.PostRestore(true, false);
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB] two-person pose: could not put " + role.AtomId
                    + " on " + atom.uid + ": " + e.Message);
                return false;
            }

            LogUtil.Log("[VPB] two-person pose: " + role.AtomId + " -> " + atom.uid
                + (anchor.Active ? " (placed at " + atom.uid + "'s spot)" : " (as saved)"));
            return true;
        }

        /// <summary>Rewrite world placements; leave local pairs alone.</summary>
        static void Reposition(JSONClass data, Anchor anchor)
        {
            Quaternion yaw = Quaternion.Euler(0f, anchor.Yaw, 0f);
            RepositionOne(data, anchor, yaw);

            JSONArray st = data["storables"] as JSONArray;
            if (st == null) return;
            for (int i = 0; i < st.Count; i++) RepositionOne(st[i] as JSONClass, anchor, yaw);
        }

        static void RepositionOne(JSONClass c, Anchor anchor, Quaternion yaw)
        {
            if (c == null) return;

            if (c.HasKey("position"))
            {
                JSONNode p = c["position"];
                Vector3 v = new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat);
                v = yaw * (v - anchor.Pivot) + anchor.Position;
                p["x"].AsFloat = v.x;
                p["y"].AsFloat = v.y;
                p["z"].AsFloat = v.z;
            }

            if (c.HasKey("rotation"))
            {
                JSONNode r = c["rotation"];
                Quaternion q = yaw * Quaternion.Euler(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat);
                Vector3 e = q.eulerAngles;
                r["x"].AsFloat = e.x;
                r["y"].AsFloat = e.y;
                r["z"].AsFloat = e.z;
            }
        }

        /// <summary>Scene people in order, as role candidates.</summary>
        public static void CollectPeople(List<Atom> into)
        {
            if (into == null) return;
            into.Clear();

            SuperController sc = SuperController.singleton;
            if (sc == null) return;

            List<Atom> all = null;
            try { all = sc.GetAtoms(); }
            catch { }
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                Atom a = all[i];
                if (a == null) continue;
                bool ok = false;
                try { ok = a.on && SceneUtils.IsPersonLikeAtom(a); }
                catch { ok = false; }
                if (ok) into.Add(a);
            }
        }

        /// <summary>Gender first, then nearest; <paramref name="prefer"/> stays on a matching role.</summary>
        public static void SuggestCast(
            VpbDualPoseFile file, List<Atom> people, Atom prefer, out Atom castA, out Atom castB)
        {
            castA = null;
            castB = null;
            if (file == null || people == null || people.Count < 2) return;

            if (prefer != null && people.Contains(prefer))
            {
                int role = 0;
                if (!file.Ambiguous)
                {
                    bool male = false;
                    try { male = AtomGenderUtils.IsMale(prefer); }
                    catch { }
                    int wanted = file.RoleOfGender(male);
                    if (wanted >= 0) role = wanted;
                }
                if (role == 0) castA = prefer;
                else castB = prefer;
            }

            if (castA == null) castA = PickFor(file.A, people, castB);
            if (castB == null) castB = PickFor(file.B, people, castA);
        }

        static Atom PickFor(VpbDualPoseRole role, List<Atom> people, Atom taken)
        {
            Atom byGender = null;
            Atom nearest = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < people.Count; i++)
            {
                Atom a = people[i];
                if (a == null || a == taken) continue;

                if (byGender == null && role != null && role.GenderKnown)
                {
                    bool male = false;
                    try { male = AtomGenderUtils.IsMale(a); }
                    catch { }
                    if (male == role.IsMale) byGender = a;
                }

                if (role != null && role.HaveRoot)
                {
                    Transform t = RootTransform(a);
                    if (t != null)
                    {
                        float d = (t.position - role.RootPos).sqrMagnitude;
                        if (d < bestSq) { bestSq = d; nearest = a; }
                    }
                }
                else if (nearest == null)
                {
                    nearest = a;
                }
            }

            return byGender != null ? byGender : nearest;
        }
    }
}
