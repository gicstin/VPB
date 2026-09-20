using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB.Outliner
{
    internal enum OutlinerPoseBucket
    {
        Expressions,
        Face,
        Hands,
        Arms,
        Legs,
        Torso,
        Other,
        Count
    }

    internal sealed class OutlinerPoseMorphEntry
    {
        internal DAZMorph Morph;
        internal string Label;
        internal string Region;
        internal OutlinerPoseBucket Bucket;
        internal float Value;
        internal bool Driven;
        internal string DrivenBy;
    }

    internal static class OutlinerPoseMorphs
    {
        internal const int AllBuckets = -1;
        internal const float NeutralEpsilon = 0.01f;
        internal const string GeometryStorableId = OutlinerLookPreview.GeometryStorableId;

        static readonly Comparison<OutlinerPoseMorphEntry> ByBucketThenMagnitude = CompareEntries;

        internal static bool Supports(Atom atom)
        {
            return OutlinerLookPreview.Supports(atom);
        }

        internal static bool IsNeutral(float value)
        {
            return Mathf.Abs(value) < NeutralEpsilon;
        }

        internal static OutlinerPoseBucket BucketOf(string region)
        {
            if (string.IsNullOrEmpty(region)) return OutlinerPoseBucket.Other;
            string r = region.ToLowerInvariant();
            if (r.IndexOf("expression", StringComparison.Ordinal) >= 0) return OutlinerPoseBucket.Expressions;
            if (Has(r, "finger") || Has(r, "hand") || Has(r, "thumb") || Has(r, "wrist")) return OutlinerPoseBucket.Hands;
            if (Has(r, "eye") || Has(r, "brow") || Has(r, "mouth") || Has(r, "lip") || Has(r, "tongue")
                || Has(r, "cheek") || Has(r, "nose") || Has(r, "jaw") || Has(r, "face") || Has(r, "head")
                || Has(r, "viseme"))
                return OutlinerPoseBucket.Face;
            if (Has(r, "arm") || Has(r, "shoulder") || Has(r, "elbow")) return OutlinerPoseBucket.Arms;
            if (Has(r, "leg") || Has(r, "knee") || Has(r, "thigh") || Has(r, "foot") || Has(r, "feet")
                || Has(r, "toe") || Has(r, "ankle"))
                return OutlinerPoseBucket.Legs;
            if (Has(r, "torso") || Has(r, "chest") || Has(r, "abdomen") || Has(r, "hip") || Has(r, "pelvis")
                || Has(r, "spine") || Has(r, "neck") || Has(r, "breast") || Has(r, "glute") || Has(r, "waist")
                || Has(r, "body") || Has(r, "back"))
                return OutlinerPoseBucket.Torso;
            return OutlinerPoseBucket.Other;
        }

        internal static string BucketLabel(OutlinerPoseBucket bucket)
        {
            switch (bucket)
            {
                case OutlinerPoseBucket.Expressions: return VPBTranslation.T("outliner.pose.bucket.expressions", "Expressions");
                case OutlinerPoseBucket.Face: return VPBTranslation.T("outliner.pose.bucket.face", "Face");
                case OutlinerPoseBucket.Hands: return VPBTranslation.T("outliner.pose.bucket.hands", "Hands");
                case OutlinerPoseBucket.Arms: return VPBTranslation.T("outliner.pose.bucket.arms", "Arms");
                case OutlinerPoseBucket.Legs: return VPBTranslation.T("outliner.pose.bucket.legs", "Legs");
                case OutlinerPoseBucket.Torso: return VPBTranslation.T("outliner.pose.bucket.torso", "Torso");
                default: return VPBTranslation.T("outliner.pose.bucket.other", "Other pose");
            }
        }

        internal static string BucketIcon(OutlinerPoseBucket bucket)
        {
            switch (bucket)
            {
                case OutlinerPoseBucket.Expressions: return "masks-theater";
                case OutlinerPoseBucket.Face: return "mood-neutral";
                case OutlinerPoseBucket.Hands: return "hand-finger";
                default: return "body-scan";
            }
        }

        internal static int Collect(Atom atom, List<OutlinerPoseMorphEntry> into, int[] bucketActive, int[] bucketTotal)
        {
            into.Clear();
            if (bucketActive != null) Array.Clear(bucketActive, 0, bucketActive.Length);
            if (bucketTotal != null) Array.Clear(bucketTotal, 0, bucketTotal.Length);
            DAZCharacterSelector selector = OutlinerLookPreview.SelectorOf(atom);
            if (selector == null) return 0;
            int active = 0;
            active += CollectFrom(MorphsOf(selector, false), into, bucketActive, bucketTotal);
            active += CollectFrom(MorphsOf(selector, true), into, bucketActive, bucketTotal);
            into.Sort(ByBucketThenMagnitude);
            return active;
        }

        internal static int CollectFrom(List<DAZMorph> morphs, List<OutlinerPoseMorphEntry> into, int[] bucketActive, int[] bucketTotal)
        {
            if (morphs == null) return 0;
            int active = 0;
            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph m = morphs[i];
                if (m == null) continue;
                bool pose = false;
                try { pose = m.isPoseControl; } catch { continue; }
                if (!pose) continue;
                string region = "";
                try { region = m.resolvedRegionName ?? ""; } catch { region = ""; }
                OutlinerPoseBucket bucket = BucketOf(region);
                if (bucketTotal != null) bucketTotal[(int)bucket]++;
                float v = 0f;
                try { v = m.morphValue; } catch { continue; }
                if (IsNeutral(v)) continue;
                active++;
                if (bucketActive != null) bucketActive[(int)bucket]++;
                var e = new OutlinerPoseMorphEntry();
                e.Morph = m;
                e.Region = region;
                e.Bucket = bucket;
                e.Value = v;
                try { e.Label = m.resolvedDisplayName ?? ""; } catch { e.Label = ""; }
                try { e.Driven = m.isDriven; } catch { e.Driven = false; }
                if (e.Driven)
                {
                    try { e.DrivenBy = m.drivenBy ?? ""; } catch { e.DrivenBy = ""; }
                }
                else e.DrivenBy = "";
                into.Add(e);
            }
            return active;
        }

        internal static int ActiveSignature(Atom atom, out int activeCount)
        {
            activeCount = 0;
            DAZCharacterSelector selector = OutlinerLookPreview.SelectorOf(atom);
            if (selector == null) return 0;
            int sig = 17;
            sig = SignatureFrom(MorphsOf(selector, false), sig, ref activeCount);
            sig = SignatureFrom(MorphsOf(selector, true), sig, ref activeCount);
            return sig;
        }

        internal static int SignatureFrom(List<DAZMorph> morphs, int sig, ref int activeCount)
        {
            if (morphs == null) return sig;
            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph m = morphs[i];
                if (m == null) continue;
                bool pose = false;
                try { pose = m.isPoseControl; } catch { continue; }
                if (!pose) continue;
                float v = 0f;
                try { v = m.morphValue; } catch { continue; }
                if (IsNeutral(v)) continue;
                activeCount++;
                string uid = null;
                try { uid = m.uid; } catch { uid = null; }
                sig = sig * 31 + (uid != null ? uid.GetHashCode() : i);
            }
            return sig;
        }

        internal static int Zero(Atom atom, int bucket, OutlinerResetSnapshot snap, out int drivenSkipped)
        {
            drivenSkipped = 0;
            DAZCharacterSelector selector = OutlinerLookPreview.SelectorOf(atom);
            if (selector == null) return 0;
            int done = 0;
            done += ZeroFrom(MorphsOf(selector, false), bucket, snap, ref drivenSkipped);
            done += ZeroFrom(MorphsOf(selector, true), bucket, snap, ref drivenSkipped);
            return done;
        }

        internal static int ZeroFrom(List<DAZMorph> morphs, int bucket, OutlinerResetSnapshot snap, ref int drivenSkipped)
        {
            if (morphs == null) return 0;
            int done = 0;
            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph m = morphs[i];
                if (m == null) continue;
                bool pose = false;
                try { pose = m.isPoseControl; } catch { continue; }
                if (!pose) continue;
                float v = 0f;
                try { v = m.morphValue; } catch { continue; }
                if (v == 0f) continue;
                if (bucket != AllBuckets)
                {
                    string region = "";
                    try { region = m.resolvedRegionName ?? ""; } catch { region = ""; }
                    if ((int)BucketOf(region) != bucket) continue;
                }
                bool driven = false;
                try { driven = m.isDriven; } catch { driven = false; }
                if (driven)
                {
                    drivenSkipped++;
                    continue;
                }
                if (snap != null) snap.CaptureMorph(m);
                if (Write(m, 0f)) done++;
            }
            return done;
        }

        internal static bool Write(DAZMorph m, float value)
        {
            if (m == null) return false;
            try
            {
                m.morphValue = value;
                return true;
            }
            catch { }
            try
            {
                return m.SetValueThreadSafe(value);
            }
            catch { return false; }
        }

        internal static float Read(DAZMorph m)
        {
            if (m == null) return 0f;
            try { return m.morphValue; } catch { return 0f; }
        }

        internal static string UndoKey(Atom atom, OutlinerPoseMorphEntry e)
        {
            if (atom == null || e == null) return "";
            string uid = "";
            try { uid = atom.uid ?? ""; } catch { uid = ""; }
            return UndoKey(uid, e.Label);
        }

        internal static string UndoKey(string atomUid, string morphLabel)
        {
            if (string.IsNullOrEmpty(atomUid) || string.IsNullOrEmpty(morphLabel)) return "";
            if (morphLabel.IndexOf('|') >= 0 || atomUid.IndexOf('|') >= 0) return "";
            return atomUid + "|" + GeometryStorableId + "|" + morphLabel;
        }

        internal static string FormatValue(float value)
        {
            return value.ToString("0.00");
        }

        internal static List<DAZMorph> MorphsOf(DAZCharacterSelector selector, bool otherGender)
        {
            if (selector == null) return null;
            try
            {
                GenerateDAZMorphsControlUI ui = otherGender ? selector.morphsControlUIOtherGender : selector.morphsControlUI;
                return ui != null ? ui.GetMorphs() : null;
            }
            catch { return null; }
        }

        static int CompareEntries(OutlinerPoseMorphEntry a, OutlinerPoseMorphEntry b)
        {
            if (a == null || b == null) return a == null ? (b == null ? 0 : 1) : -1;
            int bucket = ((int)a.Bucket).CompareTo((int)b.Bucket);
            if (bucket != 0) return bucket;
            int mag = Mathf.Abs(b.Value).CompareTo(Mathf.Abs(a.Value));
            if (mag != 0) return mag;
            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        }

        static bool Has(string lower, string needle)
        {
            return lower.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }
    }
}
