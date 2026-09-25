using System;

namespace VPB
{
    /// <summary>Gallery tile caption helpers (#90).</summary>
    internal static partial class VpbLocalDatabase
    {
        internal static string GuessGalleryCategoryFromInternalPath(string internalPath)
        {
            if (string.IsNullOrEmpty(internalPath)) return null;
            string p = internalPath;
            // Avoid alloc when already forward-slash (VaM internal paths usually are).
            if (p.IndexOf('\\') >= 0)
                p = p.Replace('\\', '/');

            if (p.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return "Scenes";

            if (p.StartsWith("Saves/SubScene/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("Saves/subscene/", StringComparison.OrdinalIgnoreCase))
                return "SubScenes";

            if (p.StartsWith("Custom/Scripts/", StringComparison.OrdinalIgnoreCase))
            {
                if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".cslist", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    return "Plugins";
            }

            if (p.StartsWith("Custom/Clothing/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".vam", StringComparison.OrdinalIgnoreCase))
                return "Clothing";

            if (p.StartsWith("Custom/Hair/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".vam", StringComparison.OrdinalIgnoreCase))
                return "Hair";

            if (p.StartsWith("Custom/Atom/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return null;
        }
    }
}
