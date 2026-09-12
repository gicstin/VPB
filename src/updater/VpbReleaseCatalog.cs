using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    public class VpbRelease
    {
        public string Version = "";
        public string Tag = "";
        public string Commit = "";
        public string DateUtc = "";
        public string Notes = "";
        public int Schema;

        public string DatePart
        {
            get
            {
                if (string.IsNullOrEmpty(DateUtc)) return "";
                int t = DateUtc.IndexOf('T');
                return t > 0 ? DateUtc.Substring(0, t) : DateUtc;
            }
        }

        public string DisplayLabel
        {
            get
            {
                string date = DatePart;
                return date.Length == 0 ? Version : Version + "  (" + date + ")";
            }
        }

        public int DaysAgo
        {
            get
            {
                if (string.IsNullOrEmpty(DateUtc)) return -1;
                DateTime parsed;
                if (!DateTime.TryParse(DateUtc,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal
                            | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out parsed))
                    return -1;

                int days = (int)(DateTime.UtcNow.Date - parsed.Date).TotalDays;
                return days < 0 ? 0 : days;
            }
        }
    }

    public class VpbReleaseCatalog
    {
        private static readonly VpbRelease[] Empty = new VpbRelease[0];

        public string MinRollbackVersion = "";
        public int ExcludedBelowMin;

        private VpbRelease[] _releases = Empty;

        public VpbRelease[] Releases { get { return _releases; } }
        public bool IsEmpty { get { return _releases.Length == 0; } }

        public static VpbReleaseCatalog Parse(string json)
        {
            var catalog = new VpbReleaseCatalog();
            if (string.IsNullOrEmpty(json)) return catalog;

            try
            {
                var root = JSON.Parse(json);
                if (root == null) return catalog;

                if (root["MinRollbackVersion"] != null)
                    catalog.MinRollbackVersion = root["MinRollbackVersion"].Value ?? "";
                if (root["ExcludedBelowMin"] != null)
                    catalog.ExcludedBelowMin = root["ExcludedBelowMin"].AsInt;

                var arr = root["Releases"] as JSONArray;
                if (arr == null) return catalog;

                var list = new List<VpbRelease>(arr.Count);
                for (int i = 0; i < arr.Count; i++)
                {
                    var node = arr[i];
                    string version = node["Version"].Value;
                    string tag = node["Tag"].Value;
                    if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(tag)) continue;
                    if (tag.IndexOf('/') >= 0) continue;

                    list.Add(new VpbRelease
                    {
                        Version = version,
                        Tag = tag,
                        Commit = node["Commit"].Value ?? "",
                        DateUtc = node["DateUtc"].Value ?? "",
                        Notes = node["Notes"].Value ?? "",
                        Schema = node["Schema"].AsInt
                    });
                }

                catalog._releases = list.ToArray();
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VpbUpdater] Could not parse the release index: " + ex.Message);
            }

            return catalog;
        }

        public VpbRelease Find(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;
            for (int i = 0; i < _releases.Length; i++)
            {
                if (string.Equals(_releases[i].Version, version, StringComparison.Ordinal))
                    return _releases[i];
            }
            return null;
        }

        public VpbRelease Latest { get { return _releases.Length > 0 ? _releases[0] : null; } }

        private int IndexOfVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return -1;
            for (int i = 0; i < _releases.Length; i++)
            {
                if (string.Equals(_releases[i].Version, version, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        public bool TryCompareByShipOrder(string a, string b, out int result)
        {
            result = 0;
            int ia = IndexOfVersion(a);
            int ib = IndexOfVersion(b);
            if (ia < 0 || ib < 0) return false;
            if (ia == ib) return true;
            result = ia > ib ? -1 : 1;
            return true;
        }

        public static bool IsOlderThan(VpbReleaseCatalog catalog, string candidate, string current)
        {
            int order;
            if (catalog != null && catalog.TryCompareByShipOrder(candidate, current, out order))
                return order < 0;
            return CompareVersions(candidate, current) < 0;
        }

        public bool IsBelowFloor(string version)
        {
            if (string.IsNullOrEmpty(MinRollbackVersion) || string.IsNullOrEmpty(version)) return false;
            return CompareVersions(version, MinRollbackVersion) < 0;
        }

        public static int CompareVersions(string a, string b)
        {
            if (a == b) return 0;
            if (string.IsNullOrEmpty(a)) return -1;
            if (string.IsNullOrEmpty(b)) return 1;

            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int len = pa.Length > pb.Length ? pa.Length : pb.Length;

            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? ParsePart(pa[i]) : 0;
                int vb = i < pb.Length ? ParsePart(pb[i]) : 0;
                if (va != vb) return va < vb ? -1 : 1;
            }
            return 0;
        }

        private static int ParsePart(string s)
        {
            int value = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                value = value * 10 + (c - '0');
            }
            return value;
        }
    }
}
