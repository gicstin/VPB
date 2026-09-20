using System;
using System.Collections.Generic;

namespace VPB.Outliner
{
    internal static class OutlinerInspectorSections
    {
        internal const string All = "";
        internal const string Transform = "transform";
        internal const string Look = "look";
        internal const string Pose = "pose";
        internal const string Favourites = "favourites";
        internal const string Light = "Light";
        internal const string Control = "control";
        internal const string Geometry = "geometry";
        internal const string Plugins = "plugins";
        internal const string Hair = "hair";
        internal const string Audio = "AudioSource";
        internal const string Camera = "CameraControl";
        internal const string Asset = "asset";
        internal const string Other = "other";
        internal const int MaxVisibleChips = 12;
        internal const int MaxNavLines = 2;

        static readonly string[] PreferredOrder =
        {
            Transform, Look, Pose, Favourites, Plugins, Light, Camera, Audio, Asset,
            Control, Geometry, Hair, Other
        };

        internal static string KeyForCard(string cardId, string atomType)
        {
            if (string.IsNullOrEmpty(cardId)) return All;
            if (string.Equals(cardId, Transform, StringComparison.Ordinal)
                || string.Equals(cardId, "st:scale", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cardId, "st:rescaleObject", StringComparison.OrdinalIgnoreCase))
                return Transform;
            if (string.Equals(cardId, Look, StringComparison.Ordinal)) return Look;
            if (string.Equals(cardId, Pose, StringComparison.Ordinal)) return Pose;
            if (string.Equals(cardId, Favourites, StringComparison.Ordinal)) return Favourites;
            if (StartsOrdinal(cardId, "st:"))
                return KeyForStorable(cardId.Substring(3));
            if (IsNamedKey(cardId)) return cardId;
            return Other;
        }

        internal static string KeyForStorable(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return All;
            if (EqualsOrdinal(storableId, "scale") || EqualsOrdinal(storableId, "rescaleObject"))
                return Transform;
            if (EqualsOrdinal(storableId, "Light")) return Light;
            if (EqualsOrdinal(storableId, "AudioSource")) return Audio;
            if (EqualsOrdinal(storableId, "CameraControl")) return Camera;
            if (EqualsOrdinal(storableId, "control") || EqualsOrdinal(storableId, "atom")) return Control;
            if (EqualsOrdinal(storableId, "geometry")) return Geometry;
            if (EqualsOrdinal(storableId, "hair") || StartsOrdinal(storableId, "hair:")) return Hair;
            if (EqualsOrdinal(storableId, "PluginManager") || StartsOrdinal(storableId, "plugin"))
                return Plugins;
            if (EqualsOrdinal(storableId, "asset") || EqualsOrdinal(storableId, "CustomUnityAsset"))
                return Asset;
            return Other;
        }

        internal static bool CardMatches(string filter, string cardId, string atomType)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return string.Equals(filter, KeyForCard(cardId, atomType), StringComparison.Ordinal);
        }

        internal static string Icon(string key)
        {
            if (string.IsNullOrEmpty(key)) return "select-all";
            if (key == Transform) return "axis_depth";
            if (key == Look) return "shirt";
            if (key == Pose) return "mood-neutral";
            if (key == Favourites) return "star";
            if (key == Light) return "bulb";
            if (key == Camera) return "camera";
            if (key == Audio) return "volume";
            if (key == Asset) return "box";
            if (key == Control) return "settings";
            if (key == Geometry) return "body-scan";
            if (key == Hair) return "wash-gentle";
            if (key == Plugins) return "plug-connected";
            return "apps";
        }

        internal static string Label(string key)
        {
            if (string.IsNullOrEmpty(key)) return "All options";
            if (key == Transform) return "Transform";
            if (key == Look) return "Worn items";
            if (key == Pose) return "Pose morphs";
            if (key == Favourites) return "Favourites";
            if (key == Light) return "Light";
            if (key == Camera) return "Camera";
            if (key == Audio) return "Sound";
            if (key == Asset) return "Asset";
            if (key == Control) return "Control";
            if (key == Geometry) return "Geometry";
            if (key == Hair) return "Hair";
            if (key == Plugins) return "Plugins";
            if (key == Other) return "Other";
            return key;
        }

        internal static bool IsNamedKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            return PreferredIndex(key) < PreferredOrder.Length;
        }

        internal static void SortKeys(List<string> keys)
        {
            if (keys == null || keys.Count < 2) return;
            keys.Sort(CompareKeys);
        }

        internal static void ClampVisible(List<string> keys)
        {
            if (keys == null || keys.Count <= MaxVisibleChips) return;
            bool spill = false;
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;
                int idx = PreferredIndex(key);
                if (idx < PreferredOrder.Length && key != Other) continue;
                keys.RemoveAt(i);
                spill = true;
            }
            if (spill) EnsureOther(keys);
            if (keys.Count <= MaxVisibleChips) return;
            int keepNamed = MaxVisibleChips - 1;
            if (keepNamed < 1) keepNamed = 1;
            int named = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) continue;
                if (string.Equals(keys[i], Other, StringComparison.Ordinal)) continue;
                named++;
                if (named <= keepNamed) continue;
                keys.RemoveAt(i);
                i--;
            }
            EnsureOther(keys);
        }

        static void EnsureOther(List<string> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], Other, StringComparison.Ordinal)) return;
            }
            keys.Add(Other);
        }

        static int CompareKeys(string a, string b)
        {
            int ia = PreferredIndex(a);
            int ib = PreferredIndex(b);
            if (ia != ib) return ia.CompareTo(ib);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static int PreferredIndex(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < PreferredOrder.Length; i++)
            {
                if (string.Equals(PreferredOrder[i], key, StringComparison.Ordinal)) return i;
            }
            return PreferredOrder.Length + 1;
        }

        static bool EqualsOrdinal(string value, string other)
        {
            return string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
        }

        static bool StartsOrdinal(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
