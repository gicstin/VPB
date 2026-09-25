using VPB.src.util;
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

			// Do not use GetPackage(uid) default (ensureInstalled: true).
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
                if (Path.StartsWith("AllPackages", StringComparison.Ordinal))
                {
					string path="AddonPackages" + Path.Substring("AllPackages".Length);
					return File.Exists(path);
				}
				else if (Path.StartsWith("AddonPackages", StringComparison.Ordinal))
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
			if (VPBLogger.Verbose) LogUtil.Log("SetAutoInstall " + b+" "+Path);
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
				hidden = VpbHideIndex.IsLooseHidden(full);
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

				_hiddenCached = VpbHideIndex.SetLooseHidden(full, b) ? b : (bool?)null;
			}
			catch { _hiddenCached = null; }
		}

		/// <summary>Force next <see cref="IsHidden"/> to re-probe disk (after external hide-marker change).</summary>
		public void InvalidateHiddenCache()
		{
			_hiddenCached = null;
			try { VpbHideIndex.InvalidateLoose(FileManager.GetFullPath(Path)); }
			catch { }
		}

        public bool Install()
        {
            if (isVar)
            {
				string installPath = null;
				string repoPath = null;
				if (Path.StartsWith("AddonPackages/", StringComparison.Ordinal))
				{
					installPath = Path;
					repoPath = "AllPackages" + Path.Substring("AddonPackages".Length);
				}
				else if (Path.StartsWith("AllPackages/", StringComparison.Ordinal))
				{
					installPath = "AddonPackages" + Path.Substring("AllPackages".Length);
					repoPath = Path;
				}
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
            }
			return false;
        }
		public bool Uninstall()
        {
			if (isVar)
			{
				string installPath = null;
				string repoPath = null;
                if (Path.StartsWith("AddonPackages/", StringComparison.Ordinal))
                {
					installPath = Path;
					repoPath = "AllPackages" + Path.Substring("AddonPackages".Length);
				}
                else if(Path.StartsWith("AllPackages/", StringComparison.Ordinal))
                {
					installPath = "AddonPackages" + Path.Substring("AllPackages".Length);
					repoPath = Path;
				}

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
