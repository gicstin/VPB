using System;
using System.Collections.Generic;

namespace VPB
{
    /// <summary>Case-insensitive creator list merge for gallery UI (display name = variant with highest count).</summary>
    internal static class CreatorNameConsolidation
    {
        private struct Group
        {
            public string DisplayName;
            public int DisplayVariantCount;
            public int TotalCount;
        }

        public static void ConsolidateInto(List<CreatorCacheEntry> source, List<CreatorCacheEntry> dest)
        {
            dest.Clear();
            if (source == null || source.Count == 0) return;

            var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                var c = source[i];
                if (string.IsNullOrEmpty(c.Name)) continue;

                Group g;
                if (!groups.TryGetValue(c.Name, out g))
                {
                    groups[c.Name] = new Group
                    {
                        DisplayName = c.Name,
                        DisplayVariantCount = c.Count,
                        TotalCount = c.Count
                    };
                    continue;
                }

                g.TotalCount += c.Count;
                if (c.Count > g.DisplayVariantCount ||
                    (c.Count == g.DisplayVariantCount &&
                     string.Compare(c.Name, g.DisplayName, StringComparison.Ordinal) < 0))
                {
                    g.DisplayName = c.Name;
                    g.DisplayVariantCount = c.Count;
                }
                groups[c.Name] = g;
            }

            if (dest.Capacity < groups.Count) dest.Capacity = groups.Count;
            foreach (var kv in groups)
            {
                Group g = kv.Value;
                dest.Add(new CreatorCacheEntry { Name = g.DisplayName, Count = g.TotalCount });
            }
        }
    }
}
