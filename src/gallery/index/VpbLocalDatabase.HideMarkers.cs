using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
	internal static partial class VpbLocalDatabase
	{
		internal const string HideScopePkg = "pkg";
		internal const string HideScopeItem = "item";

		private const string HideFanoutCollapseMetaKey = "hide_fanout_collapse_v1";

		internal static void EnsureHideMarkerSchema(VpbSqlite3.Connection conn)
		{
			if (conn == null) return;
			conn.ExecUtf8(
				"CREATE TABLE IF NOT EXISTS hide_marker (scope TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, PRIMARY KEY(scope, pkg_uid, internal_path));" +
				"CREATE INDEX IF NOT EXISTS idx_hm_pkg ON hide_marker(pkg_uid);" +
				"CREATE INDEX IF NOT EXISTS idx_hm_item ON hide_marker(pkg_uid, internal_path);");
		}

		internal static bool TryReplaceAllHideMarkers(HashSet<string> hiddenPkgUids, HashSet<string> hiddenItemKeys)
		{
			if (!VpbSqlite3.IsAvailable) return false;
			try
			{
				using (var conn = new VpbSqlite3.Connection(DbPath))
				{
					EnsureHideMarkerSchema(conn);
					conn.ExecUtf8("BEGIN IMMEDIATE;");
					try
					{
						conn.ExecUtf8("DELETE FROM hide_marker;");
						using (var ins = conn.Prepare("INSERT OR REPLACE INTO hide_marker(scope, pkg_uid, internal_path) VALUES(?,?,?)"))
						{
							if (hiddenPkgUids != null)
							{
								foreach (string uid in hiddenPkgUids)
								{
									if (string.IsNullOrEmpty(uid)) continue;
									ins.BindText(1, HideScopePkg);
									ins.BindText(2, uid);
									ins.BindText(3, "");
									ins.Step();
									ins.Reset();
								}
							}
							if (hiddenItemKeys != null)
							{
								foreach (string key in hiddenItemKeys)
								{
									string uid, internalPath;
									if (!TrySplitHideItemKey(key, out uid, out internalPath)) continue;
									ins.BindText(1, HideScopeItem);
									ins.BindText(2, uid);
									ins.BindText(3, internalPath);
									ins.Step();
									ins.Reset();
								}
							}
						}
						conn.ExecUtf8("COMMIT;");
						return true;
					}
					catch
					{
						try { conn.ExecUtf8("ROLLBACK;"); } catch { }
						throw;
					}
				}
			}
			catch (Exception ex)
			{
				try { LogUtil.LogWarning("[VPB.DB] hide_marker mirror write failed: " + ex.Message); } catch { }
				return false;
			}
		}

		internal static bool TrySetHideMarker(string scope, string pkgUid, string internalPath, bool present)
		{
			if (!VpbSqlite3.IsAvailable) return false;
			if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(pkgUid)) return false;
			try
			{
				using (var conn = new VpbSqlite3.Connection(DbPath))
				{
					EnsureHideMarkerSchema(conn);
					string sql = present
						? "INSERT OR REPLACE INTO hide_marker(scope, pkg_uid, internal_path) VALUES(?,?,?)"
						: "DELETE FROM hide_marker WHERE scope=? AND pkg_uid=? AND internal_path=?";
					using (var st = conn.Prepare(sql))
					{
						st.BindText(1, scope);
						st.BindText(2, pkgUid);
						st.BindText(3, internalPath ?? "");
						st.Step();
					}
					return true;
				}
			}
			catch (Exception ex)
			{
				try { LogUtil.LogWarning("[VPB.DB] hide_marker update failed: " + ex.Message); } catch { }
				return false;
			}
		}

		private static bool TrySplitHideItemKey(string key, out string pkgUid, out string internalPath)
		{
			pkgUid = null;
			internalPath = null;
			if (string.IsNullOrEmpty(key)) return false;
			int sep = key.IndexOf(":/", StringComparison.Ordinal);
			if (sep <= 0 || sep + 2 >= key.Length) return false;
			pkgUid = key.Substring(0, sep);
			internalPath = key.Substring(sep + 2);
			return true;
		}

		internal static void AppendGalleryHideExclusionSql(StringBuilder sb, string catMemAlias)
		{
			if (sb == null) return;
			string a = string.IsNullOrEmpty(catMemAlias) ? "m" : catMemAlias;
			sb.Append(" AND NOT EXISTS (SELECT 1 FROM hide_marker hp WHERE hp.scope='").Append(HideScopePkg)
			  .Append("' AND hp.pkg_uid=").Append(a).Append(".pkg_uid)");
			sb.Append(" AND NOT EXISTS (SELECT 1 FROM hide_marker hi WHERE hi.scope='").Append(HideScopeItem)
			  .Append("' AND hi.pkg_uid=").Append(a).Append(".pkg_uid AND hi.internal_path=").Append(a).Append(".internal_path)");
		}

		internal static bool IsHideFanoutCollapseDone()
		{
			if (!VpbSqlite3.IsAvailable) return false;
			try
			{
				using (var conn = new VpbSqlite3.Connection(DbPath))
				{
					EnsureHideMarkerSchema(conn);
					return !string.IsNullOrEmpty(MetaGet(conn, HideFanoutCollapseMetaKey));
				}
			}
			catch { return false; }
		}

		internal static void MarkHideFanoutCollapseDone()
		{
			if (!VpbSqlite3.IsAvailable) return;
			try
			{
				using (var conn = new VpbSqlite3.Connection(DbPath))
				{
					EnsureHideMarkerSchema(conn);
					MetaSet(conn, HideFanoutCollapseMetaKey, "1");
				}
			}
			catch { }
		}
	}
}
