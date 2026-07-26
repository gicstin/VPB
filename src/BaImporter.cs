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

		// In-session cache: TryDetectBaDataDir is called from many UI rebuild paths (per-row settings render, tab rebuild)
		// and would otherwise spam the log + hit the disk dozens of times per second while the settings panel is open.
		private static bool _detectCached;
		private static bool _detectCachedResult;
		private static string _detectCachedPath;

		public static bool TryDetectBaDataDir(out string path)
		{
			if (_detectCached)
			{
				path = _detectCachedPath;
				return _detectCachedResult;
			}
			path = null;
			try
			{
				string candidate = Path.Combine(Directory.GetCurrentDirectory(), BaRelativeDataDir);
				LogUtil.LogWarning("[VPB BA] TryDetectBaDataDir: checking '" + candidate + "'");
				if (Directory.Exists(candidate))
				{
					path = candidate;
					_detectCached = true;
					_detectCachedResult = true;
					_detectCachedPath = candidate;
					LogUtil.LogWarning("[VPB BA] TryDetectBaDataDir: found BA data dir");
					return true;
				}
				_detectCached = true;
				_detectCachedResult = false;
				_detectCachedPath = null;
				LogUtil.LogWarning("[VPB BA] TryDetectBaDataDir: BA data dir not present - BA never run or not installed");
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB BA] TryDetectBaDataDir exception: " + ex.Message);
			}
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
			LogUtil.Log("[VPB BA] ParseAutoHideTags: looking for BASettings.cfg at '" + cfgPath + "'");
			if (!File.Exists(cfgPath))
			{
				LogUtil.Log("[VPB BA] ParseAutoHideTags: BASettings.cfg not found - no auto-hide tags");
				return result;
			}
			try
			{
				string json = File.ReadAllText(cfgPath);
				JSONNode root = JSON.Parse(json);
				if (root == null)
				{
					LogUtil.LogWarning("[VPB BA] ParseAutoHideTags: failed to parse BASettings.cfg JSON");
					return result;
				}
				CollectHiddenTagsRecursive(root, result, depth: 0);
				if (result.Count > 0)
					LogUtil.Log("[VPB BA] ParseAutoHideTags: found " + result.Count + " auto-hide tag(s): " + string.Join(", ", new List<string>(result).ToArray()));
				else
					LogUtil.Log("[VPB BA] ParseAutoHideTags: no hiddenTags entries found in BASettings.cfg");
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
		/// True if the resourceFullFileName is a DAZ clothing or hair .vam item - the only resource types where BA
		/// auto-extracts creator metadata tags into the same "User" tagCategory bucket as user-added tags
		/// (see ResourceManifest.cs:6032-6036). Other types (scenes, presets, subscenes, audio, plugins, cua)
		/// don't auto-extract, so all their "User" tags are genuinely user-added.
		/// </summary>
		private static bool IsClothingOrHairItem(string internalPath)
		{
			if (string.IsNullOrEmpty(internalPath)) return false;
			string p = internalPath.Replace('/', '\\');
			if (!p.EndsWith(".vam", StringComparison.OrdinalIgnoreCase)) return false;
			return p.StartsWith("Custom\\Clothing\\", StringComparison.OrdinalIgnoreCase)
				|| p.StartsWith("Custom\\Hair\\",     StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// For a clothing/hair resource entry, returns the set of tag names BA recorded as user-added
		/// (vs auto-extracted from the .vam metadata). Reads the embedded baClothingUserPrefs /
		/// vamClothingUserPrefs / baHairUserPrefs / vamHairUserPrefs.userTags field
		/// (comma-separated; see ResourceManifest.cs:6045-6052, 6385-6394).
		/// Returns null when no userPrefs block exists on the entry - caller should treat that as
		/// "no user-added tags here, drop everything".
		/// </summary>
		private static HashSet<string> ReadClothingHairUserTagsAllowList(JSONNode res)
		{
			HashSet<string> allowed = null;
			string[] keys = { "baClothingUserPrefs", "vamClothingUserPrefs", "baHairUserPrefs", "vamHairUserPrefs" };
			foreach (string key in keys)
			{
				JSONClass prefs = res[key]?.AsObject;
				if (prefs == null) continue;
				string raw = prefs["userTags"]?.Value;
				if (string.IsNullOrEmpty(raw)) { if (allowed == null) allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); continue; }
				if (allowed == null) allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (string t in raw.Split(','))
				{
					string n = t?.Trim();
					if (!string.IsNullOrEmpty(n)) allowed.Add(n);
				}
			}
			return allowed;
		}

		/// <summary>
		/// Parses all *.userData files in VARResourcesUserData/. Returns entries with
		/// only userDefined tags. Skips entries with no tags.
		/// </summary>
		private static List<BaResourceEntry> ParseUserDataFiles(string baDataDir)
		{
			var entries = new List<BaResourceEntry>(256);
			string userDataDir = Path.Combine(baDataDir, BaUserDataSubfolder);
			LogUtil.Log("[VPB BA] ParseUserDataFiles: scanning '" + userDataDir + "'");
			if (!Directory.Exists(userDataDir))
			{
				LogUtil.Log("[VPB BA] ParseUserDataFiles: VARResourcesUserData folder not found - no tags to import");
				return entries;
			}

			string[] files;
			try { files = Directory.GetFiles(userDataDir, "*" + BaUserDataExt, SearchOption.TopDirectoryOnly); }
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB BA] ParseUserDataFiles: failed to list .userData files: " + ex.Message);
				return entries;
			}

			LogUtil.Log("[VPB BA] ParseUserDataFiles: found " + files.Length + " .userData file(s)");
			int totalResources = 0, totalTagged = 0;

			foreach (string filePath in files)
			{
				string fileName = Path.GetFileName(filePath);
				try
				{
					string json = File.ReadAllText(filePath);
					JSONNode root = JSON.Parse(json);
					JSONArray resources = root?["resources"]?.AsArray;
					if (resources == null)
					{
						LogUtil.Log("[VPB BA] ParseUserDataFiles: " + fileName + " - no 'resources' array, skipping");
						continue;
					}

					int fileTagged = 0, fileSkippedNoUserTag = 0, fileSystemTagsDropped = 0, fileCreatorTagsDropped = 0;
					foreach (JSONNode res in resources)
					{
						totalResources++;
						string creator = res["creatorName"]?.Value;
						string pkg     = res["packageName"]?.Value;
						string ipath   = res["resourceFullFileName"]?.Value;
						if (string.IsNullOrEmpty(creator) || string.IsNullOrEmpty(pkg) || string.IsNullOrEmpty(ipath))
							continue;

						var userTagsRaw = new List<string>(4);
						JSONArray tags = res["Tags"]?.AsArray;
						if (tags != null)
						{
							foreach (JSONNode tag in tags)
							{
								string cat = tag["tagCategory"]?.Value;
								// BA uses "User" for user-defined tags; "Body Region"/"Clothing Type" are BA system tags - drop them.
								if (!string.Equals(cat, "User", StringComparison.OrdinalIgnoreCase))
								{
									fileSystemTagsDropped++;
									continue;
								}
								string name = tag["tagName"]?.Value;
								if (!string.IsNullOrEmpty(name)) userTagsRaw.Add(name);
							}
						}

						if (userTagsRaw.Count == 0)
						{
							fileSkippedNoUserTag++;
							continue;
						}

						// For clothing/hair items the .userData "User"-category tag list mixes user-added tags with
						// creator metadata BA auto-extracts from the .vam (ResourceManifest.cs:6035). Discriminator
						// lives in the per-entry baClothingUserPrefs / vamClothingUserPrefs (and hair equivalents)
						// userTags field (ResourceManifest.cs:4204-4227, 6045-6052). Other resource types (scenes,
						// presets, subscenes, plugins, audio, cua) don't auto-extract creator tags so all "User"
						// entries there are genuinely user-added.
						var userTags = userTagsRaw;
						if (IsClothingOrHairItem(ipath))
						{
							var allowed = ReadClothingHairUserTagsAllowList(res);
							if (allowed == null)
							{
								// No userPrefs block on a clothing/hair entry → all "User" tags here are creator metadata.
								fileCreatorTagsDropped += userTagsRaw.Count;
								fileSkippedNoUserTag++;
								continue;
							}
							userTags = new List<string>(userTagsRaw.Count);
							foreach (var t in userTagsRaw)
							{
								if (allowed.Contains(t)) userTags.Add(t);
								else fileCreatorTagsDropped++;
							}
							if (userTags.Count == 0)
							{
								fileSkippedNoUserTag++;
								continue;
							}
						}

						fileTagged++;
						totalTagged++;
						LogUtil.Log("[VPB BA] ParseUserDataFiles: " + creator + "." + pkg + " | '" + ipath + "' | userTags=[" + string.Join(", ", userTags.ToArray()) + "]");

						entries.Add(new BaResourceEntry
						{
							CreatorName = creator,
							PackageName = pkg,
							InternalPath = ipath.Replace('\\', '/'),
							UserDefinedTags = userTags
						});
					}
					LogUtil.LogWarning("[VPB BA] ParseUserDataFiles: " + fileName + " - " + resources.Count + " resources | userTagged=" + fileTagged + " skippedNoUserTag=" + fileSkippedNoUserTag + " systemTagsDropped=" + fileSystemTagsDropped + " creatorTagsDropped=" + fileCreatorTagsDropped);
				}
				catch (Exception ex)
				{
					LogUtil.LogWarning("[VPB BA] ParseUserDataFiles: skipping " + fileName + " - " + ex.Message);
				}
			}
			LogUtil.Log("[VPB BA] ParseUserDataFiles: done | files=" + files.Length + " totalResources=" + totalResources + " withUserTags=" + totalTagged + " totalEntries=" + entries.Count);
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
			var sw = System.Diagnostics.Stopwatch.StartNew();
			LogUtil.LogWarning("[VPB BA] RunImport START | baDataDir='" + baDataDir + "'");
			try
			{
				// If a prior import is recorded, undo it first so re-running cleanly replaces stale rows
				// (older builds had a wrong tagCategory filter and may have left non-User tags in the DB).
				if (MigrationManifestExists())
				{
					int prevTags, prevHides;
					LogUtil.LogWarning("[VPB BA] RunImport: prior manifest detected - running TryResetMigration to clear stale rows before re-import");
					if (TryResetMigration(out prevTags, out prevHides))
						LogUtil.LogWarning("[VPB BA] RunImport: pre-import reset removed " + prevTags + " tag entries, " + prevHides + " hide markers");
					else
						LogUtil.LogWarning("[VPB BA] RunImport: pre-import reset failed or had nothing to do");
				}

				// Step 1 - parse auto-hide tags from BASettings.cfg
				HashSet<string> autoHideTags = ParseAutoHideTags(baDataDir);
				LogUtil.Log("[VPB BA] RunImport step1 done | autoHideTags=" + autoHideTags.Count);

				// Step 2 - parse resource→tag assignments
				List<BaResourceEntry> resourceEntries = ParseUserDataFiles(baDataDir);
				LogUtil.Log("[VPB BA] RunImport step2 done | resourceEntries=" + resourceEntries.Count);

				// Steps 3–5 - resolve UIDs, collect rows
				var tagRows   = new List<VpbLocalDatabase.GalleryUserTagImportRow>(resourceEntries.Count);
				var hideUids  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var pkgsByUid = FileManager.PackagesByUid; // snapshot; thread-safe read
				if (pkgsByUid == null)
				{
					LogUtil.LogWarning("[VPB BA] RunImport: FileManager.PackagesByUid is null - not ready");
					result.Error = "FileManager not ready";
					return false;
				}
				LogUtil.Log("[VPB BA] RunImport step3 start | pkgsByUid=" + pkgsByUid.Count + " entries to resolve=" + resourceEntries.Count);

				int entriesWithTags = 0;
				foreach (var entry in resourceEntries)
				{
					string prefix = entry.CreatorName + "." + entry.PackageName + ".";
					bool anyVersionMatched = false;

					foreach (var kvp in pkgsByUid)
					{
						if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
						string uid = kvp.Key;
						anyVersionMatched = true;

						// Auto-hide: if any user tag on this entry matches a BA auto-hide tag
						foreach (string t in entry.UserDefinedTags)
						{
							if (autoHideTags.Contains(t)) { hideUids.Add(uid); break; }
						}

						// Tags: need a VPB category for the item
						if (entry.UserDefinedTags.Count > 0)
						{
							entriesWithTags++;
							if (VpbLocalDatabase.TryGetCategoryForItem(uid, entry.InternalPath, out string category))
							{
								LogUtil.Log("[VPB BA] RunImport: resolved " + uid + " | path='" + entry.InternalPath + "' | category=" + category + " | tags=[" + string.Join(", ", entry.UserDefinedTags.ToArray()) + "]");
								tagRows.Add(new VpbLocalDatabase.GalleryUserTagImportRow
								{
									Category = category,
									PkgUid = uid,
									InternalPath = entry.InternalPath,
									Tags = entry.UserDefinedTags.ToArray()
								});
							}
							else
							{
								LogUtil.Log("[VPB BA] RunImport: SKIPPED (no VPB category) " + uid + " | path='" + entry.InternalPath + "'");
								result.ItemsSkipped++;
							}
						}
					}

					if (!anyVersionMatched && entry.UserDefinedTags.Count > 0)
					{
						LogUtil.Log("[VPB BA] RunImport: SKIPPED (no matching package) " + entry.CreatorName + "." + entry.PackageName + " | path='" + entry.InternalPath + "'");
						result.ItemsSkipped++;
					}
				}
				LogUtil.LogWarning("[VPB BA] RunImport step3 done | entriesWithTags=" + entriesWithTags + " tagRows=" + tagRows.Count + " hideUids=" + hideUids.Count + " skipped=" + result.ItemsSkipped);

				// Step 4 - write tags to SQLite (merge - preserves existing)
				LogUtil.Log("[VPB BA] RunImport step4: writing " + tagRows.Count + " tag rows to SQLite");
				VpbLocalDatabase.BulkMergeGalleryUserTags(tagRows);
				result.TagRowsImported = tagRows.Count;
				var taggedPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var r in tagRows) taggedPkgs.Add(r.PkgUid);
				result.PackagesTagged = taggedPkgs.Count;
				LogUtil.Log("[VPB BA] RunImport step4 done | tagRows=" + result.TagRowsImported + " pkgsTagged=" + result.PackagesTagged);

				// Step 5 - propagate auto-hide markers
				LogUtil.Log("[VPB BA] RunImport step5: writing " + hideUids.Count + " hide marker(s)");
				foreach (string uid in hideUids)
				{
					if (pkgsByUid.TryGetValue(uid, out VarPackage pkg))
					{
						if (PackageHidePrefs.TryEnsureVpbPackageHidden(pkg))
						{
							result.HideMarkersWritten++;
							LogUtil.Log("[VPB BA] RunImport: hide marker written for " + uid);
						}
					}
					else
					{
						LogUtil.LogWarning("[VPB BA] Hide: package not in FileManager index: " + uid);
					}
				}

				// Step 6 - write audit YAML
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
				string yaml = GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags, null);
				WriteTextSafe(GetAbsPath(LogRelPath), yaml);

				// Step 7 - write reversibility manifest
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

				LogUtil.LogWarning(string.Format("[VPB BA] Import complete in {4}ms: {0} tag rows, {1} pkgs tagged, {2} hide markers, {3} skipped.",
					result.TagRowsImported, result.PackagesTagged, result.HideMarkersWritten, result.ItemsSkipped, sw.ElapsedMilliseconds));

				// Invalidate user-tag caches on every gallery panel so the "Your Tags" sidebar repopulates from SQLite.
				try
				{
					var g = Gallery.singleton;
					if (g != null && g.Panels != null)
					{
						int n = 0;
						foreach (var p in g.Panels)
						{
							if (p == null) continue;
							try { p.InvalidateTags(); n++; } catch { }
						}
						LogUtil.LogWarning("[VPB BA] RunImport: invalidated tag caches on " + n + " panel(s)");
					}
				}
				catch (Exception ex)
				{
					LogUtil.LogWarning("[VPB BA] RunImport: panel cache invalidation failed: " + ex.Message);
				}

				result.Success = true;
				return true;
			}
			catch (Exception ex)
			{
				LogUtil.LogError("[VPB BA] RunImport failed after " + sw.ElapsedMilliseconds + "ms: " + ex.Message);
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
			LogUtil.Log("[VPB BA] TryResetMigration: looking for manifest at '" + manifestPath + "'");
			if (!File.Exists(manifestPath))
			{
				LogUtil.Log("[VPB BA] TryResetMigration: no manifest found - nothing to reset");
				return false;
			}
			try
			{
				string json = File.ReadAllText(manifestPath);
				BaMigrationManifest manifest;
				lock (LogUtil.JsonLock)
					manifest = JsonConvert.DeserializeObject<BaMigrationManifest>(json);
				if (manifest == null)
				{
					LogUtil.LogWarning("[VPB BA] TryResetMigration: manifest deserialized to null");
					return false;
				}
				LogUtil.Log("[VPB BA] TryResetMigration: manifest loaded | timestamp=" + manifest.Timestamp +
					" | importedTags=" + (manifest.ImportedTags?.Count ?? 0) +
					" | hideMarkers=" + (manifest.CreatedHideMarkers?.Count ?? 0));

				// Remove tags
				if (manifest.ImportedTags != null)
				{
					foreach (var entry in manifest.ImportedTags)
					{
						if (string.IsNullOrEmpty(entry.Category) || string.IsNullOrEmpty(entry.PkgUid) ||
							string.IsNullOrEmpty(entry.InternalPath) || entry.Tags == null || entry.Tags.Length == 0)
							continue;
						bool removed = VpbLocalDatabase.RemoveGalleryUserTagsForItem(
							entry.Category, entry.PkgUid, entry.InternalPath, entry.Tags);
						LogUtil.Log("[VPB BA] TryResetMigration: tag remove " + (removed ? "OK" : "noop") +
							" | pkg=" + entry.PkgUid + " | path='" + entry.InternalPath + "' | tags=[" + string.Join(", ", entry.Tags) + "]");
						if (removed) tagsRemoved++;
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
							{
								hideMarkersRemoved++;
								LogUtil.Log("[VPB BA] TryResetMigration: hide marker removed for " + entry.PkgUid);
							}
							else
							{
								LogUtil.Log("[VPB BA] TryResetMigration: hide marker not present for " + entry.PkgUid + " (already removed or never written)");
							}
						}
						else
						{
							LogUtil.LogWarning("[VPB BA] Reset: package not in FileManager for hide removal: " + entry.PkgUid);
						}
					}
				}

				// Delete manifest + log
				try { File.Delete(manifestPath); LogUtil.Log("[VPB BA] TryResetMigration: deleted manifest"); } catch { }
				try { File.Delete(GetAbsPath(LogRelPath)); LogUtil.Log("[VPB BA] TryResetMigration: deleted audit log"); } catch { }

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
