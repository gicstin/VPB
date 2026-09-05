using System;
using System.Collections.Generic;

namespace VPB
{
    internal static class GalleryHubTypeItemScope
    {
        static readonly string[] Empty = new string[0];

        static readonly string[] LooksScope = { "Appearance", "Scenes" };
        static readonly string[] ScenesScope = { "Scenes" };
        static readonly string[] EnvironmentScope = { "SubScenes", "Scenes" };
        static readonly string[] ClothingScope = { "Clothing" };
        static readonly string[] HairScope = { "Hair", "Hair Presets" };
        static readonly string[] AssetScope = { "CUA" };
        static readonly string[] PoseScope = { "Pose" };
        static readonly string[] AnimationScope = { "Animation", "Pose" };
        static readonly string[] MorphScope = { "Morphs" };
        static readonly string[] TextureScope = { "Skin" };
        static readonly string[] PluginScope = { "Plugins" };

        internal static string[] CategoriesFor(string hubDisplayName)
        {
            if (string.IsNullOrEmpty(hubDisplayName)) return Empty;
            if (Is(hubDisplayName, "Looks")) return LooksScope;
            if (Is(hubDisplayName, "Scenes")
                || Is(hubDisplayName, "Demo + Lite")
                || Is(hubDisplayName, "Comics + Storytelling")) return ScenesScope;
            if (Is(hubDisplayName, "Environments")
                || Is(hubDisplayName, "Lighting + HDRI")) return EnvironmentScope;
            if (Is(hubDisplayName, "Clothing")) return ClothingScope;
            if (Is(hubDisplayName, "Hairstyles")) return HairScope;
            if (Is(hubDisplayName, "Assets + Accessories")) return AssetScope;
            if (Is(hubDisplayName, "Poses")) return PoseScope;
            if (Is(hubDisplayName, "Mocap + Animation")) return AnimationScope;
            if (Is(hubDisplayName, "Morphs")) return MorphScope;
            if (Is(hubDisplayName, "Textures")) return TextureScope;
            if (Is(hubDisplayName, "Plugins + Scripts")) return PluginScope;
            return Empty;
        }

        internal static bool HasCategoryScope(string hubDisplayName)
        {
            return CategoriesFor(hubDisplayName).Length > 0;
        }

        static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
