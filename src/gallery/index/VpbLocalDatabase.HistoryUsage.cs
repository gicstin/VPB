using System;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>
    /// History / <c>item_usage</c> helpers for VaM-native load paths (file browser, Scene Loader triggers)
    /// in addition to explicit gallery UI records.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        private const float HistoryRecordDedupeSeconds = 2.5f;
        private static string _lastHistoryRecordKey = "";
        private static string _lastHistoryRecordKind = "";
        private static DateTime _lastHistoryRecordUtc = DateTime.MinValue;

        /// <summary>Normalize a VaM load path / UID into an <c>item_usage.item_key</c>.</summary>
        internal static string BuildUsageKeyFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string k = path;
                try
                {
                    string n = FileManager.NormalizePath(path);
                    if (!string.IsNullOrEmpty(n)) k = n;
                }
                catch { }
                if (string.IsNullOrEmpty(k)) return "";
                return k.Replace('\\', '/').Trim().ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Skip ephemeral / auto loads that must not appear in History
        /// (temp merge scenes, default scene, empty browser callbacks).
        /// </summary>
        internal static bool ShouldSkipHistoryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string p;
            try { p = path.Replace('\\', '/').Trim(); }
            catch { return true; }
            if (p.Length == 0) return true;
            if (string.Equals(p, "savefile", StringComparison.OrdinalIgnoreCase)) return true;

            string lower = p.ToLowerInvariant();
            if (lower.IndexOf("vpb_temp_", StringComparison.Ordinal) >= 0) return true;
            if (lower.IndexOf("vpb_scene", StringComparison.Ordinal) >= 0) return true;
            if (lower.IndexOf("vpb_filtered", StringComparison.Ordinal) >= 0) return true;
            if (lower.IndexOf("vpb_rewrite", StringComparison.Ordinal) >= 0) return true;
            if (lower.EndsWith("saves/scene/meshedvr/default.json", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Map VaM preset storable id → History kind (matches gallery UI kinds).</summary>
        internal static string KindFromPresetStorableId(string storableId)
        {
            if (string.IsNullOrEmpty(storableId)) return "item";
            if (string.Equals(storableId, "Appearancepresets", StringComparison.OrdinalIgnoreCase)) return "appearance";
            if (string.Equals(storableId, "clothingpresets", StringComparison.OrdinalIgnoreCase)) return "clothing";
            if (string.Equals(storableId, "hairpresets", StringComparison.OrdinalIgnoreCase)) return "hair";
            if (string.Equals(storableId, "posepresets", StringComparison.OrdinalIgnoreCase)) return "pose";
            if (string.Equals(storableId, "pluginspresets", StringComparison.OrdinalIgnoreCase)) return "plugins";
            if (string.Equals(storableId, "skin", StringComparison.OrdinalIgnoreCase)) return "skin";
            if (string.Equals(storableId, "morphs", StringComparison.OrdinalIgnoreCase)) return "morphs";
            if (string.Equals(storableId, "animationpresets", StringComparison.OrdinalIgnoreCase)) return "item";
            if (string.Equals(storableId, "breastphysicspresets", StringComparison.OrdinalIgnoreCase)) return "item";
            return "item";
        }

        /// <summary>
        /// Record History for a VaM load path. No-ops for skip paths and near-duplicate
        /// (same key+kind within <see cref="HistoryRecordDedupeSeconds"/>) so gallery UI + hooks
        /// do not double-count one user action.
        /// </summary>
        internal static void TryRecordItemUseFromPath(string path, string kind)
        {
            if (ShouldSkipHistoryPath(path)) return;
            string key = BuildUsageKeyFromPath(path);
            if (string.IsNullOrEmpty(key)) return;
            TryRecordItemUse(key, kind ?? "");
        }

        /// <summary>Returns true when this key+kind was recorded very recently (caller should skip).</summary>
        private static bool IsRecentHistoryDuplicate(string itemKey, string kind)
        {
            if (string.IsNullOrEmpty(itemKey)) return true;
            try
            {
                DateTime now = DateTime.UtcNow;
                if (string.Equals(_lastHistoryRecordKey, itemKey, StringComparison.Ordinal)
                    && string.Equals(_lastHistoryRecordKind, kind ?? "", StringComparison.Ordinal)
                    && (now - _lastHistoryRecordUtc).TotalSeconds < HistoryRecordDedupeSeconds)
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void RememberHistoryRecord(string itemKey, string kind)
        {
            try
            {
                _lastHistoryRecordKey = itemKey ?? "";
                _lastHistoryRecordKind = kind ?? "";
                _lastHistoryRecordUtc = DateTime.UtcNow;
            }
            catch { }
        }
    }
}
