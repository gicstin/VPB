using System;

namespace VpbNet
{
    public enum VpbNetStorableVerdict
    {
        Allowed = 0,
        BadIdentifier = 1,
        PluginReference = 2,
        UnknownStorable = 3,
        UnknownParam = 4,
        DeniedName = 5,
        Oversize = 6
    }

    public static class VpbNetStorableLimits
    {
        public const int MaxStorableChars = 64;
        public const int MaxParamChars = 96;
        public const int MaxStringValueChars = 160;
    }

    public static class VpbNetAtomParamKind
    {
        public const byte None = 0;
        public const byte Float = 1;
        public const byte Bool = 2;
        public const byte Color = 3;
        public const byte Chooser = 4;
        public const byte Text = 5;

        public static bool IsKnown(byte kind)
        {
            return kind >= Float && kind <= Text;
        }

        public static string Name(byte kind)
        {
            switch (kind)
            {
                case Float: return "number";
                case Bool: return "switch";
                case Color: return "colour";
                case Chooser: return "choice";
                case Text: return "text";
            }
            return "unknown";
        }
    }

    public static class VpbNetStorableWhitelist
    {
        static readonly string[] Storables =
        {
            "geometry"
        };

        static readonly string[] GeometryParamPrefixes =
        {
            "clothing:",
            "hair:",
            "morph:"
        };

        static readonly string[] DeniedFragments =
        {
            "plugin",
            "script",
            "cslist",
            "url",
            "preset",
            "loadpreset",
            "savepreset",
            "browse",
            "filepath",
            "path",
            "exec",
            "command"
        };

        public static VpbNetStorableVerdict Check(string storable, string param)
        {
            if (storable == null || param == null) return VpbNetStorableVerdict.BadIdentifier;
            if (storable.Length == 0 || param.Length == 0) return VpbNetStorableVerdict.BadIdentifier;
            if (storable.Length > VpbNetStorableLimits.MaxStorableChars
                || param.Length > VpbNetStorableLimits.MaxParamChars)
                return VpbNetStorableVerdict.Oversize;

            if (!VpbNetEventCodec.IsSafeIdentifier(storable) || !VpbNetEventCodec.IsSafeIdentifier(param))
                return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(storable) || VpbNetEventCodec.IsPluginReference(param))
                return VpbNetStorableVerdict.PluginReference;

            if (ContainsDenied(storable) || ContainsDenied(param))
                return VpbNetStorableVerdict.DeniedName;

            if (!IsKnownStorable(storable)) return VpbNetStorableVerdict.UnknownStorable;
            if (!IsKnownParam(storable, param)) return VpbNetStorableVerdict.UnknownParam;

            return VpbNetStorableVerdict.Allowed;
        }

        public static bool IsAllowed(string storable, string param)
        {
            return Check(storable, param) == VpbNetStorableVerdict.Allowed;
        }

        public static VpbNetStorableVerdict CheckTrigger(string atomUid, string storableId)
        {
            if (atomUid == null || storableId == null) return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length == 0 || storableId.Length == 0) return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length > VpbNetStorableLimits.MaxParamChars
                || storableId.Length > VpbNetStorableLimits.MaxStorableChars)
                return VpbNetStorableVerdict.Oversize;

            if (!VpbNetEventCodec.IsSafeIdentifier(atomUid) || !VpbNetEventCodec.IsSafeIdentifier(storableId))
                return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(atomUid) || VpbNetEventCodec.IsPluginReference(storableId))
                return VpbNetStorableVerdict.PluginReference;

            if (ContainsDenied(atomUid) || ContainsDenied(storableId))
                return VpbNetStorableVerdict.DeniedName;

            return VpbNetStorableVerdict.Allowed;
        }

        public static bool IsAllowedTrigger(string atomUid, string storableId)
        {
            return CheckTrigger(atomUid, storableId) == VpbNetStorableVerdict.Allowed;
        }

        // Denylist, not a lamp list — plugin/path fragments refused.
        static readonly string[] DeniedParamStorables =
        {
            "control",
            "geometry"
        };

        public static VpbNetStorableVerdict CheckAtomParam(string atomUid, string storableId, string param)
        {
            if (atomUid == null || storableId == null || param == null)
                return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length == 0 || storableId.Length == 0 || param.Length == 0)
                return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length > VpbNetStorableLimits.MaxParamChars
                || storableId.Length > VpbNetStorableLimits.MaxStorableChars
                || param.Length > VpbNetStorableLimits.MaxParamChars)
                return VpbNetStorableVerdict.Oversize;

            if (!VpbNetEventCodec.IsSafeIdentifier(atomUid)
                || !VpbNetEventCodec.IsSafeIdentifier(storableId)
                || !VpbNetEventCodec.IsSafeIdentifier(param))
                return VpbNetStorableVerdict.BadIdentifier;

            if (VpbNetEventCodec.IsPluginReference(atomUid)
                || VpbNetEventCodec.IsPluginReference(storableId)
                || VpbNetEventCodec.IsPluginReference(param))
                return VpbNetStorableVerdict.PluginReference;

            // Skip "url" fragment check only for Skyshop sky.
            bool lightingUrl = IsSceneLightingStorable(storableId)
                && IndexOfIgnoreCase(param, "url") >= 0;

            if (!lightingUrl)
            {
                if (ContainsDenied(atomUid) || ContainsDenied(storableId) || ContainsDenied(param))
                    return VpbNetStorableVerdict.DeniedName;
            }
            else if (ContainsDenied(atomUid) || ContainsDenied(storableId))
            {
                return VpbNetStorableVerdict.DeniedName;
            }

            if (IsSubSceneContentUid(atomUid)) return VpbNetStorableVerdict.DeniedName;
            if (IsDeniedParamStorable(storableId)) return VpbNetStorableVerdict.UnknownStorable;

            // CoreControl is nav rig AND Skyshop.
            if (IsCoreControlUid(atomUid) && !IsSceneLightingStorable(storableId))
                return VpbNetStorableVerdict.UnknownStorable;

            return VpbNetStorableVerdict.Allowed;
        }

        public static bool IsAllowedAtomParam(string atomUid, string storableId, string param)
        {
            return CheckAtomParam(atomUid, storableId, param) == VpbNetStorableVerdict.Allowed;
        }

        public static bool IsDeniedParamStorable(string storableId)
        {
            if (storableId == null) return true;
            for (int i = 0; i < DeniedParamStorables.Length; i++)
            {
                if (string.Equals(DeniedParamStorables[i], storableId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsCoreControlUid(string atomUid)
        {
            if (atomUid == null || atomUid.Length < 11) return false;
            return IndexOfIgnoreCase(atomUid, "CoreControl") == 0;
        }

        public static bool IsSceneLightingHost(string atomUid, string atomType)
        {
            if (IsCoreControlUid(atomUid)) return true;
            if (atomType != null && string.Equals(atomType, "CoreControl", StringComparison.OrdinalIgnoreCase))
                return true;
            if (atomType != null && string.Equals(atomType, "Environment", StringComparison.OrdinalIgnoreCase))
                return true;
            if (atomUid != null && string.Equals(atomUid, "Environment", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        public static bool IsSceneLightingStorable(string storableId)
        {
            if (storableId == null || storableId.Length == 0) return false;
            // Store id is "GlobalLighting", not the C# type SkyshopLightController.
            if (string.Equals(storableId, "GlobalLighting", StringComparison.OrdinalIgnoreCase)) return true;
            if (IndexOfIgnoreCase(storableId, "skyshop") >= 0) return true;
            if (IndexOfIgnoreCase(storableId, "skybox") >= 0) return true;
            if (IndexOfIgnoreCase(storableId, "lighting") >= 0) return true;
            if (string.Equals(storableId, "ImageControl", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static readonly string[] DeniedAtomTypes =
        {
            "CustomUnityAsset",
            "Person"
        };

        // Named not detected — syncing CoreControl drags their camera.
        static readonly string[] PlayerLocalAtomTypes =
        {
            "CoreControl",
            "WindowCamera",
            "VRController",
            "PlayerNavigationPanel"
        };

        public static VpbNetStorableVerdict CheckAtom(string atomUid, string atomType)
        {
            if (atomUid == null || atomType == null) return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length == 0 || atomType.Length == 0) return VpbNetStorableVerdict.BadIdentifier;
            if (atomUid.Length > VpbNetStorableLimits.MaxParamChars
                || atomType.Length > VpbNetStorableLimits.MaxStorableChars)
                return VpbNetStorableVerdict.Oversize;

            if (!VpbNetEventCodec.IsSafeIdentifier(atomUid) || !VpbNetEventCodec.IsSafeIdentifier(atomType))
                return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(atomUid) || VpbNetEventCodec.IsPluginReference(atomType))
                return VpbNetStorableVerdict.PluginReference;

            if (ContainsDenied(atomUid) || ContainsDenied(atomType))
                return VpbNetStorableVerdict.DeniedName;

            if (IsDeniedAtomType(atomType)) return VpbNetStorableVerdict.DeniedName;
            if (IsSubSceneContentUid(atomUid)) return VpbNetStorableVerdict.DeniedName;

            return VpbNetStorableVerdict.Allowed;
        }

        // Uid knows first ("subscene/atom") before containingSubScene.
        public static bool IsSubSceneContentUid(string atomUid)
        {
            return atomUid != null && atomUid.IndexOf('/') >= 0;
        }

        public static bool IsAllowedAtom(string atomUid, string atomType)
        {
            return CheckAtom(atomUid, atomType) == VpbNetStorableVerdict.Allowed;
        }

        public static bool IsDeniedAtomType(string atomType)
        {
            if (atomType == null) return true;
            for (int i = 0; i < DeniedAtomTypes.Length; i++)
            {
                if (string.Equals(DeniedAtomTypes[i], atomType, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return IsPlayerLocalAtomType(atomType);
        }

        public static bool IsPlayerLocalAtomType(string atomType)
        {
            if (atomType == null) return false;
            for (int i = 0; i < PlayerLocalAtomTypes.Length; i++)
            {
                if (string.Equals(PlayerLocalAtomTypes[i], atomType, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Package-qualified or loose under Custom/Saves.
        static readonly string[] SubSceneRoots = { "Custom/", "Saves/" };

        public static VpbNetStorableVerdict CheckSubSceneRef(string reference)
        {
            if (reference == null || reference.Length == 0) return VpbNetStorableVerdict.BadIdentifier;
            if (reference.Length > VpbNetStorableLimits.MaxStringValueChars) return VpbNetStorableVerdict.Oversize;
            if (!VpbNetEventCodec.IsSafeIdentifier(reference)) return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(reference)) return VpbNetStorableVerdict.PluginReference;
            if (!EndsWithOrdinalIgnoreCase(reference, ".json")) return VpbNetStorableVerdict.BadIdentifier;

            int colon = reference.IndexOf(':');
            if (colon > 0)
            {
                if (colon + 1 >= reference.Length || reference[colon + 1] != '/')
                    return VpbNetStorableVerdict.BadIdentifier;
                return StartsWithAny(reference.Substring(colon + 2), SubSceneRoots)
                    ? VpbNetStorableVerdict.Allowed
                    : VpbNetStorableVerdict.BadIdentifier;
            }

            return StartsWithAny(reference, SubSceneRoots)
                ? VpbNetStorableVerdict.Allowed
                : VpbNetStorableVerdict.BadIdentifier;
        }

        public static bool IsAllowedSubSceneRef(string reference)
        {
            return CheckSubSceneRef(reference) == VpbNetStorableVerdict.Allowed;
        }

        // VaM preset loader reads these — same path safety + content root + preset ext.
        static readonly string[] PresetRoots = { "Custom/", "Saves/", "AddonPackages/" };
        static readonly string[] PresetExtensions = { ".vap", ".vaj", ".vam", ".json" };

        static readonly string[] PresetActions =
        {
            "LoadClothing",
            "LoadHair",
            "LoadSkin",
            "LoadMorphs",
            "LoadPose",
            "LoadAppearance"
        };

        public static bool IsKnownPresetAction(string action)
        {
            if (action == null) return false;
            for (int i = 0; i < PresetActions.Length; i++)
            {
                if (string.Equals(PresetActions[i], action, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static VpbNetStorableVerdict CheckPresetRef(string reference)
        {
            if (reference == null || reference.Length == 0) return VpbNetStorableVerdict.BadIdentifier;
            if (reference.Length > VpbNetEventLimits.MaxEntryPath) return VpbNetStorableVerdict.Oversize;
            if (!VpbNetEventCodec.IsSafeIdentifier(reference, VpbNetEventLimits.MaxEntryPath))
                return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(reference)) return VpbNetStorableVerdict.PluginReference;

            bool ext = false;
            for (int i = 0; i < PresetExtensions.Length; i++)
            {
                if (!EndsWithOrdinalIgnoreCase(reference, PresetExtensions[i])) continue;
                ext = true;
                break;
            }
            if (!ext) return VpbNetStorableVerdict.BadIdentifier;

            int colon = reference.IndexOf(':');
            if (colon > 0)
            {
                if (colon + 1 >= reference.Length || reference[colon + 1] != '/')
                    return VpbNetStorableVerdict.BadIdentifier;
                return StartsWithAny(reference.Substring(colon + 2), PresetRoots)
                    ? VpbNetStorableVerdict.Allowed
                    : VpbNetStorableVerdict.BadIdentifier;
            }

            return StartsWithAny(reference, PresetRoots)
                ? VpbNetStorableVerdict.Allowed
                : VpbNetStorableVerdict.BadIdentifier;
        }

        public static bool IsAllowedPresetRef(string reference)
        {
            return CheckPresetRef(reference) == VpbNetStorableVerdict.Allowed;
        }

        static readonly string[] SkyExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".hdr", ".exr", ".tif", ".tiff" };

        public static VpbNetStorableVerdict CheckSkyRef(string reference)
        {
            if (reference == null || reference.Length == 0) return VpbNetStorableVerdict.Allowed;
            if (reference.Length > VpbNetEventLimits.MaxEntryPath) return VpbNetStorableVerdict.Oversize;
            if (!VpbNetEventCodec.IsSafeIdentifier(reference, VpbNetEventLimits.MaxEntryPath))
                return VpbNetStorableVerdict.BadIdentifier;
            if (VpbNetEventCodec.IsPluginReference(reference)) return VpbNetStorableVerdict.PluginReference;

            bool ext = false;
            for (int i = 0; i < SkyExtensions.Length; i++)
            {
                if (!EndsWithOrdinalIgnoreCase(reference, SkyExtensions[i])) continue;
                ext = true;
                break;
            }
            if (!ext) return VpbNetStorableVerdict.BadIdentifier;

            int colon = reference.IndexOf(':');
            if (colon > 0)
            {
                if (colon + 1 >= reference.Length || reference[colon + 1] != '/')
                    return VpbNetStorableVerdict.BadIdentifier;
                return StartsWithAny(reference.Substring(colon + 2), PresetRoots)
                    ? VpbNetStorableVerdict.Allowed
                    : VpbNetStorableVerdict.BadIdentifier;
            }

            return StartsWithAny(reference, PresetRoots)
                ? VpbNetStorableVerdict.Allowed
                : VpbNetStorableVerdict.BadIdentifier;
        }

        public static bool IsAllowedSkyRef(string reference)
        {
            return CheckSkyRef(reference) == VpbNetStorableVerdict.Allowed;
        }

        static bool EndsWithOrdinalIgnoreCase(string s, string suffix)
        {
            if (s.Length < suffix.Length) return false;
            int off = s.Length - suffix.Length;
            for (int i = 0; i < suffix.Length; i++)
            {
                char a = s[off + i];
                char b = suffix[i];
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
                if (a != b) return false;
            }
            return true;
        }

        public static bool IsKnownStorable(string storable)
        {
            for (int i = 0; i < Storables.Length; i++)
            {
                if (string.Equals(Storables[i], storable, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsAllowedStringValue(string value)
        {
            if (value == null) return false;
            if (value.Length > VpbNetStorableLimits.MaxStringValueChars) return false;
            if (!VpbNetEventCodec.IsSafeText(value, VpbNetStorableLimits.MaxStringValueChars)) return false;
            return !VpbNetEventCodec.IsPluginReference(value);
        }

        public static string Explain(VpbNetStorableVerdict v, string storable, string param)
        {
            switch (v)
            {
                case VpbNetStorableVerdict.Allowed:
                    return "allowed";
                case VpbNetStorableVerdict.BadIdentifier:
                    return "that peer sent a control name this build refuses to handle";
                case VpbNetStorableVerdict.PluginReference:
                    return "that peer tried to drive a plugin, which is never accepted";
                case VpbNetStorableVerdict.UnknownStorable:
                    return "that peer tried to drive \"" + storable + "\", which peers are not allowed to touch";
                case VpbNetStorableVerdict.UnknownParam:
                    return "that peer tried to set \"" + param + "\" on " + storable
                        + ", which is outside what peers may change";
                case VpbNetStorableVerdict.DeniedName:
                    return "that peer tried to drive something naming a plugin, preset or path";
                case VpbNetStorableVerdict.Oversize:
                    return "that peer sent a control name longer than this build accepts";
            }
            return "that peer sent a control this build refuses";
        }

        static bool IsKnownParam(string storable, string param)
        {
            if (string.Equals(storable, "geometry", StringComparison.Ordinal))
                return StartsWithAny(param, GeometryParamPrefixes);
            return false;
        }

        static bool StartsWithAny(string s, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
            {
                string p = prefixes[i];
                if (s.Length <= p.Length) continue;
                if (string.CompareOrdinal(s, 0, p, 0, p.Length) == 0) return true;
            }
            return false;
        }

        static bool ContainsDenied(string s)
        {
            for (int i = 0; i < DeniedFragments.Length; i++)
            {
                if (IndexOfIgnoreCase(s, DeniedFragments[i]) >= 0) return true;
            }
            return false;
        }

        static int IndexOfIgnoreCase(string s, string needle)
        {
            int limit = s.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                int j = 0;
                while (j < needle.Length)
                {
                    char a = s[i + j];
                    char b = needle[j];
                    if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                    if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
                    if (a != b) break;
                    j++;
                }
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
