using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private readonly List<CreatorCacheEntry> _displayCreatorsCache = new List<CreatorCacheEntry>(512);
        private int _displayCreatorsCacheRevision = -1;
        private bool _displayCreatorsCacheConsolidation;

        private bool GalleryConsolidateCreatorNamesEnabled =>
            VPBConfig.Instance != null && VPBConfig.Instance.GalleryConsolidateCreatorNames;

        internal void PushCreatorFilterSqlModeForDatabase()
        {
            VpbLocalDatabase.GalleryCreatorFilterCaseInsensitive = GalleryConsolidateCreatorNamesEnabled;
        }

        private void InvalidateDisplayCreatorsCache()
        {
            _displayCreatorsCacheRevision = -1;
            _titleCreatorVirtViewSig = null;
            _creatorVirtViewSig = null;
        }

        private List<CreatorCacheEntry> GetCreatorsForDisplay()
        {
            if (!creatorsCached || cachedCreators == null) return cachedCreators;

            bool consolidate = GalleryConsolidateCreatorNamesEnabled;
            int rev = creatorSideTabDataRevision;
            if (_displayCreatorsCacheRevision == rev &&
                _displayCreatorsCacheConsolidation == consolidate &&
                _displayCreatorsCache.Count > 0)
                return _displayCreatorsCache;

            _displayCreatorsCacheRevision = rev;
            _displayCreatorsCacheConsolidation = consolidate;
            _displayCreatorsCache.Clear();
            if (!consolidate)
                _displayCreatorsCache.AddRange(cachedCreators);
            else
                CreatorNameConsolidation.ConsolidateInto(cachedCreators, _displayCreatorsCache);
            return _displayCreatorsCache;
        }

        private string CreatorConsolidationSignatureFragment()
        {
            return GalleryConsolidateCreatorNamesEnabled ? "|consolidate" : "";
        }

        /// <summary>Creator filter for SQLite / scans (case folding handled in SQL when consolidation is on).</summary>
        private string GetCreatorFilterForQueries()
        {
            return currentCreator ?? "";
        }

        private string ExpandCreatorFilterForQueries(string pipeSeparated)
        {
            return pipeSeparated ?? "";
        }

        internal static void AddCreatorFilterPartsToSet(string pipeSeparated, HashSet<string> set)
        {
            if (set == null || string.IsNullOrEmpty(pipeSeparated)) return;
            string[] parts = pipeSeparated.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (p.Length > 0) set.Add(p);
            }
        }
    }
}
