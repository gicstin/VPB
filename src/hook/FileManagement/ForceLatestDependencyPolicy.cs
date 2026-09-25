using System;
using System.Collections.Generic;

namespace VPB
{
	internal enum DependencyVersionKind
	{
		Exact,
		Minimum,
		Latest
	}

	internal struct DependencyVersionRequest
	{
		public string Group;
		public DependencyVersionKind Kind;
		public int Version;
	}

	internal static class ForceLatestDependencyPolicy
	{
		const int MaxVersionDigits = 9;

		public static bool TryParse(string dependencyId, out DependencyVersionRequest request)
		{
			request = new DependencyVersionRequest();
			if (string.IsNullOrEmpty(dependencyId)) return false;
			string id = dependencyId.Trim();
			if (id.IndexOf(':') >= 0 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) return false;

			int lastDot = id.LastIndexOf('.');
			if (lastDot <= 0 || lastDot >= id.Length - 1) return false;

			string group = id.Substring(0, lastDot);
			if (!IsGroupId(group)) return false;

			string suffix = id.Substring(lastDot + 1);
			int version;
			if (string.Equals(suffix, "latest", StringComparison.OrdinalIgnoreCase))
			{
				request.Group = group;
				request.Kind = DependencyVersionKind.Latest;
				request.Version = -1;
				return true;
			}
			if (suffix.Length > 3
				&& suffix.StartsWith("min", StringComparison.OrdinalIgnoreCase)
				&& TryParseDigits(suffix.Substring(3), out version))
			{
				request.Group = group;
				request.Kind = DependencyVersionKind.Minimum;
				request.Version = version;
				return true;
			}
			if (TryParseDigits(suffix, out version))
			{
				request.Group = group;
				request.Kind = DependencyVersionKind.Exact;
				request.Version = version;
				return true;
			}
			return false;
		}

		public static bool IsGroupId(string group)
		{
			if (string.IsNullOrEmpty(group)) return false;
			if (group.IndexOf(':') >= 0 || group.IndexOf('/') >= 0 || group.IndexOf('\\') >= 0) return false;
			int dot = group.IndexOf('.');
			if (dot <= 0 || dot >= group.Length - 1) return false;
			if (group.IndexOf('.', dot + 1) >= 0) return false;
			if (group.Trim().Length != group.Length) return false;
			return true;
		}

		public static bool AppliesToGroup(
			string group,
			bool forceAll,
			bool listMode,
			ICollection<string> forcedGroups,
			ICollection<string> excludedGroups,
			bool forceExact)
		{
			if (string.IsNullOrEmpty(group) || forceExact) return false;
			if (excludedGroups != null && excludedGroups.Contains(group)) return false;
			if (forceAll) return true;
			return listMode && forcedGroups != null && forcedGroups.Contains(group);
		}

		public static bool ShouldUpgrade(DependencyVersionRequest request, int newestInstalledVersion)
		{
			if (newestInstalledVersion < 0) return false;
			switch (request.Kind)
			{
				case DependencyVersionKind.Exact:
					return newestInstalledVersion > request.Version;
				case DependencyVersionKind.Minimum:
					return newestInstalledVersion >= request.Version;
				default:
					return false;
			}
		}

		public static bool IsSameGroup(string group, string packageUidOrPath)
		{
			if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(packageUidOrPath)) return false;
			string uid = PackageReferenceVersionResolver.TryExtractPackageUid(packageUidOrPath) ?? packageUidOrPath;
			DependencyVersionRequest other;
			if (!TryParse(uid, out other)) return false;
			return string.Equals(group, other.Group, StringComparison.OrdinalIgnoreCase);
		}

		public static string NormalizeExclusionEntry(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return null;
			string s = raw.Trim();
			if (s.Length == 0) return null;

			string uid = PackageReferenceVersionResolver.TryExtractPackageUid(s);
			if (!string.IsNullOrEmpty(uid)) s = uid.Trim();

			DependencyVersionRequest request;
			if (TryParse(s, out request)) return request.Group;
			return IsGroupId(s) ? s : null;
		}

		public static bool TryParseVersionOfUid(string uid, out int version)
		{
			version = -1;
			DependencyVersionRequest request;
			if (!TryParse(uid, out request) || request.Kind != DependencyVersionKind.Exact) return false;
			version = request.Version;
			return true;
		}

		public static string NormalizeEntryInternalPath(string entryPathAfterUid)
		{
			if (string.IsNullOrEmpty(entryPathAfterUid)) return "";
			string p = entryPathAfterUid.Replace('\\', '/');
			int start = 0;
			while (start < p.Length && (p[start] == ':' || p[start] == '/')) start++;
			return start >= p.Length ? "" : p.Substring(start);
		}

		static bool TryParseDigits(string s, out int value)
		{
			value = 0;
			if (string.IsNullOrEmpty(s) || s.Length > MaxVersionDigits) return false;
			int v = 0;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c < '0' || c > '9') return false;
				v = v * 10 + (c - '0');
			}
			value = v;
			return true;
		}
	}
}
