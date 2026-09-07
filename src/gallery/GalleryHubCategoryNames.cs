using System;
using System.Text;

namespace VPB
{
    internal static class GalleryHubCategoryNames
    {
        static readonly string[] Canonical =
        {
            "Looks", "Scenes", "Clothing", "Assets + Accessories", "Hairstyles",
            "Plugins + Scripts", "Environments", "Textures", "Demo + Lite", "Guides",
            "Morphs", "Poses", "Audio", "Toolkits + Templates", "Lighting + HDRI",
            "Mocap + Animation", "Other", "Comics + Storytelling", "Voxta Content",
            "Blend Shapes",
        };

        internal static string Display(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            for (int i = 0; i < Canonical.Length; i++)
            {
                if (string.Equals(Canonical[i], value, StringComparison.OrdinalIgnoreCase))
                    return Canonical[i];
            }
            string s = value.Trim();
            if (s.Length == 0) return "";
            var sb = new StringBuilder(s.Length);
            bool cap = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '+' || c == '-' || c == '/')
                {
                    sb.Append(c);
                    cap = true;
                    continue;
                }
                if (cap)
                {
                    if (c >= 'a' && c <= 'z') sb.Append((char)(c - 32));
                    else sb.Append(c);
                    cap = false;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
