using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;

namespace VPB
{
	public class SystemFileEntry : FileEntry
	{
		public bool isVar = false;
		public VarPackage package;

		/// <summary>Cached <see cref="IsHidden"/> result for loose files (scroll/badge hot path).</summary>
		private bool? _hiddenCached;
		private static string s_cachedGameRootFull;

		/// <summary>
		/// Fast-path constructor when caller already knows file stat. Avoids per-entry FileInfo calls
		/// (used by SQLite-cached loose-file listings).
		/// </summary>
		public SystemFileEntry(string path, DateTime lastWriteTime, long size, bool exists)
			: base(path)
		{
			Exists = exists;
			LastWriteTime = lastWriteTime;
			Size = size;

			// Do not use GetPackage(uid) default (ensureInstalled: true): that runs InstallRecursive()
			package = FileManager.GetPackage(System.IO.Path.GetFileNameWithoutExtension(Path), false);
			if (package != null) isVar = true;
		}

		public SystemFileEntry(string path)
			: base(path)
		{
			Exists = File.Exists(Path);
			if (Exists)
			{
				DateTime creationTime;
				DateTime lastWriteTime;
				long size;
				if (FileStat.TryGetFileStat(Path, out creationTime, out lastWriteTime, out size))
				{
					LastWriteTime = lastWriteTime;
					Size = size;
				}
				else
				{
					FileInfo fileInfo = new FileInfo(Path);
					LastWriteTime = fileInfo.LastWriteTime;
					Size = fileInfo.Length;
				}
			}

			// Do not use GetPackage(uid) default (ensureInstalled: true): that runs InstallRecursive()
			// as a side effect of merely representing an on-disk .var (e.g. gallery Autoinstall path).
			package = FileManager.GetPackage(System.IO.Path.GetFileNameWithoutExtension(Path), false);
            if (package != null)
            {
				isVar = true;
            }
			
        }

		public override FileEntryStream OpenStream()
		{
			return new SystemFileEntryStream(this);
		}

		public override FileEntryStreamReader OpenStreamReader()
		{
			return new SystemFileEntryStreamReader(this);
		}
        public override bool IsInstalled()
        {
            if (isVar)
            {
                if (Path.StartsWith("AllPackages"))
                {
					string path="AddonPackages" + Path.Substring("AllPackages".Length);
					return File.Exists(path);
				}
				else if (Path.StartsWith("AddonPackages"))
                {
					return File.Exists(Path);
                }
			}
			return false;
		}
		public override bool IsAutoInstall()
		{
			if (isVar)
			{
				string key = System.IO.Path.GetFileNameWithoutExtension(Path);
				return AutoInstallLookup.Contains(key);
			}
			if (LocalSceneGallerySupport.TryGetLocalSceneAutoInstallLookupKey(this, out string sceneKey))
				return AutoInstallLookup.Contains(sceneKey);
			string basenameKey = System.IO.Path.GetFileNameWithoutExtension(Path);
			return AutoInstallLookup.Contains(basenameKey);
		}
		public override bool SetAutoInstall(bool b)
        {
			LogUtil.Log("SetAutoInstall " + b+" "+Path);
			if (isVar)
            {
				string key = System.IO.Path.GetFileNameWithoutExtension(Path);
				SetAutoInstallInternal(key, b);
				// Install() deferred to TryAutoInstall() on next launch (see VarFileEntry.SetAutoInstall).
            }
            else if (LocalSceneGallerySupport.TryGetLocalSceneAutoInstallLookupKey(this, out string sceneKey))
            {
				SetAutoInstallInternal(sceneKey, b);
            }
			return false;
		}

		public override bool IsHidden()
		{
			if (isVar) return false;
			if (_hiddenCached.HasValue) return _hiddenCached.Value;
			bool hidden = false;
			try
			{
				// VaM "system file" hide marker: adjacent "<file>.hide"
				string full = FileManager.GetFullPath(Path);
				string root = s_cachedGameRootFull;
				if (string.IsNullOrEmpty(root))
				{
					root = FileManager.GetFullPath(Directory.GetCurrentDirectory());
					s_cachedGameRootFull = root;
				}
				if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(root))
				{
					_hiddenCached = false;
					return false;
				}
				string rootTrim = root.TrimEnd('\\', '/');
				string rootPrefix = rootTrim + System.IO.Path.DirectorySeparatorChar;
				if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(full.TrimEnd('\\', '/'), rootTrim, StringComparison.OrdinalIgnoreCase))
				{
					_hiddenCached = false;
					return false;
				}
				hidden = File.Exists(full + ".hide");
			}
			catch { hidden = false; }
			_hiddenCached = hidden;
			return hidden;
		}

		public override void SetHidden(bool b)
		{
			if (isVar) return;
			try
			{
				string full = FileManager.GetFullPath(Path);
				string root = s_cachedGameRootFull;
				if (string.IsNullOrEmpty(root))
				{
					root = FileManager.GetFullPath(Directory.GetCurrentDirectory());
					s_cachedGameRootFull = root;
				}
				if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(root)) return;
				string rootPrefix = root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
				if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
					return;

				string hidePath = full + ".hide";
				if (b)
				{
					if (File.Exists(hidePath))
					{
						_hiddenCached = true;
						return;
					}
					string dir = System.IO.Path.GetDirectoryName(hidePath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
					File.WriteAllText(hidePath, string.Empty);
					_hiddenCached = File.Exists(hidePath);
				}
				else
				{
					if (!File.Exists(hidePath))
					{
						_hiddenCached = false;
						return;
					}
					File.Delete(hidePath);
					_hiddenCached = false;
				}
			}
			catch { _hiddenCached = null; }
		}

		/// <summary>Force next <see cref="IsHidden"/> to re-probe disk (after external hide-marker change).</summary>
		public void InvalidateHiddenCache()
		{
			_hiddenCached = null;
		}

        public bool Install()
        {
            if (isVar)
            {
				string installPath = null;
				string repoPath = null;
				if (Path.StartsWith("AddonPackages/"))
				{
					installPath = Path;
					repoPath = "AllPackages" + Path.Substring("AddonPackages".Length);
				}
				else if (Path.StartsWith("AllPackages/"))
				{
					installPath = "AddonPackages" + Path.Substring("AllPackages".Length);
					repoPath = Path;
				}
				// Uninstall
				if (File.Exists(repoPath))
				{
					if (!File.Exists(installPath))
					{
						string dir = System.IO.Path.GetDirectoryName(installPath);
						if (!Directory.Exists(dir))
							Directory.CreateDirectory(dir);

						File.Move(repoPath, installPath);
						return true;
					}
					else
					{
						LogUtil.Log(installPath + " uninstall failed because there is a file with same name in AllPackages");
					}
				}
			}
            else
            {
				// This is a var package
            }
			return false;
        }
		public bool Uninstall()
        {
			if (isVar)
			{
				string installPath = null;
				string repoPath = null;
                if (Path.StartsWith("AddonPackages/"))
                {
					installPath = Path;
					repoPath = "AllPackages" + Path.Substring("AddonPackages".Length);
				}
                else if(Path.StartsWith("AllPackages/"))
                {
					installPath = "AddonPackages" + Path.Substring("AllPackages".Length);
					repoPath = Path;
				}

                // Uninstall
                if (File.Exists(installPath))
                {
                    if (!File.Exists(repoPath))
                    {
                        string dir = System.IO.Path.GetDirectoryName(repoPath);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.Move(installPath, repoPath);
						return true;
                    }
                    else
                    {
						LogUtil.Log(installPath + " uninstall failed because there is a file with same name in AllPackages");
                    }
                }
            }
				return false;
		}

		/// <summary>
		/// Re-read the on-disk last-write time and size for this loose file. Used after an
		/// overwrite-save so Date Updated/modified sorts reflect the new timestamp without a
		/// full gallery rescan (issue #45).
		/// </summary>
		public void RefreshLastWriteTimeFromDisk()
		{
			try
			{
				Exists = File.Exists(Path);
				if (!Exists) return;
				DateTime creationTime;
				DateTime lastWriteTime;
				long size;
				if (FileStat.TryGetFileStat(Path, out creationTime, out lastWriteTime, out size))
				{
					LastWriteTime = lastWriteTime;
					Size = size;
				}
				else
				{
					FileInfo fileInfo = new FileInfo(Path);
					LastWriteTime = fileInfo.LastWriteTime;
					Size = fileInfo.Length;
				}
			}
			catch { }
		}

		/// <summary>Sync Path after <see cref="VarPackage"/> moved the .var (e.g. InstallSelf / UninstallSelf).</summary>
		public void RefreshVarDisplayPathFromPackage()
		{
			if (!isVar || package == null) return;
			string p = package.Path;
			if (string.IsNullOrEmpty(p)) return;
			Path = p.Replace('\\', '/');
			Uid = Path;
			Name = System.IO.Path.GetFileName(Path) ?? Path;
			InvalidateUidLowerInvariantCache();
		}
	}

}
