using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;

namespace VPB.Tests
{
    public sealed class VarFixture
    {
        private readonly Dictionary<string, byte[]> _entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public string Creator { get; private set; }
        public string Name { get; private set; }
        public int Version { get; private set; }

        public string Uid { get { return Creator + "." + Name + "." + Version; } }
        public string FileName { get { return Uid + ".var"; } }

        public VarFixture(string creator, string name, int version)
        {
            Creator = creator;
            Name = name;
            Version = version;
        }

        public VarFixture WithText(string internalPath, string content)
        {
            _entries[internalPath] = new UTF8Encoding(false).GetBytes(content);
            return this;
        }

        public VarFixture WithBytes(string internalPath, byte[] content)
        {
            _entries[internalPath] = content;
            return this;
        }

        public VarFixture WithPlaceholderJpg(string internalPath)
        {
            return WithBytes(internalPath, PlaceholderJpg());
        }

        public VarFixture WithMeta(
            string licenseType = "CC BY",
            string description = "",
            string promotionalLink = "",
            IEnumerable<string> dependencies = null,
            IEnumerable<string> tags = null,
            IEnumerable<string> clothingTags = null,
            IEnumerable<string> hairTags = null)
        {
            var tagList = new List<string>();
            if (tags != null) tagList.AddRange(tags);

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"licenseType\" : \"").Append(Escape(licenseType)).Append("\",\n");
            sb.Append("  \"creatorName\" : \"").Append(Escape(Creator)).Append("\",\n");
            sb.Append("  \"packageName\" : \"").Append(Escape(Name)).Append("\",\n");
            sb.Append("  \"standardReferenceVersionOption\" : \"Latest\",\n");
            sb.Append("  \"scriptReferenceVersionOption\" : \"Exact\",\n");
            sb.Append("  \"description\" : \"").Append(Escape(description)).Append("\",\n");
            sb.Append("  \"promotionalLink\" : \"").Append(Escape(promotionalLink)).Append("\",\n");
            sb.Append("  \"tags\" : \"").Append(Escape(string.Join(", ", tagList.ToArray()))).Append("\",\n");
            sb.Append("  \"programVersion\" : \"1.20.77.13\",\n");
            sb.Append("  \"contentList\" : [\n");
            AppendStringArrayBody(sb, SortedKeys(), 4);
            sb.Append("  ],\n");
            sb.Append("  \"dependencies\" : {\n");
            AppendDependencies(sb, dependencies);
            sb.Append("  },\n");
            sb.Append("  \"customOptions\" : {\n");
            sb.Append("    \"preloadMorphs\" : \"false\"\n");
            sb.Append("  },\n");
            sb.Append("  \"hadReferenceIssues\" : \"false\",\n");
            sb.Append("  \"referenceIssues\" : [],\n");
            sb.Append("  \"packageTags\" : {\n");
            sb.Append("    \"clothing\" : [\n");
            AppendStringArrayBody(sb, clothingTags, 6);
            sb.Append("    ],\n");
            sb.Append("    \"hair\" : [\n");
            AppendStringArrayBody(sb, hairTags, 6);
            sb.Append("    ]\n");
            sb.Append("  }\n");
            sb.Append("}\n");

            return WithText("meta.json", sb.ToString());
        }

        public string WriteTo(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);

            using (FileStream fs = File.Create(path))
            using (var zip = new ZipOutputStream(fs))
            {
                zip.SetLevel(6);
                foreach (KeyValuePair<string, byte[]> entry in _entries)
                {
                    var zipEntry = new ZipEntry(entry.Key);
                    zipEntry.DateTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                    zipEntry.Size = entry.Value.Length;
                    zip.PutNextEntry(zipEntry);
                    zip.Write(entry.Value, 0, entry.Value.Length);
                    zip.CloseEntry();
                }
                zip.Finish();
            }

            return path;
        }

        public VarPackage AsPackage(string relativeDirectory = "AddonPackages")
        {
            string relative = relativeDirectory.Replace('\\', '/').TrimEnd('/') + "/" + FileName;
            return new VarPackage(Uid, relative, new VarPackageGroup(Creator + "." + Name), Creator, Name, Version);
        }

        public static string SceneJson(string title, IEnumerable<string> referencedUids = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"playerHeightAdjust\" : \"0\",\n");
            sb.Append("  \"sceneTitle\" : \"").Append(Escape(title)).Append("\",\n");
            sb.Append("  \"atoms\" : [\n");
            sb.Append("    {\n");
            sb.Append("      \"id\" : \"Person\",\n");
            sb.Append("      \"type\" : \"Person\",\n");
            sb.Append("      \"storables\" : [\n");
            sb.Append("        {\n");
            sb.Append("          \"id\" : \"geometry\",\n");
            sb.Append("          \"useAuxBreastColliders\" : \"true\",\n");
            sb.Append("          \"clothing\" : [\n");

            var refs = new List<string>();
            if (referencedUids != null) refs.AddRange(referencedUids);
            for (int i = 0; i < refs.Count; i++)
            {
                sb.Append("            { \"id\" : \"").Append(Escape(refs[i]))
                  .Append(":/Custom/Clothing/Female/item").Append(i).Append("/item.vam\" }");
                sb.Append(i == refs.Count - 1 ? "\n" : ",\n");
            }

            sb.Append("          ]\n");
            sb.Append("        }\n");
            sb.Append("      ]\n");
            sb.Append("    }\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        public static byte[] PlaceholderJpg()
        {
            return new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
                0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
                0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
                0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
                0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
                0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
                0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
                0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC9, 0x00, 0x0B, 0x08, 0x00, 0x01,
                0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xCC, 0x00, 0x06, 0x00, 0x10,
                0x10, 0x05, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
                0xD2, 0xCF, 0x20, 0xFF, 0xD9,
            };
        }

        private IEnumerable<string> SortedKeys()
        {
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            keys.Remove("meta.json");
            return keys;
        }

        private static void AppendStringArrayBody(StringBuilder sb, IEnumerable<string> values, int indent)
        {
            var list = new List<string>();
            if (values != null) list.AddRange(values);
            string pad = new string(' ', indent);
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(pad).Append('"').Append(Escape(list[i])).Append('"');
                sb.Append(i == list.Count - 1 ? "\n" : ",\n");
            }
        }

        private static void AppendDependencies(StringBuilder sb, IEnumerable<string> dependencies)
        {
            var list = new List<string>();
            if (dependencies != null) list.AddRange(dependencies);
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append("    \"").Append(Escape(list[i])).Append("\" : {\n");
                sb.Append("      \"licenseType\" : \"CC BY\",\n");
                sb.Append("      \"dependencies\" : {}\n");
                sb.Append("    }");
                sb.Append(i == list.Count - 1 ? "\n" : ",\n");
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
