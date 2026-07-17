using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VPB
{
	/// <summary>
	/// Lazy dependency graph: nodes built on demand from SQLite/bulk pkg_dep edges.
	/// No full-library rebuild at startup.
	/// </summary>
	public static class DependencyGraph
	{
		private sealed class Node
		{
			public VarPackage Package;
			public List<string> DirectDeps;
			public HashSet<VarPackage> TransitiveDeps;
			public HashSet<string> MissingIds;
			public HashSet<string> MissingExactIds;
			public HashSet<string> MissingGroupIds;
			public bool Built;
		}

		private static readonly object _lock = new object();
		private static Dictionary<string, Node> _byUid = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
		private static long _cacheScanBinary = long.MinValue;

		private static readonly Regex DepExact = new Regex("^([^\\.]+\\.[^\\.]+)\\.([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
		private static readonly Regex DepLatest = new Regex("^([^\\.]+\\.[^\\.]+)\\.latest$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex DepMin = new Regex("^([^\\.]+\\.[^\\.]+)\\.min([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static void OnPackageInventoryRefreshed()
		{
			lock (_lock)
			{
				long scanBin = GetCurrentScanBinary();
				if (scanBin == _cacheScanBinary && _byUid != null && _byUid.Count > 0)
					return;
				_cacheScanBinary = scanBin;
				_byUid = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
			}
		}

		/// <summary>Force graph drop without waiting for a full FileManager scan binary bump (Hub batch installs).</summary>
		public static void Invalidate()
		{
			lock (_lock)
			{
				_cacheScanBinary = long.MinValue;
				_byUid = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
			}
		}

		public static void Rebuild()
		{
			OnPackageInventoryRefreshed();
		}

		public static void EnsureForUids(IEnumerable<string> packageUids)
		{
			if (packageUids == null) return;
			lock (_lock)
			{
				EnsureCacheGeneration();
				var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (string uid in packageUids)
				{
					if (string.IsNullOrEmpty(uid)) continue;
					Node n = GetOrCreateNodeLocked(uid);
					if (n != null)
						BuildNodeLocked(n, visiting);
				}
			}
		}

		public static void EnsureForPackage(string packageUid)
		{
			if (string.IsNullOrEmpty(packageUid)) return;
			EnsureForUids(new[] { packageUid });
		}

		public static bool TryGetTransitiveDependencies(string packageUid, out HashSet<string> dependencyUids)
		{
			dependencyUids = null;
			if (string.IsNullOrEmpty(packageUid)) return false;
			lock (_lock)
			{
				EnsureCacheGeneration();
				Node n = GetOrCreateNodeLocked(packageUid);
				if (n == null) return false;
				var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				BuildNodeLocked(n, visiting);
				if (n.TransitiveDeps == null || n.TransitiveDeps.Count == 0) return false;
				dependencyUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (VarPackage p in n.TransitiveDeps)
				{
					if (p != null && !string.IsNullOrEmpty(p.Uid))
						dependencyUids.Add(p.Uid);
				}
				return dependencyUids.Count > 0;
			}
		}

		public static bool TryGetTransitiveDependents(string packageUid, out HashSet<string> dependentUids)
		{
			dependentUids = null;
			if (string.IsNullOrEmpty(packageUid)) return false;

			string targetShort = GetPackageGroupShortUid(packageUid);
			try
			{
				var fromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (VpbLocalDatabase.TryReadDependentUids(packageUid, targetShort, fromSql) && fromSql.Count > 0)
				{
					dependentUids = fromSql;
					return true;
				}
			}
			catch { }

			if (TryGetDependentsFromBulkEdges(packageUid, targetShort, out dependentUids) && dependentUids != null && dependentUids.Count > 0)
				return true;

			return false;
		}

		public static int GetMissingCount(string packageUid)
		{
			lock (_lock)
			{
				EnsureCacheGeneration();
				Node n = GetOrCreateNodeLocked(packageUid);
				if (n == null) return 0;
				var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				BuildNodeLocked(n, visiting);
				int count = n.MissingIds != null ? n.MissingIds.Count : 0;
				try
				{
					if (n.Package != null)
						n.Package.MissingDepsCount = count;
				}
				catch { }
				return count;
			}
		}

		public static int GetMissingExactCount(string packageUid)
		{
			lock (_lock)
			{
				EnsureCacheGeneration();
				Node n = GetOrCreateNodeLocked(packageUid);
				if (n == null) return 0;
				var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				BuildNodeLocked(n, visiting);
				return n.MissingExactIds != null ? n.MissingExactIds.Count : 0;
			}
		}

		public static int GetMissingGroupCount(string packageUid)
		{
			lock (_lock)
			{
				EnsureCacheGeneration();
				Node n = GetOrCreateNodeLocked(packageUid);
				if (n == null) return 0;
				var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				BuildNodeLocked(n, visiting);
				return n.MissingGroupIds != null ? n.MissingGroupIds.Count : 0;
			}
		}

		static void EnsureCacheGeneration()
		{
			long scanBin = GetCurrentScanBinary();
			if (scanBin != _cacheScanBinary)
				OnPackageInventoryRefreshed();
		}

		static long GetCurrentScanBinary()
		{
			try { return FileManager.lastPackageRefreshTime.ToBinary(); }
			catch { return 0; }
		}

		static Node GetOrCreateNodeLocked(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return null;
			Node n;
			if (_byUid.TryGetValue(uid, out n) && n != null)
				return n;

			VarPackage pkg = null;
			try { pkg = FileManager.GetPackage(uid, false); } catch { }
			if (pkg == null)
			{
				try { pkg = FileManager.GetPackageForDependency(uid, false); } catch { }
			}
			if (pkg == null || string.IsNullOrEmpty(pkg.Uid))
				return null;

			n = new Node
			{
				Package = pkg,
				DirectDeps = GetDirectDependencyIds(pkg),
				Built = false,
			};
			_byUid[pkg.Uid] = n;
			return n;
		}

		static void BuildNodeLocked(Node n, HashSet<string> visiting)
		{
			if (n == null || n.Built) return;
			BuildNodeInternal(n, visiting);
		}

		static void BuildNodeInternal(Node n, HashSet<string> visiting)
		{
			if (n == null || n.Package == null || string.IsNullOrEmpty(n.Package.Uid)) return;
			if (n.Built) return;

			string uid = n.Package.Uid;
			if (!visiting.Add(uid))
			{
				n.TransitiveDeps = n.TransitiveDeps ?? new HashSet<VarPackage>();
				n.Built = true;
				return;
			}

			var depsSet = new HashSet<VarPackage>();
			var missingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var missingExactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var missingGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			List<string> direct = n.DirectDeps ?? new List<string>();
			for (int i = 0; i < direct.Count; i++)
			{
				string depId = direct[i];
				if (string.IsNullOrEmpty(depId)) continue;

				VarPackage resolved = FileManager.GetPackageForDependency(depId, false);
				// Exact meta pin satisfied by newer installed group version (Hub download-latest).
				if (resolved == null && FileManager.IsDependencySatisfiedByInstalled(depId))
				{
					string group = FileManager.PackageIDToPackageGroupID(depId);
					if (!string.IsNullOrEmpty(group))
					{
						try { resolved = FileManager.GetPackage(group + ".latest", false); }
						catch { resolved = null; }
					}
				}
				if (resolved != null)
				{
					depsSet.Add(resolved);
					Node child = GetOrCreateNodeLocked(resolved.Uid);
					if (child != null)
					{
						BuildNodeInternal(child, visiting);
						if (child.TransitiveDeps != null)
						{
							foreach (VarPackage t in child.TransitiveDeps)
								if (t != null) depsSet.Add(t);
						}
						if (child.MissingIds != null) missingIds.UnionWith(child.MissingIds);
						if (child.MissingExactIds != null) missingExactIds.UnionWith(child.MissingExactIds);
						if (child.MissingGroupIds != null) missingGroupIds.UnionWith(child.MissingGroupIds);
					}
				}
				else
				{
					bool isExactMissing;
					bool isGroupMissing;
					CategorizeMissing(depId, out isExactMissing, out isGroupMissing);
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

			try { n.Package.MissingDepsCount = missingIds.Count; } catch { }
		}

		static bool TryGetDependentsFromBulkEdges(string targetUid, string targetShort, out HashSet<string> dependentUids)
		{
			dependentUids = null;
			if (string.IsNullOrEmpty(targetUid)) return false;
			if (!VpbLocalDatabase.TryEnsurePackageDependencyBulkCache())
				return false;

			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool any = false;
			try
			{
				VpbLocalDatabase.TryIterateBulkPackageDependencies((srcUid, depUid) =>
				{
					if (string.IsNullOrEmpty(srcUid) || string.IsNullOrEmpty(depUid)) return;
					if (!DepRefersToTarget(depUid, targetUid, targetShort)) return;
					if (result.Add(srcUid))
						any = true;
				});
			}
			catch { return false; }

			if (!any) return false;
			dependentUids = result;
			return true;
		}

		static string GetPackageGroupShortUid(string uid)
		{
			if (string.IsNullOrEmpty(uid)) return null;
			try
			{
				int firstDot = uid.IndexOf('.');
				if (firstDot < 0) return null;
				int secondDot = uid.IndexOf('.', firstDot + 1);
				if (secondDot < 0) return null;
				return uid.Substring(0, secondDot);
			}
			catch { return null; }
		}

		static bool DepRefersToTarget(string depUidOrPath, string targetUid, string targetShort)
		{
			if (string.IsNullOrEmpty(depUidOrPath) || string.IsNullOrEmpty(targetUid)) return false;
			try
			{
				string d = depUidOrPath.Replace('\\', '/');
				int lastSlash = d.LastIndexOf('/');
				if (lastSlash >= 0 && lastSlash + 1 < d.Length) d = d.Substring(lastSlash + 1);
				if (d.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
					d = d.Substring(0, d.Length - 4);

				if (string.Equals(d, targetUid, StringComparison.OrdinalIgnoreCase)) return true;
				if (string.IsNullOrEmpty(targetShort)) return false;

				if (d.Length > targetShort.Length + 1 &&
					d.StartsWith(targetShort, StringComparison.OrdinalIgnoreCase) &&
					d[targetShort.Length] == '.')
				{
					return true;
				}
			}
			catch { }
			return false;
		}

		private static List<string> GetDirectDependencyIds(VarPackage pkg)
		{
			try
			{
				if (pkg != null && !string.IsNullOrEmpty(pkg.Uid))
				{
					List<string> fromBulk;
					if (VpbLocalDatabase.TryGetBulkDirectDependencyUids(pkg.Uid, out fromBulk))
						return fromBulk;
					var depsFromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					if (VpbLocalDatabase.TryReadRecursiveDependencyUids(pkg.Uid, depsFromSql))
						return depsFromSql.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				}
			}
			catch { }

			try
			{
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
