using System;

namespace VPB
{
    /// <summary>
    /// Lightweight list entry representing a VarPackage (not an internal file).
    /// Used by dependency filtering when we want to show packages regardless of category ext/path filters.
    /// </summary>
    public class PackageListEntry : FileEntry
    {
        private VarPackage _packageStore;

        /// <summary>When set, <see cref="Package"/> is resolved on first use via <see cref="FileManager.TryResolveVarPackageForIndexedGalleryRow"/>.</summary>
        private string _deferredPackageUid;
        private string _deferredVarPathHint;

        /// <summary>Per-uid <c>pkg.first_scanned</c> from SQLite; avoids resolving <see cref="Package"/> for DateAdded/DateUpdated sort.</summary>
        private long _galleryIndexedFirstScannedTicks = long.MinValue;

        public VarPackage Package
        {
            get { EnsurePackageResolved(); return _packageStore; }
            private set { _packageStore = value; }
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

        public PackageListEntry(VarPackage pkg)
        {
            _deferredPackageUid = null;
            _deferredVarPathHint = null;
            Package = pkg;
            if (pkg != null)
            {
                // Ratings are keyed by FileEntry.Uid. For packages on disk that is the package path
                // (e.g. AddonPackages/Foo.Bar.1.var), not the VarPackage.Uid. Use pkg.Path when available.
                Path = pkg.Path;
                Uid = !string.IsNullOrEmpty(Path) ? Path : pkg.Uid;
                Name = pkg.Uid + ".var";
                Exists = true;
                LastWriteTime = pkg.LastWriteTime;
                base.Size = pkg.Size;
                _galleryIndexedFirstScannedTicks = pkg.FirstScannedBinary;
            }
            else
            {
                Uid = "";
                Path = "";
                Name = "Missing Package";
                Exists = false;
                LastWriteTime = DateTime.MinValue;
                base.Size = 0;
            }
        }

        /// <summary>
        /// SQLite fast path: listing fields come from the local index; <see cref="Package"/> is resolved lazily when package state is needed.
        /// </summary>
        public PackageListEntry(string packageUid, string indexedVarPathHint, DateTime lastWriteTime, long size, long packageCreationTicksOrMin)
            : this(packageUid, indexedVarPathHint, lastWriteTime, size, packageCreationTicksOrMin, long.MinValue)
        {
        }

        /// <summary>SQLite fast path with first_scanned for DateAdded/DateUpdated sort.</summary>
        public PackageListEntry(string packageUid, string indexedVarPathHint, DateTime lastWriteTime, long size, long packageCreationTicksOrMin, long firstScannedTicksOrMin)
        {
            if (string.IsNullOrEmpty(packageUid))
                throw new ArgumentException("packageUid must not be null or empty.", "packageUid");
            _deferredPackageUid = packageUid;
            _deferredVarPathHint = indexedVarPathHint ?? "";
            _galleryIndexedFirstScannedTicks = firstScannedTicksOrMin;
            Package = null;

            // Ratings are keyed by FileEntry.Uid. For packages on disk that is the package path.
            // Use the indexed var path hint when available; else fall back to the package UID.
            Path = _deferredVarPathHint.Replace('\\', '/');
            Uid = !string.IsNullOrEmpty(Path) ? Path : packageUid;
            Name = packageUid + ".var";
            Exists = true;
            LastWriteTime = lastWriteTime;
            base.Size = size;
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
                RefreshPathsFromPackage();
                Exists = true;
            }
            else
            {
                Exists = false;
            }
        }

        public override FileEntryStream OpenStream()
        {
            // Package list entries are not real file entries and are never meant to be opened as streams.
            return null;
        }

        public override FileEntryStreamReader OpenStreamReader()
        {
            // Package list entries are not real file entries and are never meant to be opened as readers.
            return null;
        }

        /// <summary>Package UID for <c>gallery_item_user_tag</c>; valid before lazy <see cref="Package"/> resolve (ALL VAR / varpkg list).</summary>
        public string GetPackageUidForGalleryUserTags()
        {
            if (_packageStore != null) return _packageStore.Uid ?? "";
            return _deferredPackageUid ?? "";
        }

        public override bool IsAutoInstall()
        {
            var p = Package;
            if (p == null) return false;
            try { return AutoInstallLookup != null && AutoInstallLookup.Contains(p.Uid); }
            catch { return false; }
        }

        /// <summary>After the backing <see cref="VarPackage"/> path changes (install/uninstall), sync list row fields.</summary>
        public void RefreshPathsFromPackage()
        {
            if (_packageStore == null) return;
            Path = string.IsNullOrEmpty(_packageStore.Path) ? "" : _packageStore.Path.Replace('\\', '/');
            Uid = !string.IsNullOrEmpty(Path) ? Path : _packageStore.Uid;
            Name = _packageStore.Uid + ".var";
            LastWriteTime = _packageStore.LastWriteTime;
            base.Size = _packageStore.Size;
            InvalidateUidLowerInvariantCache();
        }
    }

    public class MissingPackageListEntry : FileEntry
    {
        public string RequestedUid { get; private set; }

        public MissingPackageListEntry(string requestedUid)
        {
            RequestedUid = requestedUid ?? "";
            Uid = RequestedUid;
            Path = "[MISSING] " + RequestedUid;
            Name = RequestedUid + ".var";
            Exists = false;
            LastWriteTime = DateTime.MinValue;
            Size = 0;
        }

        public override FileEntryStream OpenStream()
        {
            return null;
        }

        public override FileEntryStreamReader OpenStreamReader()
        {
            return null;
        }
    }
}

