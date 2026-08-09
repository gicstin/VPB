using System;

namespace VPB
{
    /// <summary>
    /// Gallery tile caption helpers (#90).
    /// Rule: dual package/leaf when scrubbed file stem ≠ package name; otherwise package alone.
    /// Creator lives on grid label right — not repeated in primary. Scroll bind: uid parse + stem scrub — no sibling SQL.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        /// <summary>Path → category without SQL (scroll-safe). Covers common gallery prefixes.</summary>
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
                // Appearance / pose / etc. share Atom presets — need tab hint for accuracy.
                return null;
            }

            return null;
        }
    }
}
