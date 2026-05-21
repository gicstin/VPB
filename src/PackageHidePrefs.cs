using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VPB
{
	/// <summary>
	/// Package-level .hide markers under <c>AddonPackagesFilePrefs</c>, compatible with native VaM format.
	/// Native VaM format: <c>AddonPackagesFilePrefs/{uid}/Saves/scene/{sceneName}.json.hide</c>
	/// Legacy VPB format: <c>AddonPackagesFilePrefs/{uid}/AddonPackages/{uid}.var.hide</c> (migrated automatically)
	/// Package hide <b>detection</b> matches <see cref="GalleryPanel"/> toolbox: <c>TryGetPackageUidForEntry</c> →
	/// <c>ResolveVarPathForUid</c> → <c>FileManager.GetFileEntry(path, true)</c> → sidecar <c>File.Exists</c>
	/// (same as <c>TryGetTboxResolvablePackageState</c> / hide-unhide buttons).
	/// </summary>
	public static class PackageHidePrefs
	{
		private static string s_prefsDirCached;

		/// <summary>Per-UID result for <see cref="IsPackageVarHidden"/> — hot path when gallery strips hidden rows (thousands of <c>File.Exists</c> + <c>GetFileEntry</c> otherwise).</summary>
		private static Dictionary<string, bool> s_varHiddenByUid;

		/// <summary>Per scene-json .hide path for <see cref="IsLocalSceneJsonHidden"/>.</summary>
		private static Dictionary<string, bool> s_localSceneHiddenByMarkerPath;

		public static void InvalidateHideMarkerCache()
		{
			s_varHiddenByUid = null;
			s_localSceneHiddenByMarkerPath = null;
		}

		public static void RebuildHideMarkerCache()
		{
			InvalidateHideMarkerCache();
		}

		/// <summary>
		/// Batch migrates all legacy VPB hide markers to native VaM-compatible format.
		/// Call this during startup to ensure all existing hide markers are compatible.
		/// </summary>
		public static void MigrateAllLegacyHideMarkers()
		{
			try
			{
				string prefsDir = GetAddonPackagesFilePrefsDir();
				if (string.IsNullOrEmpty(prefsDir) || !Directory.Exists(prefsDir)) return;

				var uidDirs = Directory.GetDirectories(prefsDir);
				int migratedCount = 0;
				int errorCount = 0;

				foreach (var uidDir in uidDirs)
				{
					try
					{
						string uid = Path.GetFileName(uidDir);
						if (string.IsNullOrEmpty(uid)) continue;

						// Look for legacy hide marker: {uidDir}/AddonPackages/{uid}.var.hide
						string addonPackagesDir = Path.Combine(uidDir, "AddonPackages");
						if (!Directory.Exists(addonPackagesDir)) continue;

						var legacyHideFiles = Directory.GetFiles(addonPackagesDir, "*.hide", SearchOption.AllDirectories);
						foreach (var legacyPath in legacyHideFiles)
						{
							try
							{
								// Try to get the package from the path
								string relativePath = legacyPath.Substring(addonPackagesDir.Length).TrimStart('\\', '/');
								// Remove .hide extension to get the original path
								string originalPath = relativePath.Substring(0, relativePath.Length - ".hide".Length);

								// Look up the package
								VarPackage pkg = FileManager.GetPackage(uid, ensureInstalled: false);
								if (pkg == null) continue;

								// Migrate this package
								MigrateLegacyHideToNativeFormat(pkg);
								migratedCount++;
							}
							catch { errorCount++; }
						}
					}
					catch { }
				}

				if (migratedCount > 0 || errorCount > 0)
				{
					LogUtil.Log($"[VPB] Hide marker migration complete: {migratedCount} packages migrated, {errorCount} errors");
				}
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning($"[VPB] Error during hide marker migration: {ex.Message}");
			}
		}

		private static void UncacheVarHiddenUid(string uid)
		{
			if (string.IsNullOrEmpty(uid) || s_varHiddenByUid == null) return;
			try { s_varHiddenByUid.Remove(uid); } catch { }
		}

		private static void InvalidateVarHiddenCacheForFileEntry(FileEntry entry)
		{
			try
			{
				string u = TryGetPackageUidForToolbox(entry);
				if (!string.IsNullOrEmpty(u)) UncacheVarHiddenUid(u);
			}
			catch { }
		}

		private static void UncacheLocalSceneHiddenPath(string hidePath)
		{
			if (string.IsNullOrEmpty(hidePath) || s_localSceneHiddenByMarkerPath == null) return;
			try { s_localSceneHiddenByMarkerPath.Remove(hidePath); } catch { }
		}

		public static string GetAddonPackagesFilePrefsDir()
		{
			if (s_prefsDirCached != null) return s_prefsDirCached;
			try { s_prefsDirCached = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackagesFilePrefs"); }
			catch { }
			return s_prefsDirCached;
		}

		private static bool PackageVarHideMarkerExists(string hidePath)
		{
			if (string.IsNullOrEmpty(hidePath)) return false;
			try { return File.Exists(Path.GetFullPath(hidePath)); }
			catch { return false; }
		}

		/// <summary>Same UID extraction as <c>GalleryPanel.TryGetPackageUidForEntry</c> (hide toolbox).</summary>
		private static string TryGetPackageUidForToolbox(FileEntry f)
		{
			if (f is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
				return vfe.Package.Uid;

			if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
				return ple.Package.Uid;

			if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
				return mp.RequestedUid;

			string p = f.Path ?? "";
			if (string.IsNullOrEmpty(p)) return null;

			int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
			if (internalSep >= 0) p = p.Substring(0, internalSep);

			p = p.Replace('\\', '/');
			if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return null;

			int slash = p.LastIndexOf('/');
			string file = (slash >= 0) ? p.Substring(slash + 1) : p;
			if (file.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
				file = file.Substring(0, file.Length - 4);

			return string.IsNullOrEmpty(file) ? null : file;
		}

		/// <summary>Same as <c>GalleryPanel.Toolbox.DeletePackages.ResolveVarPathForUid</c>.</summary>
		private static string ResolveVarPathForUidToolbox(string uid)
		{
			try
			{
				var pkg = FileManager.GetPackageForDependency(uid, false);
				if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
				{
					string p = pkg.Path.Replace('\\', '/');
					if (File.Exists(p)) return p;

					if (p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
					{
						string mapped = "AddonPackages/" + p.Substring("AllPackages/".Length);
						if (File.Exists(mapped)) return mapped;
					}
				}
			}
			catch { }

			try
			{
				string candidate = "AddonPackages/" + uid + ".var";
				if (File.Exists(candidate)) return candidate;
			}
			catch { }

			return null;
		}

		/// <summary>
		/// Disk <see cref="FileEntry"/> for package row — same resolution as toolbox hide/unhide
		/// (<c>TryGetTboxResolvablePackageState</c> package branch). Returns false for local scenes / non-packages.
		/// </summary>
		private static bool TryGetToolboxDiskFileEntryForPackageGalleryRow(FileEntry f, out FileEntry diskFe)
		{
			diskFe = null;
			if (f == null) return false;
			try
			{
				if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
					return false;
			}
			catch { return false; }

			string uid = TryGetPackageUidForToolbox(f);
			if (string.IsNullOrEmpty(uid)) return false;

			string path = ResolveVarPathForUidToolbox(uid);
			if (string.IsNullOrEmpty(path)) return false;

			try
			{
				diskFe = FileManager.GetFileEntry(path, true);
				return diskFe != null;
			}
			catch
			{
				diskFe = null;
				return false;
			}
		}

		/// <summary>Builds the absolute path to this entry's .hide sidecar (LEGACY VPB format), e.g.
		/// <c>…/AddonPackagesFilePrefs/&lt;uid&gt;/AddonPackages/author.pkg.1.var.hide</c>.
		/// This is the old VPB format, kept for migration purposes.</summary>
		public static bool TryBuildLegacyPackageVarHidePath(FileEntry entry, out string hidePath)
		{
			hidePath = null;
			try
			{
				string prefsDir = GetAddonPackagesFilePrefsDir();
				if (string.IsNullOrEmpty(prefsDir) || entry == null) return false;

				VarPackage pkg = null;
				string sysPath = null;

				if (entry is VarFileEntry vfe && vfe.Package != null)       { pkg = vfe.Package;  sysPath = pkg.Path; }
				else if (entry is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
				                                                              { pkg = sfe.package;  sysPath = pkg.Path; }
				else if (entry is PackageListEntry ple && ple.Package != null) { pkg = ple.Package; sysPath = pkg.Path; }

				if (pkg == null || string.IsNullOrEmpty(sysPath)) return false;
				string uid = pkg.Uid;
				if (string.IsNullOrEmpty(uid)) return false;

				hidePath = Path.Combine(Path.Combine(prefsDir, uid), sysPath.Replace('/', Path.DirectorySeparatorChar) + ".hide");
				return true;
			}
			catch { return false; }
		}

		/// <summary>
		/// Gets all scene files (Saves/scene/*.json) inside a VAR package.
		/// Returns the internal paths normalized with forward slashes.
		/// </summary>
		private static List<string> GetSceneFilesInPackage(VarPackage pkg)
		{
			var scenes = new List<string>();
			if (pkg?.FileEntries == null) return scenes;
			try
			{
				foreach (var entry in pkg.FileEntries)
				{
					if (entry?.InternalPath == null) continue;
					string path = entry.InternalPath.Replace('\\', '/');
					if (path.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
					    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
					{
						scenes.Add(path);
					}
				}
			}
			catch { }
			return scenes;
		}

		/// <summary>
		/// Builds native VaM-compatible hide paths for all scenes in a package.
		/// Native format: <c>AddonPackagesFilePrefs/{uid}/Saves/scene/{sceneName}.json.hide</c>
		/// </summary>
		private static List<string> BuildNativeCompatibleHidePaths(VarPackage pkg)
		{
			var paths = new List<string>();
			if (pkg == null) return paths;
			try
			{
				string prefsDir = GetAddonPackagesFilePrefsDir();
				if (string.IsNullOrEmpty(prefsDir) || string.IsNullOrEmpty(pkg.Uid)) return paths;

				var scenes = GetSceneFilesInPackage(pkg);
				if (scenes.Count == 0) return paths;

				string uidDir = Path.Combine(prefsDir, pkg.Uid);
				foreach (var scenePath in scenes)
				{
					// scenePath is like "Saves/scene/Foo.json"
					// Build: AddonPackagesFilePrefs/{uid}/Saves/scene/Foo.json.hide
					string hidePath = Path.Combine(uidDir, scenePath.Replace('/', Path.DirectorySeparatorChar) + ".hide");
					paths.Add(hidePath);
				}
			}
			catch { }
			return paths;
		}

		/// <summary>
		/// Checks if a package has native VaM-compatible hide markers.
		/// Returns true if ANY scene in the package is hidden.
		/// </summary>
		private static bool NativeCompatibleHideMarkersExist(VarPackage pkg)
		{
			if (pkg == null) return false;
			var hidePaths = BuildNativeCompatibleHidePaths(pkg);
			if (hidePaths.Count == 0) return false;
			return hidePaths.Any(File.Exists);
		}

		/// <summary>
		/// Migrates legacy VPB hide markers to native VaM-compatible format.
		/// Called automatically when checking hide status.
		/// </summary>
		private static void MigrateLegacyHideToNativeFormat(VarPackage pkg)
		{
			if (pkg == null) return;
			try
			{
				// Check for legacy hide marker
				string legacyPath = Path.Combine(
					Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
					pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");

				if (!File.Exists(legacyPath)) return;

				// Found legacy marker - migrate to native format
				var nativePaths = BuildNativeCompatibleHidePaths(pkg);
				if (nativePaths.Count == 0)
				{
					// No scenes to hide, just delete the legacy marker
					try { File.Delete(legacyPath); } catch { }
					return;
				}

				// Create native-compatible hide markers for all scenes
				foreach (var hidePath in nativePaths)
				{
					try
					{
						string dir = Path.GetDirectoryName(hidePath);
						if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
							Directory.CreateDirectory(dir);
						if (!File.Exists(hidePath))
							File.WriteAllText(hidePath, string.Empty);
					}
					catch { }
				}

				// Delete the legacy marker after successful migration
				try { File.Delete(legacyPath); } catch { }
				try { File.Delete(legacyPath + ".vpb"); } catch { }
			}
			catch { }
		}

		/// <summary>Legacy VPB format hide path for a VarPackage. Kept for reference, not used in native-compatible implementation.</summary>
		private static bool TryBuildLegacyPackageVarHidePath(VarPackage pkg, out string hidePath)
		{
			hidePath = null;
			if (pkg == null || string.IsNullOrEmpty(pkg.Uid) || string.IsNullOrEmpty(pkg.Path)) return false;
			try
			{
				string prefsDir = GetAddonPackagesFilePrefsDir();
				if (string.IsNullOrEmpty(prefsDir)) return false;
				hidePath = Path.Combine(Path.Combine(prefsDir, pkg.Uid),
					pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
				return true;
			}
			catch { return false; }
		}

		/// <summary>True when this entry has a .hide sidecar (ignores the "show hidden" toggle).</summary>
		public static bool IsPackageVarHidden(FileEntry entry)
		{
			if (entry == null) return false;

			string uidKey = null;
			try { uidKey = TryGetPackageUidForToolbox(entry); } catch { uidKey = null; }
			if (!string.IsNullOrEmpty(uidKey))
			{
				if (s_varHiddenByUid != null && s_varHiddenByUid.TryGetValue(uidKey, out bool cached))
					return cached;
				bool v = ComputeIsPackageVarHidden(entry);
				if (s_varHiddenByUid == null)
					s_varHiddenByUid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
				s_varHiddenByUid[uidKey] = v;
				return v;
			}

			return ComputeIsPackageVarHidden(entry);
		}

		/// <summary>Prefer marker path from <paramref name="entry"/> (no <c>GetFileEntry</c>); disk resolve only when needed.
		/// Checks native VaM format first, then legacy VPB format with automatic migration.</summary>
		private static bool ComputeIsPackageVarHidden(FileEntry entry)
		{
			// First, get the package to check native-compatible format
			VarPackage pkg = null;
			try
			{
				if (entry is VarFileEntry vfe && vfe.Package != null)
					pkg = vfe.Package;
				else if (entry is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
					pkg = sfe.package;
				else if (entry is PackageListEntry ple && ple.Package != null)
					pkg = ple.Package;
			}
			catch { pkg = null; }

			// Check native VaM-compatible format first (scene-based hide markers)
			if (pkg != null)
			{
				// Check for native format markers
				if (NativeCompatibleHideMarkersExist(pkg))
					return true;

				// Check for legacy marker and migrate if found
				string legacyPath = Path.Combine(
					Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
					pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
				if (File.Exists(legacyPath))
				{
					MigrateLegacyHideToNativeFormat(pkg);
					// After migration, check if any native markers now exist
					return NativeCompatibleHideMarkersExist(pkg);
				}
			}

			// Fallback to legacy path detection (for entries where package resolution failed)
			string hpFromEntry = null;
			if (TryBuildLegacyPackageVarHidePath(entry, out string hpEntry))
			{
				hpFromEntry = hpEntry;
				if (PackageVarHideMarkerExists(hpEntry))
					return true;
			}

			if (TryGetToolboxDiskFileEntryForPackageGalleryRow(entry, out FileEntry diskFe)
			    && TryBuildLegacyPackageVarHidePath(diskFe, out string hideDisk))
			{
				if (!string.IsNullOrEmpty(hpFromEntry)
				    && string.Equals(hpFromEntry, hideDisk, StringComparison.OrdinalIgnoreCase))
					return false;
				if (PackageVarHideMarkerExists(hideDisk))
					return true;
			}
			return false;
		}

		public static bool IsGalleryHideBadgeVisible(FileEntry entry)
		{
			if (entry == null) return false;
			try
			{
				if (entry is SystemFileEntry sfe && !sfe.isVar)
					return sfe.IsHidden();
			}
			catch { }
			if (IsLocalSceneJsonHidden(entry)) return true;
			return IsPackageVarHidden(entry);
		}

		public static bool IsExcludedByGalleryHideFilter(FileEntry entry)
		{
			try
			{
				if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages) return false;
			}
			catch { }
			try
			{
				if (entry is SystemFileEntry sfe && !sfe.isVar && sfe.IsHidden())
					return true;
			}
			catch { }
			return IsPackageVarHidden(entry) || IsLocalSceneJsonHidden(entry);
		}

		public static bool TryBuildLocalSceneJsonHidePath(FileEntry entry, out string hidePath)
		{
			hidePath = null;
			if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(entry, out string jsonFull, out _, false))
				return false;
			hidePath = jsonFull + ".hide";
			return true;
		}

		public static bool IsLocalSceneJsonHidden(FileEntry entry)
		{
			if (!TryBuildLocalSceneJsonHidePath(entry, out string hp)) return false;
			if (s_localSceneHiddenByMarkerPath != null && s_localSceneHiddenByMarkerPath.TryGetValue(hp, out bool cached))
				return cached;
			bool exists = false;
			try { exists = File.Exists(hp); } catch { }
			if (s_localSceneHiddenByMarkerPath == null)
				s_localSceneHiddenByMarkerPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			s_localSceneHiddenByMarkerPath[hp] = exists;
			return exists;
		}

		public static bool TryEnsureLocalSceneJsonHidden(FileEntry entry)
		{
			try
			{
				if (!TryBuildLocalSceneJsonHidePath(entry, out string hp)) return false;
				UncacheLocalSceneHiddenPath(hp);
				if (File.Exists(hp)) return true;
				File.WriteAllText(hp, string.Empty);
				return File.Exists(hp);
			}
			catch { return false; }
		}

		public static bool TryRemoveLocalSceneJsonHide(FileEntry entry)
		{
			try
			{
				if (!TryBuildLocalSceneJsonHidePath(entry, out string hp)) return false;
				UncacheLocalSceneHiddenPath(hp);
				if (!File.Exists(hp)) return false;
				File.Delete(hp);
				return true;
			}
			catch { return false; }
		}

		private static FileEntry ResolveToolboxTargetForPackageHideOps(FileEntry entry)
		{
			if (entry == null) return null;
			if (TryGetToolboxDiskFileEntryForPackageGalleryRow(entry, out FileEntry diskFe))
				return diskFe;
			return entry;
		}

		public static bool TryEnsureVpbPackageHidden(FileEntry entry)
		{
			try
			{
				FileEntry target = ResolveToolboxTargetForPackageHideOps(entry);
				InvalidateVarHiddenCacheForFileEntry(entry);
				try { InvalidateVarHiddenCacheForFileEntry(target); } catch { }

				// Get the package to create native-compatible hide markers
				VarPackage pkg = null;
				try
				{
					if (target is VarFileEntry vfe && vfe.Package != null)
						pkg = vfe.Package;
					else if (target is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
						pkg = sfe.package;
					else if (target is PackageListEntry ple && ple.Package != null)
						pkg = ple.Package;
				}
				catch { pkg = null; }

				if (pkg == null) return false;

				// Clean up legacy hide path if it exists
				try
				{
					string legacyPath = Path.Combine(
						Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
						pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
					File.Delete(legacyPath);
					File.Delete(legacyPath + ".vpb");
				}
				catch { }

				// Create native VaM-compatible hide markers for all scenes
				var hidePaths = BuildNativeCompatibleHidePaths(pkg);
				if (hidePaths.Count == 0) return false;

				bool anyCreated = false;
				foreach (var hidePath in hidePaths)
				{
					try
					{
						if (File.Exists(hidePath))
						{
							anyCreated = true;
							continue;
						}
						string dir = Path.GetDirectoryName(hidePath);
						if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
							Directory.CreateDirectory(dir);
						File.WriteAllText(hidePath, string.Empty);
						if (File.Exists(hidePath))
							anyCreated = true;
					}
					catch { }
				}
				return anyCreated;
			}
			catch { return false; }
		}

		public static bool TryEnsureVpbPackageHidden(VarPackage pkg)
		{
			try
			{
				if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) return false;
				try { UncacheVarHiddenUid(pkg.Uid); } catch { }

				// Clean up legacy hide path if it exists
				try
				{
					string legacyPath = Path.Combine(
						Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
						pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
					File.Delete(legacyPath);
					File.Delete(legacyPath + ".vpb");
				}
				catch { }

				// Create native VaM-compatible hide markers for all scenes
				var hidePaths = BuildNativeCompatibleHidePaths(pkg);
				if (hidePaths.Count == 0) return false;

				bool anyCreated = false;
				foreach (var hidePath in hidePaths)
				{
					try
					{
						if (File.Exists(hidePath))
						{
							anyCreated = true;
							continue;
						}
						string dir = Path.GetDirectoryName(hidePath);
						if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
							Directory.CreateDirectory(dir);
						File.WriteAllText(hidePath, string.Empty);
						if (File.Exists(hidePath))
							anyCreated = true;
					}
					catch { }
				}
				return anyCreated;
			}
			catch { return false; }
		}

		public static bool TryRemovePackageVarHide(FileEntry entry)
		{
			try
			{
				FileEntry target = ResolveToolboxTargetForPackageHideOps(entry);
				InvalidateVarHiddenCacheForFileEntry(entry);
				try { InvalidateVarHiddenCacheForFileEntry(target); } catch { }

				// Get the package to remove native-compatible hide markers
				VarPackage pkg = null;
				try
				{
					if (target is VarFileEntry vfe && vfe.Package != null)
						pkg = vfe.Package;
					else if (target is SystemFileEntry sfe && sfe.isVar && sfe.package != null)
						pkg = sfe.package;
					else if (target is PackageListEntry ple && ple.Package != null)
						pkg = ple.Package;
				}
				catch { pkg = null; }

				if (pkg == null) return false;

				bool anyRemoved = false;

				// Remove legacy hide path if it exists
				try
				{
					string legacyPath = Path.Combine(
						Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
						pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
					if (File.Exists(legacyPath))
					{
						File.Delete(legacyPath);
						anyRemoved = true;
					}
					File.Delete(legacyPath + ".vpb");
				}
				catch { }

				// Remove native VaM-compatible hide markers for all scenes
				var hidePaths = BuildNativeCompatibleHidePaths(pkg);
				foreach (var hidePath in hidePaths)
				{
					try
					{
						if (File.Exists(hidePath))
						{
							File.Delete(hidePath);
							anyRemoved = true;
						}
					}
					catch { }
				}

				return anyRemoved;
			}
			catch { return false; }
		}

		public static bool TryRemovePackageVarHide(VarPackage pkg)
		{
			try
			{
				if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) return false;
				try { UncacheVarHiddenUid(pkg.Uid); } catch { }

				bool anyRemoved = false;

				// Remove legacy hide path if it exists
				try
				{
					string legacyPath = Path.Combine(
						Path.Combine(GetAddonPackagesFilePrefsDir(), pkg.Uid),
						pkg.Path.Replace('/', Path.DirectorySeparatorChar) + ".hide");
					if (File.Exists(legacyPath))
					{
						File.Delete(legacyPath);
						anyRemoved = true;
					}
					File.Delete(legacyPath + ".vpb");
				}
				catch { }

				// Remove native VaM-compatible hide markers for all scenes
				var hidePaths = BuildNativeCompatibleHidePaths(pkg);
				foreach (var hidePath in hidePaths)
				{
					try
					{
						if (File.Exists(hidePath))
						{
							File.Delete(hidePath);
							anyRemoved = true;
						}
					}
					catch { }
				}

				return anyRemoved;
			}
			catch { return false; }
		}
	}
}
