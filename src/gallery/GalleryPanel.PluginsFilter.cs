using System;
using System.Collections.Generic;
using VpbDb = VPB.VpbLocalDatabase;

namespace VPB
{
    public partial class GalleryPanel
    {
        private HashSet<string> GetCslistReferencedPaths()
        {
            var local = _cslistReferencedPaths;
            if (local != null) return local;

            lock (_cslistReferencedLock)
            {
                if (_cslistReferencedPaths != null) return _cslistReferencedPaths;

                // Single SQLite connection + one SELECT pulls every disk + per-VAR cslist-ref row.
                // Earlier per-uid loop opened a connection per package (each runs EnsureSchema's
                // PRAGMA/CREATE chain), which spiked first-load to seconds on large libraries.
                var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { VpbDb.TryReadAllCslistReferencedFromCache(combined); } catch { }

                _cslistReferencedPaths = combined;
                return combined;
            }
        }

        // Mtime alone suffices: a new VAR version is a different uid, so any mtime change on
        // this uid means the same file was modified or replaced.
        internal static string PerVarSig(VarPackage pkg)
        {
            if (pkg == null) return "0";
            try { return pkg.LastWriteTime.ToBinary().ToString(); } catch { return "0"; }
        }

        private bool IsCsReferencedByAnyCslist(FileEntry entry)
        {
            if (entry == null) return false;
            string p = entry.Path;
            if (string.IsNullOrEmpty(p)) return false;
            if (!p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;

            // VAR entries: writer stored Uid form (author.pkg.1:/custom/scripts/foo.cs).
            // Loose disk: writer stored relative path (custom/scripts/foo.cs). Use Path for those.
            string norm = (entry is VarFileEntry)
                ? (entry.UidLowerInvariant ?? string.Empty)
                : p.Replace('\\', '/').ToLowerInvariant();
            var set = GetCslistReferencedPaths();
            return set.Contains(norm);
        }

        public void InvalidateCslistReferencedCache()
        {
            lock (_cslistReferencedLock)
            {
                _cslistReferencedPaths = null;
            }
        }
    }
}
