using SimpleJSON;

namespace VPB
{
    internal struct SubToggleOptions
    {
        public bool IncludeAppearanceMorphs;
        public bool IncludePhysicalPoseMorphs;

        // Consumed at apply-site as a PresetLockStore flag, not here. BA mirrors this split.
        public bool SuppressMorphLoad;
        public bool SuppressRootNodeLoad;

        public bool IncludePhysical;
        public bool IncludePose;
        public bool IncludeAppearance;
        public bool IncludeMocap;

        public static SubToggleOptions AllOn()
        {
            return new SubToggleOptions
            {
                IncludeAppearanceMorphs = true,
                IncludePhysicalPoseMorphs = true,
                SuppressMorphLoad = false,
                SuppressRootNodeLoad = false,
                IncludePhysical = true,
                IncludePose = true,
                IncludeAppearance = true,
                IncludeMocap = true,
            };
        }
    }

    internal static class VpbImportSubToggleFilter
    {
        // Mutates presetJSON in place. Caller is expected to pass a freshly-built JSON
        // each Apply (WrapAtomNodeAsPreset or fresh JSON.Parse), never a shared reference.
        public static JSONClass FilterForType(JSONClass presetJSON, VpbResourceType type, SubToggleOptions options)
        {
            if (presetJSON == null) return null;

            switch (type)
            {
                case VpbResourceType.Morphs:
                    SetStorableBool(presetJSON, "MorphPresets", "includeAppearance", options.IncludeAppearanceMorphs);
                    SetStorableBool(presetJSON, "MorphPresets", "includePhysical", options.IncludePhysicalPoseMorphs);
                    return presetJSON;

                case VpbResourceType.Pose:
                    if (options.SuppressRootNodeLoad)
                    {
                        RemoveStorable(presetJSON, "control");
                    }
                    return presetJSON;

                case VpbResourceType.General:
                    SetStorableBool(presetJSON, "Preset", "includePhysical", options.IncludePhysical);
                    SetStorableBool(presetJSON, "Preset", "includeOptional", options.IncludePose);
                    SetStorableBool(presetJSON, "Preset", "includeAppearance", options.IncludeAppearance);
                    return presetJSON;

                default:
                    return presetJSON;
            }
        }

        private static void SetStorableBool(JSONClass presetJSON, string storableId, string key, bool value)
        {
            JSONArray storables = presetJSON?["storables"]?.AsArray;
            if (storables == null) return;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass s = storables[i].AsObject;
                if (s == null) continue;
                if (s["id"] == null || s["id"].Value != storableId) continue;

                s[key] = new JSONData(value);
                return;
            }
        }

        private static void RemoveStorable(JSONClass presetJSON, string storableId)
        {
            JSONArray storables = presetJSON?["storables"]?.AsArray;
            if (storables == null) return;

            for (int i = storables.Count - 1; i >= 0; i--)
            {
                JSONClass s = storables[i].AsObject;
                if (s == null) continue;
                if (s["id"] != null && s["id"].Value == storableId)
                {
                    storables.Remove(i);
                    return;
                }
            }
        }
    }
}
