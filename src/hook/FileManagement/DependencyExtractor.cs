using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VPB
{
	/// <summary>
	/// Utility class for extracting dependencies from JSON metadata.
	/// Used by both VAR packages and scene JSON files.
	/// </summary>
	public static class DependencyExtractor
	{
		// Regex pattern for matching dependency references: Author.Name.version
		// Use word boundary before and negative lookahead after to avoid matching file extensions (.dll, .cs, etc)
		private static readonly Regex DepPattern = new Regex(@"\b([A-Za-z_][A-Za-z0-9_]*)\.((?:[A-Za-z0-9_]+\.)*[A-Za-z0-9_]+)\.(latest|min\d+|\d+(?:\.\d+)?)(?![a-zA-Z])", RegexOptions.Compiled);

		/// <summary>
		/// Recursively walks every string value in a JSONNode and extracts dependency references.
		/// </summary>
		public static void ScanAllStringsForDependencies(JSONNode node, HashSet<string> dependencies)
		{
			if (node == null) return;

			// Try to interpret as object
			var obj = node.AsObject;
			if (obj != null)
			{
				foreach (string key in obj.Keys)
				{
					ScanAllStringsForDependencies(obj[key], dependencies);
				}
				return;
			}

			// Try to interpret as array
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

		/// <summary>
		/// Applies the dep regex to a string and adds all valid matches to the set.
		/// Matches pattern: Author.Name.version (e.g., "MacGruber.Life.latest" or "vs1.vs1_H102.1")
		/// </summary>
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

		/// <summary>
		/// Validates a regex match: both author and name must contain at least one letter.
		/// Version must be "latest", "minN", or start with a digit.
		/// </summary>
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

			// Reject if author or name is a known file extension
			var ext = new[] { "dll", "cs", "exe", "jpg", "png", "json", "var", "bat", "cmd", "sh", "ps1" };
			if (ext.Contains(author, StringComparer.OrdinalIgnoreCase) ||
				ext.Contains(name, StringComparer.OrdinalIgnoreCase))
				return false;

			return true;
		}

		/// <summary>
		/// Fast extraction of dependency patterns from raw text using regex only.
		/// Skips JSON parsing for performance on large scene files.
		/// Uses a timeout to prevent regex backtracking on pathological inputs.
		/// </summary>
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
					// Safety timeout: if regex is taking too long, stop
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

		/// <summary>
		/// Extracts all dependencies from a JSON string.
		/// Fast mode: only checks explicit "dependencies" key (for performance).
		/// Slow mode: recursively scans all strings for embedded dependency patterns.
		/// </summary>
		public static HashSet<string> ExtractDependenciesFromJson(string jsonContent, bool fastModeOnly = true)
		{
			HashSet<string> dependencies = new HashSet<string>();

			try
			{
				JSONNode root = JSON.Parse(jsonContent);
				if (root != null)
				{
					// First try explicit "dependencies" key (fast)
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

					// Recursively scan all strings for embedded dependency patterns
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

		/// <summary>
		/// Recursively extracts dependencies from nested dependencies objects.
		/// </summary>
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
