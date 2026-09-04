using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VPB
{
    internal struct VpbDataPackHeader
    {
        public string PackId;
        public string PackVersion;
        public string BuiltDate;
        public string SourceUrl;
        public string Attribution;
        public string ContentHash;
        public int EntryCount;
        public int FormatVersion;
    }

    internal sealed class VpbDataPackReader : IDisposable
    {
        internal const int MaxSupportedFormatVersion = 3;

        const int ColEntryId = 0;
        const int ColSrcId = 1;
        const int ColSubject = 2;
        const int ColTitle = 3;
        const int ColCreator = 4;
        const int ColCategory = 5;
        const int ColPayType = 6;
        const int ColResourceId = 7;
        const int ColLink = 8;
        const int ColFirstRelease = 9;
        const int ColLastUpdate = 10;
        const int ColVars = 11;
        const int ColTags = 12;
        const int ColIdentKey = 13;
        const int ColTagLine = 14;
        const int ColVersion = 15;
        const int ColDownloads = 16;
        const int ColRatingAvg = 17;
        const int ColRatingCount = 18;
        const int ColDepCount = 19;
        const int ColLicense = 20;
        const int ColMinVam = 21;
        const int ColSizeKb = 22;
        const int ColFlags = 23;
        const int ColCount = 24;

        static readonly string[] ColumnNames =
        {
            "entry_id", "src_id", "subject", "title", "creator", "category", "pay_type",
            "resource_id", "link", "first_release", "last_update", "vars", "tags", "ident_key",
            "tag_line", "version", "downloads", "rating_avg", "rating_count", "dep_count",
            "license", "min_vam", "size_kb", "flags",
        };

        StreamReader _reader;
        readonly int[] _columnAt = new int[ColCount];

        internal VpbDataPackHeader Header;

        internal int EntryId;
        internal string SrcId = "";
        internal string Subject = "";
        internal string Title = "";
        internal string Creator = "";
        internal string Category = "";
        internal string PayType = "";
        internal string ResourceId = "";
        internal string Link = "";
        internal string FirstRelease = "";
        internal string LastUpdate = "";
        internal readonly List<string> IdentKeys = new List<string>(4);
        internal string TagLine = "";
        internal string Version = "";
        internal string License = "";
        internal string MinVam = "";
        internal string RatingAvg = "";
        internal int Downloads;
        internal int RatingCount;
        internal int DepCount;
        internal int SizeKb;
        internal int Flags;

        internal readonly List<string> VarKeys = new List<string>(4);
        internal readonly List<string> VarAliases = new List<string>(4);
        internal readonly List<string> VarVersions = new List<string>(4);
        internal readonly List<int> VarExact = new List<int>(4);

        internal readonly List<string> TagNamespaces = new List<string>(16);
        internal readonly List<string> TagTexts = new List<string>(16);

        internal int MalformedLines;

        internal static string AliasForVarKey(string varKey)
        {
            if (string.IsNullOrEmpty(varKey)) return "";
            return varKey.IndexOf(' ') >= 0 ? varKey.Replace(" ", "") : varKey;
        }

        internal static string NormalizeHubResourceId(string resourceId, string link)
        {
            string fromField = DigitsOnlyHubResourceId(resourceId);
            if (fromField.Length > 0) return fromField;
            return ParseHubResourceIdFromLink(link);
        }

        internal static string ParseHubResourceIdFromLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return "";
            if (link.IndexOf("hub.virtamate.com", StringComparison.OrdinalIgnoreCase) < 0
                && link.IndexOf("/resources/", StringComparison.OrdinalIgnoreCase) < 0)
                return "";

            int end = link.Length;
            while (end > 0)
            {
                char c = link[end - 1];
                if (c != '/' && c != ' ' && c != '\t') break;
                end--;
            }
            if (end <= 0) return "";

            int start = end;
            while (start > 0 && link[start - 1] != '/') start--;

            int dot = -1;
            for (int i = end - 1; i >= start; i--)
            {
                if (link[i] == '.') { dot = i; break; }
            }
            if (dot >= start && dot < end - 1)
            {
                string tail = DigitsOnlyHubResourceId(link.Substring(dot + 1, end - dot - 1));
                if (tail.Length > 0) return tail;
            }
            return DigitsOnlyHubResourceId(link.Substring(start, end - start));
        }

        static string DigitsOnlyHubResourceId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            int len = value.Length;
            int a = 0;
            int b = len;
            while (a < b && (value[a] == ' ' || value[a] == '\t')) a++;
            while (b > a && (value[b - 1] == ' ' || value[b - 1] == '\t')) b--;
            int n = b - a;
            if (n <= 0 || n > 12) return "";
            for (int i = a; i < b; i++)
            {
                char c = value[i];
                if (c < '0' || c > '9') return "";
            }
            return n == len ? value : value.Substring(a, n);
        }

        internal static bool TryReadHeader(string path, out VpbDataPackHeader header)
        {
            header = new VpbDataPackHeader();
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!File.Exists(path)) return false;
                using (var sr = new StreamReader(path))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        if (line[0] != '#') break;
                        ApplyHeaderLine(line, ref header);
                    }
                }
                return !string.IsNullOrEmpty(header.PackId);
            }
            catch
            {
                return false;
            }
        }

        static void ApplyHeaderLine(string line, ref VpbDataPackHeader header)
        {
            int eq = line.IndexOf('=');
            if (eq <= 1) return;
            string key = line.Substring(1, eq - 1).Trim();
            string value = line.Substring(eq + 1).Trim();
            if (key.Length == 0) return;

            if (key == "pack_id") header.PackId = value;
            else if (key == "pack_version") header.PackVersion = value;
            else if (key == "built_date") header.BuiltDate = value;
            else if (key == "source_url") header.SourceUrl = value;
            else if (key == "attribution") header.Attribution = value;
            else if (key == "content_hash") header.ContentHash = value;
            else if (key == "entry_count") header.EntryCount = ParseInt(value);
            else if (key == "pack_format_version") header.FormatVersion = ParseInt(value);
        }

        static int ParseInt(string s)
        {
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }

        internal bool Open(string path, out string error)
        {
            error = null;
            Header = new VpbDataPackHeader();
            for (int i = 0; i < ColCount; i++) _columnAt[i] = -1;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "data pack file not found";
                return false;
            }

            try
            {
                _reader = new StreamReader(path);
            }
            catch (Exception ex)
            {
                error = "cannot open data pack: " + ex.Message;
                return false;
            }

            string line;
            while ((line = _reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    ApplyHeaderLine(line, ref Header);
                    continue;
                }
                if (!MapColumns(line))
                {
                    error = "data pack column row is missing required columns";
                    return false;
                }
                if (Header.FormatVersion > MaxSupportedFormatVersion)
                {
                    error = "data pack format version " + Header.FormatVersion + " is newer than this build supports";
                    return false;
                }
                return true;
            }

            error = "data pack has no column row";
            return false;
        }

        bool MapColumns(string columnRow)
        {
            string[] names = columnRow.Split('\t');
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i] != null ? names[i].Trim() : "";
                if (n.Length == 0) continue;
                for (int c = 0; c < ColCount; c++)
                {
                    if (_columnAt[c] < 0 && string.Equals(n, ColumnNames[c], StringComparison.Ordinal))
                    {
                        _columnAt[c] = i;
                        break;
                    }
                }
            }
            return _columnAt[ColEntryId] >= 0 && _columnAt[ColVars] >= 0 && _columnAt[ColTags] >= 0;
        }

        internal bool ReadEntry()
        {
            if (_reader == null) return false;

            string line;
            while ((line = _reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;

                string[] f = line.Split('\t');
                int id = ParseInt(Field(f, ColEntryId));
                if (id <= 0)
                {
                    MalformedLines++;
                    continue;
                }

                EntryId = id;
                SrcId = Field(f, ColSrcId);
                Subject = Field(f, ColSubject);
                Title = Field(f, ColTitle);
                Creator = Field(f, ColCreator);
                Category = Field(f, ColCategory);
                PayType = Field(f, ColPayType);
                Link = Field(f, ColLink);
                ResourceId = NormalizeHubResourceId(Field(f, ColResourceId), Link);
                if (ResourceId.Length == 0
                    && string.Equals(Header.PackId, VpbDataPackService.HubTagsPackId, StringComparison.OrdinalIgnoreCase))
                    ResourceId = EntryId.ToString(CultureInfo.InvariantCulture);
                FirstRelease = Field(f, ColFirstRelease);
                LastUpdate = Field(f, ColLastUpdate);
                ParseIdentKeys(Field(f, ColIdentKey));
                TagLine = Field(f, ColTagLine);
                Version = Field(f, ColVersion);
                RatingAvg = Field(f, ColRatingAvg);
                License = Field(f, ColLicense);
                MinVam = Field(f, ColMinVam);
                Downloads = ParseInt(Field(f, ColDownloads));
                RatingCount = ParseInt(Field(f, ColRatingCount));
                DepCount = ParseInt(Field(f, ColDepCount));
                SizeKb = ParseInt(Field(f, ColSizeKb));
                Flags = ParseInt(Field(f, ColFlags));

                ParseVars(Field(f, ColVars));
                ParseTags(Field(f, ColTags));
                return true;
            }
            return false;
        }

        string Field(string[] fields, int column)
        {
            int at = _columnAt[column];
            if (at < 0 || at >= fields.Length) return "";
            string v = fields[at];
            return v ?? "";
        }

        void ParseVars(string cell)
        {
            VarKeys.Clear();
            VarAliases.Clear();
            VarVersions.Clear();
            VarExact.Clear();
            if (cell.Length == 0) return;

            string[] parts = cell.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;

                string key = p;
                string version = "";
                int exact = 0;

                int c1 = p.IndexOf(':');
                if (c1 > 0)
                {
                    key = p.Substring(0, c1);
                    int c2 = p.IndexOf(':', c1 + 1);
                    if (c2 > c1)
                    {
                        version = p.Substring(c1 + 1, c2 - c1 - 1);
                        exact = p.Length > c2 + 1 && p[c2 + 1] == '1' ? 1 : 0;
                    }
                    else
                    {
                        version = p.Substring(c1 + 1);
                    }
                }
                if (key.Length == 0) continue;

                VarKeys.Add(key);
                VarAliases.Add(AliasForVarKey(key));
                VarVersions.Add(version);
                VarExact.Add(exact);
            }
        }

        void ParseIdentKeys(string cell)
        {
            IdentKeys.Clear();
            if (string.IsNullOrEmpty(cell)) return;

            if (cell.IndexOf(' ') < 0)
            {
                IdentKeys.Add(cell);
                return;
            }
            string[] parts = cell.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (!string.IsNullOrEmpty(p)) IdentKeys.Add(p);
            }
        }

        void ParseTags(string cell)
        {
            TagNamespaces.Clear();
            TagTexts.Clear();
            if (cell.Length == 0) return;

            string[] parts = cell.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;

                int c = p.IndexOf(':');
                if (c <= 0 || c >= p.Length - 1) continue;

                TagNamespaces.Add(p.Substring(0, c));
                TagTexts.Add(p.Substring(c + 1));
            }
        }

        public void Dispose()
        {
            if (_reader != null)
            {
                try { _reader.Dispose(); } catch { }
                _reader = null;
            }
        }
    }
}
