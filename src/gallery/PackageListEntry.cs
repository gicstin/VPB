using System;

namespace VPB
{
    /// <summary>
    /// Lightweight list entry representing a VarPackage (not an internal file).
    /// Used by dependency filtering when we want to show packages regardless of category ext/path filters.
    /// </summary>
    public class PackageListEntry : FileEntry
    {
        public VarPackage Package { get; private set; }

        public override long Size
        {
            get { return Package != null ? Package.Size : base.Size; }
            protected set { base.Size = value; }
        }

        public PackageListEntry(VarPackage pkg)
        {
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
    }

    public class MissingPackageListEntry : FileEntry
    {
        public string RequestedUid { get; private set; }

        public MissingPackageListEntry(string requestedUid)
        {
            RequestedUid = requestedUid ?? "";
            Uid = RequestedUid;
            Path = "";
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

