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
        sealed class OwnerlessMorphReference
        {
            public JSONNode UidNode;
            public string InternalPath;
        }

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

        public static List<string> ResolveOwnerlessMorphPaths(JSONClass presetJson)
        {
            List<OwnerlessMorphReference> references = new List<OwnerlessMorphReference>();
            CollectOwnerlessMorphPaths(presetJson, references);
            HashSet<string> ownerUids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (references.Count == 0) return new List<string>();

            Dictionary<string, string> uniqueOwnerByPath =
                new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            HashSet<string> checkedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < references.Count; i++)
            {
                string path = references[i].InternalPath;
                if (!checkedPaths.Add(path)) continue;
                string ownerUid;
                MorphOwnerLookupResult result = FileManager.TryGetMorphOwner(path, out ownerUid);
                if (result == MorphOwnerLookupResult.IndexIncomplete)
                {
                    LogUtil.LogWarning("[VPB Morph] Owner index incomplete; ownerless morphs unchanged.");
                    return new List<string>();
                }
                if (result == MorphOwnerLookupResult.Unique)
                {
                    uniqueOwnerByPath[path] = ownerUid;
                }
                else
                {
                    LogUtil.LogWarning("[VPB Morph] Ownerless morph unchanged: " + path
                        + " result=" + result);
                }
            }

            HashSet<string> confirmedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> match in uniqueOwnerByPath)
            {
                if (VamOnDemandLoader.EnsurePackageRegisteredForMorph(match.Value, match.Key))
                {
                    confirmedPaths.Add(match.Key);
                    ownerUids.Add(match.Value);
                }
                else
                {
                    LogUtil.LogWarning("[VPB Morph] Owner package unavailable; morph unchanged: "
                        + match.Value + ":/" + match.Key);
                }
            }

            for (int i = 0; i < references.Count; i++)
            {
                OwnerlessMorphReference reference = references[i];
                if (!confirmedPaths.Contains(reference.InternalPath)) continue;
                string ownerUid = uniqueOwnerByPath[reference.InternalPath];
                reference.UidNode.Value = ownerUid + ":/" + reference.InternalPath;
                LogUtil.Log("[VPB Morph] Qualified ownerless morph " + reference.InternalPath
                    + " owner=" + ownerUid);
            }
            return new List<string>(ownerUids);
        }

        static void CollectOwnerlessMorphPaths(JSONNode node, List<OwnerlessMorphReference> references)
        {
            if (node == null) return;
            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                if (obj.HasKey("uid"))
                {
                    JSONNode uidNode = obj["uid"];
                    string raw = uidNode != null ? uidNode.Value : null;
                    string normalizedMorphPath = string.IsNullOrEmpty(raw)
                        ? null
                        : FileManagerSecure.NormalizePath(raw.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(normalizedMorphPath)
                        && normalizedMorphPath.IndexOf(':') < 0
                        && normalizedMorphPath.StartsWith("Custom/Atom/Person/Morphs/", System.StringComparison.OrdinalIgnoreCase)
                        && normalizedMorphPath.EndsWith(".vmi", System.StringComparison.OrdinalIgnoreCase)
                        && !FileManagerSecure.FileExists(normalizedMorphPath, true))
                    {
                        references.Add(new OwnerlessMorphReference
                        {
                            UidNode = uidNode,
                            InternalPath = normalizedMorphPath
                        });
                    }
                }

                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                    CollectOwnerlessMorphPaths(kvp.Value, references);
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr == null) return;
            for (int i = 0; i < arr.Count; i++)
                CollectOwnerlessMorphPaths(arr[i], references);
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
