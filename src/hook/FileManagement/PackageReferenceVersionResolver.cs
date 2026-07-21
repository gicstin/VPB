using System;
using System.Collections.Generic;

namespace VPB
{
	/// <summary>
	/// Honors meta.json <c>standardReferenceVersionOption</c> / <c>scriptReferenceVersionOption</c>
	/// and user settings when rewriting or resolving versioned package UIDs.
	/// </summary>
	internal static class PackageReferenceVersionResolver
	{
		private static readonly object s_Lock = new object();
		private static readonly Stack<string> s_ReferrerUidStack = new Stack<string>();
		/// <summary>
		/// Sticky referrer for async scene load (NormalizeLoadPath after Load() returns).
		/// Volatile: warm-path reads avoid locking when stack empty.
		/// </summary>
		private static volatile string s_ActiveLoadReferrerUid;

		public static void BeginReferrerContext(string packageUid)
		{
			string uid = NormalizePackageUid(packageUid);
			if (string.IsNullOrEmpty(uid)) return;
			lock (s_Lock)
			{
				s_ReferrerUidStack.Push(uid);
			}
		}

		public static void EndReferrerContext()
		{
			lock (s_Lock)
			{
				if (s_ReferrerUidStack.Count > 0)
					s_ReferrerUidStack.Pop();
			}
		}

		/// <summary>
		/// Sticky referrer for scene/preset load. Preloads meta options so NormalizeLoadPath
		/// does not open zip mid-restore.
		/// </summary>
		public static void SetActiveLoadReferrer(string packageUid)
		{
			string uid = NormalizePackageUid(packageUid);
			s_ActiveLoadReferrerUid = uid;
			if (string.IsNullOrEmpty(uid)) return;
			try
			{
				VarPackage pkg = FileManager.GetPackage(uid, ensureInstalled: false);
				if (pkg != null)
					pkg.TryEnsureReferenceVersionOptions();
			}
			catch { }
		}

		public static void ClearActiveLoadReferrer()
		{
			s_ActiveLoadReferrerUid = null;
		}

		public static string PeekReferrerUid()
		{
			lock (s_Lock)
			{
				if (s_ReferrerUidStack.Count > 0)
					return s_ReferrerUidStack.Peek();
			}
			return s_ActiveLoadReferrerUid;
		}

		static string NormalizePackageUid(string packageUid)
		{
			if (string.IsNullOrEmpty(packageUid)) return null;
			string uid = packageUid.Trim();
			if (string.IsNullOrEmpty(uid)) return null;
			int colon = uid.IndexOf(':');
			if (colon > 0) uid = uid.Substring(0, colon);
			return string.IsNullOrEmpty(uid) ? null : uid;
		}

		/// <summary>
		/// Extract host package UID from a VAR entry path / scene path hint.
		/// </summary>
		public static string TryExtractPackageUid(string pathOrUid)
		{
			if (string.IsNullOrEmpty(pathOrUid)) return null;
			string p = pathOrUid.Trim();
			if (string.IsNullOrEmpty(p)) return null;

			int colon = p.IndexOf(':');
			if (colon > 0)
				return p.Substring(0, colon);

			// Author.Pkg.N.var — accept either slash style without allocating Replace.
			if (p.Length > 4
				&& (p[p.Length - 4] == '.')
				&& (p[p.Length - 3] == 'v' || p[p.Length - 3] == 'V')
				&& (p[p.Length - 2] == 'a' || p[p.Length - 2] == 'A')
				&& (p[p.Length - 1] == 'r' || p[p.Length - 1] == 'R'))
			{
				string name = p;
				int slash = name.LastIndexOf('/');
				int bslash = name.LastIndexOf('\\');
				int sep = slash > bslash ? slash : bslash;
				if (sep >= 0 && sep + 1 < name.Length)
					name = name.Substring(sep + 1);
				if (name.Length > 4)
					name = name.Substring(0, name.Length - 4);
				return string.IsNullOrEmpty(name) ? null : name;
			}

			return null;
		}

		public static bool TryParseOption(string raw, out VarPackage.ReferenceVersionOption option)
		{
			option = VarPackage.ReferenceVersionOption.Latest;
			if (string.IsNullOrEmpty(raw)) return false;
			string s = raw.Trim();
			if (string.Equals(s, "Exact", StringComparison.OrdinalIgnoreCase))
			{
				option = VarPackage.ReferenceVersionOption.Exact;
				return true;
			}
			if (string.Equals(s, "Minimum", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s, "Min", StringComparison.OrdinalIgnoreCase))
			{
				option = VarPackage.ReferenceVersionOption.Minimum;
				return true;
			}
			if (string.Equals(s, "Latest", StringComparison.OrdinalIgnoreCase))
			{
				option = VarPackage.ReferenceVersionOption.Latest;
				return true;
			}
			return false;
		}

		public static void ApplyFromMetaJson(VarPackage pkg, SimpleJSON.JSONClass meta)
		{
			if (pkg == null || meta == null) return;
			try
			{
				VarPackage.ReferenceVersionOption std;
				if (TryParseOption(SafeJsonString(meta, "standardReferenceVersionOption"), out std))
					pkg.SetStandardReferenceVersionOption(std);
			}
			catch { }
			try
			{
				VarPackage.ReferenceVersionOption script;
				if (TryParseOption(SafeJsonString(meta, "scriptReferenceVersionOption"), out script))
					pkg.SetScriptReferenceVersionOption(script);
			}
			catch { }
			try { pkg.MarkReferenceVersionOptionsLoaded(); } catch { }
		}

		private static string SafeJsonString(SimpleJSON.JSONClass meta, string key)
		{
			if (meta == null || string.IsNullOrEmpty(key)) return null;
			SimpleJSON.JSONNode node = meta[key];
			return node != null ? node.Value : null;
		}

		public static bool ForceExactGlobally()
		{
			try
			{
				return Settings.Instance != null
					&& Settings.Instance.ForceExactPackageVersions != null
					&& Settings.Instance.ForceExactPackageVersions.Value;
			}
			catch { return false; }
		}

		public static bool RespectMetaOptions()
		{
			try
			{
				if (Settings.Instance == null || Settings.Instance.RespectPackageReferenceVersionOption == null)
					return true;
				return Settings.Instance.RespectPackageReferenceVersionOption.Value;
			}
			catch { return true; }
		}

		public static VarPackage.ReferenceVersionOption GetEffectiveOption(string entryPath)
		{
			if (ForceExactGlobally())
				return VarPackage.ReferenceVersionOption.Exact;

			if (!RespectMetaOptions())
				return VarPackage.ReferenceVersionOption.Latest;

			VarPackage referrer = TryGetReferrerPackage();
			if (referrer == null)
				return VarPackage.ReferenceVersionOption.Latest;

			// Prefer already-loaded options. Avoid zip I/O on warm NormalizeLoadPath —
			// SetActiveLoadReferrer preloads; if still cold, keep Latest default.
			if (!referrer.AreReferenceVersionOptionsLoaded)
				return VarPackage.ReferenceVersionOption.Latest;

			bool script = IsScriptEntryPath(entryPath);
			return script
				? referrer.ScriptReferenceVersionOption
				: referrer.StandardReferenceVersionOption;
		}

		public static VarPackage.ReferenceVersionOption GetSatisfactionOption(VarPackage consumer)
		{
			if (ForceExactGlobally())
				return VarPackage.ReferenceVersionOption.Exact;

			if (consumer == null || !RespectMetaOptions())
				return VarPackage.ReferenceVersionOption.Latest;

			try { consumer.TryEnsureReferenceVersionOptions(); } catch { }
			return consumer.StandardReferenceVersionOption;
		}

		public static VarPackage TryGetReferrerPackage()
		{
			string uid = PeekReferrerUid();
			if (string.IsNullOrEmpty(uid))
			{
				try { uid = FileManager.CurrentPackageUid; } catch { uid = null; }
			}
			if (string.IsNullOrEmpty(uid))
			{
				try { uid = FileManager.TopPackageUid; } catch { uid = null; }
			}
			if (string.IsNullOrEmpty(uid)) return null;
			try
			{
				return FileManager.GetPackage(uid, ensureInstalled: false);
			}
			catch { return null; }
		}

		public static bool IsScriptEntryPath(string entryPath)
		{
			if (string.IsNullOrEmpty(entryPath)) return false;
			// Avoid string.Replace alloc on warm path — accept either separator.
			if (entryPath.IndexOf(":/Custom/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			return entryPath.IndexOf(":\\Custom\\Scripts\\", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// When exact UID is missing, pick alternate UID per option. Null = do not rewrite.
		/// </summary>
		public static string ResolveMissingVersionUid(
			string group,
			int requestedVer,
			VarPackage.ReferenceVersionOption option)
		{
			if (string.IsNullOrEmpty(group) || requestedVer < 0) return null;

			VarPackageGroup pkgGroup = null;
			try { pkgGroup = FileManager.GetPackageGroup(group); } catch { }
			if (pkgGroup == null) return null;

			switch (option)
			{
				case VarPackage.ReferenceVersionOption.Exact:
					return null;

				case VarPackage.ReferenceVersionOption.Minimum:
				{
					VarPackage closest = pkgGroup.GetClosestMatchingPackageVersion(requestedVer, false, false);
					if (closest == null || string.IsNullOrEmpty(closest.Uid)) return null;
					if (closest.Version < requestedVer) return null;
					return closest.Uid;
				}

				case VarPackage.ReferenceVersionOption.Latest:
				default:
				{
					VarPackage newest = pkgGroup.NewestPackage;
					if (newest == null || string.IsNullOrEmpty(newest.Uid)) return null;
					if (newest.Version <= requestedVer) return null;
					return newest.Uid;
				}
			}
		}
	}
}
