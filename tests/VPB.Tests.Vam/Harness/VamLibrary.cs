using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VPB.Tests
{
    public sealed class VamLibrary : IDisposable
    {
        private readonly TempInstall _install;
        private readonly object _previousByUid;
        private readonly object _previousByPath;
        private readonly object _previousGroups;
        private readonly object _previousEntriesByUid;
        private readonly object _previousEntriesByPath;
        private readonly DateTime _previousRefreshTime;

        private readonly Dictionary<string, VarPackage> _byUid =
            new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VarPackage> _byPath =
            new Dictionary<string, VarPackage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VarPackageGroup> _groups =
            new Dictionary<string, VarPackageGroup>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VarFileEntry> _entriesByUid =
            new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VarFileEntry> _entriesByPath =
            new Dictionary<string, VarFileEntry>(StringComparer.OrdinalIgnoreCase);

        public VamLibrary(TempInstall install)
        {
            _install = install;

            _previousByUid = GetRegistry("packagesByUid");
            _previousByPath = GetRegistry("packagesByPath");
            _previousGroups = GetRegistry("packageGroups");
            _previousEntriesByUid = GetRegistry("uidToVarFileEntry");
            _previousEntriesByPath = GetRegistry("pathToVarFileEntry");
            _previousRefreshTime = FileManager.lastPackageRefreshTime;

            SetRegistry("packagesByUid", _byUid);
            SetRegistry("packagesByPath", _byPath);
            SetRegistry("packageGroups", _groups);
            SetRegistry("uidToVarFileEntry", _entriesByUid);
            SetRegistry("pathToVarFileEntry", _entriesByPath);
            ClearAliasCaches();

            Touch();
        }

        public IDictionary<string, VarPackage> PackagesByUid { get { return _byUid; } }
        public IDictionary<string, VarPackageGroup> Groups { get { return _groups; } }

        public VarPackage Add(VarFixture fixture)
        {
            fixture.WriteTo(_install.AddonPackagesDir);
            VarPackage package = fixture.AsPackage();
            package.Scan();
            return Register(package);
        }

        public VarPackage Register(VarPackage package)
        {
            if (package == null) return null;

            _byUid[package.Uid] = package;
            _byPath[package.Path] = package;

            string groupId = FileManager.PackageIDToPackageGroupID(package.Uid);
            VarPackageGroup group;
            if (!_groups.TryGetValue(groupId, out group) || group == null)
            {
                group = new VarPackageGroup(groupId);
                _groups[groupId] = group;
            }
            group.AddPackage(package);
            IndexFileEntries(package);
            AddWhitespaceAliases(package.Uid, groupId);
            Touch();
            return package;
        }

        private void IndexFileEntries(VarPackage package)
        {
            List<VarFileEntry> entries = package.FileEntries;
            if (entries == null) return;

            foreach (VarFileEntry entry in entries)
            {
                if (entry == null) continue;
                if (!string.IsNullOrEmpty(entry.Uid)) _entriesByUid[entry.Uid] = entry;
                if (!string.IsNullOrEmpty(entry.Path)) _entriesByPath[entry.Path] = entry;
            }
        }

        public VarPackage AddScene(string creator, string name, int version, params string[] dependencies)
        {
            return Add(IndexFixture.SceneVar(creator, name, version, dependencies));
        }

        public VarPackage AddClothing(string creator, string name, int version)
        {
            return Add(IndexFixture.ClothingVar(creator, name, version));
        }

        public void Touch()
        {
            SetRefreshTime(DateTime.UtcNow);
        }

        private static void AddWhitespaceAliases(string uid, string groupId)
        {
            Invoke("s_WhitespaceUidAliases", uid);
            Invoke("s_WhitespaceGroupAliases", groupId);
        }

        private static void Invoke(string mapField, string actualId)
        {
            MethodInfo add = typeof(FileManager).GetMethod("AddWhitespaceAlias",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo map = typeof(FileManager).GetField(mapField, BindingFlags.NonPublic | BindingFlags.Static);
            if (add == null || map == null)
                throw new InvalidOperationException(
                    "FileManager.AddWhitespaceAlias / " + mapField + " no longer exists. VamLibrary calls the " +
                    "production alias builder rather than reimplementing the rule; update it here.");

            object[] args = { map.GetValue(null), actualId };
            add.Invoke(null, args);
            map.SetValue(null, args[0]);
        }

        private static void ClearAliasCaches()
        {
            SetStatic(typeof(FileManager), "s_WhitespaceUidAliases", null, optional: true);
            SetStatic(typeof(FileManager), "s_WhitespaceGroupAliases", null, optional: true);
        }

        public void Dispose()
        {
            SetRegistry("packagesByUid", _previousByUid);
            SetRegistry("packagesByPath", _previousByPath);
            SetRegistry("packageGroups", _previousGroups);
            SetRegistry("uidToVarFileEntry", _previousEntriesByUid);
            SetRegistry("pathToVarFileEntry", _previousEntriesByPath);
            ClearAliasCaches();
            SetRefreshTime(_previousRefreshTime);
        }

        private static void SetRefreshTime(DateTime value)
        {
            PropertyInfo p = typeof(FileManager).GetProperty("lastPackageRefreshTime",
                BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.GetSetMethod(true) != null)
            {
                try { p.GetSetMethod(true).Invoke(null, new object[] { value }); return; }
                catch { }
            }

            foreach (string name in new[] { "<lastPackageRefreshTime>k__BackingField", "_lastPackageRefreshTime" })
            {
                FieldInfo f = typeof(FileManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) continue;
                try { f.SetValue(null, value); return; } catch { }
            }

            throw new InvalidOperationException(
                "Could not stamp FileManager.lastPackageRefreshTime. The index rebuild refuses to publish " +
                "against an unstamped clock, so without this a library looks like it was never scanned.");
        }

        private static object GetRegistry(string field)
        {
            return Field(field).GetValue(null);
        }

        private static void SetRegistry(string field, object value)
        {
            Field(field).SetValue(null, value);
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo f = typeof(FileManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
                throw new InvalidOperationException(
                    "FileManager." + name + " no longer exists. VamLibrary swaps the package registry so " +
                    "FileManager lookups have data; without it NormalizePath falls through to Path.GetFullPath " +
                    "and rejects the uid:/internal form. Update VamLibrary.");
            return f;
        }

        private static void SetStatic(Type type, string name, object value, bool optional)
        {
            FieldInfo f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                if (optional) return;
                throw new InvalidOperationException(type.Name + "." + name + " no longer exists.");
            }
            try { f.SetValue(null, value); } catch { }
        }
    }
}
