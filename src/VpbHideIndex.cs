using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
	public static class VpbHideIndex
	{
		private const string HideExt = ".hide";
		private const string PrefsDirName = "AddonPackagesFilePrefs";

		private static readonly object s_sync = new object();

		private static string s_prefsDirFull;
		private static volatile bool s_built;
		private static volatile bool s_sqlMirrorFresh;
		private static volatile HashSet<string> s_hiddenPkgUids;
		private static volatile HashSet<string> s_hiddenItemKeys;
		private static Dictionary<string, bool> s_looseByFullPath;

		public static bool SqlMirrorFresh
		{
			get { return s_sqlMirrorFresh; }
		}

		public static string PrefsDirFullPath
		{
			get
			{
				if (s_prefsDirFull != null) return s_prefsDirFull;
				try { s_prefsDirFull = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), PrefsDirName)); }
				catch { s_prefsDirFull = PrefsDirName; }
				return s_prefsDirFull;
			}
		}

		public static void Invalidate()
		{
			lock (s_sync)
			{
				s_built = false;
				s_sqlMirrorFresh = false;
				s_hiddenPkgUids = null;
				s_hiddenItemKeys = null;
				s_looseByFullPath = null;
			}
		}

		public static void EnsureBuilt()
		{
			if (s_built) return;
			HashSet<string> pkgs;
			HashSet<string> items;
			lock (s_sync)
			{
				if (s_built) return;
				BuildLocked();
				pkgs = s_hiddenPkgUids;
				items = s_hiddenItemKeys;
			}
			try { s_sqlMirrorFresh = VpbLocalDatabase.TryReplaceAllHideMarkers(pkgs, items); }
			catch { s_sqlMirrorFresh = false; }
		}

		public static bool IsPackageHidden(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return false;
			EnsureBuilt();
			var set = s_hiddenPkgUids;
			return set != null && set.Contains(uid);
		}

		public static bool IsItemHiddenByEntryUid(string entryUid)
		{
			if (string.IsNullOrEmpty(entryUid)) return false;
			EnsureBuilt();
			var set = s_hiddenItemKeys;
			return set != null && set.Contains(entryUid);
		}

		public static bool IsItemHidden(string uid, string internalPath)
		{
			if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath)) return false;
			return IsItemHiddenByEntryUid(BuildItemKey(uid, internalPath));
		}

		public static bool PackageHasHiddenItems(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return false;
			EnsureBuilt();
			var set = s_hiddenItemKeys;
			if (set == null || set.Count == 0) return false;
			string prefix = uid + ":/";
			foreach (string k in set)
			{
				if (k != null && k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}

		public static bool IsLooseHidden(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath)) return false;
			lock (s_sync)
			{
				if (s_looseByFullPath == null)
					s_looseByFullPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
				bool cached;
				if (s_looseByFullPath.TryGetValue(fullPath, out cached)) return cached;
				bool exists = false;
				try { exists = File.Exists(fullPath + HideExt); }
				catch { exists = false; }
				s_looseByFullPath[fullPath] = exists;
				return exists;
			}
		}

		public static void InvalidateLoose(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath)) return;
			lock (s_sync)
			{
				if (s_looseByFullPath == null) return;
				try { s_looseByFullPath.Remove(fullPath); }
				catch { }
			}
		}

		public static string BuildItemKey(string uid, string internalPath)
		{
			if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath)) return null;
			return uid + ":/" + internalPath.Replace('\\', '/');
		}

		public static string BuildVarEntryFlagPath(string uid, string internalPath, string flagName)
		{
			if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath) || string.IsNullOrEmpty(flagName))
				return null;
			try
			{
				string rel = internalPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
				return Path.Combine(Path.Combine(PrefsDirFullPath, uid), rel + "." + flagName);
			}
			catch { return null; }
		}

		public static string BuildPackageHidePath(string uid, string varRelPath)
		{
			if (string.IsNullOrEmpty(uid)) return null;
			string rel = string.IsNullOrEmpty(varRelPath) ? ("AddonPackages/" + uid + ".var") : varRelPath;
			try
			{
				rel = rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
				return Path.Combine(Path.Combine(PrefsDirFullPath, uid), rel + HideExt);
			}
			catch { return null; }
		}

		public static bool SetItemHidden(string uid, string internalPath, bool hide)
		{
			if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath)) return false;
			string markerPath = BuildVarEntryFlagPath(uid, internalPath, "hide");
			if (string.IsNullOrEmpty(markerPath)) return false;
			EnsureBuilt();
			if (!ApplyMarkerFile(markerPath, hide)) return false;

			string key = BuildItemKey(uid, internalPath);
			lock (s_sync)
			{
				if (s_built && s_hiddenItemKeys != null)
					s_hiddenItemKeys = CopyWith(s_hiddenItemKeys, key, hide);
			}
			MirrorOne(VpbLocalDatabase.HideScopeItem, uid, internalPath.Replace('\\', '/'), hide);
			return true;
		}

		private static HashSet<string> CopyWith(HashSet<string> source, string key, bool add)
		{
			if (source == null || string.IsNullOrEmpty(key)) return source;
			if (add == source.Contains(key)) return source;
			var next = new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
			if (add) next.Add(key);
			else next.Remove(key);
			return next;
		}

		public static bool SetPackageHidden(string uid, string varRelPath, bool hide)
		{
			if (string.IsNullOrEmpty(uid)) return false;
			string markerPath = BuildPackageHidePath(uid, varRelPath);
			if (string.IsNullOrEmpty(markerPath)) return false;
			EnsureBuilt();
			if (!ApplyMarkerFile(markerPath, hide)) return false;

			lock (s_sync)
			{
				if (s_built && s_hiddenPkgUids != null)
					s_hiddenPkgUids = CopyWith(s_hiddenPkgUids, uid, hide);
			}
			MirrorOne(VpbLocalDatabase.HideScopePkg, uid, "", hide);
			return true;
		}

		public static bool SetLooseHidden(string fullPath, bool hide)
		{
			if (string.IsNullOrEmpty(fullPath)) return false;
			if (!ApplyMarkerFile(fullPath + HideExt, hide)) return false;
			lock (s_sync)
			{
				if (s_looseByFullPath == null)
					s_looseByFullPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
				s_looseByFullPath[fullPath] = hide;
			}
			return true;
		}

		public static int ClearAllItemMarkersForPackage(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return 0;
			EnsureBuilt();
			var toRemove = new List<string>();
			lock (s_sync)
			{
				if (s_hiddenItemKeys == null) return 0;
				string prefix = uid + ":/";
				foreach (string k in s_hiddenItemKeys)
				{
					if (k != null && k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						toRemove.Add(k);
				}
			}

			int removed = 0;
			for (int i = 0; i < toRemove.Count; i++)
			{
				string key = toRemove[i];
				int sep = key.IndexOf(":/", StringComparison.Ordinal);
				if (sep <= 0) continue;
				string internalPath = key.Substring(sep + 2);
				if (SetItemHidden(uid, internalPath, false)) removed++;
			}
			return removed;
		}

		private static bool ApplyMarkerFile(string markerPath, bool present)
		{
			try
			{
				bool exists = File.Exists(markerPath);
				if (present)
				{
					if (exists) return true;
					string dir = Path.GetDirectoryName(markerPath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					File.WriteAllText(markerPath, string.Empty);
					return File.Exists(markerPath);
				}
				if (!exists) return true;
				File.Delete(markerPath);
				return !File.Exists(markerPath);
			}
			catch (Exception ex)
			{
				try { LogUtil.LogWarning("[VPB] HideIndex: marker write failed " + markerPath + ": " + ex.Message); }
				catch { }
				return false;
			}
		}

		private static void BuildLocked()
		{
			var pkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			string prefs = PrefsDirFullPath;
			string[] markers = null;
			try
			{
				if (!string.IsNullOrEmpty(prefs) && Directory.Exists(prefs))
					markers = Directory.GetFiles(prefs, "*" + HideExt, SearchOption.AllDirectories);
			}
			catch (Exception ex)
			{
				try { LogUtil.LogWarning("[VPB] HideIndex: prefs scan failed: " + ex.Message); }
				catch { }
				markers = null;
			}

			if (markers != null)
			{
				int prefixLen = prefs.TrimEnd('\\', '/').Length + 1;
				for (int i = 0; i < markers.Length; i++)
				{
					string full = markers[i];
					if (string.IsNullOrEmpty(full) || full.Length <= prefixLen) continue;
					string rel = full.Substring(prefixLen).Replace('\\', '/');
					int slash = rel.IndexOf('/');
					if (slash <= 0) continue;

					string uid = rel.Substring(0, slash);
					int innerLen = rel.Length - slash - 1 - HideExt.Length;
					if (innerLen <= 0) continue;
					string inner = rel.Substring(slash + 1, innerLen);

					if (IsPackageRelativeVarPath(inner)) pkgs.Add(uid);
					else items.Add(uid + ":/" + inner);
				}
			}

			s_hiddenPkgUids = pkgs;
			s_hiddenItemKeys = items;
			s_sqlMirrorFresh = false;
			s_built = true;
		}

		private static bool IsPackageRelativeVarPath(string inner)
		{
			if (string.IsNullOrEmpty(inner)) return false;
			if (!inner.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return false;
			return inner.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)
			       || inner.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase);
		}

		private static void MirrorOne(string scope, string uid, string internalPath, bool present)
		{
			try
			{
				if (!VpbLocalDatabase.TrySetHideMarker(scope, uid, internalPath, present))
					s_sqlMirrorFresh = false;
			}
			catch { s_sqlMirrorFresh = false; }
		}
	}
}
