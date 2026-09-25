using SimpleJSON;

namespace VPB
{
    internal static class VaMTextureLoadFlags
    {
        private static readonly bool[] CustomSlotDefaultLinear = { false, true, true, false, true, false };
        private static readonly bool[] CustomSlotDefaultNormal = { false, false, false, false, false, false };
        private static readonly bool[] CustomSlotDefaultTransparency = { false, false, false, false, false, false };

        public static NativeTextureOnDemandCache.TextureFlags DefaultImageLoaderFlags()
        {
            return new NativeTextureOnDemandCache.TextureFlags
            {
                compress = true,
                linear = false,
                isNormalMap = false,
                createAlphaFromGrayscale = false,
                createNormalFromBump = false,
                invert = false,
                isReadable = false,
                bumpStrength = 1f
            };
        }

        public static NativeTextureOnDemandCache.TextureFlags DiffuseFlags()
        {
            return DefaultImageLoaderFlags();
        }

        public static NativeTextureOnDemandCache.TextureFlags SpecularOrGlossFlags()
        {
            var f = DefaultImageLoaderFlags();
            f.linear = true;
            return f;
        }

        public static NativeTextureOnDemandCache.TextureFlags NormalMapFlags()
        {
            var f = DefaultImageLoaderFlags();
            f.linear = true;
            f.isNormalMap = true;
            f.compress = false;
            return f;
        }

        public static NativeTextureOnDemandCache.TextureFlags MaterialOptionsCustomFlags(bool isLinear, bool isNormalMap, bool isTransparency)
        {
            var f = DefaultImageLoaderFlags();
            f.linear = isLinear;
            f.isNormalMap = isNormalMap;
            f.createAlphaFromGrayscale = isTransparency;
            f.compress = !isNormalMap;
            return f;
        }

        public static NativeTextureOnDemandCache.TextureFlags SimulationTextureFlags()
        {
            var f = MaterialOptionsCustomFlags(isLinear: false, isNormalMap: false, isTransparency: false);
            f.isReadable = true;
            return f;
        }

        public static bool TryUnwrapUrlValue(JSONNode valueNode, out string url)
        {
            url = null;
            if (valueNode == null) return false;

            if (valueNode.AsObject == null && valueNode.AsArray == null)
            {
                url = valueNode.Value;
                return !string.IsNullOrEmpty(url);
            }

            JSONClass obj = valueNode.AsObject;
            if (obj == null) return false;

            JSONNode valNode = obj["val"];
            if (valNode == null) return false;

            url = valNode.Value;
            return !string.IsNullOrEmpty(url);
        }

        public static bool TryResolve(string jsonKey, JSONClass parentObject, out NativeTextureOnDemandCache.TextureFlags flags)
        {
            flags = DefaultImageLoaderFlags();
            if (string.IsNullOrEmpty(jsonKey)) return false;

            if (jsonKey.EndsWith("HadError", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (SuperControllerHook.IsSimulationTextureKey(jsonKey)
                || jsonKey.Equals("customSimTextureUrl", System.StringComparison.OrdinalIgnoreCase))
            {
                flags = SimulationTextureFlags();
                return true;
            }

            if (jsonKey.EndsWith("DiffuseUrl", System.StringComparison.OrdinalIgnoreCase)
                || jsonKey.EndsWith("DecalUrl", System.StringComparison.OrdinalIgnoreCase))
            {
                flags = DiffuseFlags();
                return true;
            }

            if (jsonKey.EndsWith("SpecularUrl", System.StringComparison.OrdinalIgnoreCase)
                || jsonKey.EndsWith("GlossUrl", System.StringComparison.OrdinalIgnoreCase))
            {
                flags = SpecularOrGlossFlags();
                return true;
            }

            if (jsonKey.EndsWith("NormalUrl", System.StringComparison.OrdinalIgnoreCase)
                || jsonKey.EndsWith("DetailUrl", System.StringComparison.OrdinalIgnoreCase))
            {
                flags = NormalMapFlags();
                return true;
            }

            int slot;
            if (TryParseMaterialOptionsCustomSlot(jsonKey, out slot))
            {
                bool isLinear = ReadParentBool(parentObject, "customTexture" + slot + "IsLinear", CustomSlotDefaultLinear[slot - 1]);
                bool isNormal = ReadParentBool(parentObject, "customTexture" + slot + "IsNormal", CustomSlotDefaultNormal[slot - 1]);
                bool isTransparency = ReadParentBool(parentObject, "customTexture" + slot + "IsTransparency", CustomSlotDefaultTransparency[slot - 1]);
                flags = MaterialOptionsCustomFlags(isLinear, isNormal, isTransparency);
                return true;
            }

            if (TryResolveCustomTextureShaderKey(jsonKey, out flags))
                return true;

            if (jsonKey.Equals("url", System.StringComparison.OrdinalIgnoreCase))
            {
                flags = DiffuseFlags();
                return true;
            }

            return false;
        }

        private static bool TryParseMaterialOptionsCustomSlot(string jsonKey, out int slot)
        {
            slot = 0;
            if (string.IsNullOrEmpty(jsonKey) || jsonKey.Length < 18) return false;
            if (!jsonKey.StartsWith("customTexture", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (!jsonKey.EndsWith("Url", System.StringComparison.OrdinalIgnoreCase)) return false;

            int start = "customTexture".Length;
            int end = jsonKey.Length - 3;
            if (end <= start) return false;

            string numStr = jsonKey.Substring(start, end - start);
            if (numStr.Length != 1) return false;
            if (numStr[0] < '1' || numStr[0] > '6') return false;

            slot = numStr[0] - '0';
            return true;
        }

        private static bool TryResolveCustomTextureShaderKey(string jsonKey, out NativeTextureOnDemandCache.TextureFlags flags)
        {
            flags = DefaultImageLoaderFlags();
            if (string.IsNullOrEmpty(jsonKey)) return false;
            if (jsonKey.IndexOf("customTexture_", System.StringComparison.OrdinalIgnoreCase) < 0) return false;

            if (jsonKey.IndexOf("BumpMap", System.StringComparison.OrdinalIgnoreCase) >= 0
                || jsonKey.IndexOf("NormalTex", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                flags = NormalMapFlags();
                return true;
            }

            if (jsonKey.IndexOf("SpecTex", System.StringComparison.OrdinalIgnoreCase) >= 0
                || jsonKey.IndexOf("GlossTex", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                flags = SpecularOrGlossFlags();
                return true;
            }

            if (jsonKey.IndexOf("AlphaTex", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                flags = MaterialOptionsCustomFlags(isLinear: false, isNormalMap: false, isTransparency: true);
                return true;
            }

            if (jsonKey.IndexOf("MainTex", System.StringComparison.OrdinalIgnoreCase) >= 0
                || jsonKey.IndexOf("DecalTex", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                flags = DiffuseFlags();
                return true;
            }

            return false;
        }

        private static bool ReadParentBool(JSONClass parent, string fieldName, bool defaultValue)
        {
            if (parent == null || string.IsNullOrEmpty(fieldName)) return defaultValue;
            JSONNode node = parent[fieldName];
            if (node == null) return defaultValue;
            try { return node.AsBool; }
            catch { return defaultValue; }
        }
    }
}
