using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
	/// <summary>
	/// Package-level .hide markers under AddonPackagesFilePrefs, same path layout as legacy .fav.
	/// Presence is resolved via a prescan of all <c>*.hide</c> files under that tree (see
	/// <see cref="RebuildHideMarkerCache"/>) and <see cref="HashSet{T}.Contains"/> on normalized full paths,
	/// avoiding per-entry <see cref="File.Exists"/> / <see cref="FileManager.FileExists"/> calls.
	/// Invalidate when the game FileManager runs Refresh; gallery post-filter rebuilds before bulk checks.
	/// </summary>
	public static class PackageHidePrefs
	{
		// Cached once — Directory.GetCurrentDirectory() never changes at runtime.
		private static string s_prefsDirCached;

		/// <summary>Full paths of existing .hide sidecars; null after <see cref="InvalidateHideMarkerCache"/>.</summary>
		private static HashSet<string> s_hideMarkerFullPaths;

		public static void InvalidateHideMarkerCache()
		{
			s_hideMarkerFullPaths = null;
		}

		/// <summary>Enumerates all <c>*.hide</c> under AddonPackagesFilePrefs into an in-memory set.</summary>
		public static void RebuildHideMarkerCache()
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				string root = GetAddonPackagesFilePrefsDir();
				if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
				{
					// Prefer SQLite-cached marker enumeration to avoid recursive Directory.GetFiles on every rebuild.
					string sig = "0";
					try { sig = Directory.GetLastWriteTimeUtc(root).ToBinary().ToString(); } catch { sig = "0"; }
					string cacheKey = "markers:hide|root=" + (Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/'));

					var cached = new List<VpbLocalDatabase.SystemFileRow>();
					bool hit = false;
					try { hit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached); } catch { hit = false; }

					string[] paths;
					if (hit && cached.Count > 0)
					{
						paths = new string[cached.Count];
						for (int i = 0; i < cached.Count; i++) paths[i] = cached[i].Path;
					}
					else
					{
						// .NET 3.5: use GetFiles (EnumerateFiles is 4.0+).
						paths = Directory.GetFiles(root, "*.hide", SearchOption.AllDirectories);
						try
						{
							var rows = new List<VpbLocalDatabase.SystemFileRow>(paths.Length);
							for (int i = 0; i < paths.Length; i++)
							{
								string p = paths[i];
								if (string.IsNullOrEmpty(p)) continue;
								var r = new VpbLocalDatabase.SystemFileRow();
								try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
								r.LastWriteBinaryOrInvalid = long.MinValue;
								r.SizeOrInvalid = long.MinValue;
								rows.Add(r);
							}
							if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
						}
						catch { }
					}
					for (int i = 0; i < paths.Length; i++)
					{
						try
						{
							set.Add(Path.GetFullPath(paths[i]));
						}
						catch { }
					}
				}
			}
			catch { }
			s_hideMarkerFullPaths = set;
		}

		private static void EnsureHideMarkerCache()
		{
			if (s_hideMarkerFullPaths == null)
				RebuildHideMarkerCache();
		}

		public static string GetAddonPackagesFilePrefsDir()
		{
			if (s_prefsDirCached != null) return s_prefsDirCached;
			try { s_prefsDirCached = Path.Combine(Directory.GetCurrentDirectory(), "AddonPackagesFilePrefs"); }
			catch { }
			return s_prefsDirCached;
		}

		/// <summary>Builds the absolute path to this entry's .hide sidecar, e.g.
		/// <c>…/AddonPackagesFilePrefs/&lt;uid&gt;/AddonPackages/author.pkg.1.var.hide</c>.</summary>
		public static bool TryBuildPackageVarHidePath(FileEntry entry, out string hidePath)
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

		/// <summary>True when this entry has a .hide sidecar (ignores the "show hidden" toggle).</summary>
		public static bool IsPackageVarHidden(FileEntry entry)
		{
			EnsureHideMarkerCache();
			if (!TryBuildPackageVarHidePath(entry, out string hidePath)) return false;
			try
			{
				return s_hideMarkerFullPaths.Contains(Path.GetFullPath(hidePath));
			}
			catch { return false; }
		}

		/// <summary>True when the gallery should show the "H" badge (package prefs .hide or adjacent local scene.json.hide).</summary>
		public static bool IsGalleryHideBadgeVisible(FileEntry entry)
		{
			if (entry == null) return false;
			return IsPackageVarHidden(entry) || IsLocalSceneJsonHidden(entry);
		}

		/// <summary>True when the gallery should omit this entry (hidden marker present and user is not showing hidden packages).</summary>
		public static bool IsExcludedByGalleryHideFilter(FileEntry entry)
		{
			try
			{
				if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages) return false;
			}
			catch { }
			return IsPackageVarHidden(entry) || IsLocalSceneJsonHidden(entry);
		}

		/// <summary>Adjacent <c>scene.json.hide</c> next to a disk <c>Saves/scene</c> JSON (not AddonPackagesFilePrefs).</summary>
		public static bool TryBuildLocalSceneJsonHidePath(FileEntry entry, out string hidePath)
		{
			hidePath = null;
			if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(entry, out string jsonFull, out _, false))
				return false;
			hidePath = jsonFull + ".hide";
			return true;
		}

		/// <summary>True when a <c>.hide</c> sidecar exists beside this local scene JSON.</summary>
		public static bool IsLocalSceneJsonHidden(FileEntry entry)
		{
			if (!TryBuildLocalSceneJsonHidePath(entry, out string hp)) return false;
			try { return File.Exists(hp); }
			catch { return false; }
		}

		public static bool TryEnsureLocalSceneJsonHidden(FileEntry entry)
		{
			try
			{
				if (!TryBuildLocalSceneJsonHidePath(entry, out string hp)) return false;
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
				if (!File.Exists(hp)) return false;
				File.Delete(hp);
				return true;
			}
			catch { return false; }
		}

		/// <summary>
		/// Ensures a .hide sidecar exists for this package (same marker VaM and other tools use).
		/// If one already exists, succeeds without overwriting.
		/// </summary>
		public static bool TryEnsureVpbPackageHidden(FileEntry entry)
		{
			try
			{
				if (!TryBuildPackageVarHidePath(entry, out string hidePath)) return false;
				try { File.Delete(hidePath + ".vpb"); } catch { }

				EnsureHideMarkerCache();
				string fullHide;
				try { fullHide = Path.GetFullPath(hidePath); }
				catch { return false; }
				if (s_hideMarkerFullPaths.Contains(fullHide)) return true;

				string dir = Path.GetDirectoryName(hidePath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(hidePath, string.Empty);
				try { s_hideMarkerFullPaths.Add(fullHide); } catch { }
				return true;
			}
			catch { return false; }
		}

		/// <summary>
		/// Removes the .hide sidecar for this package (and VPB companion) and updates the marker cache.
		/// </summary>
		public static bool TryRemovePackageVarHide(FileEntry entry)
		{
			try
			{
				if (!TryBuildPackageVarHidePath(entry, out string hidePath)) return false;
				EnsureHideMarkerCache();
				string fullHide;
				try { fullHide = Path.GetFullPath(hidePath); }
				catch { return false; }

				bool onDisk = false;
				try { onDisk = File.Exists(hidePath); } catch { }
				bool inCache = s_hideMarkerFullPaths != null && s_hideMarkerFullPaths.Contains(fullHide);
				if (!onDisk && !inCache) return false;

				if (onDisk)
				{
					try { File.Delete(hidePath); } catch { return false; }
				}
				try { File.Delete(hidePath + ".vpb"); } catch { }
				try { if (s_hideMarkerFullPaths != null) s_hideMarkerFullPaths.Remove(fullHide); } catch { }
				return true;
			}
			catch { return false; }
		}
	}
}
