using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VPB
{
	/// <summary>
	/// Builds a dependency graph for installed packages and exposes cached queries
	/// for transitive dependencies, transitive dependents, and missing dependency counts.
	/// </summary>
	public static class DependencyGraph
	{
		private sealed class Node
		{
			public VarPackage Package;
			public List<string> DirectDeps;

			public HashSet<VarPackage> TransitiveDeps;
			public HashSet<VarPackage> TransitiveDependents;

			public HashSet<string> MissingIds;
			public HashSet<string> MissingExactIds;
			public HashSet<string> MissingGroupIds;

			public bool Built;
		}

		private static readonly object _lock = new object();
		private static Dictionary<string, Node> _byUid = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
		private static bool _ready = false;

		private static readonly Regex DepExact = new Regex("^([^\\.]+\\.[^\\.]+)\\.([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
		private static readonly Regex DepLatest = new Regex("^([^\\.]+\\.[^\\.]+)\\.latest$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex DepMin = new Regex("^([^\\.]+\\.[^\\.]+)\\.min([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static void Rebuild()
		{
			try
			{
				var list = FileManager.GetPackages();
				var snapshot = (list != null) ? list.ToArray() : new VarPackage[0];

				var next = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < snapshot.Length; i++)
				{
					var pkg = snapshot[i];
					if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) continue;
					var direct = GetDirectDependencyIds(pkg);
					next[pkg.Uid] = new Node
					{
						Package = pkg,
						DirectDeps = direct,
						Built = false,
					};
				}

				foreach (var kvp in next)
				{
					BuildNode(kvp.Value, next);
				}

				// Build transitive dependents by inverting the transitive dependency sets.
				foreach (var kvp in next)
				{
					var n = kvp.Value;
					if (n.TransitiveDependents == null)
						n.TransitiveDependents = new HashSet<VarPackage>();
				}

				foreach (var kvp in next)
				{
					var src = kvp.Value;
					if (src.TransitiveDeps == null) continue;
					foreach (var dep in src.TransitiveDeps)
					{
						if (dep == null || string.IsNullOrEmpty(dep.Uid)) continue;
						Node target;
						if (next.TryGetValue(dep.Uid, out target))
						{
							target.TransitiveDependents.Add(src.Package);
						}
					}
				}

				// Publish counts on VarPackage for existing UI/logic.
				foreach (var kvp in next)
				{
					var n = kvp.Value;
					try { n.Package.DependentCount = n.TransitiveDependents != null ? n.TransitiveDependents.Count : 0; } catch { }
					try { n.Package.MissingDepsCount = n.MissingIds != null ? n.MissingIds.Count : 0; } catch { }
				}

				lock (_lock)
				{
					_byUid = next;
					_ready = true;
				}
			}
			catch (Exception ex)
			{
				LogUtil.LogWarning("[VPB] Dependency graph rebuild failed: " + ex.Message);
				lock (_lock) { _ready = true; }
			}
		}

		public static bool TryGetTransitiveDependencies(string packageUid, out HashSet<string> dependencyUids)
		{
			dependencyUids = null;
			if (string.IsNullOrEmpty(packageUid)) return false;
			lock (_lock)
			{
				if (!_ready) return false;
				Node n;
				if (!_byUid.TryGetValue(packageUid, out n) || n == null || n.TransitiveDeps == null) return false;
				dependencyUids = new HashSet<string>(n.TransitiveDeps.Where(p => p != null && !string.IsNullOrEmpty(p.Uid)).Select(p => p.Uid), StringComparer.OrdinalIgnoreCase);
				return true;
			}
		}

		public static bool TryGetTransitiveDependents(string packageUid, out HashSet<string> dependentUids)
		{
			dependentUids = null;
			if (string.IsNullOrEmpty(packageUid)) return false;
			lock (_lock)
			{
				if (!_ready) return false;
				Node n;
				if (!_byUid.TryGetValue(packageUid, out n) || n == null || n.TransitiveDependents == null) return false;
				dependentUids = new HashSet<string>(n.TransitiveDependents.Where(p => p != null && !string.IsNullOrEmpty(p.Uid)).Select(p => p.Uid), StringComparer.OrdinalIgnoreCase);
				return true;
			}
		}

		public static int GetMissingCount(string packageUid)
		{
			lock (_lock)
			{
				if (!_ready) return 0;
				Node n;
				if (!_byUid.TryGetValue(packageUid, out n) || n == null) return 0;
				return n.MissingIds != null ? n.MissingIds.Count : 0;
			}
		}

		public static int GetMissingExactCount(string packageUid)
		{
			lock (_lock)
			{
				if (!_ready) return 0;
				Node n;
				if (!_byUid.TryGetValue(packageUid, out n) || n == null) return 0;
				return n.MissingExactIds != null ? n.MissingExactIds.Count : 0;
			}
		}

		public static int GetMissingGroupCount(string packageUid)
		{
			lock (_lock)
			{
				if (!_ready) return 0;
				Node n;
				if (!_byUid.TryGetValue(packageUid, out n) || n == null) return 0;
				return n.MissingGroupIds != null ? n.MissingGroupIds.Count : 0;
			}
		}

		private static List<string> GetDirectDependencyIds(VarPackage pkg)
		{
			// Prefer the persisted SQLite dependency edges when available. This avoids requiring
			// per-package meta parsing just to answer dependency queries on startup.
			try
			{
				if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
				{
					var depsFromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					if (VpbLocalDatabase.TryReadRecursiveDependencyUids(pkg.Uid, depsFromSql))
						return depsFromSql.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				}
			}
			catch { }

			try
			{
				// Prefer a direct list if one exists; otherwise fall back to recursive list.
				var direct = pkg.PackageDependencies;
				if (direct != null && direct.Count > 0)
					return direct.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			}
			catch { }

			try
			{
				var deps = pkg.RecursivePackageDependencies;
				if (deps != null && deps.Count > 0)
					return deps.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			}
			catch { }

			return new List<string>();
		}

		private static void BuildNode(Node n, Dictionary<string, Node> byUid)
		{
			if (n == null) return;
			if (n.Built) return;

			// Use recursion with memoization; handle cycles via stack-local visited.
			var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			BuildNodeInternal(n, byUid, visiting);
		}

		private static void BuildNodeInternal(Node n, Dictionary<string, Node> byUid, HashSet<string> visiting)
		{
			if (n == null || n.Package == null || string.IsNullOrEmpty(n.Package.Uid)) return;
			if (n.Built) return;

			string uid = n.Package.Uid;
			if (!visiting.Add(uid))
			{
				// Cycle detected; stop expanding.
				n.TransitiveDeps = n.TransitiveDeps ?? new HashSet<VarPackage>();
				n.Built = true;
				return;
			}

			var depsSet = new HashSet<VarPackage>();
			var missingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var missingExactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var missingGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var direct = n.DirectDeps ?? new List<string>();
			for (int i = 0; i < direct.Count; i++)
			{
				string depId = direct[i];
				if (string.IsNullOrEmpty(depId)) continue;

				VarPackage resolved = FileManager.GetPackageForDependency(depId, false);
				if (resolved != null)
				{
					depsSet.Add(resolved);
					Node child;
					if (byUid.TryGetValue(resolved.Uid, out child) && child != null)
					{
						BuildNodeInternal(child, byUid, visiting);
						if (child.TransitiveDeps != null)
						{
							foreach (var t in child.TransitiveDeps)
								if (t != null) depsSet.Add(t);
						}
						// Merge transitive missing sets.
						if (child.MissingIds != null) missingIds.UnionWith(child.MissingIds);
						if (child.MissingExactIds != null) missingExactIds.UnionWith(child.MissingExactIds);
						if (child.MissingGroupIds != null) missingGroupIds.UnionWith(child.MissingGroupIds);
					}
				}
				else
				{
					CategorizeMissing(depId, out bool isExactMissing, out bool isGroupMissing);
					missingIds.Add(depId);
					if (isExactMissing) missingExactIds.Add(depId);
					if (isGroupMissing) missingGroupIds.Add(depId);
				}
			}

			n.TransitiveDeps = depsSet;
			n.MissingIds = missingIds;
			n.MissingExactIds = missingExactIds;
			n.MissingGroupIds = missingGroupIds;
			n.Built = true;
			visiting.Remove(uid);
		}

		private static void CategorizeMissing(string depId, out bool isExactMissing, out bool isGroupMissing)
		{
			isExactMissing = false;
			isGroupMissing = true;
			if (string.IsNullOrEmpty(depId)) return;

			try
			{
				Match m;
				if ((m = DepExact.Match(depId)).Success)
				{
					string group = m.Groups[1].Value;
					var pg = FileManager.GetPackageGroup(group);
					if (pg != null && pg.NewestPackage != null)
					{
						isExactMissing = true;
						isGroupMissing = false;
						return;
					}
					isGroupMissing = true;
					return;
				}
				if (DepLatest.IsMatch(depId) || DepMin.IsMatch(depId))
				{
					// Group constraint; if none installed it is group-missing.
					string group = null;
					if ((m = DepLatest.Match(depId)).Success) group = m.Groups[1].Value;
					else if ((m = DepMin.Match(depId)).Success) group = m.Groups[1].Value;
					if (!string.IsNullOrEmpty(group))
					{
						var pg = FileManager.GetPackageGroup(group);
						if (pg != null && pg.NewestPackage != null)
						{
							isGroupMissing = false;
							return;
						}
					}
					isGroupMissing = true;
				}
			}
			catch { }
		}
	}
}

