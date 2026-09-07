using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB.Tests
{
    public sealed class DataPackFixture
    {
        private readonly List<string> _headerLines = new List<string>();
        private readonly List<string> _columns = new List<string>();
        private readonly List<string[]> _rows = new List<string[]>();

        public DataPackFixture(string packId = "testpack", int formatVersion = 3)
        {
            _headerLines.Add("# fixture data pack");
            _headerLines.Add("#pack_id=" + packId);
            _headerLines.Add("#pack_version=1");
            _headerLines.Add("#built_date=2024-06-01");
            _headerLines.Add("#pack_format_version=" + formatVersion);
            _columns.AddRange(new[]
            {
                "entry_id", "src_id", "subject", "title", "creator", "category", "pay_type",
                "resource_id", "link", "first_release", "last_update", "vars", "tags", "ident_key",
                "tag_line", "version", "downloads", "rating_avg", "rating_count", "dep_count",
                "license", "min_vam", "size_kb", "flags",
            });
        }

        public DataPackFixture WithHeaderLine(string key, string value)
        {
            _headerLines.Add("#" + key + "=" + value);
            return this;
        }

        public DataPackFixture WithColumns(params string[] columns)
        {
            _columns.Clear();
            _columns.AddRange(columns);
            return this;
        }

        public DataPackFixture WithRawRow(params string[] cells)
        {
            _rows.Add(cells);
            return this;
        }

        public DataPackFixture WithEntry(
            int entryId,
            string title = "",
            string creator = "",
            string subject = "",
            string category = "",
            string vars = "",
            string tags = "",
            string identKeys = "",
            int downloads = 0)
        {
            var cells = new List<string>();
            foreach (string column in _columns)
            {
                switch (column)
                {
                    case "entry_id": cells.Add(entryId.ToString()); break;
                    case "title": cells.Add(title); break;
                    case "creator": cells.Add(creator); break;
                    case "subject": cells.Add(subject); break;
                    case "category": cells.Add(category); break;
                    case "vars": cells.Add(vars); break;
                    case "tags": cells.Add(tags); break;
                    case "ident_key": cells.Add(identKeys); break;
                    case "downloads": cells.Add(downloads.ToString()); break;
                    default: cells.Add(""); break;
                }
            }
            _rows.Add(cells.ToArray());
            return this;
        }

        public string WriteTo(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            foreach (string line in _headerLines) sb.Append(line).Append('\n');
            sb.Append("#entry_count=").Append(_rows.Count).Append('\n');
            sb.Append(string.Join("\t", _columns.ToArray())).Append('\n');
            foreach (string[] row in _rows) sb.Append(string.Join("\t", row)).Append('\n');

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
    }
}
