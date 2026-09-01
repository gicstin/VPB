using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
	public static class PackageHidePrefs
	{
		private static Dictionary<string, int> s_sceneJsonCountByUid;

		private static volatile bool s_fanoutCollapseSettled;

		public static void InvalidateHideMarkerCache()
		{
			VpbHideIndex.Invalidate();
		}

		public static void InvalidateSceneJsonCountCache()
		{
			s_sceneJsonCountByUid = null;
		}

		public static void RebuildHideMarkerCache()
		{
			InvalidateHideMarkerCache();
			VpbHideIndex.EnsureBuilt();
		}

		public static string GetAddonPackagesFilePrefsDir()
		{
			return VpbHideIndex.PrefsDirFullPath;
		}

		public static string TryGetPackageUidForEntry(FileEntry f)
		{
			if (f == null) return null;

			if (f is VarFileEntry vfe)
			{
				string u = vfe.GetRowPackageUid();
				if (!string.IsNullOrEmpty(u)) return u;
			}

			if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
				return ple.Package.Uid;

			if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
				return mp.RequestedUid;

			if (f is SystemFileEntry sfe && sfe.isVar && sfe.package != null && !string.IsNullOrEmpty(sfe.package.Uid))
				return sfe.package.Uid;

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

		private static string ResolveVarRelPathForUid(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return null;
			try
			{
				VarPackage pkg = FileManager.GetPackage(uid, false);
				if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
					return pkg.Path.Replace('\\', '/');
			}
			catch { }
			try
			{
				VarPackage pkg = FileManager.GetPackageForDependency(uid, false);
				if (pkg != null && !string.IsNullOrEmpty(pkg.Path))
					return pkg.Path.Replace('\\', '/');
			}
			catch { }
			return "AddonPackages/" + uid + ".var";
		}

		private static string TryGetVarInternalPath(FileEntry f)
		{
			var vfe = f as VarFileEntry;
			if (vfe == null) return null;
			string ip = vfe.InternalPath;
			return string.IsNullOrEmpty(ip) ? null : ip.Replace('\\', '/');
		}

		public static bool IsPackageStandInRow(FileEntry f)
		{
			if (f is PackageListEntry || f is MissingPackageListEntry) return true;
			if (f is SystemFileEntry sfe && sfe.isVar) return true;
			string ip = TryGetVarInternalPath(f);
			return ip != null && string.Equals(ip, "meta.json", StringComparison.OrdinalIgnoreCase);
		}

		public static bool IsVarSceneJsonEntry(VarFileEntry vfe)
		{
			if (vfe == null) return false;
			string path = vfe.InternalPath;
			if (string.IsNullOrEmpty(path)) return false;
			path = path.Replace('\\', '/');
			return path.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
			       && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
		}

		public static int CountSceneJsonInPackage(VarPackage pkg)
		{
			if (pkg == null) return 0;
			string uid = pkg.Uid;
			if (!string.IsNullOrEmpty(uid) && s_sceneJsonCountByUid != null)
			{
				int cached;
				if (s_sceneJsonCountByUid.TryGetValue(uid, out cached))
					return cached;
			}

			int count = 0;
			try
			{
				if (pkg.FileEntries != null)
				{
					foreach (var entry in pkg.FileEntries)
					{
						if (entry?.InternalPath == null) continue;
						string path = entry.InternalPath.Replace('\\', '/');
						if (path.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
						    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
							count++;
					}
				}
			}
			catch { }

			if (!string.IsNullOrEmpty(uid))
			{
				if (s_sceneJsonCountByUid == null)
					s_sceneJsonCountByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				s_sceneJsonCountByUid[uid] = count;
			}
			return count;
		}

		public static bool ShouldUseSceneFileStemLabel(VarFileEntry vfe)
		{
			return vfe != null;
		}

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

		public static bool IsPackageVarHidden(FileEntry entry)
		{
			if (entry == null) return false;
			string uid = null;
			try { uid = TryGetPackageUidForEntry(entry); }
			catch { uid = null; }
			return VpbHideIndex.IsPackageHidden(uid);
		}

		public static bool IsVarItemHidden(FileEntry entry)
		{
			var vfe = entry as VarFileEntry;
			if (vfe == null) return false;
			return VpbHideIndex.IsItemHiddenByEntryUid(vfe.Uid);
		}

		public static bool IsVarPackageHiddenByUid(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return false;
			if (GalleryShowsHiddenEntries()) return false;
			return VpbHideIndex.IsPackageHidden(uid);
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
			return IsVarItemHidden(entry) || IsPackageVarHidden(entry);
		}

		public static bool IsExcludedByGalleryHideFilter(FileEntry entry)
		{
			if (entry == null) return false;
			if (GalleryShowsHiddenEntries()) return false;
			try
			{
				if (entry is SystemFileEntry sfe && !sfe.isVar && sfe.IsHidden())
					return true;
			}
			catch { }
			return IsVarItemHidden(entry) || IsPackageVarHidden(entry) || IsLocalSceneJsonHidden(entry);
		}

		private static bool GalleryShowsHiddenEntries()
		{
			try { return VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; }
			catch { return false; }
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
			if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(entry, out string jsonFull, out _, false))
				return false;
			return VpbHideIndex.IsLooseHidden(jsonFull);
		}

		public static bool TryEnsureLocalSceneJsonHidden(FileEntry entry)
		{
			return SetLocalSceneJsonHidden(entry, true);
		}

		public static bool TryRemoveLocalSceneJsonHide(FileEntry entry)
		{
			return SetLocalSceneJsonHidden(entry, false);
		}

		private static bool SetLocalSceneJsonHidden(FileEntry entry, bool hide)
		{
			string entryPath = entry != null ? entry.Path : "(null)";
			try
			{
				if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(entry, out string jsonFull, out _, false))
				{
					LogUtil.LogWarning("[VPB] HidePrefs: could not build local-scene hide path for " + entryPath);
					return false;
				}
				bool ok = VpbHideIndex.SetLooseHidden(jsonFull, hide);
				if (entry is SystemFileEntry sfe) sfe.InvalidateHiddenCache();
				if (!ok)
					LogUtil.LogWarning("[VPB] HidePrefs: local-scene hide marker did not settle for " + jsonFull);
				return ok;
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] HidePrefs: exception " + (hide ? "hiding" : "unhiding") + " local scene " + entryPath + ": " + ex.Message);
				return false;
			}
		}

		public static bool TryEnsureVarItemHidden(FileEntry entry)
		{
			return SetVarItemHidden(entry, true);
		}

		public static bool TryRemoveVarItemHide(FileEntry entry)
		{
			return SetVarItemHidden(entry, false);
		}

		private static void AbandonFanoutCollapseAfterUserItemHide()
		{
			if (s_fanoutCollapseSettled) return;
			try { TryCollapseLegacyFannedOutPackageHides(); }
			catch { }
			if (s_fanoutCollapseSettled) return;
			try { VpbLocalDatabase.MarkHideFanoutCollapseDone(); }
			catch { }
			s_fanoutCollapseSettled = true;
		}

		private static bool SetVarItemHidden(FileEntry entry, bool hide)
		{
			string entryPath = entry != null ? entry.Path : "(null)";
			if (hide) AbandonFanoutCollapseAfterUserItemHide();
			try
			{
				string uid = TryGetPackageUidForEntry(entry);
				string internalPath = TryGetVarInternalPath(entry);
				if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath))
				{
					LogUtil.LogWarning("[VPB] HidePrefs: could not resolve VAR sub-item for " + entryPath);
					return false;
				}
				bool ok = VpbHideIndex.SetItemHidden(uid, internalPath, hide);
				if (!ok)
					LogUtil.LogWarning("[VPB] HidePrefs: item hide marker did not settle for " + uid + ":/" + internalPath);
				return ok;
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] HidePrefs: exception " + (hide ? "hiding" : "unhiding") + " item " + entryPath + ": " + ex.Message);
				return false;
			}
		}

		public static bool TryEnsureVpbPackageHidden(FileEntry entry)
		{
			return SetPackageHiddenForEntry(entry, true);
		}

		public static bool TryRemovePackageVarHide(FileEntry entry)
		{
			return SetPackageHiddenForEntry(entry, false);
		}

		private static bool SetPackageHiddenForEntry(FileEntry entry, bool hide)
		{
			string entryPath = entry != null ? entry.Path : "(null)";
			try
			{
				string uid = TryGetPackageUidForEntry(entry);
				if (string.IsNullOrEmpty(uid))
				{
					LogUtil.LogWarning("[VPB] HidePrefs: could not resolve package uid from " + entryPath);
					return false;
				}
				return SetPackageHiddenByUid(uid, ResolveVarRelPathForUid(uid), hide);
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] HidePrefs: exception " + (hide ? "hiding" : "unhiding") + " package " + entryPath + ": " + ex.Message);
				return false;
			}
		}

		public static bool TryEnsureVpbPackageHidden(VarPackage pkg)
		{
			if (pkg == null || string.IsNullOrEmpty(pkg.Uid))
			{
				LogUtil.LogWarning("[VPB] HidePrefs: TryEnsureVpbPackageHidden called with null/empty pkg");
				return false;
			}
			return SetPackageHiddenByUid(pkg.Uid, pkg.Path, true);
		}

		public static bool TryRemovePackageVarHide(VarPackage pkg)
		{
			if (pkg == null || string.IsNullOrEmpty(pkg.Uid))
			{
				LogUtil.LogWarning("[VPB] HidePrefs: TryRemovePackageVarHide called with null/empty pkg");
				return false;
			}
			return SetPackageHiddenByUid(pkg.Uid, pkg.Path, false);
		}

		private static bool SetPackageHiddenByUid(string uid, string varRelPath, bool hide)
		{
			try
			{
				bool ok = VpbHideIndex.SetPackageHidden(uid, varRelPath, hide);
				if (!ok)
					LogUtil.LogWarning("[VPB] HidePrefs: package hide marker did not settle for " + uid);
				return ok;
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] HidePrefs: exception " + (hide ? "hiding" : "unhiding") + " package " + uid + ": " + ex.Message);
				return false;
			}
		}

		public static int TryCollapseLegacyFannedOutPackageHides()
		{
			if (s_fanoutCollapseSettled) return 0;
			try
			{
				if (VpbLocalDatabase.IsHideFanoutCollapseDone())
				{
					s_fanoutCollapseSettled = true;
					return 0;
				}

				Dictionary<string, VarPackage> byUid = null;
				try { byUid = FileManager.PackagesByUid; }
				catch { byUid = null; }
				if (byUid == null || byUid.Count == 0) return 0;

				VpbHideIndex.EnsureBuilt();

				int collapsed = 0;
				foreach (KeyValuePair<string, VarPackage> kv in byUid)
				{
					VarPackage pkg = kv.Value;
					if (pkg == null) continue;
					string uid = pkg.Uid ?? kv.Key;
					if (string.IsNullOrEmpty(uid)) continue;
					if (VpbHideIndex.IsPackageHidden(uid)) continue;
					if (!VpbHideIndex.PackageHasHiddenItems(uid)) continue;

					var scenes = GetSceneFilesInPackage(pkg);
					if (scenes.Count == 0) continue;

					bool allScenesMarked = true;
					for (int i = 0; i < scenes.Count; i++)
					{
						if (!VpbHideIndex.IsItemHidden(uid, scenes[i])) { allScenesMarked = false; break; }
					}
					if (!allScenesMarked) continue;

					if (!MarkedItemsAreExactlyScenes(pkg, uid, scenes.Count)) continue;

					VpbHideIndex.ClearAllItemMarkersForPackage(uid);
					if (VpbHideIndex.SetPackageHidden(uid, pkg.Path, true)) collapsed++;
				}

				VpbLocalDatabase.MarkHideFanoutCollapseDone();
				s_fanoutCollapseSettled = true;
				if (collapsed > 0)
					LogUtil.Log("[VPB] Hide migration: collapsed fanned-out scene markers into a package marker for " + collapsed + " package(s)");
				return collapsed;
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] Hide migration failed (will retry next launch): " + ex.Message);
				return 0;
			}
		}

		private static bool MarkedItemsAreExactlyScenes(VarPackage pkg, string uid, int sceneCount)
		{
			try
			{
				if (pkg.FileEntries == null) return false;
				foreach (var entry in pkg.FileEntries)
				{
					if (entry?.InternalPath == null) continue;
					string path = entry.InternalPath.Replace('\\', '/');
					bool isScene = path.StartsWith("Saves/scene/", StringComparison.OrdinalIgnoreCase)
					               && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
					if (isScene) continue;
					if (VpbHideIndex.IsItemHidden(uid, path)) return false;
				}
				return sceneCount > 0;
			}
			catch { return false; }
		}
	}
}
