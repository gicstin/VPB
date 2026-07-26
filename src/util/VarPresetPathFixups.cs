using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace VPB.src.util
{
    /// <summary>
    /// Rewrites VaM preset JSON path references (SELF:, ./, unprefixed Custom/) to absolute
    /// package UIDs so deferred texture loads after PopLoadDir still resolve to the preset
    /// source package instead of the target atom's base package.
    /// </summary>
    internal static class VarPresetPathFixups
    {
        public static void Apply(JSONClass presetJson, string normalizedSourcePath)
        {
            if (presetJson == null || string.IsNullOrEmpty(normalizedSourcePath)) return;
            if (!UI.IsLikelyVarPackageReference(normalizedSourcePath)) return;

            int colon = normalizedSourcePath.IndexOf(':');
            if (colon <= 0) return;
            string presetPackageName = normalizedSourcePath.Substring(0, colon);

            JSONExtensions.ReplaceSelfPrefixWithPackageUidMutable(presetJson, presetPackageName);

            string folderFullPath = FileManagerSecure.GetDirectoryName(normalizedSourcePath);
            folderFullPath = FileManagerSecure.NormalizeLoadPath(folderFullPath);
            if (!string.IsNullOrEmpty(folderFullPath))
                FixupDotRelativePaths(presetJson, folderFullPath);

            FixupUnprefixedCustomPaths(presetJson, presetPackageName);
        }

        static void FixupDotRelativePaths(JSONNode node, string folderFullPath)
        {
            if (node == null || string.IsNullOrEmpty(folderFullPath)) return;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                    FixupDotRelativePaths(kvp.Value, folderFullPath);
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                    FixupDotRelativePaths(arr[i], folderFullPath);
                return;
            }

            string v = node.Value;
            if (string.IsNullOrEmpty(v)) return;
            if (!v.StartsWith("./", System.StringComparison.Ordinal)
                && !v.StartsWith(".\\", System.StringComparison.Ordinal)) return;

            string rel = v.Substring(2).Replace('\\', '/');
            node.Value = folderFullPath + "/" + rel;
        }

        static void FixupUnprefixedCustomPaths(JSONNode node, string presetPackageName)
        {
            if (node == null || string.IsNullOrEmpty(presetPackageName)) return;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                    FixupUnprefixedCustomPaths(kvp.Value, presetPackageName);
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                    FixupUnprefixedCustomPaths(arr[i], presetPackageName);
                return;
            }

            string v = node.Value;
            if (string.IsNullOrEmpty(v)) return;
            if (v.IndexOf(':') >= 0) return;

            string vNorm = v.Replace('\\', '/');
            if (!vNorm.StartsWith("Custom/", System.StringComparison.OrdinalIgnoreCase)) return;

            string candidate = presetPackageName + ":/" + vNorm;
            string normalizedCandidate = FileManagerSecure.NormalizePath(candidate);
            if (FileManagerSecure.FileExists(normalizedCandidate))
                node.Value = candidate;
        }
    }
}
