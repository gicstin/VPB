using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
	/// <summary>
	/// Package-level .hide markers under <c>AddonPackagesFilePrefs</c>, same path layout as legacy .fav.
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

		private static bool TryBuildPackageVarHidePath(VarPackage pkg, out string hidePath)
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

		/// <summary>Prefer marker path from <paramref name="entry"/> (no <c>GetFileEntry</c>); disk resolve only when needed.</summary>
		private static bool ComputeIsPackageVarHidden(FileEntry entry)
		{
			string hpFromEntry = null;
			if (TryBuildPackageVarHidePath(entry, out string hpEntry))
			{
				hpFromEntry = hpEntry;
				if (PackageVarHideMarkerExists(hpEntry))
					return true;
			}

			if (TryGetToolboxDiskFileEntryForPackageGalleryRow(entry, out FileEntry diskFe)
			    && TryBuildPackageVarHidePath(diskFe, out string hideDisk))
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
				if (!TryBuildPackageVarHidePath(target, out string hidePath)) return false;
				InvalidateVarHiddenCacheForFileEntry(entry);
				try { InvalidateVarHiddenCacheForFileEntry(target); } catch { }
				try { File.Delete(hidePath + ".vpb"); } catch { }

				if (PackageVarHideMarkerExists(hidePath)) return true;

				string dir = Path.GetDirectoryName(hidePath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(hidePath, string.Empty);
				return PackageVarHideMarkerExists(hidePath);
			}
			catch { return false; }
		}

		public static bool TryEnsureVpbPackageHidden(VarPackage pkg)
		{
			try
			{
				if (!TryBuildPackageVarHidePath(pkg, out string hidePath)) return false;
				try { if (pkg != null && !string.IsNullOrEmpty(pkg.Uid)) UncacheVarHiddenUid(pkg.Uid); } catch { }
				try { File.Delete(hidePath + ".vpb"); } catch { }
				if (PackageVarHideMarkerExists(hidePath)) return true;
				string dir = Path.GetDirectoryName(hidePath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(hidePath, string.Empty);
				return PackageVarHideMarkerExists(hidePath);
			}
			catch { return false; }
		}

		public static bool TryRemovePackageVarHide(FileEntry entry)
		{
			try
			{
				FileEntry target = ResolveToolboxTargetForPackageHideOps(entry);
				if (!TryBuildPackageVarHidePath(target, out string hidePath)) return false;
				InvalidateVarHiddenCacheForFileEntry(entry);
				try { InvalidateVarHiddenCacheForFileEntry(target); } catch { }
				if (!PackageVarHideMarkerExists(hidePath)) return false;
				try { File.Delete(hidePath); } catch { return false; }
				try { File.Delete(hidePath + ".vpb"); } catch { }
				return true;
			}
			catch { return false; }
		}

		public static bool TryRemovePackageVarHide(VarPackage pkg)
		{
			try
			{
				if (!TryBuildPackageVarHidePath(pkg, out string hidePath)) return false;
				try { if (pkg != null && !string.IsNullOrEmpty(pkg.Uid)) UncacheVarHiddenUid(pkg.Uid); } catch { }
				if (!PackageVarHideMarkerExists(hidePath)) return false;
				try { File.Delete(hidePath); } catch { return false; }
				try { File.Delete(hidePath + ".vpb"); } catch { }
				return true;
			}
			catch { return false; }
		}
	}
}
