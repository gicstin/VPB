using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB.src.util
{
    internal static class CslistParser
    {
        public static List<string> ParseReferencedCsPaths(Stream cslistStream, string rootForRelative)
        {
            var results = new List<string>(8);
            if (cslistStream == null) return results;

            string root = (rootForRelative ?? string.Empty).Replace('\\', '/');
            if (root.EndsWith("/", StringComparison.Ordinal)) root = root.Substring(0, root.Length - 1);

            try
            {
                // No leaveOpen overload on .NET 3.5 StreamReader; outer using disposes safely.
                using (var reader = new StreamReader(cslistStream, Encoding.UTF8, true, 1024))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0) continue;
                        if (trimmed[0] == '#') continue;
                        if (trimmed.Length >= 2 && trimmed[0] == '/' && trimmed[1] == '/') continue;

                        string norm = trimmed.Replace('\\', '/');
                        if (!norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                        string resolved;
                        if (norm.Length > 0 && norm[0] == '/')
                        {
                            resolved = norm.Substring(1);
                        }
                        else if (root.Length > 0)
                        {
                            resolved = root + "/" + norm;
                        }
                        else
                        {
                            resolved = norm;
                        }

                        resolved = CollapseRelative(resolved);
                        if (!string.IsNullOrEmpty(resolved))
                            results.Add(resolved.ToLowerInvariant());
                    }
                }
            }
            catch
            {
                results.Clear();
            }
            return results;
        }

        private static string CollapseRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var parts = path.Split('/');
            var stack = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0) continue;
                if (p == ".") continue;
                if (p == "..")
                {
                    if (stack.Count == 0) return null;
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                stack.Add(p);
            }
            return string.Join("/", stack.ToArray());
        }
    }
}
