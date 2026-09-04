using System;

namespace VPB
{
    internal static class VpbGalleryFamilyDate
    {
        internal static DateTime EarliestKnown(DateTime firstScanned, DateTime fileCreated)
        {
            if (firstScanned == DateTime.MinValue) return fileCreated;
            if (fileCreated == DateTime.MinValue) return firstScanned;
            return firstScanned <= fileCreated ? firstScanned : fileCreated;
        }

        internal static void AccumulateVersion(
            DateTime versionDate,
            int version,
            ref DateTime familyAdded,
            ref int highestVersion,
            ref DateTime familyUpdated)
        {
            if (versionDate != DateTime.MinValue
                && (familyAdded == DateTime.MinValue || versionDate < familyAdded))
                familyAdded = versionDate;

            if (version > highestVersion)
            {
                highestVersion = version;
                familyUpdated = versionDate;
            }
            else if (version == highestVersion
                && versionDate != DateTime.MinValue
                && (familyUpdated == DateTime.MinValue || versionDate < familyUpdated))
            {
                familyUpdated = versionDate;
            }
        }
    }
}
