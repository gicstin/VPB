using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VPB
{
	public static class DependencyExtractor
	{
		private static readonly Regex DepPattern = new Regex(@"\b([A-Za-z0-9_][A-Za-z0-9_-]*)\.([A-Za-z0-9_][A-Za-z0-9_-]*)\.(latest|min\d+|\d+)(?![A-Za-z0-9_-])", RegexOptions.Compiled);

		public static void ScanAllStringsForDependencies(JSONNode node, HashSet<string> dependencies)
		{
			if (node == null) return;

			var obj = node.AsObject;
			if (obj != null)
			{
				foreach (string key in obj.Keys)
				{
					ScanAllStringsForDependencies(obj[key], dependencies);
				}
				return;
			}

			var arr = node.AsArray;
			if (arr != null)
			{
				foreach (JSONNode item in arr)
				{
					ScanAllStringsForDependencies(item, dependencies);
				}
				return;
			}

			// Otherwise treat as string value
			string value = node.Value;
			if (!string.IsNullOrEmpty(value))
				ExtractDependenciesWithRegex(value, dependencies);
		}

		public static void ExtractDependenciesWithRegex(string value, HashSet<string> dependencies)
		{
			if (string.IsNullOrEmpty(value))
				return;

			try
			{
				foreach (Match m in DepPattern.Matches(value))
				{
					var author = m.Groups[1].Value;
					var name = m.Groups[2].Value;
					var version = m.Groups[3].Value;

					if (IsValidDepMatch(author, name, version))
						dependencies.Add($"{author}.{name}.{version}");
				}
			}
			catch { }
		}

		/// <summary>Validates a regex match: both author and name must contain at least one letter.</summary>
		public static bool IsValidDepMatch(string author, string name, string version)
		{
			// Version must be "latest", "minN", or start with a digit
			var v = version.ToLowerInvariant();
			if (v != "latest" &&
				!Regex.IsMatch(v, @"^min\d+$") &&
				!char.IsDigit(version[0]))
				return false;

			// Author and name must each contain at least one letter
			if (!author.Any(char.IsLetter) || !name.Any(char.IsLetter))
				return false;

			var ext = new[] { "dll", "cs", "exe", "jpg", "png", "json", "var", "bat", "cmd", "sh", "ps1" };
			if (ext.Contains(author, StringComparer.OrdinalIgnoreCase) ||
				ext.Contains(name, StringComparer.OrdinalIgnoreCase))
				return false;

			return true;
		}

		/// <summary>Fast extraction of dependency patterns from raw text using regex only.</summary>
		public static HashSet<string> ExtractDependenciesFromRawText(string content)
		{
			HashSet<string> dependencies = new HashSet<string>();
			if (string.IsNullOrEmpty(content)) return dependencies;

			try
			{
				var sw = System.Diagnostics.Stopwatch.StartNew();
				int matchCount = 0;

				foreach (Match m in DepPattern.Matches(content))
				{
					if (sw.ElapsedMilliseconds > 500)
						break;

					var author = m.Groups[1].Value;
					var name = m.Groups[2].Value;
					var version = m.Groups[3].Value;

					if (IsValidDepMatch(author, name, version))
					{
						dependencies.Add($"{author}.{name}.{version}");
						matchCount++;
					}

					// Stop after finding reasonable number of dependencies
					if (matchCount > 100)
						break;
				}
			}
			catch { }

			return dependencies;
		}

		public static HashSet<string> ExtractDependenciesFromFile(string filePath, int maxDependencies = 150, int maxMilliseconds = 1500)
		{
			HashSet<string> dependencies = new HashSet<string>();
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return dependencies;

			try
			{
				var sw = System.Diagnostics.Stopwatch.StartNew();

				// Keep some overlap between chunks to avoid losing matches at boundaries.
				const int overlapChars = 512;
				const int bufferChars = 64 * 1024;
				char[] buffer = new char[bufferChars];
				string tail = "";

				using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				using (var reader = new StreamReader(fs, Encoding.UTF8, true, bufferChars))
				{
					while (!reader.EndOfStream && dependencies.Count < maxDependencies)
					{
						if (sw.ElapsedMilliseconds > maxMilliseconds) break;

						int read = reader.Read(buffer, 0, buffer.Length);
						if (read <= 0) break;

						string chunk = tail + new string(buffer, 0, read);
						ExtractDependenciesWithRegex(chunk, dependencies);

						if (chunk.Length > overlapChars)
							tail = chunk.Substring(chunk.Length - overlapChars, overlapChars);
						else
							tail = chunk;
					}
				}
			}
			catch (Exception ex)
			{
				LogUtil.LogError($"[VPB] DependencyExtractor.ExtractDependenciesFromFile error: {ex}");
			}

			return dependencies;
		}

		public static HashSet<string> ExtractDependenciesFromJson(string jsonContent, bool fastModeOnly = true)
		{
			HashSet<string> dependencies = new HashSet<string>();

			try
			{
				JSONNode root = JSON.Parse(jsonContent);
				if (root != null)
				{
					JSONClass depObj = root["dependencies"].AsObject;
					if (depObj != null)
					{
						foreach (string key in depObj.Keys)
						{
							dependencies.Add(key);
							JSONClass subDepObj = depObj[key].AsObject;
							if (subDepObj != null)
								ExtractDependenciesFromDependenciesObject(subDepObj, dependencies);
						}
					}

					if (!fastModeOnly)
					{
						ScanAllStringsForDependencies(root, dependencies);
					}
				}
			}
			catch (Exception ex)
			{
				LogUtil.LogError($"[VPB] DependencyExtractor error: {ex}");
			}

			return dependencies;
		}

		private static void ExtractDependenciesFromDependenciesObject(JSONClass jc, HashSet<string> depends)
		{
			foreach (string key in jc.Keys)
			{
				depends.Add(key);
				JSONClass subObj = jc[key].AsObject;
				if (subObj != null)
					ExtractDependenciesFromDependenciesObject(subObj, depends);
			}
		}
	}
}
