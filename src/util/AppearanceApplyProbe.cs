using System;
using System.Diagnostics;
using System.Text;
using SimpleJSON;
using UnityEngine;

namespace VPB.src.util
{
    /// <summary>
    /// Warm-path apply diagnostics for Appearance / person-preset routing.
    /// Not for per-frame use — string format OK here (Unity GC: temporary allocs on cold/warm only).
    /// </summary>
    internal static class AppearanceApplyProbe
    {
        const string Prefix = "[VPB.AppApply]";

        static Stopwatch _sw;
        static string _tag;
        static long _lastMs;
        static int _phase;

        public static void Begin(string tag, string detail = null)
        {
            _tag = tag ?? "?";
            _phase = 0;
            _lastMs = 0;
            if (_sw == null) _sw = new Stopwatch();
            _sw.Reset();
            _sw.Start();
            try
            {
                LogUtil.Log(Prefix + " BEGIN tag=" + _tag
                    + " frame=" + Time.frameCount
                    + " t=" + Time.realtimeSinceStartup.ToString("F3")
                    + (string.IsNullOrEmpty(detail) ? "" : (" | " + detail)));
            }
            catch { }
        }

        public static void Phase(string name, string detail = null)
        {
            if (_sw == null || !_sw.IsRunning) return;
            _phase++;
            long now = _sw.ElapsedMilliseconds;
            long delta = now - _lastMs;
            _lastMs = now;
            try
            {
                LogUtil.Log(Prefix + " P" + _phase + " +" + delta + "ms (total=" + now + "ms) "
                    + name
                    + (string.IsNullOrEmpty(detail) ? "" : (" | " + detail)));
            }
            catch { }
        }

        public static void End(string result, string detail = null)
        {
            long total = 0;
            if (_sw != null && _sw.IsRunning)
            {
                _sw.Stop();
                total = _sw.ElapsedMilliseconds;
            }
            try
            {
                LogUtil.Log(Prefix + " END tag=" + (_tag ?? "?")
                    + " result=" + (result ?? "?")
                    + " totalMs=" + total
                    + " phases=" + _phase
                    + (string.IsNullOrEmpty(detail) ? "" : (" | " + detail)));
            }
            catch { }
            _tag = null;
        }

        public static void Warn(string msg)
        {
            try { LogUtil.LogWarning(Prefix + " " + msg); } catch { }
        }

        public static void Fail(string msg, Exception ex = null)
        {
            try
            {
                if (ex != null)
                    LogUtil.LogError(Prefix + " FAIL " + msg + ": " + ex.Message + "\n" + ex.StackTrace);
                else
                    LogUtil.LogError(Prefix + " FAIL " + msg);
            }
            catch { }
        }

        /// <summary>One-line route decision for Actions / drag apply.</summary>
        public static void Route(
            string category,
            string path,
            string itemType,
            string chosenAction,
            bool pathAppearance,
            bool pathPose,
            bool pathSkin,
            bool pathBreast,
            bool pathGlute,
            bool pathMorphs,
            bool pathHair,
            bool pathClothing,
            string targetUid)
        {
            try
            {
                var sb = new StringBuilder(256);
                sb.Append(Prefix).Append(" ROUTE cat='").Append(category ?? "")
                    .Append("' action=").Append(chosenAction ?? "?")
                    .Append(" itemType=").Append(itemType ?? "?")
                    .Append(" target=").Append(string.IsNullOrEmpty(targetUid) ? "(none)" : targetUid)
                    .Append(" flags[app=").Append(pathAppearance ? 1 : 0)
                    .Append(" pose=").Append(pathPose ? 1 : 0)
                    .Append(" skin=").Append(pathSkin ? 1 : 0)
                    .Append(" breast=").Append(pathBreast ? 1 : 0)
                    .Append(" glute=").Append(pathGlute ? 1 : 0)
                    .Append(" morph=").Append(pathMorphs ? 1 : 0)
                    .Append(" hair=").Append(pathHair ? 1 : 0)
                    .Append(" cloth=").Append(pathClothing ? 1 : 0)
                    .Append("] path=").Append(path ?? "");
                LogUtil.Log(sb.ToString());

                // Category vs path mismatch — common "appearance click did nothing useful" case.
                string cat = category ?? "";
                if (cat.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                    && !pathAppearance
                    && (pathBreast || pathSkin || pathGlute || pathMorphs || pathPose || pathHair || pathClothing))
                {
                    Warn("Category is Appearance but path is not /Appearance/ — applying as "
                        + chosenAction + ". Prefer remapping to sibling Appearance preset if present.");
                }
            }
            catch { }
        }

        /// <summary>
        /// If user is in Appearance category but FileEntry points at BreastPhysics/Skin/Morphs/etc
        /// with the same Preset_*.vap name, prefer the package's Appearance sibling.
        /// </summary>
        public static FileEntry TryRemapToAppearanceSibling(FileEntry entry, string category)
        {
            if (entry == null) return null;
            if (string.IsNullOrEmpty(category)
                || category.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            string uid = entry.Uid ?? entry.Path ?? "";
            string norm = uid.Replace('\\', '/');
            string lower = norm.ToLowerInvariant();
            if (lower.IndexOf("/appearance/", StringComparison.Ordinal) >= 0)
                return null;

            string[] altFolders = new[]
            {
                "/BreastPhysics/", "/Skin/", "/Morphs/", "/Hair/", "/Clothing/",
                "/Pose/", "/GlutePhysics/", "/General/"
            };
            int folderAt = -1;
            string matched = null;
            for (int i = 0; i < altFolders.Length; i++)
            {
                int idx = IndexOfOrdinalIgnoreCase(norm, altFolders[i]);
                if (idx >= 0)
                {
                    folderAt = idx;
                    matched = altFolders[i];
                    break;
                }
            }
            if (folderAt < 0 || matched == null) return null;

            string candidate = norm.Substring(0, folderAt) + "/Appearance/" + norm.Substring(folderAt + matched.Length);
            try
            {
                FileEntry sibling = FileManager.GetFileEntry(candidate);
                if (sibling == null)
                {
                    // Try with common package prefix from original uid.
                    int colon = norm.IndexOf(":/", StringComparison.Ordinal);
                    if (colon > 0)
                    {
                        string pkg = norm.Substring(0, colon);
                        string fileName = norm.Substring(folderAt + matched.Length);
                        string internalPath = "Custom/Atom/Person/Appearance/" + fileName;
                        sibling = FileManager.GetFileEntry(pkg + ":/" + internalPath);
                    }
                }
                if (sibling != null)
                {
                    LogUtil.Log(Prefix + " REMAP " + matched.Trim('/') + " -> Appearance | from="
                        + uid + " | to=" + (sibling.Uid ?? sibling.Path));
                    return sibling;
                }
                Warn("No Appearance sibling for " + uid + " (tried " + candidate + ")");
            }
            catch (Exception ex)
            {
                Warn("Remap failed: " + ex.Message);
            }
            return null;
        }

        static int IndexOfOrdinalIgnoreCase(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle)) return -1;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        public static string SummarizePreset(JSONClass preset)
        {
            if (preset == null) return "preset=null";
            var storables = preset["storables"] as SimpleJSON.JSONArray;
            int n = storables != null ? storables.Count : 0;
            int poseDrive = 0;
            int clothing = 0;
            int hair = 0;
            int morph = 0;
            int geometry = 0;
            if (storables != null)
            {
                for (int i = 0; i < storables.Count; i++)
                {
                    var s = storables[i] as SimpleJSON.JSONClass;
                    if (s == null || s["id"] == null) continue;
                    string id = s["id"].Value;
                    if (AppearancePresetSuppress.IsPoseDrivingStorableId(id)) poseDrive++;
                    else if (id.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) clothing++;
                    else if (id.StartsWith("hair:", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, "HairPresets", StringComparison.OrdinalIgnoreCase)) hair++;
                    else if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase))
                    {
                        geometry++;
                        // VaM stores worn item selection inside DAZCharacterSelector's geometry
                        // storable. Top-level clothingItem/hairItem storables only hold materials.
                        var geometryClothing = s["clothing"] as SimpleJSON.JSONArray;
                        var geometryHair = s["hair"] as SimpleJSON.JSONArray;
                        if (geometryClothing != null) clothing += geometryClothing.Count;
                        if (geometryHair != null) hair += geometryHair.Count;
                    }
                    else if (string.Equals(id, "MorphPresets", StringComparison.OrdinalIgnoreCase)
                        || id.IndexOf("morph", StringComparison.OrdinalIgnoreCase) >= 0) morph++;
                }
            }
            return "storables=" + n
                + " poseDrive=" + poseDrive
                + " clothing=" + clothing
                + " hair=" + hair
                + " morphish=" + morph
                + " geometry=" + geometry;
        }
    }
}
