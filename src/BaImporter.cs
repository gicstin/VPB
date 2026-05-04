using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using Valve.Newtonsoft.Json;

namespace VPB
{
	/// <summary>
	/// One-time import from BrowserAssist to VPB. Owns all BA file access.
	/// </summary>
	internal static class BaImporter
	{
		// BA paths (relative to VaM root)
		private const string BaRelativeDataDir = @"Saves\PluginData\JayJayWon\BrowserAssist";
		private const string BaSettingsFileName = "BASettings.cfg";
		private const string BaUserDataSubfolder = "VARResourcesUserData";
		private const string BaUserDataExt = ".userData";

		// VPB output paths (relative to VaM root)
		private const string ManifestRelPath = @"Saves\PluginData\VPB\ba_migration_manifest.json";
		private const string LogRelPath = @"Saves\PluginData\VPB\ba_migration_log.yaml";

		public struct BaMigrationResult
		{
			public int TagRowsImported;
			public int PackagesTagged;
			public int HideMarkersWritten;
			public int ItemsSkipped;
			public bool Success;
			public string Error;
		}

		// --- Internal data types ---

		private struct BaResourceEntry
		{
			public string CreatorName;
			public string PackageName;
			public string InternalPath; // forward-slash, version-independent
			public List<string> UserDefinedTags;
		}

		[Serializable]
		private class BaMigrationManifest
		{
			[JsonProperty("timestamp")] public string Timestamp;
			[JsonProperty("version")] public int Version = 1;
			[JsonProperty("importedTags")] public List<ManifestTagEntry> ImportedTags = new List<ManifestTagEntry>();
			[JsonProperty("createdHideMarkers")] public List<ManifestHideEntry> CreatedHideMarkers = new List<ManifestHideEntry>();
		}

		[Serializable]
		private class ManifestTagEntry
		{
			[JsonProperty("category")] public string Category;
			[JsonProperty("pkgUid")] public string PkgUid;
			[JsonProperty("internalPath")] public string InternalPath;
			[JsonProperty("tags")] public string[] Tags;
		}

		[Serializable]
		private class ManifestHideEntry
		{
			[JsonProperty("pkgUid")] public string PkgUid;
		}

		// --- Public API ---

		public static bool TryDetectBaDataDir(out string path)
		{
			path = null;
			try
			{
				string candidate = Path.Combine(Directory.GetCurrentDirectory(), BaRelativeDataDir);
				if (Directory.Exists(candidate)) { path = candidate; return true; }
			}
			catch { }
			return false;
		}

		public static bool MigrationManifestExists()
		{
			try
			{
				return File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ManifestRelPath));
			}
			catch { return false; }
		}

		private static string GetAbsPath(string relPath)
		{
			return Path.Combine(Directory.GetCurrentDirectory(), relPath);
		}

		/// <summary>
		/// Reads BASettings.cfg and unions all tag names that appear in any resource type's
		/// hiddenTags list. These are tags that BA auto-hides resources for.
		/// </summary>
		private static HashSet<string> ParseAutoHideTags(string baDataDir)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string cfgPath = Path.Combine(baDataDir, BaSettingsFileName);
			if (!File.Exists(cfgPath)) return result;
			try
			{
				string json = File.ReadAllText(cfgPath);
				JSONNode root = JSON.Parse(json);
				if (root == null) return result;
				CollectHiddenTagsRecursive(root, result, depth: 0);
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB BA] ParseAutoHideTags failed: " + ex.Message);
			}
			return result;
		}

		/// <summary>Walks the JSON tree to depth 3 collecting all "hiddenTags" string arrays.</summary>
		private static void CollectHiddenTagsRecursive(JSONNode node, HashSet<string> tags, int depth)
		{
			if (node == null || depth > 3) return;
			JSONNode hiddenTagsNode = node["hiddenTags"];
			if (hiddenTagsNode != null)
			{
				JSONArray arr = hiddenTagsNode.AsArray;
				if (arr != null)
				{
					foreach (JSONNode t in arr)
					{
						string name = t?.Value;
						if (!string.IsNullOrEmpty(name)) tags.Add(name);
					}
				}
			}
			JSONClass obj = node.AsObject;
			if (obj != null)
			{
				foreach (KeyValuePair<string, JSONNode> kvp in obj)
					CollectHiddenTagsRecursive(kvp.Value, tags, depth + 1);
			}
		}

		/// <summary>
		/// Parses all *.userData files in VARResourcesUserData/. Returns entries with
		/// only userDefined tags. Skips entries with no tags.
		/// </summary>
		private static List<BaResourceEntry> ParseUserDataFiles(string baDataDir)
		{
			var entries = new List<BaResourceEntry>(256);
			string userDataDir = Path.Combine(baDataDir, BaUserDataSubfolder);
			if (!Directory.Exists(userDataDir)) return entries;

			string[] files;
			try { files = Directory.GetFiles(userDataDir, "*" + BaUserDataExt, SearchOption.TopDirectoryOnly); }
			catch { return entries; }

			foreach (string filePath in files)
			{
				try
				{
					string json = File.ReadAllText(filePath);
					JSONNode root = JSON.Parse(json);
					JSONArray resources = root?["resources"]?.AsArray;
					if (resources == null) continue;

					foreach (JSONNode res in resources)
					{
						string creator = res["creatorName"]?.Value;
						string pkg     = res["packageName"]?.Value;
						string ipath   = res["resourceFullFileName"]?.Value;
						if (string.IsNullOrEmpty(creator) || string.IsNullOrEmpty(pkg) || string.IsNullOrEmpty(ipath))
							continue;

						var userTags = new List<string>(4);
						JSONArray tags = res["Tags"]?.AsArray;
						if (tags != null)
						{
							foreach (JSONNode tag in tags)
							{
								string cat = tag["tagCategory"]?.Value;
								if (!string.Equals(cat, "userDefined", StringComparison.OrdinalIgnoreCase)) continue;
								string name = tag["tagName"]?.Value;
								if (!string.IsNullOrEmpty(name)) userTags.Add(name);
							}
						}

						entries.Add(new BaResourceEntry
						{
							CreatorName = creator,
							PackageName = pkg,
							InternalPath = ipath.Replace('\\', '/'),
							UserDefinedTags = userTags
						});
					}
				}
				catch (Exception ex)
				{
					LogUtil.LogWarning("[VPB BA] ParseUserDataFiles: skipping " + Path.GetFileName(filePath) + " — " + ex.Message);
				}
			}
			return entries;
		}

		/// <summary>
		/// Full import: parse BA data → resolve VPB package UIDs → write tags to SQLite
		/// → write .hide sidecars → write audit YAML → write reversibility manifest.
		/// Returns false only on unrecoverable error; partial results are reflected in <paramref name="result"/>.
		/// </summary>
		public static bool RunImport(string baDataDir, out BaMigrationResult result)
		{
			result = default;
			try
			{
				// Step 1 — parse auto-hide tags from BASettings.cfg
				HashSet<string> autoHideTags = ParseAutoHideTags(baDataDir);

				// Step 2 — parse resource→tag assignments
				List<BaResourceEntry> resourceEntries = ParseUserDataFiles(baDataDir);

				// Steps 3–5 — resolve UIDs, collect rows
				var tagRows   = new List<VpbLocalDatabase.GalleryUserTagImportRow>(resourceEntries.Count);
				var hideUids  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var pkgsByUid = FileManager.PackagesByUid; // snapshot; thread-safe read
				if (pkgsByUid == null) { result.Error = "FileManager not ready"; return false; }

				foreach (var entry in resourceEntries)
				{
					string prefix = entry.CreatorName + "." + entry.PackageName + ".";
					bool anyVersionMatched = false;

					foreach (var kvp in pkgsByUid)
					{
						if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
						string uid = kvp.Key;
						anyVersionMatched = true;

						// Auto-hide: if any userDefined tag on this entry is in autoHideTags
						foreach (string t in entry.UserDefinedTags)
						{
							if (autoHideTags.Contains(t)) { hideUids.Add(uid); break; }
						}

						// Tags: need a VPB category for the item
						if (entry.UserDefinedTags.Count > 0)
						{
							if (VpbLocalDatabase.TryGetCategoryForItem(uid, entry.InternalPath, out string category))
							{
								tagRows.Add(new VpbLocalDatabase.GalleryUserTagImportRow
								{
									Category = category,
									PkgUid = uid,
									InternalPath = entry.InternalPath,
									Tags = entry.UserDefinedTags.ToArray()
								});
							}
							else result.ItemsSkipped++;
						}
					}

					if (!anyVersionMatched && entry.UserDefinedTags.Count > 0)
						result.ItemsSkipped++;
				}

				// Step 4 — write tags to SQLite (merge — preserves existing)
				VpbLocalDatabase.BulkMergeGalleryUserTags(tagRows);
				result.TagRowsImported = tagRows.Count;
				var taggedPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var r in tagRows) taggedPkgs.Add(r.PkgUid);
				result.PackagesTagged = taggedPkgs.Count;

				// Step 5 — propagate auto-hide markers
				foreach (string uid in hideUids)
				{
					if (pkgsByUid.TryGetValue(uid, out VarPackage pkg))
					{
						if (PackageHidePrefs.TryEnsureVpbPackageHidden(pkg))
							result.HideMarkersWritten++;
					}
					else
					{
						LogUtil.LogWarning("[VPB BA] Hide: package not in FileManager index: " + uid);
					}
				}

				// Step 6 — write audit YAML
				var itemToTags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
				foreach (var row in tagRows)
				{
					string key = GalleryUserTagYamlBrain.EncodeItemKey(row.Category, row.PkgUid, row.InternalPath);
					if (!itemToTags.TryGetValue(key, out List<string> list))
					{
						list = new List<string>();
						itemToTags[key] = list;
					}
					list.AddRange(row.Tags);
				}
				string yaml = GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags);
				WriteTextSafe(GetAbsPath(LogRelPath), yaml);

				// Step 7 — write reversibility manifest
				var manifest = new BaMigrationManifest
				{
					Timestamp = DateTime.UtcNow.ToString("O"),
					Version   = 1,
					ImportedTags = new List<ManifestTagEntry>(tagRows.Count),
					CreatedHideMarkers = new List<ManifestHideEntry>(hideUids.Count),
				};
				foreach (var row in tagRows)
					manifest.ImportedTags.Add(new ManifestTagEntry { Category = row.Category, PkgUid = row.PkgUid, InternalPath = row.InternalPath, Tags = row.Tags });
				foreach (string uid in hideUids)
					manifest.CreatedHideMarkers.Add(new ManifestHideEntry { PkgUid = uid });

				string manifestJson;
				lock (LogUtil.JsonLock)
					manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
				WriteTextSafe(GetAbsPath(ManifestRelPath), manifestJson);

				LogUtil.Log(string.Format("[VPB BA] Import complete: {0} tag rows, {1} pkgs tagged, {2} hide markers, {3} skipped.",
					result.TagRowsImported, result.PackagesTagged, result.HideMarkersWritten, result.ItemsSkipped));

				result.Success = true;
				return true;
			}
			catch (Exception ex)
			{
				LogUtil.LogError("[VPB BA] RunImport failed: " + ex.Message);
				result.Error = ex.Message;
				return false;
			}
		}

		private static void WriteTextSafe(string absPath, string text)
		{
			try
			{
				string dir = Path.GetDirectoryName(absPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(absPath, text ?? "");
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB BA] WriteTextSafe failed for " + absPath + ": " + ex.Message);
			}
		}

		/// <summary>
		/// Reverses the last import: removes only the specific tag rows recorded in the manifest,
		/// removes .hide sidecars for packages that received them. Deletes the manifest and audit log.
		/// Returns false if manifest does not exist or is unreadable.
		/// </summary>
		public static bool TryResetMigration(out int tagsRemoved, out int hideMarkersRemoved)
		{
			tagsRemoved = hideMarkersRemoved = 0;
			string manifestPath = GetAbsPath(ManifestRelPath);
			if (!File.Exists(manifestPath)) return false;
			try
			{
				string json = File.ReadAllText(manifestPath);
				BaMigrationManifest manifest;
				lock (LogUtil.JsonLock)
					manifest = JsonConvert.DeserializeObject<BaMigrationManifest>(json);
				if (manifest == null) return false;

				// Remove tags
				if (manifest.ImportedTags != null)
				{
					foreach (var entry in manifest.ImportedTags)
					{
						if (string.IsNullOrEmpty(entry.Category) || string.IsNullOrEmpty(entry.PkgUid) ||
							string.IsNullOrEmpty(entry.InternalPath) || entry.Tags == null || entry.Tags.Length == 0)
							continue;
						if (VpbLocalDatabase.RemoveGalleryUserTagsForItem(
								entry.Category, entry.PkgUid, entry.InternalPath, entry.Tags))
							tagsRemoved++;
					}
				}

				// Remove hide markers
				var pkgsByUid = FileManager.PackagesByUid;
				if (pkgsByUid == null) pkgsByUid = new Dictionary<string, VarPackage>();
				if (manifest.CreatedHideMarkers != null)
				{
					foreach (var entry in manifest.CreatedHideMarkers)
					{
						if (string.IsNullOrEmpty(entry.PkgUid)) continue;
						if (pkgsByUid.TryGetValue(entry.PkgUid, out VarPackage pkg))
						{
							if (PackageHidePrefs.TryRemovePackageVarHide(pkg))
								hideMarkersRemoved++;
						}
						else
						{
							LogUtil.LogWarning("[VPB BA] Reset: package not in FileManager for hide removal: " + entry.PkgUid);
						}
					}
				}

				// Delete manifest + log
				try { File.Delete(manifestPath); } catch { }
				try { File.Delete(GetAbsPath(LogRelPath)); } catch { }

				LogUtil.Log(string.Format("[VPB BA] Reset complete: {0} tag entries removed, {1} hide markers removed.",
					tagsRemoved, hideMarkersRemoved));
				return true;
			}
			catch (Exception ex)
			{
				LogUtil.LogError("[VPB BA] TryResetMigration failed: " + ex.Message);
				return false;
			}
		}
	}
}
