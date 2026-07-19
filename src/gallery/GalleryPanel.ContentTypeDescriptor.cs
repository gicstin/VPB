using System;
using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        private enum SideFilterFieldKind
        {
            None,
            Category,
            Creator,
            UserTags,
            Path,
            History,
            SettingsCanonical,
            RemoveClothing,
            RemoveHair,
            RemoveAtom,
        }

        private struct ContentTypeDescriptorEntry
        {
            public string PlaceholderKey;
            public string PlaceholderFallback;
            public SideFilterFieldKind SideFilterField;
            public bool SuppressSideSearch;
            public bool SuppressSideSort;
        }

        private static readonly Dictionary<ContentType, ContentTypeDescriptorEntry> ContentTypeDescriptors =
            BuildContentTypeDescriptors();

        private static Dictionary<ContentType, ContentTypeDescriptorEntry> BuildContentTypeDescriptors()
        {
            var map = new Dictionary<ContentType, ContentTypeDescriptorEntry>();

            void Add(ContentType type, string placeholderKey, string placeholderFallback,
                SideFilterFieldKind sideFilter = SideFilterFieldKind.None,
                bool suppressSideSearch = false, bool suppressSideSort = false)
            {
                map[type] = new ContentTypeDescriptorEntry
                {
                    PlaceholderKey = placeholderKey,
                    PlaceholderFallback = placeholderFallback,
                    SideFilterField = sideFilter,
                    SuppressSideSearch = suppressSideSearch,
                    SuppressSideSort = suppressSideSort,
                };
            }

            Add(ContentType.Category, "gallery.search.categories", "Filter category list...", SideFilterFieldKind.Category);
            Add(ContentType.Creator, "gallery.search.creators", "Filter creator list...", SideFilterFieldKind.Creator);
            Add(ContentType.UserTags, "gallery.search.user_tags", "Filter tag list...", SideFilterFieldKind.UserTags);
            Add(ContentType.UserTagsApplied, "gallery.search.user_tags_applied", "Filter applied tags...");
            Add(ContentType.Path, "gallery.search.paths", "Filter path list...", SideFilterFieldKind.Path);
            Add(ContentType.Tags, "gallery.search.tags", "Filter tags...");
            Add(ContentType.RemoveClothing, "gallery.search.clothing", "Filter Clothing...", SideFilterFieldKind.RemoveClothing, suppressSideSort: true);
            Add(ContentType.RemoveHair, "gallery.search.hair", "Filter Hair...", SideFilterFieldKind.RemoveHair, suppressSideSort: true);
            Add(ContentType.RemoveAtom, "gallery.search.atoms", "Filter Atoms...", SideFilterFieldKind.RemoveAtom, suppressSideSort: true);
            Add(ContentType.Target, "gallery.search.target", "Filter Targets...");
            Add(ContentType.CleanupCategories, "gallery.search.cleanup", "Filter Cleanup Categories...",
                suppressSideSearch: true);
            Add(ContentType.CleanupStaleBuckets, "gallery.search.cleanup_stale", "Filter Stale Cache Buckets...");
            Add(ContentType.History, "gallery.search.history_tabs", "Filter history tabs...", SideFilterFieldKind.History, suppressSideSort: true);
            Add(ContentType.Settings, "gallery.search.settings", "Filter settings...", SideFilterFieldKind.SettingsCanonical, suppressSideSort: true);

            return map;
        }

        internal static string GetContentTypePlaceholder(ContentType type)
        {
            ContentTypeDescriptorEntry entry;
            if (ContentTypeDescriptors.TryGetValue(type, out entry))
                return VPBTranslation.T(entry.PlaceholderKey, entry.PlaceholderFallback);
            return VPBTranslation.T("gallery.search.main", "Search name, #tag, OR, badge…");
        }

        internal static bool ContentTypeSuppressesSideSearch(ContentType type)
        {
            ContentTypeDescriptorEntry entry;
            if (ContentTypeDescriptors.TryGetValue(type, out entry))
                return entry.SuppressSideSearch;
            return false;
        }

        internal static bool ContentTypeSuppressesSideSort(ContentType type)
        {
            ContentTypeDescriptorEntry entry;
            if (ContentTypeDescriptors.TryGetValue(type, out entry))
                return entry.SuppressSideSort;
            return false;
        }

        private string GetSideFilterTextForContentType(ContentType type)
        {
            ContentTypeDescriptorEntry entry;
            if (!ContentTypeDescriptors.TryGetValue(type, out entry))
                return "";

            switch (entry.SideFilterField)
            {
                case SideFilterFieldKind.Category: return categoryFilter ?? "";
                case SideFilterFieldKind.Creator: return creatorFilter ?? "";
                case SideFilterFieldKind.UserTags: return userTagFilter ?? "";
                case SideFilterFieldKind.Path: return pathFilter ?? "";
                case SideFilterFieldKind.History: return historyTabFilter ?? "";
                case SideFilterFieldKind.SettingsCanonical: return CanonicalSettingsSideSearchText();
                case SideFilterFieldKind.RemoveClothing: return removeClothingFilter ?? "";
                case SideFilterFieldKind.RemoveHair: return removeHairFilter ?? "";
                case SideFilterFieldKind.RemoveAtom: return removeAtomFilter ?? "";
                default: return "";
            }
        }
    }
}
