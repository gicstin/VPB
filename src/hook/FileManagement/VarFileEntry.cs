using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VPB
{
	[System.Serializable]
	public class SerializableNames
	{
		public string[] names;
	}

	public class VarFileEntry : FileEntry
	{
		private VarPackage _packageStore;

		public VarPackage Package
		{
			get { EnsurePackageResolved(); return _packageStore; }
			protected set { _packageStore = value; }
		}

		/// <summary>When set, <see cref="Package"/> is resolved on first use via <see cref="FileManager.TryResolveVarPackageForIndexedGalleryRow"/>.</summary>
		private string _deferredPackageUid;
		private string _deferredVarPathHint;

		/// <summary>Package <see cref="VarPackage.CreationTime"/> from SQLite (<c>pkg.pctime</c>); avoids resolving <see cref="Package"/> for DateCreated sort.</summary>
		private long _galleryIndexedCreationTicks = long.MinValue;

		/// <summary>Per-uid <c>pkg.first_scanned</c> from SQLite; avoids resolving <see cref="Package"/> for DateAdded/DateUpdated sort.</summary>
		private long _galleryIndexedFirstScannedTicks = long.MinValue;

		/// <summary>NTFS creation time from SQLite <c>pkg.pctime</c>; bounds package New/Updated dates.</summary>
		private long _galleryIndexedFileCreationTicks = long.MinValue;

		/// <summary>When set (History grid), exact <c>item_usage.item_key</c> for deletes (matches usage tracking keys).</summary>
		private string _galleryItemUsageKey;

		public string GalleryItemUsageKey => _galleryItemUsageKey;

		public string InternalPath { get; protected set; }

		public long EntrySize
		{
			get { return base.Size; }
		}

		public override long Size
		{
			get
			{
				// Do not resolve the package for sorting/listing — use indexed size until something needs a live package.
				if (_deferredPackageUid != null)
					return base.Size;
				return _packageStore != null ? _packageStore.Size : base.Size;
			}
			protected set { base.Size = value; }
		}

		public VarFileEntry(VarPackage vp, string entryName, DateTime lastWriteTime, long size, bool simulated)
			: this(vp, entryName, lastWriteTime, size, null, simulated)
		{
		}

		/// <summary>Gallery path string from SQLite index (avoids per-row string concat when set).</summary>
		public VarFileEntry(VarPackage vp, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath)
			: this(vp, entryName, lastWriteTime, size, indexedGalleryPath, false)
		{
		}

		public VarFileEntry(VarPackage vp, string entryName, DateTime lastWriteTime, long size)
			: this(vp, entryName, lastWriteTime, size, null, false)
		{
		}

		/// <summary>
		/// Gallery fast path: listing fields come from SQLite; <see cref="Package"/> is resolved lazily when the file is opened or other package state is needed.
		/// </summary>
		public VarFileEntry(string packageUid, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath, string indexedVarPathHint)
			: this(packageUid, entryName, lastWriteTime, size, indexedGalleryPath, indexedVarPathHint, long.MinValue)
		{
		}

		public VarFileEntry(string packageUid, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath, string indexedVarPathHint, long packageCreationTicksOrMin, string galleryItemUsageKey = null)
		{
			if (string.IsNullOrEmpty(packageUid))
				throw new ArgumentException("packageUid must not be null or empty.", "packageUid");
			_deferredPackageUid = packageUid;
			_deferredVarPathHint = indexedVarPathHint ?? "";
			_galleryIndexedCreationTicks = packageCreationTicksOrMin;
			_galleryItemUsageKey = galleryItemUsageKey;
			Package = null;
			InternalPath = entryName ?? "";
			Uid = packageUid + ":/" + InternalPath;
			Path = indexedGalleryPath ?? "";
			int lastSlash = Path.LastIndexOf('/');
			Name = (lastSlash >= 0 && lastSlash + 1 < Path.Length) ? Path.Substring(lastSlash + 1) : Path;
			Exists = true;
			LastWriteTime = lastWriteTime;
			base.Size = size;
		}

		/// <summary>Gallery fast path with first_scanned for DateAdded/DateUpdated sort. Delegates to the standard ctor then sets the scan timestamp.</summary>
		public VarFileEntry(string packageUid, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath, string indexedVarPathHint, long packageCreationTicksOrMin, long firstScannedTicksOrMin, string galleryItemUsageKey = null)
			: this(packageUid, entryName, lastWriteTime, size, indexedGalleryPath, indexedVarPathHint, packageCreationTicksOrMin, galleryItemUsageKey)
		{
			_galleryIndexedFirstScannedTicks = firstScannedTicksOrMin;
		}

		public VarFileEntry(string packageUid, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath, string indexedVarPathHint, long packageCreationTicksOrMin, long firstScannedTicksOrMin, long packageFileCreationTicksOrMin, string galleryItemUsageKey = null)
			: this(packageUid, entryName, lastWriteTime, size, indexedGalleryPath, indexedVarPathHint, packageCreationTicksOrMin, firstScannedTicksOrMin, galleryItemUsageKey)
		{
			_galleryIndexedFileCreationTicks = packageFileCreationTicksOrMin;
		}

		/// <summary>Package UID for this row (deferred or resolved), for matching scoped path refresh.</summary>
		internal string GetRowPackageUid()
		{
			if (_deferredPackageUid != null) return _deferredPackageUid;
			if (_packageStore != null && !string.IsNullOrEmpty(_packageStore.Uid)) return _packageStore.Uid;
			string u = Uid ?? "";
			int idx = u.IndexOf(":/", StringComparison.Ordinal);
			if (idx > 0) return u.Substring(0, idx);
			return u;
		}

		internal bool TryGetGalleryIndexedPackageCreationTime(out DateTime dt)
		{
			dt = DateTime.MinValue;
			if (_galleryIndexedCreationTicks == long.MinValue) return false;
			try
			{
				dt = DateTime.FromBinary(_galleryIndexedCreationTicks);
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal bool TryGetGalleryIndexedFirstScanned(out DateTime dt)
		{
			dt = DateTime.MinValue;
			if (_galleryIndexedFirstScannedTicks == long.MinValue || _galleryIndexedFirstScannedTicks == 0L) return false;
			try
			{
				dt = DateTime.FromBinary(_galleryIndexedFirstScannedTicks);
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal bool TryGetGalleryIndexedFileCreationTime(out DateTime dt)
		{
			dt = DateTime.MinValue;
			if (_galleryIndexedFileCreationTicks == long.MinValue || _galleryIndexedFileCreationTicks == 0L) return false;
			try
			{
				dt = DateTime.FromBinary(_galleryIndexedFileCreationTicks);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private void EnsurePackageResolved()
		{
			if (_packageStore != null || _deferredPackageUid == null) return;
			string uid = _deferredPackageUid;
			string hint = _deferredVarPathHint ?? "";
			VarPackage p;
			if (FileManager.TryResolveVarPackageForIndexedGalleryRow(uid, hint, out p))
			{
				_deferredPackageUid = null;
				_packageStore = p;
				RefreshDisplayPathsFromPackage();
				Exists = true;
			}
			else
			{
				Exists = false;
			}
		}

		private VarFileEntry(VarPackage vp, string entryName, DateTime lastWriteTime, long size, string indexedGalleryPath, bool simulated)
		{
			Package = vp;
			InternalPath = entryName;
			Uid = vp.Uid + ":/" + InternalPath;
			if (!string.IsNullOrEmpty(indexedGalleryPath))
			{
				Path = indexedGalleryPath;
			}
			else if (string.Equals(InternalPath, "meta.json", System.StringComparison.OrdinalIgnoreCase))
			{
				Path = vp.Path;
			}
			else
			{
				Path = vp.Path + ":/" + InternalPath;
			}
			int lastSlash = Path.LastIndexOf('/');
			Name = (lastSlash >= 0 && lastSlash + 1 < Path.Length) ? Path.Substring(lastSlash + 1) : Path;
			Exists = true;
			LastWriteTime = lastWriteTime;
			base.Size = size;
		}

		public override FileEntryStream OpenStream()
		{
			EnsurePackageResolved();
			if (_packageStore == null)
				throw new IOException("VAR package not found for " + (Uid ?? ""));
			return new VarFileEntryStream(this);
		}

		public List<string> ClothingTags;// = new List<string>();
		public List<string> HairTags;// = new List<string>();

		public override FileEntryStreamReader OpenStreamReader()
		{
			EnsurePackageResolved();
			if (_packageStore == null)
				throw new IOException("VAR package not found for " + (Uid ?? ""));
			return new VarFileEntryStreamReader(this);
		}

		public override bool HasFlagFile(string flagName)
		{
			if (string.IsNullOrEmpty(flagName)) return false;
			if (string.Equals(flagName, "hide", StringComparison.OrdinalIgnoreCase))
				return VpbHideIndex.IsItemHiddenByEntryUid(Uid);
			string p = VpbHideIndex.BuildVarEntryFlagPath(GetRowPackageUid(), InternalPath, flagName);
			if (string.IsNullOrEmpty(p)) return false;
			try { return File.Exists(p); }
			catch { return false; }
		}

		public override void SetFlagFile(string flagName, bool b)
		{
			if (string.IsNullOrEmpty(flagName)) return;
			if (string.Equals(flagName, "hide", StringComparison.OrdinalIgnoreCase))
			{
				VpbHideIndex.SetItemHidden(GetRowPackageUid(), InternalPath, b);
				return;
			}
			string p = VpbHideIndex.BuildVarEntryFlagPath(GetRowPackageUid(), InternalPath, flagName);
			if (string.IsNullOrEmpty(p)) return;
			try
			{
				if (b)
				{
					if (File.Exists(p)) return;
					string dir = System.IO.Path.GetDirectoryName(p);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
					File.WriteAllText(p, string.Empty);
				}
				else if (File.Exists(p))
				{
					File.Delete(p);
				}
			}
			catch { }
		}

		public bool IsFlagFileModifiable(string flagName)
		{
			return !string.IsNullOrEmpty(flagName) && !string.IsNullOrEmpty(InternalPath);
		}

		public override bool IsHidden()
		{
			return VpbHideIndex.IsItemHiddenByEntryUid(Uid);
		}

		public override void SetHidden(bool b)
		{
			VpbHideIndex.SetItemHidden(GetRowPackageUid(), InternalPath, b);
		}

		public bool IsHiddenModifiable()
		{
			return !string.IsNullOrEmpty(InternalPath);
		}


		public override bool IsInstalled()
		{
			EnsurePackageResolved();
			if (_packageStore == null) return false;
			if(_packageStore.Path.StartsWith("AddonPackages/"))
            {
				return File.Exists(_packageStore.Path);
            }
			else if (_packageStore.Path.StartsWith("AllPackages/"))
            {
				return File.Exists("AddonPackages" + _packageStore.Path.Substring("AllPackages".Length));
            }
			return false;
		}
		public override bool IsAutoInstall()
		{
			EnsurePackageResolved();
			if (_packageStore == null) return false;
			string key = _packageStore.Uid;

			if (AutoInstallLookup.Contains(key))
				return true;
			return false;
		}

        public override bool SetAutoInstall(bool b)
        {
			EnsurePackageResolved();
			if (_packageStore == null) return false;
			string key = _packageStore.Uid;
			SetAutoInstallInternal(key, b);

			// Do not call InstallSelf() here: moving AllPackages → AddonPackages is deferred to
			// VamHookPlugin.TryAutoInstall() on the next process launch (faster UI; one batch I/O).
			return false;
		}

		/// <summary>After <see cref="VarPackage.InstallSelf"/> / recursive install moves the .var on disk, sync Path/Uid/Name.</summary>
		public void RefreshDisplayPathsFromPackage()
		{
			if (_packageStore == null) return;
			if (string.Equals(InternalPath, "meta.json", StringComparison.OrdinalIgnoreCase))
				Path = _packageStore.Path;
			else
				Path = _packageStore.Path + ":/" + InternalPath;
			Uid = _packageStore.Uid + ":/" + InternalPath;
			int lastSlash = Path.LastIndexOf('/');
			Name = (lastSlash >= 0 && lastSlash + 1 < Path.Length) ? Path.Substring(lastSlash + 1) : Path;
			InvalidateUidLowerInvariantCache();
		}

		/// <summary>
		/// Sync <see cref="FileEntry.Path"/> from <see cref="FileManager"/> after a path-only move (AllPackages ↔ AddonPackages).
		/// Resolves deferred SQLite-backed rows when possible so <see cref="RefreshDisplayPathsFromPackage"/> can run.
		/// </summary>
		public bool TryRefreshPathsFromLivePackage()
		{
			if (_packageStore != null)
			{
				RefreshDisplayPathsFromPackage();
				return true;
			}
			if (_deferredPackageUid == null) return false;
			string uid = _deferredPackageUid;
			string hint = _deferredVarPathHint ?? "";
			VarPackage p;
			if (!FileManager.TryResolveVarPackageForIndexedGalleryRow(uid, hint, out p) || p == null)
				return false;
			_packageStore = p;
			_deferredPackageUid = null;
			_deferredVarPathHint = null;
			RefreshDisplayPathsFromPackage();
			Exists = true;
			return true;
		}

	}

}
