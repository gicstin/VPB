using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VPB.Tests.Static
{
    public static class Repo
    {
        private static string s_root;

        public static string Root
        {
            get
            {
                if (s_root != null) return s_root;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VPB.csproj")))
                    dir = dir.Parent;
                if (dir == null)
                    throw new InvalidOperationException(
                        "Could not locate the repository root (no VPB.csproj found above " + AppContext.BaseDirectory + ").");
                s_root = dir.FullName;
                return s_root;
            }
        }

        public static string Path_(params string[] parts)
        {
            string p = Root;
            foreach (var part in parts) p = System.IO.Path.Combine(p, part);
            return p;
        }

        public static string SrcDir => Path_("src");

        public static IReadOnlyList<string> SourceFilesOnDisk()
        {
            return Directory
                .GetFiles(SrcDir, "*.cs", SearchOption.AllDirectories)
                .Select(ToRepoRelative)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string ToRepoRelative(string absolute)
        {
            string full = System.IO.Path.GetFullPath(absolute);
            string root = Root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path is outside the repository: " + absolute);
            return full.Substring(root.Length).Replace('\\', '/');
        }

        public static IReadOnlyList<string> ReadAllowlist(string fileName)
        {
            string path = Path_("tests", fileName);
            if (!File.Exists(path)) return Array.Empty<string>();
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => l.Replace('\\', '/'))
                .ToList();
        }

        public static string Bullets(IEnumerable<string> items, int max = 40)
        {
            var list = items.ToList();
            var shown = list.Take(max).Select(i => "  - " + i);
            string s = string.Join(Environment.NewLine, shown);
            if (list.Count > max) s += Environment.NewLine + "  ... and " + (list.Count - max) + " more";
            return s;
        }
    }
}
